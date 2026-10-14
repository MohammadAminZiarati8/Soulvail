using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and ClassSelect.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>What the page's one button is: green, violet, or grey and dead.</summary>
    public enum PageButton
    {
        /// <summary>Owned: starts the run.</summary>
        Choose,

        /// <summary>Locked and the price can be paid: buys.</summary>
        Unlock,

        /// <summary>Locked and the price cannot be paid: drawn, refused.</summary>
        Dead,
    }

    /// <summary>How a class fights, as the badge under its name says it. Follows <c>WeaponKind</c>.</summary>
    public enum RoleKind
    {
        Ranged,
        Melee,
        Caster,
    }

    /// <summary>
    /// Everything the page draws about one class, decided by the presenter (UI-05c). A struct so a
    /// step between classes builds nothing on the heap.
    /// </summary>
    public readonly struct ClassPageModel
    {
        public ClassPageModel(
            CharacterSpec spec, int index, int count, PageButton button, int price, string deedLine, RoleKind role)
        {
            Spec = spec;
            Index = index;
            Count = count;
            Button = button;
            Price = price;
            DeedLine = deedLine;
            Role = role;
        }

        public CharacterSpec Spec { get; }

        public int Index { get; }

        public int Count { get; }

        public PageButton Button { get; }

        /// <summary>The Shard price, drawn in the pill for a locked class; 0 for an owned one.</summary>
        public int Price { get; }

        /// <summary>The line under the button for a locked class with a deed, or null.</summary>
        public string DeedLine { get; }

        public RoleKind Role { get; }
    }

    /// <summary>
    /// The class-select screen's one page: a class's name and role, its 3D body, the arrows and dots
    /// that move between classes, and the one button (UI-05c).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A dumb view.</b> The presenter owns the index, the roster and every decision; this is told
    /// what to draw and reports taps, steps and turns. It never asks the presenter anything.
    /// </para>
    /// <para>
    /// <b>One gesture is never two.</b> A drag that starts on the preview only turns the model; one
    /// that starts on the name band only steps, once it passes <see cref="SwipeDp"/>. Which one is
    /// decided where the press landed, at the start of the drag.
    /// </para>
    /// <para>
    /// <b>It lays itself out in dp</b> (<see cref="Layout"/>), the way <c>TreeDetailCard</c> does, so
    /// the page fits 9:20 and every other aspect from anchors rather than from authored pixels.
    /// </para>
    /// </remarks>
    public sealed class CharacterPage : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>How far a drag on the name band must travel before it steps, in dp.</summary>
        public const float SwipeDp = 48f;

        /// <summary>Degrees the model turns per dragged pixel.</summary>
        public const float DegreesPerPixel = 0.5f;

        private const float NameBandDp = 84f;
        private const float NameDp = 28f;
        private const float RoleDp = 14f;
        private const float RoleIconDp = 24f;
        private const float LockBadgeDp = 40f;
        private const float ArrowDp = 56f;
        private const float EdgeDp = 12f;
        private const float DotDp = 14f;
        private const float DotGapDp = 8f;
        private const float DotsRowDp = 20f;
        private const float ButtonWidthDp = 168f;
        private const float ButtonHeightDp = 60f;
        private const float ButtonTextDp = 22f;
        private const float PillWidthDp = 112f;
        private const float PillHeightDp = 44f;
        private const float PillIconDp = 28f;
        private const float PriceTextDp = 18f;
        private const float RowGapDp = 8f;
        private const float DeedDp = 14f;
        private const float DeedHeightDp = 22f;
        private const float BottomDp = 16f;
        private const float ButtonBorderPx = 130f;
        private const float ButtonSpriteHeightPx = 348f;
        private const float PillBorderPx = 136f;
        private const float PillSpriteHeightPx = 273f;

        private static readonly LocKey ChooseKey = new LocKey("ui.classselect.choose");
        private static readonly LocKey UnlockKey = new LocKey("ui.classselect.unlock");
        private static readonly LocKey RangedKey = new LocKey("ui.classselect.role.ranged");
        private static readonly LocKey MeleeKey = new LocKey("ui.classselect.role.melee");
        private static readonly LocKey CasterKey = new LocKey("ui.classselect.role.caster");

        [Header("Header")]
        [SerializeField] private RectTransform _nameBand;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private Image _lockBadge;
        [SerializeField] private Image _roleBadge;
        [SerializeField] private TMP_Text _roleLabel;

        [Tooltip("Only the Ranger's badge is imported. A role with no sprite draws no badge.")]
        [SerializeField] private Sprite _rangedSprite;
        [SerializeField] private Sprite _meleeSprite;
        [SerializeField] private Sprite _casterSprite;

        [Header("Body")]
        [SerializeField] private RawImage _preview;
        [SerializeField] private Button _previous;
        [SerializeField] private Button _next;

        [Header("Dots")]
        [SerializeField] private RectTransform _dots;
        [SerializeField] private Image _dotTemplate;
        [SerializeField] private Sprite _dotOn;
        [SerializeField] private Sprite _dotOff;

        [Header("Button")]
        [SerializeField] private Button _primary;
        [SerializeField] private Image _primaryFace;
        [SerializeField] private TMP_Text _primaryLabel;
        [SerializeField] private Sprite _greenButton;
        [SerializeField] private Sprite _greenPressed;
        [SerializeField] private Sprite _violetButton;
        [SerializeField] private Sprite _violetPressed;
        [SerializeField] private Sprite _greyButton;
        [SerializeField] private RectTransform _pill;
        [SerializeField] private Image _pillIcon;
        [SerializeField] private TMP_Text _price;
        [SerializeField] private TMP_Text _deed;

        private readonly List<Image> _dotImages = new List<Image>();

        private Action _onPrevious;
        private Action _onNext;
        private Action _onPrimary;
        private Action<float> _onRotate;
        private float _px = 1f;
        private float _buttonY;
        private bool _pillShown;
        private DragMode _drag;

        private enum DragMode
        {
            None,
            Rotate,
            Swipe,
        }

        /// <summary>The image the 3D body is drawn into.</summary>
        public RawImage Preview => _preview;

        /// <summary>The page's button, for the presenter to switch off while a load is in flight.</summary>
        public Button Primary => _primary;

        /// <summary>Whether the previous arrow is on screen.</summary>
        public bool PreviousShown => _previous != null && _previous.gameObject.activeSelf;

        /// <summary>Whether the next arrow is on screen.</summary>
        public bool NextShown => _next != null && _next.gameObject.activeSelf;

        /// <summary>How many page dots are on screen.</summary>
        public int DotsShown
        {
            get
            {
                int shown = 0;

                for (int i = 0; i < _dotImages.Count; i++)
                {
                    if (_dotImages[i] != null && _dotImages[i].gameObject.activeSelf)
                    {
                        shown++;
                    }
                }

                return shown;
            }
        }

        /// <summary>The state the button was last drawn in.</summary>
        public PageButton ButtonState { get; private set; }

        /// <summary>The swipe distance in device pixels, from <see cref="SwipeDp"/> and the dpi.</summary>
        public static float SwipePixels => SwipeDp * StickShaper.PixelsPerDp(Screen.dpi);

        /// <summary>
        /// Wires the page's taps and gestures. Called again on a reopen: the previous binding is
        /// dropped first, so a tap is reported once.
        /// </summary>
        /// <param name="rotate">Degrees to turn the model by, from a drag on the preview. Optional.</param>
        public void Bind(Action previous, Action next, Action primary, Action<float> rotate = null)
        {
            _onPrevious = previous;
            _onNext = next;
            _onPrimary = primary;
            _onRotate = rotate;

            Wire(_previous, OnPrevious);
            Wire(_next, OnNext);
            Wire(_primary, OnPrimary);
        }

        /// <summary>Draws one class: name, role, arrows, dots, badge, button and price.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
        public void Show(ClassPageModel model, ILocalizer localizer)
        {
            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            gameObject.SetActive(true);

            if (_name != null)
            {
                _name.text = localizer.Get(model.Spec.NameKey);
            }

            DrawRole(model.Role, localizer);

            SetActive(_lockBadge, model.Button != PageButton.Choose);

            bool several = model.Count > 1;

            SetActive(_previous, several && model.Index > 0);
            SetActive(_next, several && model.Index < model.Count - 1);

            DrawDots(model.Index, several ? model.Count : 0);
            DrawButton(model, localizer);
        }

        /// <summary>Takes the page off the screen. Nothing is bound or drawn.</summary>
        public void Clear()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Sizes and places every piece from the page's current rect, <paramref name="px"/> canvas
        /// units to the dp (UI-05c rule 9). Text is never below 12 sp and the button is never below
        /// 48 dp tall.
        /// </summary>
        public void Layout(float px)
        {
            _px = px > 0f ? px : 1f;
            px = _px;

            float bottom = BottomDp * px;
            float deedHeight = DeedHeightDp * px;
            float buttonHeight = ButtonHeightDp * px;
            float rowGap = RowGapDp * px;
            float dotsHeight = DotsRowDp * px;

            // Bottom up: deed line, button row, dots, then the preview takes what is left.
            float deedY = bottom;
            float buttonY = deedY + deedHeight + rowGap;
            float dotsY = buttonY + Mathf.Max(buttonHeight, PillHeightDp * px) + rowGap;
            float previewY = dotsY + dotsHeight + rowGap;
            float bandHeight = NameBandDp * px;
            float edge = EdgeDp * px;
            float arrow = ArrowDp * px;

            if (_nameBand != null)
            {
                Anchor(_nameBand, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, bandHeight), new Vector2(0f, 0f), new Vector2(0.5f, 1f));
                _nameBand.sizeDelta = new Vector2(-2f * edge, bandHeight);
            }

            if (_name != null)
            {
                RectTransform rect = _name.rectTransform;
                Anchor(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, NameDp * px * 1.3f), new Vector2(0f, 0f), new Vector2(0.5f, 1f));
                rect.sizeDelta = new Vector2(-2f * LockBadgeDp * px, NameDp * px * 1.3f);
                _name.fontSize = NameDp * px;
                _name.alignment = TextAlignmentOptions.Center;
                _name.enableAutoSizing = false;
                _name.overflowMode = TextOverflowModes.Ellipsis;
            }

            if (_lockBadge != null)
            {
                float badge = LockBadgeDp * px;
                Anchor(_lockBadge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(badge, badge), new Vector2(0f, 0f), new Vector2(0f, 1f));
            }

            float roleHeight = RoleIconDp * px;
            float roleWidth = 160f * px;

            if (_roleLabel != null)
            {
                Anchor(_roleLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(roleWidth, roleHeight), new Vector2(RoleIconDp * 0.5f * px, -(NameDp * px * 1.3f) - (4f * px)), new Vector2(0.5f, 1f));
                _roleLabel.fontSize = RoleDp * px;
                _roleLabel.alignment = TextAlignmentOptions.Center;
                _roleLabel.enableAutoSizing = false;
            }

            if (_roleBadge != null)
            {
                Anchor(_roleBadge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(roleHeight, roleHeight), new Vector2(-roleWidth * 0.25f, -(NameDp * px * 1.3f) - (4f * px)), new Vector2(0.5f, 1f));
            }

            if (_preview != null)
            {
                RectTransform rect = _preview.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(edge + arrow, previewY);
                rect.offsetMax = new Vector2(-(edge + arrow), -(bandHeight + rowGap));
            }

            PlaceArrow(_previous, true, edge, arrow, previewY, bandHeight + rowGap);
            PlaceArrow(_next, false, edge, arrow, previewY, bandHeight + rowGap);

            if (_dots != null)
            {
                Anchor(_dots, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, dotsHeight), new Vector2(0f, dotsY), new Vector2(0.5f, 0f));
                _dots.sizeDelta = new Vector2(0f, dotsHeight);
            }

            if (_deed != null)
            {
                Anchor(_deed.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, deedHeight), new Vector2(0f, deedY), new Vector2(0.5f, 0f));
                _deed.rectTransform.sizeDelta = new Vector2(-2f * edge, deedHeight);
                _deed.fontSize = DeedDp * px;
                _deed.alignment = TextAlignmentOptions.Center;
                _deed.enableAutoSizing = false;
            }

            if (_primary != null && _primary.transform is RectTransform button)
            {
                button.anchorMin = new Vector2(0.5f, 0f);
                button.anchorMax = new Vector2(0.5f, 0f);
                button.pivot = new Vector2(0.5f, 0f);
                button.sizeDelta = new Vector2(ButtonWidthDp * px, buttonHeight);
                Border(_primaryFace, ButtonBorderPx, buttonHeight * ButtonBorderPx / ButtonSpriteHeightPx);
            }

            if (_primaryLabel != null)
            {
                Stretch(_primaryLabel.rectTransform, 8f * px);
                _primaryLabel.fontSize = ButtonTextDp * px;
                _primaryLabel.alignment = TextAlignmentOptions.Center;
                _primaryLabel.enableAutoSizing = false;
            }

            if (_pill != null)
            {
                _pill.anchorMin = new Vector2(0.5f, 0f);
                _pill.anchorMax = new Vector2(0.5f, 0f);
                _pill.pivot = new Vector2(0.5f, 0f);
                _pill.sizeDelta = new Vector2(PillWidthDp * px, PillHeightDp * px);
                Border(_pill.GetComponent<Image>(), PillBorderPx, PillHeightDp * px * PillBorderPx / PillSpriteHeightPx);
            }

            if (_pillIcon != null)
            {
                float icon = PillIconDp * px;
                Anchor(_pillIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(icon, icon), new Vector2(10f * px, 0f), new Vector2(0f, 0.5f));
            }

            if (_price != null)
            {
                RectTransform rect = _price.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2((PillIconDp + 14f) * px, 0f);
                rect.offsetMax = new Vector2(-8f * px, 0f);
                _price.fontSize = PriceTextDp * px;
                _price.alignment = TextAlignmentOptions.Center;
                _price.enableAutoSizing = false;
            }

            _buttonY = buttonY;

            PlaceButtonRow();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            GameObject pressed = eventData.pointerPressRaycast.gameObject != null
                ? eventData.pointerPressRaycast.gameObject
                : eventData.pointerPress;

            if (pressed != null && _preview != null && pressed.transform.IsChildOf(_preview.transform))
            {
                _drag = DragMode.Rotate;
            }
            else if (pressed != null && _nameBand != null && pressed.transform.IsChildOf(_nameBand))
            {
                _drag = DragMode.Swipe;
            }
            else
            {
                _drag = DragMode.None;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_drag == DragMode.Rotate)
            {
                // Dragging right turns the front of the model to the right.
                _onRotate?.Invoke(-eventData.delta.x * DegreesPerPixel);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DragMode mode = _drag;
            _drag = DragMode.None;

            if (mode != DragMode.Swipe)
            {
                return;
            }

            float travelled = eventData.position.x - eventData.pressPosition.x;

            if (Mathf.Abs(travelled) < SwipePixels)
            {
                return;
            }

            // A swipe left pulls the next page in, as a page turns.
            if (travelled < 0f)
            {
                _onNext?.Invoke();
            }
            else
            {
                _onPrevious?.Invoke();
            }
        }

        private void OnDestroy()
        {
            Unwire(_previous, OnPrevious);
            Unwire(_next, OnNext);
            Unwire(_primary, OnPrimary);
        }

        private void OnPrevious() => _onPrevious?.Invoke();

        private void OnNext() => _onNext?.Invoke();

        private void OnPrimary() => _onPrimary?.Invoke();

        private void DrawRole(RoleKind role, ILocalizer localizer)
        {
            LocKey key = role switch
            {
                RoleKind.Melee => MeleeKey,
                RoleKind.Caster => CasterKey,
                _ => RangedKey,
            };

            Sprite sprite = role switch
            {
                RoleKind.Melee => _meleeSprite,
                RoleKind.Caster => _casterSprite,
                _ => _rangedSprite,
            };

            if (_roleLabel != null)
            {
                _roleLabel.text = localizer.Get(key);
            }

            if (_roleBadge != null)
            {
                // A role whose sprite is not imported yet draws nothing rather than a blank square.
                _roleBadge.sprite = sprite;
                _roleBadge.gameObject.SetActive(sprite != null);
            }
        }

        private void DrawDots(int index, int count)
        {
            // Grown on demand and kept: a roster is a list, so the dot count is not a capacity.
            while (_dotImages.Count < count && _dotTemplate != null && _dots != null)
            {
                Image dot = Instantiate(_dotTemplate, _dots);
                dot.name = "Dot" + _dotImages.Count;
                _dotImages.Add(dot);
            }

            float size = DotDp * _px;
            float gap = DotGapDp * _px;
            float start = -((count - 1) * (size + gap)) * 0.5f;

            for (int i = 0; i < _dotImages.Count; i++)
            {
                Image dot = _dotImages[i];

                if (dot == null)
                {
                    continue;
                }

                bool shown = i < count;
                dot.gameObject.SetActive(shown);

                if (!shown)
                {
                    continue;
                }

                dot.sprite = i == index ? _dotOn : _dotOff;

                RectTransform rect = dot.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(start + (i * (size + gap)), 0f);
            }
        }

        private void DrawButton(ClassPageModel model, ILocalizer localizer)
        {
            ButtonState = model.Button;

            bool choose = model.Button == PageButton.Choose;
            bool live = model.Button != PageButton.Dead;

            if (_primary != null)
            {
                _primary.interactable = live;

                SpriteState sprites = _primary.spriteState;
                sprites.pressedSprite = choose ? _greenPressed : model.Button == PageButton.Unlock ? _violetPressed : null;
                _primary.spriteState = sprites;
            }

            if (_primaryFace != null)
            {
                _primaryFace.sprite = choose ? _greenButton : model.Button == PageButton.Unlock ? _violetButton : _greyButton;
            }

            if (_primaryLabel != null)
            {
                _primaryLabel.text = localizer.Get(choose ? ChooseKey : UnlockKey);
            }

            // The price reads on every locked class, payable or not: a dead button still says what
            // it would cost (rule 6).
            _pillShown = !choose && model.Price > 0;

            SetActive(_pill, _pillShown);

            if (_price != null)
            {
                _price.text = model.Price.ToString();
                _price.color = live ? Palette.Essence : Palette.Neutral;
            }

            bool deed = !choose && !string.IsNullOrEmpty(model.DeedLine);

            if (_deed != null)
            {
                _deed.gameObject.SetActive(deed);
                _deed.text = deed ? model.DeedLine : string.Empty;
            }

            PlaceButtonRow();
        }

        /// <summary>
        /// The button alone is centred; beside the pill the pair is, so nothing shifts under the
        /// player's thumb more than it must.
        /// </summary>
        private void PlaceButtonRow()
        {
            float gap = RowGapDp * _px;
            float buttonWidth = ButtonWidthDp * _px;
            float pillWidth = PillWidthDp * _px;
            float total = _pillShown ? buttonWidth + gap + pillWidth : buttonWidth;
            float left = -total * 0.5f;

            if (_primary != null && _primary.transform is RectTransform button)
            {
                button.anchoredPosition = new Vector2(left + (buttonWidth * 0.5f), _buttonY);
            }

            if (_pill != null)
            {
                float pillHeight = PillHeightDp * _px;
                float buttonHeight = ButtonHeightDp * _px;

                _pill.anchoredPosition = new Vector2(left + buttonWidth + gap + (pillWidth * 0.5f), _buttonY + ((buttonHeight - pillHeight) * 0.5f));
            }
        }

        private static void PlaceArrow(Button button, bool left, float edge, float size, float bottom, float top)
        {
            if (button == null || button.transform is not RectTransform rect)
            {
                return;
            }

            float x = left ? 0f : 1f;

            // Centred on the preview's height: the page-middle anchor, shifted by the half the
            // preview sits off it.
            rect.anchorMin = new Vector2(x, 0.5f);
            rect.anchorMax = new Vector2(x, 0.5f);
            rect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(left ? edge : -edge, (bottom - top) * 0.5f);
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 size, Vector2 position, Vector2? pivot = null)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        private static void Border(Image image, float borderPx, float wanted)
        {
            if (image != null && wanted > 0f)
            {
                image.pixelsPerUnitMultiplier = borderPx / wanted;
            }
        }

        private static void SetActive(Component component, bool value)
        {
            if (component != null)
            {
                component.gameObject.SetActive(value);
            }
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        private static void Unwire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(handler);
            }
        }
    }
}
