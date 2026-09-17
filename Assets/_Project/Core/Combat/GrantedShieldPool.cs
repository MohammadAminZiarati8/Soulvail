using System;

namespace Soulvail.Core.Combat;

/// <summary>
/// The shield points a <em>cast</em> put on something, remembered per source so that each one can
/// be taken back for exactly what it has left. <see cref="Health"/>'s third pool, spent before the
/// Aegis. See CC §6.4, CH §3.1 and AR §3.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists as its own type because a single float cannot answer the question.</b> Two sources
/// may hold shield at once — Bulwark and some later node — and when one of them expires, what comes
/// off is <em>what remains of that source</em>. Take A granting 35 and B granting 20, then 20 points
/// of damage: the total is 35 either way, but A's expiry removes 15 if the damage came out of A and
/// 35 if it came out of B. One number cannot tell those apart, so the attribution has to be kept and
/// the spend has to have a stated order. That is this class, and it is the whole of it.
/// </para>
/// <para>
/// <b>Oldest grant first, and the order is grant order rather than expiry order.</b> Spending the
/// grant that has been standing longest is the closest this class can get to "spend what is about to
/// be wasted" without knowing anything about clocks — which it deliberately does not, because
/// expiries belong to the thing that owns them (M3-05 rule 6) and nothing here ticks. A source that
/// <em>refreshes</em> keeps its slot, so a Bulwark recast does not jump the queue ahead of a grant
/// that was already there.
/// </para>
/// <para>
/// <b>A fixed array, and a ninth source throws rather than growing one.</b> Eight simultaneous,
/// distinct sources of granted shield is already an order of magnitude past anything CH §4's tree
/// describes, and this is walked from <see cref="Health.ApplyDamage"/> — a silently growing array on
/// a per-frame path is the kind of thing that is only ever found on a phone. The number is a refusal,
/// not a budget.
/// </para>
/// <para>
/// <b>Identity, never equality.</b> Sources are matched with
/// <see cref="object.ReferenceEquals(object, object)"/> because that is what
/// <c>IEffectHandler&lt;T&gt;.Remove</c> promises — <em>"identity only: it is what Remove takes back
/// by"</em> — and a source is typically a <c>SkillSpec</c>, a type that is free to give itself value
/// semantics without this class quietly starting to pool two different nodes into one entry.
/// </para>
/// <para>
/// <b><see cref="Total"/> is walked rather than cached.</b> Eight adds is cheaper than the bug: a
/// running total maintained across grant, spend and remove is a fourth invariant to keep exact, and
/// the failure mode of getting it wrong is a pool that drifts away from the entries it is made of
/// with nothing to compare it against. Nothing here allocates.
/// </para>
/// <para>
/// <b>Internal, and tested through <see cref="Health"/>.</b> It never appears in a public signature —
/// <see cref="Health"/> owns the only instance and forwards three members onto it — and
/// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> and deliberately never will
/// (AR §18.2, M0-10), so every rule below is asserted through the surface that has to be true rather
/// than through this one.
/// </para>
/// </remarks>
internal sealed class GrantedShieldPool
{
    /// <summary>How many distinct sources may hold granted shield at once.</summary>
    internal const int Capacity = 8;

    private readonly object[] _sources = new object[Capacity];
    private readonly float[] _remaining = new float[Capacity];

    private int _count;

    /// <summary>How many sources are currently held, spent or not.</summary>
    /// <remarks>
    /// A source whose points are all gone is still held: it can be refreshed, and the thing that
    /// granted it still has an expiry to honour. It leaves on <see cref="Remove"/> and on
    /// <see cref="Clear"/>, and by no other route.
    /// </remarks>
    internal int Count => _count;

    /// <summary>Points remaining across every source. Zero when nothing is held.</summary>
    internal float Total
    {
        get
        {
            float total = 0f;

            for (int i = 0; i < _count; i++)
            {
                total += _remaining[i];
            }

            return total;
        }
    }

