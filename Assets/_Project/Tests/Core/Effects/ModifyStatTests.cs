using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// The address table and the first primitive: that every <c>PlayerStat</c> resolves to the live
/// instance it names, that a <c>ModifyStat</c> is exactly one <c>Modifier</c>, and that applying
/// one moves the number the rest of the game reads.
/// </summary>
/// <remarks>
/// <para>
/// Against real objects throughout — a real <c>PlayerCombat</c>, a real <c>PlayerMotor</c>, a real
/// <c>LevelTracker</c> — because the claim being tested is that the address table reaches the
/// numbers the game actually plays with. A fake stat would prove the handler calls <c>Stat.Add</c>,
/// which <c>StatTests</c> already owns, and nothing about whether <c>MaxHp</c> is the max HP.
/// </para>
/// <para>
/// The Oathbound's numbers where a row needs one (CC §7): 140 HP, a 13-damage Censer. Two rows
/// override a number and say which and why.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ModifyStatTests
{
    private const string OathboundId = "character.oathbound";

    /// <summary>CC §7's Censer: 13 damage a swing, which is what the percentage rows are over.</summary>
    private const float WeaponDamage = 13f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;

    /// <summary>The owner's retuned move speed (CC §2.5), and the one the motor row is over.</summary>
    private const float MoveSpeed = 3f;

    private const int EnemyCapacity = 8;

    private const float Tolerance = 1e-3f;

    private SilentEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new SilentEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rule 4: the address table ---------------------------------------------------------------

    [Test]
    public void Stats_ResolveEveryMember()
    {
        // **Against an Emberwright since M6-08** (rule 8): it is the one class with a Kindling and a
        // Blink, and here it carries RS-03b's volley as well, so it is the one run that answers all
        // twenty but ContactDamage. The Oathbound's four refusals are
        // Stats_HasIsFalseForAClassWithoutTheObject's; the volley's three are asserted at the end.
        PlayerCombat combat = Ember(withVolley: true);
        PlayerMotor motor = Motor();
        LevelTracker progression = Progression();

        var stats = new PlayerStats(combat, motor, progression);
        var answered = 0;

        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            // The one member the player does not have, and the only named exception this row
            // carries (M4-01a rule 1). PlayerStat became the address space *every* combatant is
            // addressed in, so it holds one number no PlayerStats can answer — and the refusal is
            // asserted here rather than waved past, because "not in the switch" and "deliberately
            // not the player's" must not look the same from this row.
            if (member == PlayerStat.ContactDamage)
            {
                Assert.That(
                    () => stats.Resolve(member),
                    Throws.TypeOf<ArgumentOutOfRangeException>(),
                    "A player has no contact damage. Only CombatantStats answers this address.");

                Assert.That(stats.Has(member), Is.False, "Has must agree with the refusal.");

                continue;
            }

            // Written out rather than derived, so that a member added to the enum without a
            // resolver line fails here — and a member added with one but no expectation fails on
            // the row below, which is the half a Resolve-shaped test would miss.
            Stat expected = member switch
            {
                PlayerStat.MaxHp => combat.Health.MaxHp,
                PlayerStat.WeaponDamage => combat.Weapon.Damage,
                PlayerStat.FireRate => combat.Weapon.FireRate,
                PlayerStat.MoveSpeed => motor.Speed,
                PlayerStat.MovementSkillCooldown => combat.Charge.Cooldown,
                PlayerStat.XpGain => progression.XpGain,

                // M3-12a's five. Each names the very instance its owner holds, which is what the
                // ReferenceEquals below is for: a copy holding the same number would pass an
                // equality check and fail the game.
                PlayerStat.WeaponRange => combat.Weapon.Range,
                PlayerStat.WeaponConeAngle => combat.Weapon.ConeAngleDeg,
                PlayerStat.ChargeDamage => combat.Charge.Damage,
                PlayerStat.ShieldRechargeDelay => combat.Health.ShieldRechargeDelay,
                PlayerStat.HealPerKill => combat.HealPerKill,

                // M6-08's four, each on the object that owns it.
                PlayerStat.KindlingPerStack => combat.Kindling.PerStack,
                PlayerStat.KindlingMaxStacks => combat.Kindling.MaxStacks,
                PlayerStat.PoolDamage => combat.Charge.PoolDamagePerPulse,
                PlayerStat.PoolDuration => combat.Charge.PoolDuration,

                // RS-03a's, which every class has.
                PlayerStat.FireWhileMoving => combat.FireWhileMoving,

                // RS-03b's three, on the volley.
                PlayerStat.VolleyEvery => combat.Volley.Every,
                PlayerStat.VolleyArrows => combat.Volley.Arrows,
                PlayerStat.VolleyDamage => combat.Volley.Damage,
                _ => null,
            };

            Assert.That(
                expected,
                Is.Not.Null,
                $"PlayerStat.{member} has no expectation in this row. A new member is four edits: "
                    + "the Stat on its owner, the member, the Resolve line, and this switch.");

            // ReferenceEquals, never a value comparison: the point of an address is that it hands
            // back the *instance* the game reads, so that a modifier put on it moves the live
            // number. Two stats that happen to hold 13 would pass an equality check and fail the
            // game.
            Assert.That(
                stats.Resolve(member),
                Is.SameAs(expected),
                $"PlayerStat.{member} must resolve to the very stat it names.");

            Assert.That(
                stats.Has(member),
                Is.True,
                $"PlayerStat.{member} resolves, so Has must say so — no address may answer one way "
                    + "here and the other way there.");

            answered++;
        }

        Assert.That(answered, Is.EqualTo(19), "Twenty members, one named exception.");

        // RS-03b rule 8: a class without a volley has none of the three, and says so both ways.
        var plain = new PlayerStats(Ember(), motor, progression);

        foreach (PlayerStat member in new[] { PlayerStat.VolleyEvery, PlayerStat.VolleyArrows, PlayerStat.VolleyDamage })
        {
            Assert.That(plain.Has(member), Is.False, $"a class without a volley has no {member}.");
            Assert.That(() => plain.Resolve(member), Throws.TypeOf<ArgumentOutOfRangeException>(), member.ToString());
        }
    }

    // ---- M6-08 rule 8: four addresses a run may not have -----------------------------------------

    /// <summary>The four members that depend on the class — Kindling on the weapon, a pool on the Blink.</summary>
    private static readonly PlayerStat[] ClassBound =
    {
        PlayerStat.KindlingPerStack,
        PlayerStat.KindlingMaxStacks,
        PlayerStat.PoolDamage,
        PlayerStat.PoolDuration,
    };

    [Test]
    public void Stats_KindlingAddressesAreTheRunsKindling()
    {
        PlayerCombat combat = Ember();
        var stats = new PlayerStats(combat, Motor(), Progression());

        Assert.That(stats.Resolve(PlayerStat.KindlingPerStack), Is.SameAs(combat.Kindling.PerStack));
        Assert.That(stats.Resolve(PlayerStat.KindlingMaxStacks), Is.SameAs(combat.Kindling.MaxStacks));

        // And a modifier through the handler moves the ramp — Stoked Coals' +0.01, ten stacks.
        new ModifyStatHandler(stats).Apply(new ModifyStat(PlayerStat.KindlingPerStack, ModifierKind.Flat, 0.01f), this);

        for (int i = 0; i < 10; i++)
        {
            combat.Kindling.OnWeaponHitLanded();
        }

        Assert.That(combat.Kindling.Bonus, Is.EqualTo(0.30f).Within(Tolerance), "ten stacks at 3 %, not 2 %.");
    }

    [Test]
    public void Stats_HasIsFalseForAClassWithoutTheObject()
    {
        // An Oathbound: no Kindling, and a Charge that leaves no pool.
        var stats = new PlayerStats(Combat(), Motor(), Progression());

        foreach (PlayerStat member in ClassBound)
        {
            Assert.That(stats.Has(member), Is.False, $"an Oathbound has no {member}.");

            var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => stats.Resolve(member), member.ToString());

            Assert.That(thrown.ParamName, Is.EqualTo("stat"));
        }
    }

    [Test]
    public void Stats_HasIsTrueForTheClassThatHasThem()
    {
        var stats = new PlayerStats(Ember(), Motor(), Progression());

        foreach (PlayerStat member in ClassBound)
        {
            Assert.That(stats.Has(member), Is.True, member.ToString());
            Assert.That(stats.Resolve(member), Is.Not.Null, member.ToString());
        }
    }

    [Test]
    public void Stats_APoolIsTheBlinksAndKindlingTheWeapons()
    {
        // The two halves apart, which the two rows above cannot tell: a Blink without Kindling has
        // the pool and not the ramp. Has asks each object on its own, not "is this the Emberwright".
        var stats = new PlayerStats(Ember(withKindling: false), Motor(), Progression());

        Assert.That(stats.Has(PlayerStat.PoolDamage), Is.True);
        Assert.That(stats.Has(PlayerStat.PoolDuration), Is.True);
        Assert.That(stats.Has(PlayerStat.KindlingPerStack), Is.False);
        Assert.That(stats.Has(PlayerStat.KindlingMaxStacks), Is.False);
    }

    [Test]
    public void Stats_UnknownMember_Throws()
    {
        var stats = new PlayerStats(Combat(), Motor(), Progression());

        Assert.That(
            () => stats.Resolve((PlayerStat)99),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "Stat.Pool's shape: a loud default, so a member with no line here fails rather than "
                + "being silently dropped from every effect that names it.");
    }

    [Test]
    public void Stats_NullArguments_Throw()
    {
        PlayerCombat combat = Combat();
        PlayerMotor motor = Motor();
        LevelTracker progression = Progression();

        Assert.That(() => new PlayerStats(null, motor, progression), Throws.ArgumentNullException);
        Assert.That(() => new PlayerStats(combat, null, progression), Throws.ArgumentNullException);
        Assert.That(() => new PlayerStats(combat, motor, null), Throws.ArgumentNullException);
    }

    // ---- The effect, as data ---------------------------------------------------------------------

    [Test]
    public void Effect_RecordsFields()
    {
        var effect = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);

        Assert.That(effect.Stat, Is.EqualTo(PlayerStat.WeaponDamage));
        Assert.That(effect.Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(effect.Value, Is.EqualTo(0.15f).Within(Tolerance));
    }

    [Test]
    public void Effect_Guards()
    {
        Assert.That(
            () => new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, float.NaN),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "A NaN in a Stat does not produce a wrong number, it silences Changed (AR §18.3).");

        Assert.That(
            () => new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, float.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        Assert.That(
            () => new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, float.NegativeInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        Assert.That(
            () => new ModifyStat(PlayerStat.MaxHp, (ModifierKind)99, 1f),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ---- Rule 6: one effect is one modifier ------------------------------------------------------

    [Test]
    public void Handler_ApplyAddsOneModifier()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);
        var source = new object();

        handler.Apply(new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f), source);

        Stat damage = combat.Weapon.Damage;

        Assert.That(damage.Value, Is.EqualTo(WeaponDamage * 1.15f).Within(Tolerance));
        Assert.That(damage.ModifierCount, Is.EqualTo(1), "One effect, one modifier.");

        var applied = new List<Modifier>();
        damage.CopyModifiersTo(applied);

        Assert.That(applied, Has.Count.EqualTo(1));
        Assert.That(applied[0].Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(applied[0].Value, Is.EqualTo(0.15f).Within(Tolerance));
        Assert.That(
            applied[0].Source,
            Is.SameAs(source),
            "The caller's source, tagged on the modifier — which is what Remove takes back by.");
    }

    [Test]
    public void Handler_TwoEffectsOneSourcePool()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);

        // One source, two effects: the "+2 damage and +15 %" node of Stat.Add's own remark.
        var node = new object();

        handler.Apply(new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 2f), node);
        handler.Apply(new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f), node);

        Assert.That(combat.Weapon.Damage.ModifierCount, Is.EqualTo(2));
        Assert.That(
            combat.Weapon.Damage.Value,
            Is.EqualTo((WeaponDamage + 2f) * 1.15f).Within(Tolerance),
            "Flat before PercentAdd — ADR-0008's order, and the stat's, not the handler's.");
    }

    [Test]
    public void Handler_RemoveTakesTheSourceBack()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);
        var node = new object();

        var flat = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 2f);
        var percent = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);

        handler.Apply(flat, node);
        handler.Apply(percent, node);

        // The flat one only — and both go. Removal is RemoveAll(source), which is the only removal
        // the modifier stack offers and the right one: half a node removed is worse than none.
        // Rule 6's documented behaviour, pinned here so that changing it is a decision rather than
        // an accident.
        handler.Remove(flat, node);

        Assert.That(combat.Weapon.Damage.ModifierCount, Is.Zero);
        Assert.That(combat.Weapon.Damage.Value, Is.EqualTo(WeaponDamage).Within(Tolerance));
    }

    // ---- Rule 7: the source is the caller's, and cleaning up is unconditional ---------------------

    [Test]
    public void Handler_RemoveUnknownSource_IsSilent()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);
        var node = new object();
        var stranger = new object();

        var effect = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 2f);

        handler.Apply(effect, node);

        Assert.That(() => handler.Remove(effect, stranger), Throws.Nothing);

        Assert.That(combat.Weapon.Damage.ModifierCount, Is.EqualTo(1), "The node's is untouched.");
        Assert.That(
            combat.Weapon.Damage.Value,
            Is.EqualTo(WeaponDamage + 2f).Within(Tolerance));
    }

    [Test]
    public void Handler_NullArguments_Throw()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);
        var effect = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 2f);

        Assert.That(() => new ModifyStatHandler(null), Throws.ArgumentNullException);
        Assert.That(() => handler.Apply(null, new object()), Throws.ArgumentNullException);
        Assert.That(() => handler.Remove(null, new object()), Throws.ArgumentNullException);

        // From Modifier, one layer down: a modifier nothing could ever take back is refused.
        Assert.That(() => handler.Apply(effect, null), Throws.ArgumentNullException);
        Assert.That(() => handler.Remove(effect, null), Throws.ArgumentNullException);
    }

    // ---- The addresses, each reaching the number the game reads -----------------------------------

    [Test]
    public void Handler_MaxHpMovesHealthLive()
    {
        // No Aegis, so the 40 points below land on hit points rather than being absorbed. The
        // shield is ShieldSpec's business and has nothing to do with this row.
        PlayerCombat combat = Combat(withShield: false);
        ModifyStatHandler handler = Handler(combat);

        combat.Health.ApplyDamage(40f, 0f);

        Assert.That(combat.Health.Current, Is.EqualTo(100f).Within(Tolerance), "Sanity: 140 − 40.");

        handler.Apply(new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 20f), new object());

        Assert.That(combat.Health.MaxHp.Value, Is.EqualTo(160f).Within(Tolerance));
        Assert.That(
            combat.Health.Current,
            Is.EqualTo(100f).Within(Tolerance),
            "A maximum that rises leaves current HP alone — Health's rule 6, not a free heal.");
        Assert.That(combat.Health.Fraction, Is.EqualTo(0.625f).Within(Tolerance));
    }

    [Test]
    public void Handler_XpGainScalesGrants()
    {
        LevelTracker progression = Progression();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), progression));

        handler.Apply(new ModifyStat(PlayerStat.XpGain, ModifierKind.PercentAdd, 0.5f), new object());

        progression.Grant(10f);

        Assert.That(
            progression.Xp,
            Is.EqualTo(15f).Within(Tolerance),
            "The M3-01a seam: XpGain is a base-1 Stat and every grant goes through it.");
        Assert.That(progression.Level, Is.EqualTo(1), "15 is well short of ToReach(2)'s 51.67.");
    }

    [Test]
    public void Handler_MoveSpeedReachesTheMotor()
    {
        PlayerMotor motor = Motor();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), motor, Progression()));

        handler.Apply(new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.PercentAdd, 0.1f), new object());

        // A whole second at full stick, which is long past the 0.06 s ramp — the motor lands
        // exactly on its target rather than approaching it, so this is the top speed and not a
        // point on the way to it.
        motor.Tick(1f, new Vector2(0f, 1f), null);

        Assert.That(
            motor.Velocity.Length(),
            Is.EqualTo(MoveSpeed * 1.1f).Within(Tolerance),
            "The motor derives its ramp from the live Stat, so a node moves the speed it reaches.");
    }

    [Test]
    public void Handler_AllocatesNothingAfterWarmUp()
    {
        PlayerCombat combat = Combat();
        ModifyStatHandler handler = Handler(combat);
        var effect = new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);
        var source = new object();

        // Applied and removed once before measuring, which is what buys the modifier list its
        // capacity: the first Add on a fresh Stat grows the backing array, and RemoveAll does not
        // give it back. AllocationAssert.None runs the body once for exactly this, and it is
        // spelled out here because the row's name claims it.
        AllocationAssert.None(() =>
        {
            handler.Apply(effect, source);
            handler.Remove(effect, source);
        });

        Assert.That(combat.Weapon.Damage.ModifierCount, Is.Zero, "It ended where it started.");
    }

    // ---- Rule 9: the registry is not handed out --------------------------------------------------

    [Test]
    public void State_HandsOutNoRegistry()
    {
        // The sixth time AR §18.2's question has been asked, and the first time the answer is not
        // about a Tick: Apply is public on the registry, so a public handle would let a view put a
        // permanent modifier on the player's damage. Asserted by reflection rather than by "it does
        // not compile", for the reason every other seal in this project is — a future refactor that
        // widened it would otherwise be caught by nothing.
        Assert.That(
            typeof(RunState).GetProperty("Effects"),
            Is.Null,
            "Effects must not be public — like Combat, Motor, Enemies, Projectiles and "
                + "Progression.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private PlayerCombat Combat(bool withShield = true) =>
        new PlayerCombat(Character(withShield), _events, _intents, EnemyCapacity);

    private LevelTracker Progression() => new LevelTracker(Scalings.Xp(), _events);

    /// <summary>
    /// The Emberwright's shape for M6-08's rows: a Blink with M6-07b's pool and, unless told
    /// otherwise, M6-07a's Kindling. The numbers are the shipped ones; nothing here is about them.
    /// A volley only when asked, for the one row that has to reach every address (RS-03b).
    /// </summary>
    private PlayerCombat Ember(bool withKindling = true, bool withVolley = false) =>
        new PlayerCombat(
            new CharacterSpec(
                new ContentId("character.emberwright"),
                new LocKey("character.emberwright.name"),
                new LocKey("character.emberwright.description"),
                70f,
                new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
                new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
                new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 3f),
                new FocusSpec(0.4f, 1f, 1f),
                new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 0f, 3f, 3f, 4f),
                null,
                HitIFrames,
                kindling: withKindling ? new KindlingSpec(0.02f, 30) : null,
                volley: withVolley ? new VolleySpec(3, 30f, 1.5f) : null),
            _events,
            _intents,
            EnemyCapacity);

    /// <summary>The Oathbound's movement at the owner's retuned 3 m/s, facing +Z where a run starts.</summary>
    private static PlayerMotor Motor() =>
        new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ);

    private ModifyStatHandler Handler(PlayerCombat combat) =>
        new ModifyStatHandler(new PlayerStats(combat, Motor(), Progression()));

    private static CharacterSpec Character(bool withShield) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        withShield ? new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill) : null,
        HitIFrames);
}
