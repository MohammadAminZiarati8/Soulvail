using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soulvail.Game.Views
{
    public sealed class StampedeView : MonoBehaviour
    {
        private const int Capacity = 12;
        private const float StripSeconds = 0.3f;
        private const float FadeSeconds = 0.18f;
        private const float DustInterval = 0.06f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private readonly Transform[] _boars = new Transform[Capacity];
        private readonly float[] _nextDust = new float[Capacity];
        private MaterialPropertyBlock _fade;
        private Material _boarMaterial;
        private Renderer[][] _skins;
        private ParticleSystem _dust;
        private LineRenderer _strip;
        private Func<float> _time;
        private Func<int, Vector3?> _enemyPosition;
        private CameraShake _shake;
        private IDisposable[] _subscriptions;
        private Vector3 _from, _direction, _lastDeathAt;
        private int _count, _lastDeathId = -1;
        private float _length, _startedAt, _lastStep;

        public void Bind(DomainEventHub hub, GameObject boarPrefab, ParticleSystem dustPrefab,
            Material stripMaterial, Func<float> time, Func<int, Vector3?> enemyPosition, CameraShake shake)
        {
            _fade = new MaterialPropertyBlock();
            _time = time;
            _enemyPosition = enemyPosition;
            _shake = shake;
            _lastStep = time();

            var pool = new GameObject("Boar pool");
            pool.SetActive(false);
            pool.transform.SetParent(transform, false);
            _skins = new Renderer[Capacity][];
            for (int i = 0; i < Capacity; i++)
            {
                GameObject boar = Instantiate(boarPrefab, pool.transform, false);
                boar.SetActive(false);
                _boars[i] = boar.transform;
                _skins[i] = boar.GetComponentsInChildren<Renderer>(true);
            }
            pool.SetActive(true);

            _dust = Instantiate(dustPrefab, transform, false);
            var main = _dust.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            Color dustColor = Palette.Player;
            dustColor.a = 0.45f;
            main.startColor = dustColor;
            main.startLifetime = 0.28f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.38f);
            main.maxParticles = 256;

            var strip = new GameObject("Stampede strip");
            strip.transform.SetParent(transform, false);
            strip.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _strip = strip.AddComponent<LineRenderer>();
            _strip.alignment = LineAlignment.TransformZ;
            _strip.sharedMaterial = stripMaterial;
            _strip.shadowCastingMode = ShadowCastingMode.Off;
            _strip.receiveShadows = false;
            _strip.positionCount = 2;
            _strip.enabled = false;

            _subscriptions = new[]
            {
                hub.Subscribe<StampedeStarted>(OnStart),
                hub.Subscribe<BoarHit>(OnHit),
                hub.Subscribe<StampedeEnded>(_ => ReturnBoars()),
                hub.Subscribe<EnemyDied>(OnDeath),
                hub.Subscribe<StageTransitionStarted>(_ => Clear()),
                hub.Subscribe<StageArrived>(_ => Clear()),
                hub.Subscribe<RunEnded>(_ => Clear()),
                hub.Subscribe<PlayerDied>(_ => Clear()),
            };
        }

        private void OnStart(StampedeStarted evt)
        {
            ReturnBoars();
            _from = evt.From.ToUnity();
            _direction = evt.Direction.ToUnity();
            _length = evt.Length;
            _count = evt.Boars;
            _startedAt = _time();
            Vector3 lifted = _from + Vector3.up * 0.045f;
            _strip.SetPosition(0, lifted);
            _strip.SetPosition(1, lifted + _direction * _length);
            _strip.widthMultiplier = evt.HalfWidth * 2f;
            Color color = Palette.Player;
            color.a = 0.42f;
            _strip.startColor = color;
            _strip.endColor = color;
            _strip.enabled = true;
            _shake?.Kick(0.12f);
            Step(_startedAt);
        }

        public void Step(float now)
        {
            float dt = now - _lastStep;
            _lastStep = now;
            if (dt > 0f)
            {
                _dust.Simulate(dt, true, false, false);
            }

            float elapsed = now - _startedAt;
            if (elapsed >= StripSeconds)
            {
                _strip.enabled = false;
            }
            float duration = _length / StampedeSystem.BoarSpeed;
            for (int i = 0; i < _count; i++)
            {
                float age = elapsed - i * StampedeSystem.Spacing;
                if (age < 0f || age >= duration)
                {
                    _boars[i].gameObject.SetActive(false);
                    continue;
                }

                Vector3 at = _from + _direction * (age * StampedeSystem.BoarSpeed);
                _boars[i].SetPositionAndRotation(at, Quaternion.LookRotation(_direction));
                _boars[i].gameObject.SetActive(true);
                SetOpacity(i, Mathf.Clamp01((duration - age) / FadeSeconds));
                if (age >= _nextDust[i])
                {
                    _nextDust[i] = age + DustInterval;
                    EmitDust(at - _direction * 0.45f, 2);
                }
            }
        }

        private void SetOpacity(int index, float opacity)
        {
            foreach (Renderer skin in _skins[index])
            {
                if (opacity < 1f && _boarMaterial == null)
                {
                    _boarMaterial = new Material(skin.sharedMaterial);
                    _boarMaterial.SetFloat("_Surface", 1f);
                    _boarMaterial.SetOverrideTag("RenderType", "Transparent");
                    _boarMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    _boarMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    _boarMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    _boarMaterial.SetInt("_ZWrite", 0);
                    _boarMaterial.renderQueue = (int)RenderQueue.Transparent;
                    _boarMaterial.SetShaderPassEnabled("ShadowCaster", false);
                }
                if (_boarMaterial == null)
                {
                    continue;
                }

                skin.sharedMaterial = _boarMaterial;
                Color color = _boarMaterial.GetColor(BaseColor);
                color.a = opacity;
                _fade.SetColor(BaseColor, color);
                skin.SetPropertyBlock(_fade);
            }
        }

        private void OnDeath(EnemyDied evt)
        {
            _lastDeathId = evt.Id;
            _lastDeathAt = evt.Position.ToUnity();
        }

        private void OnHit(BoarHit evt)
        {
            if (_count == 0)
            {
                return;
            }

            Vector3? at = evt.EnemyId == _lastDeathId ? _lastDeathAt : _enemyPosition(evt.EnemyId);
            if (at.HasValue)
            {
                EmitDust(at.Value, 7);
            }
        }

        private void EmitDust(Vector3 at, int count)
        {
            _dust.Play();
            _dust.Pause();
            var particle = new ParticleSystem.EmitParams { position = at + Vector3.up * 0.1f };
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                particle.velocity = new Vector3(Mathf.Cos(angle), 0.5f, Mathf.Sin(angle)) * 1.4f;
                _dust.Emit(particle, 1);
            }
        }

        private void LateUpdate()
        {
            if (_time != null)
            {
                Step(_time());
            }
        }

        private void ReturnBoars()
        {
            for (int i = 0; i < _count; i++)
            {
                _boars[i].gameObject.SetActive(false);
                _nextDust[i] = 0f;
            }
            _count = 0;
            _strip.enabled = false;
        }

        private void Clear()
        {
            ReturnBoars();
            _lastDeathId = -1;
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            if (_subscriptions != null)
            {
                foreach (IDisposable subscription in _subscriptions)
                {
                    subscription.Dispose();
                }
            }
            if (_boarMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_boarMaterial);
                }
                else
                {
                    DestroyImmediate(_boarMaterial);
                }
            }
        }
    }
}
