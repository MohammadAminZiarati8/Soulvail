using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see TreeNodeButton.cs and Traps §5.
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The class's tree as one stone block (UI-04c): a panel, a tile per node, a link up each branch,
    /// "LV n" down the left side, and the class plate under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It owns the geometry of everything inside it, and nothing outside.</b> The presenter hands
    /// it the rect it may use, the tiles it drew and the columns they hang from; <see cref="Arrange"/>
    /// works out one tile size for the whole block (the smallest that every branch, row and column
    /// leaves room for) and puts the columns, tiles, links, row labels, branch names and plate where
    /// that size says. Layout lives here rather than in the prefab for <c>TreeViewPresenter</c>'s
    /// reason: a Scale-With-Screen-Size canvas measures in reference pixels, and the tiles are clones
    /// made on first open.
    /// </para>
    /// <para>
    /// <b>Arrange once per draw, <see cref="Show"/> after the tiles have their state.</b> The split is
    /// shape and state: <see cref="Arrange"/> needs the cells, <see cref="Show"/> needs what the run
    /// says about each node, and a take redraws both.
    /// </para>
    /// </remarks>
    public sealed class TreeBlockView : MonoBehaviour
    {
        private const float PadDp = 14f;
        private const float PanelBorderDp = 16f;
        private const float RowLabelDp = 36f;
        private const float RowTextDp = 12f;
        private const float BranchLabelDp = 20f;
        private const float PlateGapDp = 8f;
        private const float PlateHeightDp = 56f;
        private const float PlateMaxWidthDp = 220f;
        private const float PlateNameDp = 16f;
        private const float PlateBorderPx = 90f;
        private const float PanelBorderPx = 110f;
        private const float LinkWidthRatio = 0.3f;
        private const float LinkSpritePx = 147f;
        private const float NumeralDp = 12f;
        private const float CompactPadDp = 4f;
        private const float CompactBranchLabelDp = 18f;

        /// <summary><em>"LV {0:0}"</em> — a row's label.</summary>
        private static readonly LocKey RowKey = new LocKey("ui.tree.row");

        [Tooltip("The sliced TreeBlock_Panel behind the grid.")]
        [SerializeField] private RectTransform _panel;

        [Tooltip("TreeLink_Lit and TreeLink_Dark, between a node and the one above it in its branch.")]
        [SerializeField] private Sprite _linkLit;

        [SerializeField] private Sprite _linkDark;

        [Tooltip("The link every one is cloned from, and never shown itself.")]
        [SerializeField] private Image _linkTemplate;

        [Tooltip("The \"LV n\" label every row's is cloned from, and never shown itself.")]
        [SerializeField] private TMP_Text _rowTemplate;

        [Tooltip("Icon_Placeholder: the icon of a skill with none.")]
        [SerializeField] private Sprite _placeholder;

        [Tooltip("The sliced ClassPlate under the panel.")]
        [SerializeField] private RectTransform _plate;

        [Tooltip("The class emblem at the plate's left end; off when the class has none.")]
        [SerializeField] private Image _emblem;

        [Tooltip("The class name on the plate.")]
        [SerializeField] private TMP_Text _className;

        private readonly List<Link> _links = new List<Link>();
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();

        /// <summary>How many tiers the deepest branch has — the number of row labels shown.</summary>
        private int _rowCount;

        /// <summary>Where the plate's name starts with and without an emblem, set by <see cref="Arrange"/>.</summary>
        private float _emblemRight;
        private float _plateInset;

        /// <summary>
        /// Whether the block is on a short screen (UI-07d): tighter padding and labels, and no class
        /// plate. Set by the presenter before <see cref="Arrange"/>.
        /// </summary>
        public bool Compact { get; set; }

        /// <summary>Icon_Placeholder, for a skill with no icon of its own (UI-04c rule 3).</summary>
        public Sprite Placeholder => _placeholder;

        /// <summary>How many "LV n" labels are showing.</summary>
        public int RowCount => _rowCount;

        /// <summary>The text of row label <paramref name="index"/>, from the bottom row (tier 1) up.</summary>
        public string RowText(int index) => index >= 0 && index < _rowCount ? _rows[index].text : null;

        /// <summary>
        /// The sprite of the link running up from <paramref name="node"/>, or null when none does (the
        /// top of a branch).
        /// </summary>
        public Sprite LinkAbove(ContentId node)
        {
            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i].Image.gameObject.activeSelf && _links[i].Lower == node)
                {
                    return _links[i].Image.sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// Sizes and places the columns, tiles, links, row labels, branch names and plate inside this
        /// block's rect.
        /// </summary>
        /// <param name="tree">The class's tree.</param>
        /// <param name="cells">One tile per node, in tree order.</param>
        /// <param name="columns">Each branch's cell parent, in branch order; its parent is the column.</param>
        /// <param name="branchLabels">Each branch's name, in branch order.</param>
        /// <param name="pxPerDp">How many canvas units one dp is worth.</param>
        /// <param name="tileMaxPx">The tile side wanted when there is room.</param>
        /// <param name="gapPx">The gap between two tiles.</param>
        public void Arrange(
            SkillTreeSpec tree,
            IReadOnlyList<TreeNodeButton> cells,
            IReadOnlyList<RectTransform> columns,
            IReadOnlyList<TMP_Text> branchLabels,
            float pxPerDp,
            float tileMaxPx,
            float gapPx)
        {
            if (tree is null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            var block = (RectTransform)transform;
            float pad = (Compact ? CompactPadDp : PadDp) * pxPerDp;
            float labelW = RowLabelDp * pxPerDp;
            float labelH = (Compact ? CompactBranchLabelDp : BranchLabelDp) * pxPerDp;

            // A short screen has no room for the class plate (UI-07d).
            float plateH = Compact ? 0f : PlateHeightDp * pxPerDp;
            float plateGap = Compact ? 0f : PlateGapDp * pxPerDp;

            int rows = 1;
            int per = 1;

            for (int b = 0; b < tree.Branches.Count; b++)
            {
                rows = Mathf.Max(rows, tree.Branches[b].TierCount);

                for (int t = 1; t <= tree.Branches[b].TierCount; t++)
                {
                    per = Mathf.Max(per, tree.Branches[b].Tier(t).Count);
                }
            }

            float width = block.rect.width;
            float height = block.rect.height;
            float colW = (width - (2f * pad) - labelW) / SkillTreeSpec.BranchCount;

            // The smallest side that the height, the column width and the wanted size allow. A size
            // the layout cannot yet answer (a canvas never laid out) is skipped rather than
            // collapsing the tile — too small is a layout, nothing there is a crash.
            float tile = tileMaxPx;
            float fromHeight = (height - (2f * pad) - labelH - gapPx - plateGap - plateH - ((rows - 1) * gapPx)) / rows;
            float fromWidth = (colW - gapPx - ((per - 1) * gapPx)) / per;

            if (float.IsFinite(fromHeight) && fromHeight > 0f)
            {
                tile = Mathf.Min(tile, fromHeight);
            }

            if (float.IsFinite(fromWidth) && fromWidth > 0f)
            {
                tile = Mathf.Min(tile, fromWidth);
            }

            float pitch = tile + gapPx;
            float gridH = (rows * tile) + ((rows - 1) * gapPx);
            float columnH = gridH + gapPx + labelH;
            float panelH = (2f * pad) + columnH;
            float linkW = tile * LinkWidthRatio;

            if (_panel != null)
            {
                _panel.anchorMin = new Vector2(0f, 1f);
                _panel.anchorMax = Vector2.one;
                _panel.pivot = new Vector2(0.5f, 1f);
                _panel.sizeDelta = new Vector2(0f, panelH);
                _panel.anchoredPosition = Vector2.zero;
                SetBorder(_panel, PanelBorderPx, PanelBorderDp * pxPerDp);
            }

            int index = 0;
            int link = 0;

            for (int b = 0; b < tree.Branches.Count; b++)
            {
                RectTransform cellsRect = b < columns.Count ? columns[b] : null;

                if (cellsRect == null)
                {
                    continue;
                }

                if (cellsRect.parent is RectTransform column)
                {
                    column.anchorMin = new Vector2(0f, 1f);
                    column.anchorMax = new Vector2(0f, 1f);
                    column.pivot = new Vector2(0f, 1f);
                    column.sizeDelta = new Vector2(colW, columnH);
                    column.anchoredPosition = new Vector2(pad + labelW + (b * colW), -pad);
                }

                cellsRect.anchorMin = Vector2.zero;
                cellsRect.anchorMax = Vector2.one;
                cellsRect.offsetMin = new Vector2(0f, labelH + gapPx);
                cellsRect.offsetMax = Vector2.zero;

                if (b < branchLabels.Count && branchLabels[b] != null)
                {
                    RectTransform label = branchLabels[b].rectTransform;

                    label.anchorMin = Vector2.zero;
                    label.anchorMax = new Vector2(1f, 0f);
                    label.pivot = new Vector2(0.5f, 0f);
                    label.anchoredPosition = Vector2.zero;
                    label.sizeDelta = new Vector2(0f, labelH);
                    branchLabels[b].fontSize = RowTextDp * pxPerDp;
                    branchLabels[b].alignment = TextAlignmentOptions.Center;
                    branchLabels[b].textWrappingMode = TextWrappingModes.NoWrap;
                    branchLabels[b].overflowMode = TextOverflowModes.Ellipsis;
                }

                SkillBranchSpec branch = tree.Branches[b];

                for (int t = 1; t <= branch.TierCount; t++)
                {
                    IReadOnlyList<ContentId> tier = branch.Tier(t);

                    for (int i = 0; i < tier.Count; i++)
                    {
                        if (index >= cells.Count)
                        {
                            return;
                        }

                        PlaceTile(cells[index], tile, t, i, tier.Count, pitch, gapPx, pxPerDp);
                        index++;
                    }

                    if (t > 1)
                    {
                        IReadOnlyList<ContentId> below = branch.Tier(t - 1);

                        for (int i = 0; i < tier.Count; i++)
                        {
                            Link slot = LinkAt(link++, cellsRect);
                            ContentId lower = below[Mathf.Min(i, below.Count - 1)];
                            float x = (i - ((tier.Count - 1) / 2f)) * pitch;

                            slot.Lower = lower;

                            RectTransform rect = slot.Image.rectTransform;

                            rect.anchorMin = new Vector2(0.5f, 0f);
                            rect.anchorMax = new Vector2(0.5f, 0f);
                            rect.pivot = new Vector2(0.5f, 0f);
                            rect.sizeDelta = new Vector2(linkW, pitch);
                            rect.anchoredPosition = new Vector2(x, ((t - 2) * pitch) + (tile / 2f));
                            slot.Image.pixelsPerUnitMultiplier = LinkSpritePx / linkW;
                            slot.Image.gameObject.SetActive(true);
                        }
                    }
                }
            }

            for (int i = link; i < _links.Count; i++)
            {
                _links[i].Image.gameObject.SetActive(false);
            }

            ArrangeRows(rows, pad, labelW, tile, pitch, pxPerDp);
            ArrangePlate(panelH, plateGap, plateH, width, pxPerDp);
        }

        /// <summary>
        /// Lights each link by its lower node, writes the row labels and the plate (rules 6 to 8).
        /// </summary>
        public void Show(
            SkillTreeSpec tree,
            Func<ContentId, NodeState> stateOf,
            ILocalizer localizer,
            Sprite emblem,
            LocKey className)
        {
            if (tree is null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            if (stateOf is null)
            {
                throw new ArgumentNullException(nameof(stateOf));
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(nameof(localizer));
            }

            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i].Image.gameObject.activeSelf)
                {
                    _links[i].Image.sprite = stateOf(_links[i].Lower) == NodeState.Taken ? _linkLit : _linkDark;
                }
            }

            for (int i = 0; i < _rowCount; i++)
            {
                _rows[i].text = localizer.Format(RowKey, i + 1);
            }

            if (_className != null)
            {
                _className.text = localizer.Get(className);
            }

            bool hasEmblem = emblem != null;

            if (_emblem != null)
            {
                _emblem.sprite = hasEmblem ? emblem : null;
                _emblem.gameObject.SetActive(hasEmblem);
            }

            if (_className != null)
            {
                // The emblem takes the plate's left end; with none the name has the plate to itself,
                // centred (rule 8).
                RectTransform name = _className.rectTransform;
                float left = hasEmblem ? _emblemRight : _plateInset;

                name.offsetMin = new Vector2(left, 0f);
            }
        }

        private void PlaceTile(TreeNodeButton cell, float tile, int tier, int i, int count, float pitch, float gap, float pxPerDp)
        {
            cell.Size(tile, NumeralDp * pxPerDp);

            if (cell.transform is RectTransform rect)
            {
                // Tier 1 at the bottom, the row's nodes side by side and centred on the column.
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(tile, tile);
                rect.anchoredPosition = new Vector2((i - ((count - 1) / 2f)) * pitch, (tier - 1) * pitch);
            }
        }

        private void ArrangeRows(int rows, float pad, float labelW, float tile, float pitch, float pxPerDp)
        {
            _rowCount = rows;

            while (_rows.Count < rows && _rowTemplate != null)
            {
                _rows.Add(Instantiate(_rowTemplate, transform));
            }

            for (int t = 1; t <= _rows.Count; t++)
            {
                TMP_Text label = _rows[t - 1];

                if (t > rows)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }

                RectTransform rect = label.rectTransform;

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(labelW, tile);

                // Row t's centre, counted down from the block's top: tier 1 is the bottom row.
                rect.anchoredPosition = new Vector2(pad / 2f, -(pad + ((rows - t) * pitch) + (tile / 2f)));
                label.fontSize = RowTextDp * pxPerDp;
                label.alignment = TextAlignmentOptions.Left;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.gameObject.SetActive(true);
            }
        }

        private void ArrangePlate(float panelH, float plateGap, float plateH, float width, float pxPerDp)
        {
            if (_plate == null)
            {
                return;
            }

            _plate.gameObject.SetActive(!Compact);

            if (Compact)
            {
                return;
            }

            float plateW = float.IsFinite(width) && width > 0f
                ? Mathf.Min(width, PlateMaxWidthDp * pxPerDp)
                : PlateMaxWidthDp * pxPerDp;

            _plate.anchorMin = new Vector2(0.5f, 1f);
            _plate.anchorMax = new Vector2(0.5f, 1f);
            _plate.pivot = new Vector2(0.5f, 1f);
            _plate.sizeDelta = new Vector2(plateW, plateH);
            _plate.anchoredPosition = new Vector2(0f, -(panelH + plateGap));
            SetBorder(_plate, PlateBorderPx, plateH * 0.3f);

            float emblemSide = plateH * 0.78f;
            _plateInset = plateH * 0.28f;
            _emblemRight = _plateInset + emblemSide + (plateH * 0.06f);

            if (_emblem != null)
            {
                RectTransform rect = _emblem.rectTransform;

                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(emblemSide, emblemSide);
                rect.anchoredPosition = new Vector2(plateH * 0.28f, 0f);
            }

            if (_className != null)
            {
                RectTransform rect = _className.rectTransform;

                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = new Vector2(-_plateInset, 0f);
                _className.fontSize = PlateNameDp * pxPerDp;
                _className.alignment = TextAlignmentOptions.Center;
                _className.textWrappingMode = TextWrappingModes.NoWrap;
                _className.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        /// <summary>The <paramref name="n"/>th pooled link, parented behind the tiles of <paramref name="parent"/>.</summary>
        private Link LinkAt(int n, RectTransform parent)
        {
            while (_links.Count <= n)
            {
                if (_linkTemplate == null)
                {
                    throw new InvalidOperationException(
                        "TreeBlockView has no link template. The block is not dressed on its prefab.");
                }

                _links.Add(new Link { Image = Instantiate(_linkTemplate, parent) });
            }

            Link slot = _links[n];

            if (slot.Image.transform.parent != parent)
            {
                slot.Image.transform.SetParent(parent, false);
            }

            // Behind every tile of the column, which were built before the first link was.
            slot.Image.transform.SetAsFirstSibling();

            return slot;
        }

        /// <summary>A border <paramref name="borderPx"/> sprite pixels wide, drawn <paramref name="wanted"/> canvas units wide.</summary>
        private static void SetBorder(RectTransform rect, float borderPx, float wanted)
        {
            var image = rect.GetComponent<Image>();

            if (image != null && wanted > 0f)
            {
                image.pixelsPerUnitMultiplier = borderPx / wanted;
            }
        }

        private sealed class Link
        {
            public Image Image;
            public ContentId Lower;
        }
    }
}
