using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// What each of M3-12a's newly-live numbers actually changes: a wider cone reaches the intent, a
/// longer weapon acquires sooner, a modifier taken mid-fight lands on the <em>next</em> use rather
/// than the one after it, a stack driven absurd is answered where the number is used, and a kill
/// pays a heal through the door the experience already comes through.
/// </summary>
/// <remarks>
/// <para>
/// <b>M3-12b's <c>SwingKnockback</c> joined the five here rather than in <c>PlayerCombatTests</c>,
/// which is where its spec listed it.</b> This is the fixture that already drives a Censer to its
/// damage frame and reports a wedge back; that one has no <c>EnemySystem</c> and no swing helper, so
/// the spec's placement would have meant duplicating forty lines of fixture to watch one intent. The
/// primitive and its handler are <c>SkillTargetedEffectTests</c>'; what the swing does with the
/// number is here, with the rest of the reach.
/// </para>
/// <para>
/// <c>PlayerStatCoverageTests</c> is the other half and asks a different question: it is about the
/// address <em>table</em> — that a member resolves to the instance its owner holds. This file never
/// mentions <c>PlayerStats</c>. It puts a modifier straight onto the owner's stat and then watches
/// the game behave differently, which is the claim a player would notice.
/// </para>
/// <para>
/// <b>Built directly rather than driven through a <c>RunSession</c>, deliberately.</b>
/// <c>RunState.Combat</c>, <c>RunState.Enemies</c> and <c>RunState.Effects</c> are all
/// <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> (M0-10), so
/// there is no route from outside core to a weapon whose reach has been modified — the same trade
/// <c>ConeHitsToDamageTests</c> makes for a zero-damage swing and
/// <c>ChargeIntegrationTests</c> makes for its i-frame row. The wiring in <c>RunSession.Tick</c> is
/// pinned by <c>RunSessionTests</c>; what is pinned here is what the wiring carries.
/// </para>
/// <para>
/// CC §7's Oathbound throughout: an 8 m, 60° Censer at 13 damage and three swings a second, a
/// 20-damage Charge, against GD §8.1's 36 hit point Husk. The Focus ramp is switched off with a
/// <c>MaxMultiplier</c> of 1 in every row, for <c>PlayerCombatTests</c>' reason — every row here
/// ticks a centred stick, and a ramp quietly speeding the Censer up would move the swing cadence
/// these rows count frames against.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StatReachTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    private const float WeaponDamage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float WeaponRange = 8f;
    private const float ConeAngle = 60f;
    private const float ChargeDamage = 20f;
    private const float ChargeDuration = 0.22f;

    private const float HuskMaxHp = 36f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;

    private const int EnemyCapacity = 8;

    /// <summary>One 60 fps frame, the cadence every row here ticks at.</summary>
    private const float Frame = 1f / 60f;

    /// <summary>
    /// Ample for one swing of the Censer: a third of a second is 20 frames, and no row here slows
    /// the weapon down.
    /// </summary>
    private const int MaxFramesPerSwing = 40;

    private const float Tolerance = 1e-3f;

    /// <summary>
    /// How far ahead the shove rows stand their Husks. Three metres is well inside the Censer's
    /// 8 m and leaves room to put one to each side without leaving the 60° wedge.
    /// </summary>
    private const float ConeDepth = 3f;

    /// <summary>Straight ahead on +Z, which is where a fresh <c>PlayerMotor</c> faces.</summary>
    private static readonly Vector3 Facing = Vector3.UnitZ;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rule 2: the cone's two numbers reach the swing -------------------------------------------

    [Test]
    public void Weapon_WiderConeReachesTheIntent()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        combat.Weapon.ConeAngleDeg.Add(Percent(0.5f));

        SwingOnce(combat, registry);

        Assert.That(
            _intents.LastConeHit.AngleDeg,
            Is.EqualTo(90f).Within(Tolerance),
            "60 + 50 %. The wedge the body is asked to sweep is the live stat, not the spec.");
    }

    [Test]
    public void Weapon_LongerRangeReachesTheIntent()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        combat.Weapon.Range.Add(Flat(2f));

        SwingOnce(combat, registry);

        Assert.That(_intents.LastConeHit.Range, Is.EqualTo(10f).Within(Tolerance), "8 + 2.");
    }

    [Test]
    public void Weapon_LongerRangeAcquiresSooner()
    {
        PlayerCombat combat = Combat();

        // Nine metres: inside the 12 m acquire range, so it is targeted either way, and outside the
        // Censer's 8 m, so the weapon has nothing to swing at. That gap is the whole row.
        EnemyRegistry registry = WithHusk(At(9f));

        Advance(combat, registry, 0f, MaxFramesPerSwing);

        Assert.That(
            combat.Targeter.CurrentTargetId,
            Is.GreaterThanOrEqualTo(0),
            "Sanity: it is acquired. This row is about the weapon's reach, not the targeter's.");

        Assert.That(
            _intents.ConeHits,
            Is.Empty,
            "Out of reach: a target is not the same thing as something worth swinging at.");

        combat.Weapon.Range.Add(Flat(2f));

        SwingOnce(combat, registry);

        Assert.That(
            _intents.ConeHits.Count,
            Is.EqualTo(1),
            "Ten metres reaches nine. The node changed how the fight is spaced, not only how hard "
                + "it lands — which is rule 2's reason for promoting Range at all.");
    }

    // ---- Rule 6: sampled live, never cached at Start ----------------------------------------------

    [Test]
    public void Weapon_StatIsSampledPerSwing()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        float now = SwingOnce(combat, registry);

        Assert.That(_intents.LastConeHit.AngleDeg, Is.EqualTo(ConeAngle).Within(Tolerance), "Sanity.");

        // Mid-cadence, with the first swing already thrown and answered — a node taken between two
        // swings, which is exactly when a level-up screen hands one over (CH §4.3).
        combat.Weapon.ConeAngleDeg.Add(Percent(0.5f));

        SwingOnce(combat, registry, now);

        Assert.That(
            _intents.LastConeHit.AngleDeg,
            Is.EqualTo(90f).Within(Tolerance),
            "The very NEXT swing, not the one after it. A value cached at Start would have widened "
                + "one swing too late, and nothing outside this row would ever have said so.");
    }

    [Test]
    public void Stats_AreSampledLiveNotAtStart()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        // A fight already under way before a single modifier exists: every one of the five has been
        // read at least once at its authored value.
        float now = SwingOnce(combat, registry);

        combat.Weapon.Range.Add(Flat(2f));
        combat.Weapon.ConeAngleDeg.Add(Percent(0.5f));
        combat.Charge.Damage.Add(Percent(0.5f));
        combat.Health.ShieldRechargeDelay.Add(Percent(-0.5f));
        combat.HealPerKill.Add(Flat(2f));

        // Two of them are observable at the next use of the number, which is the next swing.
        SwingOnce(combat, registry, now);

        Assert.That(_intents.LastConeHit.Range, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(_intents.LastConeHit.AngleDeg, Is.EqualTo(90f).Within(Tolerance));

        // The other three are read at their own moments, and the values are what those moments will
        // see. Asserted on the stats rather than by staging three more mechanics here, each of
        // which has its own row above or in its own fixture.
        Assert.That(combat.Charge.Damage.Value, Is.EqualTo(30f).Within(Tolerance));
        Assert.That(combat.Health.ShieldRechargeDelay.Value, Is.EqualTo(2f).Within(Tolerance));
        Assert.That(combat.HealPerKill.Value, Is.EqualTo(2f).Within(Tolerance));
    }

    // ---- Rule 7: what the swing does with a stack driven absurd -----------------------------------

    [Test]
    public void Cone_ClampsAnAbsurdAngle()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));
        var source = new object();

        // 400°, which is more than a circle. Clamped down to the widest wedge there is.
        combat.Weapon.ConeAngleDeg.Add(new Modifier(ModifierKind.Flat, 340f, source));

        float now = SwingOnce(combat, registry);

        Assert.That(_intents.LastConeHit.AngleDeg, Is.EqualTo(360f).Within(Tolerance), "400 → 360.");

        // −10°, which is not a wedge at all.
        combat.Weapon.ConeAngleDeg.RemoveAll(source);
        combat.Weapon.ConeAngleDeg.Add(new Modifier(ModifierKind.Flat, -70f, source));

        now = SwingOnce(combat, registry, now);

        Assert.That(
            _intents.LastConeHit.AngleDeg,
            Is.Zero,
            "−10 → 0: a swing that connects with nothing, and reversible the moment the modifier "
                + "comes off — Weapon.TryGetInterval's bargain with a silenced fire rate.");

        // NaN is the third value the spec's row asks for, and it cannot be reached through either
        // ordinary door: Stat.Base and Modifier both refuse a non-finite input outright, because a
        // NaN in a stat does not produce a wrong number — it silences Changed (AR §18.3). So the
        // clamp is a backstop against arithmetic inside the stack, never against an authored
        // number, and the row that pins its answer is Cone_NaNAngleIsRefusedRatherThanWidened.
        combat.Weapon.ConeAngleDeg.RemoveAll(source);

        Assert.That(
            () => combat.Weapon.ConeAngleDeg.Base = float.NaN,
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "Refused at the door rather than clamped at the swing.");

        Assert.That(
            () => combat.Weapon.ConeAngleDeg.Add(new Modifier(ModifierKind.Flat, float.NaN, source)),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        Assert.That(
            combat.Weapon.ConeAngleDeg.Value,
            Is.EqualTo(ConeAngle).Within(Tolerance),
            "…and neither refusal left the stat in a broken state.");
    }

    [Test]
    public void Cone_ANegativeProductHitsNothingRatherThanBecomingACircle()
    {
        // The clamp's refusing branch, reached the only way a shipped modifier can reach it: not by
        // a non-finite value — Stat.Base and Modifier both refuse those at the door, which the row
        // above pins — but by a PercentMult below −1, which ADR-0008 explicitly permits and
        // Weapon.TryGetInterval already answers for the fire rate.
        //
        // It shares its branch with NaN. Both fall to zero through the same negated-positive test,
        // so this row is what proves the answer a NaN would get if arithmetic inside the stack ever
        // produced one: the narrowest wedge, never the widest.
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));
        var source = new object();

        combat.Weapon.ConeAngleDeg.Add(new Modifier(ModifierKind.PercentMult, -2f, source));

        SwingOnce(combat, registry);

        Assert.That(
            _intents.LastConeHit.AngleDeg,
            Is.Zero,
            "60 × (1 − 2) = −60, and a negative wedge hits nothing rather than becoming a circle.");
    }

    [Test]
    public void Cone_ClampsANegativeRange()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        // The swing has to be *in progress* before the reach goes negative, and that is not a dodge
        // around the rule — it is the only way the intent is observable at all. The in-range test
        // and the intent read the same clamped number, so a range that was already negative stops
        // the swing before it starts (the row below). A swing already thrown lands its damage frame
        // regardless of the target, which is Weapon's documented "the damage frame belongs to the
        // swing, not to the target".
        StartSwing(combat, registry);

        combat.Weapon.Range.Add(new Modifier(ModifierKind.Flat, -11f, new object()));

        float now = ToDamageFrame(combat, registry, Frame);

        Assert.That(
            _intents.LastConeHit.Range,
            Is.Zero,
            "−3 → 0: a wedge with no depth, which hits nothing.");

        Assert.That(now, Is.GreaterThan(0f), "Sanity: the damage frame actually arrived.");
    }

    [Test]
    public void Cone_NegativeRangeStopsTheNextSwing()
    {
        PlayerCombat combat = Combat();
        EnemyRegistry registry = WithHusk(At(5f));

        combat.Weapon.Range.Add(new Modifier(ModifierKind.Flat, -11f, new object()));

        Advance(combat, registry, 0f, MaxFramesPerSwing);

        Assert.That(
            _intents.ConeHits,
            Is.Empty,
            "The targeter's in-range test reads the same clamped reach the intent does, so a "
                + "negative range stops the swing rather than throwing one that reaches nothing. "
                + "Strictly better than the spec's row asked for, and the two sites cannot "
                + "disagree because there is one clamp.");
    }

    // ---- Rule 3: the dash's damage -----------------------------------------------------------------

    [Test]
    public void Charge_DamageReachesTheDash()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();
        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));

        combat.Charge.Damage.Add(Percent(0.5f));

        Dash(combat, enemies, husk.Id);

        Assert.That(
            husk.Health.Current,
            Is.EqualTo(HuskMaxHp - 30f).Within(Tolerance),
            "20 + 50 % = 30. ResolveChargeHits reads ChargeSkill.Damage now, which is the line its "
                + "own comment has been promising since M1-15.");
    }

    [Test]
    public void Charge_UnmodifiedDamageStillDealsTwenty()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();
        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));

        Dash(combat, enemies, husk.Id);

        Assert.That(
            husk.Health.Current,
            Is.EqualTo(HuskMaxHp - ChargeDamage).Within(Tolerance),
            "CC §7's 20, unchanged. This task retunes nothing — it only makes the number "
                + "reachable.");
    }

    // ---- Rule 5: a kill accrues, and the drain spends it ------------------------------------------

    [Test]
    public void Kill_AccruesPendingKills()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();
        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));

        Assert.That(enemies.PendingKills, Is.Zero, "Nothing has died yet.");

        enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);

        Assert.That(
            enemies.PendingKills,
            Is.EqualTo(1),
            "Banked on the one door a death comes through, beside the experience (M3-01a rule 3).");

        Assert.That(enemies.DrainKills(), Is.EqualTo(1));
        Assert.That(enemies.PendingKills, Is.Zero, "Take-and-clear in one call, DrainXp's shape.");
        Assert.That(enemies.DrainKills(), Is.Zero, "And a second drain is worth nothing.");
    }

    [Test]
    public void Kill_DamageThatDoesNotKillAccruesNothing()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();
        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));

        enemies.ApplyDamage(husk.Id, WeaponDamage, 1f, combat);

        Assert.That(
            enemies.PendingKills,
            Is.Zero,
            "A hit is not a kill. The counter moves on Killed, which is true exactly once per life.");
    }

    [Test]
    public void Kill_ClearDropsPendingKills()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        for (int i = 0; i < 3; i++)
        {
            EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f + i));
            enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);
        }

        Assert.That(enemies.PendingKills, Is.EqualTo(3), "Sanity.");

        enemies.Clear();

        Assert.That(
            enemies.PendingKills,
            Is.Zero,
            "The kills go with the bodies that earned them, exactly as the experience does — "
                + "banked on the same line, dropped on the same one.");
    }

    [Test]
    public void Kill_HealsNothingByDefault()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        combat.Health.ApplyDamage(40f + ShieldMax, 0f);

        Assert.That(combat.Health.Current, Is.EqualTo(100f).Within(Tolerance), "Sanity: 140 − 40.");

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));
        enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);

        combat.HealForKills(enemies.DrainKills());

        Assert.That(
            combat.Health.Current,
            Is.EqualTo(100f).Within(Tolerance),
            "Base zero: the line is dead code until M3-12c's Retribution, which is the whole "
                + "bargain of adding the number a task before the node that moves it.");
    }

    [Test]
    public void Kill_HealsWhenANodeSaysSo()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        combat.Health.ApplyDamage(40f + ShieldMax, 0f);

        // Flat, and it has to be — a percentage on a base of zero is zero. See
        // PlayerStatCoverageTests.HealPerKill_APercentageModifierMovesItNotAtAll.
        combat.HealPerKill.Add(Flat(2f));

        for (int i = 0; i < 3; i++)
        {
            EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f + i));
            enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);
        }

        combat.HealForKills(enemies.DrainKills());

        Assert.That(
            combat.Health.Current,
            Is.EqualTo(106f).Within(Tolerance),
            "Three kills at 2 each, paid in one drain rather than three.");
    }

    [Test]
    public void Kill_HealIsDrainedNotPaidOnTheFact()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        combat.Health.ApplyDamage(40f + ShieldMax, 0f);
        combat.HealPerKill.Add(Flat(2f));

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));
        enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);

        Assert.That(
            combat.Health.Current,
            Is.EqualTo(100f).Within(Tolerance),
            "The kill landed and paid nothing yet: a death reported between ticks accrues, and "
                + "what it is worth is decided on the tick — the experience drain's shape exactly.");

        combat.HealForKills(enemies.DrainKills());

        Assert.That(combat.Health.Current, Is.EqualTo(102f).Within(Tolerance), "…and now it is paid.");
    }

    [Test]
    public void Kill_DeadPlayerHealsNothing()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        combat.HealPerKill.Add(Flat(2f));

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));
        enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);

        combat.ApplyDamage(MaxHp + ShieldMax, 1f);

        Assert.That(combat.IsDead, Is.True, "Sanity.");

        // In the live run this call is never reached at all: RunSession.Tick returns at the death
        // check one step above the drain (M3-01a rule 6). Asserted here as the belt to that
        // braces, because Health.Heal refusing a corpse is what makes the ordering safe rather
        // than merely correct.
        Assert.That(combat.HealForKills(enemies.DrainKills()), Is.Zero);
        Assert.That(combat.Health.Current, Is.Zero, "A corpse heals nothing.");
    }

    [Test]
    public void Kill_HealForKillsIgnoresANonPositiveCount()
    {
        PlayerCombat combat = Combat();

        combat.Health.ApplyDamage(40f + ShieldMax, 0f);
        combat.HealPerKill.Add(Flat(2f));

        Assert.That(combat.HealForKills(0), Is.Zero, "The ordinary tick: nothing died.");
        Assert.That(combat.HealForKills(-3), Is.Zero, "A caller bug must not become a heal.");
        Assert.That(combat.Health.Current, Is.EqualTo(100f).Within(Tolerance));
    }

    [Test]
    public void Kill_HealIsCappedAtTheMaximum()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        combat.Health.ApplyDamage(1f + ShieldMax, 0f);
        combat.HealPerKill.Add(Flat(50f));

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(3f));
        enemies.ApplyDamage(husk.Id, HuskMaxHp, 1f, combat);

        Assert.That(
            combat.HealForKills(enemies.DrainKills()),
            Is.EqualTo(1f).Within(Tolerance),
            "Health.Heal reports what actually went in, and there was one point of headroom.");

        Assert.That(combat.Health.Current, Is.EqualTo(MaxHp).Within(Tolerance));
    }

    // ---- M3-12b rules 5, 7 and 8: the swing that shoves -------------------------------------------

    [Test]
    public void Swing_ShovesNobodyByDefault()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        int left = enemies.Spawn(new ContentId(HuskId), Beside(-1.5f)).Id;
        int right = enemies.Spawn(new ContentId(HuskId), Beside(1.5f)).Id;

        SwingInto(combat, enemies, 0f, left, right);

        Assert.That(
            combat.SwingKnockback.Base,
            Is.Zero,
            "Base 0: a swing shoves nobody until a node says so (rule 5).");

        Assert.That(
            _intents.Knockbacks,
            Is.Empty,
            "Two Husks hit and not an intent between them — which is every run this build plays, "
                + "because no tree carries a KnockbackOnSwing until M3-12c.");
    }

    [Test]
    public void Swing_ShovesWhenANodeSaysSo()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        int left = enemies.Spawn(new ContentId(HuskId), Beside(-1.5f)).Id;
        int right = enemies.Spawn(new ContentId(HuskId), Beside(1.5f)).Id;

        combat.SwingKnockback.Add(Flat(1.5f));

        SwingInto(combat, enemies, 0f, left, right);

        Assert.That(
            _intents.Knockbacks.Count,
            Is.EqualTo(2),
            "One per enemy the swing reached, not one per swing (rule 5).");

        Assert.That(_intents.Knockbacks[0].Id, Is.EqualTo(left));
        Assert.That(_intents.Knockbacks[1].Id, Is.EqualTo(right));

        foreach (EnemyKnockbackIntent shove in _intents.Knockbacks)
        {
            Assert.That(shove.Distance, Is.EqualTo(1.5f).Within(Tolerance));
        }
    }

    [Test]
    public void Swing_ShovesTheWayTheSwingFaced()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        // One Husk to each side of the facing axis and both inside the 60° wedge, which is the
        // arrangement that tells the two candidate answers apart: swept the way the swing faced,
        // both go the same way; shoved radially away from the player, they fan apart.
        int left = enemies.Spawn(new ContentId(HuskId), Beside(-1.5f)).Id;
        int right = enemies.Spawn(new ContentId(HuskId), Beside(1.5f)).Id;

        combat.SwingKnockback.Add(Flat(1.5f));

        SwingInto(combat, enemies, 0f, left, right);

        Vector2 facing = _intents.LastConeHit.FacingXZ;

        // **The same direction for both, which is ResolveChargeHits' answer one method up and
        // EnemyKnockbackIntent.DirectionXZ's own rule** (rule 7): everything one sweep catches is
        // swept the same way, which reads as a shove rather than as an explosion. A cone has no
        // single point of impact to be radial about, and the two answers differ most exactly where
        // the cone is widest.
        Assert.That(_intents.Knockbacks[0].DirectionXZ, Is.EqualTo(facing));
        Assert.That(_intents.Knockbacks[1].DirectionXZ, Is.EqualTo(facing));

        // And the row would be vacuous without this: the radial answer for the left Husk is a
        // different vector, so asserting only "they are equal to each other" would also pass
        // against a shove that pointed at nothing in particular.
        var radial = Vector2.Normalize(new Vector2(-1.5f, ConeDepth));

        Assert.That(
            _intents.Knockbacks[0].DirectionXZ.X,
            Is.Not.EqualTo(radial.X).Within(Tolerance),
            "Not away from the point of impact — rule 7 rejects that by name.");
    }

    [Test]
    public void Swing_StacksAdditively()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        int husk = enemies.Spawn(new ContentId(HuskId), At(3f)).Id;

        combat.SwingKnockback.Add(Flat(1.5f));
        combat.SwingKnockback.Add(Flat(1.5f));

        SwingInto(combat, enemies, 0f, husk);

        Assert.That(
            _intents.Knockbacks[0].Distance,
            Is.EqualTo(3f).Within(Tolerance),
            "Two nodes of 1.5 give 3 (rule 8). Nothing counts nodes: it is two Flat modifiers on "
                + "one stat and Stat's own arithmetic.");
    }

    [Test]
    public void Swing_NegativeOrNonFiniteShovesNobody()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        float now = 0f;

        // **A negative stack is a pull, and nothing in the design has ever asked for one** (rule 8).
        combat.SwingKnockback.Add(Flat(-2f));

        now = SwingInto(combat, enemies, now, Fresh(enemies));

        Assert.That(_intents.Knockbacks, Is.Empty, "−2 shoves nobody rather than pulling anybody.");

        combat.SwingKnockback.RemoveAll();

        // **Infinity, reached the only way it can be.** Modifier and Stat.Base both refuse a
        // non-finite input at the door (M3-12a's own correction), so the only route is arithmetic
        // overflow inside the stack: two Flat modifiers of float.MaxValue sum to +∞.
        combat.SwingKnockback.Add(Flat(float.MaxValue));
        combat.SwingKnockback.Add(Flat(float.MaxValue));

        Assert.That(
            float.IsPositiveInfinity(combat.SwingKnockback.Value),
            Is.True,
            "Sanity: the stack really is infinite, so this phase is about the guard rather than "
                + "about a modifier that never landed.");

        now = SwingInto(combat, enemies, now, Fresh(enemies));

        Assert.That(
            _intents.Knockbacks,
            Is.Empty,
            "An infinite shove is a teleport, and it passes a `> 0` test — which is why the guard "
                + "asks about it separately.");

        // **And NaN, which needs the infinity above and one more factor.** (0 + ∞) × (1 + (−1)) is
        // ∞ × 0. It is the branch a naive `knockback > 0f` would take safely by accident and a
        // `!(knockback <= 0f)` would wave straight through (AR §18.3).
        combat.SwingKnockback.Add(new Modifier(ModifierKind.PercentMult, -1f, new object()));

        Assert.That(float.IsNaN(combat.SwingKnockback.Value), Is.True, "Sanity: unreadable.");

        SwingInto(combat, enemies, now, Fresh(enemies));

        Assert.That(_intents.Knockbacks, Is.Empty, "A number nobody can read shoves nobody.");
    }

    [Test]
    public void Swing_RemoveStopsTheShove()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        var node = new object();

        combat.SwingKnockback.Add(new Modifier(ModifierKind.Flat, 1.5f, node));

        float now = SwingInto(combat, enemies, 0f, Fresh(enemies));

        Assert.That(_intents.Knockbacks.Count, Is.EqualTo(1), "Sanity: it was shoving.");

        combat.SwingKnockback.RemoveAll(node);
        _intents.Clear();

        SwingInto(combat, enemies, now, Fresh(enemies));

        Assert.That(
            _intents.Knockbacks,
            Is.Empty,
            "Back to zero and back to a swing that shoves nobody — which is the door the first "
                + "timed knockback buff will use (rule 8).");
    }

    [Test]
    public void Swing_ReadsTheFacingTheSwingWasThrownWith()
    {
        PlayerCombat combat = Combat();
        EnemySystem enemies = Enemies();

        int husk = enemies.Spawn(new ContentId(HuskId), At(3f)).Id;

        combat.SwingKnockback.Add(Flat(1.5f));

        // The damage frame is reached with the player facing +Z, and the report arrives at least a
        // frame later — which is the whole reason the facing is remembered with the pending request
        // rather than read when the answer comes back (rule 7).
        float now = ToDamageFrame(combat, enemies.Registry, 0f);

        Vector2 thrown = _intents.LastConeHit.FacingXZ;

        // Several frames of the player facing somewhere else entirely, before the body answers.
        WorldSnapshot snapshot = Snapshot();

        for (int i = 0; i < 5; i++)
        {
            combat.Tick(Frame, now, snapshot, enemies.Registry.Alive, -Vector3.UnitZ);
            now += Frame;
        }

        combat.ResolveConeHits(new[] { husk }, now, enemies);

        Assert.That(
            _intents.Knockbacks[0].DirectionXZ,
            Is.EqualTo(thrown),
            "The swing's facing, not the player's five frames later. A swing thrown north shoves "
                + "north however far the stick has swung since.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private static Modifier Flat(float value) => new Modifier(ModifierKind.Flat, value, new object());

    private static Modifier Percent(float value) =>
        new Modifier(ModifierKind.PercentAdd, value, new object());

    /// <summary>A point <paramref name="metres"/> straight ahead of the player, on +Z.</summary>
    private static Vector3 At(float metres) => new Vector3(0f, 0f, metres);

    /// <summary>
    /// A point <see cref="ConeDepth"/> ahead and <paramref name="offset"/> to one side — inside the
    /// Censer's 60° wedge at about 27° off the axis, which is what the shove rows need: two enemies
    /// the swing genuinely catches, on opposite sides of the facing.
    /// </summary>
    private static Vector3 Beside(float offset) => new Vector3(offset, 0f, ConeDepth);

    /// <summary>A Husk at full health, three metres ahead. One per swing, so nothing ever dies.</summary>
    /// <remarks>
    /// A fresh one per phase rather than one Husk swung at repeatedly, because 13 damage a swing
    /// into 36 hit points is a kill on the third — and <c>ResolveConeHits</c> skips an id that
    /// resolved to nothing, so a corpse would report zero shoves for the same reason a guard would.
    /// The row would pass while proving nothing (Traps §7).
    /// </remarks>
    private static int Fresh(EnemySystem enemies) =>
        enemies.Spawn(new ContentId(HuskId), At(3f)).Id;

    /// <summary>
    /// One whole swing from <paramref name="now"/>, with <paramref name="ids"/> reported back as
    /// having stood in the wedge — the round trip <c>RunSession.ReportConeHits</c> makes.
    /// </summary>
    /// <returns>The clock the swing's damage frame landed on.</returns>
    private float SwingInto(PlayerCombat combat, EnemySystem enemies, float now, params int[] ids)
    {
        now = ToDamageFrame(combat, enemies.Registry, now);

        combat.ResolveConeHits(ids, now, enemies);

        return now;
    }

    private PlayerCombat Combat() => new PlayerCombat(Oathbound(), _events, _intents, EnemyCapacity);

    private EnemySystem Enemies() => new EnemySystem(
        new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, Array.Empty<ModeSpec>()),
        _events,
        new FixedRandom(),
        new DepthScaling(Scalings.Design()),
        EnemyCapacity);

    /// <summary>A registry holding one Husk at <paramref name="position"/>.</summary>
    private static EnemyRegistry WithHusk(Vector3 position)
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        registry.Spawn(Husk(), position);

        return registry;
    }

    private static WorldSnapshot Snapshot()
    {
        var snapshot = new WorldSnapshot(EnemyCapacity);
        snapshot.MoveInput = Vector2.Zero;

        return snapshot;
    }

    /// <summary>Ticks <paramref name="frames"/> frames from <paramref name="now"/> and returns the clock.</summary>
    private static float Advance(PlayerCombat combat, EnemyRegistry registry, float now, int frames)
    {
        WorldSnapshot snapshot = Snapshot();

        for (int i = 0; i < frames; i++)
        {
            combat.Tick(Frame, now, snapshot, registry.Alive, Facing);
            now += Frame;
        }

        return now;
    }

    /// <summary>
    /// Ticks until the weapon writes its next <c>ConeHitIntent</c> and returns the clock it left
    /// off at.
    /// </summary>
    /// <remarks>
    /// The intent is the signal for <c>ConeHitsToDamageTests</c>' reason: it is the only one visible
    /// from outside, and <c>PlayerAttacked</c> announces the swing's *start*, four tenths of an
    /// interval too early.
    /// </remarks>
    private float ToDamageFrame(PlayerCombat combat, EnemyRegistry registry, float now)
    {
        WorldSnapshot snapshot = Snapshot();
        int before = _intents.ConeHits.Count;

        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            combat.Tick(Frame, now, snapshot, registry.Alive, Facing);
            now += Frame;

            if (_intents.ConeHits.Count > before)
            {
                return now;
            }
        }

        throw new InvalidOperationException(
            $"No damage frame within {MaxFramesPerSwing} ticks. The weapon has stopped swinging — "
                + "check that a living enemy is inside the Censer's reach.");
    }

    /// <summary>One whole swing, from <paramref name="now"/> to its damage frame.</summary>
    private float SwingOnce(PlayerCombat combat, EnemyRegistry registry, float now = 0f) =>
        ToDamageFrame(combat, registry, now);

    /// <summary>
    /// Ticks until a swing is in progress but has not yet landed its damage — the window
    /// <c>Cone_ClampsANegativeRange</c> changes the reach inside.
    /// </summary>
    private float StartSwing(PlayerCombat combat, EnemyRegistry registry)
    {
        WorldSnapshot snapshot = Snapshot();
        float now = 0f;

        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            combat.Tick(Frame, now, snapshot, registry.Alive, Facing);
            now += Frame;

            if (combat.Weapon.IsSwinging && _intents.ConeHits.Count == 0)
            {
                return now;
            }
        }

        throw new InvalidOperationException("The weapon never started a swing.");
    }

    /// <summary>
    /// Presses the dash, lets it start, and reports <paramref name="enemyId"/> as swept through.
    /// </summary>
    private void Dash(PlayerCombat combat, EnemySystem enemies, int enemyId)
    {
        WorldSnapshot snapshot = Snapshot();
        float now = 0f;

        combat.Charge.Request(now);

        // One tick is enough: the press is live, nothing is in flight and the cooldown has never
        // been spent, so the dash starts on the first tick that sees it.
        combat.Tick(Frame, now, snapshot, enemies.Registry.Alive, Facing);

        Assert.That(combat.Charge.IsActive, Is.True, "Sanity: the dash is in flight.");

        // Inside the report window, which is the dash's end plus a tenth of a second.
        combat.ResolveChargeHits(new[] { enemyId }, now + (ChargeDuration * 0.5f), enemies);
    }

    /// <summary>The Oathbound of CC §7, with the Focus ramp switched off.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, SwingsPerSecond, WeaponRange, ConeAngle, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Charge, 10f, ChargeDuration, 2.5f, 0.15f, ChargeDamage, 4f, 0.05f),
        new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill),
        HitIFrames);

    /// <summary>GD §8.1's Husk, Static so that it stands where it was put.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        3.5f,
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
