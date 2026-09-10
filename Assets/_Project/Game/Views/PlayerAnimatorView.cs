using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Player.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Puts the character's animation on the fight core has already decided. Reads the body's
    /// speed, listens for the four combat facts that have a pose to go with them, and drives an
    /// <see cref="Animator"/>. It decides nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Animation never gates a damage frame.</b> The Censer's cadence and the moment the cone
    /// resolves are core's (CC §4.2 puts the damage 40 % of the way through the swing), and this
    /// component only hears about the swing after the decision is made. There are deliberately no
    /// Animation Events on any clip: an event that dealt damage would move the fight into the
    /// timeline of an art asset, where it could be retimed by anyone editing an FBX.
    /// </para>
    /// <para>
    /// The consequence runs one way, and it is why <c>AttackSpeed</c> exists. The swing clip is
    /// 1.37 s and the Censer swings roughly three times a second, so the clip is *scaled to fit
    /// the weapon* every time the weapon's cadence moves — which it does, because M1-13's Focus
    /// ramp is a fire-rate modifier. A constant here would look right at rest and drift apart the
    /// moment the ramp climbed.
    /// </para>
    /// <para>
    /// Nothing here applies movement. <see cref="Animator.applyRootMotion"/> is off on the prefab
    /// and every clip is authored in place, so the only thing that can move the body is the
    /// <c>PlayerMoveIntent</c> <see cref="PlayerView"/> applies.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerView))]
    public sealed class PlayerAnimatorView : MonoBehaviour
    {
        /// <summary>
        /// How fast the blend-tree speed is allowed to chase the body's real speed, in m/s².
        /// </summary>
        /// <remarks>
        /// Core accelerates at a finite rate already (CC §2.4), so this is not smoothing the
        /// movement — it is smoothing the *sample*. Without it a single frame of collision or a
        /// stick released between two ticks snaps the tree from run to idle and back, which reads
        /// as a stutter in the legs rather than as a change of pace. High enough that a genuine
        /// stop still finishes inside a couple of frames.
        /// </remarks>
        private const float SpeedFollowRate = 24f;

        /// <summary>
        /// The reference cadence the attack clip was authored against, in seconds — the length of
        /// <c>Melee_1H_Attack_Slice_Horizontal</c>.
        /// </summary>
        /// <remarks>
        /// Paired with the interval between real swings to give the clip's speed multiplier. Held
        /// as a constant rather than read off the clip because the state's motion can be swapped
        /// in the controller without this component being rebuilt, and a wrong constant is visibly
        /// wrong where a silently-null clip reference is not.
        /// </remarks>
        private const float AuthoredSwingSeconds = 1.3666667f;

        /// <summary>Ceiling on the attack multiplier, so a very fast weapon does not become a blur.</summary>
        private const float MaxAttackSpeed = 6f;

        [Tooltip("The Animator on the character model. Required: this component exists only to " +
                 "drive one, and a missing reference would fail silently as a body that never " +
                 "moves rather than as an error.")]
        [SerializeField] private Animator _animator;

        // Hashed once. Instance fields rather than statics, because nothing in this project holds
        // static state (AR §7) and the bytes are free.
        private readonly int _speedId = Animator.StringToHash("Speed");
        private readonly int _attackSpeedId = Animator.StringToHash("AttackSpeed");
        private readonly int _attackId = Animator.StringToHash("Attack");
        private readonly int _chargeId = Animator.StringToHash("Charge");
        private readonly int _hitId = Animator.StringToHash("Hit");
        private readonly int _deadId = Animator.StringToHash("Dead");

        private PlayerView _body;

        private IDisposable _attackSubscription;
        private IDisposable _damagedSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _chargeSubscription;

        private float _shownSpeed;
        private float _lastAttackTime;
        private bool _injected;
        private bool _dead;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _attackSubscription = hub.Subscribe<PlayerAttacked>(OnAttacked);
            _damagedSubscription = hub.Subscribe<PlayerDamaged>(OnDamaged);
            _diedSubscription = hub.Subscribe<PlayerDied>(OnDied);
            _chargeSubscription = hub.Subscribe<ChargeStarted>(OnChargeStarted);
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _body = GetComponent<PlayerView>();
        }

        /// <exception cref="InvalidOperationException">No Animator is dressed, or nothing injected this component.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for the reason <c>ReticleView</c>
        /// gives — injection happens during <c>RunScope</c>'s own <c>Awake</c>, and Unity gives no
        /// order between two of those.
        /// </remarks>
        private void Start()
        {
            if (_animator == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PlayerAnimatorView)} has no {nameof(Animator)} assigned, so the " +
                    "character would stand still through the whole fight. Drag the model's " +
                    "Animator onto this component.");
            }

            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(PlayerAnimatorView)} was never injected, so no combat event will " +
                    "ever reach it. The body is registered by RunScope — drag this object onto " +
                    "its Player View field.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal, for the reason
            // PlayerView unsubscribes: a body destroyed mid-run would otherwise stay in the
            // subscriber list and be handed events for a component Unity has already killed.
            _attackSubscription?.Dispose();
            _damagedSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _chargeSubscription?.Dispose();

            _attackSubscription = null;
            _damagedSubscription = null;
            _diedSubscription = null;
            _chargeSubscription = null;
        }

        private void Update()
        {
            if (_animator == null)
            {
                return;
            }

            // PlayerView.Velocity is what core *asked* for, not what the controller achieved —
            // the same distinction that view documents. A player leaning on a wall is still
            // running, and their legs should say so.
            float target = _dead ? 0f : _body.Velocity.magnitude;

            _shownSpeed = Mathf.MoveTowards(_shownSpeed, target, SpeedFollowRate * Time.deltaTime);
            _animator.SetFloat(_speedId, _shownSpeed);
        }

        /// <remarks>
        /// The multiplier is derived from the gap between this swing and the last, so it tracks
        /// whatever the weapon's cadence currently is — including M1-13's Focus ramp — without
        /// this component knowing anything about weapons. The first swing of a run has no previous
        /// swing to measure against and plays at the authored speed; by the second it is correct,
        /// which at three swings a second is a third of a second of being slightly slow.
        /// </remarks>
        private void OnAttacked(PlayerAttacked evt)
        {
            if (_animator == null || _dead)
            {
                return;
            }

            float now = Time.time;

            if (_lastAttackTime > 0f)
            {
                float interval = now - _lastAttackTime;

                if (interval > 0f)
                {
                    _animator.SetFloat(
                        _attackSpeedId,
                        Mathf.Min(AuthoredSwingSeconds / interval, MaxAttackSpeed));
                }
            }

            _lastAttackTime = now;
            _animator.SetTrigger(_attackId);
        }

        /// <remarks>
        /// A blocked hit gets no flinch, and neither does one the Aegis ate whole. Both are facts
        /// the HUD wants and the body does not: flinching at a hit that did nothing tells the
        /// player they were hurt when they were not, which is exactly backwards from what a hit
        /// reaction is for.
        /// </remarks>
        private void OnDamaged(PlayerDamaged evt)
        {
            if (_animator == null || _dead || evt.Blocked || evt.ToHp <= 0f)
            {
                return;
            }

            _animator.SetTrigger(_hitId);
        }

        private void OnChargeStarted(ChargeStarted evt)
        {
            if (_animator == null || _dead)
            {
                return;
            }

            _animator.SetTrigger(_chargeId);
        }

        /// <remarks>
        /// A bool rather than a trigger, and latched here as well as in the controller. Death is
        /// the one state with no way out, so it has to survive any event that arrives after it —
        /// and <c>PlayerDamaged</c> can, when the killing blow and a second enemy's strike land on
        /// the same tick.
        /// </remarks>
        private void OnDied(PlayerDied evt)
        {
            if (_animator == null)
            {
                return;
            }

            _dead = true;
            _animator.SetBool(_deadId, true);
        }
    }
}
