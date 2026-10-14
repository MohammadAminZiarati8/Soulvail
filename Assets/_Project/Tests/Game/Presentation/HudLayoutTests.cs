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
/// <para>
/// <b>UI-08c adds the lower half</b>: the stick's zone and ring, the Roll, the four skill buttons, the
/// threat arrows' lane and the development readout, at the two portrait phones its rules name.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HudLayoutTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const string PausePath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string DebugPath = "Assets/_Project/Prefabs/UI/DebugOverlay.prefab";
    private const string ArrowPath = "Assets/_Project/Prefabs/UI/ThreatArrow.prefab";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// How many of the skill row's Active cells the band must hold at both sizes, after the volley's:
    /// seven 34 dp cells in all reach the enemies chip's column at 9:20 and stop short of it (UI-01e).
    /// </summary>
    private const int RowCellsChecked = 6;

    /// <summary>The most Actives a shipped class owns, so the most cells its row shows after the volley's.</summary>
    private const int ShippedActives = 3;

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

            // UI-01d rules 6 and 7: the right-hand end of the row is on the same line.
            Assert.That(band["pause"].center.y, Is.EqualTo(centre).Within(Slack), $"the pause icon at {size}");
            Assert.That(band["stage"].center.y, Is.EqualTo(centre).Within(Slack), $"the stage chip at {size}");
            Assert.That(band["enemies"].center.x, Is.EqualTo(band["pause"].center.x).Within(Slack), $"the enemies chip at {size}");
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
    public void HudLayout_BadgeOverlapsOnlyTheStar()
    {
        foreach (Vector2 size in new[] { Landscape, Portrait })
        {
            Dictionary<string, Rect> band = Lay(size);

            Rect badge = band["badge"];
            Rect star = band["star"];

            Assert.That(badge.width, Is.EqualTo(HudLayout.PointBadgeSizeDp).Within(Slack), $"at {size}");
            Assert.That(badge.height, Is.EqualTo(HudLayout.PointBadgeSizeDp).Within(Slack), $"at {size}");

            // On the upper-right corner: level with the star's top, hanging a little past its right edge.
            Assert.That(badge.yMin, Is.EqualTo(star.yMin).Within(Slack), $"not on the star's top at {size}");
            Assert.That(
                badge.xMax,
                Is.EqualTo(star.xMax + HudLayout.PointBadgeOverhangDp).Within(Slack),
                $"not on the star's right corner at {size}");
            Assert.That(Overlaps(badge, star), Is.True, $"the badge is not on its star at {size}");

            // Never the strip above the row, never the bar beside it, never the right-hand column.
            foreach (string other in band.Keys.Where(k => k != "badge" && !IsHost("badge", k)))
            {
                Assert.That(
                    Overlaps(badge, band[other]),
                    Is.False,
                    $"the badge {badge} overlaps {other} {band[other]} at {size} dp.");
            }
        }
    }

    [Test]
    public void Band_TheRowStartsUnderTheBar()
    {
        foreach (Vector2 size in new[] { Landscape, Portrait })
        {
            Dictionary<string, Rect> band = Lay(size);

            Assert.That(band["volley"].xMin, Is.EqualTo(band["bar"].xMin).Within(Slack), $"at {size}");
            Assert.That(band["volley"].yMin - band["bar"].yMax, Is.EqualTo(8f).Within(Slack), $"at {size}");

            // The pips under the volley's cell, as wide together as it, and inside the band.
            Assert.That(band["pip1"].xMin, Is.EqualTo(band["volley"].xMin).Within(Slack), $"at {size}");
            Assert.That(band[$"pip{AutoCastRow.MaxPips}"].xMax, Is.EqualTo(band["volley"].xMax).Within(Slack), $"at {size}");
            Assert.That(band["pip1"].yMin - band["volley"].yMax, Is.EqualTo(3f).Within(Slack), $"at {size}");
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

    // ---- Portrait (UI-08c) -----------------------------------------------------------------------

    [Test]
    public void Hud_Canvas_IsReferencedPortrait()
    {
        foreach (string path in new[] { HudPath, DebugPath })
        {
            var scaler = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<CanvasScaler>();

            Assert.That(scaler, Is.Not.Null, $"no CanvasScaler on {path}.");
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize), path);
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)), path);
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f), path);
        }
    }

    [Test]
    public void Hud_Controls_SitInTheThumbZones()
    {
        foreach (Phone phone in Phones)
        {
            Vector2 size = phone.SafeDp;
            Dictionary<string, Rect> controls = LayControls(size);
            string at = phone.ToString();

            // The zone: the left half of the bottom 40 %, measured from the safe area's bottom edge.
            Rect zone = controls["zone"];

            Assert.That(zone.xMin, Is.EqualTo(0f).Within(Slack), $"the zone's left at {at}");
            Assert.That(zone.xMax, Is.EqualTo(size.x * HudPresenter.StickZoneWidth).Within(Slack), $"the zone's right at {at}");
            Assert.That(zone.yMax, Is.EqualTo(size.y).Within(Slack), $"the zone's bottom at {at}");
            Assert.That(zone.yMin, Is.EqualTo(size.y * (1f - HudPresenter.StickZoneHeight)).Within(Slack), $"the zone's top at {at}");

            // The stick in the bottom-left quarter and inside its zone; the rest in the bottom-right.
            Rect stick = controls["stick"];

            Assert.That(stick.xMax, Is.LessThanOrEqualTo((size.x * 0.5f) + Slack), $"the stick crosses the middle at {at}");
            Assert.That(stick.yMin, Is.GreaterThanOrEqualTo(zone.yMin - Slack), $"the stick leaves its zone at {at}");
            AssertClearOfTheEdges("stick", stick, size, at);

            foreach (string name in RightThumb)
            {
                Rect r = controls[name];

                Assert.That(r.xMin, Is.GreaterThanOrEqualTo((size.x * 0.5f) - Slack), $"{name} {r} crosses the middle at {at}");
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo((size.y * 0.5f) - Slack), $"{name} {r} leaves the bottom half at {at}");
                AssertClearOfTheEdges(name, r, size, at);
            }
        }
    }

    [Test]
    public void Hud_DebugOverlay_ClearsTheTopBand()
    {
        foreach (Phone phone in Phones)
        {
            Vector2 size = phone.SafeDp;
            Dictionary<string, Rect> band = Lay(size);

            GameObject overlay = Spawn(DebugPath);
            RectTransform safe = Size(overlay, size);

            Invoke(overlay.GetComponent<DebugOverlay>(), "Place");
            Canvas.ForceUpdateCanvases();

            Rect line = Dp(safe, "Line", PixelsPerDp());
            string at = phone.ToString();

            Assert.That(line.yMin, Is.GreaterThanOrEqualTo(band["boss"].yMax - Slack), $"the line {line} reaches the boss band at {at}");
            Assert.That(line.yMin, Is.GreaterThanOrEqualTo(band["strip"].yMax - Slack), $"the line {line} reaches the XP strip at {at}");
            Assert.That(line.xMin, Is.GreaterThanOrEqualTo(HudLayout.MarginDp - Slack), $"at {at}");
            Assert.That(line.xMax, Is.LessThanOrEqualTo(size.x - HudLayout.MarginDp + Slack), $"the line leaves the safe area at {at}");

            foreach (KeyValuePair<string, Rect> piece in band.Where(p => !BelowTheBand.Contains(p.Key)))
            {
                Assert.That(Overlaps(line, piece.Value), Is.False, $"the line {line} overlaps {piece.Key} {piece.Value} at {at}");
            }
        }
    }

    [Test]
    public void Hud_Portrait_NothingOverlaps()
    {
        foreach (Phone phone in Phones)
        {
            Vector2 size = phone.SafeDp;
            string at = phone.ToString();

            // Rule 2: the band as UI-01b laid it out, at this phone's size, as a shipped run draws it:
            // the cells a shipped class can fill, and without the two economy readouts every shipped
            // mode switches off. At 360 dp a sixth Active's cell reaches under the pause icon, and the
            // Veilrot meter's caption into the level-up toast — the parking lot's two lines.
            AssertBandHolds(size, ShippedActives, shippedOnly: true);

            Dictionary<string, Rect> band = Shipped(Pieces(ShippedActives));
            Dictionary<string, Rect> controls = LayControls(size);
            string[] lower = new[] { "stick" }.Concat(RightThumb).ToArray();

            // The controls against each other, against every piece of the band and against the boss band.
            for (int i = 0; i < lower.Length; i++)
            {
                for (int j = i + 1; j < lower.Length; j++)
                {
                    Assert.That(
                        Overlaps(controls[lower[i]], controls[lower[j]]),
                        Is.False,
                        $"{lower[i]} {controls[lower[i]]} overlaps {lower[j]} {controls[lower[j]]} at {at}");
                }

                foreach (KeyValuePair<string, Rect> piece in band)
                {
                    Assert.That(
                        Overlaps(controls[lower[i]], piece.Value),
                        Is.False,
                        $"{lower[i]} {controls[lower[i]]} overlaps {piece.Key} {piece.Value} at {at}");
                }
            }

            // The threat arrows ride a border held inside their lane, and a corner arrow reaches past
            // that border by half its diagonal: the lane's top keeps that reach off the boss band, and
            // its three other sides keep it off every control.
            Rect lane = controls["lane"];
            float reach = ArrowReachDp(phone, inward: true);
            float overhang = ArrowReachDp(phone, inward: false);

            Assert.That(lane.yMin - overhang, Is.GreaterThanOrEqualTo(band["boss"].yMax - Slack), $"an arrow reaches the boss band at {at}");
            Assert.That(lane.yMax, Is.EqualTo(size.y).Within(Slack), $"the lane stops short of the bottom at {at}");

            foreach (string name in lower)
            {
                Rect r = controls[name];

                Assert.That(r.xMin - lane.xMin, Is.GreaterThanOrEqualTo(reach), $"an arrow on the left edge reaches {name} at {at}");
                Assert.That(lane.xMax - r.xMax, Is.GreaterThanOrEqualTo(reach), $"an arrow on the right edge reaches {name} at {at}");
                Assert.That(lane.yMax - r.yMax, Is.GreaterThanOrEqualTo(reach), $"an arrow on the bottom edge reaches {name} at {at}");
            }
        }
    }

    /// <summary>The two phones UI-08c's rules are written against, as their safe areas in dp.</summary>
    private static readonly Phone[] Phones =
    {
        new Phone(1080, 2400, 420f),
        new Phone(720, 1600, 320f),
    };

    /// <summary>The Roll and the four skill buttons: the right thumb's controls.</summary>
    private static readonly string[] RightThumb = { "roll", "s1", "s2", "s3", "s4" };

    /// <summary>UI-08c rule 3's distance from the safe area's edges, in dp.</summary>
    private const float EdgeClearDp = 24f;

    private static void AssertClearOfTheEdges(string name, Rect r, Vector2 size, string at)
    {
        Assert.That(r.xMin, Is.GreaterThanOrEqualTo(EdgeClearDp - Slack), $"{name} {r} is within 24 dp of the left edge at {at}");
        Assert.That(size.x - r.xMax, Is.GreaterThanOrEqualTo(EdgeClearDp - Slack), $"{name} {r} is within 24 dp of the right edge at {at}");
        Assert.That(size.y - r.yMax, Is.GreaterThanOrEqualTo(EdgeClearDp - Slack), $"{name} {r} is within 24 dp of the bottom edge at {at}");
    }

    /// <summary>
    /// The HUD at <paramref name="size"/> dp with every lower control placed as its own component
    /// places it, read back in dp: the stick's zone and ring, the Roll, the four skill buttons and the
    /// threat arrows' lane.
    /// </summary>
    private Dictionary<string, Rect> LayControls(Vector2 size)
    {
        Build(size);

        Invoke(_hud.GetComponentInChildren<SkillButton>(true), "Place");
        Invoke(_hud.GetComponentInChildren<SkillBarPresenter>(true), "Place");

        // The stick places its ring in Awake, from its zone's bottom-left corner, after HudPresenter's
        // Start has sized the zone — the order a run gets, since every Awake precedes every Start.
        Invoke(_hud.GetComponentInChildren<FloatingStick>(true), "Awake");

        Canvas.ForceUpdateCanvases();

        RectTransform safe = Safe(_hud);
        float px = PixelsPerDp();

        return new Dictionary<string, Rect>
        {
            ["zone"] = Dp(safe, "Region", px),
            ["stick"] = Dp(safe, "Region/Base", px),
            ["roll"] = Dp(safe, "SkillButton", px),
            ["s1"] = Dp(safe, "SkillBar/S1", px),
            ["s2"] = Dp(safe, "SkillBar/S2", px),
            ["s3"] = Dp(safe, "SkillBar/S3", px),
            ["s4"] = Dp(safe, "SkillBar/S4", px),
            ["lane"] = Dp(safe, "Arrows", px),
        };
    }

    /// <summary>
    /// How far a threat arrow reaches, in dp on <paramref name="phone"/>: <paramref name="inward"/>,
    /// from its lane's edge to the far side of a corner arrow; otherwise, past the lane's edge.
    /// </summary>
    /// <remarks>
    /// The arrow and its border inset are canvas units rather than dp, so the answer depends on the
    /// phone's canvas scale, which is Hud.prefab's <c>CanvasScaler</c> applied to its pixels. The
    /// Editor's own scale is pinned to 1 here and says nothing about a phone (Traps §9).
    /// </remarks>
    private static float ArrowReachDp(Phone phone, bool inward)
    {
        float edge = Constant(typeof(ThreatArrows), "EdgeMargin");
        float near = Constant(typeof(ThreatArrows), "NearScale");
        var arrow = (RectTransform)AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath).transform;
        float halfDiagonal = Mathf.Max(arrow.sizeDelta.x, arrow.sizeDelta.y) * near * Mathf.Sqrt(2f) * 0.5f;

        var scaler = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponent<CanvasScaler>();
        Vector2 reference = scaler.referenceResolution;
        float scale = Mathf.Pow(
            2f,
            Mathf.Lerp(
                Mathf.Log(phone.Width / reference.x, 2f),
                Mathf.Log(phone.Height / reference.y, 2f),
                scaler.matchWidthOrHeight));

        float units = inward ? edge + halfDiagonal : Mathf.Max(0f, halfDiagonal - edge);

        return units * scale / StickShaper.PixelsPerDp(phone.Dpi);
    }

    private static float Constant(System.Type type, string name) =>
        (float)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();

    /// <summary>A phone: its pixels and its density, and the safe area that makes in dp.</summary>
    private readonly struct Phone
    {
        public Phone(int width, int height, float dpi)
        {
            Width = width;
            Height = height;
            Dpi = dpi;
        }

        public int Width { get; }

        public int Height { get; }

        public float Dpi { get; }

        public Vector2 SafeDp => new Vector2(Width, Height) / StickShaper.PixelsPerDp(Dpi);

        public override string ToString() => $"{Width} × {Height} @ {Dpi} dpi";
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

        // UI-03b: the badge hangs on the star's corner, so it overlaps the star and the two pieces drawn
        // in the star — the ring behind it and the level inside it — and nothing else.
        ("badge", "star"),
        ("badge", "ring"),
        ("badge", "level"),
    };

    /// <summary>
    /// The pieces that hang below the band by design: the boss band, and the right-hand column the
    /// economy readouts stepped down to at UI-01d.
    /// </summary>
    private static readonly HashSet<string> BelowTheBand = new HashSet<string>
    {
        "boss", "essence", "essenceWord", "meter", "meterWord", "claimed", "toast",
    };

    /// <summary>
    /// The Essence counter and the Veilrot meter, with their words: switched off in every shipped
    /// mode (RS-05b, RS-05d), and switched off is not removed.
    /// </summary>
    private static readonly HashSet<string> SwitchedOff = new HashSet<string>
    {
        "essence", "essenceWord", "meter", "meterWord", "claimed",
    };

    /// <summary><paramref name="band"/> as a shipped run draws it: without <see cref="SwitchedOff"/>.</summary>
    private static Dictionary<string, Rect> Shipped(Dictionary<string, Rect> band) =>
        band.Where(p => !SwitchedOff.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);

    private void AssertBandHolds(Vector2 size, int cells = RowCellsChecked, bool shippedOnly = false)
    {
        Dictionary<string, Rect> band = Lay(size, cells);

        if (shippedOnly)
        {
            band = Shipped(band);
        }

        foreach (KeyValuePair<string, Rect> piece in band)
        {
            Rect r = piece.Value;

            Assert.That(r.width, Is.GreaterThan(0f), $"{piece.Key} has no width at {size}.");
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-Slack), $"{piece.Key} leaves the safe area's left at {size}.");
            Assert.That(r.xMax, Is.LessThanOrEqualTo(size.x + Slack), $"{piece.Key} leaves the safe area's right at {size}.");
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-Slack), $"{piece.Key} leaves the safe area's top at {size}.");

            if (!BelowTheBand.Contains(piece.Key))
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
    private Dictionary<string, Rect> Lay(Vector2 size, int cells = RowCellsChecked)
    {
        Build(size);

        return Pieces(cells);
    }

    /// <summary>The band's pieces, in dp from their safe area's top-left, y down.</summary>
    private Dictionary<string, Rect> Pieces(int cells = RowCellsChecked)
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

            // UI-01d: the right-hand chips, and the two switched-off economy readouts they displaced.
            ["stage"] = Dp(hudSafe, "StageReadout/StageChip", px),
            ["enemies"] = Dp(hudSafe, "StageReadout/EnemiesChip", px),
            ["essence"] = Dp(hudSafe, "EssenceCounter", px),
            ["essenceWord"] = Dp(hudSafe, "EssenceLabel", px),
            ["meter"] = Dp(hudSafe, "VeilrotMeter", px),
            ["meterWord"] = Dp(hudSafe, "VeilrotLabel", px),
            ["claimed"] = Dp(hudSafe, "ClaimedLabel", px),

            // UI-03b: the point badge on the star's corner, and the toast above the arena's centre.
            ["badge"] = Dp(hudSafe, "PointBadge/Badge", px),
            ["toast"] = Dp(hudSafe, "PointBadge/LevelUpToast", px),
        };

        Transform row = hudSafe.Find("AutoCastRow");

        // The volley's cell, its five pips, and the first `cells` Active cells after it (six unless a row
        // asks for fewer): the row packs from the front, and past the seventh cell a 411 dp portrait runs
        // it under the right-hand column, past the sixth a 360 dp one (the parking lot's line). No
        // shipped class owns more than three Actives.
        var volley = (RectTransform)row.Find("VolleyCell");

        pieces["volley"] = Dp(hudSafe, volley, px);

        for (int pip = 1; pip <= AutoCastRow.MaxPips; pip++)
        {
            pieces[$"pip{pip}"] = Dp(hudSafe, (RectTransform)volley.Find($"Pip{pip}"), px);
        }

        for (int cell = 1; cell <= cells; cell++)
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

        Size(_hud, size);
        Size(_pause, size);

        Invoke(_hud.GetComponent<HudPresenter>(), "Place");
        Invoke(_hud.GetComponentInChildren<XpBarView>(true), "Place");
        // Laid out as a Ranger's row is once Volley is taken: the volley's cell first, and every Active
        // cell one slot right of it (UI-01e rule 1).
        AutoCastRow row = _hud.GetComponentInChildren<AutoCastRow>(true);

        SetField(row, "_hasVolley", true);
        Invoke(row, "Place");
        Invoke(_hud.GetComponentInChildren<BossBarView>(true), "Place");
        Invoke(_pause.GetComponent<PausePresenter>(), "Place");
        Invoke(_hud.GetComponentInChildren<StageReadout>(true), "Place");
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

    /// <summary>Sizes <paramref name="root"/>'s safe area to <paramref name="size"/> dp, and returns it.</summary>
    private static RectTransform Size(GameObject root, Vector2 size)
    {
        RectTransform safe = Safe(root);

        safe.anchorMin = new Vector2(0.5f, 0.5f);
        safe.anchorMax = new Vector2(0.5f, 0.5f);
        safe.pivot = new Vector2(0.5f, 0.5f);
        safe.sizeDelta = size * PixelsPerDp();
        safe.anchoredPosition = Vector2.zero;

        return safe;
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
