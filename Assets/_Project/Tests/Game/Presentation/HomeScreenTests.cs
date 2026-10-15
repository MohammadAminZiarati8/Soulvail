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
/// UI-11e: the home screen on the real <c>Menu.unity</c> — the picks resolved, the two cards, Play
/// and Continue, and the tab bar, laid out on a 9:20 phone. The scene is opened additively, its
/// <c>Safe</c> object sized in dp (one canvas unit to the dp) and the presenter injected by hand;
/// <c>OnEnable</c> is invoked by reflection because EditMode never runs it.
/// </summary>
[TestFixture]
public sealed class HomeScreenTests
{
    private const string ScenePath = "Assets/_Project/Scenes/Menu.unity";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";

    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Wastes = new ContentId("mode.wastes");
    private static readonly ContentId Ranger = new ContentId("character.ranger");
    private static readonly ContentId Oathbound = new ContentId("character.oathbound");
    private static readonly ContentId Gravecaller = new ContentId("character.gravecaller");

    private static readonly Vector2 Portrait = new Vector2(411f, 914f);
    private static readonly Vector2 Narrow = new Vector2(360f, 780f);

    private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

    private Scene _scene;
    private RectTransform _safe;
    private MenuPresenter _menu;
    private SerializedObject _fields;
    private PendingRun _pending;
    private RecordingLoader _loader;
    private ProfileStore _profiles;
    private SavedRun _saved;
    private ContentCatalog _catalog;
    private ClassSelectRoster _roster;

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
        _saved = new SavedRun();
        _catalog = Catalog();
        _roster = new ClassSelectRoster(new[] { Ranger });
    }

    [TearDown]
    public void CloseMenu()
    {
        EditorSceneManager.CloseScene(_scene, true);

        foreach (UnityEngine.Object made in _made)
        {
            if (made != null)
            {
                UnityEngine.Object.DestroyImmediate(made);
            }
        }

        _made.Clear();
    }

    // ---- Rule 1: the picks resolve, never fail --------------------------------------------------

    [Test]
    public void Picks_Fresh_JungleAndRanger()
    {
        Enable();

        Assert.That(_menu.SelectedMode, Is.EqualTo(Jungle));
        Assert.That(_menu.SelectedCharacter, Is.EqualTo(Ranger));
    }

    [Test]
    public void Picks_LockedPlace_FallsBack()
    {
        _profiles.Adopt(PlayerProfile.Default.WithSelection(Ranger, Wastes));
        Enable();

        Assert.That(_menu.SelectedMode, Is.EqualTo(Jungle), "Wastes opens when the Jungle is finished.");

        _profiles.Adopt(PlayerProfile.Default.WithFinished(Jungle).WithSelection(Ranger, Wastes));
        _menu.Draw();

        Assert.That(_menu.SelectedMode, Is.EqualTo(Wastes), "once open, the pick stands.");
    }

    [Test]
    public void Picks_UnownedClass_FallsBack()
    {
        _roster = new ClassSelectRoster(new[] { Gravecaller, Oathbound });
        _profiles.Adopt(PlayerProfile.Default.WithSelection(Gravecaller, Jungle));
        Enable();

        Assert.That(_menu.SelectedCharacter, Is.EqualTo(Oathbound), "the Gravecaller is priced and unowned.");
    }

    // ---- Rules 2 and 3: the cards ----------------------------------------------------------------

    [Test]
    public void PlaceCard_ShowsBest()
    {
        _profiles.Adopt(PlayerProfile.Default.WithBestStage(Jungle, 12));
        Enable();

        Assert.That(Field<TMP_Text>("_placeName").text, Is.EqualTo("Jungle"));
        Assert.That(Field<TMP_Text>("_placeBest").gameObject.activeSelf, Is.True);
        Assert.That(Field<TMP_Text>("_placeBest").text, Is.EqualTo("Best: stage 12"));
    }

    [Test]
    public void PlaceCard_NoBest_NoLine()
    {
        Enable();

        Assert.That(Field<TMP_Text>("_placeBest").gameObject.activeSelf, Is.False);
    }

    [Test]
    public void PlaceCard_Tap_OpensPlaces()
    {
        // The Places tab holds nothing until UI-11g: the tap is wired and changes nothing else.
        Enable();

        Assert.That(() => Field<Button>("_placeCard").onClick.Invoke(), Throws.Nothing);
        Assert.That(_loader.Asked, Is.Empty);
        Assert.That(_pending.IsSet, Is.False);
    }

    [Test]
    public void ClassCard_Tap_OpensHeroes()
    {
        Enable();

        Field<Button>("_classCard").onClick.Invoke();

        Assert.That(Field<HeroGrid>("_heroes").gameObject.activeSelf, Is.True, "the class card opens the hero grid (UI-11f).");
        Assert.That(Field<TabBar>("_tabs").Current, Is.EqualTo(Tab.Heroes));
        Assert.That(_pending.IsSet, Is.False, "opening the grid starts nothing.");
    }

    // ---- Rule 4: Play and Continue ---------------------------------------------------------------

    [Test]
    public void Play_StartsWithPicks()
    {
        Enable();

        Field<Button>("_descend").onClick.Invoke();

        Assert.That(_pending.IsSet, Is.True);
        Assert.That(_pending.ModeId, Is.EqualTo(Jungle));
        Assert.That(_pending.CharacterId, Is.EqualTo(Ranger));
        Assert.That(_loader.Asked, Is.EqualTo(new[] { SceneLoader.Run }));
        Assert.That(_profiles.Current.SelectedModeId, Is.EqualTo(Jungle), "the picks are saved.");
        Assert.That(_profiles.Current.SelectedCharacterId, Is.EqualTo(Ranger));
        Assert.That(Field<Button>("_descend").interactable, Is.False, "a second tap finds a dead button.");
    }

    [Test]
    public void WithSave_ContinueGreenOnTop()
    {
        _saved.Set(Snapshot());
        Enable();
        Layout(Portrait, true);

        Button primary = Field<Button>("_continue");
        Button secondary = Field<Button>("_descend");

        Assert.That(primary.gameObject.activeSelf, Is.True);
        Assert.That(SpriteFile(primary), Is.EqualTo("Button_Green.png"));
        Assert.That(SpriteFile(secondary), Is.EqualTo("Button_Blue.png"));
        Assert.That(InSafe(primary).center.y, Is.GreaterThan(InSafe(secondary).center.y));
        Assert.That(Field<TMP_Text>("_descendLabel").text, Is.EqualTo("Play"));
        Assert.That(Field<TMP_Text>("_continueLabel").text, Is.EqualTo("Continue"));
    }

    // ---- Rule 5: the tab bar (UI-11e, worn in UI-11h-a) ------------------------------------------

    [Test]
    public void TabBar_OnSafeBottom_PlaySelected()
    {
        Enable();
        Layout(Portrait, false);

        var bar = Field<TabBar>("_tabs");
        Rect rect = InSafe((RectTransform)bar.transform);

        Assert.That(rect.min.y, Is.EqualTo(0f).Within(0.01f), "on the safe area's bottom edge.");
        Assert.That(rect.height, Is.EqualTo(TabBar.HeightDp).Within(0.01f));
        Assert.That(rect.width, Is.EqualTo(Portrait.x).Within(0.01f));
        Assert.That(bar.Current, Is.EqualTo(Tab.Play));

        Button[] tabs = Tabs(bar);
        var raised = new List<Tab>();
        bar.Selected += raised.Add;
        tabs[0].onClick.Invoke();
        tabs[2].onClick.Invoke();

        Assert.That(raised, Is.EqualTo(new[] { Tab.Heroes, Tab.Places }));
    }

    [Test]
    public void TabBar_Plate_ScalesItsCapsToTheBarHeight()
    {
        Enable();
        Layout(Portrait, false);

        var bar = Field<TabBar>("_tabs");
        var plate = (Image)typeof(TabBar).GetField("_plate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bar);

        Assert.That(plate, Is.Not.Null, "the bar's own Image is wired.");
        Assert.That(plate.gameObject, Is.SameAs(bar.gameObject), "it is the bar's own Image.");
        Assert.That(System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(plate.sprite)), Is.EqualTo("TabBar_Plate.png"));
        Assert.That(plate.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(plate.pixelsPerUnitMultiplier, Is.EqualTo(plate.sprite.rect.height / TabBar.HeightDp).Within(0.0001f));

        // A cap is the border at the bar's height: the drawn cap is border / multiplier.
        Assert.That(plate.sprite.border.x / plate.pixelsPerUnitMultiplier, Is.EqualTo(CapWidth(plate)).Within(0.01f));
        Assert.That(((RectTransform)bar.transform).rect.width, Is.EqualTo(Portrait.x).Within(0.01f), "the plate stretches to the bar's width.");
    }

    [Test]
    public void TabBar_Tabs_SitBetweenTheCaps_TileAndLabelFit([Values(0, 1)] int size)
    {
        Enable();
        Vector2 area = size == 0 ? Portrait : Narrow;
        Layout(area, false);

        var bar = Field<TabBar>("_tabs");
        float cap = CapWidth(Plate(bar));
        float column = (area.x - 2f * cap) / 3f;
        Button[] tabs = Tabs(bar);
        TMP_Text[] labels = Labels(bar);

        for (int i = 0; i < 3; i++)
        {
            Rect tile = InSafe(tabs[i]);
            Rect label = InSafe(labels[i].rectTransform);
            float centre = cap + (i + 0.5f) * column;

            Assert.That(tile.height, Is.EqualTo(TabBar.IconDp).Within(0.01f), "44 dp tall.");
            Assert.That(tile.center.x, Is.EqualTo(centre).Within(0.01f), "centred in its column.");
            Assert.That(tile.xMin, Is.GreaterThanOrEqualTo(cap + i * column - 0.01f), "inside its column.");
            Assert.That(tile.xMax, Is.LessThanOrEqualTo(cap + (i + 1) * column + 0.01f));
            Assert.That(((Image)tabs[i].targetGraphic).preserveAspect, Is.True);
            Assert.That(label.yMax, Is.LessThanOrEqualTo(tile.yMin + 0.01f), "the label is under the tile.");
            Assert.That(label.yMin, Is.GreaterThanOrEqualTo(-0.01f), "and on the bar.");
            Assert.That(label.xMin, Is.GreaterThanOrEqualTo(cap + i * column - 0.01f));
            Assert.That(label.xMax, Is.LessThanOrEqualTo(cap + (i + 1) * column + 0.01f));
            Assert.That(labels[i].fontSize, Is.GreaterThanOrEqualTo(12f), "UI-11e rule 6's floor.");
        }
    }

    [Test]
    public void TabBar_Tap_StillTheWholeColumn([Values(0, 1)] int size)
    {
        Enable();
        Vector2 area = size == 0 ? Portrait : Narrow;
        Layout(area, false);

        var bar = Field<TabBar>("_tabs");
        float cap = CapWidth(Plate(bar));
        float column = (area.x - 2f * cap) / 3f;
        Button[] tabs = Tabs(bar);

        for (int i = 0; i < 3; i++)
        {
            Rect tile = InSafe(tabs[i]);
            Vector4 pad = tabs[i].targetGraphic.raycastPadding;
            Rect reach = Rect.MinMaxRect(tile.xMin + pad.x, tile.yMin + pad.y, tile.xMax - pad.z, tile.yMax - pad.w);

            Assert.That(reach.xMin, Is.EqualTo(cap + i * column).Within(0.01f), "the column's left edge.");
            Assert.That(reach.xMax, Is.EqualTo(cap + (i + 1) * column).Within(0.01f), "and its right.");
            Assert.That(reach.yMin, Is.EqualTo(0f).Within(0.01f), "the bar's bottom.");
            Assert.That(reach.yMax, Is.EqualTo(TabBar.HeightDp).Within(0.01f), "and its top.");
            Assert.That(reach.width, Is.GreaterThanOrEqualTo(48f));
            Assert.That(reach.height, Is.GreaterThanOrEqualTo(48f));
        }
    }

    [Test]
    public void TabBar_Show_LightsOneTab()
    {
        Enable();
        Layout(Portrait, false);

        var bar = Field<TabBar>("_tabs");
        Button[] tabs = Tabs(bar);
        TMP_Text[] labels = Labels(bar);
        string[] names = { "Heroes", "Play", "Places" };

        foreach (Tab lit in new[] { Tab.Play, Tab.Heroes, Tab.Places })
        {
            bar.Show(lit);

            for (int i = 0; i < 3; i++)
            {
                bool on = i == (int)lit;

                Assert.That(SpriteFile(tabs[i]), Is.EqualTo($"TabBar_{names[i]}{(on ? "On" : string.Empty)}.png"), $"{lit}: tab {i}");
                Assert.That(labels[i].color, Is.EqualTo(on ? Palette.HudText : Palette.Neutral), $"{lit}: label {i}");
            }
        }

        bar.Show(Tab.Heroes);
        Layout(Portrait, false);

        Assert.That(SpriteFile(tabs[0]), Is.EqualTo("TabBar_HeroesOn.png"), "Layout redresses and keeps the lit tab.");
    }

    [Test]
    public void TabBar_Press_Tints_NotSwaps()
    {
        Enable();
        Layout(Portrait, false);

        foreach (Button tab in Tabs(Field<TabBar>("_tabs")))
        {
            Assert.That(tab.transition, Is.EqualTo(Selectable.Transition.ColorTint));
            Assert.That(tab.colors.pressedColor.r, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(tab.colors.pressedColor.g, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(tab.colors.pressedColor.b, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(tab.spriteState.pressedSprite, Is.Null, "there is no pressed sprite.");
        }
    }

    [Test]
    public void TabBar_Height_ScrollAreasEndAboveIt()
    {
        Enable();
        Layout(Portrait, false);

        Assert.That(TabBar.HeightDp, Is.EqualTo(72f));
        Assert.That(InSafe((RectTransform)Field<TabBar>("_tabs").transform).height, Is.EqualTo(72f).Within(0.01f));

        // HeroGridTests and PlaceListTests assert their viewports start at TabBar.HeightDp.
    }

    [Test]
    public void TabBar_FitsPortrait_BothSizes([Values(0, 1)] int size)
    {
        Enable();
        Vector2 area = size == 0 ? Portrait : Narrow;
        Layout(area, false);

        var bar = Field<TabBar>("_tabs");
        Rect barRect = InSafe((RectTransform)bar.transform);
        float cap = CapWidth(Plate(bar));
        var pieces = new List<(string Name, Rect Rect)>
        {
            ("left cap", Rect.MinMaxRect(0f, 0f, cap, TabBar.HeightDp)),
            ("right cap", Rect.MinMaxRect(area.x - cap, 0f, area.x, TabBar.HeightDp)),
        };

        Button[] tabs = Tabs(bar);
        TMP_Text[] labels = Labels(bar);

        for (int i = 0; i < 3; i++)
        {
            pieces.Add(($"tile {i}", InSafe(tabs[i])));
            pieces.Add(($"label {i}", InSafe(labels[i].rectTransform)));
        }

        foreach ((string name, Rect rect) in pieces)
        {
            Assert.That(rect.xMin >= barRect.xMin - 0.01f && rect.yMin >= barRect.yMin - 0.01f && rect.xMax <= barRect.xMax + 0.01f && rect.yMax <= barRect.yMax + 0.01f, Is.True, $"{name} {rect} leaves the bar {barRect}");
        }

        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                if (pieces[a].Name.StartsWith("tile") && pieces[b].Name.StartsWith("label") && pieces[a].Name[pieces[a].Name.Length - 1] == pieces[b].Name[pieces[b].Name.Length - 1])
                {
                    continue;
                }

                Assert.That(pieces[a].Rect.Overlaps(pieces[b].Rect), Is.False, $"{pieces[a].Name} overlaps {pieces[b].Name}");
            }
        }
    }

    // ---- Rule 6: it fits ---------------------------------------------------------------------------

    [Test]
    public void Home_FitsPortrait([Values(true, false)] bool hasSave, [Values(0, 1)] int size)
    {
        if (hasSave)
        {
            _saved.Set(Snapshot());
        }

        Enable();

        Vector2 area = size == 0 ? Portrait : Narrow;
        Layout(area, hasSave);

        var safe = new Rect(Vector2.zero, area);
        var pieces = new List<(string Name, Rect Rect)>
        {
            ("title plate", InSafe(Field<Image>("_titlePlate").rectTransform)),
            ("place card", InSafe((RectTransform)Field<Button>("_placeCard").transform)),
            ("class card", InSafe((RectTransform)Field<Button>("_classCard").transform)),
            ("play", InSafe(Field<Button>("_descend"))),
            ("tab bar", InSafe((RectTransform)Field<TabBar>("_tabs").transform)),
        };

        if (hasSave)
        {
            pieces.Add(("continue", InSafe(Field<Button>("_continue"))));
        }

        foreach ((string name, Rect rect) in pieces)
        {
            Assert.That(rect.xMin >= -0.01f && rect.yMin >= -0.01f && rect.xMax <= safe.xMax + 0.01f && rect.yMax <= safe.yMax + 0.01f, Is.True, $"{name} {rect} leaves {safe}");
        }

        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                Assert.That(pieces[a].Rect.Overlaps(pieces[b].Rect), Is.False, $"{pieces[a].Name} overlaps {pieces[b].Name}");
            }
        }

        foreach (string label in new[] { "_title", "_descendLabel", "_continueLabel", "_placeName", "_placeBest", "_className" })
        {
            Assert.That(Field<TMP_Text>(label).fontSize, Is.GreaterThanOrEqualTo(12f), label);
        }

        foreach (Button tap in new[] { Field<Button>("_placeCard"), Field<Button>("_classCard"), Field<Button>("_descend") })
        {
            Rect rect = InSafe(tap);

            Assert.That(rect.width, Is.GreaterThanOrEqualTo(48f));
            Assert.That(rect.height, Is.GreaterThanOrEqualTo(48f));
        }

        Assert.That(InSafe((RectTransform)Field<TabBar>("_tabs").transform).height, Is.GreaterThanOrEqualTo(48f));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private void Enable()
    {
        _menu.Construct(
            _pending,
            _saved,
            _loader,
            new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath)),
            _catalog,
            _profiles,
            _roster);

        typeof(MenuPresenter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_menu, null);

        // The class page is dressed in the scene and put down by its own Start, which never runs.
        var classSelect = Field<ClassSelectPresenter>("_classSelect");
        classSelect.Construct(_pending, _catalog, _loader, new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath)), _profiles, _roster);
    }

    private void Layout(Vector2 areaDp, bool hasSave)
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

        _menu.Layout(1f, hasSave);
    }

    private ContentCatalog Catalog()
    {
        var jungle = AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath);
        var wastes = UnityEngine.Object.Instantiate(jungle);
        _made.Add(wastes);

        var serialized = new SerializedObject(wastes);
        serialized.FindProperty("_id").stringValue = Wastes.Value;
        serialized.FindProperty("_nameKey").stringValue = "mode.wastes.name";
        serialized.FindProperty("_unlockedBy").stringValue = Jungle.Value;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var enemies = new List<EnemySpec>();

        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
        {
            enemies.Add(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        return new ContentCatalog(
            new[]
            {
                Character(OathboundPath),
                Character(GravecallerPath),
                Character(RangerPath),
            },
            enemies,
            new[] { jungle.ToSpec(), wastes.ToSpec() });
    }

    private static CharacterSpec Character(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition.ToSpec();
    }

    private static RunSnapshot Snapshot() => new RunSnapshot(
        RunSnapshot.CurrentVersion,
        Jungle,
        Ranger,
        seed: 4_242,
        stageIndex: 9,
        random: default,
        playerHp: 62f,
        playerShield: 9f,
        runTime: 412.5f,
        writtenAt: new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero),
        level: 1,
        xp: 0f,
        pendingLevelUps: 0,
        takenNodeIds: Array.Empty<ContentId>(),
        manualSkillIds: new ContentId[Soulvail.Core.Combat.SkillRunner.MaxManualSlots],
        default,
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>());

    private T Field<T>(string name) where T : UnityEngine.Object
    {
        var value = _fields.FindProperty(name).objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, $"{name} is not wired in Menu.unity.");

        return value;
    }

    private static Button[] Tabs(TabBar bar) =>
        (Button[])typeof(TabBar).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bar);

    private static TMP_Text[] Labels(TabBar bar) =>
        (TMP_Text[])typeof(TabBar).GetField("_labels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bar);

    private static Image Plate(TabBar bar) =>
        (Image)typeof(TabBar).GetField("_plate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bar);

    /// <summary>A cap's width in dp: the plate's border at the bar's height.</summary>
    private static float CapWidth(Image plate) => plate.sprite.border.x * TabBar.HeightDp / plate.sprite.rect.height;

    private static string SpriteFile(Button button)
    {
        return System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(((Image)button.targetGraphic).sprite));
    }

    private Rect InSafe(Button button) => InSafe((RectTransform)button.transform);

    private Rect InSafe(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector3 min = _safe.InverseTransformPoint(corners[0]);
        Vector3 max = _safe.InverseTransformPoint(corners[2]);

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
