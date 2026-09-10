using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// Steps 3 and 4 of CC §3.1's targeting loop — SCORE and SELECT — as a pure function of plain
/// numbers. Given what the player can see, it answers which enemy the gun should face.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing about when.</b> There is no clock here and no memory of the last
/// answer: the caller supplies the incumbent's id with every call, and M1-04's <c>Targeter</c>
/// owns the 10 Hz cadence, the immediate retarget when the current target dies, and the focus
/// override. This type is stateless past its spec, so the same inputs always give the same
/// answer and a targeting bug is either in the arithmetic here or in the timing there, never
/// smeared across both.
/// </para>
/// <para>
/// <b>Gathering is somebody else's job too.</b> Steps 1 and 2 — enemies within range, alive and
/// damageable — happen in M1-08, which is where the enemy registry and the snapshot both live.
/// What arrives here is a caller-owned span, so this class allocates nothing and holds no
/// reference to the buffer past the call.
/// </para>
/// <para>
/// <b>Non-finite inputs lose rather than win.</b> Every comparison against NaN is false, and
/// both selection loops are spelled so that the incumbent-beating test must come out
/// <em>true</em> for a candidate to be taken. A NaN distance is therefore never selected — and
/// never poisons the result either — instead of sailing through a <c>&lt;=</c> test that a
/// naive spelling would have inverted. The authored numbers cannot be non-finite at all;
/// <see cref="TargetingSpec"/> refuses them at its constructor.
/// </para>
/// </remarks>
public sealed class TargetScorer
{
    private readonly TargetingSpec _spec;

    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    public TargetScorer(TargetingSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>
    /// CC §3.2's formula: <c>priority + distanceWeight × (1 − distance / acquireRange)
    /// + eliteBonus + finisherBonus + hysteresis</c>, the last three each conditional.
    /// </summary>
    /// <remarks>
    /// Public because it is the number a debug overlay draws over each enemy's head when
    /// auto-aim is questioned, and because a formula that can only be observed through the
    /// selection it drives is a formula nobody can check. It deliberately does <em>not</em>
    /// filter: a candidate out of range or invulnerable still scores, and it is
    /// <see cref="SelectBest"/> that refuses to pick it. Keeping the two apart is what lets a
    /// test pin the arithmetic exactly (<c>Score_MatchesFormula</c>) without arranging a
    /// selection around it.
    /// </remarks>
    /// <param name="c">The enemy being scored.</param>
    /// <param name="isCurrent">
    /// Whether this is the target already being tracked, which earns
    /// <see cref="TargetingSpec.Hysteresis"/>. The caller decides — <see cref="SelectBest"/>
    /// passes <c>c.Id == currentId</c>, and an immediate retarget passes <c>false</c> for
    /// everything, which is exactly how CC §3.3's "skip hysteresis" case is spelled.
    /// </param>
    /// <param name="dpsOneSecond">
    /// What one second of fire is expected to do — the threshold for CC §3.2's finisher bonus.
    /// It is an estimate and is meant to be: the bonus nudges the choice towards a wounded
    /// target rather than promising a kill.
    /// </param>
    public float Score(in TargetCandidate c, bool isCurrent, float dpsOneSecond)
    {
        // Priority leads, and the distance term is bounded by the weight, so an archetype two
        // points above another cannot be out-scored on closeness alone. That ordering is the
        // design (CC §3.2), not an accident of the arithmetic.
        float score = c.Priority;

        score += _spec.DistanceWeight * (1f - (c.Distance / _spec.AcquireRange));

        if (c.IsElite)
        {
            score += _spec.EliteBonus;
        }

        // `<=` rather than `<`: a target with exactly one second of life left is precisely the
        // one the bonus is for. NaN hp fails the test and simply earns no bonus, rather than
        // making the whole score NaN.
        if (c.Hp <= dpsOneSecond)
        {
            score += _spec.FinisherBonus;
        }

        if (isCurrent)
        {
            score += _spec.Hysteresis;
        }

        return score;
    }

    /// <summary>
    /// The highest-scoring candidate that is vulnerable and within
    /// <see cref="TargetingSpec.AcquireRange"/>, or −1 when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ties resolve to the lowest <see cref="TargetCandidate.Id"/>. Two identical enemies
    /// equidistant from the player are not a contrived case — a symmetric spawn produces them
    /// constantly — and without a rule the answer would depend on the order the caller happened
    /// to gather them in, which is spawn order, which is a seed. Deterministic selection is
    /// what makes a replayed run replay (ADR-0004).
    /// </para>
    /// <para>
    /// The tiebreak governs the incumbent too, which is the one place M1-03's rules disagreed:
    /// a dead heat goes to the lowest id whether or not one of the two is the current target.
    /// Hysteresis is a bonus applied before the comparison, not a veto after it. Nothing
    /// oscillates as a result — the moment the target switches, the new incumbent carries the
    /// bonus and leads by a whole margin — so the alternative would buy nothing but a branch.
    /// </para>
    /// <para>
    /// The incumbent gets <see cref="TargetingSpec.Hysteresis"/> through
    /// <paramref name="currentId"/>, so a challenger must beat it by more than that margin.
    /// Passing −1 (or any id not present) is how a caller asks for a fresh choice with no
    /// incumbent — which is what M1-04 does the moment the current target dies, leaves range or
    /// becomes undamageable, per CC §3.3.
    /// </para>
    /// </remarks>
    /// <param name="candidates">
    /// Every enemy worth considering, gathered by the caller. Range and vulnerability are
    /// filtered here rather than there, so one rule lives in one place.
    /// </param>
    /// <param name="currentId">The id being tracked, or −1 for none.</param>
    /// <param name="dpsOneSecond">One second of expected damage, for the finisher bonus.</param>
    /// <returns>The chosen id, or −1 if nothing is selectable.</returns>
    public int SelectBest(ReadOnlySpan<TargetCandidate> candidates, int currentId, float dpsOneSecond)
    {
        int bestId = -1;

        // Negative infinity rather than a "have we seen one yet" flag: any finite score beats
        // it, so the first eligible candidate wins without a special case, while a NaN score
        // still fails both tests below and is passed over. A flag would have had to admit the
        // first candidate unconditionally, NaN included.
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly TargetCandidate c = ref candidates[i];

            // CC §3.6: never select something that cannot be damaged right now.
            if (!c.IsVulnerable)
            {
                continue;
            }

            // `!(distance <= range)` rather than `distance > range`, so a NaN distance is
            // excluded here instead of reaching the score. At exactly the range it is in — the
            // acquire radius is inclusive, or a target sitting on the boundary would flicker in
            // and out of contention as float dust moved it.
            if (!(c.Distance <= _spec.AcquireRange))
            {
                continue;
            }

            float score = Score(in c, c.Id == currentId, dpsOneSecond);

            if (score > bestScore || (score == bestScore && c.Id < bestId))
            {
                bestScore = score;
                bestId = c.Id;
            }
        }

        return bestId;
    }

