using System;
using Soulvail.Core.Events;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class GateFlashView : MonoBehaviour
    {
        [SerializeField] private float _outS = 0.45f;

        private Canvas _canvas;
        private Image _image;
        private IDisposable _transition;
        private IDisposable _arrived;
        private float _elapsed;
        private float _inS;
        private bool _opening;

        public string DescribeFaults() =>
            float.IsFinite(_outS) && _outS >= StageFlow.FadeTime
                ? string.Empty
                : $"Flash out time must be at least {StageFlow.FadeTime} seconds.";

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            string faults = DescribeFaults();
            if (faults.Length != 0)
            {
                throw new InvalidOperationException(faults);
            }

            var overlay = new GameObject("Gate Flash Overlay", typeof(RectTransform), typeof(Canvas));
            overlay.transform.SetParent(transform, false);
            _canvas = overlay.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 2;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(overlay.transform, false);
            _image = fill.GetComponent<Image>();
            _image.raycastTarget = false;
            RectTransform rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _canvas.enabled = false;

            _transition = hub.Subscribe<StageTransitionStarted>(OnTransition);
            _arrived = hub.Subscribe<StageArrived>(OnArrived);
        }

        private void OnDestroy()
        {
            _transition?.Dispose();
            _arrived?.Dispose();
        }

        private void OnTransition(StageTransitionStarted evt)
        {
            _elapsed = 0f;
            _inS = evt.Duration;
            _opening = false;
            _canvas.enabled = true;
            Step(0f);
        }

        private void OnArrived(StageArrived evt)
        {
            if (_canvas.enabled)
            {
                _elapsed = 0f;
                _opening = true;
                _image.color = Palette.HitFlash;
            }
        }

        private void Update()
        {
            Step(Time.unscaledDeltaTime);
        }

        private void Step(float dt)
        {
            if (_canvas == null || !_canvas.enabled)
            {
                return;
            }

            _elapsed += dt;
            if (!_opening)
            {
                float progress = _inS > 0f ? Mathf.Clamp01(_elapsed / _inS) : 1f;
                Color color = Color.Lerp(Palette.Player, Palette.HitFlash, progress);
                color.a = progress;
                _image.color = color;
                return;
            }

            // Cover the HUD's black fade until it has cleared, then spend the rest of the out beat fading.
            float alpha = _elapsed >= _outS ? 0f : 1f;
            if (_elapsed > StageFlow.FadeTime && _elapsed < _outS)
            {
                alpha = (_outS - _elapsed) / (_outS - StageFlow.FadeTime);
            }

            Color peak = Palette.HitFlash;
            peak.a = alpha;
            _image.color = peak;
            _canvas.enabled = alpha > 0f;
        }
    }
}
