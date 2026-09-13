using System;
using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Projectile.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One bolt in flight. It decides nothing: core already knows where and when this lands
    /// (M2-07a rule 2), and everything here is the picture of a decision that has been made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no projectile in core to mirror, only a point and a moment.</b>
    /// <c>ProjectileSystem</c> holds an arrival time and nothing else, so the whole flight arrives in
    /// one <c>ProjectileFired</c> — origin, target, duration — and this interpolates it. Nothing here
    /// is ever reported back: core never lost track of where a shot was, because it never held one.
    /// That is the same bargain <c>EnemyKnockbackIntent</c> makes for a shove.
    /// </para>
    /// <para>
    /// <b>XZ is core's line and the arc is this object's alone.</b> The flight time core computed is
    /// the XZ distance over the speed (AR §18.4), and the hit test it resolves is XZ too — so the
    /// hump on Y cannot change an outcome, which is exactly what makes it safe to live on this side
    /// of the boundary. It is also what GD §8.1 means by a <em>slow arcing projectile</em>: the arc
    /// is how the shot reads as incoming, not how it is decided.
    /// </para>
    /// <para>
    /// <b>It owns no <see cref="MonoBehaviour"/> frame loop.</b> <c>RunTicker</c> calls
    /// <see cref="Step"/> with the snapshot's clamped <c>Dt</c>, the same step core timed the flight
    /// with. An <c>Update</c> here would run on the wall clock, and on a hitching frame — where the
    /// clamp shortens core's step but not the wall's — the picture of the bolt would land before the
    /// damage it stands for (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has the same job <c>EnemyView.OnDespawn</c> has.</b>
    /// The same body stands in for one shot, then another, then a third; anything a flight leaves
    /// behind — where it stopped, how far along it was, which way it was tipped — is inherited by the
    /// next shot unless it is undone. Less to forget than an enemy has, and forgotten in the same
    /// place, for the same reason (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class ProjectileView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The id of a view standing in for nothing. Zero, because <c>ProjectileSystem</c> issues
        /// ids from 1 and never reuses one within a run, so no shot in the air can collide with it.
        /// </summary>
        public const int Unbound = 0;

        [Tooltip("How high the bolt humps above the straight line between its endpoints, in " +
                 "metres, at the midpoint of the flight. Cosmetic in the strict sense: core's hit " +
                 "test is XZ, so no value here can change whether a shot lands.")]
        [Min(0f)]
        [SerializeField] private float _arcHeight = 1.2f;

        private int _id = Unbound;
        private Vector3 _origin;
        private Vector3 _target;
        private float _flightTime;
        private float _elapsed;

        /// <summary>
        /// The pose this body was created at, restored on the way back to the pool so a returned
        /// body is indistinguishable from one the pool has never handed out.
        /// </summary>
        /// <remarks>
        /// Captured in <see cref="Awake"/>, which outside play mode never runs (Traps §5) — the
        /// initialisers are what an EditMode fixture gets, and they describe a prefab authored at
        /// the origin, which <c>Projectile.prefab</c> is. At runtime the pool instantiates at the
        /// prefab's own pose and <c>Awake</c> has always already read it.
        /// </remarks>
        private Vector3 _restPosition = Vector3.zero;

        /// <inheritdoc cref="_restPosition" />
        private Quaternion _restRotation = Quaternion.identity;

        /// <summary>The core-side id this bolt stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether this view is currently standing in for a shot in the air.</summary>
        public bool IsBound => _id != Unbound;

        /// <summary>
        /// Puts this body into service flying <paramref name="origin"/> → <paramref name="target"/>
        /// over <paramref name="flightTime"/> seconds, and places it at the origin.
        /// </summary>
        /// <param name="id">The shot's id, as issued by <c>ProjectileSystem</c> from 1.</param>
        /// <param name="origin">Where it left from, in world metres.</param>
        /// <param name="target">Where it arrives, in world metres.</param>
        /// <param name="flightTime">
        /// How long the whole flight takes, in seconds. Zero is legal and means a shot that has
        /// already arrived (M2-07a rule 3) — see <see cref="Step"/>, which clamps rather than
        /// divides.
        /// </param>
        /// <remarks>
        /// The two positions are not guarded here, unlike the two floats: they come off a
        /// <c>ProjectileFired</c>, and <c>Projectile</c>'s constructor already refuses a non-finite
        /// origin or target at core's own door. Re-checking six components per shot would be
        /// guarding the same value twice, one layer further from where it is authored.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="id"/> is not positive, or <paramref name="flightTime"/> is not a finite
        /// number of at least zero.
        /// </exception>
        public void Bind(int id, Vector3 origin, Vector3 target, float flightTime)
        {
            if (id <= Unbound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "A projectile id must be positive; ProjectileSystem issues them from 1.");
            }

            // Negated positives, so NaN is refused rather than admitted (AR §18.3). A NaN flight
            // time would make every progress reading NaN, which no clamp recovers from and no
            // later comparison reports.
            if (!(flightTime >= 0f) || float.IsInfinity(flightTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(flightTime),
                    flightTime,
                    "flightTime must be a finite number of at least zero. Zero is legal and means " +
                    "a shot that has already arrived.");
            }

            _id = id;
            _origin = origin;
            _target = target;
            _flightTime = flightTime;
            _elapsed = 0f;

            // Placed immediately rather than on the first Step, so the bolt is never drawn for a
            // frame at wherever the previous shot ended. The two calls happen in the same frame,
            // before anything renders.
            transform.position = PositionAt(Progress());
        }

        /// <summary>
        /// Takes this body out of service. Idempotent.
        /// </summary>
        /// <remarks>
        /// Private, unlike <c>EnemyView.Unbind</c>: the only moment a bolt leaves service is the
        /// return to the pool, so the one caller is <see cref="OnDespawn"/> a few lines below. The
        /// spec's Public API block does not list it either.
        /// </remarks>
        private void Unbind()
        {
            _id = Unbound;
            _origin = Vector3.zero;
            _target = Vector3.zero;
            _flightTime = 0f;
            _elapsed = 0f;
        }

        /// <summary>
        /// Advances the flight by <paramref name="dt"/> seconds.
        /// </summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A
        /// zero, a negative or a non-finite step does nothing, which is what a frame that did not
        /// advance the simulation should do to a picture of it.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>It never overruns.</b> Once the progress reaches 1 the body sits exactly on the target
        /// point until <c>ProjectileImpacted</c> returns it, which is at most one frame later. A
        /// view that kept flying would draw a bolt sailing past something core says it hit.
        /// </para>
        /// <para>
        /// <b>The body faces along its own travel, recomputed each step</b>, so an arcing bolt tips
        /// over instead of pointing at the sky for the whole flight. A step that moved it nowhere —
        /// a shot already arrived, a flight of no length — leaves the rotation alone, which is
        /// <c>EnemyView.Face</c>'s rule and stops <c>LookRotation</c> complaining about a zero
        /// vector.
        /// </para>
        /// </remarks>
        public void Step(float dt)
        {
            if (_id == Unbound)
            {
                return;
            }

            // Negated, so NaN is refused with the rest. A NaN added to the elapsed time would make
            // every subsequent progress reading NaN, and the bolt would be drawn nowhere for the
            // rest of its life without anything saying so.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _elapsed += dt;

            Vector3 previous = transform.position;
            Vector3 next = PositionAt(Progress());

            transform.position = next;

            Face(next - previous);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <c>ProjectileViews</c> calls <see cref="Bind"/> on
        /// the very next line with the flight this body is being put into service for, and the pool
        /// has none of it to give. Everything a rental needs undone was undone on the way out —
        /// <see cref="OnDespawn"/> is where the pooling rule lives, so a body is left clean rather
        /// than cleaned on collection.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the pose it was created at, and
        /// the flight it was carrying. Shorter than <c>EnemyView.OnDespawn</c>'s list because a bolt
        /// has less to remember — but it is the same rule and the same failure mode, which is
        /// silence. A body returned still holding its elapsed time would arrive at its next flight
        /// part-way through it, and read as a bolt that teleported.
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, _restRotation);

            Unbind();
        }

        private void Awake()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
        }

        /// <summary>
        /// How far along the flight is, in <c>[0, 1]</c>.
        /// </summary>
        /// <remarks>
        /// A zero flight time answers 1 rather than dividing (rule 7): the shot was fired from the
        /// point it was aimed at, so it has already arrived and is usually released on the same
        /// frame it was rented. Not an error, and no branch in core.
        /// </remarks>
        private float Progress() =>
            _flightTime > 0f ? Mathf.Clamp01(_elapsed / _flightTime) : 1f;

        /// <summary>
        /// Where the bolt is at <paramref name="t"/>: core's straight line, plus a hump this object
        /// owns.
        /// </summary>
        /// <remarks>
        /// <c>4h·t(1−t)</c> is the parabola through zero at both ends and <c>h</c> at the midpoint,
        /// added on top of the endpoints' own interpolation — so a shot fired downhill still lands
        /// exactly on the target's height. XZ is untouched by it, which is what keeps the picture
        /// and core's XZ hit test the same shot.
        /// </remarks>
        private Vector3 PositionAt(float t)
        {
            Vector3 point = Vector3.Lerp(_origin, _target, t);

            point.y += _arcHeight * 4f * t * (1f - t);

            return point;
        }

        /// <summary>
        /// Turns the body to look along <paramref name="travel"/>, or leaves it alone when it
        /// travelled nowhere.
        /// </summary>
        /// <remarks>
        /// <c>sqrMagnitude &gt; 0f</c> is false for NaN as well as for zero, so an unreadable step
        /// leaves the rotation alone instead of producing one no later comparison can be true about.
        /// </remarks>
        private void Face(Vector3 travel)
        {
            if (travel.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(travel, Vector3.up);
            }
        }
    }
}
