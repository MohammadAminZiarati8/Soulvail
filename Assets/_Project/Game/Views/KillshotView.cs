using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>One reusable charge, oversized arrow and wind trail; world-space particle pools retain every hit.</summary>
    public sealed class KillshotView : MonoBehaviour, IDisposable
    {
        public const float BrightSeconds = 0.1f;
        public const float FadeSeconds = 0.4f;
        public const float AimLength = 30f;
        public const float ArrowFlightSeconds = 0.2f;
        [SerializeField] private Light _charge;
        [SerializeField] private Transform _chargeGlow;
        [SerializeField] private LineRenderer _aim;
        [SerializeField] private TrailRenderer _trail;
        [SerializeField] private Transform _arrow;
        [SerializeField] private ParticleSystem _windLines;
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private ParticleSystem _bursts;

        private Transform _bow;
        private IRunSession _session;
        private CameraShake _shake;
        private int _lastHitId = -1;
        private Vector3 _lastHitAt;
        private Vector3 _direction, _fireOrigin;
        private float _fireLength, _arrowTip, _arrowTail;
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
            Bounds bounds = _arrow.GetComponent<MeshFilter>().sharedMesh.bounds;
            _arrowTip = bounds.max.z * _arrow.localScale.z;
            _arrowTail = bounds.min.z * _arrow.localScale.z;
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
            // Core owns the XZ lane. The animated bow supplies only the cosmetic height.
            _fireOrigin = evt.Origin.ToUnity();
            _fireOrigin.y = _bow != null ? _bow.position.y : _fireOrigin.y + 0.7f;
            _trail.Clear();
            _windLines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _fireLength = evt.Length;
            _fireAge = 0f;
            _fired = true;
            DrawFlight(1f);
            _shake?.KickKillshot();
        }

        private void DrawFlight(float alpha)
        {
            float length = _arrowTip - _arrowTail;
            float head = Mathf.Lerp(Mathf.Min(length, _fireLength), _fireLength,
                Mathf.Clamp01(_fireAge / ArrowFlightSeconds));
            _arrow.gameObject.SetActive(_fireAge < ArrowFlightSeconds);
            _arrow.SetPositionAndRotation(_fireOrigin + _direction * (head - _arrowTip),
                Quaternion.LookRotation(_direction));
            float tail = Mathf.Max(0f, head - length);
            Vector3 at = _fireOrigin + _direction * tail;
            _trail.enabled = true;
            Color wind = Palette.Wind;
            _trail.startColor = new Color(wind.r, wind.g, wind.b, 0.18f * alpha);
            _trail.endColor = new Color(wind.r, wind.g, wind.b, 0f);
            // Automatic trail aging/emission uses wall time. Explicit positions and colour fading
            // keep this reusable, short path on the same pause-aware clock as the particles.
            if (_trail.positionCount == 0 ||
                Vector3.SqrMagnitude(_trail.GetPosition(_trail.positionCount - 1) - at) > 0.0001f)
            {
                _trail.AddPosition(at);
            }
            if (_fireAge > 0f && _fireAge <= ArrowFlightSeconds)
            {
                _windLines.Play();
                _windLines.Pause();
                Vector3 side = Vector3.Cross(Vector3.up, _direction);
                for (int i = 0; i < 4; i++)
                {
                    float offset = (i % 2 == 0 ? 1f : -1f) * (0.45f + i / 2 * 0.45f);
                    var particle = new ParticleSystem.EmitParams
                    {
                        position = _fireOrigin + _direction * Mathf.Max(0f, tail - 0.5f - i * 0.6f)
                            + side * offset,
                        velocity = -_direction * 8f,
                    };
                    _windLines.Emit(particle, 1);
                }
            }
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
            _windLines.Simulate(dt, true, false, false);
            if (_fired)
            {
                _fireAge += dt;
                float alpha = 1f - Mathf.Clamp01((_fireAge - BrightSeconds) / FadeSeconds);
                DrawFlight(alpha);
                if (_fireAge >= BrightSeconds + FadeSeconds)
                {
                    HideFlight();
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
            HideFlight();
            _sparks?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _bursts?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void HideFlight()
        {
            if (_arrow != null)
            {
                _arrow.gameObject.SetActive(false);
            }
            if (_trail != null)
            {
                _trail.Clear();
                _trail.enabled = false;
            }
            _windLines?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
