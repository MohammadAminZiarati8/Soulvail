using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Animates the pale brood sac during the corpse's wait to burst.</summary>
    public sealed class BroodTellView : MonoBehaviour
    {
        [SerializeField] private Transform _brood;
        [SerializeField, Min(1f)] private float _swellScale = 1.35f;
        [SerializeField, Min(1)] private int _pulses = 2;

        private EnemyView _body;
        private IDisposable _swellingSubscription;
        private IDisposable _burstSubscription;
        private float _duration;
        private float _elapsed;

        public bool IsSwelling { get; private set; }

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _swellingSubscription?.Dispose();
            _burstSubscription?.Dispose();
            _body = GetComponent<EnemyView>();
            _swellingSubscription = hub.Subscribe<EnemyBroodSwelling>(OnSwelling);
            _burstSubscription = hub.Subscribe<EnemyBroodBurst>(OnBurst);
            ResetSac();
        }

        /// <summary>Advances the visual pulse independently of Unity's frame clock.</summary>
        public void Step(float dt)
        {
            if (!IsSwelling || !(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }

            _elapsed = Mathf.Min(_elapsed + dt, _duration);
            float progress = _elapsed / _duration;
            // Reach full amplitude early enough for the first pulse to crest.
            float envelope = Mathf.SmoothStep(0f, 1f, progress / Mathf.Min(0.4f, 0.8f / _pulses));
            float pulse = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * _pulses * progress);
            float swell = envelope * Mathf.Lerp(0.7f, 1f, pulse);
            _brood.localScale = Vector3.one * Mathf.Lerp(1f, _swellScale, swell);
        }

        private void LateUpdate() => Step(Time.deltaTime);

        // EnemyView's pool enables the body on every rental.
        private void OnEnable() => ResetSac();

        private void OnDisable() => ResetSac();

        private void OnDestroy()
        {
            _swellingSubscription?.Dispose();
            _burstSubscription?.Dispose();
        }

        private bool IsMine(int id) => _body != null && _body.IsBound && _body.Id == id;

        private void OnSwelling(EnemyBroodSwelling evt)
        {
            if (!isActiveAndEnabled || _brood == null || !IsMine(evt.Id) ||
                !(evt.Duration > 0f) || !float.IsFinite(evt.Duration))
            {
                return;
            }

            ResetSac();
            _duration = evt.Duration;
            IsSwelling = true;
        }

        private void OnBurst(EnemyBroodBurst evt)
        {
            if (IsMine(evt.Id))
            {
                IsSwelling = false;
            }
        }

        private void ResetSac()
        {
            IsSwelling = false;
            _elapsed = 0f;
            if (_brood != null)
            {
                _brood.localScale = Vector3.one;
            }
        }
    }
}
