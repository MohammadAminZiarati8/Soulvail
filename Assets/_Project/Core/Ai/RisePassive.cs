using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

/// <summary>
/// CH §3.2's Rise: a quarter of the enemies a Gravecaller kills stand back up on their side. One
/// draw per death, one spawn per success, nothing else. See CH §3.2, GD §12.3, ADR-0008, ADR-0011
/// and M5-04b.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is offered deaths and does not go looking for them</b> (rule 2). <c>RunSession.Tick</c>
/// pulls <c>EnemySystem.DrainDeaths</c> beside the experience drain and hands the result here, in
/// order, once a tick. Core does not subscribe to its own events — <c>RunSession</c>'s own remarks
/// refuse that in as many words — and <c>EnemySystem.PendingKills</c> is a count, which is not
/// enough: a Wight stands up <em>where the corpse fell</em>, so what this needs is the position and
/// the only thing that carries one is the death itself.
/// </para>
/// <para>
/// <b><see cref="IRandom.Drops"/>, and no sixth stream</b> (rule 1). <c>RandomState</c> carries five
/// positions and its own remarks say a sixth is <em>"the format's version field's job"</em>
/// (AR §18.3), so a stream of Rise's own would be a save-format <b>v4</b> for a 25 % chance.
/// <c>Drops</c> is right on meaning as much as on cost: it is <em>"a kill produced something"</em>,
/// it is the stream a loot drop will use when GD §14 gets one, and <b>nothing draws from it
/// today</b> — so every seeded run that has ever been played replays identically.
/// </para>
/// <para>
/// <b>One draw per death, whatever comes of it</b> (rule 1, ADR-0011). The draw happens before every
/// reason a raise might not happen — a boss, a full army, a chance nobody can read — so the position
/// of the <c>Drops</c> stream is a function of <em>how many things died</em> and never of what the
/// arena happened to allow. A sequence that skipped a draw at the cap would make a seeded run's
/// later rises depend on how crowded an earlier fight was.
/// </para>
/// <para>
/// <b>Nothing here allocates</b> (AR §4.3). It holds no collection: the deaths arrive in a span the
/// caller owns, and a raise is a call into <c>MinionSystem</c>, which built its whole army at
/// construction.
/// </para>
/// </remarks>
public sealed class RisePassive
{
    private readonly MinionSystem _minions;
    private readonly IRandomStream _random;

    /// <param name="spec">
    /// The class's minions. Read once, for <see cref="MinionSpec.RiseChance"/> — everything else a
    /// raise needs belongs to <paramref name="minions"/>, which was built from the same spec.
    /// </param>
    /// <param name="minions">The army a success stands a body up in.</param>
    /// <param name="random">
    /// The run's <see cref="IRandom.Drops"/> and no other stream (rule 1). Handed in rather than
    /// chosen here, so the one object that owns a run's randomness stays the one that hands it out —
    /// <c>SpawnDirector.Tick</c>'s shape.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public RisePassive(MinionSpec spec, MinionSystem minions, IRandomStream random)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        _minions = minions ?? throw new ArgumentNullException(nameof(minions));
        _random = random ?? throw new ArgumentNullException(nameof(random));

        Chance = new Stat(spec.RiseChance);
    }

    /// <summary>
    /// The chance a death makes a Wight, live. Where CH §3.2's Legion nodes put <em>"+10 % rise
    /// chance"</em> (rule 5).
    /// </summary>
    /// <remarks>
    /// Seeded from <see cref="MinionSpec.RiseChance"/> — the authored 0.25 — and <b>read through a
    /// clamp at the point of use rather than clamped in the stat</b>, because a <see cref="Stat"/>
    /// clamps nothing (ADR-0008). See <see cref="Rises"/> for the four directions that clamp takes
    /// and why a non-finite value is the one that has to be named.
    /// </remarks>
    public Stat Chance { get; }

    /// <summary>
    /// How many Wights this run has actually raised.
    /// </summary>
    /// <remarks>
    /// <b>Bodies that stood up, not draws that succeeded</b> (rule 3). A success against a full army
    /// is spent and does not move this — a cap is not a queue, and a counter that climbed anyway
    /// would be a readout promising an army the player cannot see.
    /// </remarks>
    public int Raised { get; private set; }

    /// <summary>
    /// Offers <paramref name="deaths"/> to the passive, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A Wight rises where the enemy fell, on the tick it fell</b> (rule 3). CH §3.2 is <em>"25 %
    /// of enemies killed rise as Wights"</em> — the corpse <em>is</em> the Wight — so the position is
    /// the death's and the moment is this tick's. The spawn goes through
    /// <c>MinionSystem.Spawn</c>, which refuses silently at the cap.
    /// </para>
    /// <para>
    /// <b>A boss does not rise, and nor does a Wight</b> (rule 11). A Warden raised as a 20 HP Wight
    /// is either absurd or free depending on which numbers it kept; and a Wight is not in
    /// <c>EnemyRegistry</c> at all (M5-04a rule 1), so its death cannot reach this path and the rule
    /// costs nothing to keep.
    /// </para>
    /// </remarks>
    /// <param name="deaths">The drained deaths, oldest first. Empty is the ordinary case.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="now"/> is not finite. Refused here rather than one call deeper, so the caller
    /// that invented the clock is still on the stack — <c>MinionSystem</c>'s rule and its reason.
    /// </exception>
    public void OnDeaths(ReadOnlySpan<EnemyDeath> deaths, float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "now must be finite. It comes from RunState.Time, which is a sum of clamped frame "
                    + "times, so a non-finite one is a mis-wired clock rather than a long session.");
        }

        for (int i = 0; i < deaths.Length; i++)
        {
            ref readonly EnemyDeath death = ref deaths[i];

            // The draw comes first and comes for every death, which is the whole of rule 1: a boss
            // draws, a success at the cap draws, and an unreadable chance draws. Everything below is
            // a reason the raise does not happen, never a reason the stream did not move.
            bool rose = Rises();

            if (!rose || death.WasBoss)
            {
                continue;
            }

            // Null is a raise refused at the cap, silently — MinionSystem.Spawn's own bargain, and
            // the honest reading of "cap 3" (rule 3). The draw above is spent either way.
            if (_minions.Spawn(death.Position, now) is null)
            {
                continue;
            }

            Raised++;
        }
    }

    /// <summary>
    /// One draw against the live <see cref="Chance"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="IRandomStream.Chance"/> rather than a comparison written here</b>, because it
    /// draws unconditionally — which is the property rule 1's reproducibility claim rests on — and
    /// because it is the same rule the real generator and the test fake both implement, so swapping
    /// one for the other never shifts how many values this consumes.
    /// </para>
    /// <para>
    /// <b>It gives rule 5 three of its four directions for free.</b> <c>NextFloat</c> is in
    /// <c>[0, 1)</c>, so a live chance at or below zero is never drawn under, one at or above one
    /// always is, and NaN loses every comparison there is. <b>The fourth is +∞</b>, which passes
    /// every <c>&gt;=</c> in the language and would turn a modifier stack nobody can read into a
    /// guaranteed army — so it is refused by name, which is the direction every comparison in core
    /// takes (AR §18.3).
    /// </para>
    /// </remarks>
    private bool Rises()
    {
        float chance = Chance.Value;

        bool drawn = _random.Chance(chance);

        return drawn && !float.IsInfinity(chance);
    }
}
