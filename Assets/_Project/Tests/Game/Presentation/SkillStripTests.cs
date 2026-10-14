using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-05d's skill strip: which four tiles a class gets, what a tap does, what the caption says and
/// whether it fits a 9:20 phone. Over the shipped <c>ClassSelect.prefab</c> and the shipped Ranger.
/// </summary>
[TestFixture]
public sealed class SkillStripTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string RollPath = "Assets/_Project/Art/UI/Icons/Icon_Roll.png";
    private const string RangerId = "character.ranger";

    private readonly List<Object> _spawned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object spawned in _spawned)
        {
            if (spawned != null)
            {
                Object.DestroyImmediate(spawned);
            }
        }

        _spawned.Clear();
    }

    // ---- Rule 1: which four ----------------------------------------------------------------------

    [Test]
    public void Strip_Ranger_IsThreeNodesAndTheRoll()
    {
        var entries = new List<StripEntry>();
        CharacterDefinition ranger = Ranger();

        SkillStrip.Build(ranger.ToSpec(), ShippedCatalog(), null, ranger.ToLook(), entries);

        Assert.That(
            entries.Select(e => e.NameKey.Key),
            Is.EqualTo(new[]
            {
                "skill.ranger.quick-draw.name",
                "skill.ranger.volley.name",
                "skill.ranger.fleet-foot.name",
                "character.ranger.space.name",
            }));
        Assert.That(entries[3].DescriptionKey.Key, Is.EqualTo("character.ranger.space.description"));
        Assert.That(entries[3].Icon, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(RollPath)), "the Roll's icon.");
    }

    [Test]
    public void Strip_NoTree_OnlyTheSpaceMove()
    {
        var entries = new List<StripEntry>();
        CharacterDefinition ranger = Ranger();

        SkillStrip.Build(ranger.ToSpec(), new ContentCatalog(new[] { ranger.ToSpec() }), null, ranger.ToLook(), entries);

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].NameKey.Key, Is.EqualTo("character.ranger.space.name"));
    }

    [Test]
    public void Strip_NoSpaceMove_HasNoFourthTile()
    {
        var entries = new List<StripEntry>();
        CharacterDefinition ranger = Object.Instantiate(Ranger());
        _spawned.Add(ranger);

        var so = new SerializedObject(ranger);
        so.FindProperty("_movementSkillKind").intValue = (int)MovementSkillKind.None;
        so.ApplyModifiedPropertiesWithoutUndo();

        SkillStrip.Build(ranger.ToSpec(), ShippedCatalog(), null, ranger.ToLook(), entries);

        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.Any(e => e.NameKey.Key == "character.ranger.space.name"), Is.False);
    }

    // ---- Rules 2, 3: tiles, selection, caption -----------------------------------------------------

    [Test]
    public void Select_FirstOnOpen_TapMoves()
    {
        SkillStrip strip = NewStrip();

        strip.Show(Entries(4), Words());

        Assert.That(strip.Selected, Is.EqualTo(0), "the first is selected on open.");
        Assert.That(strip.TilesShown, Is.EqualTo(4));
        Assert.That(FrameSprite(strip, 0), Is.SameAs(Field<Sprite>(strip, "_frameSelected")));
        Assert.That(FrameSprite(strip, 1), Is.SameAs(Field<Sprite>(strip, "_frame")));

        Tile(strip, 2).onClick.Invoke();

        Assert.That(strip.Selected, Is.EqualTo(2));
        Assert.That(FrameSprite(strip, 2), Is.SameAs(Field<Sprite>(strip, "_frameSelected")));
        Assert.That(FrameSprite(strip, 0), Is.SameAs(Field<Sprite>(strip, "_frame")), "the ring moved.");
    }

    [Test]
    public void Caption_NameAndDescription()
    {
        SkillStrip strip = NewStrip();

        strip.Show(Entries(4), Words());

        Assert.That(strip.Caption.text, Does.Contain("Name0").And.Contain("Words about 0."));

        Tile(strip, 3).onClick.Invoke();

        Assert.That(strip.Caption.text, Does.Contain("Name3").And.Contain("Words about 3.").And.Not.Contain("Name0"));
    }

    // ---- Rule 4: three lines at 14 sp --------------------------------------------------------------

    [Test]
    public void Caption_ClearsTheFloor_AtMostThreeLines()
    {
        const float Px = 2f;
        SkillStrip strip = NewStrip();

        strip.Show(Entries(4), Words());
        strip.Layout(Px);

        Assert.That(strip.Caption.fontSize, Is.EqualTo(14f * Px).Within(0.01f));
        Assert.That(strip.Caption.fontSize, Is.GreaterThanOrEqualTo(12f * Px));
        Assert.That(strip.Caption.maxVisibleLines, Is.EqualTo(3));
        Assert.That(strip.Caption.enableAutoSizing, Is.False);
    }

    // ---- Rule 5: icons ------------------------------------------------------------------------------

    [Test]
    public void Icons_FromTheBookOrThePlaceholder()
    {
        Sprite roll = AssetDatabase.LoadAssetAtPath<Sprite>(RollPath);
        var quickDraw = new ContentId("skill.ranger.quick-draw");
        var book = new SkillIconBook(new Dictionary<ContentId, Sprite> { [quickDraw] = roll });
        var entries = new List<StripEntry>();
        CharacterDefinition ranger = Ranger();

        SkillStrip.Build(ranger.ToSpec(), ShippedCatalog(), book, default, entries);

        Assert.That(entries[0].Icon, Is.SameAs(roll), "a node with an icon asks the book.");
        Assert.That(entries[1].Icon == null, Is.True, "a node with none is null, for the strip to cover.");

        SkillStrip strip = NewStrip();
        strip.Show(entries, Words());

        Assert.That(Icon(strip, 0).sprite, Is.SameAs(roll));
        Assert.That(Icon(strip, 1).sprite, Is.SameAs(Field<Sprite>(strip, "_placeholder")));
        Assert.That(Icon(strip, 1).sprite == null, Is.False, "the prefab carries a placeholder.");
    }

    // ---- Rule 6: a new class starts again ------------------------------------------------------------

    [Test]
    public void ClassChange_ResetsTheSelection()
    {
        SkillStrip strip = NewStrip();

        strip.Show(Entries(4), Words());
        Tile(strip, 3).onClick.Invoke();

        strip.Show(Entries(2), Words());

        Assert.That(strip.Selected, Is.EqualTo(0));
        Assert.That(strip.TilesShown, Is.EqualTo(2));
        Assert.That(Tile(strip, 3).gameObject.activeSelf, Is.False, "a tile past the entries is switched off.");
        Assert.That(strip.Caption.text, Does.Contain("Name0"));
    }

    // ---- Rule 7: it fits 9:20 -------------------------------------------------------------------------

    [Test]
    public void Strip_FitsPortrait()
    {
        const float Px = 2f;

        var canvas = new GameObject("Canvas", typeof(RectTransform));
        _spawned.Add(canvas);

        var canvasRect = (RectTransform)canvas.transform;
        canvasRect.sizeDelta = new Vector2(966f, 2146f);

        GameObject screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), canvas.transform);
        _spawned.Add(screen);

        var safe = (RectTransform)screen.transform.Find("Safe");
        safe.anchorMin = Vector2.zero;
        safe.anchorMax = Vector2.one;
        safe.offsetMin = safe.offsetMax = Vector2.zero;

        CharacterPage page = screen.GetComponentInChildren<CharacterPage>(true);
        ((RectTransform)page.transform).offsetMax = new Vector2(0f, -72f * Px);

        CharacterDefinition ranger = Ranger();
        var entries = new List<StripEntry>();
        SkillStrip.Build(ranger.ToSpec(), ShippedCatalog(), null, ranger.ToLook(), entries);

        page.Layout(Px);
        page.Show(new ClassPageModel(ranger.ToSpec(), 0, 1, PageButton.Choose, 0, null, RoleKind.Ranged, entries), Words());
        page.Layout(Px);

        SkillStrip strip = page.GetComponentInChildren<SkillStrip>(true);
        Rect screenRect = WorldRect(canvasRect);
        Rect stripRect = WorldRect((RectTransform)strip.transform);

        Assert.That(strip.TilesShown, Is.EqualTo(4));
        Assert.That(Contains(screenRect, stripRect), Is.True, $"the strip at {stripRect} leaves the screen {screenRect}.");

        var tiles = new List<Rect>();

        for (int i = 0; i < 4; i++)
        {
            Rect tile = WorldRect((RectTransform)Tile(strip, i).transform);
            tiles.Add(tile);

            Assert.That(tile.height, Is.GreaterThanOrEqualTo(48f * Px), $"tile {i} is under 48 dp.");
            Assert.That(tile.width, Is.GreaterThanOrEqualTo(48f * Px), $"tile {i} is under 48 dp.");
            Assert.That(Contains(stripRect, tile), Is.True, $"tile {i} at {tile} leaves the strip {stripRect}.");

            for (int j = 0; j < i; j++)
            {
                Assert.That(Overlaps(tile, tiles[j]), Is.False, $"tiles {i} and {j} overlap.");
            }
        }

        Rect captionRect = WorldRect(strip.Caption.rectTransform);

        Assert.That(Contains(stripRect, captionRect), Is.True, "the caption leaves the strip.");

        foreach (Rect tile in tiles)
        {
            Assert.That(Overlaps(tile, captionRect), Is.False, "a tile overlaps the caption.");
        }

        var others = new Dictionary<string, Rect>
        {
            ["name band"] = WorldRect(PageRect(page, "_nameBand")),
            ["preview"] = WorldRect((RectTransform)page.Preview.transform),
            ["dots"] = WorldRect(PageRect(page, "_dots")),
            ["deed"] = WorldRect(PageRect(page, "_deed")),
            ["button"] = WorldRect((RectTransform)page.Primary.transform),
        };

        foreach (KeyValuePair<string, Rect> other in others)
        {
            Assert.That(Overlaps(stripRect, other.Value), Is.False, $"the strip overlaps the {other.Key} at {other.Value}.");
        }

        foreach (TMP_Text label in strip.GetComponentsInChildren<TMP_Text>(false))
        {
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(12f * Px), $"{label.name} is under 12 sp.");
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private SkillStrip NewStrip()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject root = Object.Instantiate(prefab);
        _spawned.Add(root);

        SkillStrip strip = root.GetComponentInChildren<SkillStrip>(true);

        Assert.That(strip, Is.Not.Null, "ClassSelect.prefab has no SkillStrip.");

        return strip;
    }

    private static List<StripEntry> Entries(int count)
    {
        var entries = new List<StripEntry>();

        for (int i = 0; i < count; i++)
        {
            entries.Add(new StripEntry(null, new LocKey("test.name" + i), new LocKey("test.desc" + i)));
        }

        return entries;
    }

    private static DictionaryLocalizer Words()
    {
        var pairs = new List<string>();

        for (int i = 0; i < 4; i++)
        {
            pairs.Add("test.name" + i);
            pairs.Add("Name" + i);
            pairs.Add("test.desc" + i);
            pairs.Add("Words about " + i + ".");
        }

        pairs.Add("character.ranger.space.name");
        pairs.Add("Roll");
        pairs.Add("character.ranger.space.description");
        pairs.Add("A quick roll that slips past attacks.");

        return new DictionaryLocalizer(pairs.ToArray());
    }

    private static CharacterDefinition Ranger() => AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);

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

    private static T Field<T>(SkillStrip strip, string name) where T : Object =>
        (T)new SerializedObject(strip).FindProperty(name).objectReferenceValue;

    private static Button Tile(SkillStrip strip, int index) =>
        (Button)new SerializedObject(strip).FindProperty("_tiles").GetArrayElementAtIndex(index).objectReferenceValue;

    private static Sprite FrameSprite(SkillStrip strip, int index) =>
        ((Image)new SerializedObject(strip).FindProperty("_frames").GetArrayElementAtIndex(index).objectReferenceValue).sprite;

    private static Image Icon(SkillStrip strip, int index) =>
        (Image)new SerializedObject(strip).FindProperty("_icons").GetArrayElementAtIndex(index).objectReferenceValue;

    private static RectTransform PageRect(CharacterPage page, string field)
    {
        object value = typeof(CharacterPage)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(page);

        return value is TMP_Text text ? text.rectTransform : (RectTransform)value;
    }

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.xMin >= outer.xMin - 0.5f && inner.xMax <= outer.xMax + 0.5f
        && inner.yMin >= outer.yMin - 0.5f && inner.yMax <= outer.yMax + 0.5f;

    private static bool Overlaps(Rect a, Rect b) =>
        a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;
}
