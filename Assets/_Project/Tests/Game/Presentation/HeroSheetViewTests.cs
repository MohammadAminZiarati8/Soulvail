using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using RowKind = Soulvail.Game.Presentation.HeroSheetView.RowKind;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-12d: the hero sheet as the page draws it — which rows, in what order, how a stat and an entry
/// read, how the scroll behaves and that the rows are kept. Over the shipped <c>ClassSelect.prefab</c>,
/// the shipped Ranger and the shipped English table.
/// </summary>
[TestFixture]
public sealed class HeroSheetViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string RollPath = "Assets/_Project/Art/UI/Icons/Icon_Roll.png";
    private const string PlaceholderPath = "Assets/_Project/Art/UI/Icons/Icon_Placeholder.png";

    /// <summary><c>ClassSelectPresenter.HeaderDp</c>.</summary>
    private const float HeaderDp = 72f;

    private const float Slack = 0.5f;

    private const string LongWords =
        "Every third arrow splits in flight into three, each striking for half, and the split arrows " +
        "pierce the first enemy they meet before falling to the ground beside it.";

    /// <summary>UI-12d rule 12's phones: dp size and canvas units per dp (420 and 320 dpi).</summary>
    private static readonly (Vector2 Dp, float Px)[] Phones =
    {
        (new Vector2(411f, 914f), 420f / 160f),
        (new Vector2(360f, 780f), 2f),
    };

    private GameObject _screen;
    private CharacterPage _page;
    private HeroSheetView _sheet;
    private float _px;

    [SetUp]
    public void SetUp()
    {
        _screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _page = _screen.GetComponentInChildren<CharacterPage>(true);
        _sheet = _screen.GetComponentInChildren<HeroSheetView>(true);

        Assert.That(_sheet, Is.Not.Null, "ClassSelect.prefab has no HeroSheetView (Traps §5).");
        Assert.That(_page.Sheet, Is.SameAs(_sheet), "the page is not wired to the sheet.");

        SizeTo(1);
    }

    [TearDown]
    public void TearDown()
    {
        if (_screen != null)
        {
            Object.DestroyImmediate(_screen);
        }
    }

    // ---- Rule 3: the rows, in order --------------------------------------------------------------

    [Test]
    public void Sheet_Ranger_RowsInOrder()
    {
        _sheet.Show(RangerSheet(), English());

        var expected = new List<RowKind>
        {
            RowKind.Description,
            RowKind.Heading, RowKind.Stat, RowKind.Stat, RowKind.Stat, RowKind.Stat, RowKind.Stat,
            RowKind.Heading, RowKind.Entry,
            RowKind.Heading, RowKind.Entry,
            RowKind.Heading,
        };

        for (int b = 0; b < 3; b++)
        {
            expected.AddRange(new[] { RowKind.BranchName, RowKind.Entry, RowKind.Entry, RowKind.Entry });
        }

        Assert.That(_sheet.Rows.Select(r => r.Kind), Is.EqualTo(expected));
        Assert.That(
            _sheet.Rows.Where(r => r.Kind == RowKind.Heading).Select(r => r.Title.text),
            Is.EqualTo(new[] { "Stats", "Basic attack", "Special move", "Skill tree" }));
        Assert.That(_sheet.Rows[8].Title.text, Is.EqualTo("Longbow"));
        Assert.That(_sheet.Rows[10].Title.text, Is.EqualTo("Roll"));
        Assert.That(_sheet.Rows[0].Body.text, Is.Not.Empty.And.Not.StartWith("character."), "the description is a key, not words.");

        // Top to bottom, as listed.
        for (int i = 1; i < _sheet.Rows.Count; i++)
        {
            Assert.That(_sheet.Rows[i].Root.anchoredPosition.y, Is.LessThan(_sheet.Rows[i - 1].Root.anchoredPosition.y), $"row {i} is not under row {i - 1}.");
        }
    }

    [Test]
    public void Sheet_NoAttack_OmitsItsHeading()
    {
        CharacterDefinition ranger = Ranger();
        CharacterLook look = ranger.ToLook();
        var keyless = new CharacterLook(
            look.Body,
            emblem: look.Emblem,
            spaceIcon: look.SpaceIcon,
            spaceNameKey: look.SpaceNameKey,
            spaceDescriptionKey: look.SpaceDescriptionKey);

        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), keyless, null);

        Assert.That(sheet.HasAttack, Is.False, "the fixture's premise.");

        _sheet.Show(sheet, English());

        Assert.That(_sheet.Rows.Where(r => r.Kind == RowKind.Heading).Select(r => r.Title.text), Is.EqualTo(new[] { "Stats", "Special move", "Skill tree" }));
        Assert.That(_sheet.Rows.Count(r => r.Kind == RowKind.Entry), Is.EqualTo(1 + 9), "the Roll and nine ranks.");
        Assert.That(_sheet.Rows.Any(r => r.Title != null && r.Title.text == "Longbow"), Is.False);
    }

    // ---- Rule 4: a stat row ----------------------------------------------------------------------

    [Test]
    public void Sheet_Stat_LabelLeft_NumberRight_Formatted()
    {
        _sheet.Show(RangerSheet(), English());

        HeroSheetView.Row[] stats = _sheet.Rows.Where(r => r.Kind == RowKind.Stat).ToArray();

        Assert.That(stats.Select(r => r.Title.text), Is.EqualTo(new[] { "Health", "Damage", "Attacks/s", "Range", "Speed" }));
        Assert.That(stats.Select(r => r.Value.text), Is.EqualTo(new[] { "90", "13", "1.1", "13 m", "3.2 m/s" }));

        foreach (HeroSheetView.Row stat in stats)
        {
            Assert.That(stat.Title.alignment, Is.EqualTo(TextAlignmentOptions.Left));
            Assert.That(stat.Value.alignment, Is.EqualTo(TextAlignmentOptions.Right));
            Assert.That(stat.Title.fontSize, Is.EqualTo(HeroSheetView.BodyDp * _px).Within(0.01f));
            Assert.That(stat.Value.fontSize, Is.EqualTo(HeroSheetView.TitleDp * _px).Within(0.01f));
            Assert.That(stat.Title.color, Is.EqualTo(Palette.HudCaption));
            Assert.That(stat.Value.color, Is.EqualTo(Palette.HudText));
            Assert.That(stat.Icon, Is.Null);
            Assert.That(stat.Body, Is.Null);
            Assert.That(stat.Root.rect.height, Is.EqualTo(HeroSheetView.StatRowDp * _px).Within(0.01f));
        }
    }

    // ---- Rule 5: an entry row --------------------------------------------------------------------

    [Test]
    public void Sheet_Entry_IconNamesRankAndWraps()
    {
        var roll = AssetDatabase.LoadAssetAtPath<Sprite>(RollPath);
        var book = new SkillIconBook(new Dictionary<ContentId, Sprite> { [new ContentId("skill.a2")] = roll });

        _sheet.Show(HeroSheet.Build(Ranger().ToSpec(), TestCatalog(), default, book), Words());

        HeroSheetView.Row entry = _sheet.Rows.Single(r => r.Kind == RowKind.Entry && r.Title.text == "Name a2");

        Assert.That(entry.Icon.sprite, Is.SameAs(roll));
        Assert.That(entry.Value.text, Is.EqualTo("Rank 2"));
        Assert.That(entry.Body.text, Is.EqualTo(LongWords));
        Assert.That(entry.Title.fontSize, Is.EqualTo(HeroSheetView.TitleDp * _px).Within(0.01f));
        Assert.That(entry.Value.fontSize, Is.EqualTo(HeroSheetView.BodyDp * _px).Within(0.01f));
        Assert.That(entry.Body.fontSize, Is.EqualTo(HeroSheetView.BodyDp * _px).Within(0.01f));
        Assert.That(entry.Value.color, Is.EqualTo(Palette.HudCaption));

        // The rank sits on the name's line, right of it; the description under both, beside the icon.
        Rect icon = Local(entry.Icon.transform.parent as RectTransform, entry.Root);
        Rect name = Local(entry.Title.rectTransform, entry.Root);
        Rect rank = Local(entry.Value.rectTransform, entry.Root);
        Rect body = Local(entry.Body.rectTransform, entry.Root);

        Assert.That(icon.width, Is.EqualTo(HeroSheetView.IconDp * _px).Within(0.01f));
        Assert.That(name.xMin, Is.GreaterThanOrEqualTo(icon.xMax), "the name is not beside the icon.");
        Assert.That(rank.xMin, Is.GreaterThanOrEqualTo(name.xMax - Slack), "the rank overlaps the name.");
        Assert.That(rank.xMax, Is.EqualTo(entry.Root.rect.xMax).Within(Slack), "the rank is not right-aligned.");
        Assert.That(rank.yMax, Is.EqualTo(name.yMax).Within(Slack), "the rank is not on the name's line.");
        Assert.That(body.yMax, Is.LessThanOrEqualTo(name.yMin + Slack), "the description is not under the name.");

        float preferred = entry.Body.GetPreferredValues(entry.Body.text, body.width, float.PositiveInfinity).y;
        float oneLine = entry.Body.GetPreferredValues("M", float.PositiveInfinity, float.PositiveInfinity).y;

        Assert.That(preferred, Is.GreaterThan(2f * oneLine), "the fixture's premise: the description wraps.");
        Assert.That(body.height, Is.EqualTo(preferred).Within(Slack), "the description is not as tall as its text.");
        Assert.That(entry.Root.rect.height, Is.GreaterThanOrEqualTo(HeroSheetView.IconDp * _px));
        Assert.That(entry.Root.rect.height, Is.GreaterThanOrEqualTo(preferred));

        HeroSheetView.Row shortRank = _sheet.Rows.Single(r => r.Kind == RowKind.Entry && r.Title.text == "Name b1");

        Assert.That(shortRank.Root.rect.height, Is.EqualTo(HeroSheetView.IconDp * _px).Within(Slack), "a short row is not its icon's height.");
    }

    [Test]
    public void Sheet_Entry_NullIcon_DrawsThePlaceholder()
    {
        var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);

        _sheet.Show(HeroSheet.Build(Ranger().ToSpec(), TestCatalog(), default, null), Words());

        HeroSheetView.Row[] entries = _sheet.Rows.Where(r => r.Kind == RowKind.Entry).ToArray();

        Assert.That(entries, Is.Not.Empty);

        foreach (HeroSheetView.Row entry in entries)
        {
            Assert.That(entry.Icon.sprite, Is.SameAs(placeholder));
            Assert.That(entry.Icon.transform.parent.GetComponent<Image>().sprite.name, Is.EqualTo("CharacterPage_SkillFrame"));
        }

        Assert.That(entries.Where(e => e.Title.text == "Name a1").Single().Value.text, Is.EqualTo("Rank 1"));
    }

    // ---- Rule 6: 12 sp, nothing wider ------------------------------------------------------------

    [Test]
    public void Sheet_TextAtLeastTwelveSp_NothingWider([Values(0, 1)] int phone)
    {
        SizeTo(phone);

        _sheet.Show(RangerSheet(), English());

        Rect viewport = World(_sheet.Scroll.viewport);
        int texts = 0;

        foreach (HeroSheetView.Row row in _sheet.Rows)
        {
            foreach (TMP_Text text in row.Root.GetComponentsInChildren<TMP_Text>(false))
            {
                Rect rect = World(text.rectTransform);
                texts++;

                Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(12f * _px), $"{text.text} is under 12 sp.");
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(viewport.xMin + (HeroSheetView.SideDp * _px) - Slack), $"{text.text} leaves the viewport's left margin.");
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(viewport.xMax - (HeroSheetView.SideDp * _px) + Slack), $"{text.text} leaves the viewport's right margin.");

                float wanted = text.GetPreferredValues(text.text, rect.width, float.PositiveInfinity).y;

                Assert.That(rect.height, Is.GreaterThanOrEqualTo(wanted - Slack), $"{text.text} is clipped: {rect.height} of {wanted}.");
            }
        }

        Assert.That(texts, Is.GreaterThan(40), "the fixture drew next to nothing.");
    }

    // ---- Rule 7: the scroll ----------------------------------------------------------------------

    [Test]
    public void Sheet_Show_ResetsToTheTop()
    {
        _sheet.Show(RangerSheet(), English());

        RectTransform content = _sheet.Scroll.content;
        content.anchoredPosition = new Vector2(0f, 400f);

        _sheet.Show(RangerSheet(), English());

        Assert.That(content.anchoredPosition.y, Is.EqualTo(0f).Within(0.01f), "the sheet kept the last class's scroll.");
        Assert.That(_sheet.Scroll.velocity, Is.EqualTo(Vector2.zero), "the last class's fling carried over.");
        Assert.That(_sheet.ContentHeight, Is.GreaterThan(0f));
        Assert.That(content.rect.height, Is.EqualTo(_sheet.ContentHeight).Within(0.01f));
    }

    [Test]
    public void Sheet_Scroll_VerticalClamped()
    {
        _sheet.Show(RangerSheet(), English());

        ScrollRect scroll = _sheet.Scroll;

        Assert.That(scroll.horizontal, Is.False);
        Assert.That(scroll.vertical, Is.True);
        Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
        Assert.That(scroll.inertia, Is.True);
        Assert.That(scroll.viewport, Is.Not.Null);
        Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null, "the viewport does not clip.");
        Assert.That(scroll.viewport.GetComponent<Graphic>().raycastTarget, Is.True, "a drag between rows lands on nothing.");
        Assert.That(scroll.content.IsChildOf(scroll.viewport), Is.True);
    }

    // ---- Rule 9: rows are kept -------------------------------------------------------------------

    [Test]
    public void Sheet_SecondShow_ReusesRows()
    {
        _sheet.Show(RangerSheet(), English());

        HeroSheetView.Row[] first = _sheet.Rows.ToArray();
        int children = _sheet.Scroll.content.childCount;

        // No tree: description, stats, attack and Roll — fewer rows than the first.
        CharacterDefinition ranger = Ranger();
        _sheet.Show(HeroSheet.Build(ranger.ToSpec(), new ContentCatalog(new[] { ranger.ToSpec() }), ranger.ToLook(), null), English());

        int count = _sheet.Rows.Count;

        Assert.That(count, Is.LessThan(first.Length), "the fixture's premise.");
        Assert.That(_sheet.Scroll.content.childCount, Is.EqualTo(children), "a second Show built rows.");

        for (int i = 0; i < count; i++)
        {
            Assert.That(_sheet.Rows[i], Is.SameAs(first[i]), $"row {i} was rebuilt.");
        }

        for (int i = count; i < first.Length; i++)
        {
            Assert.That(first[i].Root.gameObject.activeSelf, Is.False, $"row {i} past the sheet is on screen.");
        }

        _sheet.Show(RangerSheet(), English());

        Assert.That(_sheet.Rows, Is.EqualTo(first), "the same rows came back.");
        Assert.That(_sheet.Scroll.content.childCount, Is.EqualTo(children));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Sizes the screen to a phone, the page under the header, and lays it out.</summary>
    private void SizeTo(int phone)
    {
        (Vector2 dp, float px) = Phones[phone];
        _px = px;

        var root = (RectTransform)_screen.transform;
        root.localScale = Vector3.one;
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = dp * px;

        var safe = (RectTransform)root.Find("Safe");
        safe.anchorMin = Vector2.zero;
        safe.anchorMax = Vector2.one;
        safe.offsetMin = safe.offsetMax = Vector2.zero;

        var page = (RectTransform)_page.transform;
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = Vector2.zero;
        page.offsetMax = new Vector2(0f, -HeaderDp * px);

        _page.Layout(px);
    }

    private static CharacterDefinition Ranger() => AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);

    private static HeroSheet RangerSheet()
    {
        CharacterDefinition ranger = Ranger();

        return HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);
    }

    private static ILocalizer English() =>
        new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath));

    /// <summary>The words the test tree reads: every name "Name x", a2's description long.</summary>
    private static DictionaryLocalizer Words()
    {
        var pairs = new List<string> { "ui.hero.rank", "Rank {0}" };

        foreach (string id in new[] { "skill.a1", "skill.a2", "skill.a3", "skill.b1", "skill.c1" })
        {
            pairs.Add(id + ".name");
            pairs.Add("Name " + id.Substring("skill.".Length));
            pairs.Add(id + ".desc");
            pairs.Add(id == "skill.a2" ? LongWords : "Short.");
        }

        return new DictionaryLocalizer(pairs.ToArray());
    }

    /// <summary>The Ranger with a three-branch tree: a1 → a2 → a3, then b1 and c1 alone.</summary>
    private static ContentCatalog TestCatalog()
    {
        CharacterSpec spec = Ranger().ToSpec();

        var tree = new SkillTreeSpec(
            new ContentId("tree.test"),
            spec.Id,
            new[]
            {
                new SkillBranchSpec(
                    new LocKey("tree.a"),
                    new IReadOnlyList<ContentId>[]
                    {
                        new[] { new ContentId("skill.a1") },
                        new[] { new ContentId("skill.a2") },
                        new[] { new ContentId("skill.a3") },
                    }),
                Single("tree.b", "skill.b1"),
                Single("tree.c", "skill.c1"),
            });

        SkillSpec[] skills = new[] { "skill.a1", "skill.a2", "skill.a3", "skill.b1", "skill.c1" }
            .Select(id => new SkillSpec(new ContentId(id), new LocKey(id + ".name"), new LocKey(id + ".desc"), SkillKind.Passive, new IEffect[] { new NoEffect() }))
            .ToArray();

        return new ContentCatalog(new[] { spec }, skills: skills, trees: new[] { tree });
    }

    private static SkillBranchSpec Single(string name, string node) => new SkillBranchSpec(
        new LocKey(name),
        new IReadOnlyList<ContentId>[] { new[] { new ContentId(node) } });

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

    /// <summary><paramref name="rect"/> in <paramref name="space"/>'s own coordinates.</summary>
    private static Rect Local(RectTransform rect, RectTransform space)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector3 min = space.InverseTransformPoint(corners[0]);
        Vector3 max = space.InverseTransformPoint(corners[2]);

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect World(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    /// <summary>An effect that is nothing but an effect: these rows are about the view.</summary>
    private sealed class NoEffect : IEffect
    {
    }
}
