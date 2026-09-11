using System;
using System.Globalization;
using System.Text;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
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
    /// The target line (M1-09) keeps that rule rather than breaking it. It comes from
    /// <c>TargetChanged</c> — an outbound event, the third side of the same boundary — and not from
    /// <c>IRunSession.State</c>. Reaching into core's state would make the overlay agree with core
    /// by construction, so it could never show the boundary disagreeing with itself, which is the
    /// one thing it is for. It also means the line and the reticle are fed by the same event: if
    /// they ever disagree, the fault is in a view rather than in targeting.
    /// </para>
    /// <para>
    /// The charge line (M1-16) is the one number here that comes from core's own state, through
    /// <c>RunState.MovementSkillCooldownFraction</c>, and it is worth saying why it does not break
    /// the rule above. There is no event that could carry it — a fill slides continuously for two
    /// and a half seconds, so publishing it would mean an event a frame — and it is read from
    /// exactly the property <c>SkillButton</c> reads. That makes it the same kind of check as the
    /// target line rather than the opposite one: if the line and the button ever disagree, the
    /// fault is in the button, because the number they are shown is one number.
    /// </para>
    /// <para>
    /// The director's three numbers (M2-05) are the newest, and they are two kinds of thing. The
    /// wave comes from <c>WaveStarted</c>, which is the same bargain the target line makes — an
    /// outbound event, not core's state. <c>enemies n/cap</c> and <c>stale n</c> are boundary reads:
    /// the first is the snapshot's own count against a composition constant, and the second is the
    /// one number in the project that has no other way of being seen at all, since what
    /// <c>NavPathSense</c> cannot keep up with never reaches core as anything but a slightly wrong
    /// direction.
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
        private readonly StringBuilder _line = new StringBuilder(160);

        private WorldSnapshot _snapshot;
        private IntentBuffer _intents;
        private IRunSession _session;

        /// <summary>
        /// The run's path cache, for <c>StalePathCount</c>. An adapter rather than core state, like
        /// the snapshot and the intent buffer either side of it — and the number it reports is one
        /// nothing else in the game can see (M2-05 rule 19).
        /// </summary>
        private NavPathSense _paths;

        /// <summary>
        /// The run's plan, read for one question only: whether this arena has anywhere to spawn.
        /// An arena dressed without spawn points makes the director inert by design (M2-05 rule
        /// 12), and the whole cost of that decision is that it is invisible — so the overlay is
        /// where it stops being.
        /// </summary>
        private SpawnPlan _spawnPlan;

        private IDisposable _targetSubscription;
        private IDisposable _waveSubscription;
        private float _untilRefresh;
        private float _fps;

        /// <summary>The last target core announced, or −1. Written by the event, read by the line.</summary>
        private int _targetId = -1;

        private bool _isFocused;
        private bool _isBlocked;

        /// <summary>The wave the director last announced, and how many the stage holds.</summary>
        private int _wave;
        private int _waveCount;

        /// <param name="snapshot">The run's one snapshot — what core was told this frame.</param>
        /// <param name="intents">The run's intent buffer — what core decided this frame.</param>
        /// <param name="session">The run, for the one number that has no other way out. See the class remarks.</param>
        /// <param name="hub">The run's event hub, for the target and wave lines. Subscribed for this component's life.</param>
        /// <param name="paths">The run's path cache, for the stale count.</param>
        /// <param name="spawnPlan">The run's plan, for whether this arena can spawn at all.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        [Inject]
        public void Construct(
            WorldSnapshot snapshot,
            IntentBuffer intents,
            IRunSession session,
            DomainEventHub hub,
            NavPathSense paths,
            SpawnPlan spawnPlan)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _intents = intents ?? throw new ArgumentNullException(nameof(intents));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _spawnPlan = spawnPlan ?? throw new ArgumentNullException(nameof(spawnPlan));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _targetSubscription = hub.Subscribe<TargetChanged>(OnTargetChanged);

            // The wave line comes from the event rather than from the director, and that is the
            // same rule the target line keeps: reaching into core's state would make the overlay
            // agree with core by construction, so it could never show the boundary disagreeing
            // with itself — which is the one thing it exists for.
            _waveSubscription = hub.Subscribe<WaveStarted>(OnWaveStarted);
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

            if (_snapshot is null || _intents is null || _session is null)
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
        /// Explicit, rather than left to the hub's disposal: an overlay destroyed before its scope
        /// — a scene reload, or the release build's own <c>Awake</c> disabling it — would otherwise
        /// stay in the subscriber list and be handed events for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _targetSubscription?.Dispose();
            _targetSubscription = null;

            _waveSubscription?.Dispose();
            _waveSubscription = null;
        }

        private void OnTargetChanged(TargetChanged evt)
        {
            _targetId = evt.Id;
            _isFocused = evt.IsFocused;
            _isBlocked = evt.IsBlocked;
        }

        private void OnWaveStarted(WaveStarted evt)
        {
            _wave = evt.Wave;
            _waveCount = evt.WaveCount;
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

            // The snapshot's count, not the run's: this line answers "how many enemies did core
            // get told about this frame", which is the number that matters when the arena looks
            // fuller or emptier than it should. A count read from core would agree with core by
            // construction and so could never show the boundary disagreeing with itself.
            //
            // Against the device cap rather than against the stage's composed concurrency (M2-05).
            // The cap is the number M2-04 measured and priced, it is the one the path budget is
            // asked about, and it is a composition constant both sides already agree on — where
            // the stage's own C(n) is core's state and would have to be dragged out through a new
            // read. An early stage whose cap is lower than 28 therefore looks emptier than it is
            // allowed to be, which is the whole of what this choice costs.
            _line.Append("  enemies ").Append(_snapshot.EnemyCount.ToString(CultureInfo.InvariantCulture));
            _line.Append('/').Append(BootInstaller.DeviceEnemyCap.ToString(CultureInfo.InvariantCulture));

            AppendDirector();

            // Zero while the pathfinder is keeping up with the population, and the number ledger
            // row 5 was missing: a refresh budget that cannot reach everybody does not fail, it
            // just lets routes age until enemies walk into pillars. Anything but zero with a full
            // wave up means the enemy cap and the path budget disagree (M2-05 rule 19).
            _line.Append("  stale ").Append(_paths.StalePathCount.ToString(CultureInfo.InvariantCulture));

            // The id and how it was chosen — the two questions a tuning session asks of targeting.
            // "target -1" is a real answer and worth showing: it is the difference between "the aim
            // picked badly" and "the aim found nothing at all".
            _line.Append("  target ").Append(_targetId.ToString(CultureInfo.InvariantCulture));

            if (_targetId >= 0 && _isFocused)
            {
                _line.Append(" focus");
            }

            if (_targetId >= 0 && _isBlocked)
            {
                _line.Append(" blocked");
            }

            // The cooldown, not the readiness: "charge 0.00" is the button being live, and the
            // number climbing back down from 1 is the only way to see on a phone whether a dash
            // that felt like it should have fired was actually inside CC §5's input buffer.
            RunState state = _session.State;

            _line.Append("  charge ").Append(Fixed(state is null ? 0f : state.MovementSkillCooldownFraction));

            _line.Append("  fps ").Append(Mathf.RoundToInt(_fps).ToString(CultureInfo.InvariantCulture));

            _text.SetText(_line);
        }

        /// <summary>
        /// Which wave of the stage is running — or that this arena cannot spawn at all.
        /// </summary>
        /// <remarks>
        /// <c>director: —</c> is rule 12 being visible rather than mysterious. An arena with no
        /// spawn points is a legal arena, and every core fixture that starts a run without caring
        /// about spawning is one; the failure mode it buys is a dressed scene that stays silent for
        /// no stated reason, and one dash on screen is the price of never debugging that.
        /// </remarks>
        private void AppendDirector()
        {
            if (_spawnPlan.SpawnPoints.Count == 0)
            {
                _line.Append("  director: —");

                return;
            }

            // Zero until the first WaveStarted arrives, which is a real state and worth showing:
            // it is the difference between "the director has not begun" and "wave 1 is taking its
            // time", and those have different causes.
            _line.Append("  wave ").Append(_wave.ToString(CultureInfo.InvariantCulture));
            _line.Append('/').Append(_waveCount.ToString(CultureInfo.InvariantCulture));
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
