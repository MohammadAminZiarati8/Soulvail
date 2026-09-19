using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_Fissure.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One crack in the floor, in the two states it has: <em>arming</em>, which is the telegraph, and
    /// <em>fired</em>, which is the bite that has already happened. It decides nothing and knows no
    /// events — <c>FissureArmed</c> has said where, how wide and how long, and this draws that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two states differ by shape, and that is rule 3 rather than a style choice.</b> A
    /// fissure that merely got brighter is the failure mode M2-12b's rings were shaped to avoid and
    /// the one <c>EnemyHitFeedback</c> already answered with a swell rather than a flash. So the arm
    /// <em>grows</em> — a disc opening from nothing to the circle core will test, which is
    /// <see cref="TelegraphRingView"/>'s <em>"filling means coming"</em> language, the only telegraph
    /// vocabulary this game has taught — and the fire <em>overshoots and collapses</em>, snapping
    /// past the radius on the frame it bites and shutting to nothing. At every moment of a fissure's
    /// life the two states are a different size moving in a different direction, before a single
    /// alpha is read.
    /// </para>
    /// <para>
    /// <b>The arm is <see cref="Palette.Danger"/>, deliberately</b> (rule 2). GD §16.4 reserves
    /// saturated red-orange for danger <em>"and nothing else, ever"</em> — and a circle that is about
    /// to take 22 hit points off the player is the thing the reservation is for. This is the first
    /// task since M3-13b to reach for that member on purpose rather than to argue its way around it.
    /// The fired state keeps it for the length of its collapse, which is the blast ring's bargain:
    /// an after-image of danger is still an image of danger, and it is gone in a third of a second.
    /// </para>
    /// <para>
    /// <b>It fires itself if nothing tells it to, and that is the net rather than the rule.</b>
    /// <c>FissureFired</c> arrives from the tick the arm ran out in core, and <c>FissureClosed</c>
    /// from the tick the window shut; this body's own clock only ever matters for a crack whose
    /// events never reached the census. Running out is the one thing such a crack can safely be
    /// assumed to have done — <see cref="ZoneView"/>'s net, for its reason, and the reason
    /// <see cref="ArmSeconds"/> rides on the event at all.
    /// </para>
    /// <para>
    /// <b>It lies flat, does not billboard, and owns no frame loop.</b> All three are
    /// <see cref="TelegraphRingView"/>'s rules for its reasons: the camera is fixed at 57° (GD §5.1),
    /// and <c>RunTicker</c> steps this with the snapshot's clamped <c>Dt</c> — the step core ran the
    /// arm's own countdown on, so a telegraph filled on <c>Time.deltaTime</c> would finish before or
    /// after the bite it is promising (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has a job.</b> The same body is a 2.5 m crack,
    /// then a fired one, then a 2.5 m crack again; the size, the place, the state and the clock are
    /// all inherited by the next life unless they are undone, and the failure mode is silence
    /// (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class FissureView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The pose every crack is drawn at: the quad's own normal turned to face straight up, so its
        /// plane is the arena floor.
        /// </summary>
        /// <remarks>
        /// A quad's face points along its local −Z, and a +90° turn about X sends that to world +Y.
        /// <see cref="TelegraphRingView"/>'s constant, for its reason — <c>static readonly</c> rather
        /// than a field per instance, because the ban on statics is about mutable state that survives
        /// a run (AR §18.4).
        /// </remarks>
        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Tooltip("The one quad this crack is drawn with, on this object. Everything visible about a " +
                 "fissure is its scale and its colour, both written here every frame it is live.")]
        [SerializeField] private MeshRenderer _quad;

        [Tooltip("How bright the crack sits at while it is arming. A telegraph the player has to " +
                 "read under pressure, so brighter than a Consecrate's resting ground and dimmer " +
                 "than the flash that follows it.")]
        [Range(0f, 1f)]
        [SerializeField] private float _armAlpha = 0.6f;

        [Tooltip("How bright it goes on the frame it bites, before the collapse takes it away.")]
        [Range(0f, 1f)]
        [SerializeField] private float _fireAlpha = 0.95f;

        [Tooltip("How far past its own radius the crack snaps open on the frame it fires, as a " +
                 "multiple. This is rule 3's shape change: the arm is still growing towards 1 when " +
                 "the fire is already past it, so the two states can never be the same picture.")]
        [Min(1f)]
        [SerializeField] private float _fireFlare = 1.35f;

        [Tooltip("How long the bite's flare takes to shut to nothing, in seconds. Cosmetic, and the " +
                 "only number here core does not supply — the blast ring's linger, for its reason.")]
        [Min(0f)]
        [SerializeField] private float _fireSeconds = 0.35f;

        [Tooltip("Metres the decal floats above the floor, to keep it out of a z-fight with the " +
                 "arena it is drawn on. Cosmetic in the strict sense: no value here can change " +
                 "which circle core tested, because every distance in the game is measured on XZ.")]
        [Min(0f)]
        [SerializeField] private float _groundOffset = 0.02f;

        /// <summary>
        /// Where the colour and the alpha are written, so nothing here ever touches a shared
        /// material.
        /// </summary>
        /// <remarks>
        /// Built on first use, for <see cref="TelegraphRingView"/>'s reason and not for tidiness: a
        /// field initialiser throws at <em>import</em> from inside prefab serialisation, and
        /// <c>Awake</c> never runs in EditMode (Traps §5), so a pooled body armed from a fixture
        /// would paint through a null.
        /// </remarks>
        private MaterialPropertyBlock _properties;

        /// <summary>URP's colour property, resolved once rather than per write.</summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private float _radius;
        private float _armSeconds;
        private float _elapsed;
        private bool _fired;
        private bool _live;

        /// <summary>
        /// The pose and size this body was created at, restored on the way back to the pool so a
        /// returned body is indistinguishable from one the pool has never handed out.
        /// </summary>
        /// <remarks>
        /// Captured in <see cref="Awake"/>, which outside play mode never runs (Traps §5) — the
        /// initialisers are what an EditMode fixture gets, and they describe a prefab authored at the
        /// origin at unit scale, which <c>VFX_Fissure.prefab</c> is.
        /// </remarks>
        private Vector3 _restPosition = Vector3.zero;

        /// <inheritdoc cref="_restPosition" />
        private Quaternion _restRotation = Quaternion.identity;

        /// <inheritdoc cref="_restPosition" />
        private Vector3 _restScale = Vector3.one;

        /// <summary>True while this crack is still on the floor, arming or fired.</summary>
        public bool IsLive => _live;

        /// <summary>True once it has bitten. The half of the state a shape is drawn from.</summary>
        public bool HasFired => _live && _fired;

        /// <summary>True while it is still counting down to the bite.</summary>
        public bool IsArming => _live && !_fired;

        /// <summary>
        /// The circle this crack stands for, in metres — exactly the one <c>FissureSystem</c> tests
        /// the player against. Zero for a body in the pool.
        /// </summary>
        public float Radius => _live ? _radius : 0f;

        /// <summary>How long the arm lasts, in seconds — the event's number. Zero in the pool.</summary>
        public float ArmSeconds => _live ? _armSeconds : 0f;

        /// <summary>
        /// The radius actually on screen this frame, in metres — <em>the shape</em>, and the thing
        /// rule 3 is about. It climbs from zero to <see cref="Radius"/> through the arm, jumps to
        /// <see cref="Radius"/> times the flare on the frame it bites, and shuts to nothing from
        /// there. Zero in the pool.
        /// </summary>
        public float DrawnRadius
        {
            get
            {
                if (!_live)
                {
                    return 0f;
                }

                return _fired
                    ? _radius * _fireFlare * (1f - FireProgress())
                    : _radius * ArmProgress();
            }
        }

        /// <summary>
        /// How bright the crack is, in <c>[0, 1]</c>: its arming alpha while it counts down, the
        /// bite's alpha fading to nothing once it has. Zero in the pool.
        /// </summary>
        public float Alpha
        {
            get
            {
                if (!_live)
                {
                    return 0f;
                }

                return _fired ? _fireAlpha * (1f - FireProgress()) : _armAlpha;
            }
        }

        /// <summary>
        /// The colour a fissure is drawn in, arming or fired: GD §16.4's reserved red-orange.
        /// </summary>
        /// <remarks>
        /// Rule 2's deliberate use of <see cref="Palette.Danger"/> — see the class remarks. A property
        /// over the palette rather than a serialized field, which is M3-13a's rule and what closed
        /// ledger row 6: a colour read back off a dressed prefab agrees with itself whatever it is
        /// (Traps §7).
        /// </remarks>
        public Color Colour => Palette.Danger;

        /// <summary>Whether this body has the quad it draws with.</summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a crack whose quad was never dragged into the field arms, fires and
        /// closes correctly and draws nothing — the feature silently absent rather than broken.
        /// <see cref="TelegraphRingView.IsDrawable"/>'s reason, for the same reader.
        /// </remarks>
        public bool IsDrawable => _quad != null;

        /// <summary>
        /// Opens a crack at <paramref name="at"/>, <paramref name="radius"/> metres across, biting in
        /// <paramref name="armSeconds"/>.
        /// </summary>
        /// <param name="at">The middle of the crack, in world metres.</param>
        /// <param name="radius">
        /// Its radius in metres — exactly the one core tests, never a flattering approximation of it.
        /// Taken from <c>FissureArmed</c> rather than from this prefab, so a retuned Warden and
        /// whatever M7 authors are the same body scaled and the number lives once, in core.
        /// </param>
        /// <param name="armSeconds">
        /// Seconds before it bites. The event's number, never <c>WardenBehaviour</c>'s constant —
        /// a hazard retuned in core that is still drawn on the old countdown is a telegraph that
        /// lies, which is the one thing GD §9.1 rule 1 forbids outright.
        /// </param>
        /// <remarks>
        /// The position is not guarded, unlike the two floats, for <see cref="TelegraphRingView.Bind"/>'s
        /// reason: it comes off an event core already refused a non-finite position at its own door.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="radius"/> or <paramref name="armSeconds"/> is not a finite positive number.
        /// Neither has a meaningful zero: a crack of no size is nowhere to stand off, and an arm of no
        /// length is a telegraph nobody could have read — and a zero arm is also the division
        /// <see cref="ArmProgress"/> refuses to do.
        /// </exception>
        public void Arm(Vector3 at, float radius, float armSeconds)
        {
            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN radius
            // would scale the body to nowhere and a NaN arm would make every progress reading NaN —
            // neither throws, and neither is recoverable once it is in.
            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    radius,
                    "A fissure's radius must be a finite number greater than zero — it is the " +
                    "circle core tests, so there is no crack without one.");
            }

            if (!(armSeconds > 0f) || float.IsInfinity(armSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(armSeconds),
                    armSeconds,
                    "A fissure's arm must be a finite number greater than zero; a crack that bites " +
                    "the instant it opens is the one thing GD §9.1 rule 1 forbids.");
            }

            _radius = radius;
            _armSeconds = armSeconds;
            _elapsed = 0f;
            _fired = false;
            _live = true;

            // Placed and drawn immediately rather than on the first Step, so a crack is never shown
            // for a frame at the size and place of the one before it.
            transform.SetPositionAndRotation(
                new Vector3(at.x, at.y + _groundOffset, at.z),
                FlatRotation);

            Draw();
        }

        /// <summary>
        /// It has bitten: the crack snaps past its radius and begins to shut.
        /// </summary>
        /// <remarks>
        /// Unconditional on whether anybody was standing in it, which is <c>FissureFired</c>'s own
        /// reasoning: a crack that only flashed when it caught somebody would teach the player that
        /// the ones they dodged never fired. A second call while it is already collapsing is ignored
        /// rather than restarting the flare — core publishes one <c>FissureFired</c> per crack, and
        /// restarting would make a duplicate look like a second bite.
        /// </remarks>
        public void Fire()
        {
            if (!_live || _fired)
            {
                return;
            }

            _fired = true;
            _elapsed = 0f;

            Draw();
        }

        /// <summary>
        /// Advances the crack by <paramref name="dt"/> seconds.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing, which is what a frame that did not advance
        /// the simulation should do to a picture of it.
        /// </param>
        /// <returns>
        /// False once the collapse is over, which is the census's cue to return it. True while it is
        /// still on the floor — including for a step that did nothing.
        /// </returns>
        /// <remarks>
        /// <b>An arm that runs out fires itself</b>, which is the net described on the class: core's
        /// <c>FissureFired</c> normally arrives first, and a crack whose event never reached the
        /// census must not sit on the floor arming for the rest of the run.
        /// </remarks>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the clock would leave this
            // crack drawn nowhere, at no size, for as long as the run lasted, and nothing anywhere
            // would say so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _elapsed += dt;

            if (!_fired)
            {
                if (_elapsed >= _armSeconds)
                {
                    Fire();
                }
                else
                {
                    Draw();
                }

                return true;
            }

            if (_elapsed >= _fireSeconds)
            {
                // Out of service on the step the flare shuts, which is the step the census returns
                // it. Not drawn again first: the body is about to be deactivated.
                _live = false;

                return false;
            }

            Draw();

            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="BossViews"/> calls <see cref="Arm"/> on the
        /// very next line with the crack this body is being put into service for, and the pool has
        /// none of it to give. <see cref="OnDespawn"/> is where the pooling rule lives, so a body is
        /// left clean rather than cleaned on collection — <see cref="TelegraphRingView"/>'s bargain.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the pose and size it was created
        /// at, the crack it was standing for, and which of the two states it was in. <b>The state is
        /// the one easy to forget here</b> — a body returned still fired would draw the next arming
        /// crack as a collapsing flare, which is a telegraph running backwards (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, _restRotation);
            transform.localScale = _restScale;

            _radius = 0f;
            _armSeconds = 0f;
            _elapsed = 0f;
            _fired = false;
            _live = false;

            Paint();
        }

        private void Awake()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restScale = transform.localScale;
        }

        /// <summary>How far through its arm the crack is, in <c>[0, 1]</c>.</summary>
        /// <remarks>
        /// Clamped rather than assumed in range, and the zero-length branch cannot be reached through
        /// <see cref="Arm"/> — it is here so that no arithmetic in this file can divide by a zero
        /// somebody types into the inspector.
        /// </remarks>
        private float ArmProgress() =>
            _armSeconds > 0f ? Mathf.Clamp01(_elapsed / _armSeconds) : 1f;

        /// <summary>How far through its collapse the crack is, in <c>[0, 1]</c>.</summary>
        /// <inheritdoc cref="ArmProgress" />
        private float FireProgress() =>
            _fireSeconds > 0f ? Mathf.Clamp01(_elapsed / _fireSeconds) : 1f;

        /// <summary>
        /// Writes this frame's size and brightness onto the body.
        /// </summary>
        /// <remarks>
        /// The scale <em>is</em> the drawn radius: a quad is one unit across, so a diameter of
        /// <c>2r</c> makes the drawn circle exactly the circle core tests at the moment the arm ends.
        /// Z is left at 1 rather than scaled with the other two — it is the quad's normal after
        /// <see cref="FlatRotation"/>, the decal's thickness through the floor, and scaling it would
        /// mean a large crack floated higher than a small one.
        /// </remarks>
        private void Draw()
        {
            float diameter = DrawnRadius * 2f;

            transform.localScale = new Vector3(diameter, diameter, 1f);

            Paint();
        }

        /// <summary>
        /// Writes the current colour and alpha into the property block, and nowhere else.
        /// </summary>
        /// <remarks>
        /// Tolerates a missing quad rather than throwing, and that is not laxity: this is called from
        /// <see cref="OnDespawn"/>, which the pool runs while a scene is being torn down and the
        /// renderer may already be destroyed. <c>RunScope</c> is where a prefab with no quad is
        /// refused, once, while the run is being composed — not here, once per frame per crack.
        /// </remarks>
        private void Paint()
        {
            // Unity's ==: a destroyed or unassigned renderer is a live reference that only compares
            // equal to null through the engine's operator.
            if (_quad == null)
            {
                return;
            }

            // One allocation per body, on its first frame in service and never again — see the
            // field's own note for why it cannot be made anywhere earlier.
            _properties ??= new MaterialPropertyBlock();

            // The palette's colour and this crack's own alpha: every value in Palette is opaque, and
            // a view that wants transparency multiplies its own (M3-13a rule 8). The alphas here are
            // still serialized, because they are feel numbers rather than colour.
            Color colour = Palette.Danger;

            // **Premultiplied, which the shared material needs and did not get before this task.**
            // M_TelegraphRing blends One / OneMinusSrcAlpha — premultiplied alpha — and does *not*
            // carry URP's `_ALPHAPREMULTIPLY_ON` keyword, so the shader never scales the albedo
            // itself. Writing the palette's colour unscaled therefore draws at full brightness
            // whatever the alpha is, and the alpha only decides how much of the floor shows through.
            // Scaling here is what makes the serialized alphas above mean what their tooltips say.
            // `ZoneView`, `TelegraphRingView` and `BulwarkView` all write unscaled and so are all
            // drawn at full strength today — a pre-existing finding recorded in M4-03's *As built*,
            // fixable for the whole game by one keyword on the material rather than by four files.
            float alpha = Alpha;

            _properties.SetColor(
                _baseColorId,
                new Color(colour.r * alpha, colour.g * alpha, colour.b * alpha, alpha));

            _quad.SetPropertyBlock(_properties);
        }
    }
}
