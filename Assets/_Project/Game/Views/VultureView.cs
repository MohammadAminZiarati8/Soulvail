using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Projects the vulture's flight phases onto its rig and ground shadow.</summary>
    [RequireComponent(typeof(EnemyView), typeof(CharacterController))]
    public sealed class VultureView : MonoBehaviour
    {
        public const float AloftHeight = 6f;

        private const float FadeSeconds = 0.2f;
        private const float ShadowOffset = 0.045f;
        private const float MinimumShadowScale = 0.25f;

        [SerializeField] private Renderer[] _body;
        [SerializeField] private Transform _model;
        [SerializeField] private Renderer _shadow;
        [SerializeField] private Material _climbFade;

        private readonly int _colorId = Shader.PropertyToID("_BaseColor");
        private EnemyView _enemy;
        private CharacterController _controller;
        private int _coverMask;
        private IDisposable _phaseSubscription;
        private IDisposable _lockSubscription;
        private IDisposable _deathSubscription;
        private Material[] _opaque;
        private Color[] _bodyTint;
        private MaterialPropertyBlock[] _bodyBlocks;
        private MaterialPropertyBlock _shadowBlock;
        private Color _shadowTint;
        private VulturePhase _phase;
        private int _id;
        private bool _dead;
        private bool _fading;
        private bool _hasLane;
        private float _seconds;
        private float _elapsed;
        private float _wingTime;
        private Vector3 _previousPosition;
        private Vector3 _origin;
        private Vector3 _landing;
        private Quaternion _shadowHeading;

        public bool PassesCover => _controller != null && (_controller.excludeLayers.value & _coverMask) != 0;

        // DomainEventHub is the run's subscribable event source.
        [Inject]
        public void Construct(DomainEventHub events)
        {
            _enemy = GetComponent<EnemyView>();
            _controller = GetComponent<CharacterController>();
            int cover = LayerMask.NameToLayer(ArenaView.CoverLayerName);
            _coverMask = cover < 0 ? 0 : 1 << cover;
            _opaque = new Material[_body.Length];
            _bodyTint = new Color[_body.Length];
            _bodyBlocks = new MaterialPropertyBlock[_body.Length];
            for (int i = 0; i < _body.Length; i++)
            {
                _opaque[i] = _body[i].sharedMaterial;
                _bodyBlocks[i] = new MaterialPropertyBlock();
            }

            _shadowBlock = new MaterialPropertyBlock();
            _shadowTint = _shadow.sharedMaterial.color;
            Bind(EnemyView.Unbound);
            _phaseSubscription = events.Subscribe<VulturePhaseChanged>(OnPhaseChanged);
            _lockSubscription = events.Subscribe<VultureDiveLocked>(OnDiveLocked);
            _deathSubscription = events.Subscribe<EnemyDied>(OnDied);
        }

        public void Bind(int enemyId)
        {
            _id = enemyId;
            _dead = false;
            _hasLane = false;
            _wingTime = 0f;
            _shadowHeading = transform.rotation;
            _previousPosition = transform.position;
            Enter(VulturePhase.Climb, 0f);
        }

        public void Step(float dt)
        {
            if (_enemy == null || _dead)
            {
                return;
            }

            if (_id == EnemyView.Unbound)
            {
                if (!_enemy.IsBound)
                {
                    return;
                }

                Bind(_enemy.Id);
            }

            if (dt > 0f && float.IsFinite(dt))
            {
                _elapsed += dt;
                _wingTime = Mathf.Repeat(_wingTime + dt, 1.4f);
            }

            Vector3 agent = transform.position;
            float t = _seconds > 0f ? Mathf.Clamp01(_elapsed / _seconds) : 0f;
            switch (_phase)
            {
                case VulturePhase.Climb:
                    float fade = _seconds > 0f ? Mathf.Clamp01((_seconds - _elapsed) / FadeSeconds) : 1f;
                    _model.position = agent + Vector3.up * (t * AloftHeight);
                    if (fade < 1f)
                    {
                        FadeBody(fade);
                        SetBody(fade > 0f);
                        DrawShadow(agent, 1f - fade, 1f);
                    }
                    break;
                case VulturePhase.Circle:
                    Vector3 motion = agent - _previousPosition;
                    motion.y = 0f;
                    if (motion.sqrMagnitude > 0.000001f)
                    {
                        _shadowHeading = Quaternion.LookRotation(motion);
                    }

                    DrawShadow(agent, 1f, 1f);
                    break;
                case VulturePhase.Lock:
                    DrawShadow(_hasLane ? _origin : agent, 1f, 1f);
                    break;
                case VulturePhase.Dive:
                    Vector3 basePosition = _hasLane ? Vector3.Lerp(_origin, _landing, t) : agent;
                    _model.position = basePosition + Vector3.up * ((1f - t) * AloftHeight);
                    DrawShadow(basePosition, 1f - t, Mathf.Lerp(1f, MinimumShadowScale, t));
                    break;
            }

            _previousPosition = agent;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void OnDisable()
        {
            if (_enemy != null)
            {
                Bind(EnemyView.Unbound);
            }
        }

        private void OnDestroy()
        {
            _phaseSubscription?.Dispose();
            _lockSubscription?.Dispose();
            _deathSubscription?.Dispose();
        }

        private bool IsMine(int id) => isActiveAndEnabled && id > EnemyView.Unbound &&
            id == (_id > EnemyView.Unbound ? _id : _enemy.Id);

        private void OnPhaseChanged(VulturePhaseChanged evt)
        {
            if (_dead || !IsMine(evt.Id))
            {
                return;
            }

            if (_id == EnemyView.Unbound)
            {
                Bind(evt.Id);
            }

            Enter(evt.Phase, evt.Seconds);
            Step(0f);
        }

        private void OnDiveLocked(VultureDiveLocked evt)
        {
            if (_dead || _phase != VulturePhase.Lock || !IsMine(evt.Id))
            {
                return;
            }

            _hasLane = true;
            _origin = evt.Origin.ToUnity();
            _landing = evt.Landing.ToUnity();
            _shadowHeading = Quaternion.LookRotation(new Vector3(evt.Direction.X, 0f, evt.Direction.Y));
            DrawShadow(_origin, 1f, 1f);
        }

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                _dead = true;
                _shadow.enabled = false;
                _enemy.HealthBar?.SetHidden(true);
            }
        }

        private void Enter(VulturePhase phase, float seconds)
        {
            if (_fading)
            {
                FadeBody(1f);
            }

            _phase = phase;
            _seconds = seconds;
            _elapsed = 0f;
            bool grounded = phase == VulturePhase.Grounded;
            bool shadowOnly = phase is VulturePhase.Circle or VulturePhase.Lock;
            int layers = _controller.excludeLayers.value;
            _controller.excludeLayers = grounded ? layers & ~_coverMask : layers | _coverMask;
            _enemy.HealthBar?.SetHidden(!grounded);
            if (phase == VulturePhase.Climb)
            {
                _enemy.Feedback?.ResetVisuals();
                _hasLane = false;
            }

            SetBody(!shadowOnly);
            _shadow.enabled = shadowOnly;
            _model.localPosition = Vector3.zero;
        }

        private void SetBody(bool shown)
        {
            foreach (Renderer renderer in _body)
            {
                renderer.enabled = shown;
            }
        }

        private void FadeBody(float alpha)
        {
            for (int i = 0; i < _body.Length; i++)
            {
                Renderer renderer = _body[i];
                MaterialPropertyBlock block = _bodyBlocks[i];
                renderer.GetPropertyBlock(block);
                if (!_fading)
                {
                    _bodyTint[i] = block.HasColor(_colorId) ? block.GetColor(_colorId) : _opaque[i].color;
                }

                Color tint = _bodyTint[i];
                tint.a *= alpha;
                block.SetColor(_colorId, tint);
                renderer.sharedMaterial = alpha < 1f ? _climbFade : _opaque[i];
                renderer.SetPropertyBlock(block);
            }

            _fading = alpha < 1f;
        }

        private void DrawShadow(Vector3 position, float opacity, float shrink)
        {
            _shadow.enabled = opacity > 0f;
            Transform decal = _shadow.transform;
            decal.SetPositionAndRotation(position + Vector3.up * ShadowOffset, _shadowHeading);
            float width = 0.92f + 0.08f * Mathf.Cos(_wingTime * (2f * Mathf.PI / 1.4f));
            float scale = shrink / transform.lossyScale.x;
            decal.localScale = new Vector3(width * scale, scale, scale);
            Color tint = _shadowTint;
            tint.a *= opacity;
            _shadowBlock.SetColor(_colorId, tint);
            _shadow.SetPropertyBlock(_shadowBlock);
        }
    }
}
