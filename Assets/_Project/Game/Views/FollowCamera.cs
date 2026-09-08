using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and the Run scene's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The eye. Holds a fixed top-down angle behind the player and slides to keep up. See GD §5.1
    /// for the perspective and GD §7.2 for why it never collides with anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It follows and nothing else. The rotation is authored, not derived — the camera never
    /// re-aims, never rolls and never leads the player, so the ground plane keeps one orientation
    /// for the whole run and a pillar is the same shape wherever it sits on screen. Every ounce of
    /// "where is the camera looking" in this game is a constant; what moves is only where it is.
    /// </para>
    /// <para>
    /// <b>Yaw is 0, and that is load-bearing.</b> <c>SnapshotBuilder</c> maps the stick straight
    /// through — X to world X, Y to world Z — which is camera-relative only because this camera is
    /// unrotated about Y. The day this field becomes non-zero, the builder must rotate
    /// <c>MoveInput</c> by −yaw before core sees it, or up on the thumb stops being up on screen.
    /// The rotation belongs there, in the senses, and never in core: core is handed a world-space
    /// stick and must never learn that a camera exists.
    /// </para>
    /// <para>
    /// <see cref="LateUpdate"/> rather than <c>Update</c>, because the body moves inside
    /// <c>RunTicker.Tick</c> — an <c>ITickable</c>, so the Update phase — and a camera that reads
    /// the player's position in the same phase would be racing it. Following a stale position by
    /// one frame is exactly the jitter this smoothing is supposed to remove.
    /// </para>
    /// </remarks>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        /// <summary>Degrees below the horizon. 57° reads as top-down while still showing a
        /// silhouette's front, which is what makes facing legible at a glance.</summary>
        [SerializeField] private float _pitchDeg = 57f;

        /// <summary>Rotation about world Y. Must stay 0 — see the type's remarks.</summary>
        [SerializeField] private float _yawDeg;

        /// <summary>Metres from the player along the view axis.</summary>
        [SerializeField] private float _distance = 16f;

        /// <summary>
        /// Seconds the camera takes to close most of the gap to the player. Small enough that the
        /// player never outruns the frame, large enough that a step into a wall does not snap.
        /// </summary>
        [SerializeField] private float _smoothTime = 0.12f;

        private Vector3 _followVelocity;

        /// <remarks>
        /// Validated once, loudly, rather than per frame: an unassigned target is a wiring mistake
        /// that should fail the moment the scene opens, and a message here costs nothing, while the
        /// same check in <see cref="LateUpdate"/> would log sixty times a second and bury itself.
        /// The camera is also placed immediately — smoothing in from wherever the camera was
        /// authored would open every run with a swoop that is not a design decision.
        /// </remarks>
        /// <exception cref="MissingReferenceException">No target is assigned.</exception>
        private void OnEnable()
        {
            if (_target == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(FollowCamera)} has no target assigned. Drag the Player object in " +
                    "this scene onto its Target field — without it the camera has nothing to " +
                    "follow and the run is played off-screen.");
            }

            _followVelocity = Vector3.zero;

            transform.SetPositionAndRotation(Desired(Rotation()), Rotation());
        }

        private void LateUpdate()
        {
            // Unity's == rather than `is null`: on scene unload the player may be destroyed before
            // this camera is, leaving a live reference that only compares equal to null through the
            // engine's operator. One frame of a stale view beats a NullReferenceException on the
            // way out of a run.
            if (_target == null)
            {
                return;
            }

            Quaternion rotation = Rotation();

            // Time.deltaTime, not the snapshot's clamped Dt, and the distinction matters. Everything
            // downstream of SnapshotBuilder integrates a step and would diverge from core if it used
            // a different one; this chases a position that is already correct, so a longer step only
            // means the camera arrives sooner. There is nothing here to accumulate.
            transform.SetPositionAndRotation(
                Vector3.SmoothDamp(transform.position, Desired(rotation), ref _followVelocity, _smoothTime),
                rotation);
        }

        /// <summary>The authored orientation, rebuilt each frame so the angle can be tuned in the
        /// Inspector while the game is running — which is how M0-20 tunes it.</summary>
        private Quaternion Rotation()
        {
            return Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        }

        /// <summary>Where the camera wants to be: back along its own view axis from the player.</summary>
        private Vector3 Desired(Quaternion rotation)
        {
            return _target.position + (rotation * Vector3.back * _distance);
        }
    }
}
