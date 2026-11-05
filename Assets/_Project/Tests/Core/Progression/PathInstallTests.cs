using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Content;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

[TestFixture]
public sealed class PathInstallTests
{
    // Behaviour 3: append in order, preserve live flags, and refuse incompatible installs.
    [Test]
    public void Install_AppendsAfterTheClass()
    {
        TreeRules rules = Rules();
        var tree = new SkillTree(rules, Registry(), new SilentEvents());
        Assert.Throws<InvalidOperationException>(() => tree.OnPathInstalled());
        tree.Take(new ContentId("skill.a1"));
        tree.Banish(new ContentId("skill.b1"));
        PathSpec path = TwoSkills();
        ContentCatalog catalog = PathCatalog();
        rules.InstallPath(path, catalog);
        tree.OnPathInstalled();
        tree.OnPathInstalled();

        Assert.That(rules.BranchCount, Is.EqualTo(5));
        Assert.That(rules.Count, Is.EqualTo(6));
        Assert.That(rules.PathId, Is.EqualTo(path.Id));
        Assert.That(rules.SplashBranch, Is.EqualTo(TreeRules.NoSplash));
        Assert.That(rules.Branch(3), Is.SameAs(path.Skills[0].Ranks));
        Assert.That(rules.Branch(4), Is.SameAs(path.Skills[1].Ranks));
        Assert.That(rules.TryLocate(new ContentId("skill.p2"), out int branch, out int tier), Is.True);
        Assert.That(branch, Is.EqualTo(3));
        Assert.That(tier, Is.EqualTo(2));
        Assert.That(tree.IsTaken(new ContentId("skill.a1")), Is.True);
        Assert.That(tree.IsBanished(new ContentId("skill.b1")), Is.True);
        Assert.That(tree.TakenInBranch(0), Is.EqualTo(1));
        Assert.That(tree.TakenInBranch(3), Is.Zero);
        Assert.Throws<InvalidOperationException>(() => rules.InstallPath(path, catalog));
        Assert.Throws<InvalidOperationException>(() => TreeRulesTests.Install(rules,
            TreeRulesTests.BorrowedBranch(), TreeRulesTests.BorrowedSkills()));

        TreeRules splashed = Rules();
        TreeRulesTests.Install(splashed, TreeRulesTests.BorrowedBranch(), TreeRulesTests.BorrowedSkills());
        Assert.Throws<InvalidOperationException>(() => splashed.InstallPath(path, catalog));
        var foreign = new PathSpec(path.Id, new ContentId("character.other"), path.NameKey, path.DescriptionKey, path.Skills);
        Assert.Throws<ArgumentException>(() => Rules().InstallPath(foreign, catalog));
        Assert.Throws<ArgumentNullException>(() => Rules().InstallPath(null, catalog));
        Assert.Throws<ArgumentNullException>(() => Rules().InstallPath(path, null));

        TreeRules unchanged = Rules();
        Assert.Throws<KeyNotFoundException>(() => unchanged.InstallPath(path, Catalog(Array.Empty<SkillSpec>())));
        Assert.That(unchanged.Count, Is.EqualTo(3));
        Assert.That(unchanged.PathId, Is.EqualTo(default(ContentId)));
        Assert.Throws<ArgumentException>(() => unchanged.InstallPath(
            Path(new PathSkillSpec(Ranks("a", 1), 1)), Catalog(new[] { Node("skill.a1") })));
        unchanged.InstallPath(path, catalog);
        Assert.That(unchanged.Count, Is.EqualTo(6), "A refused install must leave no partial nodes.");
    }

