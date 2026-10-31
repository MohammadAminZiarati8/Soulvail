using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Soulvail.Game.Presentation
{
    /// <summary>A plain circle. Pointer handlers only record gestures, never send commands.</summary>
    public sealed class SkillArcButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Image _sweep;
        [SerializeField] private Image _border;
        [SerializeField] private TMP_Text _seconds;
        [SerializeField] private TMP_Text _auto;
        [SerializeField] private TMP_Text _lock;
        [SerializeField] private TMP_Text _pips;
        [SerializeField] private Sprite _placeholder;
        [SerializeField] private Sprite _padlock;
        [SerializeField] private Image _lockIcon;

        private SkillArcInput _input;
        private bool _interactive;
        private bool _held;
        private bool _longPressed;
        private float _pressedAt;
        private int _pointer;
        private int _shownSeconds = -1;
        private int _shownShots = -1;
        private int _shownEvery = -1;

        public ContentId Id { get; private set; }
        public bool IsLocked { get; private set; }
        public bool ReadyGlow => _border.color == Palette.Player;
        public float Sweep => _sweep.fillAmount;
        public string Seconds => _seconds.text;
        public string LevelLabel => _lock.text;
        public bool AutoBadge => _auto.gameObject.activeSelf;

        public void Bind(ContentId id, SkillArcInput input, bool interactive, Sprite icon, Color tint)
        {
            Id = id;
            _input = input;
            _interactive = interactive;
            _icon.sprite = icon != null ? icon : _placeholder;
            _icon.color = tint;
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            GetComponent<Image>().raycastTarget = interactive;
        }

        public void Draw(bool taken, int unlockLevel, int level, float fraction, float seconds,
            bool ready, bool auto, string levelText, Color tint, int shots = 0, int every = 0)
        {
            IsLocked = !taken;
            if (IsLocked)
            {
                _held = false;
            }

            _icon.color = taken ? tint : Color.gray;
            _sweep.fillAmount = taken ? Mathf.Clamp01(fraction) : 0f;
            int wholeSeconds = taken && fraction > 0f ? Mathf.CeilToInt(seconds) : 0;
            if (wholeSeconds != _shownSeconds)
            {
                _shownSeconds = wholeSeconds;
                _seconds.text = wholeSeconds > 0 ? wholeSeconds.ToString() : string.Empty;
            }
            _auto.gameObject.SetActive(taken && auto && _interactive);
            _lockIcon.sprite = _padlock;
            _lockIcon.gameObject.SetActive(!taken);
            _lock.text = !taken && unlockLevel > level ? levelText : string.Empty;
            _border.color = taken && ready && _interactive ? Palette.Player : new Color(0.35f, 0.38f, 0.4f);
            // Rebuild text only when the shot counter changes. Idle HUD frames allocate nothing.
            if (shots != _shownShots || every != _shownEvery)
            {
                _shownShots = shots;
                _shownEvery = every;
                int pips = Mathf.Clamp(every, 0, 8);
                _pips.text = pips > 0 ? new string('●', Mathf.Clamp(shots, 0, pips)) + new string('○', Mathf.Clamp(pips - shots, 0, pips)) : string.Empty;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactive || IsLocked || _held)
            {
                return;
            }

            _held = true;
            _longPressed = false;
            _pointer = eventData.pointerId;
            _pressedAt = Time.unscaledTime;
        }

        public void StepHold(float now)
        {
            if (_held && !_longPressed && now - _pressedAt >= SkillArcInput.LongPressSeconds)
            {
                _longPressed = true;
                _input?.Toggle(Id);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_held || eventData.pointerId != _pointer)
            {
                return;
            }

            StepHold(Time.unscaledTime);
            if (!_longPressed)
            {
                _input?.Tap(Id);
            }

            _held = false;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointer)
            {
                _held = false;
            }
        }

        private void OnDisable() => _held = false;
    }
}
