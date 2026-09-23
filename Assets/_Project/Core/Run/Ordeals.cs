using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Run;

/// <summary>
/// GD §13.4's Ordeals, for one run: which have been dealt, and what they add up to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dealt at a boundary, kept for the run, and read by nothing yet</b> (M6-06a rule 6). The five
/// accumulated answers are the whole of what M6-06b's four consumers will ask, and after this task
/// none of them does — <c>OrdealsTests.Ordeals_NothingReadsTheDialsYet</c> says so, so the day one
/// appears it is a diff rather than a surprise.
/// </para>
/// <para>
/// <b>Drawn on <see cref="IRandom.Affixes"/> and no other stream</b>, which is the ruling M6-01b
/// deferred: nothing else draws <c>Affixes</c> today, so an Ordeal shifts no composition, no offer
/// and no boss's jitter. M7-02's Elites will be its second drawer, and their rolls will be offset by
/// however many Ordeals a run has taken — cheap now, because no seed has ever been replayed against
/// an affix.
/// </para>
/// <para>
/// <b>Nothing here allocates after construction</b> (rule 8). The dealt list is sized to the pool
/// up front, so it never grows past its capacity; the accumulators are fields recomputed when one is
/// dealt; and a boundary with nothing to deal returns before its draw.
/// </para>
/// </remarks>
public sealed class Ordeals
{
    private readonly ModeSpec _mode;
    private readonly IDomainEvents _events;

    /// <summary>Which pool positions are gone — without replacement, rule 4.</summary>
    private readonly bool[] _dealt;

    /// <summary>The pool positions dealt, in the order they were. <see cref="ThreatCostMultiplier"/> walks it.</summary>
    private readonly List<int> _order;

    /// <summary>The same order as ids — what the save carries. Capacity is the pool, so it never grows.</summary>
    private readonly List<ContentId> _applied;

    private readonly ReadOnlyCollection<ContentId> _appliedView;

    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Ordeals(ModeSpec mode, IDomainEvents events)
    {
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        int pool = mode.Ordeals.Count;

        _dealt = new bool[pool];
        _order = new List<int>(pool);
        _applied = new List<ContentId>(pool);

        // Wrapped once, here, so Applied allocates nothing and cannot be cast back and written to.
        _appliedView = _applied.AsReadOnly();

        Recompute();
    }

    /// <summary>What has been dealt, in the order it was — what the save carries.</summary>
    public IReadOnlyList<ContentId> Applied => _appliedView;

    /// <summary>How many are still in the pool. Zero is ordinary and means the run is done (rule 4).</summary>
    public int Remaining => _dealt.Length - _order.Count;

    /// <summary>Product of every dealt Ordeal's. 1 for a run with none — rule 3.</summary>
    public float EssenceMultiplier { get; private set; }

    /// <summary>The smallest non-zero one dealt, or 0 for "unchanged" — rule 3.</summary>
    /// <remarks>
    /// The smallest rather than the product or the last, because two Ordeals that each shrink an
    /// offer must not compose to a single card — and <c>OfferGenerator.Draw</c> refuses a count below
    /// one anyway.
    /// </remarks>
    public int OfferCount { get; private set; }

    /// <summary>Sum of every dealt Ordeal's.</summary>
    public int ConcurrencyBonus { get; private set; }

    /// <summary>Product.</summary>
    public float VeilrotMultiplier { get; private set; }

    /// <summary>
    /// Deals one if <paramref name="stage"/> is a boundary the mode schedules and the pool is not
    /// empty. Called once per stage entered, above the composition — rule 5.
    /// </summary>
    /// <param name="stage">The depth being entered. Numbered from 1 (GD §8.2).</param>
    /// <param name="affixes">The run's <see cref="IRandom.Affixes"/> stream and no other.</param>
    /// <remarks>
    /// <b>One <c>NextInt</c> when it deals and no draw at all when it does not</b> —
    /// <c>OfferGenerator</c>'s consumption rule: how far the stream moves is a function of how many
    /// stocked boundaries the run has crossed, and never of which Ordeal came out.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="affixes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public void OnStageEntered(int stage, IRandomStream affixes)
    {
        if (affixes is null)
        {
            throw new ArgumentNullException(nameof(affixes));
        }

        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Stages are numbered from 1 (GD §8.2).");
        }

