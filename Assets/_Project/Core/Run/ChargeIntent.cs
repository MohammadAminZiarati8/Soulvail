using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants the body to do about a dash that has just begun: move the character this far,
/// this way, over this long, and stop asking core where it should be until it is over. CC §5's
/// 10 m of travel, and the one intent that takes movement away from the stick. See AR §4.1, §6 and
/// ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// <b>Issued once per dash, not once per frame.</b> Every other movement instruction core sends is
/// a velocity for the tick it was computed in — <see cref="PlayerMoveIntent"/> goes out sixty times
/// a second precisely because the stick can change between any two of them. A dash cannot: CC §5
/// fixes its direction at the press and refuses to steer, so the whole of it is describable in
/// advance and the body is handed the plan rather than a stream of samples. For the length of
/// <see cref="Duration"/> core then emits no <see cref="PlayerMoveIntent"/> at all, which is what
/// stops two instructions arguing about where the character is going.
/// </para>
/// <para>
/// <b>It carries no origin, unlike <see cref="ConeHitIntent"/>.</b> A cone is resolved against the
/// world as it was when the swing landed, so its geometry has to travel with it; a dash starts from
/// wherever the character is at the moment the body reads this, which is the same frame it was
/// written. Sending a start position would invite the body to teleport back to it.
/// </para>
/// <para>
/// <b>Distance, not speed.</b> 10 m over 0.22 s is ≈45 m/s, and the body can divide — but the two
/// numbers the design fixes are the reach and the commitment, and a speed would hide the fact that
/// a longer dash is a longer dash rather than a faster one. It is also what lets the body stop
/// early against a wall without core having to be told: the distance is a budget, not a promise
/// (see the M1-15 spec's out-of-scope note).
/// </para>
/// <para>
/// A <c>readonly struct</c>, taken by <c>in</c> at the port, like every other intent — though this
/// one crosses the boundary once every two and a half seconds at best.
/// </para>
/// </remarks>
public readonly struct ChargeIntent
{
    /// <summary>
    /// The direction to dash, as a unit vector on the ground plane — <c>X</c> and <c>Z</c>, with
    /// the height dropped.
    /// </summary>
    /// <remarks>
    /// The stick as it was at the moment the dash started, or the character's facing when the stick
    /// was neutral (CC §5). Decided by <c>ChargeSkill</c> and never revisited: this vector is the
    /// commitment the player made, and a body that re-read the stick while executing it would be
    /// implementing a different mechanic.
    /// </remarks>
    public readonly Vector2 DirectionXZ;

    /// <summary>How far the dash travels, in metres. 10 for the Charge.</summary>
    public readonly float Distance;

    /// <summary>
    /// How long it takes, in seconds — 0.22 for the Charge. The same window core suspends the
    /// motor for, so a body that took longer would be moving while core had already handed control
    /// back to the stick.
    /// </summary>
    public readonly float Duration;

    public ChargeIntent(Vector2 directionXZ, float distance, float duration)
    {
        DirectionXZ = directionXZ;
        Distance = distance;
        Duration = duration;
    }
}
