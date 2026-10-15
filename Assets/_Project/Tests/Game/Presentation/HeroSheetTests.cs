using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-12c: what <see cref="HeroSheet.Build"/> reads off a class — five base numbers, the attack, the
/// Space move, every rank of the tree — and that every key the Ranger's sheet emits is written.
/// </summary>
[TestFixture]
public sealed class HeroSheetTests
{
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";
    private const string RollPath = "Assets/_Project/Art/UI/Icons/Icon_Roll.png";
    private const string PlaceholderPath = "Assets/_Project/Art/UI/Icons/Icon_Placeholder.png";

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

    // ---- Rule 1: reads, never writes -------------------------------------------------------------

    [Test]
    public void Build_Guards()
    {
        CharacterSpec spec = Ranger().ToSpec();
        ContentCatalog catalog = ShippedCatalog();

        Assert.Throws<ArgumentNullException>(() => HeroSheet.Build(null, catalog, default, null));
        Assert.Throws<ArgumentNullException>(() => HeroSheet.Build(spec, null, default, null));

        HeroSheet sheet = null;
        Assert.That(() => sheet = HeroSheet.Build(spec, catalog, default, null), Throws.Nothing);
        Assert.That(sheet.HasAttack, Is.False);
        Assert.That(sheet.HasSpace, Is.False);
        Assert.That(sheet.Branches.SelectMany(b => b.Ranks).All(r => r.Icon == null), Is.True, "null icons draw null.");
        Assert.That(HeroSheet.Build(spec, catalog, default, null), Is.Not.SameAs(sheet), "a new sheet each call.");
    }

    // ---- Rule 2: five stats ----------------------------------------------------------------------

    [Test]
    public void Stats_FiveInOrder_FromTheBaseSpec()
    {
        CharacterDefinition definition = Edited(
            ("_maxHp", 41f), ("_weaponDamage", 7f), ("_weaponSwingsPerSecond", 1.5f), ("_weaponRange", 6f), ("_speed", 2.5f));

        HeroSheet sheet = HeroSheet.Build(definition.ToSpec(), ShippedCatalog(), default, null);

        Assert.That(sheet.Stats.Select(s => s.Label.Key), Is.EqualTo(new[]
        {
            "ui.hero.stat.health", "ui.hero.stat.damage", "ui.hero.stat.rate", "ui.hero.stat.range", "ui.hero.stat.speed",
        }));
        Assert.That(sheet.Stats.Select(s => s.Format.Key), Is.EqualTo(new[]
        {
            "ui.hero.value.whole", "ui.hero.value.whole", "ui.hero.value.tenths", "ui.hero.value.metres", "ui.hero.value.speed",
        }));
        Assert.That(sheet.Stats.Select(s => s.Value), Is.EqualTo(new[] { 41f, 7f, 1.5f, 6f, 2.5f }));
    }

    [Test]
    public void Stats_Ranger_AreItsAuthoredNumbers()
    {
        HeroSheet sheet = HeroSheet.Build(Ranger().ToSpec(), ShippedCatalog(), Ranger().ToLook(), null);

        Assert.That(sheet.Stats.Select(s => s.Value), Is.EqualTo(new[] { 90f, 13f, 2.2f, 10f, 3.2f }).Within(1e-4f));
    }

    // ---- Rules 3, 4: the attack and the Space move -------------------------------------------------

    [Test]
    public void Attack_FromTheLook_EmblemIcon()
    {
        CharacterDefinition ranger = Ranger();
        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);

