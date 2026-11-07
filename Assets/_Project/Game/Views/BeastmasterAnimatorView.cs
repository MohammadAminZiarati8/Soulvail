using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Layers the throw over locomotion and tracks the two visible axes by flight id.</summary>
    public sealed class BeastmasterAnimatorView : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private GameObject _leftAxe;
        [SerializeField] private GameObject _rightAxe;

        private readonly int _moving = Animator.StringToHash("Moving");
        private readonly int _throw = Animator.StringToHash("Throw.Throw");
        private readonly int _empty = Animator.StringToHash("Throw.Empty");
        private readonly int _hit = Animator.StringToHash("Hit");
        private readonly int _dead = Animator.StringToHash("Dead");
        private PlayerView _body;
        private int _throwLayer;
        private int? _leftFlight;
        private int? _rightFlight;
        private bool _died;
        private IDisposable[] _subscriptions;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            Unsubscribe();
            _subscriptions = new IDisposable[]
            {
                events.Subscribe<PlayerAttacked>(OnAttacked),
                events.Subscribe<AxeThrown>(OnThrown),
                events.Subscribe<AxeCaught>(OnCaught),
                events.Subscribe<PlayerDamaged>(OnDamaged),
                events.Subscribe<PlayerDied>(OnDied),
                events.Subscribe<StageCleared>(_ => ResetHands()),
                events.Subscribe<StageTransitionStarted>(_ => ResetHands()),
                events.Subscribe<StageArrived>(_ => ResetHands()),
            };
        }

        private void Awake()
        {
            _body = GetComponentInParent<PlayerView>();
            _throwLayer = _animator.GetLayerIndex("Throw");
        }

        private void Update()
        {
            _animator.SetBool(_moving, !_died && _body != null && _body.Velocity.sqrMagnitude > 0.01f);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_subscriptions == null)
            {
                return;
            }

            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions = null;
        }

        private void OnAttacked(PlayerAttacked evt)
        {
            if (!_died && evt.Kind == WeaponKind.ReturningAxe)
            {
                _animator.Play(_throw, _throwLayer, 0f);
            }
        }

        private void OnThrown(AxeThrown evt)
        {
            if (!_leftFlight.HasValue)
            {
                _leftFlight = evt.Id;
                _leftAxe.SetActive(false);
            }
            else if (!_rightFlight.HasValue)
            {
                _rightFlight = evt.Id;
                _rightAxe.SetActive(false);
            }
        }

        private void OnCaught(AxeCaught evt)
        {
            if (_leftFlight == evt.Id)
            {
                _leftFlight = null;
                _leftAxe.SetActive(true);
            }
            else if (_rightFlight == evt.Id)
            {
                _rightFlight = null;
                _rightAxe.SetActive(true);
            }
        }

        private void ResetHands()
        {
            _leftFlight = null;
            _rightFlight = null;
            _leftAxe.SetActive(true);
            _rightAxe.SetActive(true);
            _animator.Play(_empty, _throwLayer, 0f);
        }

        private void OnDamaged(PlayerDamaged evt)
        {
            if (!_died && !evt.Blocked && evt.ToHp > 0f)
            {
                _animator.SetTrigger(_hit);
            }
        }

        private void OnDied(PlayerDied evt)
        {
            _died = true;
            _animator.SetLayerWeight(_throwLayer, 0f);
            _animator.SetBool(_dead, true);
        }
    }
}
