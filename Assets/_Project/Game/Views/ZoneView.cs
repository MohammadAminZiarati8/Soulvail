using System;
using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_ConsecrateZone.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One patch of ground on the floor. It decides nothing and knows no events: core has already
    /// said where, how wide and for how long, and this draws that, flashes when it is told something
    /// was actually healed, and then goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="TelegraphRingView"/>'s body with a longer life and no fill.</b> M2-12b already
    /// built a ground decal that places itself, runs a countdown and is returned by the census that
    /// rented it; a zone is that object asked to stand still for six seconds instead of eight tenths
    /// of one. The geometry is the same single additive quad whose scale <em>is</em> the radius —
    /// GD §11.3 names overlapping transparent VFX as the real mobile fill-rate cost, and a zone is by
    /// construction the largest decal in the game.
    /// </para>
    /// <para>
    /// <b>It does not fill, and that is the difference that matters.</b> A telegraph fills because the
    /// amount of ring that exists is the amount of time left — it is a promise being counted down to.
    /// A zone is not a promise about a moment; it is a place that is either good to stand in or not,
    /// for as long as it lasts. Drawing a shrinking disc would tell the player the <em>area</em> was
    /// closing in on them, which is the one thing about a Consecrate that is not true.
    /// </para>
    /// <para>
    /// <b>The flash is the only thing that moves.</b> <see cref="Pulse"/> is called when core says hit
    /// points were actually restored (<c>ZoneHealed</c> with a non-zero amount, M3-11b rule 8), and
    /// the disc brightens and settles back. A zone that flashed on its own schedule would be claiming
    /// a heal on every tick of its pulse clock, including the ones at full health that restore
    /// nothing — GD §16.3's game-feel list is built on exactly that distinction.
    /// </para>
    /// <para>
    /// <b>Its own countdown is cosmetic, and core's <c>ZoneExpired</c> is the authority.</b>
    /// <see cref="Step"/> exists so a decal orphaned by a missed event cannot stand on the floor for
    /// the rest of the run — <see cref="TelegraphRingView"/>'s <em>"it returns itself by running
    /// out"</em>, kept as the net rather than as the rule. Normally the event retires it first.
    /// </para>
    /// <para>
    /// <b>It lies flat and does not billboard</b>, and <b>it owns no frame loop</b>. Both are
    /// <see cref="TelegraphRingView"/>'s rules for its reasons: the camera is fixed at 57° (GD §5.1),
    /// and <c>RunTicker</c> steps this with the snapshot's clamped <c>Dt</c> because that is the step
    /// core ran the zone's own life on (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has a job.</b> The same body is a 3.5 m Consecrate,
    /// then whatever M7 authors at 6 m, then a Consecrate again; the size, the place, the time left
    /// and the flash in progress are all inherited by the next life unless they are undone, and the
    /// failure mode is silence (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class ZoneView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The pose every decal is drawn at: the quad's own normal turned to face straight up, so its
        /// plane is the arena floor.
        /// </summary>
        /// <remarks>
        /// A quad's face points along its local −Z, and a +90° turn about X sends that to world +Y.
        /// <see cref="TelegraphRingView"/>'s constant, for its reason — <c>static readonly</c> rather
        /// than a field per instance, because the ban on statics is about mutable state that survives
        /// a run (AR §18.4).
        /// </remarks>
        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Tooltip("The one quad this decal is drawn with, on this object. Everything visible about a " +
                 "zone is its scale and its colour, both written here every frame it is live.")]
        [SerializeField] private MeshRenderer _quad;

        [Tooltip("What the ground is drawn in. Authored on the prefab rather than written here: " +
                 "GD §16.4 reserves #22D3EE for the player and the things that are safe, which is " +
                 "what a patch of healing ground is. The initialiser below is deliberately NOT that " +
                 "colour, so a prefab whose field never bound is visible rather than plausible.")]
        [SerializeField] private Color _colour = Color.white;

        [Tooltip("How bright the disc sits at while nothing is happening. Low: it is on screen for " +
                 "six seconds under the player's feet, and GD §11.3's fill-rate note is about " +
                 "exactly this kind of surface.")]
        [Range(0f, 1f)]
        [SerializeField] private float _baseAlpha = 0.18f;

        [Tooltip("How bright it goes at the instant of a pulse that actually healed something.")]
        [Range(0f, 1f)]
        [SerializeField] private float _pulseAlpha = 0.55f;

        [Tooltip("How long one flash takes to settle back, in seconds. Shorter than Consecrate's " +
                 "0.5 s pulse interval, so two pulses read as two rather than as one bright disc.")]
        [Min(0f)]
        [SerializeField] private float _pulseSeconds = 0.2f;

        [Tooltip("Metres the decal floats above the floor, to keep it out of a z-fight with the " +
                 "arena it is drawn on. Cosmetic in the strict sense: no value here can change " +
                 "which circle core tested, because every distance in the game is measured on XZ.")]
        [SerializeField] private float _groundOffset = 0.01f;

        /// <summary>
        /// Where the colour and the alpha are written, so nothing here ever touches a shared
        /// material.
        /// </summary>
        /// <remarks>
        /// Built on first use, for <see cref="TelegraphRingView"/>'s reason and not for tidiness: a
        /// field initialiser throws at <em>import</em> from inside prefab serialisation, and
        /// <c>Awake</c> never runs in EditMode (Traps §5), so a pooled body placed from a fixture
        /// would paint through a null.
        /// </remarks>
        private MaterialPropertyBlock _properties;

        /// <summary>URP's colour property, resolved once rather than per write.</summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private float _radius;
        private float _duration;
        private float _elapsed;
        private float _flashLeft;
        private bool _live;

        /// <summary>
        /// The pose and size this body was created at, restored on the way back to the pool so a
        /// returned body is indistinguishable from one the pool has never handed out.
        /// </summary>
        /// <remarks>
        /// Captured in <see cref="Awake"/>, which outside play mode never runs (Traps §5) — the
        /// initialisers are what an EditMode fixture gets, and they describe a prefab authored at the
        /// origin at unit scale, which <c>VFX_ConsecrateZone.prefab</c> is.
        /// </remarks>
        private Vector3 _restPosition = Vector3.zero;

        /// <inheritdoc cref="_restPosition" />
        private Quaternion _restRotation = Quaternion.identity;

        /// <inheritdoc cref="_restPosition" />
        private Vector3 _restScale = Vector3.one;

        /// <summary>True while this decal is still standing on the floor.</summary>
        public bool IsLive => _live;

        /// <summary>
        /// The circle this decal stands for, in metres — exactly the one core is healing inside.
        /// Zero for a body in the pool.
        /// </summary>
        public float Radius => _live ? _radius : 0f;

        /// <summary>
        /// How bright the disc is, in <c>[0, 1]</c>: its resting alpha, climbing to the pulse alpha
        /// at the instant of a flash and settling back over <c>_pulseSeconds</c>. Zero in the pool.
        /// </summary>
        public float Alpha =>
            _live ? Mathf.Lerp(_baseAlpha, _pulseAlpha, FlashProgress()) : 0f;

        /// <summary>Whether a flash is in progress right now.</summary>
        /// <remarks>
        /// The rendering fact the two pulse rows are about. A count of flashes would be state kept
        /// for a test; this is what is actually on the screen, which is the thing a view is allowed
        /// to be asked.
        /// </remarks>
        public bool IsFlashing => _live && _flashLeft > 0f;

        /// <summary>The colour this decal was authored with. GD §16.4's vocabulary, and nothing else.</summary>
        public Color Colour => _colour;

        /// <summary>
        /// Whether this body has the quad it draws with.
        /// </summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a decal whose quad was never dragged into the field runs its whole life
        /// correctly and draws nothing — the feature silently absent rather than broken.
        /// <see cref="TelegraphRingView.IsDrawable"/>'s reason, for the same reader.
        /// </remarks>
        public bool IsDrawable => _quad != null;

        /// <summary>
        /// Puts this decal on the floor at <paramref name="position"/>, <paramref name="radius"/>
        /// metres across, for <paramref name="duration"/> seconds.
        /// </summary>
        /// <param name="position">The middle of the zone, in world metres.</param>
        /// <param name="radius">
        /// The zone's radius in metres — exactly the one core heals inside, never a flattering
        /// approximation of it. Taken from <c>ZoneSpawned</c> rather than from this prefab, so a
        /// 3.5 m Consecrate and whatever M7 authors at 6 m are the same body scaled and the number
        /// lives once, in the asset core reads (M3-11b rule 11).
        /// </param>
        /// <param name="duration">How long the zone stands, in seconds.</param>
        /// <remarks>
        /// The position is not guarded, unlike the two floats, for <see cref="TelegraphRingView.Bind"/>'s
        /// reason: it comes off an event core already refused a non-finite position at its own door.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="radius"/> or <paramref name="duration"/> is not a finite positive number.
        /// Neither has a meaningful zero: ground of no size is nowhere to stand, and ground with no
        /// length is a skill that did nothing.
        /// </exception>
        public void Place(Vector3 position, float radius, float duration)
        {
            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN radius
            // would scale the body to nowhere and a NaN duration would make every progress reading
            // NaN — neither throws, and neither is recoverable once it is in.
            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    radius,
                    "A zone's radius must be a finite number greater than zero — it is the circle " +
                    "core heals inside, so there is no decal without one.");
            }

            if (!(duration > 0f) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "A zone's duration must be a finite number greater than zero; ground that " +
                    "stands for no time is a skill the player could never have used.");
            }

            _radius = radius;
            _duration = duration;
            _elapsed = 0f;
            _flashLeft = 0f;
            _live = true;

            // Placed and drawn immediately rather than on the first Step, so a decal is never shown
            // for a frame at the size and place of the one before it.
            transform.SetPositionAndRotation(
                new Vector3(position.x, position.y + _groundOffset, position.z),
                FlatRotation);

            Draw();
        }

        /// <summary>
        /// One flash: something standing here was actually healed.
        /// </summary>
        /// <remarks>
        /// Unconditional, deliberately. Whether a pulse <em>earned</em> a flash is a question about
        /// the event's <c>Amount</c>, and <see cref="ZoneViews"/> is where that is read — this object
        /// is handed the decision, exactly as it is handed its radius. A re-pulse before the last one
        /// settled restarts the flash rather than queueing a second, because two overlapping flashes
        /// on one disc are one brighter disc and the player cannot count them either way.
        /// </remarks>
        public void Pulse()
        {
            if (!_live)
            {
                return;
            }

            _flashLeft = _pulseSeconds;

            Paint();
        }

        /// <summary>
        /// Advances the decal by <paramref name="dt"/> seconds.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing, which is what a frame that did not advance
        /// the simulation should do to a picture of it.
        /// </param>
        /// <returns>
        /// False once its own countdown is over, which is the census's cue to return it. True while
        /// it is still standing — including for a step that did nothing.
        /// </returns>
        /// <remarks>
        /// <b>This is the net, not the rule.</b> <c>ZoneExpired</c> is what normally retires a decal,
        /// and it arrives from the same tick that ended the zone. This countdown only ever fires for a
        /// decal whose expiry never reached it, and running out is the one thing such a decal can
        /// safely be assumed to have done.
        /// </remarks>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the elapsed time would leave
            // this decal drawn nowhere, at no size, for as long as the run lasted, and nothing
            // anywhere would say so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _elapsed += dt;

            if (_flashLeft > 0f)
            {
                _flashLeft = Mathf.Max(0f, _flashLeft - dt);
            }

            if (_elapsed >= _duration)
            {
                // Out of service on the step it runs out, which is the step the census returns it.
                // Not drawn again first: the body is about to be deactivated.
                _live = false;

                return false;
            }

            Paint();

            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="ZoneViews"/> calls <see cref="Place"/> on
        /// the very next line with the zone this body is being put into service for, and the pool has
        /// none of it to give. <see cref="OnDespawn"/> is where the pooling rule lives, so a body is
        /// left clean rather than cleaned on collection — <see cref="TelegraphRingView"/>'s bargain.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the pose and size it was created
        /// at, the zone it was standing for, and any flash still settling. The scale is the one easy
        /// to forget, because it is the only part of a zone's state that lives on the transform
        /// rather than in a field — a body returned still seven metres across would be handed to the
        /// next Consecrate as ground that starts far too wide (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, _restRotation);
            transform.localScale = _restScale;

            _radius = 0f;
            _duration = 0f;
            _elapsed = 0f;
            _flashLeft = 0f;
            _live = false;

            Paint();
        }

        private void Awake()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restScale = transform.localScale;
        }

        /// <summary>How much of the current flash is left, in <c>[0, 1]</c>. Zero when none is.</summary>
        /// <remarks>
        /// The zero-length branch cannot be reached through an authored prefab and is here so that no
        /// arithmetic in this file can divide by a zero somebody types into the inspector.
        /// </remarks>
        private float FlashProgress() =>
            _pulseSeconds > 0f ? Mathf.Clamp01(_flashLeft / _pulseSeconds) : 0f;

        /// <summary>
        /// Writes this frame's size and brightness onto the body.
        /// </summary>
        /// <remarks>
        /// The scale <em>is</em> the radius: a quad is one unit across, so a diameter of <c>2r</c>
        /// makes the drawn circle exactly the circle core heals inside. Z is left at 1 rather than
        /// scaled with the other two — it is the quad's normal after <see cref="FlatRotation"/>, the
        /// decal's thickness through the floor, and scaling it would mean a large zone floated higher
        /// than a small one.
        /// </remarks>
        private void Draw()
        {
            float diameter = _radius * 2f;

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
        /// refused, once, while the run is being composed — not here, once per frame per decal.
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

            _properties.SetColor(_baseColorId, new Color(_colour.r, _colour.g, _colour.b, Alpha));

            _quad.SetPropertyBlock(_properties);
        }
    }
}