        if (Remaining == 0 || !_mode.OrdealSchedule.DealsAt(stage))
        {
            return;
        }

        // Uniform over what is left: the k-th position not yet dealt, walked in authored order so the
        // same seed deals the same Ordeal whatever the pool's size.
        int pick = affixes.NextInt(0, Remaining);
        int index = -1;

        for (int i = 0; i < _dealt.Length; i++)
        {
            if (_dealt[i])
            {
                continue;
            }

            if (pick == 0)
            {
                index = i;
                break;
            }

            pick--;
        }

        Deal(index);

        _events.Publish(new OrdealApplied(_mode.Ordeals[index].Id, stage, _order.Count));
    }

    /// <summary>Product over the dealt Ordeals naming <paramref name="enemyId"/>. 1 for the rest.</summary>
    /// <remarks>
    /// A walk rather than a table: at most the pool's length, asked once per archetype per
    /// composition and never per frame (rule 8).
    /// </remarks>
    public float ThreatCostMultiplier(ContentId enemyId)
    {
        float product = 1f;

        for (int i = 0; i < _order.Count; i++)
        {
            OrdealSpec ordeal = _mode.Ordeals[_order[i]];

            if (ordeal.ThreatCostTarget == enemyId && enemyId.Value is not null)
            {
                product *= ordeal.ThreatCostMultiplier;
            }
        }

        return product;
    }

    /// <summary>What a resumed run comes back holding. Silent — rule 7.</summary>
    /// <remarks>
    /// <para>
    /// <b>An id the pool no longer holds is dropped, silently</b> — <c>SkillRunner.Restore</c>'s
    /// rule. Refusing it instead would make a resume fail because a designer renamed an asset. An id
    /// named twice is dealt once, for rule 4's reason.
    /// </para>
    /// <para>
    /// <b>A save written before M6-06a comes back empty</b>, because <c>RunSnapshot.OrdealIds</c> was
    /// <c>Array.Empty</c> from M6-01b until now — so the next scheduled boundary deals one. Recorded
    /// rather than discovered.
    /// </para>
    /// </remarks>
    internal void Restore(IReadOnlyList<ContentId> applied)
    {
        if (applied is null)
        {
            throw new ArgumentNullException(nameof(applied));
        }

        for (int i = 0; i < applied.Count; i++)
        {
            int index = IndexOf(applied[i]);

            if (index >= 0 && !_dealt[index])
            {
                Deal(index);
            }
        }
    }

    private int IndexOf(ContentId id)
    {
        IReadOnlyList<OrdealSpec> pool = _mode.Ordeals;

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void Deal(int index)
    {
        _dealt[index] = true;
        _order.Add(index);
        _applied.Add(_mode.Ordeals[index].Id);

        Recompute();
    }

    /// <summary>
    /// The four scalar answers, from scratch — at most once per deal, so four times a run.
    /// </summary>
    /// <remarks>
    /// Recomputed rather than folded in, so a rule changed here is changed for a restored run and a
    /// dealt one alike, with no second place to keep in step.
    /// </remarks>
    private void Recompute()
    {
        float essence = 1f;
        float veilrot = 1f;
        int offers = 0;
        int concurrency = 0;

        for (int i = 0; i < _order.Count; i++)
        {
            OrdealSpec ordeal = _mode.Ordeals[_order[i]];

            essence *= ordeal.EssenceMultiplier;
            veilrot *= ordeal.VeilrotMultiplier;
            concurrency += ordeal.ConcurrencyBonus;

            if (ordeal.OfferCount > 0 && (offers == 0 || ordeal.OfferCount < offers))
            {
                offers = ordeal.OfferCount;
            }
        }

        EssenceMultiplier = essence;
        VeilrotMultiplier = veilrot;
        OfferCount = offers;
        ConcurrencyBonus = concurrency;
    }
}
