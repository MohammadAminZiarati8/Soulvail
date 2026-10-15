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
/// UI-11f: the Heroes tab on the real <c>Menu.unity</c> — a card per roster class, the pick rimmed, a
/// locked card greyed with its price, a tap opening the character page on that class, Choose picking
/// without starting a run, and the grid fitting a 9:20 phone. The scene is opened additively and the
/// presenters injected by hand; <c>OnEnable</c> is invoked by reflection because EditMode never runs it.
/// </summary>
[TestFixture]
public sealed class HeroGridTests
{
    private const string ScenePath = "Assets/_Project/Scenes/Menu.unity";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string FrameArt = "Assets/_Project/Art/UI/Home/HeroCard_Frame.png";
    private const string SelectedArt = "Assets/_Project/Art/UI/Home/HeroCard_FrameSelected.png";
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";

    private static readonly string[] CharacterPaths =
    {
        "Assets/_Project/Data/Characters/Ranger.asset",
        "Assets/_Project/Data/Characters/Oathbound.asset",
        "Assets/_Project/Data/Characters/Gravecaller.asset",
        "Assets/_Project/Data/Characters/Emberwright.asset",
    };

    private static readonly ContentId Ranger = new ContentId("character.ranger");
    private static readonly ContentId Oathbound = new ContentId("character.oathbound");
    private static readonly ContentId Gravecaller = new ContentId("character.gravecaller");
    private static readonly ContentId Emberwright = new ContentId("character.emberwright");

