using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Rootling.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Drives an enemy body's <see cref="Animator"/> from what core already publishes: the legs from
    /// the body's velocity, a strike from its wind-up, a flinch from a hit, a fall from its death
    /// (M7-05h). It decides nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="PlayerAnimatorView"/>'s shape, for an enemy.</b> It sits on the model, finds its
    /// <see cref="EnemyView"/> above it, and reads that view's velocity every frame. It subscribes to
    /// <see cref="EnemyTelegraph"/>, <see cref="EnemyDamaged"/> and <see cref="EnemyDied"/>, and
    /// filters each by the body's id, as <see cref="EnemyHitFeedback"/> does — a pooled body is
    /// injected once and keeps its subscriptions for the pool's life, and an unbound one matches no
    /// event.
    /// </para>
    /// <para>
    /// <b>The strike lands when core's does.</b> <see cref="EnemyTelegraph.Duration"/> is the wind-up
    /// core is counting. The Attack state starts <see cref="_windupFromSeconds"/> into its clip — a
    /// transition offset in the controller, with the hands already rising — and plays at the rate
    /// that brings the clip's blow at <see cref="_strikeSeconds"/> to the end of the wind-up. So the
    /// blow on screen and the damage are one moment (GD §9.1 rule 1), whatever a depth or an affix
    /// does to the wind-up.
    /// </para>
    /// <para>
    /// <b>A pooled body forgets its life in <see cref="Forget"/></b>, which <see cref="EnemyView"/>'s
    /// despawn calls beside the feedback's reset (AR §18.4). Without it a body rented after a death
    /// would rise from the floor into its first walk.
    /// </para>
    /// </remarks>
    public sealed class EnemyAnimatorView : MonoBehaviour
    {
        /// <summary>How fast the shown speed chases the body's, in m/s per second — the player's.</summary>
        private const float SpeedFollowRate = 24f;

        /// <summary>
        /// The slowest and fastest the walk plays. Below the floor the legs creep; above the ceiling
        /// they blur, and the feet are left to slide instead — KayKit's crouched walk covers little
        /// ground at this body's size, so a Husk's 2 m/s is past the ceiling (M7-05h As built).
        /// </summary>
        private const float MinWalkRate = 0.6f;

        private const float MaxWalkRate = 4f;

        [Tooltip("The Animator on the model. Required: this component exists only to drive one.")]
        [SerializeField] private Animator _animator;

        [Tooltip("The ground speed the walk clip covers at 1x on this body, in m/s — its stride over " +
                 "its length, at the model's scale in the prefab.")]
        [Min(0.01f)]
        [SerializeField] private float _strideSpeed = 0.37f;

        [Tooltip("How far into the attack clip the Attack state starts, in seconds at 1x. Must match " +
                 "the transition offset in the controller, which the prefab's test checks.")]
        [Min(0f)]
        [SerializeField] private float _windupFromSeconds = 0.35f;

        [Tooltip("Where in the attack clip the blow lands, in seconds at 1x — the moment the rate " +
                 "brings to the end of core's wind-up.")]
        [Min(0.01f)]
        [SerializeField] private float _strikeSeconds = 0.87f;

        private readonly int _speedId = Animator.StringToHash("Speed");
        private readonly int _walkRateId = Animator.StringToHash("WalkRate");
        private readonly int _attackRateId = Animator.StringToHash("AttackRate");
        private readonly int _attackId = Animator.StringToHash("Attack");
        private readonly int _hitId = Animator.StringToHash("Hit");
        private readonly int _deadId = Animator.StringToHash("Dead");

        private EnemyView _body;
        private IDisposable _telegraphSubscription;
        private IDisposable _damagedSubscription;
        private IDisposable _diedSubscription;
        private float _shownSpeed;
        private bool _dead;
        private bool _injected;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c>, for <see cref="EnemyHitFeedback"/>'s reason:
        /// the pool instantiates the body through the container, so <c>OnEnable</c> runs before
        /// anything is injected.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _telegraphSubscription = hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _damagedSubscription = hub.Subscribe<EnemyDamaged>(OnDamaged);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
        }

        /// <summary>Moves the legs toward the body's speed by <paramref name="dt"/> seconds.</summary>
        /// <remarks>
        /// Public for <see cref="PlayerAnimatorView.Step"/>'s reason: <c>Time.deltaTime</c> is not
        /// something an EditMode fixture can advance. A zero, negative or non-finite step does
        /// nothing.
        /// </remarks>
        public void Step(float dt)
        {
            if (_animator == null || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (_body == null)
            {
                _body = GetComponentInParent<EnemyView>();
            }

            Vector3 velocity = _dead || _body == null ? Vector3.zero : _body.Velocity;
            velocity.y = 0f;

            _shownSpeed = Mathf.MoveTowards(_shownSpeed, velocity.magnitude, SpeedFollowRate * dt);

            _animator.SetFloat(_speedId, _shownSpeed);
            _animator.SetFloat(_walkRateId, Mathf.Clamp(_shownSpeed / _strideSpeed, MinWalkRate, MaxWalkRate));
        }

        /// <summary>
        /// Forgets everything a life did to the animation: the fall, the speed, a strike or a flinch
        /// still queued, and the state the Animator was in (M7-05h rule 5).
        /// </summary>
        /// <remarks>
        /// Called by <c>EnemyView.OnDespawn</c> while the body is still active, which is when
        /// <see cref="Animator.Rebind"/> can return it to its entry state; the pool deactivates it on the
        /// next line.
        /// </remarks>
        public void Forget()
        {
            _dead = false;
            _shownSpeed = 0f;

            if (_animator == null)
            {
                return;
            }

            if (_animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null)
            {
                _animator.Rebind();
            }

            _animator.SetFloat(_speedId, 0f);
            _animator.SetBool(_deadId, false);
            _animator.ResetTrigger(_attackId);
            _animator.ResetTrigger(_hitId);
        }

        private void Awake()
        {
            _body = GetComponentInParent<EnemyView>();
        }

        /// <exception cref="InvalidOperationException">A part of the body is missing.</exception>
        private void Start()
        {
            if (_animator == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyAnimatorView)} on '{name}' has no {nameof(Animator)} assigned, so " +
                    "this body would slide at the player in its rest pose. Drag the model's Animator " +
                    "onto this component.");
            }

            if (_body == null)
            {
                _body = GetComponentInParent<EnemyView>();
            }

            if (_body == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyAnimatorView)} on '{name}' has no {nameof(EnemyView)} above it, so its " +
                    "legs have no velocity to read and no id to answer to. It belongs on the model " +
                    "inside an enemy body prefab.");
            }

            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyAnimatorView)} on '{name}' was never injected, so no strike, hit " +
                    "or death will ever reach it. Enemy bodies must be created through EnemyViews.");
            }
        }

        private void OnDestroy()
        {
            _telegraphSubscription?.Dispose();
            _damagedSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _telegraphSubscription = null;
            _damagedSubscription = null;
            _diedSubscription = null;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private bool IsMine(int id)
        {
            if (_body == null)
            {
                _body = GetComponentInParent<EnemyView>();
            }

            return _body != null && _body.IsBound && id == _body.Id;
        }

        private void OnTelegraph(EnemyTelegraph evt)
        {
            // A wind-up of zero is a legal untelegraphed hit and there is nothing to draw — the same
            // guard EnemyHitFeedback makes, and what keeps the rate from dividing by nothing.
            if (_animator == null || _dead || !IsMine(evt.Id) || !(evt.Duration > 0f))
            {
                return;
            }

            _animator.SetFloat(_attackRateId, Mathf.Max(_strikeSeconds - _windupFromSeconds, 0.01f) / evt.Duration);
            _animator.SetTrigger(_attackId);
        }

        private void OnDamaged(EnemyDamaged evt)
        {
            // A killing blow does not flinch: the death is the answer to it, in the same call.
            if (_animator == null || _dead || evt.Killed || !IsMine(evt.Id))
            {
                return;
            }

            _animator.SetTrigger(_hitId);
        }

        private void OnDied(EnemyDied evt)
        {
            if (_animator == null || _dead || !IsMine(evt.Id))
            {
                return;
            }

            _dead = true;

            // Nothing queued may outlive the life it was for: a strike cancelled by the death, and a
            // flinch from the blow before it.
            _animator.ResetTrigger(_attackId);
            _animator.ResetTrigger(_hitId);
            _animator.SetBool(_deadId, true);
        }
    }
}
