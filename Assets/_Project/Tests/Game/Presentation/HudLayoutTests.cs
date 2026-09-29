using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-01's top band at a phone's size: laid out at 20:9 landscape and 9:20 portrait, nothing overlaps
/// anything but its declared host, and nothing leaves the safe area or reaches below the band's floor.
/// </summary>
/// <remarks>
/// <para>
/// <b>This fixture is the instrument the Editor cannot be by eye.</b> <c>Screen.dpi</c> reads 120 here
/// against a phone's 400, so every dp this Editor draws is a third of its device size (Traps §9), and a
/// screenshot of the Game view is a screenshot of this monitor. What <em>can</em> be checked here is the
/// arithmetic: each safe area is sized in dp, every component's own <c>Place</c> runs, and the rects
/// are read back in dp. The two sizes are a 2400 × 1080 phone at 420 dpi — 914 × 411 dp — on both
/// sides of the owner's 2026-09-29 ruling that the game goes portrait.
/// </para>
/// <para>
/// <b>Both prefabs, one frame of reference.</b> The pause icon lives on <c>Pause.prefab</c>'s own
/// canvas and every other piece on <c>Hud.prefab</c>'s. Each has a <c>SafeAreaFitter</c> over the
/// same screen, so each rect is read in dp from its own safe area's top-left corner, which on a phone
/// is the same corner.
/// </para>
/// <para>
/// <b>UI-01d, UI-01e and UI-01f extend <see cref="Pieces"/></b>, which is how a later task's chip or
/// cell is held to the same rule without a second fixture.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HudLayoutTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string PausePath = "Assets/_Project/Prefabs/UI/Pause.prefab";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>How many of the skill row's cells the band must hold at both sizes.</summary>
    private const int RowCellsChecked = 8;

    /// <summary>Half a dp: the resolution a layout claim here is made at.</summary>
    private const float Slack = 0.5f;

    private static readonly Vector2 Landscape = new Vector2(914f, 411f);
    private static readonly Vector2 Portrait = new Vector2(411f, 914f);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private GameObject _hud;
    private GameObject _pause;

    [TearDown]
    public void DestroyHud()
    {
        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();
        _hud = null;
        _pause = null;
    }

    // ---- The width rules (rules 4, 11) -----------------------------------------------------------

    [Test]
    public void BarWidth_IsTheRoomLeftUpTo240()
    {
        Assert.That(HudLayout.BarWidth(914f, 60f, 240f, 150f), Is.EqualTo(240f), "landscape leaves room for all of it.");
        Assert.That(HudLayout.BarWidth(411f, 60f, 240f, 150f), Is.EqualTo(201f), "9:20 leaves 201.");
        Assert.That(HudLayout.BarWidth(300f, 60f, 240f, 150f), Is.EqualTo(HudLayout.BarMinWidthDp), "never below the floor.");

        // A nonsense measurement is the bar at its authored width rather than a NaN rect, and a
        // nonsense ceiling is the band's own 240 — which a 9:20 row still clamps to what it leaves.
        Assert.That(HudLayout.BarWidth(float.NaN, 60f, 240f, 150f), Is.EqualTo(240f));
        Assert.That(HudLayout.BarWidth(914f, 60f, 240f, float.NaN), Is.EqualTo(240f));
        Assert.That(HudLayout.BarWidth(914f, 60f, float.NaN, 150f), Is.EqualTo(HudLayout.BarMaxWidthDp));
        Assert.That(HudLayout.BarWidth(411f, 60f, float.NaN, 150f), Is.EqualTo(201f));
    }

    [Test]
    public void BossWidth_IsAtMost400InsideTheMargins()
    {
        Assert.That(HudLayout.BossWidth(914f, 400f), Is.EqualTo(400f));
        Assert.That(HudLayout.BossWidth(411f, 400f), Is.EqualTo(411f - (2f * HudLayout.MarginDp)));
        Assert.That(HudLayout.BossWidth(10f, 400f), Is.EqualTo(0f), "never negative.");
        Assert.That(HudLayout.BossWidth(float.PositiveInfinity, 400f), Is.EqualTo(0f));
    }

    [Test]
    public void FitSlices_ScalesTheBordersWithTheHeight()
    {
        var go = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        _spawned.Add(go);

        var image = go.GetComponent<Image>();

        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Hud/HealthBar_Frame.png");
        image.type = Image.Type.Sliced;
        ((RectTransform)go.transform).sizeDelta = new Vector2(240f, 22f);

        HudLayout.FitSlices(image);

        // A 259 px frame drawn 22 units tall: its 130 px ends are drawn at 22/259 of their size.
        Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(image.sprite.rect.height / 22f).Within(1e-4f));

        // An image with no height yet is left alone rather than handed an infinite multiplier.
        ((RectTransform)go.transform).sizeDelta = new Vector2(240f, 0f);
        float before = image.pixelsPerUnitMultiplier;

        HudLayout.FitSlices(image);

        Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(before));
    }

    // ---- The band (rules 2–4, 11, 12, 14) --------------------------------------------------------

    [Test]
    public void Band_Landscape_NothingOverlaps() => AssertBandHolds(Landscape);

    [Test]
    public void Band_Portrait_NothingOverlaps() => AssertBandHolds(Portrait);

    [Test]
    public void Band_TheStarRingAndBarShareACentreLine()
    {
        foreach (Vector2 size in new[] { Landscape, Portrait })
        {
            Dictionary<string, Rect> band = Lay(size);

            float centre = band["star"].center.y;

            Assert.That(band["ring"].center.y, Is.EqualTo(centre).Within(Slack), $"the ring at {size}");
            Assert.That(band["ring"].center.x, Is.EqualTo(band["star"].center.x).Within(Slack), $"the ring at {size}");
            Assert.That(band["bar"].center.y, Is.EqualTo(centre).Within(Slack), $"the bar at {size}");
            Assert.That(band["level"].center.x, Is.EqualTo(band["star"].center.x).Within(Slack), $"the level at {size}");

            Assert.That(band["star"].width, Is.EqualTo(HudLayout.StarSizeDp).Within(Slack));
            Assert.That(band["bar"].height, Is.EqualTo(HudLayout.BarHeightDp).Within(Slack));
        }

        // Behind the star: uGUI draws in sibling order.
        Transform safe = Safe(_hud);

        Assert.That(
            safe.Find("ShieldRing").GetSiblingIndex(),
            Is.LessThan(safe.Find("LevelStar").GetSiblingIndex()),
            "the Aegis ring draws over the star rather than round it.");
    }

    [Test]
    public void Band_TheBarTakesWhatTheRowLeaves()
    {
        Assert.That(Lay(Landscape)["bar"].width, Is.EqualTo(HudLayout.BarMaxWidthDp).Within(Slack));

        Dictionary<string, Rect> portrait = Lay(Portrait);

        Assert.That(portrait["bar"].width, Is.EqualTo(201f).Within(Slack));
        Assert.That(
            portrait["bar"].xMax,
            Is.LessThanOrEqualTo(Portrait.x - HudLayout.RightReserveDp + Slack),
            "the bar runs into the right-hand cluster's room.");
    }

    [Test]
    public void Band_TheBossBandHangsBelowIt()
    {
        foreach (Vector2 size in new[] { Landscape, Portrait })
        {
            Rect boss = Lay(size)["boss"];

            Assert.That(boss.yMin, Is.EqualTo(HudLayout.BossTopDp).Within(Slack), $"at {size}");
            Assert.That(boss.center.x, Is.EqualTo(size.x * 0.5f).Within(Slack), $"not centred at {size}");
            Assert.That(boss.width, Is.EqualTo(HudLayout.BossWidth(size.x, HudLayout.BossMaxWidthDp)).Within(Slack), $"at {size}");
            Assert.That(boss.xMin, Is.GreaterThanOrEqualTo(HudLayout.MarginDp - Slack), $"past the margin at {size}");
        }
    }

    [Test]
    public void Band_TheRowStartsUnderTheBar()
    {
        foreach (Vector2 size in new[] { Landscape, Portrait })
        {
            Dictionary<string, Rect> band = Lay(size);

            Assert.That(band["cell1"].xMin, Is.EqualTo(band["bar"].xMin).Within(Slack), $"at {size}");
            Assert.That(band["cell1"].yMin - band["bar"].yMax, Is.EqualTo(6f).Within(Slack), $"at {size}");
        }
    }

    [Test]
    public void Band_ANonFiniteFieldLeavesThePieceAlone()
    {
        Build(Landscape);

        var presenter = _hud.GetComponent<HudPresenter>();
        RectTransform star = Field<Image>(presenter, "_levelStar").rectTransform;
        RectTransform bar = (RectTransform)Field<HpBarView>(presenter, "_hp").transform;

        Vector2 starAt = star.anchoredPosition;
        Vector2 starSize = star.sizeDelta;
        Vector2 barAt = bar.anchoredPosition;
        Vector2 barSize = bar.sizeDelta;

        SetField(presenter, "_rightReserveDp", float.NaN);
        Invoke(presenter, "Place");

        Assert.That(bar.anchoredPosition, Is.EqualTo(barAt), "a NaN reserve moved the bar.");
        Assert.That(bar.sizeDelta, Is.EqualTo(barSize), "a NaN reserve resized the bar.");

        SetField(presenter, "_rightReserveDp", HudLayout.RightReserveDp);
        SetField(presenter, "_starSizeDp", float.NaN);
        star.anchoredPosition = new Vector2(1f, -1f);
        starAt = star.anchoredPosition;

        Invoke(presenter, "Place");

        Assert.That(star.anchoredPosition, Is.EqualTo(starAt), "a NaN star moved the star.");
        Assert.That(star.sizeDelta, Is.EqualTo(starSize), "a NaN star resized the star.");
        Assert.That(float.IsFinite(bar.sizeDelta.x), Is.True, "a NaN star reached the bar.");
    }

    // ---- The pieces ------------------------------------------------------------------------------

    /// <summary>
    /// Every piece of the band, by the name each assertion uses, and the pairs allowed to overlap
    /// because one is the other's host.
    /// </summary>
    private static readonly (string A, string B)[] Hosts =
    {
        ("level", "star"),
        ("ring", "star"),
        ("ring", "level"),
        ("numbers", "bar"),
    };

    private void AssertBandHolds(Vector2 size)
    {
        Dictionary<string, Rect> band = Lay(size);

        foreach (KeyValuePair<string, Rect> piece in band)
        {
            Rect r = piece.Value;

            Assert.That(r.width, Is.GreaterThan(0f), $"{piece.Key} has no width at {size}.");
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-Slack), $"{piece.Key} leaves the safe area's left at {size}.");
            Assert.That(r.xMax, Is.LessThanOrEqualTo(size.x + Slack), $"{piece.Key} leaves the safe area's right at {size}.");
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-Slack), $"{piece.Key} leaves the safe area's top at {size}.");

            if (piece.Key != "boss")
            {
                Assert.That(
                    r.yMax,
                    Is.LessThanOrEqualTo(HudLayout.BandBottomDp + Slack),
                    $"{piece.Key} reaches below the top band's floor at {size}.");
            }
        }

        string[] names = band.Keys.ToArray();

        for (int i = 0; i < names.Length; i++)
        {
            for (int j = i + 1; j < names.Length; j++)
            {
                if (IsHost(names[i], names[j]))
                {
                    continue;
                }

                Assert.That(
                    Overlaps(band[names[i]], band[names[j]]),
                    Is.False,
                    $"{names[i]} {band[names[i]]} overlaps {names[j]} {band[names[j]]} at {size} dp.");
            }
        }
    }

    /// <summary>Lays both prefabs out at <paramref name="size"/> dp and reads every piece back in dp.</summary>
    private Dictionary<string, Rect> Lay(Vector2 size)
    {
        Build(size);

        return Pieces();
    }

    /// <summary>The band's pieces, in dp from their safe area's top-left, y down.</summary>
    private Dictionary<string, Rect> Pieces()
    {
        Canvas.ForceUpdateCanvases();

        RectTransform hudSafe = Safe(_hud);
        RectTransform pauseSafe = Safe(_pause);
        float px = PixelsPerDp();

        var pieces = new Dictionary<string, Rect>
        {
            ["strip"] = Dp(hudSafe, "XpStrip", px),
            ["star"] = Dp(hudSafe, "LevelStar", px),
            ["level"] = Dp(hudSafe, "Level", px),
            ["ring"] = Dp(hudSafe, "ShieldRing", px),
            ["bar"] = Dp(hudSafe, "HpBar", px),
            ["numbers"] = Dp(hudSafe, "HpText", px),
            ["boss"] = Dp(hudSafe, "BossBar", px),
            ["pause"] = Dp(pauseSafe, "Icon", px),
        };

        Transform row = hudSafe.Find("AutoCastRow");

        // The first eight of the twelve: the row packs from the front, and past eight a 9:20 portrait
        // runs it under the right-hand corner (the parking lot's line). No shipped class owns more
        // than three Actives.
        for (int cell = 1; cell <= RowCellsChecked; cell++)
        {
            pieces[$"cell{cell}"] = Dp(hudSafe, (RectTransform)row.Find($"Cell{cell}"), px);
        }

        return pieces;
    }

    /// <summary>
    /// Both prefabs, their safe areas sized to <paramref name="size"/> dp, and every placing
    /// component's <c>Place</c> run — none of them needs a run to lay itself out.
    /// </summary>
    private void Build(Vector2 size)
    {
        DestroyHud();

        _hud = Spawn(HudPath);
        _pause = Spawn(PausePath);

        float px = PixelsPerDp();

        foreach (GameObject root in new[] { _hud, _pause })
        {
            RectTransform safe = Safe(root);

            safe.anchorMin = new Vector2(0.5f, 0.5f);
            safe.anchorMax = new Vector2(0.5f, 0.5f);
            safe.pivot = new Vector2(0.5f, 0.5f);
            safe.sizeDelta = size * px;
            safe.anchoredPosition = Vector2.zero;
        }

        Invoke(_hud.GetComponent<HudPresenter>(), "Place");
        Invoke(_hud.GetComponentInChildren<XpBarView>(true), "Place");
        Invoke(_hud.GetComponentInChildren<AutoCastRow>(true), "Place");
        Invoke(_hud.GetComponentInChildren<BossBarView>(true), "Place");
        Invoke(_pause.GetComponent<PausePresenter>(), "Place");
    }

    private GameObject Spawn(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {path}.");

        GameObject root = Object.Instantiate(prefab);

        _spawned.Add(root);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler — XpBarViewTests sets it for the same reason.
        root.GetComponent<Canvas>().scaleFactor = 1f;

        return root;
    }

    private static RectTransform Safe(GameObject root) =>
        (RectTransform)root.GetComponentInChildren<SafeAreaFitter>(true).transform;

    private static Rect Dp(RectTransform safe, string child, float px)
    {
        Transform found = safe.Find(child);

        Assert.That(found, Is.Not.Null, $"no {child} under {safe.name}.");

        return Dp(safe, (RectTransform)found, px);
    }

    /// <summary>A piece's rect in dp from the safe area's top-left corner, y down.</summary>
    private static Rect Dp(RectTransform safe, RectTransform piece, float px)
    {
        var corners = new Vector3[4];

        piece.GetWorldCorners(corners);

        Vector3 bottomLeft = safe.InverseTransformPoint(corners[0]);
        Vector3 topRight = safe.InverseTransformPoint(corners[2]);
        Rect area = safe.rect;

        return Rect.MinMaxRect(
            (bottomLeft.x - area.xMin) / px,
            (area.yMax - topRight.y) / px,
            (topRight.x - area.xMin) / px,
            (area.yMax - bottomLeft.y) / px);
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.xMin < b.xMax - Slack && b.xMin < a.xMax - Slack && a.yMin < b.yMax - Slack && b.yMin < a.yMax - Slack;

    private static bool IsHost(string a, string b) =>
        Hosts.Any(pair => (pair.A == a && pair.B == b) || (pair.A == b && pair.B == a));

    /// <summary>Canvas units per dp at a canvas scale of 1 — every view's <c>PixelsPerDp</c>.</summary>
    private static float PixelsPerDp() => StickShaper.PixelsPerDp(Screen.dpi);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}
