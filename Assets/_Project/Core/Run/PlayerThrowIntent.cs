using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants the body to do with a player an enemy has just thrown: move the character this
/// far, this way, over this long (JG-25b). Gravemaw's eruption is the first thrower.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ChargeIntent"/>'s shape, for its reasons.</b> Issued once per throw, no origin,
/// and a distance rather than a speed — the distance is a budget the body walks until a wall stops
/// it, which is how a throw is "stopped by walls" while core holds no walls (AR §18.2).
/// </para>
/// <para>
/// <b>A separate intent, not a <see cref="ChargeIntent"/>,</b> because the body sweeps enemies
/// along a dash and reports them for damage; a thrown player hurts nobody it is thrown through.
/// </para>
/// </remarks>
public readonly struct PlayerThrowIntent
{
    /// <summary>The direction to throw, a unit vector on the ground plane (<c>X</c>, <c>Z</c>).</summary>
    public readonly Vector2 DirectionXZ;

    /// <summary>How far, in metres, if nothing is in the way.</summary>
    public readonly float Distance;

    /// <summary>How long it takes, in seconds — the same window core holds the motor for.</summary>
    public readonly float Duration;

    public PlayerThrowIntent(Vector2 directionXZ, float distance, float duration)
    {
        DirectionXZ = directionXZ;
        Distance = distance;
        Duration = duration;
    }
}
