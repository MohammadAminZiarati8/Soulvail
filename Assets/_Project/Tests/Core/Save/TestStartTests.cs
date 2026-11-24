using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Save;

/// <summary>DT-01: a run that begins at any stage with the experience the stages before it paid.</summary>
[TestFixture]
public sealed class TestStartTests
{
    private const int Seed = 41;

    private static readonly ContentId ModeId = new ContentId("mode.test");
    private static readonly ContentId Ranger = new ContentId("character.ranger");
    private static readonly ContentId Frog = new ContentId("enemy.frog");
    private static readonly ContentId Pitcher = new ContentId("enemy.pitcher");
    private static readonly ContentId Boss = new ContentId("boss.warden");
    private static readonly ContentId BossBody = new ContentId("enemy.warden");

    private static readonly RandomState Random = new RandomState(1, 2, 3, 4, 5);

    [Test]
    public void XpBefore_ScriptedStage_PaysItsSpawns()
    {
        var start = new TestStart(Catalog(Mode(Scripted(1, Frog, 3))));

        // Three Frogs at 10 each, not the budget's 3 × B(1) = 120.
        Assert.That(start.XpBefore(ModeId, 2), Is.EqualTo(30f));
    }

    [Test]
    public void XpBefore_OpenStage_PaysThreeTimesBudget()
    {
        ModeSpec mode = Mode();
        var start = new TestStart(Catalog(mode));

        float expected = 3f * (mode.Scaling.Budget.At(1) + mode.Scaling.Budget.At(2));

        Assert.That(start.XpBefore(ModeId, 3), Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void LegacyTestStart_UsesExistingEstimate()
    {
        ModeSpec mode = Mode(Scripted(1, Frog, 3));
        var start = new TestStart(Catalog(mode));

        var tracker = new LevelTracker(mode.Xp, new SilentEvents());
        tracker.Grant(start.XpBefore(ModeId, 10));

        RunSnapshot snapshot = start.At(ModeId, Ranger, Seed, Random, 10);

        Assert.That(snapshot.Level, Is.GreaterThan(1), "Nine stages pay more than one level.");
        Assert.That(snapshot.Level, Is.EqualTo(tracker.Level));
        Assert.That(snapshot.Xp, Is.EqualTo(tracker.Xp).Within(0.001f));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(tracker.PendingLevelUps));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(snapshot.Level - 1), "Every pick unspent.");
        AssertRestores(mode, snapshot);
    }

