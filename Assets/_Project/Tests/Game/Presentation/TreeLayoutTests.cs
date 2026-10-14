using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-07d: the tree screen on a landscape phone — the block centred, the branch names whole and
/// clear of the card, nothing overlapping.
/// </summary>
/// <remarks>
/// Each row lays the screen out with the px per dp its device would have rather than the Editor's
/// <c>Screen.dpi</c> (Traps §9), through <c>TreeViewPresenter.UsePixelsPerDp</c>.
/// </remarks>
public sealed partial class TreeScreenTests
{
    /// <summary>A device as the look pass saw it: pixels, and dpi.</summary>
    private static readonly (Vector2 Pixels, float Dpi)[] Devices =
    {
        (new Vector2(2658f, 960f), 480f),
        (new Vector2(1920f, 1080f), 400f),
    };

    private const float DpiPerDp = 160f;
    private const float LabelClearDp = 8f;
    private static readonly string[] BranchNames = { "Draw", "Volley", "Stride" };

    [Test]
    public void Tree_Block_IsCentred()
    {
        foreach ((Vector2 pixels, float dpi) in Devices)
        {
            OpenOnDevice(pixels, dpi, out float px);

            Rect frame = WorldRect(Piece("_frame"));
            var block = Field<TreeBlockView>(_screen, "_block");
            Rect[] pieces = { WorldRect(block), WorldRect(Field<RectTransform>(block, "_panel")) };

            foreach (Rect piece in pieces)
            {
                float left = piece.xMin - frame.xMin;
                float right = frame.xMax - piece.xMax;

                Assert.That(left, Is.GreaterThan(0f), $"{pixels}: the block leaves the frame.");
                Assert.That(Mathf.Abs(left - right) / px, Is.LessThanOrEqualTo(1f), $"{pixels}: margins {left / px} dp and {right / px} dp.");
            }
        }
    }

