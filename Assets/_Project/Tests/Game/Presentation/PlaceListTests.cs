using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-11g: the Places tab on the real <c>Menu.unity</c> — a card per listed place and a "more to
/// come" card, best of final, the Finished line, the picked rim, a locked card naming its opener and
/// refusing a tap, a tap picking and returning home, and the column fitting a 9:20 phone. The scene is
/// opened additively and the presenter injected by hand; <c>OnEnable</c> is invoked by reflection
/// because EditMode never runs it.
/// </summary>
[TestFixture]
public sealed class PlaceListTests
{
    private const string ScenePath = "Assets/_Project/Scenes/Menu.unity";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string JungleArt = "Assets/_Project/Art/UI/Home/PlaceCard_Jungle.png";
    private const string SoonArt = "Assets/_Project/Art/UI/Home/PlaceCard_Soon.png";
    private const string FrameArt = "Assets/_Project/Art/UI/Home/PlaceCard_Frame.png";
    private const string FrameSelectedArt = "Assets/_Project/Art/UI/Home/PlaceCard_FrameSelected.png";
    private const string LockArt = "Assets/_Project/Art/UI/Character/CharacterPage_LockBadge.png";

    private static readonly ContentId Ranger = new ContentId("character.ranger");
    private static readonly ContentId Jungle = new ContentId("mode.jungle");

    // Test-only: a second place the Jungle opens.
    private static readonly ContentId Descent = new ContentId("mode.descent");

    private Scene _scene;
    private RectTransform _safe;
    private MenuPresenter _menu;
    private SerializedObject _fields;
    private PendingRun _pending;
    private RecordingLoader _loader;
    private ProfileStore _profiles;
    private ContentCatalog _catalog;
    private ILocalizer _words;

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

        _menu.transform.localScale = Vector3.one;
        _safe = (RectTransform)_menu.transform.Find("Safe");
        _fields = new SerializedObject(_menu);

