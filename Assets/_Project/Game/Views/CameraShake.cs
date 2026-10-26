using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. A file-scoped namespace would leave
// the Run scene's reference to this component deserialising as null with nothing reported
// (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The thud a heavy landing puts through the screen (JG-06b): a damped wobble that
    /// <see cref="FollowCamera"/> adds on top of where it is following, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An offset, not a move.</b> The camera smooths toward the player from where it was last
    /// frame. A shake written into that position would be smoothed into the follow and drift it, so
    /// the follow keeps its own position and this is added after, which ends the shake exactly at rest.
    /// </para>
    /// <para>
    /// <b>A wobble that decays, not noise.</b> Down first and back up at 12 Hz, a smaller sway across
    /// at 9 Hz, both under an amplitude falling as (1 − t)² to zero at <see cref="Seconds"/>. That
    /// reads as one heavy hit and settles, and it needs no random numbers at all.
    /// </para>
    /// <para>
    /// <b>It owns no frame loop.</b> <see cref="FollowCamera"/> steps it in its own
    /// <c>LateUpdate</c>, right before reading <see cref="Offset"/>, so the order is fixed rather than
    /// left to Unity's script order. The step is <c>Time.deltaTime</c>: the shake is cosmetic, and
    /// a paused run's zero time scale freezes it mid-swing until Resume.
    /// </para>
    /// </remarks>
    public sealed class CameraShake : MonoBehaviour
    {
        /// <summary>The first swing of a slam's landing, in metres.</summary>
        public const float SlamStrength = 0.35f;

        /// <summary>Seconds from a kick to rest.</summary>
        public const float Seconds = 0.4f;

        /// <summary>The vertical bounce, in swings per second.</summary>
        private const float BounceHz = 12f;

        /// <summary>The sideways sway, in swings per second, and its share of the strength.</summary>
        private const float SwayHz = 9f;

        private const float SwayShare = 0.5f;

        private float _strength;
        private float _age;
        private bool _shaking;

        /// <summary>Where the camera is pushed from its follow position now, in world metres.</summary>
        public Vector3 Offset
        {
            get
            {
                if (!_shaking)
                {
                    return Vector3.zero;
                }

                float u = _age / Seconds;
                float envelope = _strength * (1f - u) * (1f - u);
                float phase = _age * Mathf.PI * 2f;

                return new Vector3(
                    envelope * SwayShare * Mathf.Sin(phase * SwayHz),
                    -envelope * Mathf.Cos(phase * BounceHz),
                    0f);
            }
        }

        /// <summary>True from a <see cref="Kick"/> until the shake has settled.</summary>
        public bool IsShaking => _shaking;

        /// <summary>
        /// Starts a shake of <paramref name="strength"/> metres. During a shake the decay restarts at
        /// the larger of the two strengths: kicks never add up, and a weak one never cuts a strong one
        /// short. Zero, negative or non-finite strengths are ignored.
        /// </summary>
        public void Kick(float strength)
        {
            if (!(strength > 0f) || float.IsInfinity(strength))
            {
                return;
            }

            _strength = _shaking ? Mathf.Max(_strength, strength) : strength;
            _age = 0f;
            _shaking = true;
        }

        /// <summary>
        /// Advances the shake by <paramref name="dt"/> seconds; at <see cref="Seconds"/> it rests at
        /// zero. A zero, negative or non-finite step does nothing.
        /// </summary>
        public void Step(float dt)
        {
            if (!_shaking || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _age += dt;

            if (_age >= Seconds)
            {
                _shaking = false;
                _age = 0f;
                _strength = 0f;
            }
        }

        private void OnDisable()
        {
            // A camera switched off mid-shake comes back still.
            _shaking = false;
            _age = 0f;
            _strength = 0f;
        }
    }
}
