using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Draws the two core hazards with a fixed pool and reads their current poses after the run tick.</summary>
    public sealed class TornadoViews : MonoBehaviour
    {
        private const float RiseSeconds = 0.3f;
        private const float CollapseSeconds = 0.4f;
        private const float TimeoutSeconds = 0.3f;
        private const float TurnsPerSecond = 1.6f;
        private const int GrainCount = 72;

        [SerializeField] private GameObject _funnelPrefab;

        private sealed class Funnel
        {
            public GameObject Body;
            public ParticleSystem Dust;
            public TelegraphRingView Ring;
            public ParticleSystemRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public Color Colour;
            public int Id;
            public float Age;
            public float EndDuration;
            public float EndAge;
            public float EndHeight;
            public bool Ended;
        }

        private readonly Funnel[] _pool = new Funnel[TornadoField.Capacity];
        private readonly ParticleSystem.Particle[] _grains = new ParticleSystem.Particle[GrainCount];
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private IRunSession _session;
        private IDisposable _rose;
        private IDisposable _ended;
        private IDisposable _clear;
        private IDisposable _transition;

        public int ShownCount { get; private set; }

        [Inject]
        public void Construct(IRunSession session, DomainEventHub events)
        {
            _session = session;
            var poolRoot = new GameObject("Tornado Pool");
            poolRoot.SetActive(false);
            poolRoot.transform.SetParent(transform, false);
            for (int i = 0; i < _pool.Length; i++)
            {
                GameObject body = Instantiate(_funnelPrefab, poolRoot.transform, false);
                body.SetActive(false);
                var renderer = body.GetComponentInChildren<ParticleSystemRenderer>(true);
                _pool[i] = new Funnel
                {
                    Body = body,
                    Renderer = renderer,
                    Properties = new MaterialPropertyBlock(),
                    Colour = renderer.sharedMaterial.color,
                    Dust = body.GetComponentInChildren<ParticleSystem>(true),
                    Ring = body.GetComponentInChildren<TelegraphRingView>(true),
                };
            }

            poolRoot.SetActive(true);
            _rose = events.Subscribe<TornadoRose>(OnRose);
            _ended = events.Subscribe<TornadoEnded>(OnEnded);
            _clear = events.Subscribe<StageCleared>(_ => Clear());
            _transition = events.Subscribe<StageTransitionStarted>(_ => Clear());
        }

        private void OnRose(TornadoRose evt)
        {
            if (!isActiveAndEnabled || Find(evt.TornadoId) != null)
            {
                return;
            }

            Funnel slot = null;
            foreach (Funnel candidate in _pool)
            {
                if (candidate.Id == 0)
                {
                    slot = candidate;
                    break;
                }

                // Core can reuse its slot while a finished visual is still fading.
                if (candidate.Ended && (slot == null || candidate.EndAge > slot.EndAge))
                {
                    slot = candidate;
                }
            }

            if (slot == null)
            {
                return;
            }

            Return(slot);
            slot.Id = evt.TornadoId;
            Vector3 position = evt.Position.ToUnity();
            slot.Body.transform.position = position;
            slot.Ring.gameObject.SetActive(true);
            slot.Ring.Bind(position, evt.Radius, evt.Seconds, false, Palette.Danger);
            slot.Body.SetActive(true);
            slot.Dust.Play(false);
            Draw(slot);
            ShownCount++;
        }

        private Funnel Find(int id)
        {
            foreach (Funnel slot in _pool)
            {
                if (slot.Id == id)
                {
                    return slot;
                }
            }

            return null;
        }

        private void OnEnded(TornadoEnded evt)
        {
            Funnel slot = Find(evt.TornadoId);
            if (slot != null)
            {
                if (evt.Collapsed && slot.EndDuration > 0f)
                {
                    Return(slot);
                    return;
                }

                BeginEnd(slot, evt.Collapsed);
                slot.Ended = true;
            }
        }

        private void BeginEnd(Funnel slot, bool collapsed)
        {
            slot.Ring.gameObject.SetActive(false);
            slot.EndDuration = collapsed ? CollapseSeconds : TimeoutSeconds;
            slot.EndHeight = Mathf.Clamp01(slot.Age / RiseSeconds);
        }

        public void Step(float dt)
        {
            RunState state = _session?.State;
            if (state != null)
            {
                for (int i = 0; i < state.TornadoCount; i++)
                {
                    TornadoReading reading = state.TornadoAt(i);
                    Funnel slot = Find(reading.Id);
                    if (slot == null)
                    {
                        continue;
                    }

                    slot.Body.transform.position = reading.Position.ToUnity();
                    // Core starts the harmless window before publishing TornadoEnded.
                    if (reading.Collapsing && slot.EndDuration == 0f)
                    {
                        BeginEnd(slot, collapsed: true);
                        // This frame first observes the collapse. Start at its initial pose.
                        slot.EndAge = -dt;
                    }
                }
            }

            foreach (Funnel slot in _pool)
            {
                if (slot == null || slot.Id == 0)
                {
                    continue;
                }

                slot.Age += dt;
                if (slot.EndDuration > 0f)
                {
                    slot.EndAge += dt;
                    if (slot.EndAge >= slot.EndDuration)
                    {
                        Return(slot);
                        continue;
                    }
                }

                Draw(slot);
            }
        }

        private void Draw(Funnel slot)
        {
            float height = Mathf.Clamp01(slot.Age / RiseSeconds);
            float alpha = height;
            float width = 1f;
            if (slot.EndDuration > 0f)
            {
                float remaining = 1f - Mathf.Clamp01(slot.EndAge / slot.EndDuration);
                height = slot.EndHeight;
                alpha = remaining;
                width = Mathf.Lerp(0.5f, 1f, remaining);
                if (slot.EndDuration == CollapseSeconds)
                {
                    height *= remaining;
                }
            }

            Color tint = slot.Colour;
            tint.a *= alpha;
            slot.Properties.SetColor(_baseColorId, tint);
            slot.Renderer.SetPropertyBlock(slot.Properties);

            // Three winding bands. Keep the bottom grains within a 0.56 m footprint.
            for (int i = 0; i < GrainCount; i++)
            {
                float level = (i / 3) / (GrainCount / 3f - 1f);
                float angle = (slot.Age * TurnsPerSecond + level * 1.65f + (i % 3) / 3f) * 2f * Mathf.PI;
                float radius = (0.16f + 0.59f * level * level) * width;
                _grains[i] = new ParticleSystem.Particle
                {
                    position = new Vector3(Mathf.Cos(angle) * radius, level * 2.35f * height, Mathf.Sin(angle) * radius),
                    startSize = Mathf.Lerp(0.20f, 0.37f, level) * width,
                    startColor = new Color32(255, 255, 255, (byte)Mathf.Lerp(230f, 130f, level)),
                    startLifetime = 1f,
                    remainingLifetime = 1f,
                    randomSeed = (uint)(i + 1),
                };
            }

            slot.Dust.SetParticles(_grains, GrainCount);
        }

        private void Return(Funnel slot)
        {
            if (slot.Id == 0)
            {
                return;
            }

            slot.Dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.Body.SetActive(false);
            slot.Id = 0;
            slot.Age = 0f;
            slot.EndAge = 0f;
            slot.EndDuration = 0f;
            slot.Ended = false;
            ShownCount--;
        }

        private void Clear()
        {
            foreach (Funnel slot in _pool)
            {
                if (slot != null && slot.Body != null)
                {
                    Return(slot);
                }
            }
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            _rose?.Dispose();
            _ended?.Dispose();
            _clear?.Dispose();
            _transition?.Dispose();
        }
    }
}
