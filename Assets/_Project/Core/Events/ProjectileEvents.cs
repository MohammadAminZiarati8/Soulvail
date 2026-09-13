using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The projectile module's domain events. Grouped in one file like EnemyEvents and RunEvents, for
// the same reason: an event is three lines, and reading a module's vocabulary in one place is worth
// more than one type per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// A pair, and the pairing is the whole shape of M2-07a: a shot leaves, a shot arrives, and nothing
// is said in between. Core holds no per-tick position for one (`ProjectileSystem`, rule 2), so
// `ProjectileFired` has to carry the entire flight — where it started, where it lands, how long it
// takes — and the view draws the arc itself. That is the same bargain `EnemyKnockbackIntent` makes
// for a shove, and the reason there is no `ProjectileMoved`.

/// <summary>
/// A shot has left. Published by <c>ProjectileSystem.Fire</c> the instant it is accepted, and never
/// for one that was refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries everything a view needs to draw the whole flight without asking again</b> (M2-09):
/// <see cref="Origin"/>, <see cref="Target"/> and <see cref="FlightTime"/> are a complete
/// description of the arc, so a view can interpolate between two points over a known span and needs
/// neither a catalog nor a per-frame message. Anything that wants the shooter's position wants
/// <see cref="Origin"/>, not <see cref="SourceId"/> — see that field.
/// </para>
/// <para>
/// <see cref="SpecId"/> is the <em>archetype that fired it</em> rather than a projectile's own
/// content id, because there is no projectile content type: a shot carries the numbers it was fired
/// with, and a <c>ProjectileDefinition</c> asset arrives when a second archetype needs different
/// ones. It is here so a view can pick a mesh per shooter.
/// </para>
/// </remarks>
public readonly struct ProjectileFired
{
    /// <summary>The run-stable id this shot will be named by until it lands. Issued from 1.</summary>
    public readonly int Id;

    /// <summary>Which archetype fired it, e.g. <c>enemy.spitter</c>.</summary>
    public readonly ContentId SpecId;

    /// <summary>
    /// Which enemy fired it, or 0 for nobody.
    /// </summary>
    /// <remarks>
    /// <b>A record of who fired, not a handle.</b> A shot outlives its shooter
    /// (<c>ProjectileSystem</c>, rule 5), so this id may already have despawned by the time the
    /// event is handled and will certainly have done so by the time it lands. Resolving it through
    /// the registry is the mistake this remark exists to prevent; a view that wants a muzzle
    /// position uses <see cref="Origin"/>, which is where the shot actually came from.
    /// </remarks>
    public readonly int SourceId;

    /// <summary>Where it left from, in world metres — the shooter's position at the moment of firing.</summary>
    public readonly Vector3 Origin;

    /// <summary>
    /// The point on the ground it will arrive at, in world metres. Decided once, at the moment of
    /// firing, and it never tracks the player (<c>ProjectileSystem</c>, rule 4).
    /// </summary>
    public readonly Vector3 Target;

    /// <summary>
    /// Seconds from now until it arrives — the XZ distance over the speed it was fired with.
    /// </summary>
    /// <remarks>
    /// May be zero, for a shot fired from the point it is aimed at. A view that divides by this
    /// draws nothing for a frame and is then told the shot impacted, which is the correct rendering
    /// of a shot that had no flight to draw.
    /// </remarks>
    public readonly float FlightTime;

    public ProjectileFired(
        int id,
        ContentId specId,
        int sourceId,
        Vector3 origin,
        Vector3 target,
        float flightTime)
    {
        Id = id;
        SpecId = specId;
        SourceId = sourceId;
        Origin = origin;
        Target = target;
        FlightTime = flightTime;
    }
}

/// <summary>
/// A shot has arrived. Published by <c>ProjectileSystem.Tick</c> on the tick its arrival time has
/// passed, exactly once per shot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published whether or not it hit anything</b>, because the view has to stop existing either
/// way — this is the mirror of <c>EnemyDespawned</c> rather than of <c>EnemyDied</c>. A view that
/// was told only about hits would leave every missed bolt hanging in the air.
/// </para>
/// <para>
/// The damage, if there was any, has already been applied and announced by the time this is
/// published: <c>PlayerCombat.ApplyDamage</c> publishes <c>PlayerDamaged</c> — and
/// <c>PlayerDied</c> — from inside the same landing. <see cref="HitPlayer"/> is therefore a fact
/// about the geometry, not about the outcome: a shot that arrives on a dodging player is a hit here
/// and a blocked <c>PlayerDamaged</c> there (<c>ProjectileSystem</c>, rule 6).
/// </para>
/// </remarks>
public readonly struct ProjectileImpacted
{
    /// <summary>The id that has just stopped being in flight.</summary>
    public readonly int Id;

    /// <summary>
    /// Where it landed, in world metres: the target point it was fired at, always.
    /// </summary>
    /// <remarks>
    /// The same <c>Target</c> the <see cref="ProjectileFired"/> carried, repeated rather than left
    /// to be correlated by id, for the reason <c>EnemyDied</c> repeats a position: an impact VFX has
    /// to be placed, and making every listener hold a table of shots in flight to find out where
    /// would be a state machine per projectile in every view.
    /// </remarks>
    public readonly Vector3 Position;

    /// <summary>The player was inside the shot's radius when it arrived.</summary>
    public readonly bool HitPlayer;

    public ProjectileImpacted(int id, Vector3 position, bool hitPlayer)
    {
        Id = id;
        Position = position;
        HitPlayer = hitPlayer;
    }
}
