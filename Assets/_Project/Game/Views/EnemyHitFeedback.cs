using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
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
    /// <para>
    /// <b>It owns the archetype's look as of M2-06, because it already owned the reset.</b> Every
    /// effect here is written relative to two fields — <c>_liveColour</c> and <c>_liveScale</c> —
    /// and <see cref="SetArchetypeLook"/> re-bases them on the rental so a Bloater flashes, swells
    /// and dissolves in rust rather than reverting to bone grey mid-effect.
    /// </para>
    /// <para>
    /// <b>Emission carries the flash and the wind-up as well, as of M7-05h</b>, and only a material that
    /// emits shows it. A textured body's colour is its texture times <c>_BaseColor</c>, so the white
    /// base-colour flash leaves the texture as it was; <c>_EmissionColor</c> is added on top, so it
    /// flashes any body white. A wind-up glows from black to danger red-orange (GD §16.4 gives
    /// telegraphs that colour) and drops to black at the strike. <c>M_BoneGrey</c> has emission off,
    /// so the capsules render exactly as they did; <c>M_Enemy</c> has it on (M7-05f). The emission is
    /// one more thing a rental must forget, and every reset below writes it black.
    /// </para>
    /// <para>
    /// <b>Two switches for the Puffball, as of JG-03b.</b> <c>_sporeTelegraph</c> glows the wind-up in
    /// <see cref="Palette.SporeGlow"/> rather than danger, the owner's one exception; <c>_deathBurst</c>
    /// is a particle system the death plays and the reset clears. Both are off on every other body.
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

        [Tooltip("How far the body swells over a wind-up: 1.15 is 15 % bigger by the damage frame. " +
                 "PLACEHOLDER for GD §9.1's real telegraph — deliberately a shape change and not a " +
                 "colour one, because saturated red-orange is reserved for danger (GD §16.4) and " +
                 "that palette belongs to M7's VFX.")]
        [Min(1f)]
        [SerializeField] private float _telegraphSwell = 1.15f;

        [Tooltip("How bright the wind-up glows by the strike, as a multiple of danger red-orange " +
                 "(GD §16.4). Only a material with emission on shows it — M_Enemy does, M_BoneGrey " +
                 "does not, so the capsules keep the swell alone.")]
        [Min(0f)]
        [SerializeField] private float _telegraphGlow = 0.9f;

        [Tooltip("The wind-up glows Palette.SporeGlow, cyan-white, instead of danger red-orange: the " +
                 "Puffball's swelling cap, by the owner's ruling (JG-03b). Off on every other body.")]
        [SerializeField] private bool _sporeTelegraph;

        [Tooltip("A particle system on this body played when it dies, and stopped and cleared when it " +
                 "goes back to the pool: the Puffball's spore burst. Simulated in world space and done " +
                 "inside EnemySystem.CorpseTime, or the despawn cuts it off. Empty on most bodies.")]
        [SerializeField] private ParticleSystem _deathBurst;

        [Tooltip("Shield-face sparks for a blocked hit. Empty on bodies without a shield.")]
        [SerializeField] private ParticleSystem _glance;

        /// <summary>
        /// URP's colour property, hashed once. An instance field rather than a static, because
        /// nothing in this project holds static state (AR §7) and the four bytes are free.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>URP's emission colour, hashed once, for <see cref="_baseColorId"/>'s reason.</summary>
        private readonly int _emissionColorId = Shader.PropertyToID("_EmissionColor");

        private EnemyView _view;
        private MaterialPropertyBlock _block;

        /// <summary>
        /// The material this body draws with while it is alive, remembered so a pooled body can be
        /// swapped back off the transparent stand-in a death put it on (M1-19).
        /// </summary>
        private Material _liveMaterial;

        private IDisposable _damagedSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _telegraphSubscription;

        /// <summary>
        /// The archetype's colour, and nothing else ever. What <see cref="SetArchetypeLook"/> was
        /// last told this body is standing in for.
        /// </summary>
        private Color _liveColour;

        private Vector3 _liveScale;

        /// <summary>
        /// The scale the prefab itself was authored at, which <see cref="SetArchetypeLook"/>
        /// multiplies. Kept apart from <see cref="_liveScale"/> so the multiply is idempotent: a
        /// body rented three times as a Bloater would otherwise end up at 1.35³.
        /// </summary>
        /// <remarks>
        /// The initialiser is what makes this readable in EditMode, where <c>Awake</c> never runs
        /// (Traps §5) and a zero here would shrink every rented body to nothing. Field initialisers
        /// run at construction, so play mode still gets the prefab's real scale a moment later.
        /// </remarks>
        private Vector3 _prefabScale = Vector3.one;

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
        /// taken there would be taken against a null hub on every enemy in the game.
        /// <para>
        /// M1-19's pooling left this exactly as it was, which was the question the placement was
        /// waiting on: a pooled body is injected once, when the pool creates it, and keeps these
        /// three subscriptions for the life of the pool rather than of any one enemy. That is safe
        /// because every handler here filters on <c>_view.Id</c> first, and a body sitting in the
        /// pool is unbound — so it matches no event, having been handed the id of nobody.
        /// </para>
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
            _liveMaterial = _renderer.sharedMaterial;
            _liveColour = _liveMaterial.GetColor(_baseColorId);
            _prefabScale = transform.localScale;
            _liveScale = _prefabScale;
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

        /// <summary>
        /// Re-bases the colour and the scale this body returns to, so the flash, the telegraph
        /// swell and the dissolve all play over the archetype's look rather than over the prefab's.
        /// Called by <c>EnemyViews</c> on the rental, before the body is put into service.
        /// </summary>
        /// <param name="tint">The archetype's colour — <c>EnemyLook.Tint</c>.</param>
        /// <param name="bodyScale">
        /// A multiple of the prefab's own scale — <c>EnemyLook.BodyScale</c>. Applied against
        /// <see cref="_prefabScale"/> rather than against the current one, so rentals do not
        /// compound.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>It sets the two fields every effect in this class is written relative to, which is
        /// why the look belongs here and not on a component of its own.</b> The hit flash returns
        /// to <see cref="_liveColour"/>, the telegraph swells from and back to
        /// <see cref="_liveScale"/>, and the dissolve stretches and fades from both. A second
        /// component writing the tint straight onto the renderer would be overwritten by the first
        /// flash that ended, and the enemy would turn grey the first time it was hit.
        /// </para>
        /// <para>
        /// Absolute rather than accumulated on purpose: every rental passes a look — an unauthored
        /// archetype gets <c>EnemyLook.Default</c> rather than nothing — so the body is told what it
        /// is each time rather than asked to forget what it was. That is what makes the pooled-reset
        /// invariant (AR §18.4) hold for these two things without a third field to clear.
        /// </para>
        /// <para>
        /// Safe before <c>Awake</c>, which outside play mode never runs (Traps §5). The scale is
        /// written directly; the colour needs a renderer and a property block, and this is the one
        /// place that will build the block itself rather than assume <c>Awake</c> did. That is what
        /// makes the set-and-restore half of the pooled-look rule provable in EditMode — the other
        /// half, a real rent → kill → re-rent, is M2-09's and needs PlayMode by construction.
        /// </para>
        /// </remarks>
        public void SetArchetypeLook(Color tint, float bodyScale)
        {
            _liveColour = tint;
            _liveScale = _prefabScale * bodyScale;

            transform.localScale = _liveScale;

            // Unity's == rather than `is null`: an unassigned serialized reference is a live object
            // that only compares equal to null through the engine's operator.
            if (_renderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();

            SetColour(BodyColour());
            SetEmission(Color.black);
        }

        /// <summary>
        /// Undoes everything a life did to how this body looks: the dissolve's stretch and fade,
        /// the transparent material it swapped to, a flash or a wind-up caught mid-play.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called by <c>EnemyView.OnDespawn</c> as the body goes back to the pool, which is the
        /// only moment it is correct to run: doing it on a rental instead would leave a corpse
        /// sitting in the pool fully faded, and one frame of the next Husk drawn transparent is
        /// exactly the artefact this exists to prevent.
        /// </para>
        /// <para>
        /// Safe before <c>Awake</c>, which outside play mode never runs: with no property block
        /// there is no colour to write. That is what lets an EditMode fixture rent and return a
        /// body without building a whole prefab.
        /// </para>
        /// <para>
        /// <b>It restores the <em>archetype's</em> look, not the prefab's</b>, because
        /// <see cref="SetArchetypeLook"/> is what last said what those are. That is the correct
        /// half of the pooled-reset rule: the tint a Bloater left behind is undone by the next
        /// rental being told it is a Husk, not by this method guessing.
        /// </para>
        /// </remarks>
        public void ResetVisuals()
        {
            _dissolving = false;
            _dissolveElapsed = 0f;
            _flashRemaining = 0f;
            _telegraphRemaining = 0f;

            // Ahead of the property-block guard below, unlike every other line here, because as of
            // M2-06 a body can have been rescaled before Awake ever ran — SetArchetypeLook writes
            // the scale whether or not there is a block to write a colour into. The zero test is
            // what stops an EditMode fixture that has neither woken up nor been given a look from
            // being shrunk to nothing: _liveScale is zero on a body in exactly that state and on no
            // other.
            if (_liveScale != Vector3.zero)
            {
                transform.localScale = _liveScale;
            }

            // A burst cut off by the despawn must not resume on the next rental: one more thing a
            // pooled body forgets (AR §18.4), ahead of the guard for the scale's reason.
            if (_deathBurst != null)
            {
                _deathBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_glance != null)
            {
                _glance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_block is null)
            {
                return;
            }

            // Back onto the opaque asset before the colour is written, so the alpha the dissolve
            // left in the property block is applied to a material that ignores it rather than to
            // the transparent one that would honour it for a frame.
            if (_liveMaterial != null)
            {
                _renderer.sharedMaterial = _liveMaterial;
            }

            SetColour(BodyColour());
            SetEmission(Color.black);
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
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the flash, the wind-up swell and the dissolve by <paramref name="dt"/> seconds.
        /// </summary>
        /// <remarks>
        /// Split out of <c>Update</c> on <c>FirstActiveHint</c>'s precedent, and M3-13b is what made
        /// it necessary: <c>Time.deltaTime</c> is not something an EditMode fixture can advance
        /// (Traps §5), so every claim about what the tint looks like <em>after</em> a flash or a
        /// telegraph would have had to be a PlayMode row. The scaled delta stays in <c>Update</c>,
        /// which is where the decision to freeze under a pause lives.
        /// </remarks>
        private void Tick(float dt)
        {
            if (_dissolving)
            {
                TickDissolve(dt);
                return;
            }

            TickTelegraph(dt);

            if (_flashRemaining <= 0f)
            {
                return;
            }

            _flashRemaining -= dt;

            if (_flashRemaining <= 0f)
            {
                _flashRemaining = 0f;
                SetColour(BodyColour());
                SetEmission(TelegraphGlow());
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

            if (evt.Blocked)
            {
                if (_glance != null)
                {
                    _glance.Play(true);
                }

                return;
            }

            _flashRemaining = _flashSeconds;

            // GD §16.3's white, and Palette's as of M3-13a — the ninth reader, and the one no
            // ledger row had ever counted. The archetype tint it returns to is content and stays
            // authored; this is the one colour here that is the same for every body in the game.
            SetColour(Palette.HitFlash);

            // The same white through emission, which is the only way a textured body flashes at all
            // (M7-05h rule 6). A material with emission off ignores it.
            SetEmission(Palette.HitFlash);
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

            // A corpse does not glow: a death mid-wind-up cancels the strike, and a flash is not
            // drawn over a fade (the killing blow's own rule, above).
            SetEmission(Color.black);

            // The Puffball's spores (JG-03b). On the death rather than on EnemyExploded, which follows
            // every death of an archetype that explodes, so the two are the same moment; this is the
            // event the body already hears.
            if (_deathBurst != null)
            {
                _deathBurst.Play(true);
            }

            TickDissolve(0f);
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
        private void TickTelegraph(float dt)
        {
            if (_telegraphRemaining <= 0f)
            {
                return;
            }

            _telegraphRemaining -= dt;

            if (_telegraphRemaining <= 0f)
            {
                _telegraphRemaining = 0f;
                transform.localScale = _liveScale;

                // The glow drops with the swell, in the same frame: the release is the strike.
                if (_flashRemaining <= 0f)
                {
                    SetEmission(Color.black);
                }

                return;
            }

            float t = 1f - (_telegraphRemaining / _telegraphDuration);

            transform.localScale = _liveScale * Mathf.Lerp(1f, _telegraphSwell, t);

            // A flash in flight keeps the emission until it ends; then Tick hands it back to this.
            if (_flashRemaining <= 0f)
            {
                SetEmission(TelegraphGlow());
            }
        }

        /// <summary>
        /// The emission a wind-up wants right now: danger red-orange rising with the wind-up to
        /// <see cref="_telegraphGlow"/>, or black when there is none (M7-05h rule 6). A spore body
        /// glows <see cref="Palette.SporeGlow"/> instead (JG-03b).
        /// </summary>
        private Color TelegraphGlow()
        {
            if (_telegraphRemaining <= 0f || !(_telegraphDuration > 0f))
            {
                return Color.black;
            }

            float t = 1f - (_telegraphRemaining / _telegraphDuration);
            Color glow = (_sporeTelegraph ? Palette.SporeGlow : Palette.Danger) * (_telegraphGlow * t);
            glow.a = 1f;

            return glow;
        }

        private void TickDissolve(float dt)
        {
            _dissolveElapsed += dt;

            float t = Mathf.Clamp01(_dissolveElapsed / _dissolveSeconds);

            Vector3 scale = _liveScale;
            scale.y = _liveScale.y * Mathf.Lerp(1f, _dissolveStretch, t);
            transform.localScale = scale;

            // Fade from the archetype colour, whatever HP the body had on the killing blow.
            Color colour = BodyColour();
            colour.a *= 1f - t;
            SetColour(colour);
        }

        private Color BodyColour() => _liveColour;

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

        /// <summary>
        /// Writes <paramref name="colour"/> as the body's emission, <see cref="SetColour"/>'s way. A
        /// material with emission off ignores it, which is what keeps the capsules as they were.
        /// </summary>
        /// <remarks>
        /// Guarded, unlike <see cref="SetColour"/>, because it is called from the paths that run before
        /// <c>Awake</c> — a look and a reset — where there may be no renderer or no block yet.
        /// </remarks>
        private void SetEmission(Color colour)
        {
            if (_renderer == null || _block is null)
            {
                return;
            }

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(_emissionColorId, colour);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
