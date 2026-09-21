using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The sixth effect primitive, and its handler. One file per pair, `ModifyStat.cs`' shape — and the
// first primitive whose subject is neither a Stat nor the player. See CH §4.2, AR §11.2, ADR-0009,
// ADR-0011 and M5-06a rules 10 and 11.

/// <summary>
/// CH §4.2's Exhume, as data: stand this many Wights up around the player, now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first primitive in the game whose subject is neither a <see cref="Stat"/> nor the
/// player</b> (rule 10), and the argument it does <em>not</em> make is that it is therefore a new
/// kind of thing. It is a verb with an authored count — the same shape <see cref="SpawnHealZone"/>
/// has, which places a thing in the world off two authored numbers and holds nothing afterwards.
/// </para>
/// <para>
/// <b>Both numbers are authored</b> (M3-05 rule 10's rule, five primitives later). CH §4.2's Exhume
/// is <em>"raise 3 Wights"</em>, and the radius is ours: 2.0 m is far enough that a Wight is not
/// inside the player's own silhouette and near enough to read as <em>yours</em>. The owner retunes
/// both in <c>Exhume.asset</c> rather than in a task. <b>No asset ships here</b> —
/// <c>RaiseMinions.asset</c> is M5-06b's.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The run's live objects —
/// the army, the blackboard, the clock — are the <em>handler</em>'s, which is the whole of
/// ADR-0009's split.
/// </para>
/// </remarks>
public sealed class RaiseMinions : IEffect
{
    /// <param name="count">
    /// How many to try to stand up. Strictly positive: a raise of nobody is a node whose whole
    /// behaviour is invisible.
    /// </param>
    /// <param name="radius">
    /// Metres from the player each one appears at. Strictly positive and finite — a ring with no
    /// extent stands three Wights inside the player, and an infinite one stands them outside the
    /// arena.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is not positive, or <paramref name="radius"/> is not a finite number
    /// greater than zero. Refused at this door as well as at <c>MinionSystem</c>'s, because this is
    /// where a mistake is <em>authored</em>: a content error caught when the asset is built is a
    /// content error, and the same error caught at the moment a player taps a thumb slot is a crash
    /// in a run (AR §18.3).
    /// </exception>
    public RaiseMinions(int count, float radius)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "RaiseMinions count must be at least 1. A cast that raises nobody is a node the "
                    + "player takes and gets nothing for.");
        }

        // `!(value > 0f)` rather than `value <= 0f`, so NaN is refused with everything below zero,
        // and infinity separately because it passes a `> 0` test (AR §18.3). A NaN radius would
        // spread into every position the ring is built from, and MinionSystem.Spawn would refuse
        // all three at its own door — which is the right refusal reported from the wrong place.
        if (!(radius > 0f) || float.IsInfinity(radius))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "RaiseMinions radius must be a finite number greater than zero. A ring with no "
                    + "extent stands every Wight inside the player.");
        }

        Count = count;
        Radius = radius;
    }

    /// <summary>How many to try to stand up. Three — CH §4.2's <em>"raise 3 Wights"</em>.</summary>
    public int Count { get; }

    /// <summary>Metres from the player each one appears at. 2.0 — see the remarks on this class.</summary>
    public float Radius { get; }
}

/// <summary>
/// What a <see cref="RaiseMinions"/> means to this run: <see cref="RaiseMinions.Count"/> Wights on a
/// derived ring around wherever the player is standing, on the run's own clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three run-scoped objects</b>, which is what M3-05 rule 1 means by <em>"the handler is built
/// per run and holds the run's live objects"</em>. It publishes nothing itself —
/// <c>MinionSystem.Spawn</c> announces each raise, because the system is what knows the id it
/// issued.
/// </para>
/// <para>
/// <b>It reads the player's position off the blackboard rather than taking one per cast</b>, which
/// is <see cref="SpawnHealZoneHandler"/>'s own shape and its reason:
/// <c>IEffectHandler&lt;T&gt;.Apply</c> is handed no position and no time, and
/// <c>CombatBlackboard.PlayerPosition</c> is written once a tick by one writer (ADR-0005) above
/// every reader in the frame.
/// </para>
/// <para>
/// <b>The clock is a <see cref="SimulatedClock"/> and never an <c>IClock</c>, for the third time in
/// three tasks that needed one.</b> A Wight's twenty seconds are counted in simulated time, so an
/// army raised before a level-up screen is still standing when the screen closes.
/// </para>
/// <para>
/// <b>No stream is consulted, and that is ADR-0011 rather than a simplification</b> (rule 10). A
/// draw is a change to what a seed means, and a cast the player chose the moment of is the worst
/// place to spend one: two runs on one seed would diverge on the frame a thumb landed. So the ring
/// is derived — evenly spaced, the first Wight on +X — which is <c>BossBehaviour</c>'s own placement
/// for a phase's adds, formula for formula.
/// </para>
/// <para>
/// <b>A refused spawn is silent</b> (rule 11). <c>MinionSystem.Spawn</c> returns null at the cap, so
/// Exhume at a full army raises fewer than three — or none — and neither throws nor reports:
/// <c>ProjectileSystem.Fire</c>'s bargain, and what CH §4.2's <em>"below half the cap"</em> trigger
/// exists to keep rare.
/// </para>
/// <para>
/// Allocates nothing: a loop of at most <c>Count</c> iterations over two trigonometric calls and a
/// struct position, into a system holding a preallocated army of
/// <see cref="MinionSystem.MaxConcurrent"/>.
/// </para>
/// </remarks>
public sealed class RaiseMinionsHandler : IEffectHandler<RaiseMinions>
{
    private readonly MinionSystem _minions;
    private readonly CombatBlackboard _blackboard;
    private readonly SimulatedClock _clock;

