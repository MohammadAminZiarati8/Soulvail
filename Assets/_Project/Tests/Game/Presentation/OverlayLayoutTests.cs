using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-08e: Pause and Run end on a portrait phone — one centred column no wider than the safe width
/// less 16 dp a side, every button a thumb tall, every word whole and over 12 sp — and all three
/// overlay canvases referenced upright.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shipped prefabs, laid out and never injected.</b> Neither column reads anything a
/// <c>Construct</c> hands in, so a row that built a run to reach one would be building something the
/// layout cannot see. Each row lays the screen out with the px per dp its phone would have rather
/// than the Editor's <c>Screen.dpi</c> (Traps §9), through each presenter's <c>UsePixelsPerDp</c>.
/// </para>
/// <para>
/// The words are the shipped English ones, written straight onto the labels, because <c>Start</c>
/// — which writes them in a build — does not run in EditMode (Traps §5).
/// </para>
/// </remarks>
[TestFixture]
public sealed class OverlayLayoutTests
{
    private const string PausePath = "Assets/_Project/Prefabs/UI/Pause.prefab";
    private const string RunEndPath = "Assets/_Project/Prefabs/UI/RunEnd.prefab";
    private const string TreePath = "Assets/_Project/Prefabs/UI/TreeView.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const float DpiPerDp = 160f;
    private const float SideDp = 16f;
    private const float ThumbDp = 48f;
    private const float FloorSp = 12f;

    /// <summary>UI-08e rule 4's two phones, held upright: pixels, and dpi.</summary>
    private static readonly (Vector2 Pixels, float Dpi)[] Phones =
    {
        (new Vector2(1080f, 2400f), 420f),
        (new Vector2(720f, 1600f), 320f),
    };

    private static readonly string[] PauseButtons = { "_resume", "_skills", "_viewTree", "_quit" };

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void DestroyWorld()
    {
        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();
    }

