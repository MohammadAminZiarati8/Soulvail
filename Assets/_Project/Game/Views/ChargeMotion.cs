using System;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Player.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The dash, as a body. Core decided that a Charge happens, which way and how far; this walks
    /// the capsule along that line and reports who it passed through. It decides nothing at all.
    /// CC §5, and the second half of M1-15.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of <see cref="PlayerView"/>, and deliberately not part of it.</b> A dash
    /// is not a velocity core integrated — it is a fixed distance over a fixed time, decided once
    /// and then merely executed, with the motor suspended behind it (M1-15). Folding it into
    /// <see cref="PlayerView.Apply"/> would put two unrelated ways of moving the same controller in
    /// one method, and the swept overlap this owns has nothing to do with either.
    /// </para>
    /// <para>
    /// <b>It is stepped by <c>RunTicker</c> and has no <c>Update</c> of its own.</b> That is the
    /// whole reason it is written this way. The sweep answers with <c>ReportChargeHits</c>, which
    /// makes core write <c>EnemyKnockbackIntent</c>s that something else has to read in the same
    /// frame — so "when the sweep runs" and "when the knockbacks are applied" have to be two
    /// adjacent lines in the file that owns the frame, not two <c>Update</c> calls whose order is
    /// whatever Unity happens to pick. <c>IntentBuffer.Knockbacks</c> says the same thing from the
    /// other end.
    /// </para>
    /// <para>
    /// <b>It reports a fact; it never decides a consequence.</b> Which enemies the capsule passed
    /// through is exactly the kind of thing core cannot know and must be told (ADR-0003). What that
    /// costs them — 20 damage and a 5 m shove, once per enemy per dash — is decided inside
    /// <c>ResolveChargeHits</c> a moment later. Nothing here reads health, and nothing here dedupes
    /// across frames: core does, because "per dash" is a fact about the dash rather than about any
    /// one frame of it.
    /// </para>
    /// <para>
    /// <b>Allocation-free by construction.</b> Both buffers are filled once and reused for the life
    /// of the run, the overlap is the non-allocating overload, and the ids go back through a
    /// <see cref="ReadOnlySpan{T}"/> over an array this object has owned since it woke up (AR §14).
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChargeMotion : MonoBehaviour
    {
        /// <summary>
        /// Colliders one frame's sweep can see. A dash covers about 0.75 m per frame at 60 fps, so
        /// this is far past anything a single step can genuinely overlap; it is sized like
        /// <c>ConeOverlapQuery.DefaultCapacity</c> so the two answers to "who is in this volume"
        /// cannot silently differ in how much they can see.
        /// </summary>
        private const int SweepCapacity = 32;

        /// <summary>
        /// The downward speed held while dashing, in m/s. The same pin
        /// <see cref="PlayerView"/> keeps for the same reason — <c>CharacterController.isGrounded</c>
        /// reports on the <em>last</em> <c>Move</c>, and a purely horizontal push drifts the
        /// controller off the floor a frame at a time.
        /// </summary>
        /// <remarks>
        /// A pin, not gravity. There is no airborne state in this game and a dash lasts 0.22 s, so
        /// integrating a fall through it would be a second acceleration curve nobody authored, for
        /// a fifth of a second, on the one move the player is asked to commit to.
        /// </remarks>
        private const float GroundedFallSpeed = -1f;

        private readonly Collider[] _colliders = new Collider[SweepCapacity];
        private readonly int[] _ids = new int[SweepCapacity];

        private CharacterController _controller;
        private EnemyViews _views;
        private LayerMask _enemyLayer;
        private bool _injected;

        /// <summary>The run, for reporting the sweep. Held only while a dash is in flight.</summary>
        private IRunSession _session;

        /// <summary>Metres per second along <see cref="_direction"/>: the intent's distance over its duration.</summary>
        private float _speed;

        /// <summary>Seconds of dash left to walk. Zero at rest, and what <see cref="IsActive"/> reads.</summary>
        private float _remaining;

        private Vector3 _direction;

        private bool _warnedAboutCapacity;

        /// <summary>Whether a dash is being walked right now.</summary>
        /// <remarks>
        /// <para>
        /// <b>This is the <em>movement</em> ending, not the i-frames ending.</b> The two are 0.05 s
        /// apart (CC §5) and this is the earlier one: it goes false when the capsule stops, while
        /// core keeps ignoring damage for a further trail and announces <em>that</em> with
        /// <c>ChargeEnded</c>. Anything asking "may the player be hurt?" wants the event, not this;
        /// anything asking "who is driving the controller?" wants this.
        /// </para>
        /// <para>
        /// It tracks core's own <c>ChargeSkill.IsActive</c> tick for tick without being told to,
        /// because both count the same clamped <c>Dt</c> from the same frame — see
        /// <see cref="Step"/>.
        /// </para>
        /// </remarks>
        public bool IsActive => _remaining > 0f;

        /// <summary>
        /// Takes the dash core just decided on and starts walking it.
        /// </summary>
        /// <param name="intent">The dash core wrote this tick: direction, distance, duration.</param>
        /// <param name="session">The run, for the sweep to report into until the dash ends.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// The facing is set here and never touched again for the length of the dash. That is CC
        /// §5's commitment made visible: the direction was fixed when the dash began, so a capsule
        /// that kept turning would be showing a steering the player does not have. Nothing else is
        /// writing the rotation meanwhile — core suspends the motor, so no <c>PlayerMoveIntent</c>
        /// arrives to rotate it.
        /// </para>
        /// <para>
        /// A malformed intent is refused rather than walked. A zero-length direction would leave
        /// <see cref="Quaternion.LookRotation"/> logging an error and the capsule standing still for
        /// the duration while core believed it was dashing; a non-positive duration would divide the
        /// distance by nothing. Core produces neither — <c>ChargeSkill</c> normalises the direction
        /// and <c>MovementSkillSpec</c> guards the duration — which is what makes this a check that
        /// should never fire rather than a case the game reaches.
        /// </para>
        /// </remarks>
        public void Begin(in ChargeIntent intent, IRunSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            Vector3 direction = new Vector3(intent.DirectionXZ.X, 0f, intent.DirectionXZ.Y);

            // Negated positives, for the reason MovementSpec documents: every comparison against
            // NaN is false, so the natural spelling would let an unreadable intent start a dash
            // that moved the capsule to nowhere and left every later distance test against its
            // position false.
            if (!(direction.sqrMagnitude > 0f) || !(intent.Duration > 0f))
            {
                _remaining = 0f;

                return;
            }

            _direction = direction.normalized;
            _speed = intent.Distance / intent.Duration;
            _remaining = intent.Duration;

            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        }

        /// <summary>
        /// Walks one frame of the dash and reports whoever the capsule just passed through. A no-op
        /// when no dash is in flight, which is almost every frame.
        /// </summary>
        /// <param name="dt">
        /// The same step core integrated with — <c>snapshot.Dt</c>, clamped by
        /// <see cref="SnapshotBuilder.MaxDt"/>, never <c>Time.deltaTime</c>. This is what keeps the
        /// two clocks together: core's <c>ChargeSkill</c> ends the dash off <c>RunState.Time</c>,
        /// which is the sum of exactly these steps, so the capsule stops on the same tick core
        /// stops believing it is dashing. Handing it a different <c>dt</c> would make the dash
        /// travel a distance nobody authored and end on a frame nobody agreed on.
        /// </param>
        /// <remarks>
        /// <para>
        /// The last step is clipped to what is left, so a dash covers its authored distance exactly
        /// rather than one frame's overshoot more. At 45 m/s an unclipped final frame would be up
        /// to 0.75 m of extra travel — a third of a capsule, on the move whose whole promise is
        /// that you know where you will end up.
        /// </para>
        /// <para>
        /// A wall stops the motion by itself: <c>CharacterController.Move</c> resolves the
        /// collision, the capsule ends up short, and the timer keeps running down to zero. That is
        /// deliberate — there is no "stuck charging" state to get into, and the cooldown core
        /// started is unaffected by how far the body actually got.
        /// </para>
        /// </remarks>
        public void Step(float dt)
        {
            if (!IsActive)
            {
                return;
            }

            // Guarded rather than trusted: a non-positive or unreadable dt would either stall the
            // dash forever or consume it in one frame, and both are worse than skipping a frame.
            if (!(dt > 0f))
            {
                return;
            }

            float step = dt < _remaining ? dt : _remaining;

            _remaining -= step;

            Vector3 from = transform.position;

            _controller.Move(((_direction * _speed) + new Vector3(0f, GroundedFallSpeed, 0f)) * step);

            Sweep(from, transform.position);
        }

        /// <summary>
        /// Drops any dash in flight without moving anything. What a death or a scene change gets.
        /// </summary>
        /// <remarks>
        /// The session handle is released with it, so a motion that outlives its run cannot report
        /// into one that has ended.
        /// </remarks>
        public void Cancel()
        {
            _remaining = 0f;
            _session = null;
        }

        /// <param name="views">
        /// The arena's bodies, for turning a collider back into the id core knows it by — the same
        /// index <c>ConeOverlapQuery</c> resolves through, and for the same reason: no
        /// <c>GetComponent</c> per collider per frame.
        /// </param>
        /// <param name="enemyLayer">
        /// The layers a dash sweeps: the <c>Enemy</c> layer. Passed by <c>RunScope</c> from the one
        /// field that also arms the cone query, so a dash and a swing can never disagree about what
        /// counts as an enemy.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="views"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="enemyLayer"/> selects no layers.</exception>
        [Inject]
        public void Construct(EnemyViews views, LayerMask enemyLayer)
        {
            _views = views ?? throw new ArgumentNullException(nameof(views));

            if (enemyLayer.value == 0)
            {
                throw new ArgumentException(
                    "The enemy layer mask is empty, so every dash would sweep an empty capsule and " +
                    "the Charge would pass through everything without touching it. Set RunScope's " +
                    "Enemy Layer field to the Enemy layer.",
                    nameof(enemyLayer));
            }

            _enemyLayer = enemyLayer;
            _injected = true;
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _controller = GetComponent<CharacterController>();
        }

        /// <exception cref="InvalidOperationException">Nothing injected this motion.</exception>
        /// <remarks>
        /// Unlike the reticle and the ground glow this is not optional, so it is checked
        /// unconditionally: a dash that never sweeps is not a missing decoration, it is a dodge that
        /// passes through a crowd and hurts nobody — and it would fail silently, which is the one
        /// outcome this project will not have. Checked in <c>Start</c> rather than <c>Awake</c> for
        /// the reason every other view gives: <c>RunScope</c> injects from its own <c>Awake</c>, and
        /// Unity gives no order between two of those.
        /// </remarks>
        private void Start()
        {
            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(ChargeMotion)} was never injected, so a Charge would move the capsule " +
                    "and hit nothing. The component is registered by RunScope — drag the Player " +
                    "object in this scene onto its Charge Motion field.");
            }
        }

        /// <summary>
        /// Reports every enemy standing in the capsule the body just swept from
        /// <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The volume is the segment, not the destination.</b> A sphere at where the capsule
        /// ended up would miss everything it went straight past at 45 m/s, which is most of a dash
        /// through a crowd. The capsule is built between the two body centres, so its radius sweeps
        /// exactly the ground the character covered this frame.
        /// </para>
        /// <para>
        /// <b>Zero hits are not reported.</b> Unlike a swing — where silence would strand
        /// <c>PendingConeRequestId</c> and credit the next swing's hits to this one —
        /// <c>ReportChargeHits</c> retires nothing and is documented as something to call
        /// repeatedly. On a dash through open ground that is 13 empty calls into core per dash, and
        /// skipping them costs nothing and says nothing false.
        /// </para>
        /// <para>
        /// Deduplicated within the frame by id, so an enemy caught by both cap spheres is named
        /// once. Across frames it is core that dedupes — the same enemy is legitimately inside this
        /// capsule on several consecutive frames of one dash, and CC §5 pays for it once.
        /// </para>
        /// </remarks>
        private void Sweep(Vector3 from, Vector3 to)
        {
            Vector3 offset = _controller.center;
            float radius = _controller.radius;

            // Triggers included deliberately: an enemy body *is* a trigger (Enemy.prefab, M1-07),
            // so the default QueryTriggerInteraction would find nothing at all.
            int found = Physics.OverlapCapsuleNonAlloc(
                from + offset,
                to + offset,
                radius,
                _colliders,
                _enemyLayer,
                QueryTriggerInteraction.Collide);

            if (found >= _colliders.Length)
            {
                WarnAboutCapacityOnce();
            }

            int count = 0;

            for (int i = 0; i < found; i++)
            {
                if (count >= _ids.Length)
                {
                    break;
                }

                // A collider on the enemy layer that resolves to no live enemy is skipped in
                // silence, for the reason ConeOverlapQuery skips one: a corpse's collider is
                // disabled the moment it dies and its body is destroyed a moment later, so the
                // window where physics knows about something core does not is a frame wide and
                // expected.
                if (!_views.TryGetId(_colliders[i], out int id))
                {
                    continue;
                }

                if (Contains(count, id))
                {
                    continue;
                }

                _ids[count] = id;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            _session.ReportChargeHits(new ReadOnlySpan<int>(_ids, 0, count));
        }

        private bool Contains(int count, int id)
        {
            for (int i = 0; i < count; i++)
            {
                if (_ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <remarks>
        /// Once per run, for the reason <c>ConeOverlapQuery</c> warns once: at the cap this is true
        /// on every frame of every dash through a crowd, and the logging would cost more than the
        /// enemies being complained about. A full buffer is a <em>maybe</em> rather than a miss —
        /// the non-allocating overlap returns how many it wrote and never how many it found — so
        /// the wording says so instead of claiming a hit was dropped.
        /// </remarks>
        private void WarnAboutCapacityOnce()
        {
            if (_warnedAboutCapacity)
            {
                return;
            }

            _warnedAboutCapacity = true;

            Debug.LogWarning(
                $"A dash's sweep filled its {_colliders.Length}-collider buffer. Anything else " +
                "standing in the capsule was never considered and could not be hit, and there is " +
                "no way from here to tell whether there was. Raise SweepCapacity in " +
                $"{nameof(ChargeMotion)}.");
        }
    }
}
