using Soulvail.Core.Combat;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The player's statuses under the HP bar (MK-05): a rimmed icon each, a radial sweep when timed,
    /// a count when stacked. Placeholder HUD; the slots are built once and only ever re-dressed.
    /// </summary>
    public sealed class StatusRow : MonoBehaviour
    {
        /// <summary>A buff's rim: the player's cyan.</summary>
        public static readonly Color BuffRim = Palette.Player;

        /// <summary>A debuff's rim: pale violet, because red is the enemy's (colour law).</summary>
        public static readonly Color DebuffRim = new Color(0.78f, 0.71f, 0.99f, 1f);

        [SerializeField] private StatusIconBook _icons;
        [SerializeField] private Sprite _sweepSprite;

        private readonly Image[] _rims = new Image[StatusBoard.Capacity];
        private readonly Image[] _iconImages = new Image[StatusBoard.Capacity];
        private readonly Image[] _sweeps = new Image[StatusBoard.Capacity];
        private readonly TMP_Text[] _stacks = new TMP_Text[StatusBoard.Capacity];
        private readonly int[] _shownStacks = new int[StatusBoard.Capacity];
        private IRunSession _session;
        private Canvas _canvas;
        private bool _built;
        private bool _loggedRefusal;
        private float _lastPx;

        [Inject]
        public void Construct(IRunSession session)
        {
            _session = session;
        }

        public Image Rim(int i) => _rims[i];
        public Image Icon(int i) => _iconImages[i];
        public Image Sweep(int i) => _sweeps[i];
        public TMP_Text Stacks(int i) => _stacks[i];
        public bool IsShown(int i) => _built && _rims[i].gameObject.activeSelf;

        private void Update()
        {
            RunState state = _session?.State;
            if (state is null)
            {
                HideFrom(0);
                return;
            }

            if (state.StatusRefused && !_loggedRefusal)
            {
                _loggedRefusal = true;
                Debug.LogWarning("StatusRow: a ninth status was refused; eight is the design ceiling (MK-05).", this);
            }

            int count = state.StatusCount;
            for (int i = 0; i < count; i++)
            {
                Draw(i, state.StatusAt(i));
            }

            HideFrom(count);
        }

        /// <summary>Dresses slot <paramref name="index"/> for <paramref name="entry"/>.</summary>
        public void Draw(int index, in StatusEntry entry)
        {
            EnsureBuilt();
            Place();
            _rims[index].gameObject.SetActive(true);
            _rims[index].color = entry.IsBuff ? BuffRim : DebuffRim;

            Sprite icon = _icons != null ? _icons.For(entry.Id) : null;
            if (_iconImages[index].sprite != icon)
            {
                _iconImages[index].sprite = icon;
            }

            bool timed = entry.IsTimed;
            _sweeps[index].enabled = timed;
            _sweeps[index].fillAmount = timed ? entry.Fraction : 0f;

            int stacks = entry.Stacks > 1 ? entry.Stacks : 0;
            _stacks[index].enabled = stacks > 0;
            if (stacks > 0 && _shownStacks[index] != stacks)
            {
                _stacks[index].SetText("{0}", stacks);
            }

            _shownStacks[index] = stacks;
        }

        /// <summary>Hides every slot from <paramref name="count"/> on.</summary>
        public void HideFrom(int count)
        {
            if (!_built)
            {
                return;
            }

            for (int i = count; i < StatusBoard.Capacity; i++)
            {
                if (_rims[i].gameObject.activeSelf)
                {
                    _rims[i].gameObject.SetActive(false);
                }
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _canvas = GetComponentInParent<Canvas>();
            for (int i = 0; i < StatusBoard.Capacity; i++)
            {
                _rims[i] = NewImage("Status" + i, transform);
                _iconImages[i] = NewImage("Icon", _rims[i].transform);
                _iconImages[i].color = Color.white;
                Inset(_iconImages[i].rectTransform, 0.1f);

                _sweeps[i] = NewImage("Sweep", _rims[i].transform);
                _sweeps[i].sprite = _sweepSprite;
                _sweeps[i].color = new Color(Palette.Ink.r, Palette.Ink.g, Palette.Ink.b, 0.55f);
                _sweeps[i].type = Image.Type.Filled;
                _sweeps[i].fillMethod = Image.FillMethod.Radial360;
                _sweeps[i].fillOrigin = (int)Image.Origin360.Top;
                _sweeps[i].fillClockwise = false;
                Inset(_sweeps[i].rectTransform, 0.1f);

                var text = new GameObject("Stacks", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                text.transform.SetParent(_rims[i].transform, false);
                text.alignment = TextAlignmentOptions.BottomRight;
                text.color = Palette.HudText;
                text.fontStyle = FontStyles.Bold;
                text.raycastTarget = false;
                text.enableAutoSizing = false;
                Inset(text.rectTransform, 0f);
                _stacks[i] = text;
                _shownStacks[i] = -1;

                _rims[i].gameObject.SetActive(false);
            }
        }

        private void Place()
        {
            float px = PixelsPerDp();
            if (Mathf.Approximately(px, _lastPx))
            {
                return;
            }

            _lastPx = px;
            for (int i = 0; i < StatusBoard.Capacity; i++)
            {
                RectTransform rect = _rims[i].rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = Vector2.one * (HudLayout.StatusSizeDp * px);
                rect.anchoredPosition = new Vector2(HudLayout.StatusLeftDp(i), -HudLayout.SkillRowTopDp) * px;
                _stacks[i].fontSize = HudLayout.StatusSizeDp * 0.55f * px;
            }
        }

        private static Image NewImage(string name, Transform parent)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.raycastTarget = false;
            return image;
        }

        private static void Inset(RectTransform rect, float fraction)
        {
            rect.anchorMin = Vector2.one * fraction;
            rect.anchorMax = Vector2.one * (1f - fraction);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private float PixelsPerDp()
        {
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }
    }
}
