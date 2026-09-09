using System;
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
    /// snapshot rather than acted on here. No health and no state machine — a Husk's hit points
    /// live on core's <c>EnemyAgent</c>, and the day it walks (M1-18) the direction will arrive as
    /// an intent.
    /// </para>
    /// <para>
    /// The one timer on it is <see cref="Knockback"/>'s, and it decides nothing: core decided the
    /// direction and the distance and sent them as an <c>EnemyKnockbackIntent</c> (M1-15), so all
    /// that is owned here is the tenth of a second it takes to look like an impact rather than a
    /// teleport.
    /// </para>
    /// <para>
    /// <b>An unbound view is not a live enemy.</b> <see cref="EnemyViews"/> creates one per
    /// <c>EnemySpawned</c> and destroys it on <c>EnemyDespawned</c> today, so <see cref="Bind"/>
    /// and <see cref="Unbind"/> look redundant — they are the seam M1-19 needs, where the same
    /// object is handed back to a pool and re-bound to a different id, and building it now costs
    /// two methods instead of a rewrite.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class EnemyView : MonoBehaviour
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

        private CapsuleCollider _body;
        private int _id = Unbound;
        private Vector3 _velocity;

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
        /// The horizontal velocity last applied — a shove's slide today, and the chaser's own
        /// walking from M1-18.
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

            CancelKnockback();
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
        /// <b>A transform move, not a controller one.</b> The dummies of M1 have a collider and no
        /// <c>CharacterController</c>, so there is nothing here to resolve a collision with — a
        /// shoved dummy will slide through a wall, and it is meant to, until M1-18 gives these
        /// bodies something to walk with. The single line this becomes then is the reason it is
        /// written as a target position rather than as a per-frame delta.
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

            if (t >= 1f)
            {
                transform.position = _knockbackTo;

                CancelKnockback();

                return;
            }

            Vector3 previous = transform.position;

            transform.position = Vector3.Lerp(_knockbackFrom, _knockbackTo, t);

            // Measured rather than derived from the lerp, so it stays right on the frame the slide
            // is clipped by its own end. Zero dt leaves the previous reading rather than dividing
            // by nothing.
            if (dt > 0f)
            {
                _velocity = (transform.position - previous) / dt;
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
