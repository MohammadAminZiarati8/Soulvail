using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// <b>Ledger row 1, measured rather than argued:</b> how many swings of the Censer a Husk takes at
/// stages 1, 15 and 30, under a stated path through M3-12c's tree plus CH §5.2's Overflow, against
/// GD §12.4's band of three to five.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every number here is read off shipped code, never off the spec's table</b> — which is the one
/// thing that makes this fixture worth writing. The Husk's hit points at a depth come from
/// <see cref="DepthScaling"/> applied to the shipped 36, with <see cref="Scalings.Design"/>'s curves
/// (identical to <c>Descent.asset</c>'s, field for field), so a retune of <c>h(n)</c> moves these
/// rows. Overflow's per-level damage comes from <see cref="LevelUpFlow.OverflowDamage"/> rather than
/// from the 2 % anybody typed. <b>The spec's table says 66 and 99 hit points; the shipped curve says
/// 66.24 and 98.64</b>, and a row that hard-coded the former would be asserting the prose. The hits
/// still land 3 / 4 / 5 either way, which is the table being right for a reason it did not state.
/// </para>
/// <para>
/// <b>Why a fixture rather than a <c>RunSession</c>.</b> Hits-to-kill is damage per swing against
/// maximum hit points and nothing else — no clock, no wave, no targeting. Driving a whole session to
/// stage 30 would add forty minutes of simulated time and a spawn director to a division, and every
/// one of those is pinned by its own fixture already. What is measured here is the arithmetic the
/// player feels, at the three depths GD §12.4 names.
/// </para>
/// <para>
/// <b>The path is stated and it is narrow: Keen Censer and Overflow, and nothing else.</b> Six of
/// the twelve nodes are excluded on purpose and
/// <see cref="Ttk_ExcludesSurvivabilityAndReach"/> is the row that proves the exclusion is real
/// rather than an omission — Broad Censure and Long Reach reach further and hit more, Crashing
/// Censure buys time, Zealotry swings faster, Consecrate and Bulwark keep the player alive, and not
/// one of the six raises damage per hit. <b>A test that quietly counted any of them would pass while
/// the tree did nothing</b>, which is the trap M3-12b named when it shipped the last two primitives.
/// </para>
/// <para>
/// <b>The node's numbers are written out, the way <c>DepthScalingTests</c> writes the curves out.</b>
/// <c>Soulvail.Tests.Core</c> cannot reach <c>AssetDatabase</c> (M0-10), so it cannot read
/// <c>KeenCenser.asset</c>; that the asset carries +15 % <c>PercentAdd</c> on <c>WeaponDamage</c> is
/// <c>OathboundTreeTests.StatNodes_CarryTheirNumbers</c>' claim, in the assembly that can open the
/// file. The two fixtures meet at the number, and a retune that moved one and not the other reddens
/// this file.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TimeToKillTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    /// <summary>CC §7's Censer, as <c>Oathbound.asset</c> ships it.</summary>
    private const float WeaponDamage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float WeaponRange = 8f;
    private const float ConeAngle = 60f;

    /// <summary>GD §8.1's Husk, as <c>Husk.asset</c> ships it, before any depth.</summary>
    private const float HuskMaxHp = 36f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;

    /// <summary><c>KeenCenser.asset</c>'s +15 %, pooled additively under ADR-0008's order.</summary>
    private const float KeenCenser = 0.15f;

    /// <summary>GD §12.4's band, in swings of the basic attack.</summary>
    private const int BandLow = 3;
    private const int BandHigh = 5;

    private const int EnemyCapacity = 8;

    private const float Tolerance = 1e-3f;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rule 8: the band at the three depths -----------------------------------------------------

    [Test]
    public void Ttk_StageOne_IsThreeHits()
    {
        // The opening fight, with nothing taken and nothing overflowed: the number the game has
        // shipped since M1 and the low end of GD §12.4's band.
        Assert.That(HitsToKill(stage: 1, keenCenser: false, overflowLevels: 0), Is.EqualTo(3));
    }

    [Test]
    public void Ttk_StageFifteen_IsWithinTheBand()
    {
        // The median path at stage 15: the tree filled around stage 9, so twelve nodes are taken and
        // seven levels have overflowed past them (rule 8's table).
        int hits = HitsToKill(stage: 15, keenCenser: true, overflowLevels: 7);

        Assert.That(hits, Is.EqualTo(4));
        Assert.That(hits, Is.InRange(BandLow, BandHigh), "GD §12.4's three to five.");
    }

    [Test]
    public void Ttk_StageThirty_IsWithinTheBand()
    {
        // **Ledger row 1's headline.** Twenty-nine uncapped Overflow levels against twelve nodes, so
        // the reason the band holds this deep is CH §5.2 rather than the tree — which is the warning
        // rule 8 attaches to its own table, and what M7-04's full twenty-seven will shift back.
        int hits = HitsToKill(stage: 30, keenCenser: true, overflowLevels: 29);

        Assert.That(hits, Is.EqualTo(5));
        Assert.That(hits, Is.InRange(BandLow, BandHigh), "GD §12.4's three to five.");
    }

    [Test]
    public void Ttk_UnlevelledStageThirtyStillFails()
    {
        // **Row 1's original finding, kept as the control.** Without the tree and without Overflow a
        // stage-30 Husk takes eight swings — well outside the band — so the three rows above are
        // measuring something that genuinely moved rather than a band that was never breached.
        int hits = HitsToKill(stage: 30, keenCenser: false, overflowLevels: 0);

        Assert.That(hits, Is.EqualTo(8));
        Assert.That(hits, Is.GreaterThan(BandHigh), "The state M2-15 left row 1 in.");
    }

    // ---- Rule 9: what the measurement deliberately cannot see -------------------------------------

    [Test]
    public void Ttk_ExcludesSurvivabilityAndReach()
    {
        // **The row that makes the exclusion a claim rather than an omission.** Six of the twelve
        // nodes are left out of every path above, and this is why: not one of them can move damage
        // per hit, so a fixture that applied them would report the same number while implying the
        // tree had done more.
        IEffect[] excluded =
        {
            // Long Reach — further away, not harder.
            new ModifyStat(PlayerStat.WeaponRange, ModifierKind.Flat, 1.5f),

            // Broad Censure — more targets in one swing, not more damage to any of them.
            new ModifyStat(PlayerStat.WeaponConeAngle, ModifierKind.PercentAdd, 0.5f),

            // Zealotry — more swings a second, which is DPS. Row 1 is stated in hits (rule 8).
            new ModifyStat(PlayerStat.FireRate, ModifierKind.PercentAdd, 0.12f),

            // Crashing Censure — buys time (M3-12b rule 9).
            new KnockbackOnSwing(1.5f),

            // Bulwark and Consecrate — survivability (M3-11a rule 11, M3-11b rule 12), and neither
            // is a ModifyStat at all, so neither has an address that could reach a damage number.
            new GrantShield(35f, 5f),
            new SpawnHealZone(3.5f, 6f, 3f, 0.5f),
        };

        foreach (IEffect effect in excluded)
        {
            Assert.That(
                effect is ModifyStat modify && modify.Stat == PlayerStat.WeaponDamage,
                Is.False,
                "Nothing on the excluded list may address WeaponDamage, or the exclusion would be "
                    + "hiding damage rather than declining to count reach.");
        }

        // And the mechanical half: applied for real, they move neither the damage nor the count.
        World world = NewWorld();

        float before = world.Combat.Weapon.Damage.Value;
        int hitsBefore = Hits(ScaledHuskHp(stage: 15), before);

        var node = new object();

        for (int i = 0; i < excluded.Length; i++)
        {
            if (excluded[i] is ModifyStat stat)
            {
                world.Effects.Apply(stat, node);
            }
        }

        // The knockback is not a PlayerStat — it is a rule the swing gains — so it goes on the stat
        // its own handler writes, which is the closest this fixture can come to taking the node.
        world.Combat.SwingKnockback.Add(new Modifier(ModifierKind.Flat, 1.5f, new object()));

        Assert.That(
            world.Combat.Weapon.Damage.Value,
            Is.EqualTo(before).Within(Tolerance),
            "Six nodes taken and the Censer hits for exactly what it hit for.");

        Assert.That(
            Hits(ScaledHuskHp(stage: 15), world.Combat.Weapon.Damage.Value),
            Is.EqualTo(hitsBefore));

        // Sanity, so the row cannot pass by having applied nothing at all.
        Assert.That(world.Combat.Weapon.Range.Value, Is.EqualTo(WeaponRange + 1.5f).Within(Tolerance));
        Assert.That(world.Combat.Weapon.ConeAngleDeg.Value, Is.EqualTo(90f).Within(Tolerance));
        Assert.That(world.Combat.SwingKnockback.Value, Is.EqualTo(1.5f).Within(Tolerance));
    }

    // ---- Rule 8: the pooling the table depends on -------------------------------------------------

    [Test]
    public void Ttk_OverflowPoolsWithTheNode()
    {
        World world = NewWorld();

        ApplyKeenCenser(world);
        ApplyOverflow(world, 29);

        // **×1.73, not 1.15 × 1.58 = 1.817.** Both the node and Overflow are PercentAdd, and ADR-0008
        // pools that position into one sum before multiplying — so thirty modifiers are one bracket.
        // The two answers differ by more than a whole point of damage and by no hits at all at this
        // depth, which is exactly why the row asserts the number rather than the count.
        Assert.That(
            world.Combat.Weapon.Damage.Value,
            Is.EqualTo(WeaponDamage * 1.73f).Within(Tolerance),
            "13 × (1 + 0.15 + 29 × 0.02).");

        Assert.That(
            world.Combat.Weapon.Damage.Value,
            Is.Not.EqualTo(WeaponDamage * 1.15f * 1.58f).Within(0.5f),
            "Multiplied rather than pooled, the same stack would read 23.62 — a different game.");
    }

    [Test]
    public void Ttk_OverflowPerLevelIsTheShippedConstant()
    {
        // The table's "+2 % damage per level" is read off M3-08a's code rather than out of CH §5.2,
        // because a retune there has to redden this file rather than pass silently through it.
        Assert.That(LevelUpFlow.OverflowDamage, Is.EqualTo(0.02f).Within(1e-7f));

        World world = NewWorld();
        ApplyOverflow(world, 7);

        Assert.That(
            world.Combat.Weapon.Damage.Value,
            Is.EqualTo(WeaponDamage * (1f + (7f * LevelUpFlow.OverflowDamage))).Within(Tolerance),
            "Seven levels, pooled: 13 × 1.14.");
    }

    // ---- Rule 8: the hit points are the curve's, not the table's ----------------------------------

    [Test]
    public void Ttk_HuskHpIsTheShippedCurveRatherThanTheSpecTable()
    {
        // **The correction the spec's own table needs.** h(n) = min(1 + 0.06(n − 1), 4) against the
        // Husk's 36, so stage 15 is 66.24 and stage 30 is 98.64 — not the round 66 and 99 the table
        // quotes. Asserted here so that the three band rows are known to be dividing by the shipped
        // curve, and so a change to DepthScaling arrives as a failure with a number on it.
        Assert.That(ScaledHuskHp(1), Is.EqualTo(36f).Within(Tolerance), "h(1) = 1.00.");
        Assert.That(ScaledHuskHp(15), Is.EqualTo(66.24f).Within(Tolerance), "h(15) = 1.84.");
        Assert.That(ScaledHuskHp(30), Is.EqualTo(98.64f).Within(Tolerance), "h(30) = 2.74.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// The band's arithmetic in one place: a Husk scaled to <paramref name="stage"/>, divided by a
    /// Censer carrying the stated path.
    /// </summary>
    private int HitsToKill(int stage, bool keenCenser, int overflowLevels)
    {
        World world = NewWorld();

        if (keenCenser)
        {
            ApplyKeenCenser(world);
        }

        ApplyOverflow(world, overflowLevels);

        return Hits(ScaledHuskHp(stage), world.Combat.Weapon.Damage.Value);
    }

    /// <summary>Whole swings, because a Husk at one hit point is still standing.</summary>
    private static int Hits(float hp, float damagePerHit) =>
        (int)System.Math.Ceiling(hp / damagePerHit);

    /// <summary>
    /// A Husk's maximum hit points at <paramref name="stage"/>, produced by the shipped
    /// <see cref="DepthScaling"/> rather than by multiplying h(n) out here.
    /// </summary>
    /// <remarks>
    /// Through a real <see cref="EnemyRegistry"/> spawn, so the agent has been wiped and re-based by
    /// <c>EnemyAgent.Initialise</c> before the depth goes on — which is the order a live run uses and
    /// the one that makes the number mean what the player meets (ledger row 2).
    /// </remarks>
    private static float ScaledHuskHp(int stage)
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        EnemyAgent husk = registry.Spawn(Husk(), new Vector3(0f, 0f, 3f));

        new DepthScaling(Scalings.Design()).Apply(husk, stage);

        return husk.Health.MaxHp.Value;
    }

    /// <summary>
    /// Keen Censer, applied as the authored effect through the registry the run uses — never as a
    /// bare modifier — so the pooling under test is the shipped path.
    /// </summary>
    private static void ApplyKeenCenser(World world)
    {
        var node = new object();

        world.Effects.Apply(
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, KeenCenser),
            node);
    }

    /// <summary>
    /// <paramref name="levels"/> Overflow levels, one <see cref="ModifyStat"/> each under one
    /// source — <c>LevelUpFlow.GrantOne</c>'s own shape, with its own constant.
    /// </summary>
    private static void ApplyOverflow(World world, int levels)
    {
        var overflow = new object();

        var effect = new ModifyStat(
            PlayerStat.WeaponDamage, ModifierKind.PercentAdd, LevelUpFlow.OverflowDamage);

        for (int i = 0; i < levels; i++)
        {
            world.Effects.Apply(effect, overflow);
        }
    }

    private World NewWorld()
    {
        CharacterSpec spec = Oathbound();

        var combat = new PlayerCombat(spec, _events, _intents, EnemyCapacity);
        var motor = new PlayerMotor(spec.Movement, Vector3.UnitZ);
        var progression = new LevelTracker(Scalings.Xp(), _events);

        var effects = new EffectRegistry();
        effects.Register(new ModifyStatHandler(new PlayerStats(combat, motor, progression)));

        return new World(combat, effects);
    }

    /// <summary>The three objects a row needs, so no row assembles them itself.</summary>
    private readonly struct World
    {
        internal World(PlayerCombat combat, EffectRegistry effects)
        {
            Combat = combat;
            Effects = effects;
        }

        internal PlayerCombat Combat { get; }

        internal EffectRegistry Effects { get; }
    }

    /// <summary>
    /// CC §7's Oathbound, with the Focus ramp switched off — <c>StatReachTests</c>' fixture, for its
    /// reason: a ramp would move the swing cadence, and this file divides by damage per swing.
    /// </summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, SwingsPerSecond, WeaponRange, ConeAngle, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(ShieldMax, 4f, 15f),
        0.5f);

    /// <summary>GD §8.1's Husk, Static so that it stands where it was put.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        2f,
        1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);
}
