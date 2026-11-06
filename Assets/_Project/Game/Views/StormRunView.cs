using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>Renders Storm Run from the buff and roll edges; the volley itself uses the arrow view.</summary>
    public sealed class StormRunView : MonoBehaviour, IDisposable
    {
        private const int RingParticles = 48;
        private const float RingRadius = 0.45f;
        private const float RingSpeed = 2.8f;
        private const float GroundOffset = 0.06f;

        [SerializeField] private ParticleSystem _aura;
        [SerializeField] private ParticleSystem _trail;
        [SerializeField] private ParticleSystem _burst;

        private readonly IDisposable[] _subscriptions = new IDisposable[8];
        private bool _active;
        private bool _rolling;

        public void Construct(DomainEventHub hub)
        {
            _subscriptions[0] = hub.Subscribe<StormRunChanged>(OnStormRun);
            _subscriptions[1] = hub.Subscribe<ChargeStarted>(OnRollStarted);
            _subscriptions[2] = hub.Subscribe<ChargeEnded>(_ => OnRollEnded());
            _subscriptions[3] = hub.Subscribe<StageCleared>(_ => Clear());
            _subscriptions[4] = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _subscriptions[5] = hub.Subscribe<StageArrived>(_ => Clear());
            _subscriptions[6] = hub.Subscribe<PlayerDied>(_ => Clear());
            _subscriptions[7] = hub.Subscribe<RunEnded>(_ => Clear());
        }

        private void OnStormRun(StormRunChanged evt)
        {
            if (!isActiveAndEnabled || _active == evt.Active)
            {
                return;
            }

            _active = evt.Active;
            if (_active)
            {
                _aura.Play(false);
                if (_rolling)
                {
                    _trail.Play(false);
                }
            }
            else
            {
                _aura.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                _trail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnRollStarted(ChargeStarted evt)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _rolling = true;
            Vector3 direction = new(evt.DirectionXZ.X, 0f, evt.DirectionXZ.Y);
            if (direction.sqrMagnitude > 0f)
            {
                _trail.transform.rotation = Quaternion.LookRotation(-direction);
            }

            if (_active)
            {
                _trail.Play(false);
            }
        }

        private void OnRollEnded()
        {
            _rolling = false;
            if (!_active)
            {
                return;
            }

            _trail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _burst.Play(false);
            Vector3 centre = transform.position + Vector3.up * GroundOffset;
            for (int i = 0; i < RingParticles; i++)
            {
                float angle = i * (2f * Mathf.PI / RingParticles);
                Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _burst.Emit(new ParticleSystem.EmitParams
                {
                    position = centre + radial * RingRadius,
                    velocity = radial * RingSpeed,
                }, 1);
            }
        }

        public void Clear()
        {
            _active = false;
            _rolling = false;
            _aura.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            _trail.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            _burst.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Dispose()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription?.Dispose();
            }
        }

        private void OnDisable() => Clear();

        private void OnDestroy() => Dispose();
    }
}
