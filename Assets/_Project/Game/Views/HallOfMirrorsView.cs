using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>One world-space hall marker, reused for every cast in a run.</summary>
    public sealed class HallOfMirrorsView : MonoBehaviour
    {
        private const float RiseSeconds = 0.2f;
        private const float FadeSeconds = 0.3f;
        private const float GroundLift = 0.045f;
        private const float ShimmerHeight = 0.36f;

        [SerializeField] private LineRenderer _ground;
        [SerializeField] private LineRenderer _shimmer;

        private IDisposable _raised, _cleared, _transition, _arrived, _ended;
        private Vector3 _center;
        private Color _groundColor, _shimmerColor;
        private float _age, _seconds;

        public void Construct(DomainEventHub hub)
        {
            _groundColor = _ground.startColor;
            _shimmerColor = _shimmer.startColor;
            Clear();
            _raised = hub.Subscribe<HallRaised>(OnRaised);
            _cleared = hub.Subscribe<StageCleared>(_ => Clear());
            _transition = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _arrived = hub.Subscribe<StageArrived>(_ => Clear());
            _ended = hub.Subscribe<RunEnded>(_ => Clear());
        }

        private void OnRaised(HallRaised evt)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _center = evt.Center.ToUnity();
            transform.localScale = new Vector3(evt.Radius, 1f, evt.Radius);
            _seconds = evt.Seconds;
            _age = 0f;
            _ground.enabled = true;
            Draw();
        }

        public void Step(float dt)
        {
            if (!_ground.enabled)
            {
                return;
            }

            float previousAge = _age;
            _age += dt;
            if (_age >= _seconds + FadeSeconds)
            {
                Clear();
                return;
            }

            if (previousAge < RiseSeconds || _age >= _seconds)
            {
                Draw();
            }
        }

        private void Draw()
        {
            float rise = Mathf.Clamp01(_age / RiseSeconds);
            float fade = 1f - Mathf.Clamp01((_age - _seconds) / FadeSeconds);
            transform.position = _center + Vector3.up * Mathf.Lerp(0.015f, GroundLift, rise);
            SetOpacity(_ground, _groundColor, Mathf.Lerp(1.7f, 1f, rise) * fade);

            _shimmer.enabled = rise < 1f && _age < _seconds;
            if (_shimmer.enabled)
            {
                _shimmer.transform.localPosition = Vector3.up * (ShimmerHeight * rise);
                SetOpacity(_shimmer, _shimmerColor, 1f - rise);
            }
        }

        private static void SetOpacity(LineRenderer line, Color color, float opacity)
        {
            color.a *= opacity;
            line.startColor = color;
            line.endColor = color;
        }

        private void Clear()
        {
            _ground.enabled = false;
            _shimmer.enabled = false;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            _raised?.Dispose();
            _cleared?.Dispose();
            _transition?.Dispose();
            _arrived?.Dispose();
            _ended?.Dispose();
        }
    }
}
