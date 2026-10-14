using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see TreeNodeButton.cs and Traps §5.
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The card under the tree's block (UI-04d): the selected node's name on a ribbon, its tile and
    /// icon, "Level n of m", its description, and the Learn button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It draws what it is handed and decides nothing.</b> The presenter works out whether a point
    /// can be spent and says so through <c>canLearn</c>; a tap on Learn is reported through the
    /// callback given to <see cref="Bind"/>, and the presenter asks core again before it sends
    /// <c>TakeNode</c>.
    /// </para>
    /// <para>
    /// <b>Its pieces are placed by <see cref="Layout"/> in dp</b>, <c>TreeBlockView</c>'s reason: the
    /// prefab's canvas measures in reference pixels. The card's own rect is the presenter's.
    /// </para>
    /// </remarks>
    public sealed class TreeDetailCard : MonoBehaviour
    {
        /// <summary>The height <see cref="Layout"/> is built for, in dp; the presenter sizes the card to it.</summary>
        public const float HeightDp = 176f;

        private const float PadDp = 10f;
        private const float RibbonWidthDp = 220f;
        private const float RibbonHeightDp = 40f;
        private const float RibbonTextInsetDp = 24f;
        private const float RibbonTextDp = 16f;
        private const float TileDp = 56f;
        private const float BodyGapDp = 10f;
        private const float LevelTextDp = 14f;
        private const float LevelHeightDp = 18f;
        private const float DescriptionTextDp = 14f;
        private const float LearnWidthDp = 168f;
        private const float LearnHeightDp = 48f;
        private const float LearnTextDp = 18f;
        private const float LearnIconDp = 22f;
        private const float LearnCountWidthDp = 18f;
        private const float LearnPadDp = 22f;
        private const float HintTextDp = 16f;

        private const float CardBorderPx = 80f;
        private const float CardBorderDp = 14f;
        private const float RibbonBorderPx = 200f;
        private const float RibbonSpriteHeightPx = 267f;
        private const float ButtonBorderPx = 130f;
        private const float ButtonSpriteHeightPx = 348f;

        private static readonly LocKey LearnKey = new LocKey("ui.tree.learn");
        private static readonly LocKey LearnedKey = new LocKey("ui.tree.learned");
        private static readonly LocKey LevelKey = new LocKey("ui.tree.level");
        private static readonly LocKey PickKey = new LocKey("ui.tree.pick");

        [Tooltip("The stone card behind everything: a sliced Card_Stone.")]
        [SerializeField] private Image _background;

        [Tooltip("The sliced Popup_TitleRibbon the name is written on.")]
        [SerializeField] private RectTransform _ribbon;

        [Tooltip("The node's name and tier numeral, on the ribbon.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The node's tile, plated by state like the tree's own.")]
        [SerializeField] private Image _tile;

        [Tooltip("The node's icon, centred on the tile.")]
        [SerializeField] private Image _icon;

        [Tooltip("\"Level n of m\".")]
        [SerializeField] private TMP_Text _level;

        [Tooltip("The node's description.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("The line shown when nothing is selected: \"Tap a skill to see it\".")]
        [SerializeField] private TMP_Text _hint;

        [Tooltip("Learn. Green and interactable while a point can take the node; grey otherwise.")]
        [SerializeField] private Button _learn;

        [Tooltip("The Learn button's face, swapped between green and grey.")]
        [SerializeField] private Image _learnFace;

        [Tooltip("\"Learn\" or \"Learned\".")]
        [SerializeField] private TMP_Text _learnLabel;

        [Tooltip("Icon_SkillPoint, beside the cost.")]
        [SerializeField] private Image _costIcon;

        [Tooltip("The cost: \"1\".")]
        [SerializeField] private TMP_Text _costCount;

        [SerializeField] private Sprite _takenTile;
        [SerializeField] private Sprite _availableTile;
        [SerializeField] private Sprite _lockedTile;
        [SerializeField] private Sprite _greenButton;
        [SerializeField] private Sprite _greenPressed;
        [SerializeField] private Sprite _greyButton;

        private Action _onLearn;

        /// <summary>Whether the card is showing the hint rather than a node.</summary>
        public bool IsHint { get; private set; } = true;

        /// <summary>The title as written: the name and tier numeral, or empty on the hint.</summary>
        public string TitleText => _title != null ? _title.text : null;

        /// <summary>The "Level n of m" line.</summary>
        public string LevelText => _level != null ? _level.text : null;

        /// <summary>The description line.</summary>
        public string DescriptionText => _description != null ? _description.text : null;

        /// <summary>The hint line.</summary>
        public string HintText => _hint != null ? _hint.text : null;

        /// <summary>Whether a Learn button is on the card at all — false on the hint and in a drawing run.</summary>
        public bool LearnShown => _learn != null && _learn.gameObject.activeSelf;

        /// <summary>Whether Learn would spend a point.</summary>
        public bool LearnInteractable => _learn != null && _learn.interactable;

        /// <summary>The Learn button's face: <c>Button_Green</c> or <c>Button_Grey</c>.</summary>
        public Sprite LearnSprite => _learnFace != null ? _learnFace.sprite : null;

        /// <summary>"Learn" or "Learned".</summary>
        public string LearnText => _learnLabel != null ? _learnLabel.text : null;

        /// <summary>The tile the card draws, by name.</summary>
        public Sprite TileSprite => _tile != null ? _tile.sprite : null;

        /// <summary>Hands the card the presenter's Learn handler. Called once.</summary>
        public void Bind(Action learn)
        {
            _onLearn = learn;

            if (_learn != null)
            {
                _learn.onClick.RemoveListener(OnLearn);
                _learn.onClick.AddListener(OnLearn);
            }
        }

        /// <summary>Describes one node.</summary>
        /// <param name="spec">The selected node.</param>
        /// <param name="icon">Its icon, or the block's placeholder.</param>
        /// <param name="state">What it is to this run.</param>
        /// <param name="tier">Its tier in its branch, from 1.</param>
        /// <param name="tiers">How many tiers its branch has.</param>
        /// <param name="canLearn">Whether a point is banked and this is a free run.</param>
        /// <param name="localizer">What the keys say.</param>
        /// <param name="free">Whether this run has a Learn button at all; a drawing run's has none.</param>
        public void Show(
            SkillSpec spec,
            Sprite icon,
            NodeState state,
            int tier,
            int tiers,
            bool canLearn,
            ILocalizer localizer,
            bool free = true)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            IsHint = false;

            SetActive(_hint, false);
            SetActive(_ribbon, true);
            SetActive(_tile, true);
            SetActive(_icon, true);
            SetActive(_level, true);
            SetActive(_description, true);

            Write(_title, $"{localizer.Get(spec.NameKey)} {TreeNodeButton.NumeralFor(tier)}");
            Write(_level, localizer.Format(LevelKey, tier, tiers));
            Write(_description, localizer.Get(spec.DescriptionKey));

            if (_tile != null)
            {
                _tile.sprite = state switch
                {
                    NodeState.Taken => _takenTile,
                    NodeState.Available => _availableTile,
                    _ => _lockedTile,
                };
            }

            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.color = state is NodeState.Locked or NodeState.Banished ? Palette.NodeLocked : Color.white;
            }

            bool learnable = free && state == NodeState.Available && canLearn;
            bool taken = state == NodeState.Taken;

            SetActive(_learn, free);
            DressLearn(learnable, free && taken);

            Write(_learnLabel, localizer.Get(free && taken ? LearnedKey : LearnKey));
        }

        /// <summary>Shows "Tap a skill to see it" and nothing else (UI-04d rule 2).</summary>
        public void ShowHint(ILocalizer localizer)
        {
            IsHint = true;

            SetActive(_hint, true);
            SetActive(_ribbon, false);
            SetActive(_tile, false);
            SetActive(_icon, false);
            SetActive(_level, false);
            SetActive(_description, false);
            SetActive(_learn, false);

            Write(_hint, localizer is null ? PickKey.ToString() : localizer.Get(PickKey));
        }

        /// <summary>
        /// Sizes and places every piece inside the card from its current rect, in canvas units
        /// <paramref name="px"/> to the dp.
        /// </summary>
        public void Layout(float px)
        {
            float pad = PadDp * px;
            float width = transform is RectTransform self ? self.rect.width : 0f;

            if (!float.IsFinite(width) || width <= 0f)
            {
                width = 331f * px;
            }

            Border(_background, CardBorderPx, CardBorderDp * px);

            float ribbonWidth = Mathf.Min(RibbonWidthDp * px, width - (2f * pad));
            float ribbonHeight = RibbonHeightDp * px;

            if (_ribbon != null)
            {
                Anchor(_ribbon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(ribbonWidth, ribbonHeight), new Vector2(0f, -pad));
                Border(_ribbon.GetComponent<Image>(), RibbonBorderPx, ribbonHeight * RibbonBorderPx / RibbonSpriteHeightPx);
            }

            if (_title != null)
            {
                Stretch(_title.rectTransform, RibbonTextInsetDp * px, 0f, RibbonTextInsetDp * px, 0f);
                _title.fontSize = RibbonTextDp * px;
                _title.alignment = TextAlignmentOptions.Center;
                _title.overflowMode = TextOverflowModes.Ellipsis;
            }

            float bodyTop = -(pad + ribbonHeight + (6f * px));
            float tile = TileDp * px;

            if (_tile != null)
            {
                Anchor(_tile.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(tile, tile), new Vector2(pad, bodyTop));
            }

            if (_icon != null)
            {
                Anchor(_icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(tile * 0.62f, tile * 0.62f), Vector2.zero);
            }

            float textLeft = pad + tile + (BodyGapDp * px);
            float textWidth = Mathf.Max(0f, width - textLeft - pad);
            float levelHeight = LevelHeightDp * px;

            if (_level != null)
            {
                Anchor(_level.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textWidth, levelHeight), new Vector2(textLeft, bodyTop));
                _level.fontSize = LevelTextDp * px;
                _level.alignment = TextAlignmentOptions.TopLeft;
            }

            if (_description != null)
            {
                Anchor(_description.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textWidth, tile - levelHeight), new Vector2(textLeft, bodyTop - levelHeight));
                _description.fontSize = DescriptionTextDp * px;
                _description.alignment = TextAlignmentOptions.TopLeft;
                _description.overflowMode = TextOverflowModes.Ellipsis;
                _description.textWrappingMode = TextWrappingModes.Normal;
            }

            if (_hint != null)
            {
                Stretch(_hint.rectTransform, pad, pad, pad, pad);
                _hint.fontSize = HintTextDp * px;
                _hint.alignment = TextAlignmentOptions.Center;
            }

            if (_learn != null && _learn.transform is RectTransform learn)
            {
                float learnHeight = LearnHeightDp * px;

                Anchor(learn, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(LearnWidthDp * px, learnHeight), new Vector2(0f, pad));
                Border(_learnFace, ButtonBorderPx, learnHeight * ButtonBorderPx / ButtonSpriteHeightPx);
            }

            float icon = LearnIconDp * px;
            float count = LearnCountWidthDp * px;
            float inset = LearnPadDp * px;

            if (_costCount != null)
            {
                Anchor(_costCount.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(count, LearnHeightDp * px), new Vector2(-inset, 0f));
                _costCount.fontSize = LearnTextDp * px;
                _costCount.alignment = TextAlignmentOptions.Center;
            }

            if (_costIcon != null)
            {
                Anchor(_costIcon.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(icon, icon), new Vector2(-(inset + count), 0f));
            }

            if (_learnLabel != null)
            {
                Stretch(_learnLabel.rectTransform, inset, 0f, inset + count + icon, 0f);
                _learnLabel.fontSize = LearnTextDp * px;
                _learnLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        private void OnDestroy()
        {
            _onLearn = null;

            if (_learn != null)
            {
                _learn.onClick.RemoveListener(OnLearn);
            }
        }

        /// <summary>
        /// Green and live, or grey and dead (rule 4). <c>onClick.Invoke</c> ignores
        /// <c>interactable</c>, so the callback's own owner asks core again.
        /// </summary>
        private void DressLearn(bool learnable, bool learned)
        {
            if (_learn != null)
            {
                _learn.interactable = learnable;

                SpriteState sprites = _learn.spriteState;

                sprites.pressedSprite = learnable ? _greenPressed : null;
                _learn.spriteState = sprites;
            }

            if (_learnFace != null)
            {
                _learnFace.sprite = learnable ? _greenButton : _greyButton;
            }

            // The cost reads on every Learn but "Learned", where there is nothing left to pay.
            SetActive(_costIcon, !learned);
            SetActive(_costCount, !learned);
            Write(_costCount, "1");
        }

        private void OnLearn() => _onLearn?.Invoke();

        private static void Write(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetActive(Component piece, bool active)
        {
            if (piece != null)
            {
                piece.gameObject.SetActive(active);
            }
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Border(Image image, float borderPx, float wanted)
        {
            if (image != null && wanted > 0f)
            {
                image.pixelsPerUnitMultiplier = borderPx / wanted;
            }
        }
    }
}