    /// <summary>
    /// Sets <paramref name="source"/>'s contribution to <paramref name="amount"/>, adding it to the
    /// pool if it is not already held.
    /// </summary>
    /// <remarks>
    /// <b>Sets, never adds</b> (rule 6). Two casts of Bulwark before the first expires are one
    /// source, so the second refreshes rather than doubling — per-call stacking would turn CH §4.1's
    /// floored cooldown into permanent immunity, which is the exact failure the floor exists to
    /// prevent. The consequence worth saying out loud is that a refresh <em>does</em> hand back
    /// points the source had already spent; what it does not do is stack them.
    /// </remarks>
    /// <param name="amount">
    /// The source's new contribution. Zero is legal and means "this source now holds nothing" — a
    /// state the pool reaches on its own by being spent, so refusing it here would refuse a value
    /// the class already produces.
    /// </param>
    /// <param name="source">Who is granting. Identity only; see this class's remarks.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Capacity"/> sources are already held and <paramref name="source"/> is not one of
    /// them.
    /// </exception>
    internal void Grant(float amount, object source)
    {
        for (int i = 0; i < _count; i++)
        {
            if (!ReferenceEquals(_sources[i], source))
            {
                continue;
            }

            // The slot is deliberately left where it is. Rule 6 says a refresh "resets nothing
            // else", and the slot's position *is* something else — it is this source's place in the
            // spend order.
            _remaining[i] = amount;
            return;
        }

        if (_count == Capacity)
        {
            throw new InvalidOperationException(
                $"No more than {Capacity} sources may hold granted shield at once, and a ninth is a "
                    + "bug rather than a build: nothing in the design grants shield from more than a "
                    + "couple of places. Growing the array instead would put an allocation on the "
                    + "damage path.");
        }

        _sources[_count] = source;
        _remaining[_count] = amount;
        _count++;
    }

    /// <summary>
    /// Takes up to <paramref name="amount"/> points out of the pool, oldest grant first, and reports
    /// how much was actually there to take.
    /// </summary>
    /// <remarks>
    /// The empty case is the loop not running, which is why <see cref="Health.ApplyDamage"/> needs no
    /// branch to guard the call. <paramref name="amount"/> is trusted to be positive and finite: the
    /// only caller has already refused NaN, zero and negatives at its own door, which is the same
    /// bargain <c>SkillRunner.Tick</c> makes with the clock it is handed.
    /// </remarks>
    /// <returns>Points absorbed, in <c>[0, amount]</c>.</returns>
    internal float Spend(float amount)
    {
        float spent = 0f;

        for (int i = 0; i < _count; i++)
        {
            // `!(spent < amount)` rather than `spent >= amount`, for the reason every guard in
            // Health is spelled that way: it takes the stop-early branch on a value that cannot be
            // compared, instead of walking the whole pool subtracting nothing.
            if (!(spent < amount))
            {
                break;
            }

            // Exact by construction — each take is a Min against the entry it comes out of — so an
            // emptied entry lands on exactly zero rather than on float dust.
            float take = MathF.Min(_remaining[i], amount - spent);

            _remaining[i] -= take;
            spent += take;
        }

        return spent;
    }

    /// <summary>
    /// Drops <paramref name="source"/>'s entry, whatever is left of it.
    /// </summary>
    /// <remarks>
    /// <b>What is left, never what was granted</b> (rule 8). A grant of 35 that absorbed 20 has 15,
    /// and removing 35 would take five points off a player who had already spent them — which is how
    /// a pool goes negative. A source that holds nothing is still removed and still answers
    /// <see langword="true"/>, because it <em>was</em> held; a source that was never granted answers
    /// <see langword="false"/> and changes nothing, which is <c>Stat.RemoveAll</c>'s contract and
    /// what lets a caller clean up unconditionally.
    /// </remarks>
    /// <returns>Whether <paramref name="source"/> was held.</returns>
    internal bool Remove(object source)
    {
        for (int i = 0; i < _count; i++)
        {
            if (!ReferenceEquals(_sources[i], source))
            {
                continue;
            }

            // Shifted down rather than swapped with the last entry, because the order entries sit in
            // is the order Spend walks. A swap is one write instead of a loop and would silently
            // reorder the pool every time a grant expired.
            for (int j = i; j < _count - 1; j++)
            {
                _sources[j] = _sources[j + 1];
                _remaining[j] = _remaining[j + 1];
            }

            _count--;

            // The vacated slot is cleared rather than left: a stale reference would make a later
            // Remove of the same source answer true a second time, and it would hold a SkillSpec
            // alive past the run that took it.
            _sources[_count] = null;
            _remaining[_count] = 0f;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Forgets every source without removing anything from anywhere.
    /// </summary>
    /// <remarks>
    /// What <see cref="Health.Reset"/> and <c>Health.Restore</c> call. There is nothing left to
    /// remove <em>from</em> — the health component is going back to a stated state either way — so
    /// this is the run's-end counterpart to <see cref="Remove"/> rather than a loop over it.
    /// </remarks>
    internal void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _sources[i] = null;
            _remaining[i] = 0f;
        }

        _count = 0;
    }
}
