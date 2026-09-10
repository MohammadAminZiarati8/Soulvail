using System;
using System.Collections.Generic;
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
/// M1-13's seven rules: the stationary clock, the level it is worth, the one modifier that turns
/// that into a faster swing, when the ramp is worth announcing, and what it costs per tick.
/// </summary>
/// <remarks>
/// <para>
/// CC §7's numbers throughout — 0.4 s of delay, a 1.0 s ramp, ×1.3 at the top, against the Censer's
/// 3.0 swings a second — so a failure reads as "the Oathbound stopped ramping the way the design
/// says" rather than as an arithmetic puzzle. One row overrides the multiplier, and says why.
/// </para>
/// <para>
/// <b>Ticks are 10 ms and the clock starts at zero.</b> The boundaries this fixture cares about —
/// 0.4 s for the end of the delay, 1.4 s for the top of the ramp — then land on a tick, so the
/// levels are round numbers and a row that asserts 0.5 at 0.9 s is asserting the shape of the ramp
/// rather than the fixture's rounding. The two event rows tick at their own rates, because what
/// they are about is precisely the relationship between the tick rate and the announcements.
/// </para>
/// <para>
/// <b>The clock is a float sum, and the assertions respect that.</b> Neither 0.05 nor 0.01 is exact
/// in binary, so a tick meant to land on a boundary may land a few ULPs either side of it. Levels
/// are therefore asserted within a tolerance, event counts within a range, and any row that wants
/// the ramp held still arranges it with <see cref="Saturated"/> rather than by ticking to the
/// nominal top. The drift is the arithmetic the game actually runs, not something a test should
/// pretend away.
/// </para>
/// <para>
/// <b>This is CC §4.3's Focus, which is standing still.</b> CC §3.4's tap-to-focus is a different
/// mechanic with the same name and is <c>FocusResolverTests</c>' subject; nothing here touches a
/// target.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FocusTrackerTests
{
    private const string OathboundId = "character.oathbound";

    // CC §7, Attack: "Focus delay / cap / ramp — 0.4 s / 130 % / 1.0 s".
    private const float Delay = 0.4f;
    private const float RampTime = 1f;
    private const float MaxMultiplier = 1.3f;

    /// <summary>The Censer's resting rate, and what the ramp is a multiple of.</summary>
    private const float SwingsPerSecond = 3f;

    /// <summary>Seconds per tick in this fixture. See the class remarks.</summary>
    private const float Step = 0.01f;

    /// <summary>
    /// Seconds of standing still that leave the ramp definitively at the top. A tenth past the
    /// nominal 1.4 s, and the tenth is the point: a float clock summed a hundredth at a time lands
    /// a few ULPs short of 1.4, so ticking exactly to the boundary arranges a level of 0.999999
    /// that saturates on the *next* tick — which is correct behaviour and a terrible fixture, since
    /// every row that then asserts about a stable ramp would be asserting about a moving one.
    /// </summary>
    private const float Saturated = 1.5f;

    /// <summary>Room for the one enemy the blackboard row does not have, and the buffer sizes.</summary>
    private const int EnemyCapacity = 8;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        var fireRate = new Stat(SwingsPerSecond);

        Assert.Throws<ArgumentNullException>(() => new FocusTracker(null, fireRate, _events));
        Assert.Throws<ArgumentNullException>(() => new FocusTracker(Spec(), null, _events));
        Assert.Throws<ArgumentNullException>(() => new FocusTracker(Spec(), fireRate, null));
    }

    [Test]
    public void Spec_Invalid_Throws()
    {
        // A negative delay is a ramp that started before the player stopped; NaN is the same hole
        // every spec in Core/Content closes with the same spelling.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(-0.1f, RampTime, MaxMultiplier));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(float.NaN, RampTime, MaxMultiplier));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(float.PositiveInfinity, RampTime, MaxMultiplier));

        // Zero delay is legal: the ramp begins the instant the stick is released.
        Assert.DoesNotThrow(() => new FocusSpec(0f, RampTime, MaxMultiplier));

        // A zero ramp is a step function rather than a ramp, and a negative one is nothing at all.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, 0f, MaxMultiplier));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, -1f, MaxMultiplier));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, float.NaN, MaxMultiplier));

        // Below 1 would mean standing still makes the character swing slower, which is a modifier's
        // job and not this spec's. Exactly 1 is legal and is how a class says it does not ramp.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, RampTime, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, RampTime, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusSpec(Delay, RampTime, float.PositiveInfinity));
        Assert.DoesNotThrow(() => new FocusSpec(Delay, RampTime, 1f));
    }

    // ---- Rule 2: the clock and the level -------------------------------------------------------

    [Test]
    public void Level_ZeroDuringDelay()
    {
        var tracker = Tracker(out Stat _);

        TickStationary(tracker, 0.39f);

        // The delay is what stops a momentary pause between two dodges from paying out: at 0.39 s
        // the character has stood still for almost the whole of it and earned nothing.
        Assert.That(tracker.Level, Is.Zero);
        Assert.That(tracker.StationaryTime, Is.EqualTo(0.39f).Within(1e-4f));
    }

    [Test]
    public void Level_HalfAtMidRamp()
    {
        var tracker = Tracker(out Stat _);

        // 0.4 s of delay plus half of the 1.0 s ramp.
        TickStationary(tracker, 0.9f);

        Assert.That(tracker.Level, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void Level_FullAfterRamp()
    {
        var tracker = Tracker(out Stat _);

        TickStationary(tracker, Saturated);

        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-4f));

        // And it stops there rather than climbing: the cap is a cap, not a rate.
        TickStationary(tracker, 5f - Saturated);

        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(tracker.StationaryTime, Is.EqualTo(5f).Within(1e-3f),
            "The clock keeps counting past the cap — a later node may read the seconds.");
    }

    [Test]
    public void Moving_CancelsInstantly()
    {
        var tracker = Tracker(out Stat _);

        TickStationary(tracker, Saturated);

        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-4f), "Arranged at full Focus.");

        tracker.Tick(1f / 60f, isMoving: true);

        // CC §4.3's "instant on any movement input", and the whole reason the ramp is a
        // commitment: one frame of stick costs the second that earned it. Not a decay — a reset.
        Assert.That(tracker.Level, Is.Zero);
        Assert.That(tracker.StationaryTime, Is.Zero);
    }

    // ---- Rule 3: the modifier ------------------------------------------------------------------

    [Test]
    public void Modifier_RaisesFireRate()
    {
        var tracker = Tracker(out Stat fireRate);

        TickStationary(tracker, Saturated);

        // 3.0 × 1.3 — the Oathbound at full Focus, and the one number CC §4.3's "130 %" comes to.
        Assert.That(fireRate.Value, Is.EqualTo(3.9f).Within(1e-4f));
        Assert.That(fireRate.ModifierCount, Is.EqualTo(1), "One modifier, replaced rather than stacked.");

        // Half way up: 3.0 × 1.15. Linear in the level, which is what makes the ramp readable as
        // it happens rather than only at the top.
        var half = Tracker(out Stat halfRate);

        TickStationary(half, 0.9f);

        Assert.That(halfRate.Value, Is.EqualTo(3.45f).Within(0.01f));
    }

    [Test]
    public void Modifier_RemovedWhenMoving()
    {
        var tracker = Tracker(out Stat fireRate);

        TickStationary(tracker, Saturated);

        Assert.That(fireRate.ModifierCount, Is.EqualTo(1), "Arranged with the ramp applied.");

        tracker.Tick(1f / 60f, isMoving: true);

        // Nothing sticks. ADR-0008's whole promise is that a source can take its own contribution
        // back off without knowing what else has been added since — a ramp left behind would be a
        // permanent +30 % earned by standing still once.
        Assert.That(fireRate.ModifierCount, Is.Zero);
        Assert.That(fireRate.Value, Is.EqualTo(SwingsPerSecond).Within(1e-4f));
    }

    [Test]
    public void Modifier_IsPercentAdd_SourcedByTracker()
    {
        var tracker = Tracker(out Stat fireRate);

        TickStationary(tracker, Saturated);

        var modifiers = new List<Modifier>();
        fireRate.CopyModifiersTo(modifiers);

        Assert.That(modifiers, Has.Count.EqualTo(1));

        // PercentAdd rather than PercentMult, so that M3-12's "+20 % attack speed" node and a full
        // ramp come to ×1.5 together rather than ×1.56: pooled stacking keeps the tenth source
        // worth what the first was.
        Assert.That(modifiers[0].Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(modifiers[0].Value, Is.EqualTo(0.3f).Within(1e-4f));

        // Sourced by the tracker itself, which is what makes RemoveAll(this) able to find exactly
        // its own contribution and nothing else's.
        Assert.That(modifiers[0].Source, Is.SameAs(tracker));
    }

    [Test]
    public void NoRamp_IsInert()
    {
        var fireRate = new Stat(SwingsPerSecond);
        var tracker = new FocusTracker(Spec(maxMultiplier: 1f), fireRate, _events);

        TickStationary(tracker, 3f);

        // A MaxMultiplier of 1 is how CC §4.3's "if it doesn't feel good, cut it" is spent, and it
        // has to mean the same thing everywhere: no level, so no modifier, no event and no ground
        // glow. A level that climbed while nothing got faster would light a disc under the
        // character to announce a reward that does not exist.
        Assert.That(tracker.Level, Is.Zero);
        Assert.That(fireRate.ModifierCount, Is.Zero);
        Assert.That(fireRate.Value, Is.EqualTo(SwingsPerSecond).Within(1e-4f));
        Assert.That(_events.Count<FocusRampChanged>(), Is.Zero);

        // The clock still runs, because CC §6.4 triggers read it through the blackboard whatever
        // the weapon does with it.
        Assert.That(tracker.StationaryTime, Is.EqualTo(3f).Within(1e-3f));
    }

    // ---- Rule 4: the event ---------------------------------------------------------------------

    [Test]
    public void FocusRampChanged_PublishedOnChange()
    {
        var tracker = Tracker(out Stat _);

        // 0.05 s a tick, so each step of the ramp is 0.05 of a level — five times the epsilon, so
        // every one of the twenty is announced. Thirty ticks is 1.5 s, past the top rather than on
        // it, for the reason Saturated gives.
        for (int i = 0; i < 30; i++)
        {
            tracker.Tick(0.05f, isMoving: false);
        }

        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-6f), "Arranged at the top of the ramp.");

        IReadOnlyList<FocusRampChanged> published = _events.Of<FocusRampChanged>();

        // Twenty steps of 0.05, each five times the epsilon, so each is announced. A range rather
        // than an exact count because the clock is a float sum: 0.05 is not exact in binary, so the
        // two boundaries — the end of the delay and the cap — can each be reached a few ULPs off
        // and add an invisibly small step of their own. That is the arithmetic working, not a rule
        // being broken.
        Assert.That(published, Has.Count.InRange(20, 22));
        Assert.That(published[0].Level, Is.LessThan(0.06f), "The first announcement is at the foot of the ramp.");
        Assert.That(published[^1].Level, Is.EqualTo(1f).Within(1e-6f));

        _events.Clear();

        // Nothing while the level is stable. Sixty events a second describing a number that has
        // not moved is the failure this rule exists to prevent.
        for (int i = 0; i < 60; i++)
        {
            tracker.Tick(Step, isMoving: false);
        }

        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void FocusRampChanged_SmallStepsCoalesce_EndpointsAlwaysAnnounced()
    {
        var tracker = Tracker(out Stat _);

        // 240 Hz: each tick moves the level by 0.004, well under the epsilon, so the ramp cannot be
        // announced tick by tick. This is the rule that matters on a phone — the event count is
        // bounded by how much the number moved, not by how often anyone asked.
        const float FineStep = 1f / 240f;
        const int Ticks = 336;

        for (int i = 0; i < Ticks; i++)
        {
            tracker.Tick(FineStep, isMoving: false);
        }

        int published = _events.Count<FocusRampChanged>();

        Assert.That(published, Is.LessThan(Ticks / 2),
            "A ramp announced once a tick would be 240 events a second describing a number moving by 0.004.");
        Assert.That(published, Is.GreaterThan(50),
            "Coalesced, not silent — the glow still has to climb smoothly.");

        // The top was announced even though the step that reached it was an ordinary one: the two
        // endpoints are exempt from the epsilon, because the glow reads them as "full" and "gone"
        // and cannot infer either.
        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(_events.Of<FocusRampChanged>()[^1].Level, Is.EqualTo(1f).Within(1e-6f));

        _events.Clear();
        tracker.Tick(Step, isMoving: true);

        FocusRampChanged cancelled = _events.Single<FocusRampChanged>();

        Assert.That(cancelled.Level, Is.Zero, "Zero is announced exactly, so the glow goes out.");
    }

    // ---- Rule 5: the blackboard ----------------------------------------------------------------

    [Test]
    public void Blackboard_ReflectsLevel()
    {
        var combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        var snapshot = new WorldSnapshot(EnemyCapacity);

        TickCombat(combat, snapshot, seconds: 0.9f);

        // The blackboard is a mirror, written from the tracker each tick — never a second count of
        // the same thing. ADR-0005's one-line predicate ("am I planted and burning?") reads this.
        Assert.That(combat.Blackboard.FocusRampLevel, Is.EqualTo(combat.Focus.Level).Within(1e-6f));
        Assert.That(combat.Blackboard.FocusRampLevel, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(combat.Blackboard.StationaryTime, Is.EqualTo(combat.Focus.StationaryTime).Within(1e-6f));
        Assert.That(combat.Blackboard.StationaryTime, Is.EqualTo(0.9f).Within(1e-3f));

        // And the weapon it belongs to actually got faster — the round trip M1-13 exists to make.
        Assert.That(combat.Weapon.FireRate.Value, Is.EqualTo(3.45f).Within(0.02f));

        // A moving stick resets both halves of the mirror on the same tick.
        snapshot.MoveInput = new Vector2(0f, 1f);
        combat.Tick(Step, 1f, snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);

        Assert.That(combat.Blackboard.FocusRampLevel, Is.Zero);
        Assert.That(combat.Blackboard.StationaryTime, Is.Zero);
        Assert.That(combat.Weapon.FireRate.Value, Is.EqualTo(SwingsPerSecond).Within(1e-4f));
    }

    [Test]
    public void Reset_ClearsRampAndModifier()
    {
        var combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        var snapshot = new WorldSnapshot(EnemyCapacity);

        TickCombat(combat, snapshot, seconds: Saturated);

        Assert.That(combat.Weapon.FireRate.Value, Is.EqualTo(3.9f).Within(1e-3f), "Arranged at full Focus.");

        _events.Clear();
        combat.Reset();

        // Unlike Weapon.Reset, the tracker does take its modifier off — it is the source, so it is
        // the one thing entitled to decide it should go. Nothing is published: a reset is not a
        // cancellation, and whatever asked for one redraws the view itself.
        Assert.That(combat.Focus.Level, Is.Zero);
        Assert.That(combat.Focus.StationaryTime, Is.Zero);
        Assert.That(combat.Weapon.FireRate.ModifierCount, Is.Zero);
        Assert.That(combat.Weapon.FireRate.Value, Is.EqualTo(SwingsPerSecond).Within(1e-4f));
        Assert.That(_events.All, Is.Empty);

        // And the next ramp is announced from a baseline that matches what is on screen.
        TickCombat(combat, snapshot, seconds: Saturated);

        Assert.That(_events.Of<FocusRampChanged>()[^1].Level, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void NanStick_CountsAsMovement()
    {
        var combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        var snapshot = new WorldSnapshot(EnemyCapacity);

        TickCombat(combat, snapshot, seconds: Saturated);

        Assert.That(combat.Focus.Level, Is.EqualTo(1f).Within(1e-4f), "Arranged at full Focus.");

        snapshot.MoveInput = new Vector2(float.NaN, float.NaN);
        combat.Tick(Step, 2f, snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);

        // The safe direction, and the one the inline clock took before M1-13 moved it: every
        // comparison against NaN is false, so a broken input has to fail *into* movement. A ramp
        // that paid out for an unreadable stick would be a reward for a fault.
        Assert.That(combat.Focus.Level, Is.Zero);
        Assert.That(combat.Focus.StationaryTime, Is.Zero);
    }

    // ---- Rule 7: the cost ----------------------------------------------------------------------

    [Test]
    public void Tick_StableLevel_AllocatesNothing()
    {
        var tracker = Tracker(out Stat _);

        // Warmed to the top of the ramp, which is the state the game spends most of a planted
        // fight in — and the one where Tick has to do nothing at all.
        TickStationary(tracker, Saturated);

        Assert.That(tracker.Level, Is.EqualTo(1f).Within(1e-4f));

        // AllocationAssert rather than a hand-rolled probe: on Unity's Mono
        // GC.GetAllocatedBytesForCurrentThread() is stubbed to zero and would pass code that
        // allocates freely (M0-02). It would also catch a regression that published an event here,
        // since RecordingEvents boxes every payload.
        AllocationAssert.None(() => tracker.Tick(Step, isMoving: false));
    }

    [Test]
    public void Tick_WhileMoving_AllocatesNothing()
    {
        var tracker = Tracker(out Stat _);

        // The other steady state, and the one a player is in most of the time: running, level
        // already zero, nothing to remove and nothing to say.
        tracker.Tick(Step, isMoving: true);

        AllocationAssert.None(() => tracker.Tick(Step, isMoving: true));
    }

    /// <summary>Ticks <paramref name="tracker"/> for <paramref name="seconds"/> with a centred stick.</summary>
    private static void TickStationary(FocusTracker tracker, float seconds)
    {
        int ticks = (int)MathF.Round(seconds / Step);

        for (int i = 0; i < ticks; i++)
        {
            tracker.Tick(Step, isMoving: false);
        }
    }

    /// <summary>
    /// Ticks <paramref name="combat"/> for <paramref name="seconds"/> with an empty arena and
    /// whatever stick <paramref name="snapshot"/> carries.
    /// </summary>
    private static void TickCombat(PlayerCombat combat, WorldSnapshot snapshot, float seconds)
    {
        int ticks = (int)MathF.Round(seconds / Step);

        for (int i = 0; i < ticks; i++)
        {
            combat.Tick(
                Step,
                i * Step,
                snapshot,
                ReadOnlySpan<EnemyAgent>.Empty,
                Vector3.UnitZ);
        }
    }

    /// <summary>A tracker over a fresh Censer fire rate, and that rate for the row to assert on.</summary>
    private FocusTracker Tracker(out Stat fireRate)
    {
        fireRate = new Stat(SwingsPerSecond);

        return new FocusTracker(Spec(), fireRate, _events);
    }

    /// <summary>CC §7's Focus row, with the one number two rows override.</summary>
    private static FocusSpec Spec(
        float delay = Delay,
        float rampTime = RampTime,
        float maxMultiplier = MaxMultiplier)
    {
        return new FocusSpec(delay, rampTime, maxMultiplier);
    }

    /// <summary>The Oathbound of CC §7, ramp and all — this is the fixture that wants the real one.</summary>
    private static CharacterSpec Character() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, SwingsPerSecond, 8f, 60f, 0.4f),
        Spec(),
        // Required as of M1-14, and inert in every row here: nothing in this fixture dashes, and
        // the Charge does not reach the ramp until M1-15 teaches it that a dash counts as movement.
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
