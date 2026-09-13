using System;
using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_TelegraphRing.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One ring on the ground. It decides nothing and knows no events: core has already said where
    /// and for how long, and this draws that and then goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Filling means <em>coming</em>; fading means <em>happened</em>.</b> A spawn ring is a disc
    /// growing from the centre to the rim over <c>SpawnDirector.TelegraphTime</c>, so the amount of
    /// ring that exists is the amount of time left — readable at a glance and without a number
    /// (GD §7.1, §9.1 rule 1). A blast ring is drawn at full radius on its first frame and fades,
    /// because there is nothing left to count down to: the damage was applied by the corpse that
    /// owned it (M2-08), and a blast ring that filled would be promising an explosion that is
    /// already over.
    /// </para>
    /// <para>
    /// <b>The radius is the truth, not a decoration.</b> Whatever it is bound with is drawn exactly,
    /// so <em>"I was outside it"</em> is a statement about the same circle core tested. A ring drawn
    /// a little larger to look better would make the game a liar in the one place GD §12.4's
    /// guardrails are about fairness.
    /// </para>
    /// <para>
    /// <b>One quad, scaled — not a ring of line segments.</b> GD §11.3 names overlapping transparent
    /// VFX as the real mobile fill-rate cost, and eleven rings from one wave is the exact shape of
    /// it. So the geometry is a single additive quad whose scale <em>is</em> the radius, rather than
    /// <c>ReticleView</c>'s <see cref="LineRenderer"/> ring: that one is a single marker under the
    /// player's target and can afford thirty-two segments, and a wave's worth of them could not.
    /// It is also why the fill is the quad's own size rather than a shader property — a growing disc
    /// is what rule 5 describes, and it costs no shader and no second draw.
    /// </para>
    /// <para>
    /// <b>It lies flat and does not billboard.</b> The camera is fixed at 57° (GD §5.1) and nothing
    /// in this game is seen from the side, so a billboard would solve a problem that cannot occur —
    /// <c>ReticleView</c>'s reasoning about its own chevron, applied to a decal. The flat pose is set
    /// here rather than authored on the prefab, so a prefab dressed at the wrong angle cannot make a
    /// ring that faces the sky, and this object holds no <see cref="Camera"/> for anything later to
    /// be tempted with.
    /// </para>
    /// <para>
    /// <b>It owns no <see cref="MonoBehaviour"/> frame loop.</b> <c>RunTicker</c> calls
    /// <see cref="Step"/> with the snapshot's clamped <c>Dt</c> — the same step core is running the
    /// telegraph's own countdown on. An <c>Update</c> here would run on the wall clock, and on a
    /// hitching frame, where the clamp shortens core's step but not the wall's, the ring would finish
    /// filling before — or after — the body it is promising actually appears (AR §18.1, §18.2).
    /// <see cref="ProjectileView"/>'s rule, for its reason.
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has the same job <see cref="ProjectileView"/>'s
    /// has.</b> The same body is a spawn ring, then a blast ring, then a spawn ring again; anything
    /// one life leaves behind — how far along it was, how big it got, how bright it still is — is
    /// inherited by the next unless it is undone, and the failure mode is silence (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class TelegraphRingView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The pose every ring is drawn at: the quad's own normal turned to face straight up, so its
        /// plane is the arena floor.
        /// </summary>
        /// <remarks>
        /// A quad's face points along its local −Z, and a +90° turn about X sends that to world +Y.
        /// <c>static readonly</c> rather than a field per instance: it is a constant, and the ban on
        /// statics is about mutable state that survives a run (AR §18.4) — <c>ThreatArrows</c>' two
        /// palette colours are the same shape.
        /// </remarks>
        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Tooltip("The one quad this ring is drawn with, on this object. Everything visible about a " +
                 "ring is its scale and its colour, both written here every frame it is live.")]
        [SerializeField] private MeshRenderer _quad;

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
        /// <para>
        /// <c>ReticleView</c>'s reason for having one at all: assigning to <c>Renderer.material</c>
        /// instantiates a copy per renderer, which for a pool of a dozen rings is a dozen leaked
        /// materials the pool does not know it owns.
        /// </para>
        /// <para>
        /// <b>Built on first use, and neither of the two obvious places works.</b> A field
        /// initialiser throws — <c>"CreateImpl is not allowed to be called from a MonoBehaviour
        /// constructor (or instance field initializer)"</c> — and it throws at <em>import</em>, from
        /// inside <c>AddComponent</c> and prefab serialisation, not at run time where it would be
        /// noticed. <see cref="Awake"/> is where <c>ReticleView</c> builds its own for exactly that
        /// reason, but <see cref="Awake"/> never runs in EditMode (Traps §5), and a pooled body that
        /// is bound from a fixture would then paint through a null. So: here, once, on the first
        /// paint — which is inside <see cref="Bind"/> and therefore never inside a measured step.
        /// </para>
        /// </remarks>
        private MaterialPropertyBlock _properties;

        /// <summary>URP's colour property, resolved once rather than per write.</summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private float _radius;
        private float _duration;
        private float _elapsed;
        private bool _fills;
        private Color _colour = Color.white;
        private bool _live;

        /// <summary>
        /// The pose and size this body was created at, restored on the way back to the pool so a
        /// returned body is indistinguishable from one the pool has never handed out.
        /// </summary>
        /// <remarks>
        /// Captured in <see cref="Awake"/>, which outside play mode never runs (Traps §5) — the
        /// initialisers are what an EditMode fixture gets, and they describe a prefab authored at the
        /// origin at unit scale, which <c>VFX_TelegraphRing.prefab</c> is. At runtime the pool
        /// instantiates at the prefab's own pose and <see cref="Awake"/> has always already read it.
        /// </remarks>
        private Vector3 _restPosition = Vector3.zero;

        /// <inheritdoc cref="_restPosition" />
        private Quaternion _restRotation = Quaternion.identity;

        /// <inheritdoc cref="_restPosition" />
        private Vector3 _restScale = Vector3.one;

        /// <summary>
        /// True while this ring still has time left. <see cref="TelegraphRings"/> returns it when it
        /// does not.
        /// </summary>
        public bool IsLive => _live;

        /// <summary>
        /// The circle this ring stands for, in metres. Zero for a ring standing in for nothing.
        /// </summary>
        public float Radius => _live ? _radius : 0f;

        /// <summary>
        /// How much of the ring exists, in <c>[0, 1]</c> — the countdown, drawn. Always 1 for a
        /// fading ring, which has nothing to count down to, and 0 for a ring in the pool.
        /// </summary>
        public float Fill => _live ? (_fills ? Progress() : 1f) : 0f;

        /// <summary>
        /// How bright the ring is, in <c>[0, 1]</c>. Always 1 for a filling ring — its countdown is
        /// its size, not its brightness — and the fade itself for a blast. Zero in the pool.
        /// </summary>
        public float Alpha => _live ? (_fills ? 1f : 1f - Progress()) : 0f;

        /// <summary>The colour this ring was bound with. GD §16.4's vocabulary, and nothing else.</summary>
        public Color Colour => _colour;

        /// <summary>
        /// Whether this body has the quad it draws with.
        /// </summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a ring whose quad was never dragged into the field runs its whole life
        /// correctly and draws nothing — the feature silently absent rather than broken, which is the
        /// failure mode the scope's other guards exist for. Checked once there rather than per frame
        /// here.
        /// </remarks>
        public bool IsDrawable => _quad != null;

        /// <summary>
        /// Puts this ring into service at <paramref name="centre"/>, <paramref name="radius"/> metres
        /// across, for <paramref name="duration"/> seconds.
        /// </summary>
        /// <param name="centre">The middle of the circle, in world metres.</param>
        /// <param name="radius">
        /// The circle's radius in metres — exactly the one core tested, never a flattering
        /// approximation of it.
        /// </param>
        /// <param name="duration">How long the ring is up for, in seconds.</param>
        /// <param name="fills">
        /// True draws a countdown, a disc growing to the rim; false draws a flash that fades from
        /// full — the difference between "this is coming" and "that happened".
        /// </param>
        /// <param name="colour">
        /// What it is drawn in. Supplied by the census rather than authored here, so one place owns
        /// GD §16.4's palette.
        /// </param>
        /// <remarks>
        /// The centre is not guarded, unlike the two floats, for <see cref="ProjectileView.Bind"/>'s
        /// reason: it comes off an event core already refused a non-finite position at its own door,
        /// and re-checking three components per ring would be guarding the same value twice one layer
        /// further from where it is authored.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="radius"/> or <paramref name="duration"/> is not a finite positive number.
        /// Neither has a meaningful zero: a ring of no size is a promise nobody can see, and a ring
        /// of no length is one nobody can read.
        /// </exception>
        public void Bind(Vector3 centre, float radius, float duration, bool fills, Color colour)
        {
            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN radius
            // would scale the body to nowhere and a NaN duration would make every progress reading
            // NaN — neither throws, and neither is recoverable once it is in.
            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    radius,
                    "A ring's radius must be a finite number greater than zero — it is the circle " +
                    "core tested, so there is no ring without one.");
            }

            if (!(duration > 0f) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "A ring's duration must be a finite number greater than zero; a ring of no " +
                    "length is a telegraph nobody can read.");
            }

            _radius = radius;
            _duration = duration;
            _elapsed = 0f;
            _fills = fills;
            _colour = colour;
            _live = true;

            // Placed and drawn immediately rather than on the first Step, so a ring is never shown
            // for a frame at the size and place of the one before it. Both happen in the same frame,
            // before anything renders.
            transform.SetPositionAndRotation(
                new Vector3(centre.x, centre.y + _groundOffset, centre.z),
                FlatRotation);

            Draw();
        }

        /// <summary>
        /// Advances the ring by <paramref name="dt"/> seconds, and takes it out of service when its
        /// time is up.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing, which is what a frame that did not advance
        /// the simulation should do to a picture of it.
        /// </param>
        /// <remarks>
        /// <b>There is no cancel path and there never will be.</b> A ring goes out of service by
        /// running out, here, and by nothing else: M2-05 rule 7 says a telegraph is a promise the
        /// director cannot withdraw — <em>"a ring the player dodged that produced nothing, or
        /// produced something elsewhere, is worse than no ring at all"</em> — so there is no method
        /// to end one early and no id to end it by. That is the design rule showing up as an absent
        /// method rather than a simplification.
        /// </remarks>
        public void Step(float dt)
        {
            if (!_live)
            {
                return;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the elapsed time would leave
            // this ring drawn nowhere, at no size, for as long as the run lasted, and nothing
            // anywhere would say so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _elapsed += dt;

            if (_elapsed >= _duration)
            {
                // Out of service on the frame it runs out, which is the frame the census returns it.
                // Not drawn again first: the body is about to be deactivated, and a last frame at
                // full fill would be one frame of a ring that has already been kept.
                _live = false;

                return;
            }

            Draw();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="TelegraphRings"/> calls <see cref="Bind"/>
        /// on the very next line with the circle this body is being put into service for, and the
        /// pool has none of it to give. Everything a rental needs undone was undone on the way out —
        /// <see cref="OnDespawn"/> is where the pooling rule lives, so a body is left clean rather
        /// than cleaned on collection.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the pose and size it was created
        /// at, and the ring it was carrying. The scale is on that list and is the one easy to forget,
        /// because it is the only part of a ring's state that lives on the transform rather than in a
        /// field — a body returned still three metres across would be handed to the next spawn
        /// telegraph as a disc that starts full (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, _restRotation);
            transform.localScale = _restScale;

            _radius = 0f;
            _duration = 0f;
            _elapsed = 0f;
            _fills = false;
            _colour = Color.white;
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
        /// Clamped rather than assumed in range: the census returns a ring on the frame it runs out,
        /// but <see cref="Bind"/> is the only thing that resets the clock, so a body stepped twice
        /// before it is collected must read 1 rather than 1.02. The zero-duration branch cannot be
        /// reached through <see cref="Bind"/> and is here so that no arithmetic in this file can
        /// divide by a zero somebody adds later.
        /// </remarks>
        private float Progress() =>
            _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

        /// <summary>
        /// Writes this frame's size and brightness onto the body.
        /// </summary>
        /// <remarks>
        /// The scale <em>is</em> the radius: a quad is one unit across, so a diameter of
        /// <c>2r</c> makes the drawn circle exactly the circle core tested (rule 8). A filling ring
        /// scales with its fill, which is what makes the disc grow to the rim; a fading one is drawn
        /// at full size from its first frame and changes only in brightness.
        /// <para>
        /// Z is left at 1 rather than scaled with the other two. It is the quad's normal after
        /// <see cref="FlatRotation"/> — the ring's thickness through the floor — and scaling it would
        /// mean a large ring floated higher than a small one.
        /// </para>
        /// </remarks>
        private void Draw()
        {
            float diameter = (_fills ? _radius * Progress() : _radius) * 2f;

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

            _properties.SetColor(_baseColorId, new Color(_colour.r, _colour.g, _colour.b, Alpha));

            _quad.SetPropertyBlock(_properties);
        }
    }
}
