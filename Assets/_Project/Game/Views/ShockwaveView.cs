using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_Shockwave.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One shield-slam ring, leaving the ground the Warden stood on. It decides nothing and knows no
    /// events: <c>ShockwaveEmitted</c> has already said where it started, how fast it grows and how
    /// far it reaches, and this integrates that and then goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a complete description of the ring and it asks core nothing.</b> A radius is
    /// <c>Speed × age</c>, so the three numbers off the event are the whole of what a ring is for its
    /// whole life — which is exactly why M4-02 put them on the event rather than pairing it with a
    /// read on <c>RunState</c> the way <c>ZoneSpawned</c> is. A ring is an animation and a zone is a
    /// place: an animation a view can run itself is one that cannot stutter when a frame is dropped.
    /// </para>
    /// <para>
    /// <b>Its lifetime is derived, never authored.</b> <see cref="Lifetime"/> is
    /// <c>MaxRadius / Speed</c> — 0.875 s at the Warden's shipped 7 m and 8 m/s — and the numbers are
    /// read off the event rather than off <c>WardenBehaviour</c>'s constants. A second Warden retuned
    /// to a slower ring would otherwise be drawn at the first one's speed, and nothing would say so.
    /// </para>
    /// <para>
    /// <b>The countdown is the net, and <c>ShockwavePassed</c> is the rule.</b> Rule 7: a ring whose
    /// retirement never reached the census must not expand for the rest of the run, and running out
    /// is the one thing such a ring can safely be assumed to have done. In a run where every event
    /// arrives, the event retires it first — <see cref="ZoneView"/>'s net, for its reason.
    /// </para>
    /// <para>
    /// <b>It is drawn as one flat quad, and the front edge is what bites.</b> GD §11.3 names
    /// overlapping transparent VFX as the real mobile fill-rate cost, so the geometry is
    /// <see cref="TelegraphRingView"/>'s single additive quad whose scale <em>is</em> the radius,
    /// rather than a ring of line segments. <b>A true annulus needs a ring texture and belongs to
    /// M7's art pass</b>; until then the disc is drawn at the front edge's radius and fades as it
    /// goes, so what the player reads is an outgoing pulse rather than a growing danger area. The
    /// drawn radius is always exactly the one <c>ShockwaveSystem</c> tested them against.
    /// </para>
    /// <para>
    /// <b>It lies flat, does not billboard, and owns no frame loop.</b> All three are
    /// <see cref="TelegraphRingView"/>'s rules for its reasons: the camera is fixed at 57° (GD §5.1),
    /// and <c>RunTicker</c> steps this with the snapshot's clamped <c>Dt</c> — the step core ran the
    /// ring's own expansion on, so a ring advanced on <c>Time.deltaTime</c> would be somewhere other
    /// than where the edge that hurts is, on exactly the hitching frames the clamp exists for
    /// (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has a job.</b> The same body is a 7 m ring, then
    /// whatever M7's Choirmother emits, then a 7 m ring again; the size, the place and the age are
    /// all inherited by the next life unless they are undone, and the failure mode is silence
    /// (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class ShockwaveView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The pose every ring is drawn at: the quad's own normal turned to face straight up, so its
        /// plane is the arena floor.
        /// </summary>
        /// <remarks>
        /// A quad's face points along its local −Z, and a +90° turn about X sends that to world +Y.
        /// <see cref="TelegraphRingView"/>'s constant, for its reason — <c>static readonly</c> rather
        /// than a field per instance, because the ban on statics is about mutable state that survives
        /// a run (AR §18.4).
        /// </remarks>
        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Tooltip("The one quad this ring is drawn with, on this object. Everything visible about a " +
                 "ring is its scale and its colour, both written here every frame it is live.")]
        [SerializeField] private MeshRenderer _quad;

        [Tooltip("How bright the ring sits at while it is travelling. A slam ring is the widest " +
                 "transparent surface in the game at 14 m across, and GD §11.3's fill-rate note is " +
                 "about exactly this kind of surface.")]
        [Range(0f, 1f)]
        [SerializeField] private float _baseAlpha = 0.65f;

        [Tooltip("The last fraction of the ring's life it spends fading out, in [0, 1]. Without it " +
                 "the ring vanishes at full brightness on the frame it reaches its ceiling, which " +
                 "reads as a dropped frame rather than as a wave that has passed.")]
        [Range(0f, 1f)]
        [SerializeField] private float _fadeFraction = 0.3f;

        [Tooltip("Metres the decal floats above the floor, to keep it out of a z-fight with the " +
                 "arena it is drawn on. Cosmetic in the strict sense: no value here can change " +
                 "which circle core tested, because every distance in the game is measured on XZ.")]
        [Min(0f)]
        [SerializeField] private float _groundOffset = 0.03f;

        /// <summary>
        /// Where the colour and the alpha are written, so nothing here ever touches a shared
        /// material.
        /// </summary>
        /// <remarks>
        /// Built on first use, for <see cref="TelegraphRingView"/>'s reason and not for tidiness: a
        /// field initialiser throws at <em>import</em> from inside prefab serialisation, and
        /// <c>Awake</c> never runs in EditMode (Traps §5), so a pooled body bound from a fixture
        /// would paint through a null.
        /// </remarks>
        private MaterialPropertyBlock _properties;

        /// <summary>URP's colour property, resolved once rather than per write.</summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private float _speed;
        private float _maxRadius;
        private float _elapsed;
        private bool _live;

        /// <summary>
        /// The pose and size this body was created at, restored on the way back to the pool so a
        /// returned body is indistinguishable from one the pool has never handed out.
        /// </summary>
        /// <remarks>
        /// Captured in <see cref="Awake"/>, which outside play mode never runs (Traps §5) — the
        /// initialisers are what an EditMode fixture gets, and they describe a prefab authored at the
        /// origin at unit scale, which <c>VFX_Shockwave.prefab</c> is.
        /// </remarks>
        private Vector3 _restPosition = Vector3.zero;

        /// <inheritdoc cref="_restPosition" />
        private Quaternion _restRotation = Quaternion.identity;

        /// <inheritdoc cref="_restPosition" />
        private Vector3 _restScale = Vector3.one;

        /// <summary>True while this ring is still travelling.</summary>
        public bool IsLive => _live;

        /// <summary>
        /// Where the ring's front edge is right now, in metres — exactly the circle
        /// <c>ShockwaveSystem</c> is testing bodies against. Zero on the frame it is bound, and zero
        /// for a body in the pool.
        /// </summary>
        public float Radius => _live ? Mathf.Min(_speed * _elapsed, _maxRadius) : 0f;

        /// <summary>How fast it grows, in metres per second — the event's own number. Zero in the pool.</summary>
        public float Speed => _live ? _speed : 0f;

        /// <summary>How far it reaches before it is over. Beyond this is the safe ground. Zero in the pool.</summary>
        public float MaxRadius => _live ? _maxRadius : 0f;

        /// <summary>
        /// How long this ring lasts, in seconds: <see cref="MaxRadius"/> over <see cref="Speed"/>.
        /// Zero in the pool.
        /// </summary>
        /// <remarks>
        /// Derived rather than carried, which is rule 7's whole point — the two numbers the event
        /// does carry already say it, and a third field could disagree with them.
        /// </remarks>
        public float Lifetime => _live && _speed > 0f ? _maxRadius / _speed : 0f;

        /// <summary>
        /// How bright the ring is, in <c>[0, 1]</c>: its resting alpha until the fade begins, then
        /// linearly to nothing. Zero in the pool.
        /// </summary>
        public float Alpha
        {
            get
            {
                if (!_live)
                {
                    return 0f;
                }

                float fade = Mathf.Clamp01(_fadeFraction);

                if (fade <= 0f)
                {
                    return _baseAlpha;
                }

                float remaining = 1f - Progress();

                return remaining >= fade ? _baseAlpha : _baseAlpha * (remaining / fade);
            }
        }

        /// <summary>
        /// The colour a slam ring is drawn in: GD §16.4's reserved red-orange, because a ring that
        /// takes 22 hit points off you is danger and nothing else.
        /// </summary>
        /// <remarks>
        /// A property over <see cref="Palette"/> rather than a serialized field, which is M3-13a's
        /// rule and closed ledger row 6: a colour read back off a dressed prefab agrees with itself
        /// whatever it is (Traps §7), so the field is the worse of the two even when it is dressed
        /// correctly.
        /// </remarks>
        public Color Colour => Palette.Danger;

        /// <summary>Whether this body has the quad it draws with.</summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a ring whose quad was never dragged into the field runs its whole life
        /// correctly and draws nothing — the feature silently absent rather than broken.
        /// <see cref="TelegraphRingView.IsDrawable"/>'s reason, for the same reader.
        /// </remarks>
        public bool IsDrawable => _quad != null;

        /// <summary>
        /// Sends this ring out from <paramref name="origin"/> at <paramref name="speed"/> metres a
        /// second, as far as <paramref name="maxRadius"/>.
        /// </summary>
        /// <param name="origin">The slam point, in world metres — where the boss <em>stood</em>.</param>
        /// <param name="speed">How fast the front edge travels. The event's number, never a constant.</param>
        /// <param name="maxRadius">Where the ring is over. The event's number, never a constant.</param>
        /// <remarks>
        /// The origin is not guarded, unlike the two floats, for <see cref="TelegraphRingView.Bind"/>'s
        /// reason: it comes off an event core already refused a non-finite position at its own door.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="speed"/> or <paramref name="maxRadius"/> is not a finite positive number.
        /// Neither has a meaningful zero: a ring that does not travel never reaches anybody, and one
        /// with no ceiling never ends — and a zero speed is also the division <see cref="Lifetime"/>
        /// refuses to do.
        /// </exception>
        public void Bind(Vector3 origin, float speed, float maxRadius)
        {
            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN speed
            // would make every radius reading NaN and a NaN ceiling would leave the ring expanding
            // for the rest of the run — neither throws, and neither is recoverable once it is in.
            if (!(speed > 0f) || float.IsInfinity(speed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(speed),
                    speed,
                    "A shockwave's speed must be a finite number greater than zero — it is what " +
                    "turns the ring's age into the circle core is testing, so there is no ring " +
                    "without one.");
            }

            if (!(maxRadius > 0f) || float.IsInfinity(maxRadius))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRadius),
                    maxRadius,
                    "A shockwave's ceiling must be a finite number greater than zero; a ring with " +
                    "no ceiling is one that never leaves the floor it was slammed on.");
            }

            _speed = speed;
            _maxRadius = maxRadius;
            _elapsed = 0f;
            _live = true;

            // Placed and drawn immediately rather than on the first Step, so a ring is never shown
            // for a frame at the size and place of the one before it.
            transform.SetPositionAndRotation(
                new Vector3(origin.x, origin.y + _groundOffset, origin.z),
                FlatRotation);

            Draw();
        }

        /// <summary>
        /// Advances the ring by <paramref name="dt"/> seconds.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing, which is what a frame that did not advance
        /// the simulation should do to a picture of it.
        /// </param>
        /// <returns>
        /// False once its own countdown is over, which is the census's cue to return it. True while
        /// it is still travelling — including for a step that did nothing.
        /// </returns>
        /// <remarks>
        /// <b>This is the net, not the rule</b> (rule 7). <c>ShockwavePassed</c> is what normally
        /// retires a ring and it arrives from the same tick that retired it in core; this countdown
        /// only ever fires for a ring whose retirement never reached the census.
        /// </remarks>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the age would leave this
            // ring drawn nowhere, at no size, for as long as the run lasted, and nothing anywhere
            // would say so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _elapsed += dt;

            if (_elapsed >= Lifetime)
            {
                // Out of service on the step it reaches its ceiling, which is the step the census
                // returns it. Not drawn again first: the body is about to be deactivated.
                _live = false;

                return false;
            }

            Draw();

            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="BossViews"/> calls <see cref="Bind"/> on
        /// the very next line with the ring this body is being put into service for, and the pool has
        /// none of it to give. <see cref="OnDespawn"/> is where the pooling rule lives, so a body is
        /// left clean rather than cleaned on collection — <see cref="TelegraphRingView"/>'s bargain.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the pose and size it was created
        /// at, and the ring it was standing for. The scale is the one easy to forget, because it is
        /// the only part of a ring's state that lives on the transform rather than in a field — a
        /// body returned still fourteen metres across would be handed to the next slam as a wave
        /// that starts at its ceiling (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, _restRotation);
            transform.localScale = _restScale;

            _speed = 0f;
            _maxRadius = 0f;
            _elapsed = 0f;
            _live = false;

            Paint();
        }

        private void Awake()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restScale = transform.localScale;
        }

        /// <summary>How far through its life the ring is, in <c>[0, 1]</c>.</summary>
        /// <remarks>
        /// Clamped rather than assumed in range: the census returns a ring on the step it runs out,
        /// but <see cref="Bind"/> is the only thing that resets the clock, so a body stepped twice
        /// before it is collected must read 1 rather than 1.02.
        /// </remarks>
        private float Progress()
        {
            float lifetime = Lifetime;

            return lifetime > 0f ? Mathf.Clamp01(_elapsed / lifetime) : 1f;
        }

        /// <summary>
        /// Writes this frame's size and brightness onto the body.
        /// </summary>
        /// <remarks>
        /// The scale <em>is</em> the radius: a quad is one unit across, so a diameter of <c>2r</c>
        /// makes the drawn circle exactly the circle core tested. Z is left at 1 rather than scaled
        /// with the other two — it is the quad's normal after <see cref="FlatRotation"/>, the decal's
        /// thickness through the floor, and scaling it would mean a large ring floated higher than a
        /// small one.
        /// </remarks>
        private void Draw()
        {
            float diameter = Radius * 2f;

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
        /// refused, once, while the run is being composed — not here, once per frame per ring.
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

            // The palette's colour and this ring's own alpha: every value in Palette is opaque, and
            // a view that wants transparency multiplies its own (M3-13a rule 8). The two alphas here
            // are still serialized, because they are feel numbers rather than colour.
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
