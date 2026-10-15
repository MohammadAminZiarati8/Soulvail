using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Save;
using Soulvail.Game.Composition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs: a file-scoped namespace on a
// MonoBehaviour makes every scene reference to it deserialise as null (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The Places tab (UI-11g): a scrolling column with one card per listed place, in catalog order,
    /// and a "more to come" card at the end. An open card is a tap target — <see cref="Tapped"/> says
    /// which place — and a locked card names the place that opens it and does nothing when touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built in code, once</b>, on the first <see cref="Show"/>, as <see cref="HeroGrid"/> is: the
    /// column's length is the catalog's, so there is no prefab to keep in step with it.
    /// </para>
    /// <para>
    /// <b>It draws the profile and owns none of it</b>: open, finished and best come from
    /// <see cref="PlaceUnlocks"/> and the profile, the picked place is told to <see cref="Show"/>.
    /// </para>
    /// </remarks>
    public sealed class PlaceList : MonoBehaviour
    {
        /// <summary>Layout, in dp.</summary>
        public const float MarginDp = 12f;
        public const float GapDp = 12f;
        public const float CardHeightDp = 140f;
        public const float TitleHeightDp = 56f;
        public const float TitleTextDp = 26f;
        public const float NameTextDp = 24f;
        public const float SmallTextDp = 16f;
        public const float RimDp = 3f;
        public const float SoonAlpha = 0.5f;

        private static readonly LocKey TitleKey = new LocKey("ui.places.title");
        private static readonly LocKey BestKey = new LocKey("ui.places.best");
        private static readonly LocKey FinishedKey = new LocKey("ui.places.finished");
        private static readonly LocKey LockedKey = new LocKey("ui.places.locked");
        private static readonly LocKey SoonKey = new LocKey("ui.places.soon");

        [Tooltip("The scene's UI font, so the list's words match the screens around it.")]
        [SerializeField] private TMP_FontAsset _font;

        private readonly List<Card> _cards = new List<Card>();
        private ContentCatalog _catalog;
        private ILocalizer _localizer;
        private ContentId[] _listed = Array.Empty<ContentId>();
        private TMP_Text _title;
        private RectTransform _viewport;
        private RectTransform _content;
        private float _px = 1f;

        /// <summary>Raised when an open place card is tapped. Never for a locked card or the last one.</summary>
        public event Action<ContentId> Tapped;

        /// <summary>One card per listed place in catalog order, then the "more to come" card.</summary>
        public IReadOnlyList<Card> Cards => _cards;

        /// <summary>The scrolling content, for the layout test.</summary>
        public RectTransform Content => _content;

        /// <param name="catalog">What places exist.</param>
        /// <param name="localizer">What turns the list's keys and place names into words.</param>
        /// <exception cref="ArgumentNullException">The catalog or the localizer is null.</exception>
        public void Configure(ContentCatalog catalog, ILocalizer localizer)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>Puts the list up, drawn from <paramref name="profile"/>, with <paramref name="picked"/> rimmed.</summary>
        public void Show(in PlayerProfile profile, ContentId picked)
        {
            if (_catalog is null)
            {
                return;
            }

            gameObject.SetActive(true);
            Build();

            if (_listed.Length < _catalog.Modes.Count)
            {
                _listed = new ContentId[_catalog.Modes.Count];
            }

            int count = PlaceUnlocks.Listed(_catalog, _listed);

            // One card per place and one more for the "soon" card, kept at the end.
            while (_cards.Count < count + 1)
            {
                _cards.Add(MakeCard());
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                if (i < count)
                {
                    DressPlace(_cards[i], _listed[i], profile, picked);
                }
                else if (i == count)
                {
                    DressSoon(_cards[i]);
                }
                else
                {
                    _cards[i].Root.gameObject.SetActive(false);
                }
            }

            _title.text = _localizer.Get(TitleKey);

            Layout(_px, count + 1);
        }

        /// <summary>Takes the list down.</summary>
        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// Lays the list out in dp, <paramref name="px"/> canvas units to the dp, from this object's
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

            float margin = MarginDp * _px;
            float gap = GapDp * _px;
            float cardH = CardHeightDp * _px;

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
            _content.sizeDelta = new Vector2(0f, margin + (count * (cardH + gap)));
            _content.anchoredPosition = Vector2.zero;

            for (int i = 0; i < count && i < _cards.Count; i++)
            {
                RectTransform rect = _cards[i].Root;

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(margin, -(margin + (i * (cardH + gap)) + cardH));
                rect.offsetMax = new Vector2(-margin, -(margin + (i * (cardH + gap))));

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
            float rim = RimDp * _px;
            float text = NameTextDp * _px;
            float small = SmallTextDp * _px;

            Stretch(card.Rim.rectTransform, 0f);
            Stretch((RectTransform)card.Button.transform, rim);

            // From the bottom of the plate: the finished line, the best / locked line, then the name.
            Band(card.FinishedLabel, small * 0.4f, small * 1.8f, small);
            Band(card.StatusLabel, small * 1.8f, small * 3.2f, small);
            Band(card.NameLabel, small * 3.2f, small * 3.2f + (text * 1.4f), text);
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
            rect.offsetMin = new Vector2(8f, bottom);
            rect.offsetMax = new Vector2(-8f, top);
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void DressPlace(Card card, ContentId id, in PlayerProfile profile, ContentId picked)
        {
            ModeSpec mode = _catalog.Mode(id);
            bool open = PlaceUnlocks.IsUnlocked(id, profile, _catalog);
            bool finished = open && PlaceUnlocks.IsFinished(id, profile);
            bool isPicked = open && id == picked;
            int best = profile.BestStage(id);

            card.Id = id;
            card.Locked = !open;
            card.IsSoon = false;
            card.Root.gameObject.SetActive(true);

            card.Rim.gameObject.SetActive(isPicked);
            card.Rim.color = Palette.Player;

            card.Plate.color = open ? Plate : Color.Lerp(Plate, Palette.NodeLocked, 0.6f);
            card.Button.interactable = open;

            card.NameLabel.text = _localizer.Get(mode.NameKey);
            card.NameLabel.color = open ? Palette.HudText : Palette.Neutral;

            string status = string.Empty;

            if (!open)
            {
                status = _localizer.Format(LockedKey, _localizer.Get(_catalog.Mode(mode.UnlockedBy).NameKey));
            }
            else if (best > 0)
            {
                status = _localizer.Format(BestKey, best, mode.FinalStage);
            }

            card.StatusLabel.gameObject.SetActive(status.Length > 0);
            card.StatusLabel.text = status;
            card.StatusLabel.color = Palette.Neutral;

            card.FinishedLabel.gameObject.SetActive(finished);
            card.FinishedLabel.text = finished ? _localizer.Get(FinishedKey) : string.Empty;
            card.FinishedLabel.color = Palette.Essence;
        }

        private void DressSoon(Card card)
        {
            card.Id = default;
            card.Locked = false;
            card.IsSoon = true;
            card.Root.gameObject.SetActive(true);

            card.Rim.gameObject.SetActive(false);
            card.Plate.color = new Color(Plate.r, Plate.g, Plate.b, SoonAlpha);
            card.Button.interactable = false;

            card.NameLabel.gameObject.SetActive(true);
            card.NameLabel.text = _localizer.Get(SoonKey);
            card.NameLabel.color = Palette.Neutral;

            card.StatusLabel.gameObject.SetActive(false);
            card.FinishedLabel.gameObject.SetActive(false);
        }

        private static Color Plate => Color.Lerp(Palette.Ink, Color.white, 0.14f);

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

            Image rim = MakeImage("Rim", root.transform);
            rim.raycastTarget = false;

            Image plate = MakeImage("Plate", root.transform);

            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;

            // A card that cannot be tapped shows the colour it was dressed in, not a disabled tint.
            ColorBlock colours = button.colors;
            colours.disabledColor = Color.white;
            button.colors = colours;

            var card = new Card((RectTransform)root.transform, rim, plate, button,
                MakeText("Name", plate.transform), MakeText("Status", plate.transform), MakeText("Finished", plate.transform));

            button.onClick.AddListener(() =>
            {
                if (!card.Locked && !card.IsSoon)
                {
                    Tapped?.Invoke(card.Id);
                }
            });

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

        /// <summary>One place's card: the pieces a test and the layout read.</summary>
        public sealed class Card
        {
            internal Card(RectTransform root, Image rim, Image plate, Button button, TMP_Text name, TMP_Text status, TMP_Text finished)
            {
                Root = root;
                Rim = rim;
                Plate = plate;
                Button = button;
                NameLabel = name;
                StatusLabel = status;
                FinishedLabel = finished;
            }

            public RectTransform Root { get; }

            /// <summary>The <see cref="Palette.Player"/> frame behind the plate, on while this is the pick.</summary>
            public Image Rim { get; }

            public Image Plate { get; }

            /// <summary>Not interactable on a locked card or the "more to come" card, so neither presses.</summary>
            public Button Button { get; }

            public TMP_Text NameLabel { get; }

            /// <summary>"Best: stage N / M" on an open place, "Finish X to open" on a locked one.</summary>
            public TMP_Text StatusLabel { get; }

            /// <summary>"Finished" in <see cref="Palette.Essence"/>.</summary>
            public TMP_Text FinishedLabel { get; }

            public ContentId Id { get; internal set; }

            public bool Locked { get; internal set; }

            /// <summary>The "more to come" card at the end of the column.</summary>
            public bool IsSoon { get; internal set; }
        }
    }
}
