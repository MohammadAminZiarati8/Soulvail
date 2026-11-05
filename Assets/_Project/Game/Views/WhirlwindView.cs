using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>Draws each launched ring in world space while the Ranger runs on.</summary>
    public sealed class WhirlwindView : MonoBehaviour, IDisposable
    {
        public const float Duration = 0.25f;
        private const float GroundOffset = 0.035f;
        private const int PoolSize = 3;

        [SerializeField] private GameObject _swirl;

        private readonly GameObject[] _pool = new GameObject[PoolSize];
        private readonly float[] _started = new float[PoolSize];
        private Action<bool> _spin;
        private IDisposable _ring, _cleared, _transition, _arrived, _died, _ended;
        private int _active;
        private int _next;

        public void Construct(DomainEventHub hub, Action<bool> spin)
        {
            _spin = spin;
            _pool[0] = _swirl;
            _swirl.SetActive(false);
            for (int i = 1; i < _pool.Length; i++)
            {
                _pool[i] = Instantiate(_swirl, transform, false);
            }

            _ring = hub.Subscribe<WhirlwindRingFired>(OnRing);
            _cleared = hub.Subscribe<StageCleared>(_ => Clear());
            _transition = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _arrived = hub.Subscribe<StageArrived>(_ => Clear());
            _died = hub.Subscribe<PlayerDied>(_ => Clear());
            _ended = hub.Subscribe<RunEnded>(_ => Clear());
        }

        private void OnRing(WhirlwindRingFired evt)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            int slot = _next;
            _next = (_next + 1) % _pool.Length;
            GameObject swirl = _pool[slot];
            if (!swirl.activeSelf)
            {
                _active++;
            }

            swirl.transform.SetPositionAndRotation(evt.Origin.ToUnity() + Vector3.up * GroundOffset,
                Quaternion.identity);
            swirl.transform.localScale = Vector3.one;
            _started[slot] = Time.time;
            swirl.SetActive(true);
            _spin(true);
        }

        public void Step(float now)
        {
            if (_active == 0)
            {
                return;
            }

            for (int i = 0; i < _pool.Length; i++)
            {
                GameObject swirl = _pool[i];
                if (!swirl.activeSelf)
                {
                    continue;
                }

                float age = now - _started[i];
                if (now >= _started[i] + Duration)
                {
                    swirl.SetActive(false);
                    _active--;
                    continue;
                }

                float progress = Mathf.Clamp01(age / Duration);
                swirl.transform.rotation = Quaternion.Euler(0f, progress * 100f, 0f);
                swirl.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, progress);
            }

            if (_active == 0)
            {
                _spin(false);
            }
        }

        public void Clear()
        {
            if (_active == 0)
            {
                return;
            }

            foreach (GameObject swirl in _pool)
            {
                swirl.SetActive(false);
            }

            _active = 0;
            _spin(false);
        }

        public void Dispose()
        {
            Clear();
            _ring?.Dispose();
            _cleared?.Dispose();
            _transition?.Dispose();
            _arrived?.Dispose();
            _died?.Dispose();
            _ended?.Dispose();
        }

        private void LateUpdate() => Step(Time.time);

        private void OnDisable() => Clear();

        private void OnDestroy() => Dispose();
    }
}