    // Behaviour 4: the shared gate controls reads, offers, free picks and restore.
    [Test]
    public void Gate_BelowLevel_Unavailable()
    {
        TreeRules rules = Rules();
        var events = new SilentEvents();
        EffectRegistry effects = Registry();
        var tree = new SkillTree(rules, effects, events);
        var offers = new OfferGenerator(rules); // Created before install: its buffers must grow.
        rules.InstallPath(TwoSkills(), PathCatalog());
        tree.OnPathInstalled();
        Action<int> sync = LevelSync(tree);
        ContentId head = new ContentId("skill.p1");
        ContentId next = new ContentId("skill.p2");
        ContentId later = new ContentId("skill.q1");
        ContentId[] available = new ContentId[rules.Count];
        var drawn = new ContentId[3];
        sync(13);
        Assert.That(tree.IsAvailable(head), Is.False);
        Assert.That(tree.Available(available), Is.EqualTo(3));
        Assert.Throws<InvalidOperationException>(() => tree.Take(head));
        for (int seed = 0; seed < 32; seed++)
        {
            int count = offers.Draw(tree, new FixedRandom(seed).Offers, 3, drawn, out _);
            Assert.That(count, Is.EqualTo(3));
            for (int i = 0; i < count; i++)
            {
                Assert.That(rules.UnlockLevelOf(drawn[i]), Is.EqualTo(1));
            }
        }

        var progression = new LevelTracker(Scalings.Xp(), events);
        progression.Grant(progression.XpToNext);
        var combat = new PlayerCombat(PathSpecTests.Character(), events, new RecordingIntents(), 8);
        var stats = new PlayerStats(combat,
            new PlayerMotor(new MovementSpec(3f, 0.06f, 0.08f, 720f), Vector3.UnitZ), progression);
        var flow = new LevelUpFlow(tree, progression, new SkillRunner(effects, combat.Blackboard, events),
            effects, events,
            new Veilrot(stats, combat, combat.Blackboard, events), isFree: true);
        Assert.Throws<InvalidOperationException>(() => flow.Take(head));
        Assert.That(progression.PendingLevelUps, Is.EqualTo(1));

        sync(14);
        Assert.That(tree.IsAvailable(head), Is.True);
        Assert.That(tree.IsAvailable(next), Is.False, "The level cannot bypass rank order.");
        Assert.That(tree.IsAvailable(later), Is.False);
        Assert.That(tree.Available(available), Is.EqualTo(4));
        tree.Take(new ContentId("skill.a1"));
        tree.Take(new ContentId("skill.b1"));
        tree.Take(new ContentId("skill.c1"));
        Assert.That(offers.Draw(tree, new FixedRandom(7).Offers, 3, drawn, out _), Is.EqualTo(1));
        Assert.That(drawn[0], Is.EqualTo(head), "Offers include the newly unlocked skill.");
        flow.Take(head);
        Assert.That(progression.PendingLevelUps, Is.Zero);
        Assert.That(tree.IsAvailable(next), Is.True);
        sync(13);
        Assert.That(tree.IsAvailable(next), Is.False, "The last synced level gates every rank.");
        sync(17);
        Assert.That(tree.IsAvailable(later), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => sync(0));

        var replay = new SkillTree(rules, effects, events);
        LevelSync(replay)(14);
        replay.Restore(new[] { head, next });
        Assert.That(replay.TakenCount, Is.EqualTo(2));
    }

    // Behaviour 5: unknown ids fail; ordinary and borrowed nodes open at 1.
    [Test]
    public void UnlockLevelOf_ClassNodeIsOne()
    {
        TreeRules rules = Rules();
        rules.InstallPath(TwoSkills(), PathCatalog());
        Assert.That(rules.UnlockLevelOf(new ContentId("skill.a1")), Is.EqualTo(1));
        Assert.That(rules.UnlockLevelOf(new ContentId("skill.p1")), Is.EqualTo(14));
        Assert.That(rules.UnlockLevelOf(new ContentId("skill.p2")), Is.EqualTo(14));
        Assert.That(rules.UnlockLevelOf(new ContentId("skill.q1")), Is.EqualTo(17));
        Assert.Throws<KeyNotFoundException>(() => rules.UnlockLevelOf(new ContentId("skill.unknown")));
        Assert.Throws<KeyNotFoundException>(() => rules.UnlockLevelOf(default));
        TreeRules splashed = Rules();
        TreeRulesTests.Install(splashed, TreeRulesTests.BorrowedBranch(), TreeRulesTests.BorrowedSkills());
        Assert.That(splashed.UnlockLevelOf(new ContentId(TreeRulesTests.Node('x', 1, 'a'))), Is.EqualTo(1));
    }

