using NUnit.Framework;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-06b: the menu screen laid out on a 9:20 phone, with a save and without. The real
/// <c>Menu.unity</c> is opened additively, its <c>Safe</c> object is sized in dp (one canvas unit to
/// the dp, so every number read back is a dp) and <see cref="MenuPresenter.Layout"/> is run on it.
/// </summary>
[TestFixture]
public sealed class MenuLayoutTests
{
    private const string ScenePath = "Assets/_Project/Scenes/Menu.unity";

    private static readonly Vector2 Portrait = new Vector2(411f, 914f);
    private static readonly Vector2 Narrow = new Vector2(360f, 780f);

    private Scene _scene;
    private RectTransform _safe;
    private MenuPresenter _menu;
    private SerializedObject _fields;

    [SetUp]
    public void OpenMenu()
    {
        _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        _menu = null;

        foreach (GameObject root in _scene.GetRootGameObjects())
        {
            _menu = root.GetComponentInChildren<MenuPresenter>(true);

            if (_menu != null)
            {
                break;
            }
        }

        Assert.That(_menu, Is.Not.Null, "Menu.unity has no MenuPresenter.");

        // The saved canvas carries a zero scale (the canvas writes it at runtime); measure at one.
        _menu.transform.localScale = Vector3.one;
        _safe = (RectTransform)_menu.transform.Find("Safe");
        _fields = new SerializedObject(_menu);
    }

    [TearDown]
    public void CloseMenu()
    {
        EditorSceneManager.CloseScene(_scene, true);
    }

    [Test]
    public void Menu_WithSave_ContinueIsGreenAndAbove()
    {
        Layout(Portrait, true);

        Button primary = Field<Button>("_continue");
        Button secondary = Field<Button>("_descend");

        Assert.That(SpriteFile(primary), Is.EqualTo("Button_Green.png"));
        Assert.That(SpriteFile(secondary), Is.EqualTo("Button_Blue.png"));
        Assert.That(InSafe(primary).center.y, Is.GreaterThan(InSafe(secondary).center.y));
        AssertButtonSize(primary);
        AssertButtonSize(secondary);
    }

    [Test]
    public void Menu_NoSave_DescendIsGreenAlone()
    {
        Layout(Portrait, true);
        Rect primaryPlace = InSafe(Field<Button>("_continue"));

        Layout(Portrait, false);
        Button descend = Field<Button>("_descend");

        Assert.That(SpriteFile(descend), Is.EqualTo("Button_Green.png"));
        Assert.That(InSafe(descend).center.y, Is.EqualTo(primaryPlace.center.y).Within(0.01f));
        AssertButtonSize(descend);
    }

    [Test]
    public void Menu_TitleOnThePlate()
    {
        Layout(Portrait, true);

        Image plate = Field<Image>("_titlePlate");
        Rect plateRect = InSafe(plate.rectTransform);
        Rect title = InSafe(Field<TMP_Text>("_title").rectTransform);

        Assert.That(plate.sprite.name, Is.EqualTo("Menu_TitlePlate"));
        Assert.That(plateRect.center.y, Is.GreaterThan(Portrait.y * 2f / 3f));
        Assert.That(plateRect.center.x, Is.EqualTo(Portrait.x * 0.5f).Within(0.01f));
        Assert.That(plateRect.Contains(title.min) && plateRect.Contains(title.max - new Vector2(0.01f, 0.01f)), Is.True);
    }

    [Test]
    public void Menu_Pressed_UsesPressedSprites()
    {
        Layout(Portrait, true);

        Button green = Field<Button>("_continue");
        Button blue = Field<Button>("_descend");

        Assert.That(green.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
        Assert.That(blue.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
        Assert.That(AssetDatabase.GetAssetPath(green.spriteState.pressedSprite), Does.EndWith("Button_Green_Pressed.png"));
        Assert.That(AssetDatabase.GetAssetPath(blue.spriteState.pressedSprite), Does.EndWith("Button_Blue_Pressed.png"));

        Layout(Portrait, false);

        Assert.That(AssetDatabase.GetAssetPath(blue.spriteState.pressedSprite), Does.EndWith("Button_Green_Pressed.png"));
    }

    [Test]
    public void Menu_FitsPortrait([Values(true, false)] bool hasSave, [Values(0, 1)] int size)
    {
        Vector2 area = size == 0 ? Portrait : Narrow;
        Layout(area, hasSave);

        var safe = new Rect(Vector2.zero, area);
        Rect background = InSafe(Field<Image>("_background").rectTransform);

        Assert.That(background.size, Is.EqualTo(area), "the background fills the safe area");

        var pieces = new System.Collections.Generic.List<Rect> { InSafe(Field<Image>("_titlePlate").rectTransform) };
        pieces.Add(InSafe(Field<Button>("_descend")));

        if (hasSave)
        {
            pieces.Add(InSafe(Field<Button>("_continue")));
        }

        foreach (Rect piece in pieces)
        {
            Assert.That(safe.Contains(piece.min) && safe.Contains(piece.max), Is.True, $"{piece} leaves {safe}");
        }

        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                Assert.That(pieces[a].Overlaps(pieces[b]), Is.False, $"{pieces[a]} overlaps {pieces[b]}");
            }
        }

        Assert.That(Field<TMP_Text>("_title").fontSize, Is.GreaterThanOrEqualTo(12f));
        Assert.That(Field<TMP_Text>("_descendLabel").fontSize, Is.GreaterThanOrEqualTo(12f));
        Assert.That(Field<TMP_Text>("_continueLabel").fontSize, Is.GreaterThanOrEqualTo(12f));
    }

    private void Layout(Vector2 areaDp, bool hasSave)
    {
        _safe.anchorMin = Vector2.zero;
        _safe.anchorMax = Vector2.zero;
        _safe.pivot = Vector2.zero;
        _safe.anchoredPosition = Vector2.zero;
        _safe.sizeDelta = areaDp;

        _menu.Layout(1f, hasSave);
    }

    private T Field<T>(string name) where T : Object
    {
        var value = _fields.FindProperty(name).objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, $"{name} is not wired in Menu.unity.");

        return value;
    }

    private static string SpriteFile(Button button)
    {
        return System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(((Image)button.targetGraphic).sprite));
    }

    private static void AssertButtonSize(Button button)
    {
        Vector2 size = ((RectTransform)button.transform).sizeDelta;

        Assert.That(size.x, Is.GreaterThanOrEqualTo(240f));
        Assert.That(size.y, Is.GreaterThanOrEqualTo(56f));
    }

    private Rect InSafe(Button button)
    {
        return InSafe((RectTransform)button.transform);
    }

    private Rect InSafe(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector3 min = _safe.InverseTransformPoint(corners[0]);
        Vector3 max = _safe.InverseTransformPoint(corners[2]);

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