    [Test]
    public void Tree_BranchNames_ClearTheCard()
    {
        foreach ((Vector2 pixels, float dpi) in Devices)
        {
            OpenOnDevice(pixels, dpi, out float px);

            Rect card = WorldRect(Field<TreeDetailCard>(_screen, "_card"));
            TMP_Text[] labels = Field<TMP_Text[]>(_screen, "_branchLabels");

            for (int b = 0; b < labels.Length; b++)
            {
                // The shipped names, whichever keys the fixture's tree carries.
                labels[b].text = BranchNames[b];

                Rect rect = WorldRect(labels[b].rectTransform);
                float width = labels[b].GetPreferredValues(BranchNames[b]).x;

                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(card.yMax + (LabelClearDp * px) - 0.5f), $"{pixels}: '{BranchNames[b]}' ends {(rect.yMin - card.yMax) / px} dp above the card.");
                Assert.That(width, Is.LessThanOrEqualTo(rect.width + 0.5f), $"{pixels}: '{BranchNames[b]}' wants {width} of {rect.width}.");
                Assert.That(rect.height, Is.GreaterThanOrEqualTo(labels[b].fontSize), $"{pixels}: '{BranchNames[b]}' is clipped.");
            }
        }
    }

    [Test]
    public void Tree_NothingOverlaps()
    {
        foreach ((Vector2 pixels, float dpi) in Devices)
        {
            OpenOnDevice(pixels, dpi, out float px);

            var block = Field<TreeBlockView>(_screen, "_block");
            var card = Field<TreeDetailCard>(_screen, "_card");

            var top = new List<(string Name, Rect Rect)>
            {
                ("ribbon", WorldRect(Piece("_ribbon"))),
                ("pill", WorldRect(Piece("_pill"))),
                ("close", WorldRect(Field<UnityEngine.UI.Button>(_screen, "_close").transform as RectTransform)),
                ("block", WorldRect(block)),
                ("card", WorldRect(card)),
            };

            AssertDisjoint(top, pixels);

            Rect frame = WorldRect(Piece("_frame"));

            foreach ((string name, Rect rect) in top.Skip(3))
            {
                Assert.That(Contains(frame, rect), Is.True, $"{pixels}: {name} {rect} leaves the frame {frame}.");
            }

            // Inside the block: the three names and every tile.
            Rect inner = WorldRect(block);
            var parts = new List<(string Name, Rect Rect)>();

            foreach (TMP_Text label in Field<TMP_Text[]>(_screen, "_branchLabels"))
            {
                parts.Add((label.name, WorldRect(label.rectTransform)));
            }

            foreach (TreeNodeButton cell in Shown())
            {
                parts.Add((cell.name, WorldRect(cell)));
            }

            AssertDisjoint(parts, pixels);

            foreach ((string name, Rect rect) in parts)
            {
                Assert.That(Contains(inner, rect), Is.True, $"{pixels}: {name} {rect} leaves the block {inner}.");
            }

            // Inside the card, with a node selected: tile, name, level, description, Learn.
            Tap(Shown().First(c => c.State == NodeState.Available));
            Canvas.ForceUpdateCanvases();

            Rect cardRect = WorldRect(card);
            var inside = new List<(string Name, Rect Rect)>();

            foreach (string field in new[] { "_ribbon", "_level", "_description", "_learn" })
            {
                inside.Add((field, WorldRect(Field<Component>(card, field))));
            }

            inside.Add(("_tile", WorldRect(Field<Component>(card, "_tile"))));

            AssertDisjoint(inside, pixels);

            foreach ((string name, Rect rect) in inside)
            {
                Assert.That(Contains(cardRect, rect), Is.True, $"{pixels}: card {name} {rect} leaves the card {cardRect}.");
            }

            foreach (TMP_Text text in _screen.GetComponentsInChildren<TMP_Text>(false))
            {
                Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), $"{pixels}: {text.name} is under the floor.");
            }
        }
    }

    /// <summary>A fresh Ranger run opened on a device of <paramref name="pixels"/> at <paramref name="dpi"/>.</summary>
    private void OpenOnDevice(Vector2 pixels, float dpi, out float px)
    {
        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        px = dpi / DpiPerDp;

        RectTransform safe = Safe();

        safe.anchorMin = new Vector2(0.5f, 0.5f);
        safe.anchorMax = new Vector2(0.5f, 0.5f);
        safe.pivot = new Vector2(0.5f, 0.5f);
        safe.sizeDelta = pixels;
        safe.anchoredPosition = Vector2.zero;

        _screen.UsePixelsPerDp(px);
        _screen.Open();
        Canvas.ForceUpdateCanvases();

        Assert.That(_screen.IsCompact, Is.True, $"{pixels} is a short screen.");
    }

    private static bool Contains(Rect outer, Rect inner, float slack = 0.5f) =>
        inner.xMin >= outer.xMin - slack && inner.xMax <= outer.xMax + slack
        && inner.yMin >= outer.yMin - slack && inner.yMax <= outer.yMax + slack;

    private static void AssertDisjoint(List<(string Name, Rect Rect)> rects, Vector2 pixels)
    {
        for (int a = 0; a < rects.Count; a++)
        {
            for (int b = a + 1; b < rects.Count; b++)
            {
                // Shrunk by half a unit, so two rects that share an edge are not read as overlapping.
                Rect first = Rect.MinMaxRect(rects[a].Rect.xMin + 0.5f, rects[a].Rect.yMin + 0.5f, rects[a].Rect.xMax - 0.5f, rects[a].Rect.yMax - 0.5f);
                Rect second = Rect.MinMaxRect(rects[b].Rect.xMin + 0.5f, rects[b].Rect.yMin + 0.5f, rects[b].Rect.xMax - 0.5f, rects[b].Rect.yMax - 0.5f);

                Assert.That(first.Overlaps(second), Is.False, $"{pixels}: {rects[a].Name} {rects[a].Rect} overlaps {rects[b].Name} {rects[b].Rect}.");
            }
        }
    }
}