    private Scene _scene;
    private RectTransform _safe;
    private MenuPresenter _menu;
    private SerializedObject _fields;
    private PendingRun _pending;
    private RecordingLoader _loader;
    private InMemorySaveStore _store;
    private ProfileStore _profiles;
    private ContentCatalog _catalog;
    private ClassSelectRoster _roster;
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
        _store = new InMemorySaveStore();
        _profiles = new ProfileStore(_store);
        _words = new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath));
        _catalog = Catalog();
        _roster = new ClassSelectRoster(new[] { Ranger, Oathbound, Gravecaller, Emberwright });
    }

    [TearDown]
    public void CloseMenu() => EditorSceneManager.CloseScene(_scene, true);

    [Test]
    public void Grid_OneCardPerRosterClass_InOrder()
    {
        Enable();
        OpenHeroes();

        HeroGrid grid = Grid();

        Assert.That(grid.Cards.Count, Is.EqualTo(4));
        Assert.That(new[] { grid.Cards[0].Id, grid.Cards[1].Id, grid.Cards[2].Id, grid.Cards[3].Id },
            Is.EqualTo(new[] { Ranger, Oathbound, Gravecaller, Emberwright }));
        Assert.That(grid.Cards[0].NameLabel.text, Is.EqualTo(_words.Get(_catalog.Character(Ranger).NameKey)));

        // Today's roster is the Ranger alone: one card, and the other three are gone, not hidden.
        _roster = new ClassSelectRoster(new[] { Ranger });
        Enable();
        OpenHeroes();

        int shown = 0;

        foreach (HeroGrid.Card card in Grid().Cards)
        {
            if (card.Root.gameObject.activeSelf)
            {
                shown++;
            }
        }

        Assert.That(shown, Is.EqualTo(1));
    }

    [Test]
    public void Card_Frame_IsSlicedAndOutset()
    {
        Enable();
        OpenHeroes();
        Grid().Layout(1f);

        foreach (HeroGrid.Card card in Grid().Cards)
        {
            Assert.That(card.Frame.gameObject.activeSelf, Is.True);
            Assert.That(card.Frame.enabled, Is.True);
            Assert.That(card.Frame.sprite, Is.SameAs(Art(FrameArt)));
            Assert.That(card.Frame.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(card.Frame.pixelsPerUnitMultiplier * HeroGrid.FrameBorderDp,
                Is.EqualTo(card.Frame.sprite.border.x).Within(0.01f), "the drawn border is not FrameBorderDp.");
            Assert.That(card.Frame.rectTransform.offsetMin.x, Is.EqualTo(-HeroGrid.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMin.y, Is.EqualTo(-HeroGrid.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMax.x, Is.EqualTo(HeroGrid.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.rectTransform.offsetMax.y, Is.EqualTo(HeroGrid.GlowPadDp).Within(0.01f));
            Assert.That(card.Frame.raycastTarget, Is.False);
        }

        // CardRatio is unchanged: the frame is drawn outside the card, the card keeps its size.
        Rect card0 = InSafe(Grid().Cards[0].Root);
        Assert.That(card0.height, Is.EqualTo(card0.width * HeroGrid.CardRatio).Within(0.01f));
    }

    [Test]
    public void Card_Picked_ShowsSelectedFrame()
    {
        _profiles.Adopt(PlayerProfile.Default.WithSelection(Ranger, new ContentId("mode.jungle")));
        Enable(Looks(true));
        OpenHeroes();
        Grid().Layout(1f);

        HeroGrid.Card picked = Grid().Cards[0];

        Assert.That(picked.Locked, Is.False);
        Assert.That(picked.Rim.gameObject.activeSelf, Is.True);
        Assert.That(picked.Rim.sprite, Is.SameAs(Art(SelectedArt)));
        Assert.That(picked.Rim.color, Is.EqualTo(Color.white), "the sprite carries the cyan.");
        Assert.That(picked.Rim.type, Is.EqualTo(Image.Type.Sliced));
        Assert.That(picked.Rim.transform.GetSiblingIndex(), Is.GreaterThan(picked.Frame.transform.GetSiblingIndex()), "the selected frame is not drawn over the normal one.");
        Assert.That(picked.PickedLabel.gameObject.activeSelf, Is.True);
        Assert.That(picked.PickedLabel.text, Is.EqualTo("Picked"));
    }

    [Test]
    public void Card_WithPortrait_ShowsIt_StandingOnTheName()
    {
        CharacterLookBook looks = Looks(true);

        Enable(looks);
        OpenHeroes();
        Grid().Layout(1f);

        HeroGrid.Card card = Grid().Cards[0];
        Rect opening = InSafe((RectTransform)card.Button.transform);
        Rect portrait = InSafe(card.Emblem.rectTransform);

        Assert.That(card.ShowsPortrait, Is.True);
        Assert.That(card.Emblem.sprite == looks.For(Ranger).Portrait, Is.True);
        Assert.That(card.Emblem.color, Is.EqualTo(Color.white));
        Assert.That(card.Emblem.preserveAspect, Is.True);
        Assert.That(card.Emblem.rectTransform.pivot, Is.EqualTo(new Vector2(0.5f, 0f)));
        Assert.That(portrait.yMin, Is.EqualTo(InSafe(card.NameLabel.rectTransform).yMax).Within(0.01f), "the portrait does not stand on the name.");
        Assert.That(portrait.xMin, Is.EqualTo(opening.xMin).Within(0.01f));
        Assert.That(portrait.xMax, Is.EqualTo(opening.xMax).Within(0.01f));
        Assert.That(opening.yMax - portrait.yMax, Is.EqualTo(HeroGrid.PortraitTopDp).Within(0.01f));
    }

    [Test]
    public void Card_NoPortrait_FallsBackToTheEmblem()
    {
        CharacterLookBook looks = Looks(false);

        Enable(looks);
        OpenHeroes();
        Grid().Layout(1f);

        HeroGrid.Card card = Grid().Cards[0];
        Rect opening = InSafe((RectTransform)card.Button.transform);
        Rect emblem = InSafe(card.Emblem.rectTransform);

        Assert.That(card.ShowsPortrait, Is.False);
        Assert.That(card.Emblem.sprite == looks.For(Ranger).Emblem, Is.True);
        Assert.That(card.Emblem.enabled, Is.True);
        Assert.That(emblem.xMin - opening.xMin, Is.EqualTo(4f).Within(0.01f), "the emblem lost its inset.");
        Assert.That(opening.yMax - emblem.yMax, Is.EqualTo(4f).Within(0.01f));

        // No look book at all: no emblem, no portrait, no throw.
        Enable();

        Assert.That(() => OpenHeroes(), Throws.Nothing);
        Assert.That(Grid().Cards[0].ShowsPortrait, Is.False);
        Assert.That(Grid().Cards[0].Emblem.enabled, Is.False);
    }

    [Test]
    public void LockedCard_PortraitGreyed_PadlockOver()
    {
        Enable(Looks(true));
        OpenHeroes();
        Grid().Layout(1f);

        HeroGrid.Card locked = Grid().Cards[3];

        Assert.That(locked.ShowsPortrait, Is.True);
        Assert.That(locked.Emblem.color.r, Is.EqualTo(Palette.NodeLocked.r).Within(0.001f));
        Assert.That(locked.Emblem.color.a, Is.EqualTo(HeroGrid.LockedAlpha).Within(0.001f));
        Assert.That(locked.Lock.gameObject.activeSelf, Is.True);
        Assert.That(locked.Lock.transform.GetSiblingIndex(), Is.GreaterThan(locked.Emblem.transform.GetSiblingIndex()));
    }

    [Test]
    public void Card_Unpicked_RimOff()
    {
        _profiles.Adopt(PlayerProfile.Default.WithSelection(Ranger, new ContentId("mode.jungle")));
        Enable();
        OpenHeroes();

        for (int i = 1; i < Grid().Cards.Count; i++)
        {
            HeroGrid.Card other = Grid().Cards[i];

            Assert.That(other.Rim.gameObject.activeSelf, Is.False, $"{other.Id} wears the rim.");
            Assert.That(other.PickedLabel.gameObject.activeSelf, Is.False);
            Assert.That(other.Frame.gameObject.activeSelf, Is.True);
        }
    }

    [Test]
    public void Card_Plate_AndContents_ClearTheFrame()
    {
        Enable(Looks(true));
        OpenHeroes();
        Grid().Layout(1f);

        float edge = HeroGrid.FrameBorderDp - HeroGrid.GlowPadDp;

        foreach (HeroGrid.Card card in Grid().Cards)
        {
            Rect outer = InSafe(card.Root);
            Rect plate = InSafe((RectTransform)card.Button.transform);

            Assert.That(plate.xMin - outer.xMin, Is.EqualTo(edge).Within(0.01f));
            Assert.That(outer.yMax - plate.yMax, Is.EqualTo(edge).Within(0.01f));
            Assert.That(card.Button.targetGraphic.color, Is.EqualTo(Color.Lerp(Palette.Ink, Color.white, 0.14f)), "the plate lost its flat colour.");

            foreach (Graphic part in new Graphic[] { card.Emblem, card.Lock, card.NameLabel, card.PickedLabel, card.Price, card.Deed })
            {
                Rect rect = InSafe(part.rectTransform);

                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(plate.xMin - 0.01f), $"{part.name} runs under the left stone.");
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(plate.xMax + 0.01f), $"{part.name} runs under the right stone.");
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(plate.yMin - 0.01f), $"{part.name} runs under the bottom stone.");
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(plate.yMax + 0.01f), $"{part.name} runs under the top stone.");
            }
        }
    }

    [Test]
    public void LockedCard_Unchanged_InsideTheFrame()
    {
        Enable();
        OpenHeroes();
        Grid().Layout(1f);

        HeroGrid.Card locked = Grid().Cards[3];

        Assert.That(locked.Frame.enabled, Is.True);
        Assert.That(locked.Frame.sprite, Is.SameAs(Art(FrameArt)));
        Assert.That(locked.Rim.gameObject.activeSelf, Is.False);
        Assert.That(locked.Lock.gameObject.activeSelf, Is.True);
        Assert.That(locked.Emblem.color.a, Is.EqualTo(HeroGrid.LockedAlpha).Within(0.001f));
        Assert.That(locked.Price.gameObject.activeSelf, Is.True);
        Assert.That(locked.Deed.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void Card_Locked_GreyWithPrice()
    {
        Enable();
        OpenHeroes();

        HeroGrid.Card locked = Grid().Cards[3];
        CharacterSpec spec = _catalog.Character(Emberwright);

        Assert.That(locked.Locked, Is.True);
        Assert.That(locked.Lock.gameObject.activeSelf, Is.True, "no lock on a locked card.");
        Assert.That(locked.Emblem.color.a, Is.EqualTo(HeroGrid.LockedAlpha).Within(0.001f));
        Assert.That(locked.Emblem.color.r, Is.EqualTo(Palette.NodeLocked.r).Within(0.001f));
        Assert.That(locked.Price.text, Is.EqualTo(spec.Unlock.ShardPrice.ToString()));
        Assert.That(locked.Rim.gameObject.activeSelf, Is.False);

        Assert.That(locked.Deed.text, Is.EqualTo("or reach stage 20"), "the existing deed line.");
        Assert.That(Grid().Cards[2].Deed.gameObject.activeSelf, Is.False, "a boss deed draws no line.");
        Assert.That(Grid().Cards[0].Lock.gameObject.activeSelf, Is.False, "an owned card is badged locked.");
    }

    [Test]
    public void Card_Tap_OpensPageOnThatClass()
    {
        Enable();
        OpenHeroes();

        Grid().Cards[1].Button.onClick.Invoke();

        var page = Field<ClassSelectPresenter>("_classSelect");

        Assert.That(page.IsOpen, Is.True);
        Assert.That(page.Index, Is.EqualTo(1), "the page opened on another class.");

        page.Close();
        Grid().Cards[3].Button.onClick.Invoke();

        Assert.That(page.IsOpen, Is.True, "a locked card is not tappable.");
        Assert.That(page.Index, Is.EqualTo(3));
    }

    [Test]
    public void Page_Choose_PicksAndReturnsHome_NoRun()
    {
        Enable();
        OpenHeroes();

        Grid().Cards[1].Button.onClick.Invoke();

        var page = Field<ClassSelectPresenter>("_classSelect");
        var character = (CharacterPage)typeof(ClassSelectPresenter).GetField("_page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(page);

        character.Primary.onClick.Invoke();

        Assert.That(_profiles.Current.SelectedCharacterId, Is.EqualTo(Oathbound));
        Assert.That(_profiles.Current.SelectedModeId.Value, Is.EqualTo("mode.jungle"));
        Assert.That(page.IsOpen, Is.False, "the page stayed up.");
        Assert.That(Grid().gameObject.activeSelf, Is.False, "home is not showing.");
        Assert.That(_menu.SelectedCharacter, Is.EqualTo(Oathbound), "home does not show the new class.");
        Assert.That(Field<TabBar>("_tabs").Current, Is.EqualTo(Tab.Play));
        Assert.That(_pending.IsSet, Is.False, "Choose started a run.");
        Assert.That(_loader.Asked, Is.Empty);
    }

    [Test]
    public void Grid_FitsPortrait([Values(0, 1)] int size)
    {
        Enable(Looks(true));

        Vector2 area = size == 0 ? new Vector2(411f, 914f) : new Vector2(360f, 780f);
        Layout(area);
        OpenHeroes();
        Grid().Layout(1f);

        Rect safe = new Rect(Vector2.zero, area);
        var rows = new List<Rect>();

        foreach (HeroGrid.Card card in Grid().Cards)
        {
            Rect rect = InSafe(card.Root);
            rows.Add(rect);

            Assert.That(rect.width, Is.GreaterThanOrEqualTo(HeroGrid.MinCardWidthDp), "a card is under 120 dp wide.");
            Assert.That(rect.xMin >= -0.01f && rect.xMax <= safe.xMax + 0.01f, Is.True, $"card {rect} leaves {safe} sideways.");

            Rect opening = InSafe((RectTransform)card.Button.transform);
            Rect portrait = InSafe(card.Emblem.rectTransform);

            Assert.That(card.ShowsPortrait, Is.True);
            Assert.That(portrait.xMin, Is.GreaterThanOrEqualTo(opening.xMin - 0.01f), "the portrait runs under the left stone.");
            Assert.That(portrait.xMax, Is.LessThanOrEqualTo(opening.xMax + 0.01f), "the portrait runs under the right stone.");
            Assert.That(portrait.yMin, Is.GreaterThanOrEqualTo(opening.yMin - 0.01f), "the portrait runs under the bottom stone.");
            Assert.That(portrait.yMax, Is.LessThanOrEqualTo(opening.yMax + 0.01f), "the portrait runs under the top stone.");
        }

        for (int a = 0; a < rows.Count; a++)
        {
            for (int b = a + 1; b < rows.Count; b++)
            {
                Assert.That(rows[a].Overlaps(rows[b]), Is.False, $"card {a} overlaps card {b}");
            }
        }

        // The scrolling area ends above the tab bar, so no card is ever under it.
        var viewport = (RectTransform)Grid().Content.parent;

        Assert.That(InSafe(viewport).yMin, Is.GreaterThanOrEqualTo(TabBar.HeightDp - 0.01f));
        Assert.That(InSafe(viewport).yMax, Is.LessThanOrEqualTo(safe.yMax + 0.01f));

        foreach (TMP_Text label in Grid().GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(12f), $"{label.name} is under 12 sp.");
        }
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A look per class: a square portrait and an emblem, or the emblem alone.</summary>
    private static CharacterLookBook Looks(bool portrait)
    {
        var texture = new Texture2D(8, 8);
        var picture = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0f));
        var emblem = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f));
        var looks = new Dictionary<ContentId, CharacterLook>();

        foreach (ContentId id in new[] { Ranger, Oathbound, Gravecaller, Emberwright })
        {
            looks.Add(id, new CharacterLook(null, emblem: emblem, portrait: portrait ? picture : null));
        }

        return new CharacterLookBook(looks);
    }

    private HeroGrid Grid() => Field<HeroGrid>("_heroes");

    private static Sprite Art(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    private void Enable(CharacterLookBook looks = null)
    {
        _menu.Construct(_pending, new SavedRun(), _loader, _words, _catalog, _profiles, _roster, looks: looks);

        typeof(MenuPresenter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_menu, null);

        // The class page is put down by its own Start, which never runs in EditMode.
        Field<ClassSelectPresenter>("_classSelect").Construct(_pending, _catalog, _loader, _words, _profiles, _roster);
    }

    /// <summary>The Heroes tab, tapped.</summary>
    private void OpenHeroes() => Field<Button>("_classCard").onClick.Invoke();

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

    private ContentCatalog Catalog()
    {
        var specs = new List<CharacterSpec>();

        foreach (string path in CharacterPaths)
        {
            var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");
            specs.Add(definition.ToSpec());
        }

        var enemies = new List<EnemySpec>();

        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
        {
            enemies.Add(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec());
        }

        return new ContentCatalog(specs, enemies, new[] { AssetDatabase.LoadAssetAtPath<ModeDefinition>(JunglePath).ToSpec() });
    }

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
