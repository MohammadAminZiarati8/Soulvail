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
}
