using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core is told about one enemy this frame: purely spatial facts it cannot derive.
/// Everything else about an enemy — hp, state, cooldowns, whether it is elite — core
/// already owns, so none of it belongs here.
/// </summary>
/// <remarks>
/// A mutable struct, filled in place through <see cref="WorldSnapshot.AddEnemy"/>. That is
/// unusual by convention and deliberate here: the array of these is preallocated once and
/// rewritten every frame, so a copy-on-write shape would allocate exactly where the
/// architecture says nothing may. M1-06 extends it with the senses combat needs.
/// </remarks>
public struct EnemySense
{
    /// <summary>Stable identity for the run. Core answers in ids; views resolve them to objects.</summary>
    public int Id;

    public Vector3 Position;

    public Vector3 Velocity;

    /// <summary>
    /// Unit XZ direction along the NavMesh path towards the player, or zero when there is no
    /// path. A sense, not an instruction — core decides what to do about it.
    /// </summary>
    public Vector2 PathDirectionToPlayer;

    public bool HasLineOfSight;
}
