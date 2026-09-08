using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M1-04 spec's eight rules, plus rows for the two guards this task decided on rather than
/// inherited: the constructor's arguments and the finiteness of <c>dt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Oathbound's targeting numbers throughout — range 12, distance weight 3, elite 2, finisher
/// 1, hysteresis 1.5, cadence 0.1 (CC §7) — because they are what <c>Oathbound.asset</c> ships
/// and what M1-03's scorer was pinned against. Most candidates sit exactly on the acquire range,
/// where the distance term is exactly zero and a score is its priority alone; that is what lets
/// these rows state their arithmetic in whole numbers and keeps them about <em>timing</em>, which
/// is what this class owns, rather than about scoring, which is already covered next door in
/// <see cref="TargetScorerTests"/>.
/// </para>
/// <para>
/// A challenger is made to win by more than the 1.5 hysteresis margin wherever a row is about
/// something other than hysteresis, so no assertion here can pass or fail on that margin by
/// accident.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TargeterTests
{
    private const float AcquireRange = 12f;
    private const float Cadence = 0.1f;

    /// <summary>CC §7's focus drop delay, mirrored here so the rows can name it.</summary>
    private const float FocusDropDelay = 2f;

    /// <summary>Where the allocation test parks what it reads, so the calls cannot be elided.</summary>
    private int _sink;

    // ---- Rule 1: the cadence ------------------------------------------------------------------

    [Test]
    public void Tick_BeforeCadence_DoesNotReselect()
    {
        Targeter targeter = Fresh();

        // The first tick acquires: with no target at all there is nothing to hold, so rule 1's
        // immediate path fires and the accumulator stays where it is. Every row below starts this
        // way.
        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        // A clearly better target appears. The incumbent is still alive, in range and vulnerable,
        // so nothing invalidates it and the decision has to wait for the schedule — CC §3.1's
        // 10 Hz loop, which is the whole reason this class exists rather than the scorer being
        // called per frame.
        targeter.Tick(Cadence / 2f, Pair(Weak, Strong), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));
        Assert.That(targeter.ChangedThisTick, Is.False);
    }

    [Test]
    public void Tick_AtCadence_Reselects()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);
        targeter.Tick(Cadence / 2f, Pair(Weak, Strong), dpsOneSecond: 0f);

        // Two halves of the cadence reach it exactly — doubling is exact in binary floats — and
        // the comparison is inclusive, so this is the first tick that may decide.
        targeter.Tick(Cadence / 2f, Pair(Weak, Strong), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(StrongId));
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    [Test]
    public void ImmediateRetarget_DoesNotStarveTheSchedule()
    {
        // Beyond the spec's table, and the one semantic M1-04 had to choose rather than read: an
        // immediate re-selection leaves the accumulator alone. The alternative — resetting it on
        // every immediate retarget — lets a stream of dying targets postpone the scheduled
        // decision for ever, so the character would only ever answer "what died" and never "what
        // is best".
        Targeter targeter = Fresh();

        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);

        // Half a cadence banked...
        targeter.Tick(Cadence * 0.5f, One(Weak), dpsOneSecond: 0f);

        // ...then Weak dies, which forces an immediate re-selection two tenths of a cadence
        // later. The accumulator now stands at 0.7 of a cadence if the immediate path left it
        // alone, and at 0.2 if it reset it.
        targeter.Tick(Cadence * 0.2f, One(Decoy), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(DecoyId), "Sanity: the retarget happened.");

        // Four tenths more passes the cadence on the first reading and falls well short of it on
        // the second. Decoy is present and valid throughout this tick, so nothing here can fire
        // the immediate path — only the schedule can move the target.
        targeter.Tick(Cadence * 0.4f, Pair(Decoy, Strong), dpsOneSecond: 0f);

        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(StrongId),
            "The scheduled decision was due and must not have been pushed back by the retarget.");
    }

    [Test]
    public void CurrentDies_ImmediateRetarget()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        // Absent from the span is what death looks like from here: the caller gathers the living
        // (CC §3.1 step 2), so a corpse simply stops arriving. A tenth of the cadence later, the
        // new target is already chosen.
        targeter.Tick(Cadence / 10f, One(Decoy), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(DecoyId));
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    [Test]
    public void CurrentOutOfRange_ImmediateRetarget()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);

        // Weak is still there, still alive, still vulnerable — and one metre past the acquire
        // range, which CC §3.3 lists as one of the three reasons not to wait.
        TargetCandidate fled = Candidate(WeakId, distance: AcquireRange + 1f, priority: 8);

        targeter.Tick(Cadence / 10f, Pair(fled, Decoy), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(DecoyId));
        Assert.That(targeter.IsCurrentBlocked, Is.False);
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    [Test]
    public void CurrentBecomesInvulnerable_ImmediateRetarget()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);

        // A Warden turning to face the player. It still out-scores everything present, and is
        // dropped anyway: CC §3.6's rule is a filter, not a weight.
        TargetCandidate shielded = Candidate(WeakId, AcquireRange, priority: 8, isVulnerable: false);

        targeter.Tick(Cadence / 10f, Pair(shielded, Decoy), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(DecoyId));
        Assert.That(
            targeter.IsCurrentBlocked,
            Is.False,
            "Something damageable was available, so this is an ordinary retarget, not the blocked state.");
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    // ---- Rule 2: focus overrides scoring ------------------------------------------------------

    [Test]
    public void Focus_OverridesScore()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, Pair(Weak, Strong), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(StrongId), "Scoring picks the better one.");

        targeter.Focus(WeakId);

        // Rule 5: the command is queued, not applied. Between ticks this object has no candidate
        // span and therefore no way to know whether that id is alive, in range, or real.
        Assert.That(targeter.HasFocus, Is.False);
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(-1));

        // And it lands on the very next tick rather than waiting for the cadence — a tap that
        // took up to 100 ms to show a ring would read as the game ignoring it.
        targeter.Tick(Cadence / 10f, Pair(Weak, Strong), dpsOneSecond: 0f);

        Assert.That(targeter.FocusedTargetId, Is.EqualTo(WeakId));
        Assert.That(targeter.HasFocus, Is.True);
        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(WeakId),
            "CC §3.4: the player's tap ignores scoring entirely.");
        Assert.That(targeter.IsCurrentBlocked, Is.False);
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    [Test]
    public void ClearFocus_ReturnsToScoring()
    {
        Targeter targeter = Fresh();

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(Weak, Strong), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        targeter.ClearFocus();
        targeter.Tick(Cadence / 10f, Pair(Weak, Strong), dpsOneSecond: 0f);

        // Strong leads by 5 against a margin of 1.5, so this row says "scoring decides again"
        // without also taking a position on whether the unfocused incumbent keeps its hysteresis.
        // It does — rule 4 names Current as the incumbent unconditionally, and unfocusing is not
        // one of CC §3.3's three reasons to skip the margin.
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(-1));
        Assert.That(targeter.HasFocus, Is.False);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(StrongId));
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    // ---- Rule 3: when focus drops, and when it does not ---------------------------------------

    [Test]
    public void Focus_DropsWhenFocusedDies()
    {
        Targeter targeter = Fresh();

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        targeter.Tick(Cadence / 10f, One(Decoy), dpsOneSecond: 0f);

        Assert.That(targeter.HasFocus, Is.False);
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(-1));
        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(DecoyId),
            "Scoring takes over in the same tick — a dead focus must not leave the gun idle.");
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    [Test]
    public void Focus_DropsAfterTwoSecondsOutOfRange()
    {
        Targeter targeter = Fresh();

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        TargetCandidate fled = Candidate(WeakId, distance: 15f, priority: WeakPriority);

        // 1.9 s outside the range. The focus is held the whole way — CC §3.4's window is long
        // enough that chasing a target through a pack, or Charging past it, does not lose it.
        TickFor(targeter, seconds: FocusDropDelay - 0.1f, fled, Decoy);

        Assert.That(targeter.HasFocus, Is.True);
        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(DecoyId),
            "An out-of-range focus stands down from selection while it is still held: the player " +
            "keeps the target, the gun does not stand idle.");

        TickFor(targeter, seconds: 0.2f, fled, Decoy);

        Assert.That(targeter.HasFocus, Is.False);
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(-1));
    }

    [Test]
    public void Focus_OutOfRangeTimer_ResetsWhenBackInRange()
    {
        Targeter targeter = Fresh();

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(Weak, Decoy), dpsOneSecond: 0f);

        TargetCandidate fled = Candidate(WeakId, distance: 15f, priority: WeakPriority);

        // 3 s outside the range in total, and never 2 s of it consecutively. Rule 3's window is
        // continuous, not cumulative: a target that ducks in and out of range is not one the
        // player has abandoned.
        TickFor(targeter, seconds: 1.5f, fled, Decoy);
        Assert.That(targeter.HasFocus, Is.True);

        TickFor(targeter, seconds: 0.1f, Weak, Decoy);

        TickFor(targeter, seconds: 1.5f, fled, Decoy);

        Assert.That(targeter.HasFocus, Is.True);
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(WeakId));
    }

    [Test]
    public void Focus_Invulnerable_ShowsBlockedOnFocused()
    {
        Targeter targeter = Fresh();

        // The focused enemy is undamageable, and the alternative is both nearer and higher
        // scoring — so every route other than "hold the focus" would visibly pick Strong, whether
        // by scoring or by the nearest-target rule.
        TargetCandidate shielded = Candidate(WeakId, AcquireRange, WeakPriority, isVulnerable: false);
        TargetCandidate near = Candidate(StrongId, distance: 1f, priority: StrongPriority);

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(shielded, near), dpsOneSecond: 0f);

        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(WeakId),
            "The player chose this one on purpose and is walking around it — the game must not " +
            "quietly overrule a decision it just promised was theirs.");
        Assert.That(targeter.IsCurrentBlocked, Is.True);
        Assert.That(
            targeter.HasFocus,
            Is.True,
            "Rule 3: a phase of invulnerability is exactly what the override exists for.");

        // And it stays there across a scheduled decision, rather than surviving one tick on the
        // strength of the immediate path.
        targeter.Tick(Cadence, Pair(shielded, near), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));
        Assert.That(targeter.IsCurrentBlocked, Is.True);
        Assert.That(targeter.HasFocus, Is.True);
        Assert.That(targeter.ChangedThisTick, Is.False);
    }

    // ---- Rule 4: scoring, the blocked state, and nothing at all -------------------------------

    [Test]
    public void AllBlocked_HoldsNearest_WithFlag()
    {
        Targeter targeter = Fresh();

        // Nearest is deliberately the worst of the three by score and the highest by id, so
        // "nearest" cannot be confused with "best" or with "first seen".
        var candidates = new[]
        {
            Candidate(1, distance: 10f, priority: 8, isVulnerable: false),
            Candidate(2, distance: 6f, priority: 5, isVulnerable: false),
            Candidate(3, distance: 2f, priority: 1, isVulnerable: false),
        };

        targeter.Tick(0f, candidates, dpsOneSecond: 0f);

        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(3),
            "CC §3.6: hold facing on the nearest one — the game saying 'go around'.");
        Assert.That(targeter.IsCurrentBlocked, Is.True);
    }

    [Test]
    public void NoCandidates_NoTarget()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        targeter.Tick(Cadence / 10f, ReadOnlySpan<TargetCandidate>.Empty, dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(
            targeter.IsCurrentBlocked,
            Is.False,
            "An empty field is not the blocked state: there is nothing to hold facing on, and " +
            "nothing for the reticle to draw a glyph over.");
        Assert.That(targeter.ChangedThisTick, Is.True);
    }

    // ---- Rule 6: what the caller publishes on ------------------------------------------------

    [Test]
    public void ChangedThisTick_FalseWhenStable()
    {
        Targeter targeter = Fresh();

        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);
        Assert.That(targeter.ChangedThisTick, Is.True, "Acquiring a target is a change.");

        // A full cadence, so the decision genuinely ran and came to the same conclusion — this
        // row would be vacuous if it only proved that no decision was made.
        targeter.Tick(Cadence, One(Weak), dpsOneSecond: 0f);

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));
        Assert.That(targeter.ChangedThisTick, Is.False);
    }

    // ---- Rule 7: reset -----------------------------------------------------------------------

    [Test]
    public void Reset_ClearsState()
    {
        Targeter targeter = Fresh();

        targeter.Focus(WeakId);
        targeter.Tick(0f, Pair(Weak, Strong), dpsOneSecond: 0f);

        // Nine tenths of a cadence banked, so the accumulator has something to lose.
        targeter.Tick(Cadence * 0.9f, Pair(Weak, Strong), dpsOneSecond: 0f);

        Assert.That(targeter.HasFocus, Is.True);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        targeter.Reset();

        Assert.That(targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(targeter.FocusedTargetId, Is.EqualTo(-1));
        Assert.That(targeter.HasFocus, Is.False);
        Assert.That(targeter.IsCurrentBlocked, Is.False);
        Assert.That(targeter.ChangedThisTick, Is.False);

        // The accumulator too, which is otherwise invisible: it is read back by re-acquiring and
        // then ticking less than a cadence. With 0.9 still banked this second tick would reach
        // the cadence and switch to Strong; from zero it cannot, and holds.
        targeter.Tick(0f, One(Weak), dpsOneSecond: 0f);
        Assert.That(targeter.CurrentTargetId, Is.EqualTo(WeakId));

        targeter.Tick(Cadence * 0.5f, Pair(Weak, Strong), dpsOneSecond: 0f);

        Assert.That(
            targeter.CurrentTargetId,
            Is.EqualTo(WeakId),
            "A reset targeter decides on a fresh cadence rather than inheriting the last run's phase.");
    }

    // ---- Rule 8: allocation ------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        Targeter targeter = Fresh();

        // A heap array rather than a stack-allocated span, because a lambda cannot close over a
        // ref struct (M1-03). It converts to a ReadOnlySpan at the call site inside the measured
        // body, which is the conversion under test.
        var candidates = new TargetCandidate[40];

        for (int i = 0; i < candidates.Length; i++)
        {
            // Spread so every branch stays live across the sweep: some out of range, some
            // invulnerable, some elite, some at finisher hp, and ids descending so the scorer's
            // tiebreak is exercised too.
            candidates[i] = new TargetCandidate(
                id: candidates.Length - i,
                distance: i * 0.4f,
                priority: 1 + (i % 8),
                isElite: i % 5 == 0,
                isVulnerable: i % 7 != 0,
                hp: i % 3 == 0 ? 4f : 80f);
        }

        // The focused id is one of the ones sitting past the acquire range (index 35, distance
        // 14 m), so the sweep runs the id lookup and the out-of-range timer, falls through to
        // scoring while the timer builds, and crosses the focus drop after two simulated seconds
        // — every path in Tick, in one measured body.
        targeter.Focus(candidates[35].Id);

        // dt is a fifth of the cadence, so the sweep crosses a scheduled decision every five
        // iterations rather than measuring only the cheap ticks.
        AllocationAssert.None(() => targeter.Tick(Cadence / 5f, candidates, 10f));

        _sink = targeter.CurrentTargetId;
        Assert.That(_sink, Is.GreaterThan(0), "Sanity: the targeter actually chose something.");

        // And the probe is live under this exact harness, not merely in AllocationAssert's own
        // fixture — M1-01's lesson: a measurement that cannot fail proves nothing.
        Assert.Throws<AssertionException>(
            () => AllocationAssert.None(() => _sink = new int[8].Length, iterations: 8));
    }

    // ---- Beyond the spec: the guards this task added ------------------------------------------

    [Test]
    public void Constructor_RejectsNulls()
    {
        TargetingSpec spec = OathboundSpec();

        Assert.Throws<ArgumentNullException>(() => new Targeter(null, spec));
        Assert.Throws<ArgumentNullException>(() => new Targeter(new TargetScorer(spec), null));
    }

    [Test]
    public void Tick_RejectsNonFiniteDt()
    {
        Targeter targeter = Fresh();

        // A non-finite step poisons both timers at once and says nothing: the cadence would
        // either fire every tick or never again, and the focus drop window would stop comparing.
        // The `!(dt >= 0f)` spelling is what refuses NaN — the natural `dt < 0f` waves it through.
        var candidates = new[] { Weak };

        Assert.Throws<ArgumentOutOfRangeException>(() => targeter.Tick(-0.01f, candidates, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => targeter.Tick(float.NaN, candidates, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => targeter.Tick(float.PositiveInfinity, candidates, 0f));

        Assert.DoesNotThrow(() => targeter.Tick(0f, candidates, 0f));
    }

    // ---- Fixture --------------------------------------------------------------------------

    private const int WeakId = 1;
    private const int StrongId = 2;
    private const int DecoyId = 3;

    private const int WeakPriority = 3;
    private const int StrongPriority = 8;

    /// <summary>
    /// The incumbent in most rows: on the acquire range, so its score is its priority alone.
    /// </summary>
    private static TargetCandidate Weak => Candidate(WeakId, AcquireRange, WeakPriority);

    /// <summary>
    /// The challenger, leading by 5 — comfortably more than the 1.5 hysteresis margin, so no row
    /// that is not about hysteresis can turn on it.
    /// </summary>
    private static TargetCandidate Strong => Candidate(StrongId, AcquireRange, StrongPriority);

    /// <summary>Something to retarget to that is never the best or the nearest of anything.</summary>
    private static TargetCandidate Decoy => Candidate(DecoyId, AcquireRange, priority: 2);

    private static TargetingSpec OathboundSpec() => new TargetingSpec(
        AcquireRange,
        distanceWeight: 3f,
        eliteBonus: 2f,
        finisherBonus: 1f,
        hysteresis: 1.5f,
        cadence: Cadence);

    private static Targeter Fresh()
    {
        TargetingSpec spec = OathboundSpec();
        return new Targeter(new TargetScorer(spec), spec);
    }

    private static TargetCandidate Candidate(
        int id,
        float distance,
        int priority,
        bool isVulnerable = true,
        float hp = 100f)
        => new TargetCandidate(id, distance, priority, isElite: false, isVulnerable, hp);

    private static TargetCandidate[] One(TargetCandidate only) => new[] { only };

    private static TargetCandidate[] Pair(TargetCandidate a, TargetCandidate b) => new[] { a, b };

    /// <summary>
    /// Ticks in tenth-of-a-second steps for roughly <paramref name="seconds"/>, which is what the
    /// focus rows need: the drop window is driven by the sum of the <c>dt</c>s handed in, never by
    /// a clock, so a test advances time by ticking exactly as the run does.
    /// </summary>
    private static void TickFor(
        Targeter targeter,
        float seconds,
        TargetCandidate a,
        TargetCandidate b)
    {
        var candidates = new[] { a, b };
        int steps = (int)MathF.Round(seconds / 0.1f);

        for (int i = 0; i < steps; i++)
        {
            targeter.Tick(0.1f, candidates, dpsOneSecond: 0f);
        }
    }
}
