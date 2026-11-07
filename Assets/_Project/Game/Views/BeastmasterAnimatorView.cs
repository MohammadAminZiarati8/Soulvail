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
        private readonly int _rescuing = Animator.StringToHash("Rescuing");
        private readonly int _jump = Animator.StringToHash("Base Layer.Jump_Full_Short");
        private bool _swap;
        private bool _rescueAdvanced;
        private PlayerView _body;
        private int _throwLayer;
        private int? _leftFlight;
        private int? _rightFlight;
        private bool _died;
        private IDisposable[] _subscriptions;

        [Inject]
        public void Construct(DomainEventHub events, ContentCatalog catalog = null)
        {
            Unsubscribe();
            _subscriptions = new IDisposable[]
            {
                events.Subscribe<RunStarted>(evt =>
                {
                    _swap = catalog != null && catalog.TryGetCharacter(evt.CharacterId, out CharacterSpec character)
                        && character.MovementSkill.Kind == MovementSkillKind.Swap;
                }),
                events.Subscribe<ChargeStarted>(_ => PlaySwap()),
                events.Subscribe<PlayerAttacked>(OnAttacked),
                events.Subscribe<AxeThrown>(OnThrown),
                events.Subscribe<AxeCaught>(OnCaught),
                events.Subscribe<PlayerDamaged>(OnDamaged),
                events.Subscribe<PlayerDied>(OnDied),
                events.Subscribe<RescueProgressed>(_ => _rescueAdvanced = true),
                events.Subscribe<PetRose>(_ => StopRescue()),
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
            _animator.SetBool(_moving, IsMoving());
        }

        private void LateUpdate()
        {
            bool kneeling = !_died && _rescueAdvanced && !IsMoving();
            _rescueAdvanced = false;
            _animator.SetBool(_rescuing, kneeling);
            bool jumping = _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == _jump;
            _animator.SetLayerWeight(_throwLayer, !_died && !kneeling && !jumping ? 1f : 0f);
        }

        private void PlaySwap()
        {
            if (_swap && !_died)
            {
                StopRescue();
                _animator.SetLayerWeight(_throwLayer, 0f);
                _animator.Play(_jump, 0, 0f);
            }
        }

        private bool IsMoving() => !_died && _body != null && _body.Velocity.sqrMagnitude > 0.01f;

        private void StopRescue()
        {
            _rescueAdvanced = false;
            _animator.SetBool(_rescuing, false);
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
            StopRescue();
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
            StopRescue();
            _animator.SetLayerWeight(_throwLayer, 0f);
            _animator.SetBool(_dead, true);
        }
    }
}
