using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-05c and UI-12d: the page as a view. It is told a <see cref="ClassPageModel"/> and draws it; the
/// presenter rows (<c>ClassSelectPresenterTests</c>) own what the model says, and
/// <c>HeroSheetViewTests</c> what the sheet draws.
/// </summary>
/// <remarks>
/// Over the shipped <c>ClassSelect.prefab</c>, so the rows describe the asset the Menu scene holds.
/// </remarks>
[TestFixture]
public sealed class CharacterPageTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary><c>ClassSelectPresenter.HeaderDp</c>: the page starts under the back button's row.</summary>
    private const float HeaderDp = 72f;

    private const float Slack = 0.01f;

    /// <summary>UI-12d rule 12's phones, in dp: 9:20 at 411 × 914 and 360 × 780.</summary>
    private static readonly Vector2[] Phones =
    {
        new Vector2(411f, 914f),
        new Vector2(360f, 780f),
    };

    private GameObject _screen;
    private CharacterPage _page;
    private CharacterSpec _spec;
    private int _previous;
    private int _next;
    private int _primary;
    private float _rotated;

    [SetUp]
    public void SetUp()
    {
        _screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _page = _screen.GetComponentInChildren<CharacterPage>(true);
        _spec = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath).ToSpec();
        _previous = _next = _primary = 0;
        _rotated = 0f;

        _page.Bind(() => _previous++, () => _next++, () => _primary++, degrees => _rotated += degrees);
    }

    [TearDown]
    public void TearDown()
    {
        if (_screen != null)
        {
            Object.DestroyImmediate(_screen);
        }
    }

    [Test]
    public void Show_Choose_IsGreenAndLive()
    {
        _page.Show(Model(PageButton.Choose, price: 0), Localizer());

        Assert.That(_page.ButtonState, Is.EqualTo(PageButton.Choose));
        Assert.That(_page.Primary.interactable, Is.True);
        Assert.That(Face().sprite.name, Is.EqualTo("Button_Green"));
        Assert.That(TextOf("_primaryLabel"), Is.EqualTo("ui.classselect.choose"));
        Assert.That(RectOf("_pill").gameObject.activeSelf, Is.False, "an owned class shows a price.");
        Assert.That(ImageOf("_lockBadge").gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Show_Unlock_IsVioletWithAPrice()
    {
        _page.Show(Model(PageButton.Unlock, price: 2_000), Localizer());

        Assert.That(_page.Primary.interactable, Is.True);
        Assert.That(Face().sprite.name, Is.EqualTo("Button_Violet"));
        Assert.That(TextOf("_primaryLabel"), Is.EqualTo("ui.classselect.unlock"));
        Assert.That(TextOf("_price"), Is.EqualTo("2000"));
        Assert.That(RectOf("_pill").gameObject.activeSelf, Is.True);
        Assert.That(ImageOf("_lockBadge").gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Show_Dead_IsGreyAndRefuses()
    {
        _page.Show(Model(PageButton.Dead, price: 2_000, deed: "or reach stage 20"), Localizer());

        Assert.That(_page.Primary.interactable, Is.False);
        Assert.That(Face().sprite.name, Is.EqualTo("Button_Grey"));
        Assert.That(TextOf("_price"), Is.EqualTo("2000"), "a dead price is hidden.");
        Assert.That(TextOf("_deed"), Is.EqualTo("or reach stage 20"));
    }

    [Test]
    public void Show_TheButtonReportsItsTap()
    {
        _page.Show(Model(PageButton.Choose, price: 0), Localizer());
        _page.Bind(() => _previous++, () => _next++, () => _primary++);

        _page.Primary.onClick.Invoke();

        Assert.That(_primary, Is.EqualTo(1), "a tap is reported once, however often the page is bound.");
    }

    [Test]
    public void Show_Dots_CountTheRosterAndLightTheIndex()
    {
        _page.Show(Model(PageButton.Choose, price: 0, index: 2, count: 4), Localizer());

        Assert.That(_page.DotsShown, Is.EqualTo(4));
        Assert.That(_page.NextShown, Is.True);
        Assert.That(_page.PreviousShown, Is.True);

        _page.Show(Model(PageButton.Choose, price: 0, index: 0, count: 1), Localizer());

        Assert.That(_page.DotsShown, Is.Zero);
        Assert.That(_page.NextShown, Is.False);
        Assert.That(_page.PreviousShown, Is.False);
    }

    [Test]
    public void Show_ARoleWithNoSpriteDrawsNoBadge()
    {
        _page.Show(Model(PageButton.Choose, price: 0, role: RoleKind.Ranged), Localizer());

        Assert.That(ImageOf("_roleBadge").gameObject.activeSelf, Is.True);

        _page.Show(Model(PageButton.Choose, price: 0, role: RoleKind.Caster), Localizer());

        Assert.That(ImageOf("_roleBadge").gameObject.activeSelf, Is.False);
        Assert.That(TextOf("_roleLabel"), Is.EqualTo("ui.classselect.role.caster"), "the word still draws.");
    }

    [Test]
    public void Gesture_SwipeOnTheBandStepsDragOnThePreviewOnlyTurns()
    {
        var band = (RectTransform)typeof(CharacterPage).GetField("_nameBand", Private).GetValue(_page);

        Drag(_page.Preview.gameObject, deltaX: 20f, travelled: -4f * CharacterPage.SwipePixels);

        Assert.That(_next + _previous, Is.Zero, "a drag on the preview stepped.");
        Assert.That(_rotated, Is.Not.Zero, "a drag on the preview did not turn the model.");

        float turned = _rotated;

        Drag(band.gameObject, deltaX: -5f, travelled: -0.5f * CharacterPage.SwipePixels);

        Assert.That(_next + _previous, Is.Zero, "a swipe short of 48 dp stepped.");

        Drag(band.gameObject, deltaX: -40f, travelled: -2f * CharacterPage.SwipePixels);

        Assert.That(_next, Is.EqualTo(1));
        Assert.That(_previous, Is.Zero);

        Drag(band.gameObject, deltaX: 40f, travelled: 2f * CharacterPage.SwipePixels);

        Assert.That(_previous, Is.EqualTo(1));
        Assert.That(_rotated, Is.EqualTo(turned), "a swipe turned the model.");

        Drag(_screen, deltaX: 40f, travelled: 2f * CharacterPage.SwipePixels);

        Assert.That(_next + _previous, Is.EqualTo(2), "a drag that began on neither moved something.");
    }

    [Test]
    public void Gesture_DragOnTheSheet_NeitherStepsNorTurns()
    {
        LayOut(Phones[1]);

        Drag(_page.Sheet.Scroll.viewport.gameObject, deltaX: 40f, travelled: -4f * CharacterPage.SwipePixels);

        Assert.That(_next + _previous, Is.Zero, "a drag on the sheet stepped.");
        Assert.That(_rotated, Is.Zero, "a drag on the sheet turned the model.");

        Drag(_page.Sheet.Rows[0].Root.gameObject, deltaX: 40f, travelled: 4f * CharacterPage.SwipePixels);

        Assert.That(_next + _previous, Is.Zero, "a drag on a row stepped.");
        Assert.That(_rotated, Is.Zero, "a drag on a row turned the model.");
    }

    [Test]
    public void Show_RefusesANullLocalizer()
    {
        Assert.Throws<ArgumentNullException>(() => _page.Show(Model(PageButton.Choose, price: 0), null));
    }

    [Test]
    public void Page_Stack_BandPreviewSheetButton_NoOverlap([Values(0, 1)] int phone)
    {
        Rect page = LayOut(Phones[phone]);

        Rect band = InPage(RectOf("_nameBand"));
        Rect preview = InPage(_page.Preview.rectTransform);
        Rect sheet = Viewport();
        Rect buttons = ButtonRow();

        // Top to bottom: name band, preview, sheet, button row (rule 1).
        Assert.That(band.yMin, Is.GreaterThanOrEqualTo(preview.yMax - Slack), "the preview is not under the band.");
        Assert.That(preview.yMin, Is.GreaterThanOrEqualTo(sheet.yMax - Slack), "the sheet is not under the preview.");
        Assert.That(sheet.yMin, Is.GreaterThanOrEqualTo(buttons.yMax - Slack), "the button row is not under the sheet.");

        // Rule 12.
        Assert.That(preview.height, Is.GreaterThanOrEqualTo(CharacterPage.PreviewMinDp - Slack));
        Assert.That(sheet.height, Is.GreaterThanOrEqualTo(160f - Slack), "the sheet's viewport is under 160 dp.");

        AssertFitsWithoutOverlap(page, dots: true);
    }

    [Test]
    public void Page_Preview_TakesItsShare_Clamped([Values(300f, 600f, 1200f)] float pageHeight)
    {
        // 300 dp is under the clamp (108 → 120), 600 inside it (216) and 1200 over it (432 → 300).
        LayOut(new Vector2(360f, pageHeight + HeaderDp));

        float expected = Mathf.Clamp(CharacterPage.PreviewShare * pageHeight, CharacterPage.PreviewMinDp, CharacterPage.PreviewMaxDp);

        Assert.That(InPage(_page.Preview.rectTransform).height, Is.EqualTo(expected).Within(Slack));
        Assert.That(((RectTransform)_page.Primary.transform).sizeDelta, Is.EqualTo(new Vector2(168f, 60f)), "the button gave way.");
    }

    [Test]
    public void Page_NoDots_KeepsNoSpace()
    {
        LayOut(Phones[1], count: 1);

        Assert.That(RectOf("_dots").gameObject.activeSelf, Is.False, "the dots row is drawn for one class.");
        Assert.That(Viewport().yMax, Is.EqualTo(InPage(_page.Preview.rectTransform).yMin - 8f).Within(Slack), "the sheet does not start one gap under the preview.");

        AssertFitsWithoutOverlap(InPage((RectTransform)_page.transform), dots: false);
    }

    [Test]
    public void Page_Dots_SitUnderThePreview()
    {
        LayOut(Phones[1], count: 3);

        Rect dots = InPage(RectOf("_dots"));

        Assert.That(RectOf("_dots").gameObject.activeSelf, Is.True);
        Assert.That(dots.yMax, Is.LessThanOrEqualTo(InPage(_page.Preview.rectTransform).yMin + Slack), "the dots are not under the preview.");
        Assert.That(Viewport().yMax, Is.LessThanOrEqualTo(dots.yMin + Slack), "the sheet is not under the dots.");
    }

    [Test]
    public void Page_ButtonRow_StaysPut_WhenTheSheetScrolls()
    {
        LayOut(Phones[1]);

        Rect button = InPage((RectTransform)_page.Primary.transform);
        Rect deed = InPage(FieldOf<TMP_Text>("_deed").rectTransform);
        Rect viewport = Viewport();
        RectTransform content = _page.Sheet.Scroll.content;

        Assert.That(_page.Sheet.ContentHeight, Is.GreaterThan(viewport.height), "the fixture's premise: the Ranger's sheet scrolls.");

        content.anchoredPosition = new Vector2(0f, _page.Sheet.ContentHeight - viewport.height);

        Assert.That(InPage(_page.Sheet.Rows[_page.Sheet.Rows.Count - 1].Root).yMin, Is.GreaterThanOrEqualTo(viewport.yMin - Slack), "the content did not scroll to its bottom.");
        Assert.That(InPage((RectTransform)_page.Primary.transform), Is.EqualTo(button), "the button moved with the sheet.");
        Assert.That(InPage(FieldOf<TMP_Text>("_deed").rectTransform), Is.EqualTo(deed), "the deed moved with the sheet.");
        Assert.That(Viewport().yMin, Is.GreaterThanOrEqualTo(ButtonRow().yMax - Slack), "the viewport reaches the button row.");
        Assert.That(_page.Sheet.Scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null, "the viewport does not clip.");
    }

    [Test]
    public void Page_ScreenChanges_LaysOutAgain()
    {
        LayOut(Phones[1]);

        var screen = new Rect(0f, 0f, 720f, 1600f);

        // No canvas above the instantiated prefab, so 160 dpi is one canvas unit to the dp.
        Assert.That(_page.Refit(720, 1600, 160f, screen), Is.True, "the first look lays out.");

        RectTransform preview = _page.Preview.rectTransform;
        Vector2 laidMin = preview.offsetMin;
        Vector2 laidMax = preview.offsetMax;
        float width = ((RectTransform)_page.Primary.transform).sizeDelta.x;

        preview.offsetMin = new Vector2(3f, 3f);
        preview.offsetMax = new Vector2(-3f, -3f);

        Assert.That(_page.Refit(720, 1600, 160f, screen), Is.False, "nothing changed and it laid out.");
        Assert.That(preview.offsetMin, Is.EqualTo(new Vector2(3f, 3f)), "nothing changed and the preview moved.");

        Assert.That(_page.Refit(720, 1600, 160f, new Rect(0f, 48f, 720f, 1500f)), Is.True, "a new safe area was ignored.");
        Assert.That(preview.offsetMin, Is.EqualTo(laidMin));
        Assert.That(preview.offsetMax, Is.EqualTo(laidMax));

        Assert.That(_page.Refit(720, 1600, 320f, new Rect(0f, 48f, 720f, 1500f)), Is.True, "a new dpi was ignored.");
        Assert.That(((RectTransform)_page.Primary.transform).sizeDelta.x, Is.EqualTo(2f * width).Within(Slack));

        Assert.That(_page.Refit(1600, 720, 320f, new Rect(0f, 0f, 1600f, 720f)), Is.True, "a rotation was ignored.");
    }

    /// <summary>
    /// Sizes the screen to <paramref name="screenDp"/> at one canvas unit to the dp, places the page
    /// under the header as the presenter does, draws a locked Ranger with its sheet and lays out —
    /// in the presenter's order, layout first.
    /// </summary>
    /// <returns>The page's rect, in its own space.</returns>
    private Rect LayOut(Vector2 screenDp, int count = 3)
    {
        var root = (RectTransform)_screen.transform;
        root.localScale = Vector3.one;
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = screenDp;

        var safe = (RectTransform)root.Find("Safe");
        safe.localScale = Vector3.one;
        safe.anchorMin = Vector2.zero;
        safe.anchorMax = Vector2.one;
        safe.offsetMin = safe.offsetMax = Vector2.zero;

        var page = (RectTransform)_page.transform;
        page.localScale = Vector3.one;
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.pivot = new Vector2(0.5f, 0.5f);
        page.offsetMin = Vector2.zero;
        page.offsetMax = new Vector2(0f, -HeaderDp);

        CharacterDefinition ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);
        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);

        _page.Layout(1f);
        _page.Show(new ClassPageModel(ranger.ToSpec(), 0, count, PageButton.Unlock, 2_000, "or reach stage 20", RoleKind.Ranged, sheet), English());

        return InPage(page);
    }

    /// <summary>
    /// Every piece inside the page and no two overlapping: name, role, preview, both arrows, the
    /// dots when drawn, the sheet's viewport, button, pill and deed line.
    /// </summary>
    private void AssertFitsWithoutOverlap(Rect page, bool dots)
    {
        var pieces = new List<(string Name, Rect Rect)>
        {
            ("name", InPage(FieldOf<TMP_Text>("_name").rectTransform)),
            ("role", InPage(FieldOf<TMP_Text>("_roleLabel").rectTransform)),
            ("preview", InPage(_page.Preview.rectTransform)),
            ("previous", InPage((RectTransform)FieldOf<Button>("_previous").transform)),
            ("next", InPage((RectTransform)FieldOf<Button>("_next").transform)),
            ("sheet", Viewport()),
            ("button", InPage((RectTransform)_page.Primary.transform)),
            ("pill", InPage(RectOf("_pill"))),
            ("deed", InPage(FieldOf<TMP_Text>("_deed").rectTransform)),
        };

        if (dots)
        {
            pieces.Add(("dots", InPage(RectOf("_dots"))));
        }

        foreach ((string name, Rect rect) in pieces)
        {
            Assert.That(rect.width > 0f && rect.height > 0f, Is.True, $"{name} is {rect}.");
            Assert.That(
                rect.xMin >= page.xMin - Slack && rect.yMin >= page.yMin - Slack && rect.xMax <= page.xMax + Slack && rect.yMax <= page.yMax + Slack,
                Is.True,
                $"{name} {rect} leaves the page {page}.");
        }

        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                Assert.That(Shrunk(pieces[a].Rect).Overlaps(Shrunk(pieces[b].Rect)), Is.False, $"{pieces[a].Name} {pieces[a].Rect} overlaps {pieces[b].Name} {pieces[b].Rect}.");
            }
        }
    }

    private static Rect Shrunk(Rect rect) =>
        Rect.MinMaxRect(rect.xMin + Slack, rect.yMin + Slack, rect.xMax - Slack, rect.yMax - Slack);

    private Rect Viewport() => InPage(_page.Sheet.Scroll.viewport);

    /// <summary>The button and the pill beside it, as one rect.</summary>
    private Rect ButtonRow()
    {
        Rect button = InPage((RectTransform)_page.Primary.transform);
        Rect pill = InPage(RectOf("_pill"));

        return Rect.MinMaxRect(
            Mathf.Min(button.xMin, pill.xMin),
            Mathf.Min(button.yMin, pill.yMin),
            Mathf.Max(button.xMax, pill.xMax),
            Mathf.Max(button.yMax, pill.yMax));
    }

    /// <summary><paramref name="rect"/>'s corners in the page's own space.</summary>
    private Rect InPage(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Transform page = _page.transform;
        Vector3 min = page.InverseTransformPoint(corners[0]);
        Vector3 max = page.InverseTransformPoint(corners[2]);

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private ClassPageModel Model(PageButton button, int price, int index = 0, int count = 2, string deed = null, RoleKind role = RoleKind.Melee) =>
        new ClassPageModel(_spec, index, count, button, price, deed, role);

    private static Soulvail.Core.Ports.ILocalizer Localizer() =>
        new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>());

    private static Soulvail.Core.Ports.ILocalizer English() =>
        new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath));

    private static ContentCatalog ShippedCatalog()
    {
        var characters = new List<CharacterSpec>();
        var skills = new List<SkillSpec>();
        var trees = new List<SkillTreeSpec>();

        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition"))
        {
            characters.Add(AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinition"))
        {
            skills.Add(AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        foreach (string guid in AssetDatabase.FindAssets("t:SkillTreeDefinition"))
        {
            trees.Add(AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        return new ContentCatalog(characters, skills: skills, trees: trees);
    }

    private void Drag(GameObject onto, float deltaX, float travelled)
    {
        var press = new PointerEventData(null)
        {
            position = new Vector2(500f, 500f),
            pressPosition = new Vector2(500f, 500f),
            pointerPressRaycast = new RaycastResult { gameObject = onto },
        };

        _page.OnBeginDrag(press);

        press.delta = new Vector2(deltaX, 0f);
        press.position = new Vector2(500f + travelled, 500f);

        _page.OnDrag(press);
        _page.OnEndDrag(press);
    }

    private Image Face() => ImageOf("_primaryFace");

    private Image ImageOf(string field) =>
        (Image)typeof(CharacterPage).GetField(field, Private).GetValue(_page);

    private RectTransform RectOf(string field) =>
        (RectTransform)typeof(CharacterPage).GetField(field, Private).GetValue(_page);

    private string TextOf(string field) =>
        ((TMP_Text)typeof(CharacterPage).GetField(field, Private).GetValue(_page)).text;

    private T FieldOf<T>(string field) where T : Object =>
        (T)typeof(CharacterPage).GetField(field, Private).GetValue(_page);
}