        Assert.That(sheet.HasAttack, Is.True);
        Assert.That(sheet.Attack.NameKey.Key, Is.EqualTo("character.ranger.attack.name"));
        Assert.That(sheet.Attack.DescriptionKey.Key, Is.EqualTo("character.ranger.attack.description"));
        Assert.That(sheet.Attack.Rank, Is.Zero);
        Assert.That(sheet.Attack.Icon == ranger.ToLook().Emblem, Is.True, "the class emblem.");
        Assert.That(sheet.Attack.Icon == null, Is.False, "the Ranger has an emblem.");
    }

    [Test]
    public void Attack_NoKey_NoEntry()
    {
        CharacterDefinition ranger = Ranger();
        CharacterLook look = ranger.ToLook();
        var keyless = new CharacterLook(look.Body, emblem: look.Emblem, spaceNameKey: look.SpaceNameKey);

        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), keyless, null);

        Assert.That(sheet.HasAttack, Is.False);
        Assert.That(sheet.Attack.NameKey.Key, Is.Null);
    }

    [Test]
    public void Space_PresentWithAKindAndAKey()
    {
        CharacterDefinition ranger = Ranger();
        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);

        Assert.That(sheet.HasSpace, Is.True);
        Assert.That(sheet.Space.NameKey.Key, Is.EqualTo("character.ranger.space.name"));
        Assert.That(sheet.Space.DescriptionKey.Key, Is.EqualTo("character.ranger.space.description"));
        Assert.That(sheet.Space.Rank, Is.Zero);
        Assert.That(sheet.Space.Icon, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(RollPath)));

        var unnamed = new CharacterLook(ranger.ToLook().Body, spaceIcon: ranger.ToLook().SpaceIcon);

        Assert.That(HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), unnamed, null).HasSpace, Is.False, "a kind with no key.");
    }

    [Test]
    public void Space_NoneKind_NoEntry()
    {
        CharacterDefinition none = Edited(("_movementSkillKind", (int)MovementSkillKind.None));

        HeroSheet sheet = HeroSheet.Build(none.ToSpec(), ShippedCatalog(), Ranger().ToLook(), null);

        Assert.That(sheet.HasSpace, Is.False);
    }

    // ---- Rules 5, 6: the tree --------------------------------------------------------------------

    [Test]
    public void Branches_EveryTierEveryNode_InOrder()
    {
        CharacterSpec spec = Ranger().ToSpec();
        ContentCatalog catalog = TestCatalog(spec, Tree(spec, ("skill.a1", "skill.a2x", "skill.a2y")), All());

        HeroSheet sheet = HeroSheet.Build(spec, catalog, default, null);

        Assert.That(sheet.Branches.Count, Is.EqualTo(3));
        Assert.That(sheet.Branches[0].NameKey.Key, Is.EqualTo("tree.a"));
        Assert.That(sheet.Branches[0].Ranks.Select(r => r.NameKey.Key), Is.EqualTo(new[] { "skill.a1.name", "skill.a2x.name", "skill.a2y.name" }));
        Assert.That(sheet.Branches[0].Ranks.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 2 }));
        Assert.That(sheet.Branches[0].Ranks[1].DescriptionKey.Key, Is.EqualTo("skill.a2x.desc"));
        Assert.That(sheet.Branches[1].Ranks.Select(r => r.NameKey.Key), Is.EqualTo(new[] { "skill.b1.name" }));
        Assert.That(sheet.Branches[2].Ranks.Select(r => r.NameKey.Key), Is.EqualTo(new[] { "skill.c1.name" }));
    }

    [Test]
    public void Branches_Ranger_ThreeOfThree()
    {
        CharacterDefinition ranger = Ranger();
        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);

        Assert.That(sheet.Branches.Count, Is.EqualTo(3));

        foreach (HeroSheet.Branch branch in sheet.Branches)
        {
            Assert.That(branch.NameKey.Key, Does.StartWith("tree.ranger."));
            Assert.That(branch.Ranks.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(branch.Ranks.All(r => r.NameKey.Key.StartsWith("skill.ranger.", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void Branches_NoTree_Empty()
    {
        CharacterSpec spec = Ranger().ToSpec();
        var catalog = new ContentCatalog(new[] { spec });

        Assert.That(HeroSheet.Build(spec, catalog, default, null).Branches, Is.Empty);
    }

    [Test]
    public void Icon_FallsBackToTheBranchFirst_ThenNull()
    {
        var roll = AssetDatabase.LoadAssetAtPath<Sprite>(RollPath);
        var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
        CharacterSpec spec = Ranger().ToSpec();
        ContentCatalog catalog = TestCatalog(spec, Tree(spec, ("skill.a1", "skill.a2x", "skill.a2y")), All());

        // Branch a: the first and one later node have icons, the rest borrow the first's.
        // Branch b: no icon anywhere, so null. Branch c: only its first, which every rank here is.
        var book = new SkillIconBook(new Dictionary<ContentId, Sprite>
        {
            [new ContentId("skill.a1")] = roll,
            [new ContentId("skill.a2y")] = placeholder,
            [new ContentId("skill.c1")] = placeholder,
        });

        HeroSheet sheet = HeroSheet.Build(spec, catalog, default, book);

        Assert.That(sheet.Branches[0].Ranks[0].Icon, Is.SameAs(roll), "its own.");
        Assert.That(sheet.Branches[0].Ranks[1].Icon, Is.SameAs(roll), "none of its own: the branch's first.");
        Assert.That(sheet.Branches[0].Ranks[2].Icon, Is.SameAs(placeholder), "its own wins over the first.");
        Assert.That(sheet.Branches[1].Ranks[0].Icon == null, Is.True, "no icon in the branch: null.");
        Assert.That(sheet.Branches[2].Ranks[0].Icon, Is.SameAs(placeholder));
    }

    [Test]
    public void Node_MissingFromCatalog_Skipped()
    {
        CharacterSpec spec = Ranger().ToSpec();
        IEnumerable<SkillSpec> withoutA2X = All().Where(s => s.Id.Value != "skill.a2x");
        ContentCatalog catalog = TestCatalog(spec, Tree(spec, ("skill.a1", "skill.a2x", "skill.a2y")), withoutA2X);

        HeroSheet sheet = HeroSheet.Build(spec, catalog, default, null);

        Assert.That(sheet.Branches[0].Ranks.Select(r => r.NameKey.Key), Is.EqualTo(new[] { "skill.a1.name", "skill.a2y.name" }));
        Assert.That(sheet.Branches[0].Ranks.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2 }), "ranks keep their own tier numbers.");
    }

    // ---- Rule 7: the description -----------------------------------------------------------------

    [Test]
    public void Description_IsTheSpecs()
    {
        CharacterSpec spec = Ranger().ToSpec();

        Assert.That(HeroSheet.Build(spec, ShippedCatalog(), default, null).DescriptionKey, Is.EqualTo(spec.DescriptionKey));
    }

    // ---- Rule 8: every key is written ------------------------------------------------------------

    [Test]
    public void Ranger_EveryKeyResolvesInEnglish()
    {
        CharacterDefinition ranger = Ranger();
        HeroSheet sheet = HeroSheet.Build(ranger.ToSpec(), ShippedCatalog(), ranger.ToLook(), null);

        var keys = new List<LocKey>
        {
            HeroSheet.StatsHeading, HeroSheet.AttackHeading, HeroSheet.SpaceHeading, HeroSheet.TreeHeading, HeroSheet.RankFormat,
            sheet.DescriptionKey,
            sheet.Attack.NameKey, sheet.Attack.DescriptionKey,
            sheet.Space.NameKey, sheet.Space.DescriptionKey,
        };

        foreach (HeroSheet.Stat stat in sheet.Stats)
        {
            keys.Add(stat.Label);
            keys.Add(stat.Format);
        }

        foreach (HeroSheet.Branch branch in sheet.Branches)
        {
            keys.Add(branch.NameKey);

            foreach (HeroSheet.Entry rank in branch.Ranks)
            {
                keys.Add(rank.NameKey);
                keys.Add(rank.DescriptionKey);
            }
        }

        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);
        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        IReadOnlyDictionary<LocKey, string> rows = table.ToDictionary();

        foreach (LocKey key in keys)
        {
            Assert.That(key.Key, Is.Not.Null, "a default key was emitted.");
            Assert.That(rows.ContainsKey(key), Is.True, $"English has no row for '{key.Key}'.");
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static CharacterDefinition Ranger() => AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);

    /// <summary>A copy of the Ranger with the named serialized fields set.</summary>
    private CharacterDefinition Edited(params (string Field, float Value)[] fields)
    {
        CharacterDefinition copy = Object.Instantiate(Ranger());
        _spawned.Add(copy);

        var serialized = new SerializedObject(copy);

        foreach ((string field, float value) in fields)
        {
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"CharacterDefinition has no field {field}.");

            if (property.propertyType == SerializedPropertyType.Integer || property.propertyType == SerializedPropertyType.Enum)
            {
                property.intValue = (int)value;
            }
            else
            {
                property.floatValue = value;
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return copy;
    }

    private static SkillSpec Skill(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        new LocKey(id + ".desc"),
        SkillKind.Passive,
        new IEffect[] { new NoEffect() });

    private static IEnumerable<SkillSpec> All() =>
        new[] { "skill.a1", "skill.a2x", "skill.a2y", "skill.b1", "skill.c1" }.Select(Skill);

    /// <summary>Branch a holds the three ids given (one in tier 1, two in tier 2); b and c one node each.</summary>
    private static SkillTreeSpec Tree(CharacterSpec spec, (string One, string TwoX, string TwoY) a) => new SkillTreeSpec(
        new ContentId("tree.test"),
        spec.Id,
        new[]
        {
            new SkillBranchSpec(
                new LocKey("tree.a"),
                new IReadOnlyList<ContentId>[]
                {
                    new[] { new ContentId(a.One) },
                    new[] { new ContentId(a.TwoX), new ContentId(a.TwoY) },
                }),
            Single("tree.b", "skill.b1"),
            Single("tree.c", "skill.c1"),
        });

    private static SkillBranchSpec Single(string name, string node) => new SkillBranchSpec(
        new LocKey(name),
        new IReadOnlyList<ContentId>[] { new[] { new ContentId(node) } });

    private static ContentCatalog TestCatalog(CharacterSpec spec, SkillTreeSpec tree, IEnumerable<SkillSpec> skills) =>
        new ContentCatalog(new[] { spec }, skills: skills.ToList(), trees: new[] { tree });

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

    /// <summary>An effect that is nothing but an effect: these rows are about the sheet, not a primitive.</summary>
    private sealed class NoEffect : IEffect
    {
    }
}
