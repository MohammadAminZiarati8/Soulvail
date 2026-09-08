using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Player.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The body. Core decided a velocity and a facing; this moves the capsule and turns it, and
    /// decides nothing at all. See AR §3 and CC §2.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reads no input, holds no gameplay state and never touches <c>RunState</c>. Everything
    /// it applies arrives in a <see cref="PlayerMoveIntent"/>, and everything it knows goes back
    /// out through <see cref="Position"/> and <see cref="Velocity"/>, which the snapshot builder
    /// reports as senses. If an <c>if</c> about gameplay ever wants to live here, it belongs in
    /// core.
    /// </para>
    /// <para>
    /// Gravity is the one number here that core does not own, and it is not a gameplay decision:
    /// there is no jump, no fall damage and no airborne state in this game's design — the
    /// downward push exists only so <c>CharacterController</c> keeps contact with the floor and
    /// reports <see cref="CharacterController.isGrounded"/> honestly. Nothing in core can observe
    /// it, because the snapshot's player position comes back from the controller either way.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerView : MonoBehaviour
    {
        /// <summary>
        /// Downward acceleration while airborne, in m/s². Steeper than Earth's, which is the
        /// convention in top-down action games: the character should settle onto a step or a ramp
        /// immediately rather than float down it.
        /// </summary>
        private const float Gravity = -20f;

        /// <summary>
        /// The downward speed held while grounded. Not zero: <c>CharacterController.isGrounded</c>
        /// is a report about the *last* <c>Move</c>, so a controller pushed with no vertical
        /// component drifts off the ground for a frame at a time and the flag flickers. A small
        /// constant push keeps it pinned and is invisible at any frame rate.
        /// </summary>
        private const float GroundedFallSpeed = -1f;

        private CharacterController _controller;
        private Vector3 _velocity;
        private float _fallSpeed;

        /// <summary>Where the body is, for the snapshot to report back to core.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// The horizontal velocity last applied — what core asked for, not what the controller
        /// achieved.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>CharacterController.velocity</c>, which is the resolved
        /// displacement over the last frame divided by <c>dt</c>. That number collapses to zero
        /// the moment the player leans on a wall, and core reading it back as
        /// <c>PlayerVelocity</c> would see a player who had stopped trying to move rather than one
        /// who is blocked — a difference that matters the first time a mechanic asks "is the
        /// player moving?". Position is a sense because collision genuinely changes it; intended
        /// velocity is not collision's to edit.
        /// </remarks>
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// Applies one tick's intent: move at this velocity, face this way.
        /// </summary>
        /// <param name="intent">What core decided this tick.</param>
        /// <param name="dt">
        /// The same step core integrated with — <c>snapshot.Dt</c>, clamped by
        /// <see cref="SnapshotBuilder.MaxDt"/>, never <c>Time.deltaTime</c>. Brain and body
        /// disagreeing about how much time passed is how a hitch becomes a teleport.
        /// </param>
        public void Apply(in PlayerMoveIntent intent, float dt)
        {
            Vector3 horizontal = intent.Velocity.ToUnity();

            _fallSpeed = _controller.isGrounded ? GroundedFallSpeed : _fallSpeed + (Gravity * dt);

            // One Move per frame, with both components together. Two calls would resolve collision
            // twice and let a horizontal push climb what a vertical one had just settled onto.
            _controller.Move((horizontal + new Vector3(0f, _fallSpeed, 0f)) * dt);

            _velocity = horizontal;

            // No smoothing: core already turned at 720°/s (CC §2.4), so easing here would be a
            // second rotation curve on top of the one the design specifies. Guarded because
            // LookRotation of a zero vector logs an error and leaves the rotation unchanged, and
            // core is free to send a facing it has not established yet.
            Vector3 facing = intent.Facing.ToUnity();

            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _controller = GetComponent<CharacterController>();
        }
    }
}
