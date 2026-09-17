using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Player.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The shell on the player while a granted shield is up: CC §6.4's Bulwark, made visible. It is
    /// the only thing in the world that says the skill did anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reads the total, not the removal.</b> <c>ShieldGranted</c> raises it and
    /// <c>ShieldGrantExpired</c> lowers it <em>only when <c>Total</c> has reached zero</em>. M3-11a
    /// rule 9 put the total on the expiry event for exactly this reader: two overlapping grants end
    /// at two different moments, and a shell that dropped on the first expiry would leave the player
    /// looking unshielded while thirty-five points were still standing between them and the next
    /// bolt. The event says how much shield exists now; this asks nothing else.
    /// </para>
    /// <para>
    /// <b>It fades rather than pops, and 0.15 s is the number.</b> The shell appears in the middle of
    /// a fight, on a frame the player is already reading for something else, and a hard pop at that
    /// moment reads as a rendering glitch rather than as a skill — which is the opposite of what the
    /// only feedback this skill has is for. A serialized field rather than a constant, because it is
    /// a feel number and M8-01's game-feel pass is where feel numbers get retuned.
    /// </para>
    /// <para>
    /// <b>It holds nothing core owns.</b> <c>DomainEventHub</c> and no <c>IRunSession</c>: a grant's
    /// whole life is on the two events, so there is no number to poll and no state here that a run
    /// could be the authority on. <see cref="ZoneViews"/> and this are the two purest
    /// event-rendering objects in the project, and the suite says so with a reflection row.
    /// </para>
    /// <para>
    /// <b>The fade owns an <see cref="Update"/>, and that is not <c>ZoneView</c>'s rule broken.</b> A
    /// decal is stepped by <c>RunTicker</c> on the snapshot's clamped <c>Dt</c> because core timed the
    /// zone's life on that step and the two must not disagree. Nothing in core has an opinion about
    /// this fade: it is 0.15 s of cosmetics either side of a state the events already settled, so the
    /// wall clock is the honest clock for it — and at a <c>timeScale</c> of 0 it freezes with the
    /// world, which is what a picture of a frozen world should do.
    /// </para>
    /// </remarks>
    public sealed class BulwarkView : MonoBehaviour
    {
        [Tooltip("The shell itself, on a child of the body. Shown while a grant is up and hidden " +
                 "when the last one ends — the component is harmless without one, it just has " +
                 "nothing to draw.")]
        [SerializeField] private Renderer _shell;

        [Tooltip("What the shell is drawn in. GD §16.4 reserves #22D3EE for the player and the " +
                 "things that keep them safe, which is exactly what a granted shield is. The " +
                 "initialiser below is deliberately NOT that colour, so a prefab whose field never " +
                 "bound is visible rather than plausible.")]
        [SerializeField] private Color _colour = Color.white;

        [Tooltip("Alpha at a fully faded-in shell. Low enough to read as a shell around the " +
                 "character rather than as a replacement for them.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxAlpha = 0.45f;

        [Tooltip("Seconds the shell takes to fade in, and out again. A pop at this moment reads " +
                 "as a glitch — see the class remarks.")]
        [Min(0f)]
        [SerializeField] private float _fadeSeconds = 0.15f;

        /// <summary>URP's colour property, hashed once.</summary>
        /// <remarks>
        /// An instance field rather than a static, because nothing in this project holds static state
        /// (AR §7) and the four bytes are free. <see cref="FocusGlowView"/>'s shape, beside which this
        /// object sits on the same prefab.
        /// </remarks>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Reused for every colour write. A property block rather than the material, because
        /// assigning to <c>Renderer.material</c> instantiates a copy — and this renderer shares its
        /// material with the glow and the reticle, which would then be drawing a different asset than
        /// they thought.
        /// </summary>
        private MaterialPropertyBlock _properties;

        private IDisposable _grantedSubscription;
        private IDisposable _expiredSubscription;

        private bool _up;
        private float _alpha;
        private bool _injected;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// Subscribing here rather than in <c>OnEnable</c>, and for the reason <see cref="FocusGlowView"/>
        /// gives: <c>RunScope</c> injects during its own <c>Awake</c>, well before the first tick, so
        /// this is listening before core can publish anything.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _grantedSubscription = hub.Subscribe<ShieldGranted>(OnGranted);
            _expiredSubscription = hub.Subscribe<ShieldGrantExpired>(OnExpired);
        }

        /// <summary>Whether a grant is standing, whatever the fade has got to yet.</summary>
        public bool IsUp => _up;

        /// <summary>
        /// How solid the shell is right now, in <c>[0, 1]</c> of <c>_maxAlpha</c>. Zero on the frame a
        /// grant lands, which is rule 6's whole point — see <see cref="Step"/>.
        /// </summary>
        public float Alpha => _alpha;

        /// <summary>
        /// Advances the fade by <paramref name="dt"/> seconds toward whatever the events last said.
        /// </summary>
        /// <param name="dt">
        /// The wall clock's step. Public rather than private so the fade is drivable from a fixture
        /// at a step of its choosing — <c>ZoneView.Step</c>'s shape, and the reason
        /// <c>TelegraphRingView</c> has one too.
        /// </param>
        /// <remarks>
        /// A zero <c>_fadeSeconds</c> snaps rather than dividing by it, which is what an owner who
        /// types 0 into the inspector is asking for.
        /// </remarks>
        public void Step(float dt)
        {
            float target = _up ? 1f : 0f;

            if (_alpha == target)
            {
                return;
            }

            // Negated, so NaN is refused with the rest: a NaN alpha renders as a black shell under
            // URP's blend rather than as an error.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _alpha = _fadeSeconds > 0f
                ? Mathf.MoveTowards(_alpha, target, dt / _fadeSeconds)
                : target;

            Paint();
        }

        /// <exception cref="InvalidOperationException">A shell is dressed but nothing injected it.</exception>
        /// <remarks>
        /// Only an error when there is something to draw, which is what makes this optional in the
        /// way the glow and the reticle are: a test scene may legitimately not dress one. Checked in
        /// <c>Start</c> rather than <c>Awake</c> because <c>RunScope</c> injects from its own
        /// <c>Awake</c> and Unity gives no order between two of those (M0-18).
        /// </remarks>
        private void Start()
        {
            if (_shell != null && !_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(BulwarkView)} has a shell but was never injected, so a granted " +
                    "shield will never be visible. The component is registered by RunScope — drag " +
                    "the Player object in this scene onto its Bulwark field.");
            }
        }

        private void Awake()
        {
            // Hidden until a grant says otherwise. A shell on a character who has cast nothing would
            // be the game claiming a protection nobody has.
            Apply();
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal, for the reason
            // FocusGlowView unsubscribes: a body destroyed mid-run would otherwise stay in the
            // subscriber list and be handed events for a component Unity has already killed.
            _grantedSubscription?.Dispose();
            _expiredSubscription?.Dispose();

            _grantedSubscription = null;
            _expiredSubscription = null;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <remarks>
        /// The amount and the duration are deliberately unread. How much shield there is belongs to
        /// a readout, and M3-13 owns the one GD §16.2 asks for; this is a shell in the world, which
        /// is either there or not.
        /// </remarks>
        private void OnGranted(ShieldGranted evt)
        {
            _up = true;

            Apply();
        }

        /// <summary>Rule 6: the shell comes down on the <em>total</em> reaching zero, never on a removal.</summary>
        private void OnExpired(ShieldGrantExpired evt)
        {
            if (evt.Total > 0f)
            {
                return;
            }

            _up = false;

            Apply();
        }

        /// <summary>
        /// Shows or hides the shell object to match <see cref="_up"/>, and repaints.
        /// </summary>
        /// <remarks>
        /// The object is activated on the way up before a single frame of fade has run, so the first
        /// frame of a grant draws a shell at alpha 0 rather than nothing — and deactivated on the way
        /// down only once the fade has finished, in <see cref="Paint"/>, so the last frames of the
        /// fade out are still drawn.
        /// </remarks>
        private void Apply()
        {
            if (_shell == null)
            {
                return;
            }

            if (_up && !_shell.gameObject.activeSelf)
            {
                _shell.gameObject.SetActive(true);
            }

            Paint();
        }

        /// <summary>Writes the current alpha onto the shell, and puts it away once it is invisible.</summary>
        private void Paint()
        {
            // Unity's ==: a destroyed or unassigned renderer is a live reference that only compares
            // equal to null through the engine's operator.
            if (_shell == null)
            {
                return;
            }

            bool visible = _up || _alpha > 0f;

            if (_shell.gameObject.activeSelf != visible)
            {
                _shell.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            // One allocation per component, on its first paint and never again. A field initialiser
            // throws at import from inside prefab serialisation, and Awake is not early enough for a
            // fixture that never runs one (Traps §5) — ZoneView's field carries the long version.
            _properties ??= new MaterialPropertyBlock();

            // Filled from the renderer first so that a material with anything else on it keeps it — a
            // block replaces every property it names and leaves the rest alone only if they were read
            // in. FocusGlowView's rule, on the same prefab.
            _shell.GetPropertyBlock(_properties);
            _properties.SetColor(
                _baseColorId,
                new Color(_colour.r, _colour.g, _colour.b, _maxAlpha * _alpha));
            _shell.SetPropertyBlock(_properties);
        }
    }
}
