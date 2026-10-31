using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Progression;

namespace Soulvail.Tests.Core.Content;

[TestFixture]
public sealed class PathSpecTests
{
    // Behaviour 1: ranks are single-node tiers; kinds and parents need the catalog at install.
    [Test]
    public void PathSkill_RanksAreTiers()
    {
        var ranks = new PathSkillSpec(PathInstallTests.Ranks("p", 3), 14);
        PathSpec path = PathInstallTests.Path(ranks);
        Assert.That(ranks.Ranks.Tier(2)[0], Is.EqualTo(new ContentId("skill.p2")));
        Assert.That(ranks.UnlockLevel, Is.EqualTo(14));
        Assert.That(path.TryLocate(new ContentId("skill.p2"), out int skill, out int rank), Is.True);
        Assert.That(skill, Is.Zero);
        Assert.That(rank, Is.EqualTo(2));
        Assert.That(path.NodeCount, Is.EqualTo(3));
        Assert.Throws<ArgumentNullException>(() => new PathSkillSpec(null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathSkillSpec(ranks.Ranks, 0));
        Assert.Throws<ArgumentException>(() => new PathSkillSpec(
            TreeRulesTests.Branch('p', new[] { "skill.p1", "skill.p2" }), 1));
        Assert.Throws<ArgumentException>(() => PathInstallTests.Ranks("p", 0));
        Assert.Throws<ArgumentException>(() => PathInstallTests.Ranks("p", 9));
        Assert.That(new PathSkillSpec(PathInstallTests.Ranks("p", 8), 1).Ranks.TierCount, Is.EqualTo(8));

        var nodes = new List<SkillSpec>
        {
            PathInstallTests.Node("skill.p1"),
            PathInstallTests.Node("skill.p2", "skill.p1"),
            PathInstallTests.Node("skill.p3", "skill.p2"),
        };
        PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes));
        nodes[0] = TreeRulesTests.Active("skill.p1");
        PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes));
        nodes[0] = TreeRulesTests.Keystone("skill.p1");
        Assert.Throws<ArgumentException>(() => PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes)));
        nodes[0] = PathInstallTests.Node("skill.p1", "skill.other");
        Assert.Throws<ArgumentException>(() => PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes)));
        nodes[0] = PathInstallTests.Node("skill.p1");
        nodes[1] = PathInstallTests.Node("skill.p2");
        Assert.Throws<ArgumentException>(() => PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes)));
        nodes[1] = PathInstallTests.Node("skill.p2", "skill.p1");
        nodes[2] = PathInstallTests.Node("skill.p3", "skill.p1");
        Assert.Throws<ArgumentException>(() => PathInstallTests.Rules().InstallPath(path, PathInstallTests.Catalog(nodes)));
    }

    // Behaviour 2: spec guards and immutable catalog indexing.
    [Test]
    public void PathSpec_Validates()
    {
        var skill = new PathSkillSpec(PathInstallTests.Ranks("p", 1), 1);
        Assert.Throws<ArgumentException>(() => PathInstallTests.Path());
        Assert.Throws<ArgumentException>(() => PathInstallTests.Path(new PathSkillSpec[9]));
        Assert.Throws<ArgumentException>(() => PathInstallTests.Path(skill, skill));
        Assert.Throws<ArgumentException>(() => PathInstallTests.Path(new PathSkillSpec(
            TreeRulesTests.Branch('p', new[] { "skill.p1" }, new[] { "skill.p1" }), 1)));
        Assert.Throws<ArgumentNullException>(() => PathInstallTests.Path(null));
        Assert.Throws<ArgumentNullException>(() => PathInstallTests.Path(new PathSkillSpec[] { null }));
        Assert.Throws<ArgumentException>(() => Make(default, new ContentId("character.ranger"), new LocKey("path.name"), new LocKey("path.desc")));
        Assert.Throws<ArgumentException>(() => Make(new ContentId("path.test"), default, new LocKey("path.name"), new LocKey("path.desc")));
        Assert.Throws<ArgumentException>(() => Make(new ContentId("path.test"), new ContentId("character.ranger"), default, new LocKey("path.desc")));
        Assert.Throws<ArgumentException>(() => Make(new ContentId("path.test"), new ContentId("character.ranger"), new LocKey("path.name"), default));

        PathSkillSpec[] source = { skill };
        PathSpec path = PathInstallTests.Path(source);
        source[0] = null;
        Assert.That(path.Skills[0], Is.SameAs(skill));
        Assert.Throws<NotSupportedException>(() => ((IList<PathSkillSpec>)path.Skills)[0] = null);
        Assert.That(path.TryLocate(default, out int missingSkill, out int missingRank), Is.False);
        Assert.That(missingSkill, Is.EqualTo(-1));
        Assert.That(missingRank, Is.Zero);

        var eight = new PathSkillSpec[PathSpec.MaxSkills];
        for (int i = 0; i < eight.Length; i++)
        {
            eight[i] = new PathSkillSpec(PathInstallTests.Ranks("p" + i, 1), 1);
        }
        Assert.That(PathInstallTests.Path(eight).Skills.Count, Is.EqualTo(8));

        PathSpec[] paths = { path };
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>(), paths: paths);
        paths[0] = null;
        Assert.That(catalog.Paths[0], Is.SameAs(path));
        Assert.That(catalog.Path(path.Id), Is.SameAs(path));
        Assert.That(catalog.TryGetPath(path.Id, out PathSpec found), Is.True);
        Assert.That(found, Is.SameAs(path));
        Assert.That(catalog.TryGetPath(default, out _), Is.False);
        Assert.Throws<KeyNotFoundException>(() => catalog.Path(default));
        Assert.Throws<ArgumentException>(() => new ContentCatalog(Array.Empty<CharacterSpec>(), paths: new[] { path, path }));
        Assert.Throws<ArgumentException>(() => new ContentCatalog(Array.Empty<CharacterSpec>(), paths: new PathSpec[] { null }));

        PathSpec Make(ContentId id, ContentId character, LocKey name, LocKey description) =>
            new PathSpec(id, character, name, description, new[] { skill });
    }

    // Behaviour 6: classes without paths keep their old constructor defaults.
    [Test]
    public void Character_PathFields_Validate()
    {
        Assert.That(Character().PathIds, Is.Empty);
        Assert.That(Character().PathOpensAtLevel, Is.Zero);
        ContentId[] ids = { new ContentId("path.marksman"), new ContentId("path.skirmisher") };
        CharacterSpec character = Character(ids, 11);
        ids[0] = new ContentId("path.changed");
        Assert.That(character.PathIds[0], Is.EqualTo(new ContentId("path.marksman")));
        Assert.That(character.PathOpensAtLevel, Is.EqualTo(11));
        Assert.Throws<NotSupportedException>(() => ((IList<ContentId>)character.PathIds)[0] = default);
        Assert.Throws<ArgumentOutOfRangeException>(() => Character(null, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Character(Array.Empty<ContentId>(), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Character(ids, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Character(ids, 1));
        Assert.That(Character(ids, 2).PathOpensAtLevel, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => Character(new[] { ids[0], ids[0] }, 11));
        Assert.Throws<ArgumentException>(() => Character(new ContentId[] { default }, 11));
    }

    internal static CharacterSpec Character(IReadOnlyList<ContentId> paths = null, int opens = 0) => new CharacterSpec(
        new ContentId("character.ranger"), new LocKey("character.ranger.name"), new LocKey("character.ranger.desc"),
        100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 10f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        pathIds: paths, pathOpensAtLevel: opens);
}
