using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>One reusable charge and streak; world-space particle pools retain every hit.</summary>
    public sealed class KillshotView : MonoBehaviour, IDisposable
    {
        public const float BrightSeconds = 0.1f;
        public const float FadeSeconds = 0.4f;
        public const float AimLength = 30f;
        [SerializeField] private Light _charge;
        [SerializeField] private Transform _chargeGlow;
        [SerializeField] private LineRenderer _aim;
        [SerializeField] private LineRenderer _streak;
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private ParticleSystem _bursts;

        private readonly Color _cyanWhite = new Color(0.55f, 1f, 1f, 1f);
        private Transform _bow;
        private IRunSession _session;
        private CameraShake _shake;
        private int _lastHitId = -1;
        private Vector3 _lastHitAt;
        private Vector3 _direction;
        private float _windSeconds, _windAge, _fireAge, _clock;
        private bool _fired;
        private IDisposable _wind, _fire, _hit, _execute, _died, _boundary, _arrived, _ended;

        public bool IsWindingUp { get; private set; }

        public void Construct(DomainEventHub hub, IRunSession session, Transform bow, CameraShake shake)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }
            _session = session;
            _bow = bow;
            _shake = shake;
            _clock = session?.State?.Time ?? 0f;
            Clear();
            _wind = hub.Subscribe<KillshotWindUp>(OnWindUp);
            _fire = hub.Subscribe<KillshotFired>(OnFire);
            _hit = hub.Subscribe<KillshotHit>(OnHit);
            _execute = hub.Subscribe<KillshotExecuted>(OnExecute);
            _died = hub.Subscribe<PlayerDied>(_ => Clear());
            _boundary = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _arrived = hub.Subscribe<StageArrived>(_ => Clear());
            _ended = hub.Subscribe<RunEnded>(_ => Clear());
        }

        private Vector3 ArrowTip => _bow != null
            ? _bow.position + _direction * 0.25f
            : transform.position + Vector3.up * 0.7f;

        private void OnWindUp(KillshotWindUp evt)
        {
            Clear();
            _direction = evt.Direction.ToUnity();
            _windSeconds = evt.Seconds;
            _windAge = 0f;
            IsWindingUp = true;
            DrawCharge();
        }

        private void DrawCharge()
        {
            Vector3 tip = ArrowTip;
            _charge.enabled = true;
            _charge.transform.position = tip;
            _chargeGlow.gameObject.SetActive(true);
            _chargeGlow.position = tip;
            float u = Mathf.Clamp01(_windAge / _windSeconds);
            _chargeGlow.localScale = Vector3.one * Mathf.Lerp(0.06f, 0.18f, u);
            _charge.intensity = Mathf.Lerp(0.3f, 2.5f, u);
            _charge.range = Mathf.Lerp(0.5f, 1.3f, u);
            _aim.enabled = true;
            _aim.SetPosition(0, tip);
            _aim.SetPosition(1, tip + _direction * AimLength);
        }

        private void OnHit(KillshotHit evt)
        {
            _lastHitId = evt.EnemyId;
            _lastHitAt = evt.At.ToUnity();
            Emit(_sparks, evt.At.ToUnity() + Vector3.up * 0.65f, 6);
        }

        private void OnExecute(KillshotExecuted evt)
        {
            // Core publishes Hit immediately before Execute, while the victim still exists.
            if (_lastHitId == evt.EnemyId)
            {
                Emit(_bursts, _lastHitAt + Vector3.up * 0.65f, 18);
            }
        }

        private void OnFire(KillshotFired evt)
        {
            IsWindingUp = false;
            _charge.enabled = false;
            _chargeGlow.gameObject.SetActive(false);
            _aim.enabled = false;
            _direction = evt.Direction.ToUnity();
            Vector3 tip = _bow != null ? ArrowTip : evt.Origin.ToUnity() + Vector3.up * 0.7f;
            _streak.SetPosition(0, tip);
            _streak.SetPosition(1, tip + _direction * evt.Length);
            _streak.enabled = true;
            _streak.startColor = _streak.endColor = _cyanWhite;
            _fireAge = 0f;
            _fired = true;
            _shake?.KickKillshot();
        }

        private void Emit(ParticleSystem pool, Vector3 at, int count)
        {
            // EmitParams on a stopped system emits nothing in this Unity version. Initialise,
            // then pause: Step alone advances the shared pools on the run's cosmetic clock.
            pool.Play();
            pool.Pause();
            var particle = new ParticleSystem.EmitParams { position = at };
            float speed = pool.main.startSpeed.constant;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                particle.velocity = new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * speed;
                pool.Emit(particle, 1);
            }
        }

        public void Step(float dt)
        {
            if (!(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }
            if (IsWindingUp)
            {
                _windAge += dt;
                // Fire/cancel facts own the lifetime, rather than a second wind-up timer.
                DrawCharge();
            }
            if (_fired)
            {
                _fireAge += dt;
                float alpha = 1f - Mathf.Clamp01((_fireAge - BrightSeconds) / FadeSeconds);
                Color colour = _cyanWhite;
                colour.a = alpha;
                _streak.startColor = _streak.endColor = colour;
                if (_fireAge >= BrightSeconds + FadeSeconds)
                {
                    _streak.enabled = false;
                    _fired = false;
                }
            }
            _sparks.Simulate(dt, true, false, false);
            _bursts.Simulate(dt, true, false, false);
        }

        public void Clear()
        {
            IsWindingUp = false;
            _fired = false;
            _lastHitId = -1;
            if (_charge != null)
            {
                _charge.enabled = false;
            }
            if (_chargeGlow != null)
            {
                _chargeGlow.gameObject.SetActive(false);
            }
            if (_aim != null)
            {
                _aim.enabled = false;
            }
            if (_streak != null)
            {
                _streak.enabled = false;
            }
            _sparks?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _bursts?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void LateUpdate()
        {
            float now = _session?.State?.Time ?? _clock;
            Step(now - _clock);
            _clock = now;
        }

        private void OnDisable() => Clear();
        private void OnDestroy() => Dispose();

        public void Dispose()
        {
            _wind?.Dispose();
            _fire?.Dispose();
            _hit?.Dispose();
            _execute?.Dispose();
            _died?.Dispose();
            _boundary?.Dispose();
            _arrived?.Dispose();
            _ended?.Dispose();
            Clear();
        }
    }
}
