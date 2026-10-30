using System;
using Soulvail.Game.Pooling;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>A planted bulb, animated only by the snapshot step until core hatches it.</summary>
    public sealed class SeedPodView : MonoBehaviour, IPoolable
    {
        public const float StartScale = 0.7f;
        private const float WobbleDegrees = 8f;
        private const float WobbleCycles = 3f;

        private float _seconds;
        private float _elapsed;
        private bool _planted;

        public float Swell => _planted ? Mathf.Clamp01(_elapsed / _seconds) : 0f;

        public void Plant(Vector3 at, float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            _seconds = seconds;
            _elapsed = 0f;
            _planted = true;
            transform.position = at;
            Paint();
            gameObject.SetActive(true);
        }

        public void Step(float dt)
        {
            if (!_planted || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _elapsed = Mathf.Min(_seconds, _elapsed + dt);
            Paint();
        }

        public void Hide()
        {
            _planted = false;
            _seconds = 0f;
            _elapsed = 0f;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one * StartScale;
            gameObject.SetActive(false);
        }

        public void OnSpawn() { }

        public void OnDespawn() => Hide();

        private void Paint()
        {
            float swell = Swell;
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, swell);
            float wobble = Mathf.Sin(swell * WobbleCycles * 2f * Mathf.PI) * WobbleDegrees * (1f - swell);
            transform.localRotation = Quaternion.Euler(wobble * 0.5f, 0f, wobble);
        }
    }
}
