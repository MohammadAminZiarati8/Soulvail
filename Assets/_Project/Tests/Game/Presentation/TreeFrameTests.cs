using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-04b: the tree screen's stone frame, ribbon, points pill and Close.
/// </summary>
/// <remarks>
/// A second file of <see cref="TreeScreenTests"/> rather than a second fixture: the run, the catalog
/// and both prefabs it builds are that fixture's private helpers, and a copy of them would be the
/// drift this project's test support exists to prevent.
/// </remarks>
public sealed partial class TreeScreenTests
{
    private static readonly Vector2 Landscape = new Vector2(914f, 411f);

    private const float FrameMarginDp = 12f;
    private const float FrameTopMarginDp = 36f;
    private const float CloseMinDp = 48f;
    private const float TextFloorDp = 12f;

    [Test]
    public void Frame_FillsSafeAreaLessMargin()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        OpenAt(Portrait);

        float px = StickShaper.PixelsPerDp(Screen.dpi);
        Rect area = WorldRect(Safe());
        Rect frame = WorldRect(Piece("_frame"));

        Assert.That(frame.xMin, Is.EqualTo(area.xMin + (FrameMarginDp * px)).Within(0.5f));
        Assert.That(frame.xMax, Is.EqualTo(area.xMax - (FrameMarginDp * px)).Within(0.5f));
        Assert.That(frame.yMin, Is.EqualTo(area.yMin + (FrameMarginDp * px)).Within(0.5f));
        Assert.That(frame.yMax, Is.EqualTo(area.yMax - (FrameTopMarginDp * px)).Within(0.5f));
        Assert.That(Piece("_frame").GetComponent<Image>().type, Is.EqualTo(Image.Type.Sliced));
    }

    [Test]
    public void Ribbon_ReadsTitleKey()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        OpenAt(Portrait);

        Assert.That(Field<TMP_Text>(_screen, "_titleLabel").text, Is.EqualTo("Skill Tree"));
        Assert.That(Piece("_ribbon").GetComponent<Image>().sprite.name, Is.EqualTo("Popup_TitleRibbon"));

        // Centred on the frame's top edge (rule 2).
        Assert.That(
            WorldRect(Piece("_ribbon")).center.y,
            Is.EqualTo(WorldRect(Piece("_frame")).yMax).Within(0.5f));
    }

    [Test]
    public void Pill_ShowsPointsAndTint()
    {
        StartRun(Ranger(), free: true, pending: 2);
        Build(Shipped());

        OpenAt(Portrait);

        TMP_Text count = Field<TMP_Text>(_screen, "_pointsLabel");

        Assert.That(count.text, Is.EqualTo("2"));
        Assert.That(count.color, Is.EqualTo(Palette.Available));
        Assert.That(Piece("_pill").GetComponent<Image>().sprite.name, Is.EqualTo("Pill_Dark"));
        Assert.That(Piece("_pillIcon").GetComponent<Image>().sprite.name, Is.EqualTo("Icon_SkillPoint"));

        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 0);
        Build(Shipped());

        OpenAt(Portrait);

        count = Field<TMP_Text>(_screen, "_pointsLabel");

        Assert.That(count.text, Is.EqualTo("0"));
        Assert.That(count.color, Is.EqualTo(Color.white), "0 points is not plain.");
    }

    [Test]
    public void Pill_UpdatesAfterTake()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        OpenAt(Portrait);

        Take(CellFor(_tree.Branches[0].Tier(1)[0]));

        TMP_Text count = Field<TMP_Text>(_screen, "_pointsLabel");

        Assert.That(count.text, Is.EqualTo("0"), "the count was read before the point was spent.");
        Assert.That(count.color, Is.EqualTo(Color.white));
    }

    [Test]
    public void Close_HitAreaAtLeast48Dp()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        OpenAt(Portrait);

        float px = StickShaper.PixelsPerDp(Screen.dpi);
        var close = Field<Button>(_screen, "_close");
        Rect rect = WorldRect((RectTransform)close.transform);

        Assert.That(rect.width, Is.GreaterThanOrEqualTo((CloseMinDp * px) - 0.5f));
        Assert.That(rect.height, Is.GreaterThanOrEqualTo((CloseMinDp * px) - 0.5f));
        Assert.That(close.GetComponent<Image>().raycastTarget, Is.True);
        Assert.That(close.GetComponent<Image>().sprite.name, Is.EqualTo("Button_Close"));
    }

    [Test]
    public void Pill_DrawingRun_ShowsZero()
    {
        StartRun(Ranger(), free: false, pending: 1);
        Build(Shipped());

        _screen.Open(null);

        TMP_Text count = Field<TMP_Text>(_screen, "_pointsLabel");

        Assert.That(count.gameObject.activeInHierarchy, Is.True, "a drawing run hides the pill.");
        Assert.That(count.text, Is.EqualTo("0"));
        Assert.That(Piece("_pill").gameObject.activeInHierarchy, Is.True);
    }

    [Test]
    public void Frame_FitsBothAspects()
    {
        foreach (Vector2 shape in new[] { Portrait, Landscape })
        {
            DestroyWorld();
            CreateWorld();
            StartRun(Ranger(), free: true, pending: 1);
            Build(Shipped());

            OpenAt(shape);

            float px = StickShaper.PixelsPerDp(Screen.dpi);
            Rect area = WorldRect(Safe());

            Rect[] pieces = new[]
            {
                WorldRect(Piece("_ribbon")),
                WorldRect(Piece("_pill")),
                WorldRect((RectTransform)Field<Button>(_screen, "_close").transform),
            };

            foreach (Rect piece in pieces.Append(WorldRect(Piece("_frame"))))
            {
                Assert.That(
                    piece.xMin >= area.xMin - 0.5f && piece.xMax <= area.xMax + 0.5f
                        && piece.yMin >= area.yMin - 0.5f && piece.yMax <= area.yMax + 0.5f,
                    Is.True,
                    $"{piece} leaves the {shape.x}×{shape.y} safe area {area}.");
            }

            for (int a = 0; a < pieces.Length; a++)
            {
                for (int b = a + 1; b < pieces.Length; b++)
                {
                    Assert.That(pieces[a].Overlaps(pieces[b]), Is.False, $"header pieces {a} and {b} overlap at {shape}.");
                }
            }

            foreach (TMP_Text text in _screen.GetComponentsInChildren<TMP_Text>(false))
            {
                Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(TextFloorDp - 1e-3f), $"{text.name} is under the floor.");
            }
        }
    }

    /// <summary>The safe area pinned to <paramref name="dp"/>, as <c>Layout_FitsPortrait</c> pins it, and the screen opened.</summary>
    private void OpenAt(Vector2 dp)
    {
        RectTransform safe = Safe();
        float px = StickShaper.PixelsPerDp(Screen.dpi);

        safe.anchorMin = new Vector2(0.5f, 0.5f);
        safe.anchorMax = new Vector2(0.5f, 0.5f);
        safe.pivot = new Vector2(0.5f, 0.5f);
        safe.sizeDelta = dp * px;
        safe.anchoredPosition = Vector2.zero;

        _screen.Open();
        Canvas.ForceUpdateCanvases();
    }

    private RectTransform Piece(string field) => Field<RectTransform>(_screen, field);
}
