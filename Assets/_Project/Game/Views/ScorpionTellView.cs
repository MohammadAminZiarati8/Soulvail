using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Shows the scorpion's wind-up along the facing already locked by core.</summary>
    public sealed class ScorpionTellView : MonoBehaviour
    {
        private const float LaneGroundOffset = 0.075f;

        [SerializeField] private Renderer _tip;
        [SerializeField] private Renderer _strip;
        [SerializeField] private float _stripLength = 1.8f;
        [SerializeField] private float _stripHalfWidth = 0.5f;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private EnemyView _body;
        private MaterialPropertyBlock _block;
        private IDisposable _telegraphSubscription;
        private IDisposable _diedSubscription;
        private float _duration;
        private float _elapsed;

        public bool IsShowing { get; private set; }

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _telegraphSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _body = GetComponent<EnemyView>();
            _block ??= new MaterialPropertyBlock();
            _telegraphSubscription = hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
            Hide();
        }

        /// <summary>Advances the cosmetic timer independently of Unity's frame clock.</summary>
        public void Step(float dt)
        {
            if (!IsShowing || !(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }

            _elapsed += dt;
            Paint(_strip, Mathf.Lerp(0.3f, 0.9f, _elapsed / _duration));
            if (_elapsed >= _duration)
            {
                Hide();
            }
        }

        private void Update() => Step(Time.deltaTime);

        private bool IsMine(int id) => _body != null && _body.IsBound && _body.Id == id;

        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (!isActiveAndEnabled || !IsMine(evt.Id) || !(evt.Duration > 0f) ||
                !float.IsFinite(evt.Duration) || _tip == null || _strip == null)
            {
                return;
            }

            _duration = evt.Duration;
            _elapsed = 0f;
            Transform strip = _strip.transform;
            strip.localPosition = new Vector3(0f, LaneGroundOffset, _stripLength * 0.5f);
            strip.localScale = new Vector3(2f * _stripHalfWidth, _stripLength, 1f);
            Paint(_tip, 1f);
            Paint(_strip, 0.3f);
            _tip.enabled = _strip.enabled = IsShowing = true;
        }

        private void Paint(Renderer target, float alpha)
        {
            Color colour = Palette.Danger;
            colour.a = alpha;
            _block.SetColor(_baseColorId, colour);
            target.SetPropertyBlock(_block);
        }

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                Hide();
            }
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            _telegraphSubscription?.Dispose();
            _diedSubscription?.Dispose();
        }

        private void Hide()
        {
            IsShowing = false;
            if (_tip != null)
            {
                _tip.enabled = false;
            }
            if (_strip != null)
            {
                _strip.enabled = false;
            }
        }
    }
}
