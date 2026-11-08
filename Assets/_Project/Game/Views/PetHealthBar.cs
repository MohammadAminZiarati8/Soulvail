using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Game.Views
{
    public sealed class PetHealthBar : MonoBehaviour
    {
        /// <summary>BT-06b: the status a frenzied animal shows over its bar (attack up).</summary>
        public static readonly ContentId FrenziedStatus = new("status.pet.frenzied");

        [SerializeField, Min(0f)] private float _height = 1.45f;
        [SerializeField] private StatusIconBook _icons;

        private Camera _camera;
        private RectTransform _billboard;
        private RectTransform _fill;
        private Image _fillImage;
        private bool _pulsing;
        private Image _status;

        /// <summary>The attack-up icon over the bar; shown only while the animal is frenzied.</summary>
        public Image Status => _status;

        public void Bind(float fraction, Camera camera)
        {
            _camera = camera;
            if (_billboard == null)
            {
                Build();
            }

            SetVisible(true);
            SetFrenzied(false);
            SetFraction(fraction);
        }

        public void SetFraction(float fraction) =>
            _fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);

        public void SetVisible(bool visible) => _billboard.gameObject.SetActive(visible);

        /// <summary>Shows or hides the frenzy's attack-up icon (BT-06b rule 4).</summary>
        public void SetFrenzied(bool frenzied)
        {
            if (_billboard == null)
            {
                return;
            }

            if (frenzied && _status.sprite == null && _icons != null)
            {
                _status.sprite = _icons.For(FrenziedStatus);
            }

            _status.gameObject.SetActive(frenzied);
        }

        public void SetPulsing(bool pulsing)
        {
            _pulsing = pulsing;
            UpdatePulse(0f);
        }

        public void UpdatePulse(float seconds)
        {
            if (_fillImage == null)
            {
                return;
            }

            Color cyan = Palette.Player;
            if (_pulsing)
            {
                cyan.a = Mathf.Lerp(0.5f, 1f, Mathf.PingPong(seconds * 3f, 1f));
            }

            _fillImage.color = cyan;
        }

        private void LateUpdate()
        {
            UpdatePulse(Time.time);
            if (_billboard != null && _camera != null)
            {
                _billboard.rotation = _camera.transform.rotation;
            }
        }

        private void Build()
        {
            var billboard = new GameObject("Pet Health", typeof(RectTransform), typeof(Canvas));
            _billboard = (RectTransform)billboard.transform;
            _billboard.SetParent(transform, false);
            _billboard.localPosition = Vector3.up * _height;
            _billboard.localScale = Vector3.one * 0.01f;
            _billboard.sizeDelta = new Vector2(80f, 8f);
            billboard.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            AddImage("Track", new Color(0.08f, 0.1f, 0.12f));
            _fill = AddImage("Fill", Palette.Player);
            _fillImage = _fill.GetComponent<Image>();

            // One slot above the bar's left end: the pet wears a single buff, so no row to rank.
            var status = new GameObject("Status", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)status.transform;
            rect.SetParent(_billboard, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(22f, 22f);
            rect.anchoredPosition = new Vector2(0f, 3f);
            _status = status.GetComponent<Image>();
            _status.raycastTarget = false;
            _status.color = Color.white;
            status.SetActive(false);
        }

        private RectTransform AddImage(string name, Color colour)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)child.transform;
            rect.SetParent(_billboard, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = child.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return rect;
        }
    }
}
