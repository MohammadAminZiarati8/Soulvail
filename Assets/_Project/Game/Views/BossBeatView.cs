using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_BossBeat.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The shell a boss wears while it cannot be hurt — GD §9.1 rule 3's invulnerable beat, drawn.
    /// It decides nothing and knows no events: <c>BossBeatStarted</c> has said whose body and for how
    /// long, and this hangs on that body, pulses, and goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is parented to the agent, not raised on a canvas</b> (rule 4). A screen-wide flash every
    /// phase change is GD §11.3's fill-rate warning on a phone and a readability problem besides;
    /// what the player needs to know is <em>this thing is not taking damage</em>, which is a property
    /// of the thing. Parenting is also what makes the shell follow a body that is still being moved
    /// by <c>EnemyViews</c> — no frame ordering to get wrong and no position to copy.
    /// </para>
    /// <para>
    /// <b>It pulses rather than sitting still</b>, because a static shell reads as art. The pulse is
    /// a scale and a brightness together, which is <see cref="ZoneView.Pulse"/>'s vocabulary; what it
    /// is <em>not</em> is <see cref="Palette.Danger"/>. A beat is the boss being safe, not the player
    /// being in danger, and GD §16.4 reserves that colour for the second thing and nothing else — so
    /// the shell is <see cref="Palette.Neutral"/>, §16.4's <em>"everything else"</em>. Cyan was
    /// considered and refused for the mirror of the same reason: it means the player and the things
    /// that keep them safe.
    /// </para>
    /// <para>
    /// <b>Its own countdown is the net, and <c>BossBeatEnded</c> is the rule.</b> The beat's length
    /// rides on <c>BossBeatStarted</c> exactly so this body can end itself if the closing event never
    /// reaches the census — <see cref="ZoneView"/>'s net, for its reason. A shell that outlived its
    /// beat would say a boss was untouchable while it was being killed.
    /// </para>
    /// <para>
    /// <b>It owns no frame loop.</b> <c>RunTicker</c> steps it with the snapshot's clamped <c>Dt</c>,
    /// the step core ran the beat's own 1.5 s on, so a shell on the wall clock would come off before
    /// or after the boss became hittable again (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, and the parent is the thing <see cref="OnDespawn"/> must undo.</b> Everything
    /// else a life leaves behind is a field; the parent is not, and a body returned still hanging off
    /// a corpse would be moved, scaled and eventually destroyed by an object the pool knows nothing
    /// about (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class BossBeatView : MonoBehaviour, IPoolable
    {
        [Tooltip("The shell this beat is drawn with — a sphere around the body, on a child object " +
                 "so its own scale is independent of the boss's. Everything visible about a beat " +
                 "is this renderer's scale and colour, both written here every frame it is live.")]
        [SerializeField] private Renderer _shell;

        [Tooltip("The shell's resting size, as a local scale under the body it hangs on. The boss's " +
                 "own body scale multiplies this — a Warden is 2.2 — so the shell wraps whatever " +
                 "it is attached to rather than needing a number per archetype.")]
        [SerializeField] private Vector3 _shellScale = new Vector3(1.6f, 1.9f, 1.6f);

        [Tooltip("How much larger the shell swells at the top of a pulse, as a multiple.")]
        [Min(1f)]
        [SerializeField] private float _pulseScale = 1.12f;

        [Tooltip("How many full pulses a second. Fast enough to read as a heartbeat inside a 1.5 s " +
                 "beat and slow enough not to strobe (GD §16.3).")]
        [Min(0f)]
        [SerializeField] private float _pulseHz = 2f;

        [Tooltip("How bright the shell sits at the bottom of a pulse.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minAlpha = 0.25f;

        [Tooltip("How bright it goes at the top of one. Deliberately low: a boss is the largest " +
                 "body in the game, so this is both the largest transparent surface on screen " +
                 "during a beat (GD §11.3) and the one most able to hide the thing it is about.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxAlpha = 0.6f;

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

        /// <summary>
        /// Where this body hangs when it is not on a boss: the pool's own root.
        /// </summary>
        /// <remarks>
        /// <b>Captured on the first <see cref="Bind"/> rather than in <see cref="Awake"/></b>, which
        /// is the one place this class departs from its siblings and it is Traps §5 that forces it:
        /// <see cref="Awake"/> never runs in EditMode, so a fixture's body would record a rest parent
        /// of null and be returned to the scene root instead of to the pool's. At the first bind the
        /// object is still exactly where the pool created it, which is the answer both in a run and
        /// in a fixture.
        /// </remarks>
        private Transform _restParent;

        private bool _restParentCaptured;

        private float _seconds;
        private float _elapsed;
        private bool _live;

        /// <summary>True while the beat is still running.</summary>
        public bool IsLive => _live;

        /// <summary>
        /// The body this shell is hanging on, or null for one in the pool. Rule 4, read back: what a
        /// beat is drawn on is a transform in the arena and never a canvas.
        /// </summary>
        public Transform Body => _live ? transform.parent : null;

        /// <summary>How long the beat lasts, in seconds — the event's number. Zero in the pool.</summary>
        public float Seconds => _live ? _seconds : 0f;

        /// <summary>
        /// How bright the shell is, in <c>[0, 1]</c>: between its two authored alphas, on the pulse.
        /// Zero in the pool.
        /// </summary>
        public float Alpha => _live ? Mathf.Lerp(_minAlpha, _maxAlpha, Pulse()) : 0f;

        /// <summary>
        /// The colour a beat is drawn in: GD §16.4's desaturated bone, <em>everything else</em>.
        /// </summary>
        /// <remarks>
        /// Deliberately neither <see cref="Palette.Danger"/> nor <see cref="Palette.Player"/> — see
        /// the class remarks. A property over the palette rather than a serialized field, which is
        /// M3-13a's rule and what closed ledger row 6.
        /// </remarks>
        public Color Colour => Palette.Neutral;

        /// <summary>Whether this body has the shell it draws with.</summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a shell whose renderer was never dragged into the field binds, pulses
        /// and retires correctly and draws nothing — the feature silently absent rather than broken.
        /// <see cref="TelegraphRingView.IsDrawable"/>'s reason, for the same reader.
        /// </remarks>
        public bool IsDrawable => _shell != null;

        /// <summary>
        /// Hangs this shell on <paramref name="body"/> for <paramref name="seconds"/>.
        /// </summary>
        /// <param name="body">
        /// The boss's body, in the arena. Rule 4: the effect goes on the thing that is not taking
        /// damage, so this is a world transform and never a canvas.
        /// </param>
        /// <param name="seconds">
        /// How long the beat lasts. The event's number, never the spec's — whatever draws the beat
        /// holds a look book and not the catalog, which is <c>BossBeatStarted.Seconds</c>' own reason
        /// for existing.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="body"/> is null or destroyed. A shell with nothing to hang on is the
        /// census asking for a beat on a boss that has no view, which is a wiring fault rather than
        /// a frame to survive.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="seconds"/> is not a finite positive number. A beat of no length is a
        /// phase change with nothing between its two halves, and one that never ended would say a
        /// boss was untouchable for the rest of the run.
        /// </exception>
        public void Bind(Transform body, float seconds)
        {
            // Unity's ==: a destroyed transform is a live reference that only compares equal to null
            // through the engine's operator, and parenting to one would lose this body silently.
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN length
            // would make every progress reading NaN and leave the shell on the boss for the rest of
            // the run — it does not throw, and it is not recoverable once it is in.
            if (!(seconds > 0f) || float.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    seconds,
                    "A beat's length must be a finite number greater than zero — it is the window " +
                    "the boss cannot be hurt in, so there is no shell without one.");
            }

            // Before the reparent, and only once: at this moment the body is still where the pool
            // put it. See the field's own note for why Awake cannot do this.
            if (!_restParentCaptured)
            {
                _restParent = transform.parent;
                _restParentCaptured = true;
            }

            _seconds = seconds;
            _elapsed = 0f;
            _live = true;

            // worldPositionStays: false — the shell is defined in the body's space, so it lands
            // centred on it rather than keeping the pose the pool left it in.
            transform.SetParent(body, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Drawn immediately rather than on the first Step, so the shell is never shown for a
            // frame at the size and brightness the previous beat ended on.
            Draw();
        }

        /// <summary>
        /// Advances the beat by <paramref name="dt"/> seconds.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing, which is what a frame that did not advance
        /// the simulation should do to a picture of it.
        /// </param>
        /// <returns>
        /// False once its own countdown is over, which is the census's cue to return it. True while
        /// the beat is still running — including for a step that did nothing.
        /// </returns>
        /// <remarks>
        /// <b>This is the net, not the rule.</b> <c>BossBeatEnded</c> is what normally takes the
        /// shell off, and it arrives from the tick the boss became hittable again.
        /// </remarks>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the clock would leave this
            // shell on the boss for as long as the run lasted, and nothing anywhere would say so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _elapsed += dt;

            if (_elapsed >= _seconds)
            {
                // Out of service on the step the beat ends, which is the step the census returns it.
                // Not drawn again first: the body is about to be deactivated.
                _live = false;

                return false;
            }

            Draw();

            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="BossViews"/> calls <see cref="Bind"/> on
        /// the very next line with the body this shell is being put into service for, and the pool
        /// has none of it to give. <see cref="OnDespawn"/> is where the pooling rule lives.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The parent first, and it is the only line here that is not bookkeeping: a shell left
        /// hanging off the boss would be scaled by an archetype look it has nothing to do with, moved
        /// by a body the pool does not own, and destroyed with that body rather than with the pool
        /// that made it.
        /// </remarks>
        public void OnDespawn()
        {
            // Only once a bind has recorded where this body belongs. Before that it has never left
            // the pool's root, so there is nothing to put back — and reparenting to a null captured
            // by nobody would move it to the scene root.
            if (_restParentCaptured)
            {
                transform.SetParent(_restParent, worldPositionStays: false);
            }

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            _seconds = 0f;
            _elapsed = 0f;
            _live = false;

            Paint();
        }

        /// <summary>Where in the pulse the shell is, in <c>[0, 1]</c>: 0 at the trough, 1 at the peak.</summary>
        /// <remarks>
        /// A cosine rather than a saw, so the swell has no corner in it — the shell breathes rather
        /// than ticking. A zero rate holds it at the trough, which is the honest answer for a pulse
        /// that does not pulse rather than a division to guard.
        /// </remarks>
        private float Pulse() =>
            0.5f - (0.5f * Mathf.Cos(_elapsed * _pulseHz * 2f * Mathf.PI));

        /// <summary>Writes this frame's size and brightness onto the shell.</summary>
        /// <remarks>
        /// The scale is written in the body's space, so a Warden at body scale 2.2 wears a shell 2.2
        /// times the authored one without this class knowing the number — which is the whole reason
        /// the shell is parented rather than placed.
        /// </remarks>
        private void Draw()
        {
            transform.localScale = _shellScale * Mathf.Lerp(1f, _pulseScale, Pulse());

            Paint();
        }

        /// <summary>
        /// Writes the current colour and alpha into the property block, and nowhere else.
        /// </summary>
        /// <remarks>
        /// Tolerates a missing shell rather than throwing, and that is not laxity: this is called
        /// from <see cref="OnDespawn"/>, which the pool runs while a scene is being torn down and the
        /// renderer may already be destroyed. <c>RunScope</c> is where a prefab with no shell is
        /// refused, once, while the run is being composed — not here, once per frame.
        /// </remarks>
        private void Paint()
        {
            // Unity's ==: a destroyed or unassigned renderer is a live reference that only compares
            // equal to null through the engine's operator.
            if (_shell == null)
            {
                return;
            }

            // One allocation per body, on its first frame in service and never again — see the
            // field's own note for why it cannot be made anywhere earlier.
            _properties ??= new MaterialPropertyBlock();

            // The palette's colour and this shell's own alpha: every value in Palette is opaque, and
            // a view that wants transparency multiplies its own (M3-13a rule 8). The two alphas here
            // are still serialized, because they are feel numbers rather than colour.
            Color colour = Palette.Neutral;

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

            _shell.SetPropertyBlock(_properties);
        }
    }
}