        _pending = new PendingRun();
        _loader = new RecordingLoader();
        _profiles = new ProfileStore(new InMemorySaveStore());
        _words = new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath));
        _catalog = Catalog(withSecond: false);
    }

    [TearDown]
    public void CloseMenu() => EditorSceneManager.CloseScene(_scene, true);

    [Test]
    public void List_OneCardPerListedPlace_ThenSoon()
    {
        Enable();
        OpenPlaces();

        Assert.That(List().Cards.Count, Is.EqualTo(2));
        Assert.That(List().Cards[0].Id, Is.EqualTo(Jungle));
        Assert.That(List().Cards[0].NameLabel.text, Is.EqualTo("Jungle"));
        Assert.That(List().Cards[1].IsSoon, Is.True);
        Assert.That(List().Cards[1].NameLabel.text, Is.EqualTo("More places coming"));
        Assert.That(List().Cards[1].Id.Value, Is.Null, "the last card names a place.");

        _catalog = Catalog(withSecond: true);
        Enable();
        OpenPlaces();

        Assert.That(ActiveCards(), Is.EqualTo(3));
        Assert.That(List().Cards[1].Id, Is.EqualTo(Descent));
        Assert.That(List().Cards[2].IsSoon, Is.True);
    }

    [Test]
    public void OpenCard_ShowsBestOfFinal()
    {
        Enable();
        OpenPlaces();

        Assert.That(List().Cards[0].StatusLabel.gameObject.activeSelf, Is.False, "a best of nothing is drawn.");

        _profiles.Adopt(PlayerProfile.Default.WithBestStage(Jungle, 12));
        Enable();
        OpenPlaces();

        Assert.That(List().Cards[0].StatusLabel.text, Is.EqualTo("Best: stage 12 / 30"));
        Assert.That(List().Cards[0].FinishedLabel.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void OpenCard_Finished_ShowsGoldLabel()
    {
        _profiles.Adopt(PlayerProfile.Default.WithBestStage(Jungle, 30).WithFinished(Jungle));
        Enable();
        OpenPlaces();

        PlaceList.Card card = List().Cards[0];

        Assert.That(card.FinishedLabel.gameObject.activeSelf, Is.True);
        Assert.That(card.FinishedLabel.text, Is.EqualTo("Finished"));
        Assert.That(card.FinishedLabel.color, Is.EqualTo(Palette.Essence));
    }

    [Test]
    public void OpenCard_ShowsItsPicture()
    {
        Enable(Pictures());
        OpenPlaces();
        List().Layout(1f);

        PlaceList.Card card = List().Cards[0];

        Assert.That(card.Plate.sprite, Is.SameAs(Art(JungleArt)));
        Assert.That(card.Plate.color, Is.EqualTo(Color.white));

        // It fills the frame's opening and runs PictureTuckDp under the frame.
        float opening = PlaceList.FrameBorderDp - PlaceList.GlowPadDp;
        var plate = (RectTransform)card.Button.transform;

        Assert.That(plate.offsetMin.x, Is.EqualTo(opening - PlaceList.PictureTuckDp).Within(0.01f));
        Assert.That(plate.offsetMax.y, Is.EqualTo(-(opening - PlaceList.PictureTuckDp)).Within(0.01f));
    }

    [Test]
    public void Card_Frame_IsSlicedAndOutset()
    {
        Enable(Pictures());
        OpenPlaces();
        List().Layout(1f);

        foreach (PlaceList.Card card in List().Cards)
        {
            Assert.That(card.Frame.gameObject.activeSelf, Is.True);
            Assert.That(card.Frame.enabled, Is.True);
            Assert.That(card.Frame.sprite, Is.SameAs(Art(FrameArt)));
            Assert.That(card.Frame.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(card.Frame.pixelsPerUnitMultiplier * PlaceList.FrameBorderDp,
                Is.EqualTo(card.Frame.sprite.border.x).Within(0.01f), "the drawn border is not FrameBorderDp.");
            Assert.That(card.Frame.rectTransform.offsetMin.x, Is.EqualTo(-PlaceList.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMin.y, Is.EqualTo(-PlaceList.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMax.x, Is.EqualTo(PlaceList.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMax.y, Is.EqualTo(PlaceList.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.raycastTarget, Is.False);
        }
    }

    [Test]
    public void OpenCard_Picked_ShowsSelectedFrame()
    {
        _catalog = Catalog(withSecond: true);
        _profiles.Adopt(PlayerProfile.Default.WithFinished(Jungle).WithSelection(Ranger, Descent));
        Enable(Pictures());
        OpenPlaces();
        List().Layout(1f);

        PlaceList.Card picked = List().Cards[1];

        Assert.That(picked.Rim.gameObject.activeSelf, Is.True);
        Assert.That(picked.Rim.sprite, Is.SameAs(Art(FrameSelectedArt)));
        Assert.That(picked.Rim.color, Is.EqualTo(Color.white));
        Assert.That(picked.Rim.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(picked.Rim.transform.GetSiblingIndex(), Is.GreaterThan(picked.Frame.transform.GetSiblingIndex()), "the selected frame is under the normal one.");
        Assert.That(picked.Rim.rectTransform.offsetMin, Is.EqualTo(picked.Frame.rectTransform.offsetMin));
        Assert.That(picked.Rim.rectTransform.offsetMax, Is.EqualTo(picked.Frame.rectTransform.offsetMax));
    }

    [Test]
    public void OpenCard_Unpicked_RimOff()
    {
        _catalog = Catalog(withSecond: true);
        _profiles.Adopt(PlayerProfile.Default.WithFinished(Jungle).WithSelection(Ranger, Descent));
        Enable(Pictures());
        OpenPlaces();

        Assert.That(List().Cards[0].Rim.gameObject.activeSelf, Is.False);
        Assert.That(List().Cards[2].Rim.gameObject.activeSelf, Is.False, "the soon card is rimmed.");
    }

    [Test]
    public void LockedCard_TintedAndLocked_NotTappable()
    {
        _catalog = Catalog(withSecond: true);
        Enable(Pictures());
        OpenPlaces();
        List().Layout(1f);

        PlaceList.Card open = List().Cards[0];
        PlaceList.Card locked = List().Cards[1];

        Assert.That(locked.Locked, Is.True);
        Assert.That(locked.StatusLabel.text, Is.EqualTo("Finish Jungle to open"));
        Assert.That(locked.Plate.sprite, Is.SameAs(Art(JungleArt)), "a locked card hides its picture.");
        Assert.That(locked.Plate.color, Is.EqualTo(Palette.NodeLocked));
        Assert.That(open.Plate.color, Is.EqualTo(Color.white));
        Assert.That(open.Lock.gameObject.activeSelf, Is.False);

        Assert.That(locked.Lock.gameObject.activeSelf, Is.True);
        Assert.That(locked.Lock.sprite, Is.SameAs(Art(LockArt)));
        Assert.That(locked.Lock.rectTransform.sizeDelta, Is.EqualTo(new Vector2(PlaceList.LockDp, PlaceList.LockDp)));
        Assert.That(locked.Lock.rectTransform.anchorMin, Is.EqualTo(Vector2.one), "the padlock is not top right.");
        Assert.That(locked.Lock.transform.GetSiblingIndex(), Is.GreaterThan(locked.Frame.transform.GetSiblingIndex()));
        Assert.That(locked.Button.interactable, Is.False, "a locked card presses.");

        locked.Button.onClick.Invoke();

        Assert.That(_profiles.Current.SelectedModeId.Value, Is.Not.EqualTo(Descent.Value));
        Assert.That(List().gameObject.activeSelf, Is.True, "a tap on a locked card left the list.");
    }

    [Test]
    public void SoonCard_ShowsSoonPicture_NoLock()
    {
        Enable(Pictures());
        OpenPlaces();

        PlaceList.Card soon = List().Cards[1];

        Assert.That(soon.IsSoon, Is.True);
        Assert.That(soon.Plate.sprite, Is.SameAs(Art(SoonArt)));
        Assert.That(soon.Plate.color, Is.EqualTo(new Color(1f, 1f, 1f, PlaceList.SoonAlpha)));
        Assert.That(soon.Lock.gameObject.activeSelf, Is.False);
        Assert.That(soon.Frame.gameObject.activeSelf, Is.True);
        Assert.That(soon.Rim.gameObject.activeSelf, Is.False);
        Assert.That(soon.Button.interactable, Is.False);
    }

    [Test]
    public void Card_NoPicture_FallsBackToFlat()
    {
        _catalog = Catalog(withSecond: true);

        // A book that knows no place, then no book at all.
        foreach (PlaceLookBook book in new[] { new PlaceLookBook(new Dictionary<ContentId, Sprite>()), null })
        {
            Enable(book);
            OpenPlaces();
            List().Layout(1f);

            PlaceList.Card open = List().Cards[0];
            PlaceList.Card locked = List().Cards[1];

            Assert.That(open.Plate.sprite, Is.Null);
            Assert.That(open.Plate.color, Is.Not.EqualTo(Color.white), "a flat card is drawn white.");
            Assert.That(open.Plate.color.a, Is.EqualTo(1f));
            Assert.That(locked.Plate.sprite, Is.Null);
            Assert.That(locked.Plate.color, Is.Not.EqualTo(open.Plate.color));
            Assert.That(open.NameLabel.text, Is.EqualTo("Jungle"));
        }
    }

    [Test]
    public void Card_Scrim_SitsBetweenPictureAndFrame()
    {
        Enable(Pictures());
        OpenPlaces();
        List().Layout(1f);

        PlaceList.Card card = List().Cards[0];

        // Drawn after the picture's own pixels (a child of it) and before the words, and under the frame.
        Assert.That(card.Scrim.transform.parent, Is.SameAs(card.Plate.transform));
        Assert.That(card.Scrim.transform.GetSiblingIndex(), Is.LessThan(card.NameLabel.transform.GetSiblingIndex()));
        Assert.That(card.Plate.transform.GetSiblingIndex(), Is.LessThan(card.Frame.transform.GetSiblingIndex()));
        Assert.That(card.Scrim.color.r, Is.EqualTo(Palette.Ink.r).Within(0.001f));
        Assert.That(card.Scrim.color.a, Is.EqualTo(PlaceList.ScrimAlpha).Within(0.001f));
        Assert.That(card.Scrim.raycastTarget, Is.False);

        // It reaches above every word band.
        float scrimTop = card.Scrim.rectTransform.offsetMax.y;

        foreach (TMP_Text label in new[] { card.NameLabel, card.StatusLabel, card.FinishedLabel })
        {
            Assert.That(label.rectTransform.offsetMax.y, Is.LessThanOrEqualTo(scrimTop + 0.01f), $"{label.name} rises above the scrim.");
            Assert.That(label.rectTransform.offsetMin.y, Is.GreaterThanOrEqualTo(PlaceList.PictureTuckDp), $"{label.name} is under the frame.");
        }
    }

    [Test]
    public void OpenCard_Tap_PicksAndGoesHome()
    {
        _catalog = Catalog(withSecond: true);
        _profiles.Adopt(PlayerProfile.Default.WithFinished(Jungle));
        Enable();
        OpenPlaces();

        List().Cards[1].Button.onClick.Invoke();

        Assert.That(_profiles.Current.SelectedModeId, Is.EqualTo(Descent));
        Assert.That(_profiles.Current.SelectedCharacterId, Is.EqualTo(Ranger), "the class moved with the place.");
        Assert.That(List().gameObject.activeSelf, Is.False, "home is not showing.");
        Assert.That(_menu.SelectedMode, Is.EqualTo(Descent));
        Assert.That(Field<TabBar>("_tabs").Current, Is.EqualTo(Tab.Play));
        Assert.That(_pending.IsSet, Is.False, "picking a place started a run.");
        Assert.That(_loader.Asked, Is.Empty);

        // The soon card does nothing.
        OpenPlaces();
        List().Cards[2].Button.onClick.Invoke();

        Assert.That(List().gameObject.activeSelf, Is.True);
    }

    [Test]
    public void List_FitsPortrait([Values(0, 1)] int size)
    {
        _catalog = Catalog(withSecond: true);
        Enable();

        Vector2 area = size == 0 ? new Vector2(411f, 914f) : new Vector2(360f, 780f);
        Layout(area);
        OpenPlaces();
        List().Layout(1f);

        Rect safe = new Rect(Vector2.zero, area);
        var rows = new List<Rect>();

        foreach (PlaceList.Card card in List().Cards)
        {
            if (!card.Root.gameObject.activeSelf)
            {
                continue;
            }

            Rect rect = InSafe(card.Root);
            rows.Add(rect);

            Assert.That(rect.height, Is.EqualTo(PlaceList.CardHeightDp).Within(0.01f));
            Assert.That(rect.xMin >= -0.01f && rect.xMax <= safe.xMax + 0.01f, Is.True, $"card {rect} leaves {safe} sideways.");
        }

        for (int a = 0; a < rows.Count; a++)
        {
            for (int b = a + 1; b < rows.Count; b++)
            {
                Assert.That(rows[a].Overlaps(rows[b]), Is.False, $"card {a} overlaps card {b}");
            }
        }

        var viewport = (RectTransform)List().Content.parent;

        Assert.That(InSafe(viewport).yMin, Is.GreaterThanOrEqualTo(TabBar.HeightDp - 0.01f));
        Assert.That(InSafe(viewport).yMax, Is.LessThanOrEqualTo(safe.yMax + 0.01f));

        foreach (TMP_Text label in List().GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(12f), $"{label.name} is under 12 sp.");
        }
    }

    [Test]
    public void Card_StaysOneHundredFortyDp([Values(0, 1)] int size)
    {
        _catalog = Catalog(withSecond: true);
        Enable(Pictures());

        Layout(size == 0 ? new Vector2(411f, 914f) : new Vector2(360f, 780f));
        OpenPlaces();
        List().Layout(1f);

        foreach (PlaceList.Card card in List().Cards)
        {
            Assert.That(card.Root.rect.height, Is.EqualTo(PlaceList.CardHeightDp).Within(0.01f));

            // The frame's glow overlaps the gap and the margin, never the neighbouring card.
            Assert.That(PlaceList.GlowPadDp, Is.LessThan(PlaceList.GapDp));
            Assert.That(PlaceList.GlowPadDp, Is.LessThan(PlaceList.MarginDp));
            Assert.That(card.Frame.rectTransform.rect.height, Is.EqualTo(PlaceList.CardHeightDp + (2f * PlaceList.GlowPadDp)).Within(0.01f));
        }
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private PlaceList List() => Field<PlaceList>("_places");

    private int ActiveCards()
    {
        int count = 0;

        foreach (PlaceList.Card card in List().Cards)
        {
            if (card.Root.gameObject.activeSelf)
            {
                count++;
            }
        }

        return count;
    }

    private static Sprite Art(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        Assert.That(sprite, Is.Not.Null, $"{path} is not a sprite.");

        return sprite;
    }

    /// <summary>A book giving the Jungle and the test-only second place the Jungle's picture.</summary>
    private static PlaceLookBook Pictures() => new PlaceLookBook(new Dictionary<ContentId, Sprite>
    {
        [Jungle] = Art(JungleArt),
        [Descent] = Art(JungleArt),
    });

    private void Enable(PlaceLookBook pictures = null)
    {
        _menu.Construct(_pending, new SavedRun(), _loader, _words, _catalog, _profiles, new ClassSelectRoster(new[] { Ranger }), places: pictures);

        typeof(MenuPresenter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_menu, null);

        Field<ClassSelectPresenter>("_classSelect").Construct(_pending, _catalog, _loader, _words, _profiles, new ClassSelectRoster(new[] { Ranger }));
    }

    /// <summary>The Places tab, tapped.</summary>
    private void OpenPlaces() => Field<Button>("_placeCard").onClick.Invoke();

    private void Layout(Vector2 areaDp)
    {
        var canvas = (RectTransform)_menu.transform;
        canvas.anchorMin = canvas.anchorMax = canvas.pivot = Vector2.zero;
        canvas.anchoredPosition = Vector2.zero;
        canvas.sizeDelta = areaDp;

        _safe.anchorMin = Vector2.zero;
        _safe.anchorMax = Vector2.zero;
        _safe.pivot = Vector2.zero;
        _safe.anchoredPosition = Vector2.zero;
        _safe.sizeDelta = areaDp;

        _menu.Layout(1f, false);
    }

    private static ContentCatalog Catalog(bool withSecond)
    {
        var ranger = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);
        var jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath).ToSpec();
        var enemies = new List<EnemySpec>();

        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
        {
            enemies.Add(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        return new ContentCatalog(
            new[] { ranger.ToSpec() },
            enemies,
            withSecond ? new[] { jungle, Second() } : new[] { jungle });
    }

    private static ModeSpec Second() => new ModeSpec(
        Descent,
        new LocKey("mode.descent.name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        new ScalingSpec(
            new BudgetCurve(40f, 12f, 0.9f),
            new WaveCurve(2, 5, 2, 5),
            new ConcurrencyCurve(10, 2),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        new XpCurve(20f, 12f, 1.4f),
        Array.Empty<RosterEntry>(),
        unlockedBy: Jungle,
        isListed: true);

    private T Field<T>(string name) where T : UnityEngine.Object
    {
        var value = _fields.FindProperty(name).objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, $"{name} is not wired in Menu.unity.");

        return value;
    }

    /// <summary>A rect in the safe area's own space, canvas units being dp at this scale.</summary>
    private Rect InSafe(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector3 low = _safe.InverseTransformPoint(corners[0]) - (Vector3)(_safe.rect.min);
        Vector3 high = _safe.InverseTransformPoint(corners[2]) - (Vector3)(_safe.rect.min);

        return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
    }

    private sealed class RecordingLoader : SceneLoader
    {
        public List<string> Asked { get; } = new List<string>();

        public override Task LoadAsync(string sceneName)
        {
            Asked.Add(sceneName);

            return Task.CompletedTask;
        }
    }
}
