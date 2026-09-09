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
    /// snapshot rather than acted on here. No health, no state machine, no timers — a Husk's hit
    /// points live on core's <c>EnemyAgent</c>, and the day it moves (M1-18) the direction will
    /// arrive as an intent.
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

        private CapsuleCollider _body;
        private int _id = Unbound;
        private Vector3 _velocity;

        /// <summary>The core-side id this body stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether this view is currently standing in for a live enemy.</summary>
        public bool IsBound => _id != Unbound;

        /// <summary>Where the body is, for the snapshot to report back to core.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// The horizontal velocity last applied — zero until M1-18 gives the chaser something to
        /// apply.
        /// </summary>
        /// <remarks>
        /// Present now, and reported into the snapshot now, for the reason <c>EnemySense</c>
        /// carries the field at all: a zero that is written every frame is a fact, while a field
        /// nobody fills is a slot that keeps whatever the previous occupant of the slot left in
        /// it. It will be what core asked for rather than what collision achieved, exactly as
        /// <see cref="PlayerView.Velocity"/> is, and for the same reason.
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
        }

        /// <summary>Takes this body out of service. Idempotent.</summary>
        public void Unbind()
        {
            _id = Unbound;
            _velocity = Vector3.zero;
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path — and M1-12's overlap
            // query is worse than per-frame, it is per-collider-per-swing.
            _body = GetComponent<CapsuleCollider>();
        }
    }
}
