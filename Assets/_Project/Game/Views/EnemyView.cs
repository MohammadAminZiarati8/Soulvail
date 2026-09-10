using System;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Enemy.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One enemy's body. Core decided that an enemy exists and where it started; this is the
    /// object standing there, and it decides nothing at all. See AR §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The enemy counterpart of <see cref="PlayerView"/> and just as dumb: it holds an id, a
    /// transform and a velocity, and every one of those is reported back to core through the
    /// snapshot rather than acted on here. No health and no state machine — a Husk's hit points live
    /// on core's <c>EnemyAgent</c> and its state machine is <c>ChaserBehaviour</c>. Which way it
    /// walks and which way it looks arrive every tick in an <see cref="EnemyMoveIntent"/> (M1-18),
    /// and this applies them without asking why.
    /// </para>
    /// <para>
    /// The one timer on it is <see cref="Knockback"/>'s, and it decides nothing: core decided the
    /// direction and the distance and sent them as an <c>EnemyKnockbackIntent</c> (M1-15), so all
    /// that is owned here is the tenth of a second it takes to look like an impact rather than a
    /// teleport.
    /// </para>
    /// <para>
    /// <b>Two colliders, and each answers a different question.</b> The
    /// <see cref="CharacterController"/> is what moves — it is what makes a Husk bump into walls,
    /// pillars, the player and other Husks instead of walking through them — while the trigger
    /// <see cref="CapsuleCollider"/> stays exactly as M1-07 authored it, because that is the shape
    /// M1-12's cone sweep and M1-15's dash sweep query against and <see cref="EnemyViews"/> indexes
    /// by. A controller's own collider is not a <see cref="CapsuleCollider"/> and is not in that
    /// index, so it is invisible to both sweeps and cannot double-report a hit.
    /// </para>
    /// <para>
    /// <b>An unbound view is not a live enemy.</b> <see cref="EnemyViews"/> rents one from a
    /// <see cref="ViewPool{T}"/> per <c>EnemySpawned</c> and returns it on <c>EnemyDespawned</c>,
    /// so the same object stands in for a Husk, then a corpse, then a different Husk with a
    /// different id. <see cref="Bind"/> and <see cref="Unbind"/> are that seam — built in M1-07
    /// against the day it would be needed, and used for real as of M1-19.
    /// </para>
    /// <para>
    /// <b>Which is why <see cref="OnDespawn"/> exists and is the most dangerous method here.</b>
    /// Everything a life leaves behind — the dissolve's stretch and fade, the collider it switched
    /// off, a shove still in flight — is inherited by the next enemy to be handed this body unless
    /// it is undone. A reset that is forgotten does not fail: it produces a Husk that spawns
    /// half-transparent, stretched, and unhittable, which reads as a rendering bug rather than as a
    /// pooling one.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class EnemyView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The id of a view that is not standing in for anything. Zero, because
        /// <c>EnemyRegistry</c> hands out ids from 1 and never reuses one within a run, so no
        /// live enemy can ever collide with it.
        /// </summary>
        public const int Unbound = 0;

        /// <summary>
        /// How long a shove takes to play out, in seconds. Short enough to read as an impact rather
        /// than as the enemy walking backwards, long enough that 5 m is a slide instead of a
        /// teleport.
        /// </summary>
        private const float KnockbackSeconds = 0.15f;

        /// <summary>
        /// Downward acceleration while airborne, in m/s². The same number
        /// <see cref="PlayerView"/> uses and for the same reason: it is not a gameplay decision,
        /// only what keeps a <see cref="CharacterController"/> in contact with the floor. Nothing in
        /// core can observe it — the position comes back through the snapshot either way.
        /// </summary>
        private const float Gravity = -20f;

        /// <summary>
        /// The downward speed held while grounded, matching <see cref="PlayerView"/>: a controller
        /// pushed with no vertical component drifts off the ground for a frame at a time and
        /// <c>isGrounded</c> flickers.
        /// </summary>
        private const float GroundedFallSpeed = -1f;

        private CapsuleCollider _body;
        private CharacterController _controller;
        private EnemyHitFeedback _feedback;
        private bool _searchedForFeedback;
        private int _id = Unbound;
        private Vector3 _velocity;
        private float _fallSpeed;

        private Vector3 _knockbackFrom;
        private Vector3 _knockbackTo;
        private float _knockbackElapsed;
        private bool _isKnockedBack;

        /// <summary>The core-side id this body stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether this view is currently standing in for a live enemy.</summary>
        public bool IsBound => _id != Unbound;

        /// <summary>Where the body is, for the snapshot to report back to core.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// The horizontal velocity last applied — the chaser's own walking, or a shove's slide.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Present since M1-07, and reported into the snapshot since then, for the reason
        /// <c>EnemySense</c> carries the field at all: a zero that is written every frame is a
        /// fact, while a field nobody fills is a slot that keeps whatever the previous occupant of
        /// the slot left in it. It is what core asked for rather than what collision achieved,
        /// exactly as <see cref="PlayerView.Velocity"/> is, and for the same reason.
        /// </para>
        /// <para>
        /// <b>A knockback writes it, and that is not a widening of the rule.</b> A shove is core's
        /// decision arriving as an <c>EnemyKnockbackIntent</c>, so the slide is exactly "what core
        /// asked for". Leaving it at zero would have the snapshot report a body standing still
        /// while its position moved 5 m — two senses about the same enemy that contradict each
        /// other, which is the one thing a sense may not do.
        /// </para>
        /// </remarks>
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// This body's trigger collider, cached once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Exposed so <see cref="EnemyViews"/> can index it and answer "which enemy is this
        /// collider?" without a <c>GetComponent</c> inside M1-12's cone-overlap loop, which runs
        /// once per swing against every collider in the sweep.
        /// </para>
        /// <para>
        /// Resolved lazily when <see cref="Awake"/> has not run, which outside play mode it never
        /// does. That is not a test convenience: it is the only reason an EditMode test can build a
        /// body whose collider physics can actually find, and without it M1-12's overlap query
        /// would have no automated coverage at all — the index would be empty and every query would
        /// correctly return nothing, which looks exactly like a passing test of a broken feature.
        /// At runtime <c>Awake</c> has always already filled it, so the branch is never taken on a
        /// path that matters.
        /// </para>
        /// </remarks>
        public Collider Body
        {
            get
            {
                // Unity's == rather than `is null`: a destroyed collider is a live reference that
                // only compares equal to null through the engine's operator.
                if (_body == null)
                {
                    _body = GetComponent<CapsuleCollider>();
                }

                return _body;
            }
        }

        /// <summary>
        /// This body's character controller, cached once.
        /// </summary>
        /// <remarks>
        /// Resolved lazily when <see cref="Awake"/> has not run, exactly as <see cref="Body"/> is
        /// and for the same reason: outside play mode <c>Awake</c> never runs on an instantiated
        /// prefab, and an EditMode test that builds one must still get a whole object rather than a
        /// half-null one. Nothing calls the movement paths from a test, so at runtime this branch is
        /// never taken.
        /// </remarks>
        private CharacterController Controller
        {
            get
            {
                // Unity's == rather than `is null`: a destroyed component is a live reference that
                // only compares equal to null through the engine's operator.
                if (_controller == null)
                {
                    _controller = GetComponent<CharacterController>();
                }

                return _controller;
            }
        }

        /// <summary>
        /// The flash-and-dissolve on this body, or null on a body that has none.
        /// </summary>
        /// <remarks>
        /// Guarded by a flag rather than by a null check, unlike <see cref="Body"/> and
        /// <see cref="Controller"/>, because here null is a legitimate answer: the component is not
        /// <c>[RequireComponent]</c>ed, and an EditMode fixture builds bodies without one. Without
        /// the flag every despawn of such a body would search its components again for something
        /// that was never there.
        /// </remarks>
        private EnemyHitFeedback Feedback
        {
            get
            {
                if (!_searchedForFeedback)
                {
                    _searchedForFeedback = true;
                    _feedback = GetComponent<EnemyHitFeedback>();
                }

                return _feedback;
            }
        }

        /// <summary>
        /// Applies one tick's intent: walk at this velocity, look this way.
        /// </summary>
        /// <param name="intent">What core decided for this enemy this tick.</param>
        /// <param name="dt">
        /// The same step core integrated with — <c>snapshot.Dt</c>, clamped by
        /// <c>SnapshotBuilder.MaxDt</c>, never <c>Time.deltaTime</c>. The rule
        /// <see cref="PlayerView.Apply"/> states: brain and body disagreeing about how much time
        /// passed is how a hitch becomes a teleport.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>A shove outranks a walk, and that is the only priority rule in this class.</b> While a
        /// <see cref="Knockback"/> is playing this returns without moving anything, so the body is
        /// never pushed by two things in one frame — which would resolve collision twice and let a
        /// Husk walk out of the knockback that was meant to punish it. Core is not told and does not
        /// need to be: where the enemy ended up comes back in the next snapshot, which is the whole
        /// contract for positions (AR §4.2).
        /// </para>
        /// <para>
        /// The id on the intent is deliberately not checked against <see cref="Id"/>. Routing an
        /// intent to the right body is <c>RunTicker</c>'s job and it does it by looking this object
        /// up by that same id, so a check here could only ever be true.
        /// </para>
        /// </remarks>
        public void Apply(in EnemyMoveIntent intent, float dt)
        {
            if (_isKnockedBack)
            {
                return;
            }

            Vector3 horizontal = intent.Velocity.ToUnity();

            MoveBody(horizontal, dt);

            _velocity = horizontal;

            Face(intent.FacingXZ);
        }

        /// <summary>
        /// Puts this body into service as <paramref name="id"/>, standing at
        /// <paramref name="position"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="id"/> is not positive. <c>EnemyRegistry</c> issues ids from 1, so a
        /// zero or negative one means the caller invented it — and a view bound to
        /// <see cref="Unbound"/> would report itself into the snapshot under an id core can never
        /// resolve, which core ignores in silence.
        /// </exception>
        public void Bind(int id, Vector3 position)
        {
            if (id <= Unbound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "An enemy id must be positive; EnemyRegistry issues them from 1.");
            }

            _id = id;
            _velocity = Vector3.zero;

            // Back to rest, so a body pooled while it was falling (M1-19) does not arrive at its
            // next spawn still carrying the previous life's downward speed.
            _fallSpeed = 0f;

            transform.position = position;

            // Cleared here as well as on the way out, because M1-19 hands the same object back to a
            // pool: a body rented out while a shove was still playing would otherwise spend its
            // first 0.15 s sliding towards where the *previous* enemy had been pushed.
            CancelKnockback();
        }

        /// <summary>Takes this body out of service. Idempotent.</summary>
        public void Unbind()
        {
            _id = Unbound;
            _velocity = Vector3.zero;
            _fallSpeed = 0f;

            CancelKnockback();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="EnemyViews"/> calls <see cref="Bind"/> on
        /// the very next line with the id and the position this body is being put into service
        /// with, and the pool has neither to give. Everything a rental needs undone was undone on
        /// the way out — see <see cref="OnDespawn"/>, which is where the pooling rule lives so that
        /// a body is left clean rather than cleaned on collection.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// The whole of "put this body back the way you found it", in the order it has to happen.
        /// The visuals go first because the dissolve owns the scale and the material and would keep
        /// writing both from its own <c>Update</c>; the collider is switched back on because a death
        /// switched it off; and the unbind is last, since it is what makes the body stop reporting
        /// itself into the snapshot.
        /// </para>
        /// <para>
        /// <b>It reaches into <see cref="EnemyHitFeedback"/>, and that is the one direction this
        /// class ever points outward.</b> The alternative — the census resetting each component in
        /// turn — spreads "what a rental has to forget" across two files, and the file that would
        /// not have it is the one holding the pooled object. Anything else added to the prefab that
        /// remembers something joins the list here.
        /// </para>
        /// </remarks>
        public void OnDespawn()
        {
            EnemyHitFeedback feedback = Feedback;

            if (feedback != null)
            {
                feedback.ResetVisuals();
            }

            // Back into the physics query. A death took it out (EnemyHitFeedback.OnDied) so that a
            // swing in the same frame could not spend a hit on a corpse; leaving it out would make
            // the next enemy to be handed this body permanently unhittable — a bug that looks like
            // damage not registering, three systems away from its cause.
            Collider body = Body;

            if (body != null)
            {
                body.enabled = true;
            }

            Unbind();
        }

        /// <summary>
        /// Slides this body <paramref name="distance"/> metres along <paramref name="directionXZ"/>
        /// over <see cref="KnockbackSeconds"/>. What an <c>EnemyKnockbackIntent</c> looks like.
        /// </summary>
        /// <param name="directionXZ">
        /// Where the shove points, on the ground plane. Normalised here, so an intent that arrives
        /// unnormalised still moves the authored distance rather than a multiple of it.
        /// </param>
        /// <param name="distance">
        /// How far to slide, in metres. Zero or less is a no-op and leaves any shove already
        /// playing alone — a zero-knockback movement skill (<c>MovementSkillSpec.Knockback</c> may
        /// legally be zero) should not cancel a shove that came from somewhere else.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Written as a target position, walked to through the controller.</b> M1-15 left this as
        /// a straight transform move because the dummies had nothing to collide with; now they do,
        /// so each frame's step is the gap between where the slide should have reached and where the
        /// body actually is, pushed through <see cref="CharacterController.Move"/>. A shove into a
        /// wall therefore stops at the wall and keeps leaning on it for the rest of the 0.15 s,
        /// which is what "pushed 5 m" has always meant (see <c>EnemyKnockbackIntent</c>) — and it is
        /// why the target is still stored as a position rather than as a per-frame delta.
        /// </para>
        /// <para>
        /// <b>A second shove restarts from where the body is now</b> rather than compounding onto
        /// the first. Two Charges through the same enemy inside 0.15 s should move it 5 m from
        /// wherever it got to, not 10 m from where it started — and the alternative reads as an
        /// enemy fired out of the arena.
        /// </para>
        /// </remarks>
        public void Knockback(Vector2 directionXZ, float distance)
        {
            // Negated positives throughout: every comparison against NaN is false, so the natural
            // spelling would let an unreadable intent send the body to a position no later distance
            // test could ever be true about.
            if (!(distance > 0f))
            {
                return;
            }

            float lengthSq = (directionXZ.x * directionXZ.x) + (directionXZ.y * directionXZ.y);

            if (!(lengthSq > 0f) || float.IsInfinity(lengthSq))
            {
                return;
            }

            float scale = distance / Mathf.Sqrt(lengthSq);

            _knockbackFrom = transform.position;
            _knockbackTo = _knockbackFrom + new Vector3(directionXZ.x * scale, 0f, directionXZ.y * scale);
            _knockbackElapsed = 0f;
            _isKnockedBack = true;
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path — and M1-12's overlap
            // query is worse than per-frame, it is per-collider-per-swing.
            _body = GetComponent<CapsuleCollider>();
            _controller = GetComponent<CharacterController>();
        }

        /// <remarks>
        /// <para>
        /// The one frame loop on this component, and it returns on the first line for all but a
        /// tenth of a second in every arena's life.
        /// </para>
        /// <para>
        /// <c>Time.deltaTime</c> rather than the snapshot's clamped <c>Dt</c>, unlike every gameplay
        /// path in the project. This is presentation: nothing core decides depends on how far along
        /// the slide is, only on where the body ends up, and it ends up in the same place either
        /// way. Using the simulation's step would mean plumbing it into every enemy body to make a
        /// cosmetic lerp agree with a clock nothing is reading.
        /// </para>
        /// </remarks>
        private void Update()
        {
            if (!_isKnockedBack)
            {
                return;
            }

            float dt = Time.deltaTime;

            _knockbackElapsed += dt;

            float t = _knockbackElapsed / KnockbackSeconds;
            bool finished = t >= 1f;

            Vector3 target = finished ? _knockbackTo : Vector3.Lerp(_knockbackFrom, _knockbackTo, t);

            // The gap between where the slide should have reached and where the body actually is,
            // which is what makes a blocked shove keep pushing instead of silently teleporting past
            // the wall on the last frame. Flattened, because the lerp runs along the ground and the
            // vertical component belongs to gravity alone.
            Vector3 delta = target - transform.position;
            delta.y = 0f;

            // Zero dt would divide by nothing; the frame is skipped rather than guessed at.
            Vector3 horizontal = dt > 0f ? delta / dt : Vector3.zero;

            MoveBody(horizontal, dt);

            _velocity = horizontal;

            if (finished)
            {
                CancelKnockback();
            }
        }

        /// <summary>
        /// Moves the body by <paramref name="horizontal"/> for <paramref name="dt"/> seconds, with
        /// gravity folded in. The one place this object touches the controller.
        /// </summary>
        /// <remarks>
        /// One <c>Move</c> per frame with both components together, exactly as
        /// <see cref="PlayerView.Apply"/> does it: two calls would resolve collision twice and let a
        /// horizontal push climb what a vertical one had just settled onto.
        /// </remarks>
        private void MoveBody(Vector3 horizontal, float dt)
        {
            CharacterController controller = Controller;

            _fallSpeed = controller.isGrounded ? GroundedFallSpeed : _fallSpeed + (Gravity * dt);

            controller.Move((horizontal + new Vector3(0f, _fallSpeed, 0f)) * dt);
        }

        /// <summary>
        /// Turns the body to look along <paramref name="facingXZ"/>, or leaves it alone when core
        /// has no opinion.
        /// </summary>
        /// <remarks>
        /// A zero facing is core saying "keep the rotation you have" — an idle enemy, or one
        /// standing exactly where the player is — and is the normal reason this does nothing. It is
        /// also what stops <c>LookRotation</c> logging an error over a zero vector. No smoothing,
        /// for the reason <see cref="PlayerView"/> gives: any easing here would be a second rotation
        /// curve on top of whatever core decided.
        /// </remarks>
        private void Face(System.Numerics.Vector2 facingXZ)
        {
            var facing = new Vector3(facingXZ.X, 0f, facingXZ.Y);

            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
        }

        /// <summary>Stops any shove in flight, leaving the body wherever it got to.</summary>
        private void CancelKnockback()
        {
            _isKnockedBack = false;
            _knockbackElapsed = 0f;
            _velocity = Vector3.zero;
        }
    }
}
