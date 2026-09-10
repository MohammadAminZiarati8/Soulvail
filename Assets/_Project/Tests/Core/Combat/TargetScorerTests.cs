using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M1-03 spec's six rules, plus rows for the guards this task added to
/// <see cref="TargetingSpec"/> rather than inherited.
/// </summary>
/// <remarks>
/// <para>
/// The Oathbound's targeting numbers are the running example throughout — range 12, distance
/// weight 3, elite 2, finisher 1, hysteresis 1.5, cadence 0.1 (CC §7) — because they are what
/// <c>Oathbound.asset</c> ships and what CC §3.2's worked example was written against.
/// </para>
/// <para>
/// Candidates are built as arrays and converted to a <see cref="ReadOnlySpan{T}"/> at the call
/// site, which is what M1-08 will do with a buffer it reuses each targeting tick. The arrays
/// here are per-row and allocate freely; only <see cref="Select_AllocatesNothing"/> cares, and
/// it builds its one array outside the measured body.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TargetScorerTests
{
    private const float AcquireRange = 12f;
    private const float DistanceWeight = 3f;
    private const float EliteBonus = 2f;
    private const float FinisherBonus = 1f;
    private const float Hysteresis = 1.5f;
    private const float Cadence = 0.1f;

    /// <summary>
    /// Tight enough that no two of the Oathbound's weights satisfy each other's assertion.
    /// </summary>
    private const float Tolerance = 1e-4f;

    /// <summary>Where the allocation test parks its result, so the calls cannot be elided.</summary>
    private int _sink;

    // ---- Rule 1: the formula ----------------------------------------------------------------

    [Test]
    public void Score_MatchesFormula()
    {
        TargetScorer scorer = Oathbound();

        // priority 4, 6 m of a 12 m range, elite, 5 hp against 10 dps, and the incumbent:
        // 4 + 3 × (1 − 0.5) + 2 + 1 + 1.5 = 10. Every term is switched on, and no two of them
        // contribute the same amount, so a transposed weight moves the total.
        var c = new TargetCandidate(1, 6f, 4, isElite: true, isVulnerable: true, hp: 5f);

        Assert.That(
            scorer.Score(in c, isCurrent: true, dpsOneSecond: 10f),
            Is.EqualTo(10f).Within(Tolerance));

        // And with every conditional term off, so the distance term is not being credited with
        // somebody else's points: 4 + 1.5 = 5.5.
        var plain = new TargetCandidate(1, 6f, 4, isElite: false, isVulnerable: true, hp: 500f);

        Assert.That(
            scorer.Score(in plain, isCurrent: false, dpsOneSecond: 10f),
            Is.EqualTo(5.5f).Within(Tolerance));
    }

    [Test]
    public void PriorityBeatsDistance()
    {
        TargetScorer scorer = Oathbound();

        // CC §3.2's own worked example, and the whole reason priority is a designer's column:
        // a Choir healing the pack at 11 m must outrank a Husk chewing on you at 5 m.
        // Choir  8 + 3 × (1 − 11/12) = 8.25
        // Husk   1 + 3 × (1 −  5/12) = 2.75
        var choir = new TargetCandidate(1, 11f, 8, isElite: false, isVulnerable: true, hp: 100f);
        var husk = new TargetCandidate(2, 5f, 1, isElite: false, isVulnerable: true, hp: 100f);

        Assert.That(
            scorer.Score(in choir, isCurrent: false, dpsOneSecond: 0f),
            Is.EqualTo(8.25f).Within(Tolerance));
        Assert.That(
            scorer.Score(in husk, isCurrent: false, dpsOneSecond: 0f),
            Is.EqualTo(2.75f).Within(Tolerance));

        var candidates = new[] { choir, husk };

        Assert.That(
            scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 0f),
            Is.EqualTo(1),
            "The Choir is more than twice as far and still wins: priority dominates by design.");
    }

    // ---- Rule 3: hysteresis ------------------------------------------------------------------

    [Test]
    public void Hysteresis_KeepsIncumbent()
    {
        TargetScorer scorer = Oathbound();

        // Bare scores 5 and 6 — priority alone, since both sit exactly on the acquire range and
        // their distance terms are therefore exactly zero. The incumbent's +1.5 puts it at 6.5
        // and it holds, which is CC §3.3: without this the character twitches between the two
        // every 100 ms and wastes the swing that was mid-windup.
        var candidates = new[]
        {
            AtRange(id: 1, priority: 5),
            AtRange(id: 2, priority: 6),
        };

        Assert.That(scorer.SelectBest(candidates, currentId: 1, dpsOneSecond: 0f), Is.EqualTo(1));

        // The same pair with no incumbent goes the other way, which is what proves the bonus is
        // doing the work rather than the iteration order.
        Assert.That(scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(2));
    }

    [Test]
    public void Hysteresis_ChallengerWinsPastMargin()
    {
        TargetScorer scorer = Oathbound();

        // 5 + 1.5 = 6.5 against 7. The margin is a threshold to be beaten, not a wall.
        var candidates = new[]
        {
            AtRange(id: 1, priority: 5),
            AtRange(id: 2, priority: 7),
        };

        Assert.That(scorer.SelectBest(candidates, currentId: 1, dpsOneSecond: 0f), Is.EqualTo(2));
    }

    [Test]
    public void Hysteresis_ExactTie_FallsToLowestId()
    {
        // Beyond the spec's table, and the one place its rules disagree: rule 2 resolves exact
        // ties to the lowest id, while rule 3 says a challenger must beat the incumbent by
        // *more* than the margin — which at a dead heat would mean the incumbent holds. Rule 2
        // wins, because it is also what the Public API block states without qualification, and
        // because the tiebreak is about determinism rather than about incumbency.
        //
        // Nothing jitters either way, which is why this is a coin-flip worth documenting rather
        // than a defect. Once the switch happens the new incumbent carries the bonus, so it then
        // leads by a whole margin and the pair never oscillates. See PROGRESS, M1-03.
        //
        // Hysteresis 2 rather than the Oathbound's 1.5, because the tie has to be exact in
        // binary floats and every other term here is a whole number: 5 + 2 = 7 against 7.
        var scorer = new TargetScorer(SpecWith(hysteresis: 2f));

        var incumbentIsHigher = new[]
        {
            AtRange(id: 1, priority: 7),
            AtRange(id: 2, priority: 5),
        };

        Assert.That(
            scorer.SelectBest(incumbentIsHigher, currentId: 2, dpsOneSecond: 0f),
            Is.EqualTo(1),
            "An exact tie is not a hold: the lowest id takes it, incumbent or not.");

        // The mirror image, so the row pins "lowest id" rather than "the challenger wins ties":
        // with the incumbent already the lowest id, it keeps the target.
        var incumbentIsLower = new[]
        {
            AtRange(id: 1, priority: 5),
            AtRange(id: 2, priority: 7),
        };

        Assert.That(
            scorer.SelectBest(incumbentIsLower, currentId: 1, dpsOneSecond: 0f),
            Is.EqualTo(1));
    }

    // ---- Rule 2: what is not selectable ------------------------------------------------------

    [Test]
    public void Invulnerable_NeverSelected()
    {
        TargetScorer scorer = Oathbound();

        // CC §3.6. A Warden immune from the front is out of contention entirely, however well it
        // would otherwise score — priority 8, elite, and at point-blank range here.
        var blocked = new[]
        {
            new TargetCandidate(1, 1f, 8, isElite: true, isVulnerable: false, hp: 1f),
        };

        Assert.That(scorer.SelectBest(blocked, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(-1));

        // Not even as the incumbent: a target that becomes undamageable is dropped, which is the
        // fact M1-04's immediate-retarget rule is built on.
        Assert.That(scorer.SelectBest(blocked, currentId: 1, dpsOneSecond: 0f), Is.EqualTo(-1));

        // And it is vulnerability that excluded it, not something else about the candidate.
        var vulnerable = new[]
        {
            new TargetCandidate(1, 1f, 8, isElite: true, isVulnerable: true, hp: 1f),
        };

        Assert.That(scorer.SelectBest(vulnerable, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(1));
    }

    [Test]
    public void OutOfRange_NeverSelected()
    {
        TargetScorer scorer = Oathbound();

        var beyond = new[]
        {
            new TargetCandidate(1, 12.1f, 8, isElite: false, isVulnerable: true, hp: 100f),
        };

        Assert.That(scorer.SelectBest(beyond, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(-1));

        // Even the incumbent's bonus does not buy it back in: range is a filter, not a term.
        Assert.That(scorer.SelectBest(beyond, currentId: 1, dpsOneSecond: 0f), Is.EqualTo(-1));

        // Exactly at the range is in. The boundary is inclusive on purpose: a target standing on
        // it would otherwise flicker in and out of contention on float dust alone.
        var onTheLine = new[]
        {
            new TargetCandidate(1, 12f, 8, isElite: false, isVulnerable: true, hp: 100f),
        };

        Assert.That(scorer.SelectBest(onTheLine, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(1));
    }

    // ---- Rule 1: the conditional bonuses -----------------------------------------------------

    [Test]
    public void EliteBonus_Applies()
    {
        TargetScorer scorer = Oathbound();

        // Identical but for the flag, and the elite is the *higher* id, so the lowest-id
        // tiebreak would pick the other one if the bonus were not applied.
        var candidates = new[]
        {
            new TargetCandidate(1, 5f, 3, isElite: false, isVulnerable: true, hp: 100f),
            new TargetCandidate(2, 5f, 3, isElite: true, isVulnerable: true, hp: 100f),
        };

        Assert.That(scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(2));
    }

    [Test]
    public void FinisherBonus_AppliesWhenHpAtOrBelowDps()
    {
        TargetScorer scorer = Oathbound();

        var wounded = new TargetCandidate(1, 6f, 3, isElite: false, isVulnerable: true, hp: 10f);
        var barelyNot = new TargetCandidate(1, 6f, 3, isElite: false, isVulnerable: true, hp: 10.1f);

        // 3 + 3 × (1 − 6/12) = 4.5 before the bonus.
        float baseline = 3f + (DistanceWeight * 0.5f);

        // At exactly one second of life the bonus applies — that target is precisely what CC
        // §3.2's "prefer finishing wounded targets" is about, so the comparison is `<=`.
        Assert.That(
            scorer.Score(in wounded, isCurrent: false, dpsOneSecond: 10f),
            Is.EqualTo(baseline + FinisherBonus).Within(Tolerance));

        Assert.That(
            scorer.Score(in barelyNot, isCurrent: false, dpsOneSecond: 10f),
            Is.EqualTo(baseline).Within(Tolerance));
    }

    // ---- Rules 2 and 4: determinism ----------------------------------------------------------

    [Test]
    public void Ties_ResolveToLowestId()
    {
        TargetScorer scorer = Oathbound();

        // Identical in every scored respect, and deliberately in descending id order so that
        // "first seen wins" and "lowest id wins" give different answers. A symmetric spawn
        // produces this constantly, and without the rule the answer would depend on gather
        // order — which is spawn order, which is a seed (ADR-0004).
        var candidates = new[]
        {
            new TargetCandidate(9, 5f, 3, isElite: false, isVulnerable: true, hp: 100f),
            new TargetCandidate(3, 5f, 3, isElite: false, isVulnerable: true, hp: 100f),
        };

        Assert.That(scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(3));
        Assert.That(scorer.SelectNearest(candidates), Is.EqualTo(3));
    }

    // ---- Rule 5: nothing to choose from ------------------------------------------------------

    [Test]
    public void Empty_ReturnsMinusOne()
    {
        TargetScorer scorer = Oathbound();

        Assert.That(
            scorer.SelectBest(ReadOnlySpan<TargetCandidate>.Empty, currentId: -1, dpsOneSecond: 0f),
            Is.EqualTo(-1));
        Assert.That(scorer.SelectNearest(ReadOnlySpan<TargetCandidate>.Empty), Is.EqualTo(-1));

        // An incumbent that is not in the span does not survive by being named: −1 means "there
        // is nothing to shoot", and M1-04 has to drop the target rather than keep firing at a
        // corpse.
        Assert.That(
            scorer.SelectBest(ReadOnlySpan<TargetCandidate>.Empty, currentId: 7, dpsOneSecond: 0f),
            Is.EqualTo(-1));
    }

    // ---- Rule 4: the all-blocked state -------------------------------------------------------

    [Test]
    public void SelectNearest_IgnoresVulnerability()
    {
        TargetScorer scorer = Oathbound();

        // CC §3.6: when everything is blocked, hold facing on the nearest one and keep the glyph
        // up — the game saying "go around". Nearest means nearest, not nearest-that-can-be-hurt,
        // or the facing would swing to something the player is not being told about.
        var candidates = new[]
        {
            new TargetCandidate(1, 2f, 1, isElite: false, isVulnerable: false, hp: 100f),
            new TargetCandidate(2, 5f, 8, isElite: true, isVulnerable: true, hp: 1f),
        };

        Assert.That(scorer.SelectNearest(candidates), Is.EqualTo(1));

        // Distance alone decides it: the far one out-scores the near one on every other term,
        // and SelectBest picks it — which is the answer to the other question, and the reason
        // the two are separate calls rather than one with a fallback inside it.
        Assert.That(scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 10f), Is.EqualTo(2));
    }

    // ---- Rule 6: allocation ------------------------------------------------------------------

    [Test]
    public void Select_AllocatesNothing()
    {
        TargetScorer scorer = Oathbound();

        // A heap array rather than a stack-allocated span, because a lambda cannot close over a
        // ref struct. It converts to a ReadOnlySpan at the call site inside the measured body,
        // which is the conversion under test; the array itself is built once, out here.
        var candidates = new TargetCandidate[40];

        for (int i = 0; i < candidates.Length; i++)
        {
            // Spread so that every branch is live across the sweep: some out of range, some
            // invulnerable, some elite, some at finisher hp, and ids descending so the tiebreak
            // is exercised too. A uniform array would measure one path and report it as all
            // of them.
            candidates[i] = new TargetCandidate(
                id: candidates.Length - i,
                distance: i * 0.4f,
                priority: 1 + (i % 8),
                isElite: i % 5 == 0,
                isVulnerable: i % 7 != 0,
                hp: i % 3 == 0 ? 4f : 80f);
        }

        AllocationAssert.None(() => _sink = scorer.SelectBest(candidates, 12, 10f));
        Assert.That(_sink, Is.GreaterThan(0), "Sanity: SelectBest actually chose something.");

        AllocationAssert.None(() => _sink = scorer.SelectNearest(candidates));
        Assert.That(_sink, Is.EqualTo(40), "Sanity: SelectNearest found the candidate at distance 0.");

        // And the probe is live *here*, not merely in AllocationAssert's own fixture: an
        // allocating body under this exact harness must fail. M1-01's lesson — a measurement
        // that cannot fail proves nothing, and both BCL counters this project first reached for
        // are inert on Unity's Mono.
        Assert.Throws<AssertionException>(
            () => AllocationAssert.None(() => _sink = new int[8].Length, iterations: 8));
    }

    // ---- Beyond the spec: the guards this task added -----------------------------------------

    [Test]
    public void Spec_RejectsNonPositiveRangeAndCadence()
    {
        // A zero range selects nobody and divides the distance term by nothing; a zero cadence
        // turns CC §3.1's 10 Hz loop back into a per-frame one, which is the cost the cadence
        // exists to avoid. NaN is refused by the same `!(v > 0f)` spelling — the natural
        // `v <= 0f` would wave it through, and a NaN range makes every distance term NaN, so the
        // gun would quietly stop choosing a target with nothing logged.
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(acquireRange: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(acquireRange: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(acquireRange: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(acquireRange: float.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(cadence: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(cadence: float.NaN));
    }

    [Test]
    public void Spec_AllowsZeroWeightsButNotNegativeOnes()
    {
        // Zero switches a term off, which is a coherent thing to tune towards — a class that
        // picks purely by archetype priority. Negative would invert the term's meaning while
        // still reading as a weight: "distance weight −3" would quietly prefer the enemy
        // furthest away.
        Assert.DoesNotThrow(() => SpecWith(distanceWeight: 0f));
        Assert.DoesNotThrow(() => SpecWith(hysteresis: 0f));

        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(distanceWeight: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(eliteBonus: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecWith(finisherBonus: float.PositiveInfinity));
    }

    [Test]
    public void Scorer_RejectsNullSpec()
    {
        Assert.Throws<ArgumentNullException>(() => new TargetScorer(null));
    }

    [Test]
    public void NonFiniteDistance_IsNeverSelected()
    {
        TargetScorer scorer = Oathbound();

        // Beyond the spec's table, and the reason both loops are spelled the way they are. A
        // candidate whose distance is NaN fails every comparison, so it has to *lose* rather
        // than win — and it must not become a hole that stops anything else winning either. It
        // is listed first for exactly that reason: a loop that admits its first candidate
        // unconditionally would seat the NaN and then reject every real target after it.
        var candidates = new[]
        {
            new TargetCandidate(1, float.NaN, 8, isElite: true, isVulnerable: true, hp: 1f),
            new TargetCandidate(2, 5f, 1, isElite: false, isVulnerable: true, hp: 100f),
        };

        Assert.That(scorer.SelectBest(candidates, currentId: -1, dpsOneSecond: 0f), Is.EqualTo(2));
        Assert.That(scorer.SelectNearest(candidates), Is.EqualTo(2));
    }

    /// <summary>A scorer on the Oathbound's numbers, CC §7.</summary>
    private static TargetScorer Oathbound() => new TargetScorer(SpecWith());

    private static TargetingSpec SpecWith(
        float acquireRange = AcquireRange,
        float distanceWeight = DistanceWeight,
        float eliteBonus = EliteBonus,
        float finisherBonus = FinisherBonus,
        float hysteresis = Hysteresis,
        float cadence = Cadence)
        => new TargetingSpec(acquireRange, distanceWeight, eliteBonus, finisherBonus, hysteresis, cadence);

    /// <summary>
    /// A vulnerable candidate sitting exactly on the acquire range, so its distance term is
    /// exactly zero and its score is its priority alone. That is what lets the hysteresis rows
    /// state their arithmetic in whole numbers.
    /// </summary>
    private static TargetCandidate AtRange(int id, int priority) => new TargetCandidate(
        id, AcquireRange, priority, isElite: false, isVulnerable: true, hp: float.MaxValue);
}
