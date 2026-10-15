using System;
using System.Collections.Generic;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. ClassSelect.prefab's reference to this
// component would silently deserialise as null in a file-scoped one (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The character page's scrolling hero sheet (UI-12d): the description, the five stats as
    /// numbers, the basic attack, the special move and every rank of every branch. Draws a
    /// <see cref="HeroSheet"/>; a dumb view that sends nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its rows are built in code, once, and reused</b> (rule 9): a second <see cref="Show"/> takes
    /// the first's rows back and only adds one when a sheet needs more. Each row carries every part
    /// any kind of row draws, and the parts its kind does not draw are switched off.
    /// </para>
    /// <para>
    /// <b>It lays itself out in dp</b>, the page's way: a row is as tall as TMP says its text needs
    /// at the row's width, never shorter than its icon (rule 5).
    /// </para>
    /// </remarks>
    public sealed class HeroSheetView : MonoBehaviour
    {
        /// <summary>The sheet's left and right margin, in dp.</summary>
        public const float SideDp = 16f;

        /// <summary>A heading's text, in sp.</summary>
        public const float HeadingDp = 20f;

        /// <summary>Descriptions, stat labels and an entry's rank, in sp.</summary>
        public const float BodyDp = 14f;

        /// <summary>An entry's name, a branch's name and a stat's number, in sp.</summary>
        public const float TitleDp = 17f;

        /// <summary>An entry's framed icon, in dp.</summary>
        public const float IconDp = 48f;

        public const float StatRowDp = 30f;
        public const float SectionGapDp = 18f;
        public const float RowGapDp = 8f;

        /// <summary>Between an entry's icon and its text, in dp.</summary>
        private const float IconGapDp = 12f;

        /// <summary>The icon's inset inside its frame, in dp.</summary>
        private const float IconInsetDp = 6f;

        /// <summary>Between an entry's name line and its description, in dp.</summary>
        private const float NameGapDp = 2f;

        [Tooltip("Vertical only, clamped, with inertia (rule 7). Its viewport clips with a RectMask2D.")]
        [SerializeField] private ScrollRect _scroll;

        [Tooltip("An entry's icon frame — CharacterPage_SkillFrame.")]
        [SerializeField] private Sprite _frame;

        [Tooltip("What an entry with no icon of its own draws — Icon_Placeholder.")]
        [SerializeField] private Sprite _placeholder;

        [SerializeField] private TMP_FontAsset _font;

        private readonly List<Row> _pool = new List<Row>();
        private readonly List<Row> _rows = new List<Row>();
        private float _px = 1f;

        /// <summary>Which part a row is.</summary>
        public enum RowKind
        {
            Description,
            Heading,
            Stat,
            BranchName,
            Entry,
        }

        public ScrollRect Scroll => _scroll;

        /// <summary>The rows on screen, in draw order, top to bottom; inactive rows are not in it.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The content's height as last laid out, in canvas units.</summary>
        public float ContentHeight { get; private set; }

        /// <summary>
        /// Draws <paramref name="sheet"/> and puts the scroll at the top (rule 7). A null sheet hides.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
        public void Show(HeroSheet sheet, ILocalizer localizer)
        {
            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            if (sheet is null)
            {
                Hide();
                return;
            }

            gameObject.SetActive(true);
            _rows.Clear();

            TextRow(RowKind.Description, null, null, localizer.Get(sheet.DescriptionKey));

            TextRow(RowKind.Heading, localizer.Get(HeroSheet.StatsHeading), null, null);

            for (int i = 0; i < sheet.Stats.Count; i++)
            {
                HeroSheet.Stat stat = sheet.Stats[i];
                TextRow(RowKind.Stat, localizer.Get(stat.Label), localizer.Format(stat.Format, stat.Value), null);
            }

            if (sheet.HasAttack)
            {
                TextRow(RowKind.Heading, localizer.Get(HeroSheet.AttackHeading), null, null);
                EntryRow(sheet.Attack, localizer);
            }

            if (sheet.HasSpace)
            {
                TextRow(RowKind.Heading, localizer.Get(HeroSheet.SpaceHeading), null, null);
                EntryRow(sheet.Space, localizer);
            }

            if (sheet.Branches.Count > 0)
            {
                TextRow(RowKind.Heading, localizer.Get(HeroSheet.TreeHeading), null, null);

                for (int b = 0; b < sheet.Branches.Count; b++)
                {
                    HeroSheet.Branch branch = sheet.Branches[b];
                    TextRow(RowKind.BranchName, localizer.Get(branch.NameKey), null, null);

                    for (int r = 0; r < branch.Ranks.Count; r++)
                    {
                        EntryRow(branch.Ranks[r], localizer);
                    }
                }
            }

            for (int i = _rows.Count; i < _pool.Count; i++)
            {
                _pool[i].Root.gameObject.SetActive(false);
            }

            Layout(_px);
            ToTop();
        }

        /// <summary>Takes the sheet off the screen. Its rows are kept for the next <see cref="Show"/>.</summary>
        public void Hide()
        {
            _rows.Clear();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Sizes and stacks every row inside the viewport's width, <paramref name="px"/> canvas units
        /// to the dp, and sizes the content to hold them.
        /// </summary>
        public void Layout(float px)
        {
            _px = px > 0f ? px : 1f;
            px = _px;

            Configure();

            RectTransform viewport = Viewport();
            RectTransform content = _scroll != null ? _scroll.content : null;
            float width = viewport != null ? viewport.rect.width : 0f;
            float inner = float.IsFinite(width) ? Mathf.Max(0f, width - (2f * SideDp * px)) : 0f;
            float y = RowGapDp * px;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];

                if (i > 0)
                {
                    y += (row.Kind == RowKind.Heading ? SectionGapDp : RowGapDp) * px;
                }

                float height = LayoutRow(row, inner, px);

                RectTransform root = row.Root;
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(1f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.sizeDelta = new Vector2(-2f * SideDp * px, height);
                root.anchoredPosition = new Vector2(0f, -y);

                y += height;
            }

            ContentHeight = y + (SectionGapDp * px);

            if (content != null)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.sizeDelta = new Vector2(0f, ContentHeight);
            }
        }

        /// <summary>Vertical only, clamped, with inertia (rule 7) — whatever the prefab was left as.</summary>
        private void Configure()
        {
            if (_scroll == null)
            {
                return;
            }

            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true;
        }

        private RectTransform Viewport()
        {
            if (_scroll != null && _scroll.viewport != null)
            {
                return _scroll.viewport;
            }

            return transform as RectTransform;
        }

        private void ToTop()
        {
            if (_scroll == null)
            {
                return;
            }

            _scroll.StopMovement();

            if (_scroll.content != null)
            {
                _scroll.content.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>A row of text alone: the description, a heading, a stat or a branch's name.</summary>
        private void TextRow(RowKind kind, string title, string value, string body)
        {
            Row row = Next(kind);

            row.SetTexts(title, value, body);
            row.Frame.gameObject.SetActive(false);
        }

        private void EntryRow(HeroSheet.Entry entry, ILocalizer localizer)
        {
            Row row = Next(RowKind.Entry);

            string name = entry.NameKey.Key is null ? string.Empty : localizer.Get(entry.NameKey);
            string rank = entry.Rank > 0 ? localizer.Format(HeroSheet.RankFormat, entry.Rank) : null;
            string description = entry.DescriptionKey.Key is null ? string.Empty : localizer.Get(entry.DescriptionKey);

            row.SetTexts(name, rank, description);
            row.Frame.gameObject.SetActive(true);
            row.Frame.sprite = _frame;
            row.Picture.sprite = entry.Icon != null ? entry.Icon : _placeholder;
        }

        /// <summary>The next row from the pool, built if the pool has run out.</summary>
        private Row Next(RowKind kind)
        {
            if (_rows.Count == _pool.Count)
            {
                _pool.Add(Build(_pool.Count));
            }

            Row row = _pool[_rows.Count];
            row.Kind = kind;
            row.Root.gameObject.SetActive(true);
            _rows.Add(row);

            return row;
        }

        private Row Build(int index)
        {
            Transform parent = _scroll != null && _scroll.content != null ? _scroll.content : transform;

            var root = new GameObject("Row" + index, typeof(RectTransform));
            root.transform.SetParent(parent, false);

            Image frame = NewImage("Frame", root.transform);
            Image picture = NewImage("Icon", frame.transform);
            picture.preserveAspect = true;

            return new Row(
                (RectTransform)root.transform,
                frame,
                picture,
                NewText("Title", root.transform),
                NewText("Value", root.transform),
                NewText("Body", root.transform));
        }

        private static Image NewImage(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(parent, false);

            var image = child.GetComponent<Image>();
            image.raycastTarget = false;

            return image;
        }

        private TMP_Text NewText(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(parent, false);

            TextMeshProUGUI text = child.AddComponent<TextMeshProUGUI>();

            if (_font != null)
            {
                text.font = _font;
            }

            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;

            return text;
        }

        /// <summary>Sizes and places one row's parts in a row <paramref name="width"/> wide.</summary>
        /// <returns>The row's height.</returns>
        private static float LayoutRow(Row row, float width, float px)
        {
            switch (row.Kind)
            {
                case RowKind.Description:
                    return Block(row.BodyText, width, 0f, 0f, BodyDp * px, Palette.HudText);

                case RowKind.Heading:
                    return Block(row.TitleText, width, 0f, 0f, HeadingDp * px, Palette.HudText);

                case RowKind.BranchName:
                    return Block(row.TitleText, width, 0f, 0f, TitleDp * px, Palette.HudCaption);

                case RowKind.Stat:
                    float height = StatRowDp * px;
                    Line(row.TitleText, 0f, width, height, BodyDp * px, Palette.HudCaption, TextAlignmentOptions.Left);
                    Line(row.ValueText, 0f, width, height, TitleDp * px, Palette.HudText, TextAlignmentOptions.Right);
                    return height;

                default:
                    return LayoutEntry(row, width, px);
            }
        }

        /// <summary>
        /// The icon in its frame on the left; the name with its rank right-aligned on the name's line,
        /// and the description under them, beside it (rule 5).
        /// </summary>
        private static float LayoutEntry(Row row, float width, float px)
        {
            float icon = IconDp * px;
            float x = icon + (IconGapDp * px);
            float column = Mathf.Max(0f, width - x);

            Place(row.Frame.rectTransform, 0f, 0f, icon, icon);

            RectTransform picture = row.Picture.rectTransform;
            picture.anchorMin = Vector2.zero;
            picture.anchorMax = Vector2.one;
            picture.offsetMin = new Vector2(IconInsetDp * px, IconInsetDp * px);
            picture.offsetMax = new Vector2(-IconInsetDp * px, -IconInsetDp * px);

            TMP_Text value = row.ValueText;
            float rankWidth = 0f;

            if (value.gameObject.activeSelf)
            {
                Style(value, BodyDp * px, Palette.HudCaption, TextAlignmentOptions.Right);
                value.textWrappingMode = TextWrappingModes.NoWrap;
                rankWidth = Mathf.Min(column, value.GetPreferredValues(value.text).x);
            }

            float nameWidth = Mathf.Max(0f, column - (rankWidth > 0f ? rankWidth + (RowGapDp * px) : 0f));
            float nameHeight = Block(row.TitleText, nameWidth, x, 0f, TitleDp * px, Palette.HudText);

            if (rankWidth > 0f)
            {
                // On the name's first line: as tall as one line of the name, its text centred in it.
                float line = Mathf.Min(nameHeight, row.TitleText.GetPreferredValues("M", float.PositiveInfinity, float.PositiveInfinity).y);
                Place(value.rectTransform, width - rankWidth, 0f, rankWidth, line);
            }

            float bodyTop = nameHeight + (NameGapDp * px);
            float bodyHeight = Block(row.BodyText, column, x, bodyTop, BodyDp * px, Palette.HudText);

            return Mathf.Max(icon, bodyTop + bodyHeight);
        }

        /// <summary>A wrapping text <paramref name="width"/> wide at (<paramref name="x"/>, −<paramref name="top"/>), as tall as it needs.</summary>
        /// <returns>Its height.</returns>
        private static float Block(TMP_Text text, float width, float x, float top, float size, Color color)
        {
            Style(text, size, color, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;

            float height = width > 0f ? text.GetPreferredValues(text.text, width, float.PositiveInfinity).y : 0f;

            Place(text.rectTransform, x, top, width, height);

            return height;
        }

        /// <summary>One line across the row, its text aligned in it.</summary>
        private static void Line(TMP_Text text, float x, float width, float height, float size, Color color, TextAlignmentOptions alignment)
        {
            Style(text, size, color, alignment);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Place(text.rectTransform, x, 0f, width, height);
        }

        private static void Style(TMP_Text text, float size, Color color, TextAlignmentOptions alignment)
        {
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
        }

        /// <summary>Top-left anchored in the row: <paramref name="x"/> in, <paramref name="top"/> down.</summary>
        private static void Place(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -top);
        }

        /// <summary>One row of the sheet. Which of its parts are drawn is its <see cref="Kind"/>'s.</summary>
        public sealed class Row
        {
            internal Row(RectTransform root, Image frame, Image picture, TMP_Text title, TMP_Text value, TMP_Text body)
            {
                Root = root;
                Frame = frame;
                Picture = picture;
                TitleText = title;
                ValueText = value;
                BodyText = body;
            }

            public RowKind Kind { get; internal set; }

            public RectTransform Root { get; }

            /// <summary>The entry's icon, inside its frame; null for any other row.</summary>
            public Image Icon => Kind == RowKind.Entry ? Picture : null;

            /// <summary>A heading's text, a stat's label, a branch's name or an entry's name; null for the description.</summary>
            public TMP_Text Title => TitleText.gameObject.activeSelf ? TitleText : null;

            /// <summary>A stat's number or a rank's "Rank n"; null otherwise.</summary>
            public TMP_Text Value => ValueText.gameObject.activeSelf ? ValueText : null;

            /// <summary>The description's text or an entry's; null for headings, stats and branch names.</summary>
            public TMP_Text Body => BodyText.gameObject.activeSelf ? BodyText : null;

            internal Image Frame { get; }

            internal Image Picture { get; }

            internal TMP_Text TitleText { get; }

            internal TMP_Text ValueText { get; }

            internal TMP_Text BodyText { get; }

            /// <summary>Writes each part; a null part is switched off.</summary>
            internal void SetTexts(string title, string value, string body)
            {
                Set(TitleText, title);
                Set(ValueText, value);
                Set(BodyText, body);
            }

            private static void Set(TMP_Text text, string value)
            {
                bool shown = value != null;

                text.gameObject.SetActive(shown);
                text.text = shown ? value : string.Empty;
            }
        }
    }
}
