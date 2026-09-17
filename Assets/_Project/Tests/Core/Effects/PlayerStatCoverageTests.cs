using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// M3-12a's five addresses: that each resolves to the very instance its owner holds, that a
/// modifier put on it through the address moves the thing the member names, and that the numbers
/// deliberately left unreachable are still unreachable.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModifyStatTests.Stats_ResolveEveryMember</c> walks the whole enum and is the row that fails
/// when a member arrives without a resolver line. This fixture is the other half: it asks what each
/// of the five is actually <em>for</em>, one member at a time, against real objects — a real
/// <c>Weapon</c>, a real <c>ChargeSkill</c>, a real <c>Health</c> — because the claim under test is
/// that the address reaches the number the game plays with rather than a stat that happens to
/// exist.
/// </para>
/// <para>
/// The behaviour each newly-live number changes is <c>StatReachTests</c>' subject, one module over.
/// The split is deliberate: this file is about the <em>table</em>, that one is about the swing.
/// </para>
/// <para>
/// The Oathbound's numbers throughout (CC §7): an 8 m, 60° Censer at 13 damage, a 20-damage Charge,
/// a 30-point Aegis on a 4 s delay.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PlayerStatCoverageTests
{
    private const string OathboundId = "character.oathbound";

    private const float WeaponDamage = 13f;
    private const float WeaponRange = 8f;
    private const float ConeAngle = 60f;
    private const float ChargeDamage = 20f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;

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

    // ---- Rule 1: the five, each reaching the instance its owner holds -----------------------------

    [Test]
    public void Stats_TheFiveResolveToTheirOwnersInstances()
    {
        PlayerCombat combat = Combat();
        PlayerStats stats = Stats(combat);

        // ReferenceEquals throughout, never a value comparison: an address that handed back a copy
        // holding 8 would pass every equality check in this file and move nothing in the game.
        Assert.That(stats.Resolve(PlayerStat.WeaponRange), Is.SameAs(combat.Weapon.Range));
        Assert.That(stats.Resolve(PlayerStat.WeaponConeAngle), Is.SameAs(combat.Weapon.ConeAngleDeg));
        Assert.That(stats.Resolve(PlayerStat.ChargeDamage), Is.SameAs(combat.Charge.Damage));
        Assert.That(
            stats.Resolve(PlayerStat.ShieldRechargeDelay),
            Is.SameAs(combat.Health.ShieldRechargeDelay));
        Assert.That(stats.Resolve(PlayerStat.HealPerKill), Is.SameAs(combat.HealPerKill));
    }

    [Test]
    public void Stats_TheFiveStartAtTheAuthoredNumbersWithNoModifiers()
    {
        PlayerCombat combat = Combat();

        Assert.That(combat.Weapon.Range.Base, Is.EqualTo(WeaponRange).Within(Tolerance));
        Assert.That(combat.Weapon.ConeAngleDeg.Base, Is.EqualTo(ConeAngle).Within(Tolerance));
        Assert.That(combat.Charge.Damage.Base, Is.EqualTo(ChargeDamage).Within(Tolerance));
        Assert.That(combat.Health.ShieldRechargeDelay.Base, Is.EqualTo(ShieldDelay).Within(Tolerance));

        Assert.That(
            combat.HealPerKill.Base,
            Is.Zero,
            "The one number that is new rather than promoted: nothing heals on a kill until a node "
                + "says so (rule 5).");

        // Every one of them starts clean, which is what makes "this task retunes nothing" checkable
        // rather than asserted: a promoted number carrying a modifier from birth would be a buff
        // nobody authored.
        Assert.That(combat.Weapon.Range.ModifierCount, Is.Zero);
        Assert.That(combat.Weapon.ConeAngleDeg.ModifierCount, Is.Zero);
        Assert.That(combat.Charge.Damage.ModifierCount, Is.Zero);
        Assert.That(combat.Health.ShieldRechargeDelay.ModifierCount, Is.Zero);
        Assert.That(combat.HealPerKill.ModifierCount, Is.Zero);
    }

    [Test]
    public void Stats_AModifierThroughTheAddressMovesEachOwner()
    {
        PlayerCombat combat = Combat();
        var handler = new ModifyStatHandler(Stats(combat));
        var node = new object();

        handler.Apply(new ModifyStat(PlayerStat.WeaponRange, ModifierKind.Flat, 2f), node);
        handler.Apply(new ModifyStat(PlayerStat.WeaponConeAngle, ModifierKind.PercentAdd, 0.5f), node);
        handler.Apply(new ModifyStat(PlayerStat.ChargeDamage, ModifierKind.PercentAdd, 0.5f), node);
        handler.Apply(
            new ModifyStat(PlayerStat.ShieldRechargeDelay, ModifierKind.PercentAdd, -0.5f),
            node);
        handler.Apply(new ModifyStat(PlayerStat.HealPerKill, ModifierKind.Flat, 2f), node);

        // Read off the owners rather than off the addresses, which is the whole point: the node
        // named a member of an enum and the number on the weapon moved.
        Assert.That(combat.Weapon.Range.Value, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(combat.Weapon.ConeAngleDeg.Value, Is.EqualTo(90f).Within(Tolerance));
        Assert.That(combat.Charge.Damage.Value, Is.EqualTo(30f).Within(Tolerance));
        Assert.That(combat.Health.ShieldRechargeDelay.Value, Is.EqualTo(2f).Within(Tolerance));
        Assert.That(combat.HealPerKill.Value, Is.EqualTo(2f).Within(Tolerance));
    }

    [Test]
    public void Stats_RemovingTheNodeTakesAllFiveBack()
    {
        PlayerCombat combat = Combat();
        var handler = new ModifyStatHandler(Stats(combat));
        var node = new object();

        var effects = new[]
        {
            new ModifyStat(PlayerStat.WeaponRange, ModifierKind.Flat, 2f),
            new ModifyStat(PlayerStat.WeaponConeAngle, ModifierKind.PercentAdd, 0.5f),
            new ModifyStat(PlayerStat.ChargeDamage, ModifierKind.PercentAdd, 0.5f),
            new ModifyStat(PlayerStat.ShieldRechargeDelay, ModifierKind.PercentAdd, -0.5f),
            new ModifyStat(PlayerStat.HealPerKill, ModifierKind.Flat, 2f),
        };

        foreach (ModifyStat effect in effects)
        {
            handler.Apply(effect, node);
        }

        foreach (ModifyStat effect in effects)
        {
            handler.Remove(effect, node);
        }

        // Back to the authored numbers exactly. A promoted stat that could be moved but not
        // un-moved would break M6's Pacts and M3-11a-ii's timed grants before it broke a node.
        Assert.That(combat.Weapon.Range.Value, Is.EqualTo(WeaponRange).Within(Tolerance));
        Assert.That(combat.Weapon.ConeAngleDeg.Value, Is.EqualTo(ConeAngle).Within(Tolerance));
        Assert.That(combat.Charge.Damage.Value, Is.EqualTo(ChargeDamage).Within(Tolerance));
        Assert.That(combat.Health.ShieldRechargeDelay.Value, Is.EqualTo(ShieldDelay).Within(Tolerance));
        Assert.That(combat.HealPerKill.Value, Is.Zero);
    }

    // ---- Rule 5: the trap base 0 lays for whoever authors Retribution -----------------------------

    [Test]
    public void HealPerKill_APercentageModifierMovesItNotAtAll()
    {
        PlayerCombat combat = Combat();
        var handler = new ModifyStatHandler(Stats(combat));

        // Five hundred per cent, which is far past anything a node would ever author, and it is
        // still exactly nothing.
        handler.Apply(new ModifyStat(PlayerStat.HealPerKill, ModifierKind.PercentAdd, 5f), new object());

        Assert.That(
            combat.HealPerKill.Value,
            Is.Zero,
            "Stat computes (Base + SumFlat) * (1 + SumPercentAdd) * Prod(1 + PercentMult), so a "
                + "base of zero with no Flat on the stack multiplies every percentage into zero. "
                + "ModifierKind.Flat is the ONLY kind that can move HealPerKill, and M3-12c's "
                + "Retribution must be authored Flat or it heals nothing, silently.");

        Assert.That(
            combat.HealPerKill.ModifierCount,
            Is.EqualTo(1),
            "The modifier is genuinely on the stack — this is arithmetic, not a rejected effect, "
                + "which is exactly why nothing anywhere reports it.");
    }

    [Test]
    public void HealPerKill_AFlatModifierIsWhatAPercentageThenScales()
    {
        PlayerCombat combat = Combat();
        var handler = new ModifyStatHandler(Stats(combat));

        // The order the remark on PlayerCombat.HealPerKill prescribes: a Flat node first, and a
        // percentage node afterwards has something to be a percentage *of*.
        handler.Apply(new ModifyStat(PlayerStat.HealPerKill, ModifierKind.Flat, 2f), new object());
        handler.Apply(new ModifyStat(PlayerStat.HealPerKill, ModifierKind.PercentAdd, 0.5f), new object());

        Assert.That(
            combat.HealPerKill.Value,
            Is.EqualTo(3f).Within(Tolerance),
            "(0 + 2) * 1.5. The percentage works once a Flat has given it a number to scale.");
    }

    // ---- Rules 3 and 4: what is deliberately still unreachable ------------------------------------

    [Test]
    public void Charge_KnockbackIsNotAddressable()
    {
        Assert.That(
            NamesAnythingLike("knockback"),
            Is.False,
            "Rule 3: 4 m is a positioning number, a node that moved it would need a playtest of "
                + "its own, and none of v1's twelve wants it. Left authored on MovementSkillSpec.");
    }

    [Test]
    public void Swing_IsNotAPlayerStatMember()
    {
        // **Beside Charge_KnockbackIsNotAddressable rather than in a file of its own, and it is the
        // same scan on purpose** (M3-12b rule 6). The one fragment protects two claims now: the
        // dash's 4 m is left authored, and the swing's shove is reachable only through
        // KnockbackOnSwing — which is what makes that primitive's choice of ModifierKind.Flat the
        // *only* way the number can ever be moved.
        Assert.That(
            NamesAnythingLike("knockback"),
            Is.False,
            "Rule 6: PlayerCombat.SwingKnockback has a base of zero, so a node authored as "
                + "\"+50 % swing knockback\" through ModifyStat would shove nobody, silently and "
                + "for ever — HealPerKill's trap, which M3-12a measured. A named primitive with "
                + "one number and no kind makes the wrong authoring impossible; a PlayerStat "
                + "member would put the dropdown back.");

        Assert.That(
            NamesAnythingLike("swing"),
            Is.False,
            "…and it is not reachable under another name either. Nothing in the enum says swing.");
    }

    [Test]
    public void Swing_TheStatItselfStartsAtZeroWithNoModifiers()
    {
        PlayerCombat combat = Combat();

        Assert.That(
            combat.SwingKnockback.Base,
            Is.Zero,
            "A swing shoves nobody until a node says so (rule 5).");

        Assert.That(
            combat.SwingKnockback.ModifierCount,
            Is.Zero,
            "And it ships clean, which is what makes \"this task retunes nothing\" checkable "
                + "rather than asserted.");
    }

    [Test]
    public void Health_ShieldMaxAndRateAreNotAddressable()
    {
        Assert.That(
            NamesAnythingLike("shieldmax"),
            Is.False,
            "Rule 4: raising a maximum without filling it is the trap Handler_MaxHpMovesHealthLive "
                + "documents.");

        Assert.That(
            NamesAnythingLike("refill"),
            Is.False,
            "Rule 4: RefillPerSecond is Unbroken's keystone and should arrive whole rather than "
                + "half-reachable.");

        // The delay is the one that did arrive, asserted alongside so that this row says which of
        // the Aegis's three is reachable rather than only which two are not.
        Assert.That(
            NamesAnythingLike("shieldrechargedelay"),
            Is.True,
            "The delay is the number the player feels, and is the one node-reachable Aegis number.");
    }

    [Test]
    public void Stats_EveryMemberIsDistinctlyNamed()
    {
        // Cheap, and it guards the one mistake this table cannot survive: two members sharing an
        // ordinal would make one node silently move the other's number. Nothing else checks it,
        // because Resolve's switch would still compile.
        Array values = Enum.GetValues(typeof(PlayerStat));

        Assert.That(
            Enum.GetNames(typeof(PlayerStat)).Length,
            Is.EqualTo(values.Length),
            "One name per value.");

        Assert.That(
            values.Length,
            Is.EqualTo(11),
            "Six from M3-05 plus M3-12a's five. A member arriving without a spec is what this "
                + "catches; update the number and say which task added it.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// Whether any <see cref="PlayerStat"/> member's name contains <paramref name="fragment"/>,
    /// compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// A name scan rather than a resolver call, because the claim is that the number is
    /// <em>unnameable</em>: an authored effect can only ever say a member of this enum, so a
    /// fragment nothing is called is a number no node can reach.
    /// </remarks>
    private static bool NamesAnythingLike(string fragment)
    {
        foreach (string name in Enum.GetNames(typeof(PlayerStat)))
        {
            if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private PlayerCombat Combat() => new PlayerCombat(Character(), _events, _intents, EnemyCapacity);

    private PlayerStats Stats(PlayerCombat combat) =>
        new PlayerStats(combat, Motor(), new LevelTracker(Scalings.Xp(), _events));

    private static PlayerMotor Motor() =>
        new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ);

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, WeaponRange, ConeAngle, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, ChargeDamage, 4f, 0.05f),
        new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill),
        HitIFrames);
}