    [Test]
    public void Overlays_Canvas_IsReferencedPortrait()
    {
        foreach (string path in new[] { TreePath, PausePath, RunEndPath })
        {
            var scaler = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<CanvasScaler>();

            Assert.That(scaler, Is.Not.Null, $"no CanvasScaler on {path}.");
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize), path);
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)), path);
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f), path);
        }
    }

    [Test]
    public void Pause_Portrait_FitsTheSafeWidth()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            PausePresenter pause = OpenPause(pixels, dpi, out float px, out Rect safe);
            var rects = new List<(string Name, Rect Rect)>();

            foreach (string field in PauseButtons)
            {
                rects.Add((field, WorldRect(Field<Button>(pause, field))));
            }

            AssertColumn(rects, safe, px, pixels, thumb: rects);

            // UI-09b rule 7: the stone and its ribbon stay inside the safe area too.
            float widest = safe.width - (2f * SideDp * px);

            foreach (string field in new[] { "_frame", "_ribbon" })
            {
                Rect rect = WorldRect(Field<Image>(pause, field));

                Assert.That(Contains(safe, rect), Is.True, $"{pixels}: {field} {rect} leaves the safe area {safe}.");
                Assert.That(rect.width, Is.LessThanOrEqualTo(widest + 0.5f), $"{pixels}: {field} is {rect.width / px} dp wide.");
            }
        }
    }

    [Test]
    public void Pause_Frame_WrapsTheShownButtons()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            foreach (bool withTree in new[] { true, false })
            {
                PausePresenter pause = OpenPause(pixels, dpi, out float px, out Rect safe);

                if (!withTree)
                {
                    Field<Button>(pause, "_viewTree").gameObject.SetActive(false);
                    pause.UsePixelsPerDp(px);
                    Canvas.ForceUpdateCanvases();
                }

                Image frame = Field<Image>(pause, "_frame");
                Rect first = WorldRect(Field<Button>(pause, "_resume"));
                Rect last = WorldRect(Field<Button>(pause, "_quit"));
                Rect rect = WorldRect(frame);
                string at = $"{pixels}, tree {withTree}";

                Assert.That(frame.sprite.name, Is.EqualTo("Popup_Panel"), at);
                Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced), at);
                Assert.That(rect.width, Is.EqualTo(first.width + (48f * px)).Within(0.5f), $"{at}: 24 dp a side.");
                Assert.That(rect.yMax, Is.EqualTo(first.yMax + (40f * px)).Within(0.5f), $"{at}: 40 dp above the first button.");
                Assert.That(rect.yMin, Is.EqualTo(last.yMin - (24f * px)).Within(0.5f), $"{at}: 24 dp below the last button.");
                Assert.That(rect.center.x, Is.EqualTo(safe.center.x).Within(0.5f), $"{at}: centred on the safe area.");
                Assert.That(frame.transform.GetSiblingIndex(), Is.LessThan(Field<Button>(pause, "_resume").transform.GetSiblingIndex()), $"{at}: behind the buttons.");
                Assert.That(frame.transform.GetSiblingIndex(), Is.GreaterThan(frame.transform.parent.Find("Scrim").GetSiblingIndex()), $"{at}: over the scrim.");

                float expected = withTree ? (4 * 56f) + (3 * 12f) : (3 * 56f) + (2 * 12f);

                Assert.That(first.yMax - last.yMin, Is.EqualTo(expected * px).Within(0.5f), $"{at}: the frame fits the shown buttons, no empty slot.");
            }
        }
    }

    [Test]
    public void Pause_Ribbon_StraddlesTheFrameTop()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            PausePresenter pause = OpenPause(pixels, dpi, out float px, out _);
            Image ribbon = Field<Image>(pause, "_ribbon");
            TMP_Text title = Field<TMP_Text>(pause, "_titleLabel");
            Rect rect = WorldRect(ribbon);
            Rect frame = WorldRect(Field<Image>(pause, "_frame"));
            Rect column = WorldRect(Field<Button>(pause, "_resume"));

            Assert.That(ribbon.sprite.name, Is.EqualTo("Popup_TitleRibbon"), $"{pixels}");
            Assert.That(ribbon.type, Is.EqualTo(Image.Type.Sliced), $"{pixels}");
            Assert.That(rect.width, Is.EqualTo(column.width + (48f * px)).Within(0.5f), $"{pixels}: the column plus 48 dp.");
            Assert.That(rect.height, Is.EqualTo(64f * px).Within(0.5f), $"{pixels}: 64 dp tall.");
            Assert.That(rect.center.y, Is.EqualTo(frame.yMax).Within(0.5f), $"{pixels}: centred on the frame's top edge.");
            Assert.That(rect.center.x, Is.EqualTo(frame.center.x).Within(0.5f), $"{pixels}");
            Assert.That(title.transform.parent, Is.EqualTo(ribbon.transform), "the title sits on the ribbon.");
            Assert.That(title.fontSize / px, Is.EqualTo(28f).Within(1e-3f), $"{pixels}: 28 dp.");
            Assert.That(title.color, Is.EqualTo(Palette.HudText), $"{pixels}");
            Assert.That(title.alignment, Is.EqualTo(TextAlignmentOptions.Center), $"{pixels}");
        }
    }

    [Test]
    public void Pause_Buttons_WearTheirSprites()
    {
        GameObject root = Spawn(PausePath);
        var pause = root.GetComponent<PausePresenter>();

        foreach ((string field, string label, string face, string pressed) in new[]
        {
            ("_resume", "_resumeLabel", "Button_Green", "Button_Green_Pressed"),
            ("_skills", "_skillsLabel", "Button_Blue", "Button_Blue_Pressed"),
            ("_viewTree", "_viewTreeLabel", "Button_Blue", "Button_Blue_Pressed"),
            ("_quit", "_quitLabel", "Button_Grey", null),
        })
        {
            Button button = Field<Button>(pause, field);
            var image = (Image)button.targetGraphic;

            Assert.That(image.sprite.name, Is.EqualTo(face), field);
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced), field);
            Assert.That(image.color, Is.EqualTo(Color.white), $"{field}: the art is not tinted.");
            Assert.That(Field<TMP_Text>(pause, label).color, Is.EqualTo(Palette.HudText), label);

            if (pressed != null)
            {
                Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap), field);
                Assert.That(button.spriteState.pressedSprite.name, Is.EqualTo(pressed), field);
            }
            else
            {
                Assert.That(button.transition, Is.EqualTo(Selectable.Transition.ColorTint), $"{field}: no pressed sprite, so a tint.");
                Assert.That(button.colors.pressedColor, Is.EqualTo(new Color(0.8f, 0.8f, 0.8f, 1f)), field);
            }
        }
    }

    [Test]
    public void Pause_Glyphs_SitLeftOfTheLabel()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            PausePresenter pause = OpenPause(pixels, dpi, out float px, out _);

            foreach ((string field, string label, string glyph) in new[]
            {
                ("_resume", "_resumeLabel", "Icon_Resume"),
                ("_skills", "_skillsLabel", "Icon_Skills"),
                ("_viewTree", "_viewTreeLabel", "Icon_Tree"),
                ("_quit", "_quitLabel", "Icon_Quit"),
            })
            {
                Button button = Field<Button>(pause, field);
                var image = button.transform.Find("Glyph").GetComponent<Image>();
                Rect buttonRect = WorldRect(button);
                Rect rect = WorldRect(image);
                Rect labelRect = WorldRect(Field<TMP_Text>(pause, label));
                string at = $"{pixels}: {field}";

                Assert.That(image.sprite.name, Is.EqualTo(glyph), at);
                Assert.That(image.raycastTarget, Is.False, $"{at}: the glyph takes no tap from its button.");
                Assert.That(rect.width / px, Is.EqualTo(28f).Within(1e-3f), $"{at}: 28 dp wide.");
                Assert.That(rect.height / px, Is.EqualTo(28f).Within(1e-3f), $"{at}: 28 dp tall.");
                Assert.That((rect.xMin - buttonRect.xMin) / px, Is.EqualTo(16f).Within(1e-3f), $"{at}: 16 dp in.");
                Assert.That(rect.center.y, Is.EqualTo(buttonRect.center.y).Within(0.5f), $"{at}: centred on the height.");
                Assert.That(labelRect.xMin, Is.EqualTo(rect.xMax).Within(0.5f), $"{at}: the label starts right of the glyph.");
                Assert.That(labelRect.xMax, Is.EqualTo(buttonRect.xMax).Within(0.5f), $"{at}: and runs to the button's right edge.");
            }
        }
    }

    [Test]
    public void Pause_Title_IsWritten()
    {
        var key = new LocKey("ui.pause.title");

        Assert.That(Shipped().Get(key), Is.EqualTo("Paused"));

        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(LocalisationSweepTests.PseudoPath);

        Assert.That(pseudo, Is.Not.Null, $"No localization table at {LocalisationSweepTests.PseudoPath}.");
        Assert.That(pseudo.ToDictionary().ContainsKey(key), Is.True, "Pseudo has no ui.pause.title row; regenerate it.");
    }

    [Test]
    public void RunEnd_Portrait_FitsTheSafeWidth()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out Rect safe);
            var rects = new List<(string Name, Rect Rect)>
            {
                ("_title", WorldRect(Field<TMP_Text>(end, "_title"))),
                ("depth row", WorldRect(Field<TMP_Text>(end, "_depthLabel").transform.parent)),
                ("bosses row", WorldRect(Field<TMP_Text>(end, "_bossLabel").transform.parent)),
                ("shards row", WorldRect(Field<TMP_Text>(end, "_shardLabel").transform.parent)),
                ("_return", WorldRect(Field<Button>(end, "_return"))),
            };

            AssertColumn(rects, safe, px, pixels, thumb: rects.GetRange(4, 1));
        }
    }

    [Test]
    public void Overlays_Portrait_TextClears12Sp()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            PausePresenter pause = OpenPause(pixels, dpi, out float px, out _);

            AssertWords(pause.gameObject, px, pixels);

            RunEndPresenter end = OpenRunEnd(pixels, dpi, out px, out _);

            AssertWords(end.gameObject, px, pixels);
        }
    }

    /// <summary>
    /// One centred column: every piece inside the safe area, no wider than its width less 16 dp a
    /// side, centred on it, stacked without overlap, and each of <paramref name="thumb"/> 48 dp tall.
    /// </summary>
    private static void AssertColumn(
        List<(string Name, Rect Rect)> rects,
        Rect safe,
        float px,
        Vector2 pixels,
        List<(string Name, Rect Rect)> thumb)
    {
        float widest = safe.width - (2f * SideDp * px);

        foreach ((string name, Rect rect) in rects)
        {
            Assert.That(rect.width, Is.LessThanOrEqualTo(widest + 0.5f), $"{pixels}: {name} is {rect.width / px} dp wide.");
            Assert.That(Mathf.Abs(rect.center.x - safe.center.x), Is.LessThanOrEqualTo(0.5f), $"{pixels}: {name} is off the centre line.");
            Assert.That(Contains(safe, rect), Is.True, $"{pixels}: {name} {rect} leaves the safe area {safe}.");
        }

        foreach ((string name, Rect rect) in thumb)
        {
            Assert.That(rect.height / px, Is.GreaterThanOrEqualTo(ThumbDp - 1e-3f), $"{pixels}: {name} is {rect.height / px} dp tall.");
        }

        for (int a = 0; a < rects.Count; a++)
        {
            for (int b = a + 1; b < rects.Count; b++)
            {
                Rect first = Shrink(rects[a].Rect);
                Rect second = Shrink(rects[b].Rect);

                Assert.That(first.Overlaps(second), Is.False, $"{pixels}: {rects[a].Name} overlaps {rects[b].Name}.");

                // Top to bottom in the order given.
                Assert.That(rects[a].Rect.center.y, Is.GreaterThan(rects[b].Rect.center.y), $"{pixels}: {rects[a].Name} is not above {rects[b].Name}.");
            }
        }
    }

    /// <summary>Every shown word at 12 sp or more, and whole in its rect.</summary>
    private static void AssertWords(GameObject screen, float px, Vector2 pixels)
    {
        Canvas.ForceUpdateCanvases();

        foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(FloorSp - 1e-3f), $"{pixels}: {text.name} is {text.fontSize / px} sp.");

            Vector2 wanted = text.GetPreferredValues(text.text);
            Rect rect = text.rectTransform.rect;

            Assert.That(wanted.x, Is.LessThanOrEqualTo(rect.width + 0.5f), $"{pixels}: '{text.text}' wants {wanted.x} of {rect.width}.");
            Assert.That(text.fontSize, Is.LessThanOrEqualTo(rect.height + 0.5f), $"{pixels}: '{text.text}' is clipped top and bottom.");
        }
    }

    private PausePresenter OpenPause(Vector2 pixels, float dpi, out float px, out Rect safe)
    {
        GameObject root = Spawn(PausePath);
        var pause = root.GetComponent<PausePresenter>();

        Assert.That(pause, Is.Not.Null, "PausePresenter did not load off its prefab (Traps §5).");

        ILocalizer english = Shipped();

        Field<TMP_Text>(pause, "_resumeLabel").text = english.Get(new LocKey("ui.pause.resume"));
        Field<TMP_Text>(pause, "_skillsLabel").text = english.Get(new LocKey("ui.pause.skills"));
        Field<TMP_Text>(pause, "_viewTreeLabel").text = english.Format(new LocKey("ui.pause.tree.points"), 3);
        Field<TMP_Text>(pause, "_quitLabel").text = english.Get(new LocKey("ui.pause.quit"));
        Field<TMP_Text>(pause, "_titleLabel").text = english.Get(new LocKey("ui.pause.title"));

        px = dpi / DpiPerDp;
        safe = PinSafe(root, pixels);

        pause.UsePixelsPerDp(px);
        Canvas.ForceUpdateCanvases();

        return pause;
    }

    private RunEndPresenter OpenRunEnd(Vector2 pixels, float dpi, out float px, out Rect safe)
    {
        GameObject root = Spawn(RunEndPath);
        var end = root.GetComponent<RunEndPresenter>();

        Assert.That(end, Is.Not.Null, "RunEndPresenter did not load off its prefab (Traps §5).");

        ILocalizer english = Shipped();

        Field<TMP_Text>(end, "_title").text = english.Get(new LocKey("ui.death.title"));
        Field<TMP_Text>(end, "_returnLabel").text = english.Get(new LocKey("ui.death.hint"));
        Field<TMP_Text>(end, "_depthLabel").text = english.Get(new LocKey("ui.runend.depth"));
        Field<TMP_Text>(end, "_bossLabel").text = english.Get(new LocKey("ui.runend.bosses"));
        Field<TMP_Text>(end, "_shardLabel").text = english.Get(new LocKey("ui.runend.shards"));

        // The widest figures a long run draws.
        Field<TMP_Text>(end, "_depth").text = "999";
        Field<TMP_Text>(end, "_bosses").text = "99";
        Field<TMP_Text>(end, "_shards").text = "99999";

        px = dpi / DpiPerDp;
        safe = PinSafe(root, pixels);

        end.UsePixelsPerDp(px);
        Canvas.ForceUpdateCanvases();

        return end;
    }

    /// <summary>The prefab's safe area pinned to <paramref name="pixels"/>, centred; its world rect.</summary>
    private static Rect PinSafe(GameObject root, Vector2 pixels)
    {
        var safe = (RectTransform)root.GetComponentInChildren<SafeAreaFitter>(true).transform;

        safe.anchorMin = new Vector2(0.5f, 0.5f);
        safe.anchorMax = new Vector2(0.5f, 0.5f);
        safe.pivot = new Vector2(0.5f, 0.5f);
        safe.sizeDelta = pixels;
        safe.anchoredPosition = Vector2.zero;

        Canvas.ForceUpdateCanvases();

        return WorldRect(safe);
    }

    private GameObject Spawn(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {path}.");

        GameObject root = Object.Instantiate(prefab);

        _spawned.Add(root);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its scaler.
        root.GetComponent<Canvas>().scaleFactor = 1f;

        return root;
    }

    private static ILocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    private static bool Contains(Rect outer, Rect inner, float slack = 0.5f) =>
        inner.xMin >= outer.xMin - slack && inner.xMax <= outer.xMax + slack
        && inner.yMin >= outer.yMin - slack && inner.yMax <= outer.yMax + slack;

    private static Rect Shrink(Rect rect) =>
        Rect.MinMaxRect(rect.xMin + 0.5f, rect.yMin + 0.5f, rect.xMax - 0.5f, rect.yMax - 0.5f);

    private static Rect WorldRect(Component piece) => WorldRect((RectTransform)piece.transform);

    private static Rect WorldRect(Transform piece)
    {
        var corners = new Vector3[4];

        ((RectTransform)piece).GetWorldCorners(corners);

        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
}
