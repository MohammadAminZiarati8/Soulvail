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
            foreach (bool withLine in new[] { false, true })
            {
                RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out Rect safe, withLine);
                // The emblem overlaps the ribbon by design (rule 2), so it is held to the safe area
                // below rather than to the column's no-overlap rule.
                var rects = new List<(string Name, Rect Rect)>
                {
                    ("_title", WorldRect(Field<TMP_Text>(end, "_title"))),
                };

                if (withLine)
                {
                    // The archway and its words are centred together (rule 6), so the pair is the piece.
                    Rect words = WorldRect(Field<TMP_Text>(end, "_opened"));
                    Rect arch = WorldRect(Field<Image>(end, "_openedIcon"));

                    rects.Add(("opened line", Rect.MinMaxRect(
                        Mathf.Min(words.xMin, arch.xMin), Mathf.Min(words.yMin, arch.yMin),
                        Mathf.Max(words.xMax, arch.xMax), Mathf.Max(words.yMax, arch.yMax))));
                }

                rects.Add(("depth row", WorldRect(Field<TMP_Text>(end, "_depthLabel").transform.parent)));
                rects.Add(("bosses row", WorldRect(Field<TMP_Text>(end, "_bossLabel").transform.parent)));
                rects.Add(("shards row", WorldRect(Field<TMP_Text>(end, "_shardLabel").transform.parent)));
                rects.Add(("_return", WorldRect(Field<Button>(end, "_return"))));

                AssertColumn(rects, safe, px, pixels, thumb: rects.GetRange(rects.Count - 1, 1));

                // UI-09c rule 8: the emblem's top and the stone stay inside the safe area too.
                Rect emblem = WorldRect(Field<Image>(end, "_emblem"));
                Rect frame = WorldRect(Field<Image>(end, "_frame"));

                Assert.That(Contains(safe, emblem), Is.True, $"{pixels}, line {withLine}: the emblem {emblem} leaves the safe area {safe}.");

                Assert.That(Contains(safe, frame), Is.True, $"{pixels}, line {withLine}: the frame {frame} leaves the safe area {safe}.");
                Assert.That(frame.width, Is.LessThanOrEqualTo(safe.width - (2f * SideDp * px) + 0.5f), $"{pixels}: the frame is {frame.width / px} dp wide.");
                AssertWords(end.gameObject, px, pixels);
            }
        }
    }

    [Test]
    public void RunEnd_Frame_HoldsTheColumn()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out Rect safe);
            Image frame = Field<Image>(end, "_frame");
            Rect rect = WorldRect(frame);
            Rect row = WorldRect(Field<TMP_Text>(end, "_depthLabel").transform.parent);
            Rect exit = WorldRect(Field<Button>(end, "_return"));
            Rect ribbon = WorldRect(Field<Image>(end, "_ribbon"));

            Assert.That(frame.sprite.name, Is.EqualTo("Popup_Panel"), $"{pixels}");
            Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced), $"{pixels}");
            Assert.That(frame.transform.GetSiblingIndex(), Is.Zero, $"{pixels}: the frame is drawn behind everything in the safe area.");
            Assert.That(rect.width, Is.EqualTo(row.width + (48f * px)).Within(0.5f), $"{pixels}: the column plus 24 dp a side.");
            Assert.That(rect.center.x, Is.EqualTo(safe.center.x).Within(0.5f), $"{pixels}: centred on the safe area.");
            Assert.That(rect.center.y, Is.EqualTo(safe.center.y).Within(0.5f), $"{pixels}: centred on the safe area.");
            Assert.That(rect.yMax, Is.EqualTo(ribbon.center.y).Within(0.5f), $"{pixels}: it holds the ribbon's lower half.");
            Assert.That(rect.yMin, Is.EqualTo(exit.yMin - (24f * px)).Within(0.5f), $"{pixels}: 24 dp below Return.");
            Assert.That(Contains(rect, row) && Contains(rect, exit), Is.True, $"{pixels}: the rows and Return are on the stone.");
        }
    }

    [Test]
    public void RunEnd_EmblemAndRibbon_StackOnTheFrameTop()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out _);
            Rect emblem = WorldRect(Field<Image>(end, "_emblem"));
            Image ribbonImage = Field<Image>(end, "_ribbon");
            Rect ribbon = WorldRect(ribbonImage);
            Rect frame = WorldRect(Field<Image>(end, "_frame"));
            Rect row = WorldRect(Field<TMP_Text>(end, "_depthLabel").transform.parent);
            TMP_Text title = Field<TMP_Text>(end, "_title");

            Assert.That(ribbonImage.type, Is.EqualTo(Image.Type.Sliced), $"{pixels}");
            Assert.That(ribbon.width, Is.EqualTo(row.width + (48f * px)).Within(0.5f), $"{pixels}: the column plus 48 dp.");
            Assert.That(ribbon.height, Is.EqualTo(72f * px).Within(0.5f), $"{pixels}: 72 dp tall.");
            Assert.That(ribbon.center.y, Is.EqualTo(frame.yMax).Within(0.5f), $"{pixels}: centred on the frame's top edge.");
            Assert.That(emblem.width / px, Is.EqualTo(96f).Within(1e-3f), $"{pixels}: 96 dp square.");
            Assert.That(emblem.height / px, Is.EqualTo(96f).Within(1e-3f), $"{pixels}: 96 dp square.");
            Assert.That(emblem.center.x, Is.EqualTo(ribbon.center.x).Within(0.5f), $"{pixels}: centred above the ribbon.");
            Assert.That((ribbon.yMax - emblem.yMin) / px, Is.EqualTo(16f).Within(1e-3f), $"{pixels}: overlapping it by 16 dp.");
            Assert.That(Field<Image>(end, "_emblem").transform.GetSiblingIndex(), Is.GreaterThan(ribbonImage.transform.GetSiblingIndex()), "the emblem is drawn over the ribbon.");
            Assert.That(title.transform.parent, Is.EqualTo(ribbonImage.transform), "the title sits on the ribbon.");
            Assert.That(title.fontSize / px, Is.EqualTo(40f).Within(1e-3f), $"{pixels}: UI-08e's 40 dp.");
        }
    }

    [Test]
    public void RunEnd_Rows_SitOnPillsWithIcons()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out _);

            Invoke(end, "Tint");

            foreach ((string word, string figure, string icon, string sprite, Color colour) in new[]
            {
                ("_depthLabel", "_depth", "_depthIcon", "Icon_Stage", Palette.HudText),
                ("_bossLabel", "_bosses", "_bossIcon", "Icon_BossKill", Palette.HudText),
                ("_shardLabel", "_shards", "_shardIcon", "Icon_SoulShard", Palette.Essence),
            })
            {
                TMP_Text label = Field<TMP_Text>(end, word);
                TMP_Text number = Field<TMP_Text>(end, figure);
                Image iconImage = Field<Image>(end, icon);
                var pill = label.transform.parent.GetComponent<Image>();
                Rect row = WorldRect(label.transform.parent);
                Rect iconRect = WorldRect(iconImage);
                Rect labelRect = WorldRect(label);
                Rect numberRect = WorldRect(number);
                string at = $"{pixels}: {word}";

                Assert.That(pill, Is.Not.Null, $"{at}: no pill.");
                Assert.That(pill.sprite.name, Is.EqualTo("Pill_Dark"), at);
                Assert.That(pill.type, Is.EqualTo(Image.Type.Sliced), at);
                Assert.That(row.height / px, Is.EqualTo(44f).Within(1e-3f), $"{at}: 44 dp tall.");
                Assert.That(iconImage.sprite.name, Is.EqualTo(sprite), at);
                Assert.That(iconRect.width / px, Is.EqualTo(28f).Within(1e-3f), $"{at}: a 28 dp icon.");
                Assert.That((iconRect.xMin - row.xMin) / px, Is.EqualTo(12f).Within(1e-3f), $"{at}: 12 dp in.");
                Assert.That(iconRect.center.y, Is.EqualTo(row.center.y).Within(0.5f), at);
                Assert.That(labelRect.xMin, Is.GreaterThanOrEqualTo(iconRect.xMax), $"{at}: the word comes after the icon.");
                Assert.That(label.color, Is.EqualTo(Palette.HudCaption), at);
                Assert.That((row.xMax - numberRect.xMax) / px, Is.EqualTo(16f).Within(1e-3f), $"{at}: the figure ends 16 dp from the right.");
                Assert.That(number.alignment, Is.EqualTo(TextAlignmentOptions.MidlineRight), at);
                Assert.That(number.color, Is.EqualTo(colour), at);
            }
        }
    }

    [Test]
    public void RunEnd_ShardGlow_SitsBehindTheFigure()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out _);

            Invoke(end, "Tint");

            Image glow = Field<Image>(end, "_shardGlow");
            TMP_Text figure = Field<TMP_Text>(end, "_shards");
            Rect rect = WorldRect(glow);
            Rect numberRect = WorldRect(figure);
            float wide = figure.GetPreferredValues(figure.text).x;
            Color expected = Palette.Essence;

            expected.a = 0.6f;

            Assert.That(glow.sprite.name, Is.EqualTo("Glow_Soft"), $"{pixels}");
            Assert.That(glow.color, Is.EqualTo(expected), $"{pixels}: Essence at 0.6.");
            Assert.That(glow.raycastTarget, Is.False, $"{pixels}");
            Assert.That(glow.transform.parent, Is.EqualTo(figure.transform.parent), $"{pixels}: above the pill, which is the row's own image.");
            Assert.That(glow.transform.GetSiblingIndex(), Is.LessThan(figure.transform.GetSiblingIndex()), $"{pixels}: behind the figure.");
            Assert.That(rect.height, Is.EqualTo(2f * numberRect.height).Within(0.5f), $"{pixels}: twice the figure's height.");
            Assert.That(rect.center.y, Is.EqualTo(numberRect.center.y).Within(0.5f), $"{pixels}: centred on the figure.");
            Assert.That(rect.center.x, Is.EqualTo(numberRect.xMax - (wide / 2f)).Within(0.5f), $"{pixels}: centred on the figure's text.");
        }
    }

    [Test]
    public void RunEnd_Opened_HasItsIcon()
    {
        foreach ((Vector2 pixels, float dpi) in Phones)
        {
            RunEndPresenter end = OpenRunEnd(pixels, dpi, out float px, out Rect safe, withLine: true);
            Image icon = Field<Image>(end, "_openedIcon");
            TMP_Text line = Field<TMP_Text>(end, "_opened");
            Rect iconRect = WorldRect(icon);
            Rect lineRect = WorldRect(line);

            Assert.That(icon.gameObject.activeSelf, Is.True, $"{pixels}: shown with the line.");
            Assert.That(icon.sprite.name, Is.EqualTo("Icon_PlaceOpened"), $"{pixels}");
            Assert.That(iconRect.width / px, Is.EqualTo(28f).Within(1e-3f), $"{pixels}: 28 dp.");
            Assert.That(iconRect.xMax, Is.LessThanOrEqualTo(lineRect.xMin + 0.5f), $"{pixels}: left of the text.");
            Assert.That(iconRect.center.y, Is.EqualTo(lineRect.center.y).Within(0.5f), $"{pixels}");
            Assert.That((iconRect.xMin + lineRect.xMax) / 2f, Is.EqualTo(safe.center.x).Within(0.5f), $"{pixels}: the two centred together.");
            Assert.That(line.color, Is.EqualTo(Palette.Neutral), $"{pixels}: Neutral on the light stone.");
        }
    }

    [Test]
    public void RunEnd_Return_IsGreenHome()
    {
        RunEndPresenter end = OpenRunEnd(Phones[0].Pixels, Phones[0].Dpi, out _, out _);
        Button button = Field<Button>(end, "_return");
        var face = (Image)button.targetGraphic;

        Assert.That(face.sprite.name, Is.EqualTo("Button_Green"));
        Assert.That(face.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
        Assert.That(button.spriteState.pressedSprite.name, Is.EqualTo("Button_Green_Pressed"));
        Assert.That(Field<TMP_Text>(end, "_returnLabel").text, Is.EqualTo("Home"));
        Assert.That(Field<TMP_Text>(end, "_returnLabel").color, Is.EqualTo(Palette.HudText));
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

    private RunEndPresenter OpenRunEnd(Vector2 pixels, float dpi, out float px, out Rect safe, bool withLine = false)
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

        if (withLine)
        {
            // Built the way Start builds it, then shown the way a finish that opens a place shows it.
            var line = (TMP_Text)typeof(RunEndPresenter)
                .GetMethod("BuildOpenedLine", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Field<TMP_Text>(end, "_title"), Field<Button>(end, "_return").transform.parent });

            line.text = english.Format(new LocKey("ui.runend.opened"), "Descent");
            line.gameObject.SetActive(true);
            Field<Image>(end, "_openedIcon").gameObject.SetActive(true);
            SetField(end, "_opened", line);
            SetField(end, "_showsOpened", true);
        }

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

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
}