    /// <param name="minions">This run's army — the only thing that may stand a Wight up.</param>
    /// <param name="blackboard">
    /// Where the player is. Read rather than passed per cast — see the remarks on this class.
    /// </param>
    /// <param name="clock">The simulated second a raised Wight's twenty starts from.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// <b>A null <paramref name="minions"/> is refused rather than accepted as <em>"this class
    /// raises nothing"</em></b>, which is the opposite of <see cref="ModifyStatHandler"/>'s answer
    /// one file over, and the asymmetry is the point. A target is a <em>field</em> on a registered
    /// type, so <see cref="EffectRegistry.CanApply"/> cannot see it and the refusal has to be built;
    /// a verb is a <em>type</em>, so a run that cannot raise simply does not register this handler
    /// and <c>SkillTree</c>'s existing sweep refuses a tree that carries one, before
    /// <c>RunStarted</c>, naming the node. The cheaper door was already there.
    /// </remarks>
    public RaiseMinionsHandler(
        MinionSystem minions,
        CombatBlackboard blackboard,
        SimulatedClock clock)
    {
        _minions = minions ?? throw new ArgumentNullException(nameof(minions));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Stands up to <see cref="RaiseMinions.Count"/> Wights on a ring around the player.
    /// </summary>
    /// <param name="source">
    /// Who cast it — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8). Unread here: a Wight belongs
    /// to the run rather than to the node that raised it, and nothing ever takes one back.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public void Apply(RaiseMinions effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        Vector3 centre = _blackboard.PlayerPosition;
        float now = _clock.Now;

        float step = 2f * MathF.PI / effect.Count;

        for (int i = 0; i < effect.Count; i++)
        {
            float angle = i * step;

            // Y is the player's own, not zero: a Wight stands on the floor the player is standing
            // on, and core does not know how high that is (AR §18.4's XZ rule seen from the other
            // side — the plane is the plane, and the height is reported rather than invented).
            var at = new Vector3(
                centre.X + (MathF.Cos(angle) * effect.Radius),
                centre.Y,
                centre.Z + (MathF.Sin(angle) * effect.Radius));

            // The return is deliberately dropped. Null is the cap refusing a raise, which is not an
            // error and not an event — see the remarks on this class.
            _minions.Spawn(at, now);
        }
    }

    /// <summary>
    /// Does nothing: a Wight owns its own twenty seconds (rule 11).
    /// </summary>
    /// <remarks>
    /// <see cref="SpawnHealZoneHandler.Remove"/>'s answer verbatim, and for its reason.
    /// <c>IEffectHandler&lt;T&gt;.Remove</c> says a source holding nothing is not an error —
    /// <c>Stat.RemoveAll</c>'s contract, and what lets a caller clean up unconditionally — and this
    /// handler's answer is that <em>every</em> source holds nothing, because what a cast left behind
    /// is a body on a clock rather than a modifier. <b>Nothing calls it</b>: no minion is ever held
    /// in <c>TimedEffects</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null. Refused even though nothing
    /// afterwards would read either, so that this door and <c>GrantShieldHandler</c>'s answer a
    /// mis-wired caller the same way rather than one of them silently accepting a removal that names
    /// nobody.
    /// </exception>
    public void Remove(RaiseMinions effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
    }
}
