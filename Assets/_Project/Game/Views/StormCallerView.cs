using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Draws the staff's sand tell for the current rental of this body.</summary>
    public sealed class StormCallerView : MonoBehaviour
    {
        private const float ScatterSeconds = 0.15f;
        private const float GrainTravelSeconds = 0.85f;
        private const float GrainInterval = 0.027f;

        [SerializeField] private ParticleSystem _spiral;

        private readonly ParticleSystem.Particle[] _grains = new ParticleSystem.Particle[32];
        private EnemyView _body;
        private EnemyAnimatorView _animation;
        private IDisposable _phase;
        private IDisposable _broken;
        private IDisposable _died;
        private int _enemyId;
        private float _age;
        private float _duration;
        private int _grainCount;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            _body = GetComponent<EnemyView>();
            _animation = GetComponentInChildren<EnemyAnimatorView>(true);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", new Color32(206, 163, 109, 220));
            _spiral.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(properties);
            _phase = events.Subscribe<StormCallerPhaseChanged>(OnPhase);
            _broken = events.Subscribe<StormChannelBroken>(OnBroken);
            _died = events.Subscribe<EnemyDied>(OnDied);
        }

        public void Bind(int enemyId)
        {
            Clear();
            _enemyId = enemyId;
        }

        private bool IsMine(int id) =>
            isActiveAndEnabled && id > 0 && id == (_enemyId != 0 ? _enemyId : _body != null ? _body.Id : 0);

        private void OnPhase(StormCallerPhaseChanged evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            _animation.SetChanneling(evt.Phase == StormCallerPhase.Channel);
            if (evt.Phase == StormCallerPhase.Channel)
            {
                _age = 0f;
                _duration = evt.Seconds;
                _spiral.Play(false);
            }
            else
            {
                // A broken channel also publishes Hold first. Keep its last grains for the scatter.
                _duration = 0f;
                _spiral.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnBroken(StormChannelBroken evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            _animation.SetChanneling(false);
            _duration = 0f;
            _spiral.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            for (int i = 0; i < _grainCount; i++)
            {
                Vector3 outward = _grains[i].position - transform.position;
                outward.y = 0f;
                _grains[i].velocity = outward.normalized * 2.8f + Vector3.up * 0.45f;
                _grains[i].remainingLifetime = ScatterSeconds;
                _grains[i].startLifetime = ScatterSeconds;
            }

            _spiral.SetParticles(_grains, _grainCount);
            _grainCount = 0;
        }

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                Clear();
            }
        }

        public void Step(float dt)
        {
            if (!(_duration > 0f))
            {
                return;
            }

            _age = Mathf.Min(_age + dt, _duration);
            int newest = Mathf.FloorToInt(_age / GrainInterval);
            Vector3 glow = _spiral.transform.position;
            Vector3 staffOffset = glow - transform.position;
            float heading = Mathf.Atan2(staffOffset.z, staffOffset.x);
            float orbitRadius = Mathf.Max(0.65f, new Vector2(staffOffset.x, staffOffset.z).magnitude);
            _grainCount = 0;

            for (int i = Mathf.Max(0, newest - _grains.Length + 1); i <= newest; i++)
            {
                float travel = (_age - i * GrainInterval) / GrainTravelSeconds;
                if (travel >= 1f)
                {
                    continue;
                }

                float angle = heading + travel * 2.4f * Mathf.PI * 2f;
                float radius = Mathf.Lerp(orbitRadius, 0.48f, travel);
                Vector3 orbit = transform.position + new Vector3(
                    Mathf.Cos(angle) * radius, staffOffset.y + 0.75f * travel, Mathf.Sin(angle) * radius);
                _grains[_grainCount++] = new ParticleSystem.Particle
                {
                    position = Vector3.Lerp(glow, orbit, Mathf.Clamp01(travel * 12f)),
                    startSize = 0.065f + 0.025f * (i % 3),
                    startColor = new Color32(255, 255, 255, (byte)(220f * (1f - travel))),
                    startLifetime = 1f,
                    remainingLifetime = 0.08f,
                    randomSeed = (uint)(i + 1),
                };
            }

            _spiral.SetParticles(_grains, _grainCount);
        }

        private void Clear()
        {
            _duration = 0f;
            _grainCount = 0;
            _animation?.SetChanneling(false);
            if (_spiral != null)
            {
                _spiral.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void OnDisable()
        {
            _enemyId = 0;
            Clear();
        }

        private void OnDestroy()
        {
            _phase?.Dispose();
            _broken?.Dispose();
            _died?.Dispose();
        }
    }
}
