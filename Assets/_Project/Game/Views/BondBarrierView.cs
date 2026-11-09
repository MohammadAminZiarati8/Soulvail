using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// BT-08 rule 8: the Beastmaster's Bond Barrier as a pale bubble on his body — up from a
    /// barrier grant until it expires, and a flash and pop when a hit breaks it.
    /// </summary>
    /// <remarks>
    /// Event-driven, like <see cref="BulwarkView"/>: <c>ShieldGranted</c> and
    /// <c>ShieldGrantExpired</c> with <c>BondBarrier</c> set mean core's
    /// <c>Health.GrantedShieldOf</c> the barrier went above zero and came off; <c>BondBarrierBroke</c>
    /// is the pop. The colour is the HP bar's granted-shield segment, <see cref="Palette.PlayerBlocked"/>,
    /// never <see cref="Palette.Player"/>'s cyan.
    /// </remarks>
    public sealed class BondBarrierView : MonoBehaviour
    {
        [Tooltip("The bubble, on a child of the body. Hidden while no barrier stands.")]
        [SerializeField] private Renderer _bubble;

        [Tooltip("Alpha of a fully faded-in bubble.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxAlpha = 0.35f;

        [Tooltip("Seconds the bubble takes to fade in, and out on expiry.")]
        [Min(0f)]
        [SerializeField] private float _fadeSeconds = 0.15f;

        [Tooltip("Seconds the pop takes: a flash to full alpha that swells and vanishes.")]
        [Min(0.01f)]
        [SerializeField] private float _popSeconds = 0.25f;

        [Tooltip("How much bigger the bubble is at the end of the pop.")]
        [Min(1f)]
        [SerializeField] private float _popScale = 1.4f;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _properties;
        private IDisposable[] _subscriptions;
        private Vector3 _restScale = Vector3.one;
        private bool _up;
        private float _alpha;
        private float _pop;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            Unsubscribe();
            _subscriptions = new IDisposable[]
            {
                hub.Subscribe<ShieldGranted>(OnGranted),
                hub.Subscribe<ShieldGrantExpired>(OnExpired),
                hub.Subscribe<BondBarrierBroke>(_ => Pop()),
                hub.Subscribe<RunStarted>(_ => Hide()),
                hub.Subscribe<PlayerDied>(_ => Hide()),
            };
        }

        /// <summary>Whether a barrier is standing, as far as this view has been told.</summary>
        public bool IsUp => _up;

        /// <summary>Whether the pop is playing.</summary>
        public bool IsPopping => _pop > 0f;

        /// <summary>Advances the fade and the pop. <see cref="Update"/> calls it; tests may.</summary>
        public void Step(float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (_pop > 0f)
            {
                _pop = Mathf.Max(0f, _pop - (dt / _popSeconds));
                _alpha = _pop;
                Paint();
                return;
            }

            float target = _up ? 1f : 0f;
            if (_alpha == target)
            {
                return;
            }

            _alpha = _fadeSeconds > 0f ? Mathf.MoveTowards(_alpha, target, dt / _fadeSeconds) : target;
            Paint();
        }

        private void Awake()
        {
            if (_bubble != null)
            {
                _restScale = _bubble.transform.localScale;
            }

            Paint();
        }

        private void OnDestroy() => Unsubscribe();

        private void Update() => Step(Time.deltaTime);

        private void OnGranted(ShieldGranted evt)
        {
            if (!evt.BondBarrier)
            {
                return;
            }

            _up = true;
            _pop = 0f;
            Paint();
        }

        private void OnExpired(ShieldGrantExpired evt)
        {
            if (evt.BondBarrier)
            {
                _up = false;
            }
        }

        private void Pop()
        {
            _up = false;
            _pop = 1f;
            _alpha = 1f;
            Paint();
        }

        private void Hide()
        {
            _up = false;
            _pop = 0f;
            _alpha = 0f;
            Paint();
        }

        private void Paint()
        {
            if (_bubble == null)
            {
                return;
            }

            bool visible = _up || _alpha > 0f || _pop > 0f;
            if (_bubble.gameObject.activeSelf != visible)
            {
                _bubble.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            float swell = _pop > 0f ? Mathf.Lerp(_popScale, 1f, _pop) : 1f;
            _bubble.transform.localScale = _restScale * swell;

            // A pop flashes at full alpha; the resting bubble stays pale.
            float alpha = _pop > 0f ? _pop : _maxAlpha * _alpha;
            Color colour = Palette.PlayerBlocked;
            _properties ??= new MaterialPropertyBlock();
            _bubble.GetPropertyBlock(_properties);
            _properties.SetColor(_baseColorId, new Color(colour.r, colour.g, colour.b, alpha));
            _bubble.SetPropertyBlock(_properties);
        }

        private void Unsubscribe()
        {
            if (_subscriptions is null)
            {
                return;
            }

            foreach (IDisposable subscription in _subscriptions)
            {
                subscription?.Dispose();
            }

            _subscriptions = null;
        }
    }
}
