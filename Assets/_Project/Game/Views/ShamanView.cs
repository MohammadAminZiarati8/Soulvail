using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Draws core's support pulse. Pool deactivation clears every transient and subscription.</summary>
    public sealed class ShamanView : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _seed;
        [SerializeField] private Texture _seedMask;
        [SerializeField] private Transform _ring;
        [SerializeField] private float _castClipSeconds = 1f;
        [SerializeField] private float _baseMoveSpeed = 2f;
        /// <summary>The support lime, shared with the jackal priest's mend beam (SS-11c).</summary>
        public static readonly Color Lime = new Color(.75f, .9f, .38f, 1f);
        private MaterialPropertyBlock _seedBlock;
        private MaterialPropertyBlock _ringBlock;
        private DomainEventHub _hub;
        private EnemyView _body;
        private Renderer _ringRenderer;
        private IDisposable _cast;
        private IDisposable _pulse;
        private IDisposable _died;
        private int _boundId = EnemyView.Unbound;
        private float _castSeconds;
        private float _castElapsed;
        private float _ringElapsed;
        private float _radius;
        private bool _casting;
        private bool _pulsing;
        private bool _dead;
        public float SeedGlow { get; private set; }
        public int CastCount { get; private set; }
        private int Id => _boundId != EnemyView.Unbound ? _boundId : _body != null ? _body.Id : EnemyView.Unbound;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _seedBlock ??= new MaterialPropertyBlock();
            _ringBlock ??= new MaterialPropertyBlock();
            _body = GetComponent<EnemyView>();
            _ringRenderer = _ring != null ? _ring.GetComponent<Renderer>() : null;
            Unsubscribe();
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            Subscribe();
            ResetVisuals();
        }

        public void Bind(int enemyId, DomainEventHub hub)
        {
            Construct(hub);
            _boundId = enemyId;
        }

        public void Step(float dt)
        {
            if (!(dt > 0f) || !float.IsFinite(dt))
            { return; }
            if (_casting)
            {
                if (_body != null && _body.Velocity.magnitude > _baseMoveSpeed * 1.2f)
                {
                    StopCast();
                    if (_animator != null)
                    { _animator.CrossFade("Locomotion", .05f); }
                }
                else
                {
                    _castElapsed += dt;
                    Glow(Mathf.Clamp01(_castElapsed / _castSeconds));
                    if (_castElapsed > _castSeconds + .05f)
                    { StopCast(); }
                }
            }
            if (_pulsing)
            {
                _ringElapsed = Mathf.Min(.4f, _ringElapsed + dt);
                float t = _ringElapsed / .4f;
                if (_ring != null)
                { _ring.localScale = Vector3.one * (_radius * t); }
                if (_ringRenderer != null)
                {
                    Color colour = Lime;
                    colour.a = 1f - t;
                    _ringRenderer.GetPropertyBlock(_ringBlock);
                    _ringBlock.SetColor("_BaseColor", colour);
                    _ringRenderer.SetPropertyBlock(_ringBlock);
                }
                if (t >= 1f)
                { _pulsing = false; if (_ring != null) { _ring.gameObject.SetActive(false); } }
            }
        }

        private void Update() => Step(Time.deltaTime);
        public void OnSpawn() => Subscribe();
        public void OnDespawn() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnEnable() { Subscribe(); }
        private void OnDisable() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_hub == null || _cast != null)
            { return; }
            _cast = _hub.Subscribe<ShamanCast>(OnCast);
            _pulse = _hub.Subscribe<ShamanPulsed>(OnPulse);
            _died = _hub.Subscribe<EnemyDied>(OnDied);
        }

        private void Unsubscribe()
        {
            _cast?.Dispose();
            _pulse?.Dispose();
            _died?.Dispose();
            _cast = _pulse = _died = null;
        }

        private void OnCast(ShamanCast evt)
        {
            if (_dead || Id == EnemyView.Unbound || evt.ShamanId != Id)
            { return; }
            _casting = true;
            _castSeconds = Mathf.Max(.01f, evt.Seconds);
            _castElapsed = 0;
            CastCount++;
            if (_animator != null)
            {
                _animator.SetFloat("CastRate", _castClipSeconds / _castSeconds);
                _animator.ResetTrigger("Cast");
                _animator.SetTrigger("Cast");
            }
        }

        private void OnPulse(ShamanPulsed evt)
        {
            if (_dead || Id == EnemyView.Unbound || evt.ShamanId != Id)
            { return; }
            StopCast();
            _radius = evt.Radius;
            _ringElapsed = 0;
            _pulsing = true;
            if (_ring != null)
            {
                // Ring mesh has radius one. Compensate authored body scale to keep core metres.
                _ring.SetParent(null, true);
                _ring.position = new Vector3(evt.Position.X, evt.Position.Y + .035f, evt.Position.Z);
                _ring.rotation = Quaternion.identity;
                _ring.localScale = Vector3.zero;
                _ring.gameObject.SetActive(true);
            }
        }

        private void OnDied(EnemyDied evt)
        { if (evt.Id == Id) { ResetVisuals(); _dead = true; } }
        private void StopCast() { _casting = false; Glow(0f); }
        private void Glow(float fraction)
        {
            SeedGlow = fraction;
            if (_seed == null)
            { return; }
            _seedBlock ??= new MaterialPropertyBlock();
            _seed.GetPropertyBlock(_seedBlock);
            _seedBlock.SetTexture("_EmissionMap", _seedMask);
            _seedBlock.SetColor("_EmissionColor", Lime * (fraction * 0.4f));
            _seed.SetPropertyBlock(_seedBlock);
        }

        private void ResetVisuals()
        {
            StopCast();
            _pulsing = false;
            _dead = false;
            CastCount = 0;
            if (_ring != null)
            {
                _ring.gameObject.SetActive(false);
                _ring.SetParent(transform, false);
                _ring.localPosition = Vector3.up * .035f;
                _ring.localScale = Vector3.zero;
            }
            if (_animator != null && _animator.isActiveAndEnabled && _animator.isInitialized)
            { _animator.ResetTrigger("Cast"); }
        }
    }
}
