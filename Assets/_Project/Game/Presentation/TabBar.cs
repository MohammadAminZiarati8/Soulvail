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
    /// Three tabs between the end caps of a stone plate, one lit (UI-11e rule 5, UI-11h-a). It raises
    /// <see cref="Selected"/> on a tap and decides nothing else: the screen that owns it says what a
    /// tab opens, and <see cref="Show"/> says which one is lit.
    /// </summary>
    /// <remarks>
    /// The bar is a child of the <c>Safe</c> object, so its bottom edge is the home bar's top. Its own
    /// <c>Image</c> wears <c>TabBar_Plate</c>, sliced so the caps keep their proportion; a tab is a tile
    /// <c>Image</c> 44 dp tall with its label under it, and its tap area is still the whole column
    /// (<see cref="Graphic.raycastPadding"/> on the tile). Without a plate the tiles stand alone.
    /// </remarks>
    public sealed class TabBar : MonoBehaviour
    {
        /// <summary>The bar's height, in dp.</summary>
        public const float HeightDp = 72f;

        /// <summary>The label's size, under the tile.</summary>
        public const float TextDp = 12f;

        /// <summary>The tile's height, glow included.</summary>
        public const float IconDp = 44f;

        private const float LabelDp = 18f;
        private const float PressedGrey = 0.8f;

        private static readonly LocKey[] Keys =
        {
            new LocKey("ui.tab.heroes"),
            new LocKey("ui.tab.play"),
            new LocKey("ui.tab.places"),
        };

        [Tooltip("Heroes, Play, Places — in that order, left to right. Each button's target graphic is its tile.")]
        [SerializeField] private Button[] _tabs = new Button[3];

        [SerializeField] private TMP_Text[] _labels = new TMP_Text[3];

        [Tooltip("The bar's own Image, wearing TabBar_Plate.")]
        [SerializeField] private Image _plate;

        [Tooltip("Off tiles: Heroes, Play, Places.")]
        [SerializeField] private Sprite[] _icons = new Sprite[3];

        [Tooltip("Lit tiles, in the same order.")]
        [SerializeField] private Sprite[] _iconsOn = new Sprite[3];

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
        /// Puts the bar on the bottom edge of its parent, <see cref="HeightDp"/> tall, with three tiles
        /// between its caps; <paramref name="px"/> canvas units to the dp.
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

            float barHeight = HeightDp * px;
            float cap = CapWidth(barHeight);
            float column = (bar.rect.width - 2f * cap) / 3f;
            float labelHeight = LabelDp * px;
            float tileBottom = labelHeight + 4f * px;

            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                Sprite icon = Pick(_icons, i);
                float tileHeight = IconDp * px;
                float tileWidth = icon != null ? tileHeight * icon.rect.width / icon.rect.height : tileHeight;

                var rect = (RectTransform)_tabs[i].transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(tileWidth, tileHeight);
                rect.anchoredPosition = new Vector2(cap + (i + 0.5f) * column, tileBottom);

                // The tile is the tap graphic; its padding grows it back to the whole column.
                if (_tabs[i].targetGraphic is Graphic tile)
                {
                    float side = (column - tileWidth) * 0.5f;
                    tile.raycastPadding = new Vector4(-side, -tileBottom, -side, -(barHeight - tileBottom - tileHeight));
                }

                if (i < _labels.Length && _labels[i] != null)
                {
                    RectTransform label = _labels[i].rectTransform;
                    label.anchorMin = label.anchorMax = new Vector2(0.5f, 0f);
                    label.pivot = new Vector2(0.5f, 1f);
                    label.sizeDelta = new Vector2(column, labelHeight);
                    label.anchoredPosition = Vector2.zero;
                    _labels[i].fontSize = TextDp * px;
                    _labels[i].alignment = TextAlignmentOptions.Center;
                    _labels[i].enableAutoSizing = false;
                    _labels[i].textWrappingMode = TextWrappingModes.NoWrap;
                    _labels[i].raycastTarget = false;
                }
            }

            Dress();
        }

        /// <summary>A cap's width: the plate's border at the bar's height, 0 without a plate.</summary>
        private float CapWidth(float barHeight)
        {
            Sprite plate = _plate != null ? _plate.sprite : null;

            return plate != null ? plate.border.x * barHeight / plate.rect.height : 0f;
        }

        private static Sprite Pick(Sprite[] sprites, int index) => sprites != null && index < sprites.Length ? sprites[index] : null;

        private void Dress()
        {
            if (_plate != null && _plate.sprite != null)
            {
                float height = ((RectTransform)transform).rect.height;

                _plate.type = Image.Type.Sliced;
                _plate.color = Color.white;
                _plate.raycastTarget = false;
                _plate.pixelsPerUnitMultiplier = height > 0f ? _plate.sprite.rect.height / height : 1f;
            }

            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                bool lit = i == (int)_selected;

                // A press tints the tile; there is no pressed sprite.
                _tabs[i].transition = Selectable.Transition.ColorTint;

                ColorBlock colors = _tabs[i].colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(PressedGrey, PressedGrey, PressedGrey, 1f);
                _tabs[i].colors = colors;

                if (_tabs[i].targetGraphic is Image image)
                {
                    image.sprite = Pick(lit ? _iconsOn : _icons, i);
                    image.color = Color.white;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                }

                if (i < _labels.Length && _labels[i] != null)
                {
                    _labels[i].color = lit ? Palette.HudText : Palette.Neutral;
                }
            }
        }
    }
}
