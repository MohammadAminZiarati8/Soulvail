using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Authoring;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. ClassSelect.prefab's reference to this
// component would silently deserialise as null in a file-scoped one (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>One tile of the class page's skill strip: its icon and the two keys its caption reads.</summary>
    public readonly struct StripEntry
    {
        public StripEntry(Sprite icon, LocKey nameKey, LocKey descriptionKey)
        {
            Icon = icon;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
        }

        /// <summary>The tile's picture, or null for the strip's placeholder.</summary>
        public Sprite Icon { get; }

        public LocKey NameKey { get; }

        public LocKey DescriptionKey { get; }
    }

    /// <summary>
    /// Four icon tiles under the class's preview and a caption that names the selected one (UI-05d).
    /// A dumb view: it sends no command and opens nothing.
    /// </summary>
    public sealed class SkillStrip : MonoBehaviour
    {
        /// <summary>The most tiles the strip draws.</summary>
        public const int MaxTiles = 4;

        /// <summary>The caption's size in sp; it clears the 12 sp floor.</summary>
        public const float CaptionDp = 14f;

        /// <summary>The most lines the caption wraps to.</summary>
        public const int CaptionLines = 3;

        private const float TileDp = 56f;
        private const float TileGapDp = 12f;
        private const float CaptionGapDp = 8f;
        private const float IconInsetDp = 6f;
        private const float LineHeight = 1.25f;

        [Tooltip("The tiles, left to right. A tile past the entries is switched off.")]
        [SerializeField] private Button[] _tiles = new Button[0];

        [Tooltip("Each tile's frame, index-parallel with the tiles.")]
        [SerializeField] private Image[] _frames = new Image[0];

        [Tooltip("Each tile's icon, index-parallel with the tiles.")]
        [SerializeField] private Image[] _icons = new Image[0];

        [SerializeField] private TMP_Text _caption;
        [SerializeField] private Sprite _frame;
        [SerializeField] private Sprite _frameSelected;

        [Tooltip("What a skill with no icon of its own draws — Icon_Placeholder.")]
        [SerializeField] private Sprite _placeholder;

        private readonly List<StripEntry> _entries = new List<StripEntry>(MaxTiles);
        private ILocalizer _localizer;
        private bool _wired;

        /// <summary>The height the strip needs, in dp: a tile, a gap and a caption of three lines.</summary>
        public static float HeightDp => TileDp + CaptionGapDp + (CaptionDp * LineHeight * CaptionLines);

        /// <summary>The selected tile, or -1 when nothing is drawn.</summary>
        public int Selected { get; private set; } = -1;

        /// <summary>How many tiles are on screen.</summary>
        public int TilesShown => _entries.Count;

        /// <summary>The caption, for the layout and the tests.</summary>
        public TMP_Text Caption => _caption;

        /// <summary>
        /// The tiles for one class (rule 1): the tier-1 node of each branch in branch order, then its Space
        /// move. A class with no tree gets only the Space move and one with no Space move no fourth tile.
        /// </summary>
        public static void Build(
            CharacterSpec spec,
            ContentCatalog catalog,
            SkillIconBook icons,
            CharacterLook look,
            List<StripEntry> into)
        {
            into.Clear();

            if (spec is null || catalog is null)
            {
                return;
            }

            if (catalog.TryGetTreeFor(spec.Id, out SkillTreeSpec tree))
            {
                for (int b = 0; b < tree.Branches.Count && into.Count < MaxTiles - 1; b++)
                {
                    ContentId node = tree.Branches[b].Tier(1)[0];

                    if (catalog.TryGetSkill(node, out SkillSpec skill))
                    {
                        into.Add(new StripEntry(icons?.For(node), skill.NameKey, skill.DescriptionKey));
                    }
                }
            }

            if (spec.MovementSkill.Kind != MovementSkillKind.None && into.Count < MaxTiles)
            {
                into.Add(new StripEntry(look.SpaceIcon, look.SpaceNameKey, look.SpaceDescriptionKey));
            }
        }

        /// <summary>Draws the entries, selects the first and writes its caption.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public void Show(IReadOnlyList<StripEntry> entries, ILocalizer localizer)
        {
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            Wire();

            _entries.Clear();

            for (int i = 0; i < entries.Count && i < MaxTiles; i++)
            {
                _entries.Add(entries[i]);
            }

            gameObject.SetActive(_entries.Count > 0);

            for (int i = 0; i < _tiles.Length; i++)
            {
                bool shown = i < _entries.Count;

                if (_tiles[i] != null)
                {
                    _tiles[i].gameObject.SetActive(shown);
                }

                if (shown && i < _icons.Length && _icons[i] != null)
                {
                    Sprite icon = _entries[i].Icon;
                    _icons[i].sprite = icon != null ? icon : _placeholder;
                }
            }

            Select(_entries.Count > 0 ? 0 : -1);
        }

        /// <summary>Takes the strip off the screen.</summary>
        public void Hide()
        {
            _entries.Clear();
            Selected = -1;
            gameObject.SetActive(false);
        }

        /// <summary>Selects a tile and writes its name and description under the strip (rule 3).</summary>
        public void Select(int index)
        {
            if (index >= _entries.Count)
            {
                return;
            }

            Selected = index;

            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i] != null)
                {
                    _frames[i].sprite = i == index ? _frameSelected : _frame;
                }
            }

            if (_caption == null)
            {
                return;
            }

            if (index < 0 || _localizer == null)
            {
                _caption.text = string.Empty;
                return;
            }

            StripEntry entry = _entries[index];
            string name = entry.NameKey.Key is null ? string.Empty : _localizer.Get(entry.NameKey);
            string description = entry.DescriptionKey.Key is null ? string.Empty : _localizer.Get(entry.DescriptionKey);

            _caption.text = "<b>" + name + "</b>\n" + description;
        }

        /// <summary>Sizes the tiles and the caption inside the strip's rect, <paramref name="px"/> canvas units to the dp.</summary>
        public void Layout(float px)
        {
            px = px > 0f ? px : 1f;

            float tile = TileDp * px;
            float gap = TileGapDp * px;
            float captionHeight = CaptionDp * LineHeight * CaptionLines * px;
            float start = -((MaxTiles - 1) * (tile + gap)) * 0.5f;

            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == null || _tiles[i].transform is not RectTransform rect)
                {
                    continue;
                }

                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(tile, tile);
                rect.anchoredPosition = new Vector2(start + (i * (tile + gap)), 0f);

                if (i < _icons.Length && _icons[i] != null)
                {
                    RectTransform icon = _icons[i].rectTransform;
                    icon.anchorMin = Vector2.zero;
                    icon.anchorMax = Vector2.one;
                    icon.offsetMin = new Vector2(IconInsetDp * px, IconInsetDp * px);
                    icon.offsetMax = new Vector2(-IconInsetDp * px, -IconInsetDp * px);
                }
            }

            if (_caption != null)
            {
                RectTransform rect = _caption.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(-2f * 12f * px, captionHeight);
                rect.anchoredPosition = Vector2.zero;
                _caption.fontSize = CaptionDp * px;
                _caption.alignment = TextAlignmentOptions.Top;
                _caption.enableAutoSizing = false;
                _caption.textWrappingMode = TextWrappingModes.Normal;
                _caption.overflowMode = TextOverflowModes.Ellipsis;
                _caption.maxVisibleLines = CaptionLines;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] != null)
                {
                    _tiles[i].onClick.RemoveAllListeners();
                }
            }
        }

        private void Wire()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == null)
                {
                    continue;
                }

                int index = i;
                _tiles[i].onClick.AddListener(() => Select(index));
            }
        }
    }
}