    /// <summary>
    /// The nearest candidate, vulnerable or not, or −1 when the span is empty.
    /// </summary>
    /// <remarks>
    /// CC §3.6's all-blocked state: when every candidate is undamageable, the player holds
    /// facing on the nearest one with the blocked glyph up, which is the game saying "go around"
    /// in its own language. That is the only reason this ignores vulnerability, and it is why
    /// the answer is deliberately separate from <see cref="SelectBest"/> rather than a fallback
    /// inside it — the caller has to know which of the two questions it got an answer to, since
    /// one means "shoot this" and the other means "look at this".
    /// <para>
    /// Range is not filtered, because the caller has already gathered within it (CC §3.1 step 1)
    /// and a "nearest" that could answer −1 for a non-empty span would need a third result to
    /// distinguish from "nothing there at all". Ties resolve to the lowest id, for the same
    /// determinism reason as <see cref="SelectBest"/>.
    /// </para>
    /// </remarks>
    public int SelectNearest(ReadOnlySpan<TargetCandidate> candidates)
    {
        int bestId = -1;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly TargetCandidate c = ref candidates[i];

            if (c.Distance < bestDistance || (c.Distance == bestDistance && c.Id < bestId))
            {
                bestDistance = c.Distance;
                bestId = c.Id;
            }
        }

        return bestId;
    }
}
