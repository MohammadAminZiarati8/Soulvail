using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

[TestFixture]
public sealed class PathFlowTests
{
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId Marksman = new("path.marksman");
    private static readonly ContentId OtherPath = new("path.other");
    private static readonly ContentId MissingPath = new("path.missing");
    private static readonly ContentId ModeId = new("mode.test");
    private static readonly ContentId FirstRank = new("skill.p1");
    private static readonly ContentId SecondRank = new("skill.p2");
    private static readonly ContentId LastSkill = new("skill.q1");
    private static readonly DateTimeOffset Instant = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private ContentCatalog _catalog;
    private RunSession _session;
    private IProgressionCommands Commands => _session;

    [Test]
    public void Open_AtTheLevel_NotBefore()
    {
        Start(10);
        Assert.That(_session.State.IsPathChoiceOpen, Is.False);
        Start(11);
        Assert.That(_session.State.IsPathChoiceOpen, Is.True);
    }

    [Test]
    public void Open_TreeNotFull_StillOpens()
    {
        Start(11, BaseNodes(5));
        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(5));
        Assert.That(_session.State.IsPathChoiceOpen, Is.True);
    }

    [Test]
    public void Candidates_AuthoredPaths_InClassOrder()
    {
        Start(11);
        Assert.That(_session.State.PathCandidates, Is.EqualTo(new[] { OtherPath, Marksman }));
        Assert.That(_session.State.PathId, Is.EqualTo(default(ContentId)));
    }

    [Test]
    public void Choose_InstallsAndPublishesOnce()
    {
        Start(11, BaseNodes());
        Commands.ChoosePath(Marksman);
        Assert.That(_session.State.PathId, Is.EqualTo(Marksman));
        Assert.That(_session.State.TreeNodeCount, Is.EqualTo(12));
        Assert.That(_session.State.IsNodeAvailable(FirstRank), Is.True);
        Assert.That(_session.State.IsPathChoiceOpen, Is.False);
        Assert.That(_events.Count<PathChosen>(), Is.EqualTo(1));
        Assert.That(_events.Of<PathChosen>()[0].PathId, Is.EqualTo(Marksman));
        Commands.ChoosePath(OtherPath);
        Assert.That(_session.State.PathId, Is.EqualTo(Marksman));
        Assert.That(_events.Count<PathChosen>(), Is.EqualTo(1));
    }

    [Test]
    public void Choose_SpendsNoPoint()
    {
        Start(12, BaseNodes());
        Assert.That(_session.State.SkillPoints, Is.EqualTo(2));
        Commands.ChoosePath(Marksman);
        Assert.That(_session.State.SkillPoints, Is.EqualTo(2));
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
    }

    [Test]
    public void Choose_NotACandidate_Ignored()
    {
        Start(11);
        Commands.ChoosePath(MissingPath);
        Commands.ChoosePath(default);
        Assert.That(_session.State.PathId, Is.EqualTo(default(ContentId)));
        Assert.That(_session.State.TreeNodeCount, Is.EqualTo(9));
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
    }

    [Test]
    public void Choose_Closed_Ignored()
    {
        Start(10);
        Commands.ChoosePath(Marksman);
        Assert.That(_session.State.PathId, Is.EqualTo(default(ContentId)));
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
        _session.End();
        Assert.DoesNotThrow(() => Commands.ChoosePath(Marksman));
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
    }

    [Test]
    public void Choose_Twice_Refused()
    {
        PathFlow flow = Flow(out SkillTree tree);
        Assert.Throws<InvalidOperationException>(() => flow.Choose(Marksman));
        Sync(tree, 11);
        Assert.Throws<ArgumentException>(() => flow.Choose(MissingPath));
        flow.Choose(Marksman);
        Assert.Throws<InvalidOperationException>(() => flow.Choose(OtherPath));
        Assert.That(flow.ChosenId, Is.EqualTo(Marksman));
        Assert.That(_events.Count<PathChosen>(), Is.EqualTo(1));
    }

    [Test]
    public void Resume_DerivesThePath()
    {
        var taken = new List<ContentId>(BaseNodes()) { FirstRank, SecondRank };
        Start(20, taken);
        Assert.That(_session.State.PathId, Is.EqualTo(Marksman));
        Assert.That(_session.State.TakenNodeIds, Is.EqualTo(taken));
        Assert.That(_session.State.IsNodeAvailable(LastSkill), Is.True);
        Assert.That(_session.State.UnlockLevelOf(LastSkill), Is.EqualTo(20));
        Assert.That(_session.State.UnlockLevelOf(BaseNodes()[0]), Is.EqualTo(1));
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
        Assert.That(_events.Count<NodeTaken>(), Is.Zero);
        Assert.That(RunSnapshot.CurrentVersion, Is.EqualTo(6));
    }

    [Test]
    public void Resume_ChosenButUnpicked_AsksAgain()
    {
        Start(11, BaseNodes());
        Commands.ChoosePath(Marksman);
        IReadOnlyList<ContentId> taken = _session.State.TakenNodeIds;
        Start(11, taken);
        Assert.That(_session.State.PathId, Is.EqualTo(default(ContentId)));
        Assert.That(_session.State.IsPathChoiceOpen, Is.True);
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
        Commands.ChoosePath(OtherPath);
        Assert.That(_session.State.PathId, Is.EqualTo(OtherPath));
    }

    [Test]
    public void Splash_NeverOpensWithPaths()
    {
        // A second class with a tree makes the old splash eligible absent the new guard.
        Start(11, BaseNodes(), free: false);
        Assert.That(_session.State.IsSplashPending, Is.False);
        Commands.OpenSplash();
        Assert.That(_session.State.IsSplashOpen, Is.False);
        Assert.That(_events.Count<SplashOffered>(), Is.Zero);
        var tree = Tree();
        var splash = new SplashFlow(tree, _catalog, Registry(), Ranger, _events);
        foreach (ContentId node in BaseNodes())
        {
            tree.Take(node);
        }
        splash.Open();
        Assert.That(splash.IsPending, Is.False);
        Assert.That(splash.IsOpen, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Points_BankedBeforeAPath(bool free)
    {
        for (int level = 11; level <= 12; level++)
        {
            Start(level, BaseNodes(), free);
            Settle(free);
            Assert.That(_session.State.PendingLevelUps, Is.EqualTo(level - 10));
            Assert.That(_session.State.IsTreeFull, Is.False);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Points_GatedNodesLeft_Banks(bool free)
    {
        var taken = new List<ContentId>(BaseNodes()) { FirstRank, SecondRank };
        Start(18, taken, free);
        Assert.That(_session.State.IsNodeAvailable(LastSkill), Is.False);
        Settle(free);
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(6));
        Assert.That(_session.State.IsTreeFull, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Points_AllTaken_StayBanked(bool free)
    {
        var taken = new List<ContentId>(BaseNodes()) { FirstRank, SecondRank, LastSkill };
        Start(20, taken, free);
        Settle(free);
        Assert.That(_session.State.IsTreeFull, Is.True);
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(7));
    }

    [Test]
    public void Derive_NoPathNode_False_AndRestoreIsSilent()
    {
        PathFlow flow = Flow(out _);
        Assert.That(flow.TryDerive(BaseNodes(), out ContentId absent), Is.False);
        Assert.That(absent, Is.EqualTo(default(ContentId)));
        Assert.That(flow.TryDerive(new[] { SecondRank }, out ContentId found), Is.True);
        Assert.That(found, Is.EqualTo(Marksman));
        flow.Restore(found);
        Assert.That(flow.ChosenId, Is.EqualTo(Marksman));
        Assert.That(_events.Count<PathChosen>(), Is.Zero);
    }

    private void Settle(bool free)
    {
        if (free)
        {
            Commands.SettleFullTree();
        }
        else
        {
            Commands.OpenLevelUp();
        }
    }

    private PathFlow Flow(out SkillTree tree)
    {
        Start(11);
        tree = Tree();
        return new PathFlow(tree, _catalog, _catalog.Character(Ranger), _events);
    }

    private SkillTree Tree() => new(new TreeRules(_catalog.Trees[0], _catalog), Registry(), _events);

    private static EffectRegistry Registry()
    {
        var registry = new EffectRegistry();
        registry.Register<ModifyStat>(new Ignoring());
        return registry;
    }

    private static void Sync(SkillTree tree, int level) => typeof(SkillTree).GetMethod(
        "SyncLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .Invoke(tree, new object[] { level });

    private void Start(int level, IReadOnlyList<ContentId> taken = null, bool free = true)
    {
        _events = new RecordingEvents();
        var character = new CharacterSpec(Ranger, new LocKey("class.ranger"), new LocKey("class.desc"),
            100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
            pathIds: new[] { MissingPath, OtherPath, Marksman }, pathOpensAtLevel: 11);
        var other = new CharacterSpec(new ContentId("character.other"), character.NameKey, character.DescriptionKey,
            character.MaxHp, character.Movement, character.Targeting, character.Weapon, character.Focus,
            character.MovementSkill);
        var branches = new[] { Branch("a"), Branch("b"), Branch("c") };
        var tree = new SkillTreeSpec(new ContentId("tree.ranger"), Ranger, branches);
        var skills = new List<SkillSpec>();
        foreach (ContentId id in BaseNodes())
        {
            skills.Add(Node(id));
        }
        skills.Add(Node(FirstRank));
        skills.Add(Node(SecondRank, FirstRank));
        skills.Add(Node(LastSkill));
        skills.Add(Node(new ContentId("skill.other1")));
        var marksman = new PathSpec(Marksman, Ranger, new LocKey("path.name"), new LocKey("path.desc"),
            new[] { new PathSkillSpec(PathInstallTests.Ranks("p", 2), 11),
                new PathSkillSpec(PathInstallTests.Ranks("q", 1), 20) });
        var alternative = new PathSpec(OtherPath, Ranger, marksman.NameKey, marksman.DescriptionKey,
            new[] { new PathSkillSpec(PathInstallTests.Ranks("other", 1), 11) });
        var mode = new ModeSpec(ModeId, new LocKey("mode.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(20f, 6f, 0.04f), new WaveCurve(2, 1000, 2, 2),
                new ConcurrencyCurve(28, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)), Scalings.Xp(),
            new[] { new RosterEntry(new ContentId("enemy.husk"), 1) },
            new[] { new ArenaEntry(new ContentId("arena.first")) },
            hasFreePick: free);
        var enemy = new EnemySpec(new ContentId("enemy.husk"), new LocKey("enemy.name"),
            10f, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
        _catalog = new ContentCatalog(new[] { character, other }, new[] { enemy }, new[] { mode }, skills,
            new[] { tree, new SkillTreeSpec(new ContentId("tree.other"), other.Id, branches) },
            paths: new[] { marksman, alternative });
        var random = new FixedRandom(1, new float[8192]);
        _session = new RunSession(_catalog, random, _events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(Instant), _events), 64, 28, 8);
        taken ??= Array.Empty<ContentId>();
        var snapshot = new RunSnapshot(RunSnapshot.CurrentVersion, ModeId, Ranger, 1, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, Instant, level, 0f,
            level - 1 - taken.Count, taken, new ContentId[SkillRunner.MaxManualSlots],
            new RunEconomy(0, 0f, 0, 0), Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        _session.Start(new RunConfig(ModeId, Ranger, 1, 1, SpawnPlan.Empty, snapshot));
    }

    private static SkillSpec Node(ContentId id, ContentId parent = default) => new(id,
        new LocKey(id.Value + ".name"), new LocKey(id.Value + ".desc"),
        parent.Value is null ? SkillKind.Passive : SkillKind.Upgrade,
        new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.01f) }, parentId: parent);

    // Direct flow rows exercise choice and tree flags; session rows use the real stat handler.
    private sealed class Ignoring : IEffectHandler<ModifyStat>
    {
        public void Apply(ModifyStat effect, object source)
        {
        }

        public void Remove(ModifyStat effect, object source)
        {
        }
    }

    private static SkillBranchSpec Branch(string name) => new(new LocKey("branch." + name),
        new IReadOnlyList<ContentId>[] { new[] { new ContentId("skill." + name + "1"),
            new ContentId("skill." + name + "2"), new ContentId("skill." + name + "3") } });

    private static ContentId[] BaseNodes(int count = 9)
    {
        var ids = new ContentId[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = new ContentId("skill." + (char)('a' + i / 3) + (i % 3 + 1));
        }
        return ids;
    }
}
