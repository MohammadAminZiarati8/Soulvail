using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Save;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs: a file-scoped namespace on a
// MonoBehaviour makes every scene reference to it deserialise as null (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The Heroes tab (UI-11f): two columns of cards, one per class on the roster in roster order,
    /// scrolling. A card is a tap target and nothing else — <see cref="Tapped"/> says which class, and
    /// the screen that owns the grid opens the character page on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built in code, once</b>, on the first <see cref="Show"/>: the grid is a list whose length is
    /// the roster's, so there is no prefab to keep in step with it. Later shows re-dress the cards
    /// already made and instantiate nothing unless the roster grew.
    /// </para>
    /// <para>
    /// <b>It draws the profile and owns none of it</b>: owned and locked come from
    /// <see cref="ClassUnlocks"/>, the picked class is told to <see cref="Show"/>.
    /// </para>
    /// </remarks>
    public sealed class HeroGrid : MonoBehaviour
    {
        /// <summary>Layout, in dp. A card is never narrower than <see cref="MinCardWidthDp"/>.</summary>
        public const float MinCardWidthDp = 120f;
        public const float MarginDp = 12f;
        public const float GapDp = 12f;
        public const float TitleHeightDp = 56f;
        public const float TitleTextDp = 26f;
        public const float NameTextDp = 18f;
        public const float SmallTextDp = 14f;
        public const float FrameBorderDp = 18f;   // the frame's drawn border, however thick the sprite's own
        public const float GlowPadDp = 6f;        // the frame is drawn this far outside the card, for the glow
        public const float CardRatio = 1.3f;
        public const float LockedAlpha = 0.35f;
        public const float PortraitTopDp = 4f;    // the portrait stops this far under the frame's opening

        private const int Columns = 2;
        private const float LockDp = 36f;

        private static readonly LocKey TitleKey = new LocKey("ui.heroes.title");
        private static readonly LocKey PickedKey = new LocKey("ui.heroes.picked");
        private static readonly LocKey DeedKey = new LocKey("ui.classselect.locked.deed");

        [Tooltip("CharacterPage_LockBadge, drawn over a locked card.")]
        [SerializeField] private Sprite _lockSprite;

        [Tooltip("HeroCard_Frame: the stone portrait frame every card wears.")]
        [SerializeField] private Sprite _frame;

        [Tooltip("HeroCard_FrameSelected: the cyan-glowing frame the picked hero wears over the normal one.")]
        [SerializeField] private Sprite _frameSelected;

        [Tooltip("The scene's UI font, so the grid's words match the screens around it.")]
        [SerializeField] private TMP_FontAsset _font;

        private readonly List<Card> _cards = new List<Card>();
        private ContentCatalog _catalog;
        private ClassSelectRoster _roster;
        private ILocalizer _localizer;
        private CharacterLookBook _looks;
        private TMP_Text _title;
        private RectTransform _viewport;
        private RectTransform _content;
        private float _px = 1f;

        /// <summary>Raised when a card is tapped, locked or not.</summary>
        public event Action<ContentId> Tapped;

        /// <summary>One card per roster class, in roster order, as of the last <see cref="Show"/>.</summary>
        public IReadOnlyList<Card> Cards => _cards;

        /// <summary>The scrolling content, for the layout test.</summary>
        public RectTransform Content => _content;

        /// <param name="catalog">What classes exist.</param>
        /// <param name="roster">Which are on offer. Null means every class.</param>
        /// <param name="localizer">What turns the grid's keys and class names into words.</param>
        /// <param name="looks">The class → emblem table. Null draws cards without an emblem.</param>
        /// <exception cref="ArgumentNullException">The catalog or the localizer is null.</exception>
        public void Configure(ContentCatalog catalog, ClassSelectRoster roster, ILocalizer localizer, CharacterLookBook looks)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _roster = roster;
            _looks = looks;
        }

        /// <summary>Puts the grid up, drawn from <paramref name="profile"/>, with <paramref name="picked"/> rimmed.</summary>
        public void Show(in PlayerProfile profile, ContentId picked)
        {
            if (_catalog is null)
            {
                return;
            }

            gameObject.SetActive(true);
            Build();

            IReadOnlyList<CharacterSpec> shown = _roster is null ? _catalog.Characters : _roster.ShownFrom(_catalog);

            while (_cards.Count < shown.Count)
            {
                _cards.Add(MakeCard());
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                if (i >= shown.Count)
                {
                    _cards[i].Root.gameObject.SetActive(false);
                    continue;
                }

                Dress(_cards[i], shown[i], profile, picked);
            }

            _title.text = _localizer.Get(TitleKey);

            Layout(_px, shown.Count);
        }

        /// <summary>Takes the grid down.</summary>
        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// Lays the grid out in dp, <paramref name="px"/> canvas units to the dp, from this object's
        /// own rect: the title on top, the scroll area down to the tab bar's top.
        /// </summary>
        public void Layout(float px) => Layout(px, ActiveCount());

        private void Layout(float px, int count)
        {
            _px = px > 0f ? px : 1f;

            if (_viewport == null)
            {
                return;
            }

            float width = ((RectTransform)transform).rect.width;
            float margin = MarginDp * _px;
            float gap = GapDp * _px;
            float cardW = Mathf.Max(MinCardWidthDp * _px, (width - (2f * margin) - ((Columns - 1) * gap)) / Columns);
            float cardH = cardW * CardRatio;
            int rows = (count + Columns - 1) / Columns;

            RectTransform title = _title.rectTransform;
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = Vector2.one;
            title.pivot = new Vector2(0.5f, 1f);
            title.offsetMin = new Vector2(margin, -TitleHeightDp * _px);
            title.offsetMax = new Vector2(-margin, 0f);
            _title.fontSize = TitleTextDp * _px;

            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(0f, TabBar.HeightDp * _px);
            _viewport.offsetMax = new Vector2(0f, -TitleHeightDp * _px);

            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);
            _content.sizeDelta = new Vector2(0f, margin + (rows * (cardH + gap)));
            _content.anchoredPosition = Vector2.zero;

            for (int i = 0; i < count && i < _cards.Count; i++)
            {
                int column = i % Columns;
                int row = i / Columns;
                RectTransform rect = _cards[i].Root;

                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(cardW, cardH);
                rect.anchoredPosition = new Vector2(margin + (column * (cardW + gap)), -(margin + (row * (cardH + gap))));

                LayoutCard(_cards[i]);
            }
        }

        private int ActiveCount()
        {
            int count = 0;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Root.gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        private void LayoutCard(Card card)
        {
            float glow = GlowPadDp * _px;
            float edge = (FrameBorderDp - GlowPadDp) * _px;
            float inner = 4f * _px;
            float text = NameTextDp * _px;
            float small = SmallTextDp * _px;

            // Both frames are drawn outside the card, over the gap and the margin, never the neighbour.
            Stretch(card.Frame.rectTransform, -glow);
            Stretch(card.Rim.rectTransform, -glow);
            FrameBorder(card.Frame);
            FrameBorder(card.Rim);

            // The plate fills the frame's opening, so nothing on it is ever under stone.
            Stretch((RectTransform)card.Button.transform, edge);

            // From the bottom of the plate: the picked line, the price, the deed, then the name.
            Band(card.PickedLabel, 0f, small * 1.4f, small);
            Band(card.Price, small * 1.4f, small * 2.8f, small);
            Band(card.Deed, small * 2.8f, small * 4.2f, small);
            Band(card.NameLabel, small * 4.2f, small * 4.2f + (text * 1.4f), text);

            float top = small * 4.2f + (text * 1.4f);
            RectTransform emblem = card.Emblem.rectTransform;
            emblem.anchorMin = Vector2.zero;
            emblem.anchorMax = Vector2.one;

            if (card.ShowsPortrait)
            {
                // Standing on the name band, the opening's full width; a square portrait in this taller
                // rect keeps its spare height above the head because the pivot is bottom-centre.
                emblem.pivot = new Vector2(0.5f, 0f);
                emblem.offsetMin = new Vector2(0f, top);
                emblem.offsetMax = new Vector2(0f, -PortraitTopDp * _px);
            }
            else
            {
                emblem.pivot = new Vector2(0.5f, 0.5f);
                emblem.offsetMin = new Vector2(inner, top);
                emblem.offsetMax = new Vector2(-inner, -inner);
            }

            RectTransform padlock = card.Lock.rectTransform;
            padlock.anchorMin = padlock.anchorMax = padlock.pivot = new Vector2(1f, 1f);
            padlock.sizeDelta = new Vector2(LockDp * _px, LockDp * _px);
            padlock.anchoredPosition = new Vector2(-inner, -inner);
        }

        /// <summary>Draws the sprite's border <see cref="FrameBorderDp"/> thick, whatever it is in pixels.</summary>
        private void FrameBorder(Image image)
        {
            image.enabled = image.sprite != null;

            if (image.sprite == null)
            {
                return;
            }

            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = image.sprite.border.x / (FrameBorderDp * _px);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Band(TMP_Text label, float bottom, float top, float size)
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(4f, bottom);
            rect.offsetMax = new Vector2(-4f, top);
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void Dress(Card card, CharacterSpec spec, in PlayerProfile profile, ContentId picked)
        {
            bool owned = ClassUnlocks.IsUnlocked(spec.Id, profile, _catalog);
            bool isPicked = owned && spec.Id == picked;
            CharacterLook look = _looks?.For(spec.Id) ?? default;
            bool portrait = look.Portrait != null;
            Sprite emblem = portrait ? look.Portrait : look.Emblem;

            card.Id = spec.Id;
            card.Locked = !owned;
            card.Root.gameObject.SetActive(true);

            card.Frame.sprite = _frame;
            card.Frame.color = Color.white;
            card.Rim.sprite = _frameSelected;
            card.Rim.color = Color.white;
            card.Rim.gameObject.SetActive(isPicked);

            card.ShowsPortrait = portrait;
            card.Emblem.sprite = emblem;
            card.Emblem.enabled = emblem != null;
            card.Emblem.preserveAspect = true;
            card.Emblem.color = owned ? Color.white : new Color(Palette.NodeLocked.r, Palette.NodeLocked.g, Palette.NodeLocked.b, LockedAlpha);

            card.Lock.gameObject.SetActive(!owned);

            card.NameLabel.text = _localizer.Get(spec.NameKey);
            card.NameLabel.color = owned ? Palette.HudText : Palette.Neutral;

            card.PickedLabel.gameObject.SetActive(isPicked);
            card.PickedLabel.text = isPicked ? _localizer.Get(PickedKey) : string.Empty;
            card.PickedLabel.color = Palette.Player;

            int price = owned ? 0 : spec.Unlock.ShardPrice;
            card.Price.gameObject.SetActive(price > 0);
            card.Price.text = price > 0 ? price.ToString() : string.Empty;
            card.Price.color = Palette.Essence;

            bool deed = !owned && spec.Unlock.DeedStage > 0;
            card.Deed.gameObject.SetActive(deed);
            card.Deed.text = deed ? _localizer.Format(DeedKey, spec.Unlock.DeedStage) : string.Empty;
            card.Deed.color = Palette.Neutral;
        }

        /// <summary>The backdrop, title and scroll area, made on the first show.</summary>
        private void Build()
        {
            if (_content != null)
            {
                return;
            }

            var backdrop = gameObject.GetComponent<Image>();

            if (backdrop == null)
            {
                backdrop = gameObject.AddComponent<Image>();
            }

            backdrop.color = Palette.Ink;
            backdrop.raycastTarget = true;

            _title = MakeText("Title", transform);
            _title.alignment = TextAlignmentOptions.Center;
            _title.color = Palette.HudText;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(transform, false);
            _viewport = (RectTransform)viewport.transform;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(_viewport, false);
            _content = (RectTransform)content.transform;

            var scroll = gameObject.AddComponent<ScrollRect>();
            scroll.viewport = _viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        private Card MakeCard()
        {
            var root = new GameObject("Card", typeof(RectTransform));
            root.transform.SetParent(_content, false);

            // Back to front: the flat plate with the emblem and words on it, the frame over it, the
            // selected frame over that.
            Image plate = MakeImage("Plate", root.transform);
            plate.color = Color.Lerp(Palette.Ink, Color.white, 0.14f);

            Image frame = MakeImage("Frame", root.transform);
            frame.raycastTarget = false;

            Image rim = MakeImage("Rim", root.transform);
            rim.raycastTarget = false;

            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;

            Image emblem = MakeImage("Emblem", plate.transform);
            emblem.raycastTarget = false;

            Image padlock = MakeImage("Lock", plate.transform);
            padlock.sprite = _lockSprite;
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;

            var card = new Card((RectTransform)root.transform, frame, rim, button, emblem, padlock,
                MakeText("Name", plate.transform), MakeText("Picked", plate.transform),
                MakeText("Price", plate.transform), MakeText("Deed", plate.transform));

            button.onClick.AddListener(() => Tapped?.Invoke(card.Id));

            return card;
        }

        private static Image MakeImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            return go.GetComponent<Image>();
        }

        private TMP_Text MakeText(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();

            if (_font != null)
            {
                text.font = _font;
            }

            text.raycastTarget = false;

            return text;
        }

        /// <summary>One class's card: the pieces a test and the layout read.</summary>
        public sealed class Card
        {
            internal Card(RectTransform root, Image frame, Image rim, Button button, Image emblem, Image padlock, TMP_Text name, TMP_Text picked, TMP_Text price, TMP_Text deed)
            {
                Root = root;
                Frame = frame;
                Rim = rim;
                Button = button;
                Emblem = emblem;
                Lock = padlock;
                NameLabel = name;
                PickedLabel = picked;
                Price = price;
                Deed = deed;
            }

            public RectTransform Root { get; }

            /// <summary>The normal frame, always on.</summary>
            public Image Frame { get; }

            /// <summary>The selected frame, drawn over <see cref="Frame"/> while this is the pick.</summary>
            public Image Rim { get; }

            public Button Button { get; }

            /// <summary>Draws the class's portrait when it has one, its emblem when not.</summary>
            public Image Emblem { get; }

            public Image Lock { get; }

            public TMP_Text NameLabel { get; }

            public TMP_Text PickedLabel { get; }

            public TMP_Text Price { get; }

            public TMP_Text Deed { get; }

            public ContentId Id { get; internal set; }

            public bool Locked { get; internal set; }

            /// <summary>True when <see cref="Emblem"/> is drawing the portrait rather than the emblem.</summary>
            public bool ShowsPortrait { get; internal set; }
        }
    }
}
