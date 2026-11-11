using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class EnemyShineView : MonoBehaviour
    {
        [SerializeField] private Renderer _overlay;
        [SerializeField] private ProjectileView _bolt;

        private readonly int _intensityId = Shader.PropertyToID("_Intensity");
        private readonly int _alphaId = Shader.PropertyToID("_Alpha");
        private EnemyView _body;
        private IDisposable _subscription;
        private MaterialPropertyBlock _properties;
        private int _enemyId;
        private int _renderedId;
        private ShinePhase _phase;
        private float _duration;
        private float _elapsed;

        internal ProjectileView Bolt => _bolt;

        private int BoundId => _enemyId > 0 ? _enemyId : _body != null ? _body.Id : 0;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            _body = GetComponentInParent<EnemyView>(true);
            _subscription?.Dispose();
            _subscription = events.Subscribe<EnemyShineChanged>(OnShineChanged);
            ResetShine();
        }

        public void Bind(int enemyId)
        {
            _enemyId = enemyId;
            ResetShine();
        }

        public void Step(float dt)
        {
            if (_phase == ShinePhase.None || _phase == ShinePhase.Open || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (BoundId != _renderedId)
            {
                ResetShine();
                return;
            }

            _elapsed += dt;
            float intensity = _phase == ShinePhase.Glint
                ? 0.6f * Mathf.Clamp01(_elapsed / _duration)
                : 1f + 0.1f * Mathf.Sin(2f * Mathf.PI * 6f * _elapsed);
            Paint(intensity);
        }

        private void Update() => Step(Time.deltaTime);

        private void OnDisable() => Bind(0);

        private void OnDestroy() => _subscription?.Dispose();

        private void OnShineChanged(EnemyShineChanged evt)
        {
            int id = BoundId;
            if (!isActiveAndEnabled || _overlay == null || id <= 0 || evt.Id != id)
            {
                return;
            }

            _renderedId = id;
            _phase = evt.Phase;
            _duration = evt.Seconds > 0f && !float.IsInfinity(evt.Seconds) ? evt.Seconds : 1f;
            _elapsed = 0f;
            _overlay.enabled = _phase is ShinePhase.Glint or ShinePhase.Shine;
            Paint(_phase == ShinePhase.Shine ? 1f : 0f);
        }

        private void ResetShine()
        {
            _phase = ShinePhase.None;
            _elapsed = 0f;
            if (_overlay != null)
            {
                _overlay.enabled = false;
                Paint(0f);
            }
        }

        private void Paint(float intensity)
        {
            _properties ??= new MaterialPropertyBlock();
            _overlay.GetPropertyBlock(_properties);
            _properties.SetFloat(_intensityId, intensity);
            // The shared additive Fireball shader uses alpha as its blend strength.
            _properties.SetFloat(_alphaId, intensity);
            _overlay.SetPropertyBlock(_properties);
        }
    }
}
