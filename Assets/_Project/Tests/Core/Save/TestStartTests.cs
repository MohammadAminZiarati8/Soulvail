using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
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
    public void At_LevelsLikeTheTracker()
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

    // ---- Fixtures ------------------------------------------------------------------------------

    private static ScriptedStage Scripted(int stage, ContentId specId, int count) =>
        new ScriptedStage(stage, new[] { new ScriptedSpawn(1, specId, count) });

    private static ModeSpec Mode(params ScriptedStage[] scripted) => new ModeSpec(
        ModeId,
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        Scalings.Design(),
        Scalings.Xp(),
        new[] { new RosterEntry(Frog, 1), new RosterEntry(Pitcher, 2) },
        bossRoster: new[] { new BossRosterEntry(new ContentId("boss.warden"), 5) },
        scriptedStages: scripted);

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        Array.Empty<CharacterSpec>(),
        new[] { Archetype(Frog, xpValue: 10f), Archetype(Pitcher, xpValue: 21f) },
        new[] { mode });

    private static EnemySpec Archetype(ContentId id, float xpValue) => new EnemySpec(
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
        behaviour: EnemyBehaviourKind.Chaser);
}
