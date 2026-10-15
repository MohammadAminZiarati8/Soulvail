using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs: a file-scoped namespace on a
// MonoBehaviour makes every scene reference to it deserialise as null (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>The home screen's tabs, in bar order.</summary>
    public enum Tab
    {
        Heroes,
        Play,
        Places,
    }

    /// <summary>
    /// Three equal tabs on the safe area's bottom edge, one selected (UI-11e rule 5). It raises
    /// <see cref="Selected"/> on a tap and decides nothing else: the screen that owns it says what a
    /// tab opens, and <see cref="Show"/> says which one is lit.
    /// </summary>
    /// <remarks>
    /// Until UI-11h's art lands a tab is <c>Button_Blue</c> and the selected tab is
    /// <c>Button_Green</c>. The bar is a child of the <c>Safe</c> object, so its bottom edge is the
    /// home bar's top.
    /// </remarks>
    public sealed class TabBar : MonoBehaviour
    {
        /// <summary>The bar's height, in dp.</summary>
        public const float HeightDp = 64f;

        public const float TextDp = 18f;

        private static readonly LocKey[] Keys =
        {
            new LocKey("ui.tab.heroes"),
            new LocKey("ui.tab.play"),
            new LocKey("ui.tab.places"),
        };

        [Tooltip("Heroes, Play, Places — in that order, left to right.")]
        [SerializeField] private Button[] _tabs = new Button[3];

        [SerializeField] private TMP_Text[] _labels = new TMP_Text[3];

        [SerializeField] private Sprite _blue;
        [SerializeField] private Sprite _bluePressed;
        [SerializeField] private Sprite _green;
        [SerializeField] private Sprite _greenPressed;

        private Tab _selected = Tab.Play;
        private bool _wired;

        /// <summary>Raised when the player taps a tab, whether or not it is the lit one.</summary>
        public event Action<Tab> Selected;

        /// <summary>The lit tab.</summary>
        public Tab Current => _selected;

        /// <summary>Lights <paramref name="tab"/> and puts the others out. Raises nothing.</summary>
        public void Show(Tab tab)
        {
            _selected = tab;
            Dress();
        }

        /// <summary>Wires the taps and writes the three labels.</summary>
        public void Configure(ILocalizer localizer)
        {
            if (!_wired)
            {
                _wired = true;

                for (int i = 0; i < _tabs.Length; i++)
                {
                    if (_tabs[i] != null)
                    {
                        Tab tab = (Tab)i;
                        _tabs[i].onClick.AddListener(() => Selected?.Invoke(tab));
                    }
                }
            }

            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i] != null)
                {
                    _labels[i].text = localizer is null ? Keys[i].ToString() : localizer.Get(Keys[i]);
                }
            }
        }

        /// <summary>
        /// Puts the bar on the bottom edge of its parent, <see cref="HeightDp"/> tall, with three equal
        /// tabs; <paramref name="px"/> canvas units to the dp.
        /// </summary>
        public void Layout(float px)
        {
            px = px > 0f ? px : 1f;

            var bar = (RectTransform)transform;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = new Vector2(0f, HeightDp * px);

            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                var rect = (RectTransform)_tabs[i].transform;
                rect.anchorMin = new Vector2(i / 3f, 0f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                if (i < _labels.Length && _labels[i] != null)
                {
                    RectTransform label = _labels[i].rectTransform;
                    label.anchorMin = Vector2.zero;
                    label.anchorMax = Vector2.one;
                    label.offsetMin = new Vector2(4f * px, 0f);
                    label.offsetMax = new Vector2(-4f * px, 0f);
                    _labels[i].fontSize = TextDp * px;
                    _labels[i].alignment = TextAlignmentOptions.Center;
                    _labels[i].enableAutoSizing = false;
                }
            }

            Dress();
        }

        private void Dress()
        {
            float height = ((RectTransform)transform).rect.height;

            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                bool lit = i == (int)_selected;
                Sprite face = lit ? _green : _blue;

                SpriteState sprites = _tabs[i].spriteState;
                sprites.pressedSprite = lit ? _greenPressed : _bluePressed;
                _tabs[i].spriteState = sprites;

                if (_tabs[i].targetGraphic is Image image)
                {
                    image.sprite = face;
                    image.color = Color.white;

                    if (face != null)
                    {
                        image.type = Image.Type.Sliced;
                        image.pixelsPerUnitMultiplier = height > 0f ? face.rect.height / height : 1f;
                    }
                }
            }
        }
    }
}
