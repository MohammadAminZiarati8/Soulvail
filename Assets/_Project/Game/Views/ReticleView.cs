using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Reticle.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The ring under whatever the gun is aimed at. CC §3.5 makes it mandatory: a player who cannot
    /// see what the auto-aim chose concludes it is broken even while it is choosing well, and CC
    /// §3.6's "blocked" state has no other way to say "go around". See GD §16.4 for the three looks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reads one event and one dictionary, and decides nothing.</b> <c>TargetChanged</c> says
    /// which enemy and in what way; <see cref="EnemyViews"/> says where that enemy's body is. There
    /// is no scoring here, no distance test, no notion of what a focus means — all of that happened
    /// in core before the event was published. AR §3: the view renders events.
    /// </para>
    /// <para>
    /// <b>Position every frame, appearance only on change.</b> The target moves continuously and the
    /// ring has to keep up, but the three looks change only when core says so — so the colour, the
    /// width and the two glyphs are written from the event handler and never from
    /// <see cref="LateUpdate"/>. What is left in the frame is a transform write and a sine, which is
    /// what rule 5's "allocates nothing per frame" comes down to.
    /// </para>
    /// <para>
    /// <b>The geometry is built here, not authored.</b> Three line renderers, filled in
    /// <see cref="Awake"/> from the radius below, so the prefab carries no baked circle that would
    /// quietly disagree with the number a designer changed. It costs three small arrays once per
    /// run and puts every shape in one readable method.
    /// </para>
    /// <para>
    /// <see cref="LateUpdate"/> for the reason <see cref="FollowCamera"/> uses it: the bodies move
    /// inside <c>RunTicker.Tick</c>, which is the Update phase, so a ring positioned in
    /// <c>Update</c> would trail its target by a frame — visible as the ring lagging behind a
    /// walking enemy, which is exactly the "the aim is broken" impression it exists to prevent.
    /// </para>
    /// <para>
    /// <b>There are two markers from M2-12a, and three levels of the same cyan.</b> The ordering is
    /// the information: <em>focused-and-firing</em> is bright and pulsing, <em>auto-selected</em> is
    /// subtle and still, and <em>held-but-inactive</em> — the enemy the player tapped that is too
    /// far away for the gun to have taken — is fainter still and parked on that enemy rather than on
    /// the target. A chevron sits over it, because CC §3.5 makes the chevron the "you picked this"
    /// glyph and the player did pick this one; it is the <em>ring</em> that claims "and the gun is
    /// on it", which this marker does not. That is ledger row 12: targeting was never wrong, the
    /// game simply had no way to say what it had already decided.
    /// </para>
    /// <para>
    /// <b>The two markers are independent, and that is why the held one is not a child of
    /// <c>_visual</c>.</b> They stand on two different enemies, so the root follows the current
    /// target and the held marker's world position is written straight onto it; the pulse is a scale
    /// on <c>_visual</c> alone, so nothing the bright ring does reaches the faint one.
    /// </para>
    /// </remarks>
    public sealed class ReticleView : MonoBehaviour
    {
        /// <summary>The id <c>TargetChanged</c> carries when there is nothing to face.</summary>
        private const int NoTarget = -1;

        /// <summary>Pulses a second, for the focused ring (GD §16.4).</summary>
        private const float PulseHz = 2f;

        /// <summary>How far above 1 the focused pulse reaches: 1.0 ↔ 1.15.</summary>
        private const float PulseAmplitude = 0.15f;

        [Tooltip("Everything that is drawn. Toggled off rather than the whole object, so this " +
                 "component keeps its frame and can turn itself back on.")]
        [SerializeField] private GameObject _visual;

        [Tooltip("The ring itself — a closed loop of segments on the ground plane.")]
        [SerializeField] private LineRenderer _ring;

        [Tooltip("The focused-only chevron, floating over the target.")]
        [SerializeField] private LineRenderer _chevron;

        [Tooltip("The two strokes of the blocked '×'.")]
        [SerializeField] private LineRenderer _blockedFirstStroke;

        [SerializeField] private LineRenderer _blockedSecondStroke;

        [Tooltip("The second marker: everything drawn on a focus the player is holding on " +
                 "something the gun has not taken. Toggled like the Visual above, and a sibling " +
                 "of it rather than a child, because the two stand on two different enemies.")]
        [SerializeField] private GameObject _heldVisual;

        [Tooltip("The held marker's ring. Fainter and smaller than the main one — see the type's " +
                 "remarks for why the ordering is the information.")]
        [SerializeField] private LineRenderer _heldRing;

        [Tooltip("The held marker's chevron. CC §3.5's 'you picked this' glyph, on the enemy the " +
                 "player actually picked.")]
        [SerializeField] private LineRenderer _heldChevron;

        [Tooltip("Ring radius in metres. A little wider than an enemy capsule, so the body reads " +
                 "as standing inside the ring rather than wearing it.")]
        [Min(0.1f)]
        [SerializeField] private float _radius = 0.9f;

        [Tooltip("Segments in the ring. 32 is round at arm's length on a phone and cheap.")]
        [Range(8, 128)]
        [SerializeField] private int _segments = 32;

        [Tooltip("Metres above the floor. Enough to clear z-fighting with the arena, little " +
                 "enough that the ring still reads as lying on the ground.")]
        [SerializeField] private float _groundOffset = 0.02f;

        [Tooltip("How high over the target the focused chevron floats, in metres.")]
        [SerializeField] private float _chevronHeight = 2.2f;

        [Tooltip("Ring colour. Cyan for all three states — red is reserved for danger (GD §16.4), " +
                 "so a blocked target must never look like an attack telegraph.")]
        [SerializeField] private Color _colour = new Color(0.3f, 0.9f, 1f, 1f);

        [Tooltip("Alpha of the auto-aimed ring. Subtle: it is on screen constantly.")]
        [Range(0f, 1f)]
        [SerializeField] private float _autoAlpha = 0.6f;

        [Tooltip("Line width of the ordinary ring, in metres.")]
        [SerializeField] private float _width = 0.08f;

        [Tooltip("Line width of the blocked ring — hollow and thin, so 'cannot be hurt' reads at " +
                 "a glance without a second colour.")]
        [SerializeField] private float _blockedWidth = 0.03f;

        [Tooltip("The held marker's radius as a fraction of the ring's. Below 1 so the two never " +
                 "read as one object when the player walks the focus into range and the bright " +
                 "ring takes it over.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _heldScale = 0.8f;

        [Tooltip("Alpha of the held marker. Below the auto ring's, which is below the focused " +
                 "ring's 1.0 — three levels of one colour, brightest for what the gun is on.")]
        [Range(0f, 1f)]
        [SerializeField] private float _heldAlpha = 0.35f;

        /// <summary>
        /// Reused for every colour write. A property block rather than the material, because
        /// assigning to <c>Renderer.material</c> instantiates a copy per renderer — four leaked
        /// materials a run, and the shared asset silently no longer the one being drawn.
        /// </summary>
        private MaterialPropertyBlock _block;

        /// <summary>
        /// URP's colour property, hashed once. An instance field rather than a static, because
        /// nothing in this project holds static state (AR §7) and the four bytes are free.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private EnemyViews _views;
        private IDisposable _subscription;

        private int _targetId = NoTarget;
        private bool _isFocused;
        private bool _isBlocked;

        /// <summary>
        /// The enemy the player tapped that the gun has not taken, or −1. Never equal to
        /// <see cref="_targetId"/> when it is set — core guarantees the two are never both live
        /// (<c>TargetChanged.HeldFocusId</c>).
        /// </summary>
        private int _heldFocusId = NoTarget;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="views">The arena's bodies, for resolving the target id to a position.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribing here rather than in <c>OnEnable</c>, and for the reason <see cref="EnemyViews"/>
        /// gives: <c>RunScope</c> injects during its own <c>Awake</c>, well before the first tick, so
        /// this is listening before core can publish anything. A subscription taken in
        /// <c>OnEnable</c> would be at the mercy of Unity's unordered <c>Awake</c> calls.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub, EnemyViews views)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _views = views ?? throw new ArgumentNullException(nameof(views));

            _subscription = hub.Subscribe<TargetChanged>(OnTargetChanged);
        }

        /// <exception cref="MissingReferenceException">A part of the prefab is unassigned.</exception>
        /// <remarks>
        /// The serialized halves are checked in <c>Awake</c> and the injected half in
        /// <see cref="Start"/>, and the split is about ordering: these are the prefab's own
        /// references and are there or not the moment it exists, while <c>RunScope</c> injects from
        /// its own <c>Awake</c> and Unity gives no order between two of those. <c>Start</c> is the
        /// first moment "nothing injected me" is a conclusion rather than a race (M0-18).
        /// </remarks>
        private void Awake()
        {
            RequireAssigned(_visual, nameof(_visual));
            RequireAssigned(_ring, nameof(_ring));
            RequireAssigned(_chevron, nameof(_chevron));
            RequireAssigned(_blockedFirstStroke, nameof(_blockedFirstStroke));
            RequireAssigned(_blockedSecondStroke, nameof(_blockedSecondStroke));
            RequireAssigned(_heldVisual, nameof(_heldVisual));
            RequireAssigned(_heldRing, nameof(_heldRing));
            RequireAssigned(_heldChevron, nameof(_heldChevron));

            _block = new MaterialPropertyBlock();

            BuildGeometry();

            // Hidden until core says otherwise. A ring sitting at the world origin for the first
            // frame of a run is a bug report waiting to be filed.
            ApplyState();

            // The held marker's look never changes — one colour, one alpha, one width, and no
            // pulse — so it is written once here rather than from the event handler. That is also
            // what keeps it out of ApplyState, which returns early when there is no target and
            // would therefore skip exactly the case a held focus is most likely to be in.
            ApplyHeldLook();

            _heldVisual.SetActive(false);
        }

        /// <exception cref="InvalidOperationException">Nothing injected this reticle.</exception>
        private void Start()
        {
            if (_views is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ReticleView)} was never injected. Drag this object onto RunScope's " +
                    "Reticle field — the reticle resolves core's target id to a body through the " +
                    "run's EnemyViews, and only the run's scope can hand it that.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's own disposal, because a
            // reticle destroyed mid-run — a scene reload, a pooled HUD — would otherwise stay in
            // the subscriber list and be handed events for a component Unity has already killed.
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <remarks>
        /// The lookup can miss, and a miss is not an error: <c>TargetChanged</c> is published from
        /// inside core's tick, and the body it names is destroyed by <c>EnemyViews</c> on a
        /// <c>EnemyDespawned</c> that may arrive in the same frame. Hiding is the honest answer for
        /// the frame in between — better than freezing the ring over a corpse that is no longer
        /// there.
        /// </remarks>
        private void LateUpdate()
        {
            // The two markers are followed independently, and the held one first: a focus held on
            // something out of range is exactly the case where the gun may have no target at all,
            // so a held marker that returned early with the main one would be invisible in the one
            // situation it exists for.
            FollowHeldFocus();

            if (_targetId == NoTarget || _views is null || !_views.TryGet(_targetId, out EnemyView view) || view == null)
            {
                Hide();
                return;
            }

            if (!_visual.activeSelf)
            {
                _visual.SetActive(true);
            }

            Vector3 position = view.Position;
            position.y = _groundOffset;
            transform.position = position;

            // Only the focused ring pulses, so an auto-aimed one sits perfectly still and the
            // difference between "the game picked this" and "you picked this" is visible without
            // reading anything.
            float scale = _isFocused
                ? 1f + (PulseAmplitude * 0.5f * (1f + Mathf.Sin(Time.time * PulseHz * 2f * Mathf.PI)))
                : 1f;

            _visual.transform.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>
        /// Parks the faint marker on the enemy the player tapped, or takes it away.
        /// </summary>
        /// <remarks>
        /// Its world position is written rather than inherited, because the root this component
        /// sits on is following the <em>current</em> target and the held focus is by definition a
        /// different enemy. A lookup that misses hides it, on the same "a miss is not an error"
        /// terms as the ring above: the body may be destroyed in the frame between core publishing
        /// and this running.
        /// </remarks>
        private void FollowHeldFocus()
        {
            if (_heldVisual == null)
            {
                return;
            }

            if (_heldFocusId == NoTarget
                || _views is null
                || !_views.TryGet(_heldFocusId, out EnemyView held)
                || held == null)
            {
                if (_heldVisual.activeSelf)
                {
                    _heldVisual.SetActive(false);
                }

                return;
            }

            if (!_heldVisual.activeSelf)
            {
                _heldVisual.SetActive(true);
            }

            Vector3 position = held.Position;
            position.y = _groundOffset;

            _heldVisual.transform.position = position;
        }

        private void OnTargetChanged(TargetChanged evt)
        {
            _targetId = evt.Id;
            _isFocused = evt.IsFocused;
            _isBlocked = evt.IsBlocked;
            _heldFocusId = evt.HeldFocusId;

            ApplyState();
        }

        /// <summary>The three looks of GD §16.4, written once per change rather than per frame.</summary>
        private void ApplyState()
        {
            if (_targetId == NoTarget)
            {
                Hide();
                return;
            }

            _chevron.gameObject.SetActive(_isFocused);
            _blockedFirstStroke.gameObject.SetActive(_isBlocked);
            _blockedSecondStroke.gameObject.SetActive(_isBlocked);

            // Blocked decides the width, focused decides the brightness, and the two are
            // independent because both can be true at once: a player holding a focus on a Warden
            // mid-shield is exactly the case CC §3.6 describes, and it must read as both.
            float width = _isBlocked ? _blockedWidth : _width;

            _ring.startWidth = width;
            _ring.endWidth = width;

            var colour = new Color(_colour.r, _colour.g, _colour.b, _isFocused ? 1f : _autoAlpha);

            SetColour(_ring, colour);
            SetColour(_chevron, colour);
            SetColour(_blockedFirstStroke, colour);
            SetColour(_blockedSecondStroke, colour);
        }

        /// <summary>
        /// The held marker's constant look: the ring colour at <see cref="_heldAlpha"/>, which sits
        /// below <see cref="_autoAlpha"/>, which sits below the focused ring's 1.0.
        /// </summary>
        private void ApplyHeldLook()
        {
            var colour = new Color(_colour.r, _colour.g, _colour.b, _heldAlpha);

            SetColour(_heldRing, colour);
            SetColour(_heldChevron, colour);
        }

        /// <remarks>
        /// Hides the main marker only. The held one is followed on its own terms in
        /// <see cref="FollowHeldFocus"/>, because "the gun has nothing to shoot" and "the player is
        /// not holding a focus" are different facts and the first does not imply the second.
        /// </remarks>
        private void Hide()
        {
            if (_visual.activeSelf)
            {
                _visual.SetActive(false);
            }
        }

        /// <remarks>
        /// <c>_BaseColor</c> is URP's colour property, and the property block is filled from the
        /// renderer first so that a material with anything else on it keeps it — a block replaces
        /// every property it names and leaves the rest alone only if they were read in.
        /// </remarks>
        private void SetColour(LineRenderer line, Color colour)
        {
            line.GetPropertyBlock(_block);
            _block.SetColor(_baseColorId, colour);
            line.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Fills the three shapes: a closed ring on the ground, a chevron above the target, and the
        /// two strokes of a cross at the ring's centre.
        /// </summary>
        /// <remarks>
        /// Local space throughout (<c>useWorldSpace = false</c>), which is what lets the pulse be a
        /// single scale on the parent instead of a rebuild of every point.
        /// </remarks>
        private void BuildGeometry()
        {
            BuildRing(_ring, _radius, _width);

            BuildChevron(_chevron, scale: 1f, _width);

            // Two separate renderers rather than one four-point line, because a single line would
            // draw the connecting stroke between the two arms and the glyph would be a triangle.
            float arm = _radius * 0.45f;

            BuildStroke(_blockedFirstStroke, new Vector3(-arm, 0f, -arm), new Vector3(arm, 0f, arm));
            BuildStroke(_blockedSecondStroke, new Vector3(-arm, 0f, arm), new Vector3(arm, 0f, -arm));

            // The held marker is the same two shapes at a fraction of the size, built rather than
            // scaled by a transform: a transform scale would be the pulse's channel, and the one
            // thing this marker must never do is pulse.
            BuildRing(_heldRing, _radius * _heldScale, _width * _heldScale);

            BuildChevron(_heldChevron, _heldScale, _width * _heldScale);
        }

        private void BuildRing(LineRenderer line, float radius, float width)
        {
            line.useWorldSpace = false;

            // Looped rather than closed by repeating the first point, so the join is mitred like
            // every other segment instead of showing a seam.
            line.loop = true;
            line.positionCount = _segments;

            float step = 2f * Mathf.PI / _segments;

            for (int i = 0; i < _segments; i++)
            {
                float angle = i * step;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            line.startWidth = width;
            line.endWidth = width;
        }

        /// <summary>
        /// A shallow V lying flat, pointing at the enemy below it.
        /// </summary>
        /// <remarks>
        /// Flat rather than upright because the camera is fixed at 57° (GD §5.1) — nothing in this
        /// game is ever seen from the side, so a billboard would be solving a problem that cannot
        /// occur.
        /// </remarks>
        private void BuildChevron(LineRenderer line, float scale, float width)
        {
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 3;
            line.SetPosition(0, new Vector3(-0.28f * scale, _chevronHeight * scale, 0.22f * scale));
            line.SetPosition(1, new Vector3(0f, _chevronHeight * scale, -0.22f * scale));
            line.SetPosition(2, new Vector3(0.28f * scale, _chevronHeight * scale, 0.22f * scale));
            line.startWidth = width;
            line.endWidth = width;
        }

        private void BuildStroke(LineRenderer line, Vector3 from, Vector3 to)
        {
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = _blockedWidth;
            line.endWidth = _blockedWidth;
        }

        /// <remarks>
        /// Unity's <c>==</c> rather than <c>is null</c>: an unassigned or destroyed serialized
        /// reference is a live object that only compares equal to null through the engine's
        /// operator.
        /// </remarks>
        private void RequireAssigned(UnityEngine.Object reference, string field)
        {
            if (reference != null)
            {
                return;
            }

            throw new MissingReferenceException(
                $"{nameof(ReticleView)} has no {field} assigned. Reticle.prefab is expected to " +
                "carry all eight — without them there is nothing to draw, no way to say which of " +
                "GD §16.4's three states the target is in, and no way to show a focus the gun has " +
                "not taken (CC §3.5).");
        }
    }
}
