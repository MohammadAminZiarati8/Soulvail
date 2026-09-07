using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants the body to do with the player this tick: move at this velocity, face this
/// way. The first of the intents AR §4.1 sends out every frame — core writes it to
/// <c>IIntentSink</c> and never touches a transform itself. See ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and the constructor neither normalises nor validates. Core has already decided
/// both vectors by the time it builds one; re-deriving <see cref="Facing"/> here would put the
/// same computation in two places, and the copy nobody is watching is the one that goes wrong.
/// The invariants below are core's to keep — this type only carries them.
/// </para>
/// <para>
/// A <c>readonly struct</c>, taken by <c>in</c> at the port, so crossing the boundary costs no
/// copy and no box. It crosses 60 times a second.
/// </para>
/// <para>
/// The two public fields are readonly on an immutable value, which is the shape a carrier of
/// exactly two numbers should have; properties would hide nothing and add a call per read per
/// frame. Same reasoning as <see cref="EnemySense"/>, one step safer.
/// </para>
/// </remarks>
public readonly struct PlayerMoveIntent
{
    /// <summary>Desired velocity in world units per second, on the ground plane (<c>Y == 0</c>).</summary>
    public readonly Vector3 Velocity;

    /// <summary>
    /// Desired facing, a unit vector on the ground plane (<c>Y == 0</c>).
    /// </summary>
    /// <remarks>
    /// Carried separately rather than derived from <see cref="Velocity"/>, because the player
    /// keeps facing a target while strafing away from it or standing still — which is the whole
    /// point of auto-aim.
    /// </remarks>
    public readonly Vector3 Facing;

    public PlayerMoveIntent(Vector3 velocity, Vector3 facing)
    {
        Velocity = velocity;
        Facing = facing;
    }
}
