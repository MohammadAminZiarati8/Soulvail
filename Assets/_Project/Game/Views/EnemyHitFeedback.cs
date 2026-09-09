using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Enemy.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// What a hit, a wind-up and a death look like on one enemy: a white flash on
    /// <see cref="EnemyDamaged"/>, a swell on <see cref="EnemyTelegraph"/>, a stretch-and-fade on
    /// <see cref="EnemyDied"/>. Placeholder feel for M1 — GD §16.3's real treatment is M8-01's and
    /// the art is M7's — but without it a swing is arithmetic nobody can see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders events and decides nothing.</b> Whether damage landed, how much, and whether it
    /// killed are all settled inside core before either event is published; this reads the two of
    /// them, filters by its own id, and animates. There is no health here, no timer that any rule
    /// depends on, and nothing it does is visible to core.
    /// </para>
    /// <para>
    /// <b>Why the death is a separate event rather than a flag on the damage.</b> It is not:
    /// <see cref="EnemyDamaged.Killed"/> exists precisely so this component can tell a flash from a
    /// dissolve in one event instead of correlating two by id — so a killing blow does not flash at
    /// all, it goes straight into the fade. <see cref="EnemyDied"/> is what starts that, and the
    /// half-second it lasts fits inside <c>EnemySystem.CorpseTime</c>'s 0.6 s, which is the whole
    /// reason a death and a despawn are different things.
    /// </para>
    /// <para>
    /// <b>A property block, never a material.</b> Assigning to <c>Renderer.material</c> instantiates
    /// a copy per enemy — one leaked material per corpse, and the shared asset quietly no longer the
    /// one being drawn. The dissolve swaps <c>sharedMaterial</c> instead, because the live material
    /// is opaque (<c>M_BoneGrey</c>, URP/Lit, <c>_Surface: 0</c>) and an alpha written into a
    /// property block on it would be a fade that never happens and never says so.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EnemyView))]
    public sealed class EnemyHitFeedback : MonoBehaviour
    {
        [Tooltip("The body's renderer — the Mesh child. Assigned rather than searched for: a " +
                 "GetComponentInChildren per spawn is a per-wave cost for a reference the prefab " +
                 "already knows.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("The transparent stand-in the corpse is drawn with while it fades. The live " +
                 "material is opaque, so this is the only way an alpha means anything.")]
        [SerializeField] private Material _dissolveMaterial;

        [Tooltip("How long the white hit flash lasts, in seconds. Long enough to read at 60 fps, " +
                 "short enough that three swings a second do not merge into a glow.")]
        [Min(0f)]
        [SerializeField] private float _flashSeconds = 0.08f;

        [Tooltip("How long the corpse takes to fade, in seconds. Must stay under " +
                 "EnemySystem.CorpseTime (0.6 s) or the body is destroyed mid-dissolve.")]
        [Min(0.01f)]
        [SerializeField] private float _dissolveSeconds = 0.5f;

        [Tooltip("How far the corpse stretches upward over the dissolve: 1.4 is 40 % taller.")]
        [Min(1f)]
        [SerializeField] private float _dissolveStretch = 1.4f;

        [Tooltip("The flash colour. White reads as 'hit' on every body colour the game will have.")]
        [SerializeField] private Color _flashColour = Color.white;

        [Tooltip("How far the body swells over a wind-up: 1.15 is 15 % bigger by the damage frame. " +
                 "PLACEHOLDER for GD §9.1's real telegraph — deliberately a shape change and not a " +
                 "colour one, because saturated red-orange is reserved for danger (GD §16.4) and " +
                 "that palette belongs to M7's VFX.")]
        [Min(1f)]
        [SerializeField] private float _telegraphSwell = 1.15f;

        /// <summary>
        /// URP's colour property, hashed once. An instance field rather than a static, because
        /// nothing in this project holds static state (AR §7) and the four bytes are free.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private EnemyView _view;
        private MaterialPropertyBlock _block;
        private IDisposable _damagedSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _telegraphSubscription;

        private Color _liveColour;
        private Vector3 _liveScale;

        private float _flashRemaining;
        private float _dissolveElapsed;
        private float _telegraphRemaining;
        private float _telegraphDuration;
        private bool _dissolving;
        private bool _injected;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c>, which is what the M1-12 spec sketched and
        /// what the ordering rules out: <c>EnemyViews</c> creates this body with
        /// <c>IObjectResolver.Instantiate</c>, and Unity runs <c>Awake</c> and <c>OnEnable</c>
        /// during the instantiate itself — before VContainer has injected anything. A subscription
        /// taken there would be taken against a null hub on every enemy in the game. The pooling of
        /// M1-19 is where an enable-time hook starts to matter, and it will have
        /// <see cref="EnemyView.Bind"/> to hang off.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _damagedSubscription = hub.Subscribe<EnemyDamaged>(OnDamaged);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
            _telegraphSubscription = hub.Subscribe<EnemyTelegraph>(OnTelegraph);
        }

        /// <exception cref="MissingReferenceException">A part of the prefab is unassigned.</exception>
        private void Awake()
        {
            _view = GetComponent<EnemyView>();

            // Unity's == rather than `is null`: an unassigned serialized reference is a live object
            // that only compares equal to null through the engine's operator.
            if (_renderer == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(EnemyHitFeedback)} on '{name}' has no Renderer assigned. Drag the " +
                    "Mesh child onto its Renderer field — without it a hit has nothing to flash.");
            }

            if (_dissolveMaterial == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(EnemyHitFeedback)} on '{name}' has no dissolve material assigned. " +
                    "Drag M_BoneGrey_Dissolve onto its Dissolve Material field — the live material " +
                    "is opaque, so without it the corpse would stretch and never fade.");
            }

            _block = new MaterialPropertyBlock();

            // sharedMaterial, not material: reading the live asset's colour costs nothing, while
            // the instancing property would clone it on every enemy that ever takes a hit.
            _liveColour = _renderer.sharedMaterial.GetColor(_baseColorId);
            _liveScale = transform.localScale;
        }

        /// <exception cref="InvalidOperationException">Nothing injected this body.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for the reason <c>ReticleView</c> gives:
        /// injection happens after the instantiate that ran <c>Awake</c>, so <c>Start</c> is the
        /// first moment "nothing injected me" is a conclusion rather than a race. Loud rather than
        /// silent because the failure mode is a whole run of enemies that never react to being hit,
        /// which reads as broken combat rather than as broken wiring.
        /// </remarks>
        private void Start()
        {
            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyHitFeedback)} on '{name}' was never injected, so it is deaf to " +
                    "every hit and every death. Enemy bodies must be created through " +
                    "EnemyViews — that is what routes VContainer's injection into the prefab.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal: a body destroyed
            // mid-run — which is what every despawn is — would otherwise stay in the subscriber
            // list and be handed events for a component Unity has already killed.
            _damagedSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _telegraphSubscription?.Dispose();
            _damagedSubscription = null;
            _diedSubscription = null;
            _telegraphSubscription = null;
        }

        private void Update()
        {
            if (_dissolving)
            {
                TickDissolve();
                return;
            }

            TickTelegraph();

            if (_flashRemaining <= 0f)
            {
                return;
            }

            _flashRemaining -= Time.deltaTime;

            if (_flashRemaining <= 0f)
            {
                _flashRemaining = 0f;
                SetColour(_liveColour);
            }
        }

        private void OnDamaged(EnemyDamaged evt)
        {
            if (evt.Id != _view.Id || _dissolving)
            {
                return;
            }

            // A killing blow does not flash. EnemyDied arrives in the same call and the dissolve is
            // the answer to it; flashing first would be one frame of white under a fade that is
            // already starting, which reads as a glitch rather than as a hit.
            if (evt.Killed)
            {
                return;
            }

            _flashRemaining = _flashSeconds;

            SetColour(_flashColour);
        }

        private void OnDied(EnemyDied evt)
        {
            if (evt.Id != _view.Id || _dissolving)
            {
                return;
            }

            _dissolving = true;
            _dissolveElapsed = 0f;
            _flashRemaining = 0f;

            // A Husk killed mid-windup has had its strike cancelled by core (EnemySystem does not
            // tick a corpse), so the swell it was in the middle of has to stop too. The dissolve
            // rewrites the scale from _liveScale every frame anyway; this is what stops the two
            // fighting over it for the half-second they would otherwise overlap.
            _telegraphRemaining = 0f;

            // Out of the physics query immediately, so the swing that lands in the same frame as the
            // death cannot spend a hit on a corpse. Core would refuse the damage anyway — an id
            // that is already dead is a no-op in ApplyDamage — but a corpse still occupying the
            // cone is a hit slot wasted for half a second.
            if (_view.Body != null)
            {
                _view.Body.enabled = false;
            }

            // sharedMaterial, so the swap costs nothing and leaks nothing. Every corpse in the run
            // draws with the same transparent asset; what differs between them is the alpha, and
            // that is what the property block is for.
            _renderer.sharedMaterial = _dissolveMaterial;

            TickDissolve();
        }

        /// <remarks>
        /// GD §9.1 rule 1 — an attack the player cannot see coming is not a difficulty, it is a
        /// bug. The duration comes off the event rather than out of a spec lookup, so what is drawn
        /// and what core is counting are the same 0.4 s by construction.
        /// </remarks>
        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (evt.Id != _view.Id || _dissolving)
            {
                return;
            }

            // A non-positive windup is a legal archetype — EnemySpec allows zero, meaning an
            // untelegraphed hit — and there is nothing to draw for it. Guarded rather than divided
            // by, because the alternative is an infinity in the scale on the frame it arrives.
            if (!(evt.Duration > 0f))
            {
                return;
            }

            _telegraphDuration = evt.Duration;
            _telegraphRemaining = evt.Duration;
        }

        /// <summary>
        /// Swells the body over the wind-up and drops it back at the damage frame.
        /// </summary>
        /// <remarks>
        /// The snap back is the point: it grows steadily for the whole telegraph and returns to
        /// normal in one frame, so the eye reads the release as the moment of the strike. Easing it
        /// out would blur the one instant the player is timing a dodge against.
        /// </remarks>
        private void TickTelegraph()
        {
            if (_telegraphRemaining <= 0f)
            {
                return;
            }

            _telegraphRemaining -= Time.deltaTime;

            if (_telegraphRemaining <= 0f)
            {
                _telegraphRemaining = 0f;
                transform.localScale = _liveScale;

                return;
            }

            float t = 1f - (_telegraphRemaining / _telegraphDuration);

            transform.localScale = _liveScale * Mathf.Lerp(1f, _telegraphSwell, t);
        }

        private void TickDissolve()
        {
            _dissolveElapsed += Time.deltaTime;

            float t = Mathf.Clamp01(_dissolveElapsed / _dissolveSeconds);

            Vector3 scale = _liveScale;
            scale.y = _liveScale.y * Mathf.Lerp(1f, _dissolveStretch, t);
            transform.localScale = scale;

            Color colour = _liveColour;
            colour.a = _liveColour.a * (1f - t);
            SetColour(colour);
        }

        /// <remarks>
        /// The block is filled from the renderer first, so any property the material carries that
        /// this does not name survives: a property block replaces every property in it and leaves
        /// the rest alone only if they were read in.
        /// </remarks>
        private void SetColour(Color colour)
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(_baseColorId, colour);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