    [Test]
    public void At_WritesAFullHealthEmptyRun()
    {
        RunSnapshot snapshot = new TestStart(Catalog(Mode())).At(ModeId, Ranger, Seed, Random, 5);

        Assert.That(snapshot.Version, Is.EqualTo(RunSnapshot.CurrentVersion));
        Assert.That(snapshot.ModeId, Is.EqualTo(ModeId));
        Assert.That(snapshot.CharacterId, Is.EqualTo(Ranger));
        Assert.That(snapshot.Seed, Is.EqualTo(Seed));
        Assert.That(snapshot.Random, Is.EqualTo(Random));
        Assert.That(snapshot.StageIndex, Is.EqualTo(5));
        Assert.That(snapshot.PlayerHp, Is.EqualTo(float.MaxValue), "Health.Restore clamps it to full.");
        Assert.That(snapshot.PlayerShield, Is.EqualTo(float.MaxValue));
        Assert.That(snapshot.RunTime, Is.EqualTo(0f));
        Assert.That(snapshot.TakenNodeIds, Is.Empty);
        Assert.That(snapshot.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(snapshot.ManualSkillIds, Has.All.EqualTo(default(ContentId)));
        Assert.That(snapshot.Economy, Is.EqualTo(default(RunEconomy)));
        Assert.That(snapshot.BanishedNodeIds, Is.Empty);
        Assert.That(snapshot.PactedNodeIds, Is.Empty);
        Assert.That(snapshot.OrdealIds, Is.Empty);
    }

    [Test]
    public void At_OutsideTheMode_Throws()
    {
        var start = new TestStart(Catalog(Mode()));

        Assert.Throws<ArgumentOutOfRangeException>(() => start.At(ModeId, Ranger, Seed, Random, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => start.At(ModeId, Ranger, Seed, Random, 31));
        Assert.Throws<ArgumentOutOfRangeException>(() => start.XpBefore(ModeId, 31));
    }

    [Test]
    public void At_FirstStage_IsAFreshRun()
    {
        RunSnapshot snapshot = new TestStart(Catalog(Mode())).At(ModeId, Ranger, Seed, Random, 1);

        Assert.That(snapshot.Level, Is.EqualTo(1));
        Assert.That(snapshot.Xp, Is.EqualTo(0f));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_RefusesANullCatalog()
    {
        Assert.Throws<ArgumentNullException>(() => new TestStart(null));
    }

    [Test]
    public void CampaignTestStart_ReplaysRegularAndBossStagesInOrder()
    {
        ModeSpec mode = CampaignMode();
        var start = new TestStart(Catalog(mode));
        var tracker = new LevelTracker(mode.Xp, new SilentEvents());
        tracker.Grant(54);
        tracker.Grant(135);
        tracker.Grant(174);
        tracker.Grant(234);

        AssertProgress(start.At(ModeId, Ranger, Seed, Random, 5),
            tracker.Level, tracker.Xp, tracker.PendingLevelUps);
        Assert.That(start.XpBefore(ModeId, 5), Is.EqualTo(597f).Within(0.001f));

        float reward = 0.75f * tracker.XpToNext;
        tracker.Grant(reward);
        AssertProgress(start.At(ModeId, Ranger, Seed, Random, 6),
            tracker.Level, tracker.Xp, tracker.PendingLevelUps);
        Assert.That(start.XpBefore(ModeId, 6), Is.EqualTo(597f + reward).Within(0.001f));
    }

    [Test]
    public void CampaignTestStart_FirstAndCappedEntriesHaveCorrectPoints()
    {
        ModeSpec mode = CampaignMode();
        var start = new TestStart(Catalog(mode));
        AssertProgress(start.At(ModeId, Ranger, Seed, Random, 1), 1, 0f, 0);
        AssertProgress(start.At(ModeId, Ranger, Seed, Random, 29), 30, 0f, 29);
        AssertProgress(start.At(ModeId, Ranger, Seed, Random, 30), 30, 0f, 29);

        float capCost = 0f;
        for (int level = 2; level <= 30; level++)
        {
            capCost += mode.Xp.ToReach(level);
        }

        Assert.That(start.XpBefore(ModeId, 29), Is.EqualTo(capCost));
        Assert.That(start.XpBefore(ModeId, 30), Is.EqualTo(capCost));
    }

    [Test]
    public void CampaignTestStart_RemainderAndPointsSurviveContinue()
    {
        ModeSpec mode = CampaignMode();
        RunSnapshot snapshot = new TestStart(Catalog(mode)).At(ModeId, Ranger, Seed, Random, 2);
        AssertProgress(snapshot, 2, 54f - mode.Xp.ToReach(2), 1);
        AssertRestores(mode, snapshot);
    }

    private static void AssertProgress(RunSnapshot snapshot, int level, float xp, int points)
    {
        Assert.That(snapshot.Level, Is.EqualTo(level));
        Assert.That(snapshot.Xp, Is.EqualTo(xp).Within(0.001f));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(points));
    }

    private static void AssertRestores(ModeSpec mode, RunSnapshot snapshot)
    {
        var random = new FixedRandom(snapshot.Seed);
        var events = new RecordingEvents();
        var session = new RunSession(Catalog(mode), random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), 32, 28, 8);
        session.Start(new RunConfig(ModeId, Ranger, snapshot.Seed, snapshot.StageIndex, SpawnPlan.Empty, snapshot));
        Assert.That(session.State.Level, Is.EqualTo(snapshot.Level));
        Assert.That(session.State.Xp, Is.EqualTo(snapshot.Xp));
        Assert.That(session.State.PendingLevelUps, Is.EqualTo(snapshot.PendingLevelUps));
    }

    // ---- Fixtures ------------------------------------------------------------------------------

    private static ScriptedStage Scripted(int stage, ContentId specId, int count) =>
        new ScriptedStage(stage, new[] { new ScriptedSpawn(1, specId, count) });

    private static ModeSpec CampaignMode() => Mode(new CampaignXpProfile(), Scripted(1, Pitcher, 1));

    private static ModeSpec Mode(params ScriptedStage[] scripted) => Mode(null, scripted);

    private static ModeSpec Mode(CampaignXpProfile profile, params ScriptedStage[] scripted) => new ModeSpec(
        ModeId,
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        Scalings.Design(),
        Scalings.Xp(),
        new[] { new RosterEntry(Frog, 1), new RosterEntry(Pitcher, 2) },
        bossRoster: new[] { new BossRosterEntry(Boss, everyNStages: 0, atStage: 5) },
        scriptedStages: scripted,
        campaignXp: profile);

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        new[]
        {
            new CharacterSpec(Ranger, new LocKey("character.ranger.name"), new LocKey("character.ranger.desc"),
                100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
                new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
                new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
                new FocusSpec(0.4f, 1f, 1f),
                new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f)),
        },
        new[]
        {
            Archetype(Frog, 10f),
            Archetype(Pitcher, 21f, brood: new BroodSpec(Frog, 2, 0.05f, 1f)),
            Archetype(BossBody, 300f, EnemyBehaviourKind.Boss),
        },
        new[] { mode },
        bosses: new[] { new BossSpec(Boss, BossBody, new[] { new BossPhaseSpec(1f) }, 1.5f) });

    private static EnemySpec Archetype(ContentId id, float xpValue,
        EnemyBehaviourKind behaviour = EnemyBehaviourKind.Chaser, BroodSpec brood = null) => new EnemySpec(
        id,
        new LocKey($"{id}.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: xpValue,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: behaviour,
        brood: brood);
}
