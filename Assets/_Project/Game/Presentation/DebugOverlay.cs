using System;
using System.Globalization;
using System.Text;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, so DebugOverlay.prefab's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// What core is seeing and what it decided, in one line of text — so tuning on a phone is done
    /// against numbers rather than against a feeling. Required by the CC §8 checklist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reads the two ends of the frame and nothing in between: the snapshot core was handed and
    /// the intent core wrote back. That is deliberate — the overlay is a window onto the boundary
    /// of AR §4.3, so if the capsule is doing something the numbers do not explain, the fault is in
    /// the body, and if the numbers themselves are wrong, the fault is upstream of core. Reading
    /// <c>PlayerView</c> instead would collapse both cases into one.
    /// </para>
    /// <para>
    /// Development only. It removes itself in <see cref="Awake"/> outside the Editor and
    /// development builds, so the release APK never carries it on screen — M0-19's manual step 3 is
    /// the check that this actually holds.
    /// </para>
    /// </remarks>
    public sealed class DebugOverlay : MonoBehaviour
    {
        /// <summary>Seconds between refreshes. Ten a second is faster than an eye reads a changing
        /// number and slow enough that the text is legible rather than a blur.</summary>
        private const float RefreshInterval = 0.1f;

        /// <summary>Roughly the window the frame rate is averaged over, in seconds. Long enough
        /// that a single slow frame does not dominate, short enough that a real drop shows up
        /// while the thumb that caused it is still moving.</summary>
        private const float FpsSmoothing = 0.5f;

        [SerializeField] private TMP_Text _text;

        /// <summary>Keeps the overlay on in a non-development build. Off by default; it exists so a
        /// release build can be diagnosed without becoming a development build, which would change
        /// the very frame rate being diagnosed.</summary>
        [SerializeField] private bool _forceVisible;

        /// <remarks>
        /// Preallocated and rewritten in place. <c>SetText(StringBuilder)</c> copies straight into
        /// TMP's backing array, so the text never becomes a new string — which is the whole reason
        /// the spec asks for a builder rather than concatenation.
        /// </remarks>
        private readonly StringBuilder _line = new StringBuilder(96);

        private WorldSnapshot _snapshot;
        private IntentBuffer _intents;
        private float _untilRefresh;
        private float _fps;

        /// <param name="snapshot">The run's one snapshot — what core was told this frame.</param>
        /// <param name="intents">The run's intent buffer — what core decided this frame.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(WorldSnapshot snapshot, IntentBuffer intents)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        }

        private void Awake()
        {
            if (Debug.isDebugBuild || Application.isEditor || _forceVisible)
            {
                return;
            }

            // Not merely hidden: disabled, so nothing below ever runs and the release build pays
            // neither the string formatting nor the canvas rebuild.
            gameObject.SetActive(false);
        }

        /// <remarks>
        /// The injection check lives here rather than in <c>Awake</c> or <c>OnEnable</c>, and the
        /// reason is ordering. <c>RunScope</c> builds its container — and injects this component —
        /// from its own <c>Awake</c>, and Unity gives no order between two <c>Awake</c> calls in a
        /// scene. <c>Start</c> is the first moment every <c>Awake</c> in the scene is guaranteed to
        /// have run, so it is the earliest point at which "not injected" is a real conclusion
        /// rather than a race.
        /// </remarks>
        /// <exception cref="MissingReferenceException">No text field is assigned.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this overlay.</exception>
        private void Start()
        {
            if (_text == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(DebugOverlay)} has no {nameof(TMP_Text)} assigned. Drag the Line " +
                    "object on this prefab onto its Text field — without it the overlay has " +
                    "nowhere to write.");
            }

            if (_snapshot is null || _intents is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(DebugOverlay)} was never injected. Drag this object onto " +
                    "RunScope's Debug Overlay field — the overlay reads the run's snapshot and " +
                    "intent buffer, and only the run's scope can hand it those.");
            }

            // Written once immediately, so an overlay that shows nothing is unambiguous evidence
            // that the run never started, rather than a refresh that has not come round yet.
            Refresh();
        }

        /// <remarks>
        /// <see cref="LateUpdate"/> because <c>RunTicker</c> is an <c>ITickable</c> and therefore
        /// runs in the Update phase: by LateUpdate the snapshot has been built, core has ticked and
        /// the intent has been written and applied, so every number on screen belongs to the frame
        /// the player is looking at. In <c>Update</c> the overlay would be a frame behind, and a
        /// tuning aid that lags the thing being tuned is worse than none.
        /// </remarks>
        private void LateUpdate()
        {
            // Sampled every frame even though it is drawn ten times a second — an average that only
            // saw every sixth frame would miss exactly the stutter it exists to catch.
            float dt = Time.unscaledDeltaTime;

            if (dt > 0f)
            {
                float instant = 1f / dt;

                // Seeded rather than eased on the first frame: starting from zero would spend half
                // a second climbing to the truth and read as a hitch that never happened.
                _fps = _fps <= 0f ? instant : Mathf.Lerp(_fps, instant, dt / FpsSmoothing);
            }

            _untilRefresh -= dt;

            if (_untilRefresh > 0f)
            {
                return;
            }

            _untilRefresh = RefreshInterval;

            Refresh();
        }

        private void Refresh()
        {
            // var, not an explicit type: these are System.Numerics vectors and UnityEngine has its
            // own Vector2/Vector3, so naming the type here would need an alias or a qualification
            // for no gain.
            var input = _snapshot.MoveInput;

            bool moving = _intents.HasPlayerMove;

            // The flag is checked, not assumed. IntentBuffer.Clear leaves the previous intent in
            // place (M0-06), so reading it unconditionally would show the last velocity core ever
            // asked for, frozen on screen, long after the run stopped asking — an overlay that
            // lies about the exact thing it exists to prove.
            var intent = _intents.PlayerMove;

            float speed = moving ? intent.Velocity.Length() : 0f;
            float faceX = moving ? intent.Facing.X : 0f;
            float faceZ = moving ? intent.Facing.Z : 0f;

            _line.Clear();
            _line.Append("in ").Append(Fixed(input.X)).Append(' ').Append(Fixed(input.Y));
            _line.Append("  |v| ").Append(Fixed(speed));
            _line.Append("  face ").Append(Fixed(faceX)).Append(' ').Append(Fixed(faceZ));
            _line.Append("  fps ").Append(Mathf.RoundToInt(_fps).ToString(CultureInfo.InvariantCulture));

            _text.SetText(_line);
        }

        /// <summary>
        /// Two decimals, invariant. The culture is pinned because a phone set to a comma-decimal
        /// locale would otherwise render <c>0,00</c>, and these numbers get copied into notes.
        /// </summary>
        private static string Fixed(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
