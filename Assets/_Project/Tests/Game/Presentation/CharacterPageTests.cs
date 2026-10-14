using System;
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
/// UI-05c: the page as a view. It is told a <see cref="ClassPageModel"/> and draws it; the presenter
/// rows (<c>ClassSelectPresenterTests</c>) own what the model says.
/// </summary>
/// <remarks>
/// Over the shipped <c>ClassSelect.prefab</c>, so the rows describe the asset the Menu scene holds.
/// </remarks>
[TestFixture]
public sealed class CharacterPageTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary><c>ClassSelectPresenter.HeaderDp</c>: the page starts under the back button's row.</summary>
    private const float HeaderDp = 72f;

    private const float Slack = 0.01f;

    /// <summary>
    /// UI-08d's phones, in dp (px × 160 ÷ dpi): 1080 × 2400 @ 420, 720 × 1600 @ 320, and the look
    /// pass's 2658 × 960 @ 480 (UI-07a finding 1).
    /// </summary>
    private static readonly Vector2[] Phones =
    {
        new Vector2(1080f, 2400f) * (160f / 420f),
        new Vector2(720f, 1600f) * (160f / 320f),
        new Vector2(2658f, 960f) * (160f / 480f),
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
    public void Show_RefusesANullLocalizer()
    {
        Assert.Throws<ArgumentNullException>(() => _page.Show(Model(PageButton.Choose, price: 0), null));
    }

    [Test]
    public void Page_Portrait_StacksWithoutOverlap([Values(0, 1)] int phone)
    {
        Rect page = LayOut(Phones[phone]);

        Rect name = InPage(FieldOf<TMP_Text>("_name").rectTransform);
        Rect role = InPage(FieldOf<TMP_Text>("_roleLabel").rectTransform);
        Rect preview = InPage(_page.Preview.rectTransform);
        Rect tiles = Tiles();
        Rect caption = InPage(Strip().Caption.rectTransform);
        Rect button = InPage((RectTransform)_page.Primary.transform);

        // Top to bottom: name band, role, preview, strip, caption, button (rule 3).
        Assert.That(name.yMin, Is.GreaterThanOrEqualTo(role.yMax - Slack), "the role is not under the name.");
        Assert.That(role.yMin, Is.GreaterThanOrEqualTo(preview.yMax - Slack), "the preview is not under the role.");
        Assert.That(preview.yMin, Is.GreaterThanOrEqualTo(tiles.yMax - Slack), "the strip is not under the preview.");
        Assert.That(tiles.yMin, Is.GreaterThanOrEqualTo(caption.yMax - Slack), "the caption is not under the tiles.");
        Assert.That(caption.yMin, Is.GreaterThanOrEqualTo(button.yMax - Slack), "the button is not under the caption.");

        AssertFitsWithoutOverlap(page);
    }

    [Test]
    public void Page_Preview_NeverUnder120Dp([Values(0, 1, 2, 3)] int phone)
    {
        // The fourth size is a short portrait page the stack cannot fill: the preview stops at its
        // floor there, and nothing else gives way (rule 4).
        LayOut(phone < Phones.Length ? Phones[phone] : new Vector2(320f, 420f));

        Rect preview = InPage(_page.Preview.rectTransform);

        Assert.That(preview.height, Is.GreaterThanOrEqualTo(CharacterPage.PreviewMinDp - Slack));
        Assert.That(preview.width, Is.GreaterThan(0f), "the preview has no width, and the body never draws.");
        Assert.That(((RectTransform)_page.Primary.transform).sizeDelta, Is.EqualTo(new Vector2(168f, 60f)), "the button gave way.");
        Assert.That(((RectTransform)Strip().transform).sizeDelta.y, Is.EqualTo(SkillStrip.HeightDp).Within(Slack), "the strip gave way.");
    }

    [Test]
    public void Page_LookPassSize_Fits()
    {
        // UI-07a finding 1: 2658 × 960 @ 480 dpi, a landscape phone 320 dp tall.
        Rect page = LayOut(Phones[2]);

        Rect preview = InPage(_page.Preview.rectTransform);

        Assert.That(preview.height, Is.GreaterThanOrEqualTo(CharacterPage.PreviewMinDp - Slack));
        Assert.That(preview.width, Is.GreaterThanOrEqualTo(CharacterPage.PreviewMinDp - Slack), "the Ranger has no room to stand.");
        Assert.That(InPage(Strip().Caption.rectTransform).yMax, Is.LessThanOrEqualTo(InPage(FieldOf<TMP_Text>("_roleLabel").rectTransform).yMin + Slack), "the caption reaches the role label.");

        AssertFitsWithoutOverlap(page);
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
    /// under the header as the presenter does, draws every piece a locked class shows and lays out.
    /// </summary>
    /// <returns>The page's rect, in its own space.</returns>
    private Rect LayOut(Vector2 screenDp)
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

        var strip = new[]
        {
            new StripEntry(null, new LocKey("skill.a.name"), new LocKey("skill.a.description")),
            new StripEntry(null, new LocKey("skill.b.name"), new LocKey("skill.b.description")),
            new StripEntry(null, new LocKey("skill.c.name"), new LocKey("skill.c.description")),
            new StripEntry(null, new LocKey("skill.d.name"), new LocKey("skill.d.description")),
        };

        _page.Show(new ClassPageModel(_spec, 1, 3, PageButton.Unlock, 2_000, "or reach stage 20", RoleKind.Ranged, strip), Localizer());
        _page.Layout(1f);

        return InPage(page);
    }

    /// <summary>
    /// Every piece inside the page and no two overlapping: name, role, preview, both arrows, tiles,
    /// caption, dots, button, pill and deed line.
    /// </summary>
    private void AssertFitsWithoutOverlap(Rect page)
    {
        var pieces = new (string Name, Rect Rect)[]
        {
            ("name", InPage(FieldOf<TMP_Text>("_name").rectTransform)),
            ("role", InPage(FieldOf<TMP_Text>("_roleLabel").rectTransform)),
            ("preview", InPage(_page.Preview.rectTransform)),
            ("previous", InPage((RectTransform)FieldOf<Button>("_previous").transform)),
            ("next", InPage((RectTransform)FieldOf<Button>("_next").transform)),
            ("tiles", Tiles()),
            ("caption", InPage(Strip().Caption.rectTransform)),
            ("dots", InPage(RectOf("_dots"))),
            ("button", InPage((RectTransform)_page.Primary.transform)),
            ("pill", InPage(RectOf("_pill"))),
            ("deed", InPage(FieldOf<TMP_Text>("_deed").rectTransform)),
        };

        foreach ((string name, Rect rect) in pieces)
        {
            Assert.That(rect.width > 0f && rect.height > 0f, Is.True, $"{name} is {rect}.");
            Assert.That(
                rect.xMin >= page.xMin - Slack && rect.yMin >= page.yMin - Slack && rect.xMax <= page.xMax + Slack && rect.yMax <= page.yMax + Slack,
                Is.True,
                $"{name} {rect} leaves the page {page}.");
        }

        for (int a = 0; a < pieces.Length; a++)
        {
            for (int b = a + 1; b < pieces.Length; b++)
            {
                Assert.That(Shrunk(pieces[a].Rect).Overlaps(Shrunk(pieces[b].Rect)), Is.False, $"{pieces[a].Name} {pieces[a].Rect} overlaps {pieces[b].Name} {pieces[b].Rect}.");
            }
        }
    }

    private static Rect Shrunk(Rect rect) =>
        Rect.MinMaxRect(rect.xMin + Slack, rect.yMin + Slack, rect.xMax - Slack, rect.yMax - Slack);

    private SkillStrip Strip() => FieldOf<SkillStrip>("_strip");

    /// <summary>The tile row: the first tile to the last.</summary>
    private Rect Tiles()
    {
        Transform strip = Strip().transform;
        Rect first = InPage((RectTransform)strip.Find("Tile0"));
        Rect last = InPage((RectTransform)strip.Find("Tile3"));

        return Rect.MinMaxRect(first.xMin, Mathf.Min(first.yMin, last.yMin), last.xMax, Mathf.Max(first.yMax, last.yMax));
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
