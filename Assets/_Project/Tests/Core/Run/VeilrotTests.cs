using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// GD §10's meter: the number, its four thresholds, the latch at the top and the drain that ends
/// the run it bought.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two fixtures in one file, because they are two depths of one claim</b> (M6-04's Files table).
/// The <c>Meter_</c>, <c>Twenty5_</c>, <c>Fifty_</c>, <c>Seventy5_</c>, <c>Claiming_</c>,
/// <c>Trigger_</c> and <c>Restore_</c> rows are arithmetic over objects with no world around them —
/// a player's stats, a meter and, where a row is about a spawn, an <see cref="EnemySystem"/>.
/// <c>Claiming_PublishesPlayerDied</c> alone drives a whole <see cref="RunSession"/>, which is the only way to assert that
/// a drain step ends the run on the tick it happened: <c>RunState.Rot</c> is <c>internal</c> and this
/// assembly has no <c>InternalsVisibleTo</c> and deliberately never will (AR §18.2).
/// </para>
/// <para>
/// <b>That seal is also why the session row reaches 100 Veilrot through a <em>resume</em>.</b> There
/// is no other door: nothing in the build gains Veilrot until M6-05b's Pacts, and a test cannot call
/// <c>Gain</c> on a live run's meter. Restoring at 100 is therefore both the only route and a second
/// assertion — the save's meter is what puts the run back in the Claiming.
/// </para>
/// <para>
/// <b><c>Restore</c> is reached by reflection</b>, for <c>EssenceWalletTests</c>' reason: it is
/// <c>internal</c>, and opening internals to the test assembly to avoid one <c>MethodInfo</c> is the
/// wrong trade.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VeilrotTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string ModeId = "mode.test";

    /// <summary>The maximum hit points every row gets unless it says otherwise.</summary>
    private const float BaseMaxHp = 200f;

    /// <summary>The Oathbound's authored top speed here — a round number, so ×1.3 is one too.</summary>
    private const float BaseMoveSpeed = 4f;

    /// <summary>The Censer's authored damage here, for the Claiming's ×2.</summary>
    private const float BaseWeaponDamage = 13f;

    /// <summary>The dash's authored cooldown here, for the Claiming's ×0.5.</summary>
    private const float BaseDashCooldown = 2.5f;

    /// <summary>Room for every body a <c>Twenty5_</c> row stands up.</summary>
    private const int Capacity = 16;

    /// <summary>The most bodies the session row's stages may stand up at once.</summary>
    private const int DeviceCap = 8;

    /// <summary>Room for shots nothing here fires; required by <c>RunSession</c> and guarded positive.</summary>
    private const int ProjectileCapacity = 4;

    /// <summary>Deep enough that GD §12.3's speed curve has something to say.</summary>
    private const int Depth = 10;

    /// <summary>60 fps — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 60f;

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private PlayerMotor _motor;
    private PlayerStats _stats;
    private Veilrot _meter;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();

        Build();
    }

    // ---- The meter (rules 1, 3) -------------------------------------------------------------------

    [Test]
    public void Meter_StartsAtZero()
    {
        Assert.That(_meter.Value, Is.Zero);
        Assert.That(_meter.IsClaimed, Is.False);
        Assert.That(_meter.ClaimedFor, Is.Zero);
        Assert.That(_meter.EnemySpeedBonus, Is.Zero);

        Assert.That(_events.All, Is.Empty, "A meter coming into existence is not news.");
    }

    [Test]
    public void Meter_GainAdds()
    {
        _meter.Gain(15f);

        Assert.That(_meter.Value, Is.EqualTo(15f).Within(1e-4f));

        VeilrotChanged changed = _events.Single<VeilrotChanged>();

        // Both numbers, for EssenceChanged's reason: neither can be derived from the other without
        // the reader keeping state.
        Assert.That(changed.Value, Is.EqualTo(15f).Within(1e-4f));
        Assert.That(changed.Delta, Is.EqualTo(15f).Within(1e-4f));

        Assert.That(
            _events.Count<VeilrotThresholdCrossed>(),
            Is.Zero,
            "15 is below every row GD §10.2 has.");
    }

    [Test]
    public void Meter_GainIsSilentForNothing()
    {
        // **EssenceWallet.Earn's rule, and Health.Heal's before it** (rule 1). A movement of nothing
        // is not news, and a reader that had to filter zeroes out of VeilrotChanged would be a
        // reader that could forget to. NaN is refused with the zeroes rather than thrown at, because
        // the guard is spelled as the negated positive and every comparison against NaN is false.
        Assert.DoesNotThrow(() =>
        {
            _meter.Gain(0f);
            _meter.Gain(-5f);
            _meter.Gain(float.NaN);
            _meter.Cleanse(0f);
            _meter.Cleanse(-5f);
            _meter.Cleanse(float.NaN);
        });

        Assert.That(_meter.Value, Is.Zero, "and in particular not −5.");
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Meter_GainClampsAtMax()
    {
        _meter.Gain(85f);

        _events.Clear();

        _meter.Gain(20f);

        // **The clamp is what makes the Claiming reachable by a single +20 Pact from 85** (rule 1)
        // rather than something a run has to hit exactly.
        Assert.That(_meter.Value, Is.EqualTo(Veilrot.Max));

        Assert.That(
            _events.Single<VeilrotChanged>().Delta,
            Is.EqualTo(15f).Within(1e-4f),
            "The delta is what moved, not what was asked for.");

        Assert.That(_meter.IsClaimed, Is.True);
        Assert.That(_events.Count<ClaimingBegan>(), Is.EqualTo(1));
    }

    [Test]
    public void Meter_NeverDecays()
    {
        // GD §10.1: "never decays." Nothing ticks it upward and nothing ticks it down; the only
        // downward verb is Cleanse.
        _meter.Gain(40f);

        _events.Clear();

        for (int i = 0; i < 600; i++)
        {
            Tick(1f);
        }

        Assert.That(_meter.Value, Is.EqualTo(40f).Within(1e-4f));
        Assert.That(_events.All, Is.Empty, "Ten minutes of ticking is not an event.");
    }

    [Test]
    public void Meter_TheThresholdsAreNotAMutableStatic()
    {
        // **The Public API's own amendment, asserted** (M6-00b). The four rows were drafted as
        // `public static readonly float[] Thresholds`, and `readonly` protects the handle rather
        // than the four floats: any caller could write `Thresholds[3] = 5f` and every later
        // comparison in the meter would be wrong for the rest of the session. AR §7 bans static
        // mutable state, and grepped, Soulvail.Core has no `public static readonly` field at all —
        // so this would have been the first, and nothing would have caught it.
        FieldInfo[] statics = typeof(Veilrot).GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (FieldInfo field in statics)
        {
            Assert.That(
                field.IsLiteral,
                Is.True,
                $"Veilrot.{field.Name} is a static field that is not a const. A const is baked "
                    + "into its call sites and cannot be written; anything else is state the whole "
                    + "session shares (AR §7).");
        }

        Assert.That(Veilrot.ThresholdCount, Is.EqualTo(4), "GD §10.2 has four rows.");

        Assert.That(Veilrot.Threshold(0), Is.EqualTo(25f));
        Assert.That(Veilrot.Threshold(1), Is.EqualTo(50f));
        Assert.That(Veilrot.Threshold(2), Is.EqualTo(75f));
        Assert.That(Veilrot.Threshold(3), Is.EqualTo(100f));
        Assert.That(Veilrot.Threshold(3), Is.EqualTo(Veilrot.Max), "…and the last one is the top.");
    }

    [Test]
    public void Meter_ThresholdRefusesAnIndexOutsideIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Veilrot.Threshold(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Veilrot.Threshold(Veilrot.ThresholdCount));
    }

    [Test]
    public void Meter_CleanseTakesAway()
    {
        _meter.Gain(40f);

        _events.Clear();

        _meter.Cleanse(15f);

        Assert.That(_meter.Value, Is.EqualTo(25f).Within(1e-4f));

        VeilrotChanged changed = _events.Single<VeilrotChanged>();

        Assert.That(changed.Value, Is.EqualTo(25f).Within(1e-4f));
        Assert.That(changed.Delta, Is.EqualTo(-15f).Within(1e-4f), "Negative for a cleanse.");
    }

    [Test]
    public void Meter_CleanseClampsAtZero()
    {
        // **A partial cleanse is not refused** (rule 3). Buying a 15-point one at 8 Rot takes the
        // meter to 0 and wastes 7, which is the player's decision and not the model's to prevent;
        // what M6-02b refuses is buying one at 0, where there is nothing to cleanse at all.
        _meter.Gain(8f);

        _events.Clear();

        _meter.Cleanse(15f);

        Assert.That(_meter.Value, Is.Zero);
        Assert.That(_events.Single<VeilrotChanged>().Delta, Is.EqualTo(-8f).Within(1e-4f));

        _events.Clear();

        _meter.Cleanse(15f);

        Assert.That(_events.All, Is.Empty, "Cleansing nothing off nothing is not news either.");
    }

    // ---- The 25 row (rule 4) ----------------------------------------------------------------------

    [Test]
    public void Twenty5_SpeedsWhatSpawnsNext()
    {
        var scaling = new DepthScaling(Scalings.Design());
        EnemySystem enemies = Enemies(scaling);

        _meter.Gain(25f);

        Assert.That(_meter.EnemySpeedBonus, Is.EqualTo(0.05f).Within(1e-6f));

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), Vector3.Zero);

        // **On top of depth scaling, not instead of it** (rule 4): a stage-10 Husk at 50 Veilrot is
        // fast for both reasons, and the two are two modifiers with two sources — which is what lets
        // a debug panel answer "why is this Husk quick?" (ADR-0008).
        var stack = new List<Modifier>();

        husk.MoveSpeed.CopyModifiersTo(stack);

        Assert.That(stack.Count, Is.EqualTo(2), "The depth's modifier and the meter's, and no more.");

        Modifier fromTheMeter = stack.Find(m => ReferenceEquals(m.Source, _meter));

        Assert.That(
            fromTheMeter.Source,
            Is.SameAs(_meter),
            "No modifier on the body is sourced to the meter.");

        Assert.That(fromTheMeter.Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(fromTheMeter.Value, Is.EqualTo(0.05f).Within(1e-6f));

        Assert.That(
            stack.Exists(m => ReferenceEquals(m.Source, scaling)),
            Is.True,
            "…and the depth's is still there beside it.");
    }

    [Test]
    public void Twenty5_LeavesWhatIsStanding()
    {
        // **Rule 4's stated cost.** Walking EnemyRegistry.Alive on the crossing was weighed and
        // refused: it needs the meter to hold the census, or a second call site in RunSession and a
        // method on EnemySystem, for five per cent of one wave's move speed. Veilrot is gained at a
        // level-up, which pauses the run, so the bodies that miss it are the ones already on screen
        // when the player took the Pact — the lag is the rest of the current wave.
        EnemySystem enemies = Enemies(new DepthScaling(Scalings.Design()));

        EnemyAgent standing = enemies.Spawn(new ContentId(HuskId), Vector3.Zero);

        float before = standing.MoveSpeed.Value;

        _meter.Gain(25f);

        Assert.That(standing.MoveSpeed.Value, Is.EqualTo(before), "A body already up does not change pace.");
        Assert.That(standing.MoveSpeed.ModifierCount, Is.EqualTo(1), "…and wears only the depth's.");

        EnemyAgent next = enemies.Spawn(new ContentId(HuskId), Vector3.UnitX);

        Assert.That(next.MoveSpeed.Value, Is.GreaterThan(before), "The next one is faster.");
        Assert.That(next.MoveSpeed.Value, Is.EqualTo(before * 1.05f).Within(1e-3f));
    }

    [Test]
    public void Twenty5_CleansingBelowItSlowsTheNextSpawn()
    {
        // **The three lower states are recomputed from Value rather than latched** (rule 2), which
        // is GD §13.3's whole reason to exist.
        EnemySystem enemies = Enemies(new DepthScaling(Scalings.Design()));

        _meter.Gain(30f);
        _meter.Cleanse(15f);

        Assert.That(_meter.Value, Is.EqualTo(15f).Within(1e-4f));
        Assert.That(_meter.EnemySpeedBonus, Is.Zero);

        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), Vector3.Zero);

        // Absent rather than present-and-zero: a PercentMult of 0 is a factor of 1 and changes
        // nothing, but "does this body carry the Veilrot bonus" should be answerable by looking.
        Assert.That(husk.MoveSpeed.ModifierCount, Is.EqualTo(1), "Only the depth's modifier.");

        var stack = new List<Modifier>();

        husk.MoveSpeed.CopyModifiersTo(stack);

        Assert.That(stack.Exists(m => ReferenceEquals(m.Source, _meter)), Is.False);
    }

    // ---- The 50 row: published, and nothing else --------------------------------------------------

    [Test]
    public void Fifty_IsPublishedAndDoesNothingElse()
    {
        // **GD §10.2's second row names an enemy this game will not have in V1.** The Revenant is
        // GD §8.1's eighth archetype and GD §19 puts it in V2, so the threshold ships crossed,
        // published and otherwise silent — and substituting a shipped archetype was refused rather
        // than forgotten, because a Husk that follows you every stage is a free kill at 50 Veilrot
        // and would make the threshold a reward.
        EnemySystem enemies = Enemies(new DepthScaling(Scalings.Design()));

        _meter.Gain(25f);

        float maxHp = _combat.Health.MaxHp.Value;
        float damage = _combat.Weapon.Damage.Value;
        float speed = _motor.Speed.Value;
        float cooldown = _combat.Charge.Cooldown.Value;
        float enemyBonus = _meter.EnemySpeedBonus;

        _events.Clear();

        _meter.Gain(25f);

        Assert.That(_meter.Value, Is.EqualTo(50f).Within(1e-4f));

        VeilrotThresholdCrossed crossed = _events.Single<VeilrotThresholdCrossed>();

        Assert.That(crossed.Threshold, Is.EqualTo(50f));
        Assert.That(crossed.Entered, Is.True);

        // The movement and the crossing, and nothing else at all: no spawn, no stat moved, and in
        // particular no second event standing in for a stalker nobody has built.
        Assert.That(_events.All.Count, Is.EqualTo(2));
        Assert.That(_events.Count<VeilrotChanged>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
        Assert.That(enemies.Registry.AliveCount, Is.Zero);

        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(maxHp));
        Assert.That(_combat.Weapon.Damage.Value, Is.EqualTo(damage));
        Assert.That(_motor.Speed.Value, Is.EqualTo(speed));
        Assert.That(_combat.Charge.Cooldown.Value, Is.EqualTo(cooldown));
        Assert.That(_meter.EnemySpeedBonus, Is.EqualTo(enemyBonus), "…including the 25 row's.");
    }

    // ---- The 75 row (rule 5) ----------------------------------------------------------------------

    [Test]
    public void Seventy5_TakesAFifthOfWhateverYouBuilt()
    {
        _meter.Gain(75f);

        Assert.That(
            _combat.Health.MaxHp.Value,
            Is.EqualTo(0.8f * BaseMaxHp).Within(1e-3f),
            "×0.8 of 200.");

        var stack = new List<Modifier>();

        _combat.Health.MaxHp.CopyModifiersTo(stack);

        Assert.That(stack.Count, Is.EqualTo(1));
        Assert.That(stack[0].Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(stack[0].Value, Is.EqualTo(-0.2f).Within(1e-6f));
        Assert.That(stack[0].Source, Is.SameAs(_meter));

        // **And this is why it is a PercentMult** (rule 5). Pooled additively against a +40 % node
        // the two would come to 200 × (1 + 0.4 − 0.2) = 240, and the threshold would silently do
        // four fifths of nothing for a run that had taken one. As its own factor it is a true fifth
        // of whatever the player has built: 200 × 1.4 × 0.8 = 224.
        _combat.Health.MaxHp.Add(new Modifier(ModifierKind.PercentAdd, 0.4f, new object()));

        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(224f).Within(1e-3f));
        Assert.That(_combat.Health.MaxHp.Value, Is.Not.EqualTo(240f).Within(1e-3f));
    }

    [Test]
    public void Seventy5_PullsCurrentHpDownWithIt()
    {
        // Health.OnMaxHpChanged's rule, asserted here because the Claiming and this row are its
        // first callers: nothing in V1 removed maximum hit points until now.
        Assert.That(_combat.Health.Current, Is.EqualTo(BaseMaxHp).Within(1e-4f), "Sanity: full.");

        _meter.Gain(75f);

        Assert.That(_combat.Health.Current, Is.EqualTo(160f).Within(1e-3f), "…and not 200.");
        Assert.That(_combat.Health.IsDead, Is.False);
    }

    [Test]
    public void Seventy5_IsGivenBackByCleansing()
    {
        _meter.Gain(78f);
        _meter.Cleanse(15f);

        Assert.That(_meter.Value, Is.EqualTo(63f).Within(1e-4f));

        Assert.That(
            _combat.Health.MaxHp.Value,
            Is.EqualTo(BaseMaxHp).Within(1e-3f),
            "The maximum comes back with the threshold.");

        Assert.That(_combat.Health.MaxHp.ModifierCount, Is.Zero);

        // **A lost maximum is not a heal.** Health raises no hit points when the maximum rises —
        // otherwise a buff cycled on and off would be a free heal each time.
        Assert.That(_combat.Health.Current, Is.EqualTo(160f).Within(1e-3f));

        // The event carries a direction, because Cleanse goes back down: 75 was entered on the way
        // up and left on the way back, and nothing else was.
        IReadOnlyList<VeilrotThresholdCrossed> crossings = _events.Of<VeilrotThresholdCrossed>();

        Assert.That(crossings.Count, Is.EqualTo(4), "25, 50 and 75 entered, then 75 left.");
        Assert.That(crossings[3].Threshold, Is.EqualTo(75f));
        Assert.That(crossings[3].Entered, Is.False);
    }

    // ---- The Claiming (rules 6, 7, 8) -------------------------------------------------------------

    [Test]
    public void Claiming_PutsOnAllFour()
    {
        _meter.Gain(Veilrot.Max);

        Assert.That(_meter.IsClaimed, Is.True);

        Assert.That(
            _combat.Weapon.Damage.Value,
            Is.EqualTo(2f * BaseWeaponDamage).Within(1e-3f),
            "+100 % damage.");

        Assert.That(
            _motor.Speed.Value,
            Is.EqualTo(1.3f * BaseMoveSpeed).Within(1e-3f),
            "+30 % move speed.");

        Assert.That(
            _combat.Charge.Cooldown.Value,
            Is.EqualTo(0.5f * BaseDashCooldown).Within(1e-3f),
            "…and the dash cooldown halved.");

        ClaimingBegan began = _events.Single<ClaimingBegan>();

        // The post-buff maximum, which at 100 Veilrot means with the 75 row's −20 % already on it:
        // the drain is 1 % of *this* number per second, so a bar drawing the countdown needs the
        // number the countdown is against.
        Assert.That(began.MaxHpAtClaiming, Is.EqualTo(0.8f * BaseMaxHp).Within(1e-3f));

        // …and it arrives after the crossing that caused it (rule 6).
        Assert.That(
            IndexOf<ClaimingBegan>(),
            Is.GreaterThan(IndexOfCrossing(Veilrot.Max)),
            "ClaimingBegan was published before the 100 crossing.");
    }

    [Test]
    public void Claiming_FiresExactlyOnce()
    {
        _meter.Gain(Veilrot.Max);
        _meter.Gain(20f);

        Assert.That(_meter.Value, Is.EqualTo(Veilrot.Max));
        Assert.That(_events.Count<ClaimingBegan>(), Is.EqualTo(1));

        // Four modifiers: the 75 row's on the maximum and the Claiming's three. Not eight.
        Assert.That(_combat.Health.MaxHp.ModifierCount, Is.EqualTo(1));
        Assert.That(_combat.Weapon.Damage.ModifierCount, Is.EqualTo(1));
        Assert.That(_motor.Speed.ModifierCount, Is.EqualTo(1));
        Assert.That(_combat.Charge.Cooldown.ModifierCount, Is.EqualTo(1));

        // A gain of 20 at 100 moves nothing, so it says nothing either — VeilrotChanged.Delta is
        // never zero (rule 1).
        Assert.That(_events.Count<VeilrotChanged>(), Is.EqualTo(1));
    }

    [Test]
    public void Claiming_SurvivesCleansing()
    {
        // **The one state combination that looks like a bug and is not** (rule 6). GD §10.2 says
        // the Claiming lasts "until you die", so cleansing is a way to survive the drain rather than
        // a way to give the power back.
        //
        // **The spec's row says the 25 state is off at 40 and it cannot be**: 40 is above 25, so the
        // first row is still entered. What it means is that the rows the meter has fallen out of come
        // off as it passes them, which is what the second cleanse below shows — see *As built*.
        _meter.Gain(Veilrot.Max);

        _meter.Cleanse(60f);

        Assert.That(_meter.Value, Is.EqualTo(40f).Within(1e-4f));
        Assert.That(_meter.IsClaimed, Is.True, "The latch does not open.");

        Assert.That(_combat.Weapon.Damage.Value, Is.EqualTo(2f * BaseWeaponDamage).Within(1e-3f));
        Assert.That(_motor.Speed.Value, Is.EqualTo(1.3f * BaseMoveSpeed).Within(1e-3f));
        Assert.That(_combat.Charge.Cooldown.Value, Is.EqualTo(0.5f * BaseDashCooldown).Within(1e-3f));

        Assert.That(
            _combat.Health.MaxHp.Value,
            Is.EqualTo(BaseMaxHp).Within(1e-3f),
            "…and the 75 row is off, because it is not a latch.");

        Assert.That(_meter.EnemySpeedBonus, Is.EqualTo(0.05f).Within(1e-6f), "40 is still above 25.");

        _meter.Cleanse(20f);

        Assert.That(_meter.Value, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(_meter.EnemySpeedBonus, Is.Zero, "…and now it is not.");
        Assert.That(_meter.IsClaimed, Is.True, "The latch still does not open.");
    }

    [Test]
    public void Claiming_DrainsAWholePercentPerSecond()
    {
        // 250 base, so the maximum at the Claiming is 200 with the 75 row's fifth already gone.
        Build(maxHp: 250f);

        _meter.Gain(Veilrot.Max);

        Assert.That(
            _events.Single<ClaimingBegan>().MaxHpAtClaiming,
            Is.EqualTo(200f).Within(1e-3f),
            "Sanity: 250 × 0.8.");

        Tick(1f);

        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(198f).Within(1e-2f));

        Drain(9f);

        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(180f).Within(1e-2f));

        Drain(40f);

        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(100f).Within(1e-2f));

        // A fixed denominator, never a compounding one: 1 % of the *current* maximum is asymptotic
        // and would never kill anybody, which is not what GD §10.3's ninety seconds means.
        Assert.That(_meter.ClaimedFor, Is.EqualTo(50f).Within(1e-2f));
    }

    [Test]
    public void Claiming_StepsOncePerSecondNotPerFrame()
    {
        _meter.Gain(Veilrot.Max);

        var changes = 0;

        _combat.Health.MaxHp.Changed += _ => changes++;

        for (int i = 0; i < 60; i++)
        {
            Tick(Frame);
        }

        // **Once per whole second, not once per frame** (rule 7). Per-frame rewriting would raise
        // Stat.Changed sixty times a second on the one stat the HUD is subscribed to, for a step
        // GD §10.2 states in seconds.
        //
        // One rather than none is also what the meter's second-tolerance buys: sixty additions of
        // 1f/60f come to 0.9999997, so a bare `>=` would lose the step a whole second had earned —
        // and, sixty seconds later, lose the run's last one.
        Assert.That(changes, Is.EqualTo(1));

        Assert.That(
            _combat.Health.MaxHp.Value,
            Is.EqualTo(0.8f * BaseMaxHp * 0.99f).Within(1e-2f),
            "…and the one step it took is worth exactly one per cent.");
    }

    [Test]
    public void Claiming_ReachesZeroInAHundredSeconds()
    {
        // GD §10.3's "90 seconds of godhood to push two more stages", bounded: −1 % of the starting
        // maximum per second reaches nothing in a hundred, whatever the maximum was.
        _meter.Gain(Veilrot.Max);

        Drain(99f);

        Assert.That(_combat.Health.MaxHp.Value, Is.GreaterThan(0f), "Sanity: still alive at 99.");
        Assert.That(_combat.Health.IsDead, Is.False);

        Drain(1f);

        Assert.That(_combat.Health.MaxHp.Value, Is.Zero);
        Assert.That(_combat.Health.Current, Is.Zero);
        Assert.That(_combat.Health.IsDead, Is.True);
    }

    [Test]
    public void Claiming_MultipliesWithTheSeventyFive()
    {
        // The drain multiplies with rule 5's −20 % rather than replacing it, so a Claimed run at
        // 100 Rot is on ×0.8 × (1 − 0.01 t).
        _meter.Gain(Veilrot.Max);

        Drain(50f);

        Assert.That(
            _combat.Health.MaxHp.Value,
            Is.EqualTo(0.8f * 0.5f * BaseMaxHp).Within(1e-2f),
            "0.8 × 0.5 × 200 = 80.");

        Assert.That(_combat.Health.MaxHp.ModifierCount, Is.EqualTo(2), "The threshold's and the drain's.");
    }

    [Test]
    public void Claiming_DoesNotPublishTwiceIfDamageGotThereFirst()
    {
        // **One publisher, one flag, one place** (rule 8). A second publisher with its own flag would
        // make "exactly once per life" a property of two objects agreeing.
        _meter.Gain(Veilrot.Max);

        _combat.ApplyDamage(10_000f, _now);

        Assert.That(_combat.IsDead, Is.True);
        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1), "Sanity: the Husk got there first.");

        Drain(3f);

        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));
    }

    // ---- The blackboard, and the clause it finally lets fire (rule 10) -----------------------------

    [Test]
    public void Trigger_VeilrotIsWrittenEveryTick()
    {
        Assert.That(_combat.Blackboard.Veilrot, Is.Zero, "Sanity: nothing has been written yet.");

        _meter.Gain(30f);

        Assert.That(
            _combat.Blackboard.Veilrot,
            Is.Zero,
            "A gain is not a tick — the field is written by the one thing that owns it, once a "
                + "frame, the way ProjectileSystem writes IncomingProjectiles.");

        Tick(Frame);

        Assert.That(_combat.Blackboard.Veilrot, Is.EqualTo(30f).Within(1e-4f));

        _meter.Cleanse(10f);

        Tick(Frame);

        Assert.That(_combat.Blackboard.Veilrot, Is.EqualTo(20f).Within(1e-4f));
    }

    [Test]
    public void Trigger_VeilrotClauseNowFires()
    {
        // **The clause M5-06b rule 6 refused to author**, and the entry whose absence was the whole
        // value of ContentValidationTests.Written. CH §4.2's Rot Nova is "Veilrot ≥ 50 and ≥ 4
        // enemies within 8 m"; M7-04 is what authors it, and this is the half that had no writer.
        var clause = new TriggerClause(TriggerField.Veilrot, TriggerComparison.AtLeast, 50f);

        _meter.Gain(49f);

        Tick(Frame);

        Assert.That(clause.IsMet(_combat.Blackboard), Is.False);

        _meter.Gain(1f);

        Tick(Frame);

        Assert.That(clause.IsMet(_combat.Blackboard), Is.True);
    }

    // ---- What a resumed run comes back as (rule 9) -------------------------------------------------

    [Test]
    public void Restore_ComesBackAtItsValueAndSilently()
    {
        Restore(_meter, 78f);

        Assert.That(_meter.Value, Is.EqualTo(78f).Within(1e-4f));
        Assert.That(_meter.IsClaimed, Is.False);

        // Every state the value implies, applied without a single crossing being announced.
        Assert.That(_meter.EnemySpeedBonus, Is.EqualTo(0.05f).Within(1e-6f), "The 25 row is on.");
        Assert.That(_combat.Health.MaxHp.Value, Is.EqualTo(0.8f * BaseMaxHp).Within(1e-3f), "…and the 75.");

        Assert.That(
            _events.All,
            Is.Empty,
            "A resume is not news, and a ClaimingBegan published inside RunSession.Start would "
                + "reach a HUD that has not subscribed yet (EssenceWallet.Restore's rule).");
    }

    [Test]
    public void Restore_AtAHundredComesBackClaimed()
    {
        Restore(_meter, Veilrot.Max);

        Assert.That(_meter.IsClaimed, Is.True);

        Assert.That(_combat.Health.MaxHp.ModifierCount, Is.EqualTo(1));
        Assert.That(_combat.Weapon.Damage.ModifierCount, Is.EqualTo(1));
        Assert.That(_motor.Speed.ModifierCount, Is.EqualTo(1));
        Assert.That(_combat.Charge.Cooldown.ModifierCount, Is.EqualTo(1));

        // **Rule 9's stated cost.** Nothing on disk carries ClaimedFor — v4 cut the field — so the
        // drain restarts and a Continue taken at 100 Veilrot is worth up to a hundred seconds. It is
        // recorded rather than discovered, and it is smaller than the free cooldown reset the same
        // Continue already grants.
        Assert.That(_meter.ClaimedFor, Is.Zero);

        Assert.That(_events.All, Is.Empty);
    }

    // ---- The whole run (rules 8, 9) ---------------------------------------------------------------

    [Test]
    public void Claiming_PublishesPlayerDied()
    {
        // **The only row here that needs a session**, and the only way into one at 100 Veilrot: the
        // meter is internal on RunState, nothing in the build gains any until M6-05b's Pacts, and a
        // resume is therefore both the route and a second assertion.
        RunSession session = Session();

        session.Start(ResumedConfig());

        Assert.That(session.State.IsClaimed, Is.True, "The save's meter put the run back in it.");
        Assert.That(session.State.Veilrot, Is.EqualTo(Veilrot.Max));

        // The saved hit points are absolute and meet a maximum the meter has already taken a fifth
        // of — which is what AR §18.1 puts this restore above Health.Restore for.
        Assert.That(session.State.PlayerMaxHp, Is.EqualTo(0.8f * BaseMaxHp).Within(1e-3f));
        Assert.That(session.State.PlayerHp, Is.EqualTo(0.8f * BaseMaxHp).Within(1e-3f));

        Assert.That(_events.Count<ClaimingBegan>(), Is.Zero, "…and silently.");

        var ticks = 0;

        while (ticks < 600 && _events.Count<PlayerDied>() == 0)
        {
            session.Tick(SessionSnapshot(0.5f));

            ticks++;
        }

        // Exactly one PlayerDied, from the one publisher, for a death no DamageResult described.
        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));

        // …and the run ended on that same tick: the loop stopped the instant the death was
        // published, so a RunEnded in the log now is one from this tick and not the next.
        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1));
        Assert.That(session.IsRunning, Is.False);

        Assert.That(
            session.State.Time,
            Is.EqualTo(100f).Within(1f),
            "A hundred seconds of godhood, near enough to see it was the drain that did it.");
    }

    // ---- Guards ------------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        CombatBlackboard blackboard = _combat.Blackboard;

        Assert.Throws<ArgumentNullException>(() => new Veilrot(null, _combat, blackboard, _events));
        Assert.Throws<ArgumentNullException>(() => new Veilrot(_stats, null, blackboard, _events));
        Assert.Throws<ArgumentNullException>(() => new Veilrot(_stats, _combat, null, _events));
        Assert.Throws<ArgumentNullException>(() => new Veilrot(_stats, _combat, blackboard, null));
    }

    [Test]
    public void Tick_RefusesABadStep()
    {
        // A negative step would rewind the drain and hand the maximum back; a non-finite one would
        // put NaN into ClaimedFor and stop every later comparison against it from ever being true,
        // with nothing logged.
        Assert.Throws<ArgumentOutOfRangeException>(() => _meter.Tick(-0.1f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _meter.Tick(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _meter.Tick(float.PositiveInfinity, 0f));

        Assert.DoesNotThrow(() => _meter.Tick(0f, 0f), "A frame of no time is a legal frame.");
    }

    // ---- Rule 11 -----------------------------------------------------------------------------------

    [Test]
    public void Meter_AllocatesNothing()
    {
        // SilentEvents rather than the fixture's recorder: RecordingEvents stores each payload in a
        // List<object>, which boxes the struct and would report the fake's allocation as the meter's.
        Build(events: new SilentEvents());

        _meter.Gain(Veilrot.Max);

        var now = 0f;

        // Ten thousand iterations of four verbs: a cleanse that crosses back out of the top row, a
        // gain that crosses back into it, and a tick that takes a whole drain step — so the
        // Stat.RemoveAll/Add pair is inside the measurement for the first hundred of them and the
        // crossings are inside all ten thousand.
        AllocationAssert.None(() =>
        {
            _meter.Cleanse(1f);
            _meter.Gain(1f);

            now += 1f;

            _meter.Tick(1f, now);
        });
    }

    // ---- Fixture: the player and the meter ----------------------------------------------------------

    /// <summary>
    /// A player, its address table and a meter over them — everything the meter can reach.
    /// </summary>
    private void Build(float maxHp = BaseMaxHp, IDomainEvents events = null)
    {
        IDomainEvents sink = events ?? _events;

        CharacterSpec character = Oathbound(maxHp);

        _combat = new PlayerCombat(character, sink, new RecordingIntents(), Capacity);
        _motor = new PlayerMotor(character.Movement, Vector3.UnitZ);
        _stats = new PlayerStats(_combat, _motor, new LevelTracker(Scalings.Xp(), sink));
        _meter = new Veilrot(_stats, _combat, _combat.Blackboard, sink);
        _now = 0f;
    }

    /// <summary>One frame of <paramref name="dt"/> seconds, on the fixture's own clock.</summary>
    private void Tick(float dt)
    {
        _now += dt;

        _meter.Tick(dt, _now);
    }

    /// <summary><paramref name="seconds"/> of the Claiming, a whole second at a time.</summary>
    private void Drain(float seconds)
    {
        for (var i = 0; i < (int)seconds; i++)
        {
            Tick(1f);
        }
    }

    /// <summary>An <see cref="EnemySystem"/> wired to the fixture's meter.</summary>
    private EnemySystem Enemies(DepthScaling scaling) => new EnemySystem(
        Catalog(),
        _events,
        new FixedRandom(0),
        scaling,
        Capacity,
        veilrot: _meter)
    {
        Depth = Depth,
    };

    /// <summary>
    /// <c>Restore</c> is internal; this assembly has no access, so it goes through reflection.
    /// </summary>
    /// <remarks>
    /// Reflection rather than <c>InternalsVisibleTo</c>, which AR §18.2 says
    /// <c>Soulvail.Tests.Core</c> does not have and never will — <c>EssenceWalletTests</c>' route to
    /// <c>EssenceWallet.Restore</c>, written the same way.
    /// </remarks>
    private static void Restore(Veilrot meter, float value)
    {
        MethodInfo method = typeof(Veilrot).GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "Veilrot.Restore has gone.");

        try
        {
            method.Invoke(meter, new object[] { value });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException!;
        }
    }

    /// <summary>Where in the log the first event of type <typeparamref name="T"/> sits.</summary>
    private int IndexOf<T>()
        where T : struct
    {
        for (var i = 0; i < _events.All.Count; i++)
        {
            if (_events.All[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Where in the log the crossing of <paramref name="threshold"/> sits.</summary>
    private int IndexOfCrossing(float threshold)
    {
        for (var i = 0; i < _events.All.Count; i++)
        {
            if (_events.All[i] is VeilrotThresholdCrossed crossed && crossed.Threshold == threshold)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- Fixture: a run -----------------------------------------------------------------------------

    /// <summary>A whole run, for the one row that is about what a tick does.</summary>
    private RunSession Session() => new RunSession(
        Catalog(),
        new FixedRandom(0),
        _events,
        new RecordingIntents(),
        new RunRecorder(new FixedRandom(0), new FixedClock(default), _events),
        Capacity,
        DeviceCap,
        ProjectileCapacity);

    /// <summary>A run resumed at 100 Veilrot and full hit points — the only door into the Claiming.</summary>
    private static RunConfig ResumedConfig() => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        1,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(ModeId),
            new ContentId(OathboundId),
            0,
            1,
            new RandomState(101, 102, 103, 104, 105),

            // Full against the base, so what comes back is what the meter's fifth leaves of it.
            BaseMaxHp,
            0f,
            0f,
            default,
            1,
            0f,
            0,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            new RunEconomy(0, Veilrot.Max, 0, 0),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>()));

    private static WorldSnapshot SessionSnapshot(float dt) => new WorldSnapshot(Capacity)
    {
        Dt = dt,
        PlayerPosition = Vector3.Zero,
        SpawnPoints = Array.Empty<Vector3>(),
    };

    // ---- Fixture: content ---------------------------------------------------------------------------

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound(BaseMaxHp) },
        new[] { Husk() },
        new[] { Mode() });

    /// <summary>
    /// A mode with an empty roster, so the session row composes nothing and its stage never clears.
    /// </summary>
    /// <remarks>
    /// The row is about a hundred seconds of drain in an arena, and a schedule would fill that arena
    /// with bodies whose only contribution would be noise in the log.
    /// </remarks>
    private static ModeSpec Mode() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    /// <summary>
    /// The class every row plays: CC §7's Oathbound with three numbers rounded, so that ×2, ×1.3 and
    /// ×0.5 are numbers a reader can check in their head.
    /// </summary>
    /// <remarks>
    /// No Aegis, because a shield refilling through a hundred seconds of drain is background in every
    /// row here and noise in two of them. The Focus ramp is switched off with a maximum of 1, for
    /// <c>PlayerCombatTests</c>' reason: it would put a modifier and a stream of events into fixtures
    /// measuring neither.
    /// </remarks>
    private static CharacterSpec Oathbound(float maxHp) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        maxHp,
        new MovementSpec(BaseMoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, BaseWeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Charge,
            10f,
            0.22f,
            BaseDashCooldown,
            0.15f,
            20f,
            5f,
            0.05f));

    /// <summary>GD §8.1's Husk, authored <c>Static</c>: these rows are about a spawn, not a fight.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: 3.5f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);
}
