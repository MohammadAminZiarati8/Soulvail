using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Presentation
{
    public sealed class StageCardView : MonoBehaviour
    {
        private const float FadeInStart = 0.45f;
        private const float FadeInEnd = 0.65f;
        private const float FadeOutStart = 2.15f;
        private const float FadeOutEnd = 2.5f;
        private const float FontSizeDp = 96f;

        private static readonly LocKey StageKey = new LocKey("ui.hud.stage");

        [SerializeField] private TMP_Text _text;

        private IDisposable _arrivedSubscription;
        private IDisposable _endedSubscription;
        private ILocalizer _localizer;
        private Canvas _canvas;
        private float _elapsed;

        public bool Showing { get; private set; }
        public float Alpha => _text.alpha;

        [Inject]
        public void Construct(DomainEventHub hub, ILocalizer localizer)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _canvas = GetComponentInParent<Canvas>();
            _text.outlineColor = Palette.Player;
            _text.outlineWidth = 0.2f;
            Hide();

            _arrivedSubscription?.Dispose();
            _endedSubscription?.Dispose();
            _arrivedSubscription = hub.Subscribe<StageArrived>(OnStageArrived);
            _endedSubscription = hub.Subscribe<RunEnded>(OnRunEnded);
        }

        public void Advance(float unscaledDt)
        {
            if (!Showing || !(unscaledDt > 0f))
            {
                return;
            }

            _elapsed += unscaledDt;
            if (_elapsed >= FadeOutEnd)
            {
                Hide();
                return;
            }

            _text.alpha = _elapsed < FadeOutStart
                ? Mathf.InverseLerp(FadeInStart, FadeInEnd, _elapsed)
                : 1f - Mathf.InverseLerp(FadeOutStart, FadeOutEnd, _elapsed);
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        private void OnStageArrived(StageArrived evt)
        {
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _text.fontSize = FontSizeDp * StickShaper.PixelsPerDp(Screen.dpi) / scale;
            _text.SetText(_localizer.Get(StageKey), evt.Stage);
            _elapsed = 0f;
            _text.alpha = 0f;
            _text.enabled = true;
            Showing = true;
        }

        private void OnRunEnded(RunEnded evt) => Hide();

        private void Hide()
        {
            Showing = false;
            _text.alpha = 0f;
            _text.enabled = false;
        }

        private void OnDestroy()
        {
            _arrivedSubscription?.Dispose();
            _endedSubscription?.Dispose();
        }
    }
}
