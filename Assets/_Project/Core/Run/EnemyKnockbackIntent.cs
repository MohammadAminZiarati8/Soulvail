using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants the body to do about an enemy a dash has just passed through: shove it this far,
/// this way. CC §5's 5 m of knockback, and the first intent core issues about something that is not
/// the player. See AR §4.1, §6 and ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// <b>An intent rather than a position, for the reason core never assigns one.</b> Being pushed 5 m
/// means being pushed until something stops you — a wall, a pillar, the arena's edge — and only the
/// body knows where those are. Core says how hard and which way; where the enemy ends up comes back
/// in the next snapshot like every other position (AR §4.2). Writing a destination here would be
/// core deciding a collision it cannot see.
/// </para>
/// <para>
/// <b>Named by id, because the intent outlives the tick that produced it.</b> Knockbacks are
/// written while core is resolving a <c>ReportChargeHits</c> fact, which arrives between frames, and
/// the body applies them against agents it looks up by the same id it reported. An
/// <c>EnemyAgent</c> handle would be a core object crossing the boundary, which AR §3 does not
/// allow, and a position would be one frame stale by the time it was used.
/// </para>
/// <para>
/// <b>Several per report, not one.</b> A dash through a knot of Husks knocks back every one of
/// them, so unlike <see cref="PlayerMoveIntent"/> — a state, where the newest is the only one that
/// matters — these accumulate, the same way <see cref="ConeHitIntent"/> does and for the same
/// reason: dropping one because a second arrived would lose an enemy's whole reaction.
/// </para>
/// <para>
/// M2-07's Spitter and M3-11's Consecrate will want the same instruction for the same reason, which
/// is why the name says <em>enemy</em> and <em>knockback</em> rather than <em>charge</em>: nothing
/// about this struct is about a dash, and the day something else pushes an enemy it reuses this
/// rather than growing a parallel one.
/// </para>
/// </remarks>
public readonly struct EnemyKnockbackIntent
{
    /// <summary>
    /// Which enemy to push — an id from <c>EnemyRegistry</c>, the same one <c>EnemySpawned</c>
    /// named and the body reported back.
    /// </summary>
    public readonly int Id;

    /// <summary>
    /// The direction to push, as a unit vector on the ground plane — <c>X</c> and <c>Z</c>.
    /// </summary>
    /// <remarks>
    /// The dash's direction, not the direction from the player to the enemy, and the difference is
    /// visible: everything a Charge passes through is swept the same way, which reads as a plough
    /// rather than as an explosion. Radial knockback is a different mechanic and will carry its own
    /// direction when something needs it.
    /// </remarks>
    public readonly Vector2 DirectionXZ;

    /// <summary>How far to push, in metres. 5 for the Charge.</summary>
    /// <remarks>
    /// A distance rather than an impulse, for the reason <see cref="ChargeIntent.Distance"/> is one:
    /// it is the number CC §5 authors, and how fast the shove resolves is the body's to choose.
    /// Zero is legal and means a hit that does not move anything.
    /// </remarks>
    public readonly float Distance;

    public EnemyKnockbackIntent(int id, Vector2 directionXZ, float distance)
    {
        Id = id;
        DirectionXZ = directionXZ;
        Distance = distance;
    }
}
