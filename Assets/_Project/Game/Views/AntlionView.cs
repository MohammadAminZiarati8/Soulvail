using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Draws the antlion's phases and the eruption warning sent by core.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class AntlionView : MonoBehaviour
    {
        private const float DigRippleLead = 0.2f;

        [SerializeField] private Renderer[] _body;
        [SerializeField] private ParticleSystem _ripple;
        [SerializeField] private ParticleSystem _swirl;
        [SerializeField] private Renderer _mound;
        [SerializeField] private TrailRenderer _trail;
        [SerializeField] private ParticleSystem _burst;
        [SerializeField] private TelegraphRingView _ring;
        [SerializeField] private Renderer _ringRenderer;
        [SerializeField] private Transform _armEffects;
        [SerializeField] private Color _moundTint = new(0.44f, 0.35f, 0.22f);

        private EnemyView _enemy;
        private IDisposable _phaseSubscription;
        private IDisposable _ringSubscription;
        private IDisposable _deathSubscription;
        private AntlionPhase _phase;
        private int _id;
        private bool _dead;
        private float _untilRipple = float.PositiveInfinity;
        private float _fillRemaining;
        private Vector3 _ringCentre;
        private Vector3? _burstCentre;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            _enemy = GetComponent<EnemyView>();
            _phaseSubscription = events.Subscribe<AntlionPhaseChanged>(OnPhaseChanged);
            _ringSubscription = events.Subscribe<AntlionRingArmed>(OnRingArmed);
            _deathSubscription = events.Subscribe<EnemyDied>(OnDied);

            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", _moundTint);
            _mound.SetPropertyBlock(tint);
        }

        public void Bind(int enemyId)
        {
            _id = enemyId;
            _dead = false;
            _phase = AntlionPhase.Dig;
            SetUnderground(false);
            ClearEffects();
        }

        public void Step(float dt)
        {
            if (_dead || !(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }

            if (_burstCentre is Vector3 centre)
            {
                _burstCentre = null;
                _burst.transform.position = centre;
                _burst.Play(withChildren: false);
            }

            if (_phase == AntlionPhase.Dig && float.IsFinite(_untilRipple))
            {
                _untilRipple -= dt;
                if (_untilRipple <= 0f)
                {
                    _untilRipple = float.PositiveInfinity;
                    StartRipple();
                }
            }

            if (_ringRenderer.enabled)
            {
                PlaceArmEffects();
                float advance = Mathf.Min(dt, _fillRemaining);
                if (advance > 0f)
                {
                    _ring.Step(advance);
                    _fillRemaining -= advance;
                }
            }
        }

        // Core's movement intents have already moved the enemy by the time we pin the warning.
        private void LateUpdate() => Step(Time.deltaTime);

        private void OnDisable() => Bind(EnemyView.Unbound);

        private void OnDestroy()
        {
            _phaseSubscription?.Dispose();
            _ringSubscription?.Dispose();
            _deathSubscription?.Dispose();
        }

        // EnemyViews binds the EnemyView before core can publish the first Dig event.
        private bool IsMine(int id) => isActiveAndEnabled && id > EnemyView.Unbound &&
            id == (_id > EnemyView.Unbound ? _id : _enemy.Id);

        private void OnPhaseChanged(AntlionPhaseChanged evt)
        {
            if (_dead || !IsMine(evt.Id))
            {
                return;
            }

            bool wasArmed = _ringRenderer.enabled;
            _phase = evt.Phase;
            bool underground = _phase is AntlionPhase.Tunnel or AntlionPhase.Arm;
            SetUnderground(underground);

            if (underground)
            {
                _enemy.Feedback?.ResetVisuals();
                StartRipple();
                return;
            }

            StopRipple(clear: false);
            StopArmEffects();

            if (_phase == AntlionPhase.Dig)
            {
                _untilRipple = Mathf.Max(0f, evt.Seconds - DigRippleLead);
                if (_untilRipple == 0f)
                {
                    _untilRipple = float.PositiveInfinity;
                    StartRipple();
                }
            }
            else
            {
                _burstCentre = wasArmed ? _ringCentre : transform.position;
            }
        }

        private void OnRingArmed(AntlionRingArmed evt)
        {
            if (_dead || _phase != AntlionPhase.Arm || !IsMine(evt.Id))
            {
                return;
            }

            _ringCentre = evt.Centre.ToUnity();
            PlaceArmEffects();
            _ring.Bind(_ringCentre, evt.Radius, evt.Seconds, fills: true, Palette.Danger);
            // Keep the final frame of the fill until core announces the eruption.
            _fillRemaining = evt.Seconds * 0.9999f;
            _ringRenderer.enabled = true;
            _swirl.transform.localScale = Vector3.one * evt.Radius;
            _swirl.Play(withChildren: false);
        }

        private void PlaceArmEffects()
        {
            _armEffects.SetPositionAndRotation(_ringCentre, Quaternion.identity);
            // Enemy looks scale the whole prefab uniformly. The radius is already in world metres.
            _armEffects.localScale = Vector3.one / transform.lossyScale.x;
        }

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                _dead = true;
                ClearEffects();
            }
        }

        private void SetUnderground(bool underground)
        {
            foreach (Renderer renderer in _body)
            {
                renderer.enabled = !underground;
            }

            _enemy?.HealthBar?.SetHidden(underground);
        }

        private void StartRipple()
        {
            _mound.enabled = true;
            _trail.emitting = true;
            if (!_ripple.isPlaying)
            {
                _ripple.Play(withChildren: false);
            }
        }

        private void StopRipple(bool clear)
        {
            _untilRipple = float.PositiveInfinity;
            _mound.enabled = false;
            _trail.emitting = false;
            _ripple.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear :
                ParticleSystemStopBehavior.StopEmitting);
            if (clear)
            {
                _trail.Clear();
            }
        }

        private void StopArmEffects()
        {
            if (_ringRenderer.enabled)
            {
                _ringRenderer.enabled = false;
                _ring.OnDespawn();
            }

            _swirl.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ClearEffects()
        {
            _burstCentre = null;
            StopRipple(clear: true);
            StopArmEffects();
            _burst.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
