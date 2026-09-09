using System.Numerics;

namespace Soulvail.Core.Run;

/// <summary>
/// What core wants one enemy's body to do this tick: travel at this velocity, look this way. The
/// enemy counterpart of <see cref="PlayerMoveIntent"/>, and the instruction every walking archetype
/// from the Husk onward is moved by. See AR §4.1, §6 and ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// <b>A velocity rather than a destination, for the reason core never assigns a position.</b> Where
/// a Husk actually ends up is decided by the wall it walks into, the pillar it rounds and the other
/// Husk already standing there — all things only the body can see. Core says how fast and which way;
/// where that got to comes back in the next snapshot like every other position (AR §4.2).
/// </para>
/// <para>
/// <b>Named by id, like <see cref="EnemyKnockbackIntent"/> and unlike the player's.</b> There is one
/// player and sixty enemies, so an intent about an enemy has to say which one. An <c>EnemyAgent</c>
/// handle would be a core object crossing the boundary, which AR §3 does not allow, and a position
/// would be one frame stale by the time the body read it.
/// </para>
/// <para>
/// <b>Facing is carried separately from velocity, and the two disagree on purpose.</b> A chaser
/// winding up its strike stands still and stares at the player — zero velocity, a live facing — and
/// deriving the look direction from the movement would make it turn away the instant it stopped,
/// which is exactly the frame the telegraph has to read from. The same split
/// <see cref="PlayerMoveIntent"/> makes, for the same reason.
/// </para>
/// <para>
/// <b>Several per tick, one per living enemy.</b> Unlike <see cref="PlayerMoveIntent"/> — a state
/// where the newest is the only one that matters — these accumulate, because they are about
/// different bodies and dropping one because a second arrived would leave that enemy standing still.
/// Within one tick an enemy is named at most once.
/// </para>
/// </remarks>
public readonly struct EnemyMoveIntent
{
    /// <summary>
    /// Which enemy to move — an id from <c>EnemyRegistry</c>, the same one <c>EnemySpawned</c> named
    /// and the body reports back through the snapshot.
    /// </summary>
    public readonly int Id;

    /// <summary>
    /// How fast to travel, in metres per second. Horizontal: <c>Y</c> is always zero, because
    /// gravity is the body's to apply and nothing in this game's design leaves the ground.
    /// </summary>
    /// <remarks>
    /// Zero is a normal value and is emitted every tick an enemy is deliberately standing still — a
    /// windup, a recovery, an idle. A tick that emitted nothing instead would leave the body
    /// applying whatever it last read, which is the sliding-forever bug <c>IntentBuffer</c> warns
    /// about from the other end.
    /// </remarks>
    public readonly Vector3 Velocity;

    /// <summary>
    /// The direction to look, as a vector on the ground plane — <c>X</c> and <c>Z</c>.
    /// </summary>
    /// <remarks>
    /// Zero means "core has no opinion this tick", and a body reads it as "keep the facing you
    /// have" rather than as a rotation to snap to. It is what an idle enemy that has never seen the
    /// player sends, and what an enemy standing exactly where the player is sends — there is no
    /// direction between two identical points, and inventing one would spin the body on noise.
    /// </remarks>
    public readonly Vector2 FacingXZ;

    public EnemyMoveIntent(int id, Vector3 velocity, Vector2 facingXZ)
    {
        Id = id;
        Velocity = velocity;
        FacingXZ = facingXZ;
    }
}