    // Behaviour 7: exercise closed, open, taken, parent-gated and unknown paths under the recorder.
    [Test]
    public void IsAvailable_NoAlloc()
    {
        TreeRules rules = Rules();
        rules.InstallPath(TwoSkills(), PathCatalog());
        var tree = new SkillTree(rules, Registry(), new SilentEvents());
        Action<int> sync = LevelSync(tree);
        ContentId head = new ContentId("skill.p1");
        ContentId next = new ContentId("skill.p2");
        ContentId later = new ContentId("skill.q1");
        ContentId ordinary = new ContentId("skill.a1");
        ContentId missing = new ContentId("skill.unknown");
        sync(13);
        AllocationAssert.None(() => tree.IsAvailable(head));
        sync(14);
        AllocationAssert.None(() =>
        {
            tree.IsAvailable(head);
            tree.IsAvailable(next);
            tree.IsAvailable(later);
            tree.IsAvailable(ordinary);
            tree.IsAvailable(missing);
        });
        tree.Take(head);
        AllocationAssert.None(() =>
        {
            tree.IsAvailable(head);
            tree.IsAvailable(next);
        });
    }

    internal static SkillBranchSpec Ranks(string stem, int count)
    {
        var tiers = new IReadOnlyList<ContentId>[count];
        for (int i = 0; i < count; i++)
        {
            tiers[i] = new[] { new ContentId($"skill.{stem}{i + 1}") };
        }
        return new SkillBranchSpec(new LocKey($"branch.{stem}"), tiers);
    }

    internal static PathSpec Path(params PathSkillSpec[] skills) => new PathSpec(
        new ContentId("path.marksman"), new ContentId("character.ranger"),
        new LocKey("path.marksman.name"), new LocKey("path.marksman.desc"), skills);

    internal static SkillSpec Node(string id, string parent = null) => new SkillSpec(
        new ContentId(id), new LocKey(id + ".name"), new LocKey(id + ".desc"),
        parent is null ? SkillKind.Passive : SkillKind.Upgrade, new IEffect[] { TreeRulesTests.Damage(0.05f) },
        parentId: parent is null ? default : new ContentId(parent));

    internal static ContentCatalog Catalog(IReadOnlyList<SkillSpec> skills) =>
        new ContentCatalog(Array.Empty<CharacterSpec>(), skills: skills);

    internal static TreeRules Rules()
    {
        var tree = new SkillTreeSpec(new ContentId("tree.ranger"), new ContentId("character.ranger"),
            new[] { Ranks("a", 1), Ranks("b", 1), Ranks("c", 1) });
        return new TreeRules(tree, Catalog(new[] { Node("skill.a1"), Node("skill.b1"), Node("skill.c1") }));
    }

    private static PathSpec TwoSkills() => Path(new PathSkillSpec(Ranks("p", 2), 14), new PathSkillSpec(Ranks("q", 1), 17));

    private static ContentCatalog PathCatalog() => Catalog(new[]
    {
        Node("skill.p1"), Node("skill.p2", "skill.p1"), Node("skill.q1"),
    });

    // Preserve the internal API without granting the whole test assembly access to RunState.
    private static Action<int> LevelSync(SkillTree tree) => (Action<int>)Delegate.CreateDelegate(
        typeof(Action<int>), tree, typeof(SkillTree).GetMethod("SyncLevel", BindingFlags.Instance | BindingFlags.NonPublic));

    private static EffectRegistry Registry()
    {
        var registry = new EffectRegistry();
        registry.Register<ModifyStat>(new Ignoring());
        return registry;
    }

    // These rows check gates and flags; stat application is covered by SkillTreeTests.
    private sealed class Ignoring : IEffectHandler<ModifyStat>
    {
        public void Apply(ModifyStat effect, object source)
        {
        }

        public void Remove(ModifyStat effect, object source)
        {
        }
    }
}
