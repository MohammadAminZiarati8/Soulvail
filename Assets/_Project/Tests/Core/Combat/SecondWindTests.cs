using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
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

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class SecondWindTests
{
    private const int Capacity = 8;
    private const float RollDuration = 0.5f;
    private const float Cooldown = 2.5f;

    private readonly ContentId _guardStatus = new("status.second-wind");
    private readonly ContentId _debuff = new("status.test-slow");
    private RecordingEvents _events;
    private WorldSnapshot _snapshot;
    private PlayerCombat _player;
    private StatusBoard _status;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _snapshot = new WorldSnapshot(Capacity);
        _player = CreatePlayer(_events);
        _status = new StatusBoard();
        _player.AttachStatus(_status);
    }

    [Test]
    public void Rank1_CooldownHalfSecondShorter() => AssertCooldown(rank: 1, expected: 2f);

    [Test]
    public void Rank2_OneSecondShorter() => AssertCooldown(rank: 2, expected: 1.5f);

    [Test]
    public void NoRank_OneCharge_AsToday()
    {
        ChargeSkill charge = _player.Charge;
        Assert.That(charge.MaxCharges, Is.EqualTo(1));
        Assert.That(charge.ReadyCharges, Is.EqualTo(1));
        Roll(0f);
        Step(0.75f);
        Assert.That(charge.ReadyCharges, Is.Zero);
        Assert.That(charge.IsReady, Is.False);
        Assert.That(charge.IsGuarding, Is.False);
        Step(Cooldown);
        Assert.That(charge.ReadyCharges, Is.EqualTo(1));
        Assert.That(charge.IsReady, Is.True);
    }

    [Test]
    public void TwoCharges_RollTwiceBackToBack()
    {
        GrantRank(3);
        Roll(0f);
        Assert.That(_player.Charge.IsReady, Is.False);
        Roll(RollDuration);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(2));
        Assert.That(_player.Charge.ReadyCharges, Is.Zero);
    }

    [Test]
    public void Charges_RefillOneAtATime()
    {
        GrantRank(3);
        Roll(0f);
        Roll(RollDuration);
        Step(1.5f);
        Assert.That(_player.Charge.ReadyCharges, Is.EqualTo(1));
        Assert.That(_player.Charge.IsReady, Is.True);
        Step(2.99f);
        Assert.That(_player.Charge.ReadyCharges, Is.EqualTo(1));
        Step(3f);
        Assert.That(_player.Charge.ReadyCharges, Is.EqualTo(2));
    }

    [Test]
    public void CooldownFraction_ReportsRefillingCharge()
    {
        GrantRank(3);
        Roll(0f);
        Roll(RollDuration);
        Step(0.75f);
        Assert.That(_player.Charge.CooldownFraction, Is.EqualTo(0.5f));
        Step(1.5f);
        Assert.That(_player.Charge.IsReady, Is.True);
        Assert.That(_player.Charge.CooldownFraction, Is.EqualTo(1f));
        Step(2.25f);
        Assert.That(_player.Charge.CooldownFraction, Is.EqualTo(0.5f));
        Step(3f);
        Assert.That(_player.Charge.CooldownFraction, Is.Zero);
    }

    [Test]
    public void ChargesChanged_Publishes()
    {
        GrantRank(3);
        Roll(0f);
        Roll(RollDuration);
        Step(0.75f);
        Step(3f);
        _player.Charge.BonusCharges.RemoveAll(this);

        var events = _events.Of<ChargeChargesChanged>();
        Assert.That(events.Count, Is.EqualTo(6));
        Assert.That((events[0].Ready, events[0].Max), Is.EqualTo((2, 2)));
        Assert.That((events[1].Ready, events[1].Max), Is.EqualTo((1, 2)));
        Assert.That((events[2].Ready, events[2].Max), Is.EqualTo((0, 2)));
        Assert.That((events[3].Ready, events[3].Max), Is.EqualTo((1, 2)));
        Assert.That((events[4].Ready, events[4].Max), Is.EqualTo((2, 2)));
        Assert.That((events[5].Ready, events[5].Max), Is.EqualTo((1, 1)));
    }

    [Test]
    public void Guard_BlocksFirstHitAfterRoll()
    {
        GrantRank(2);
        Roll(0f);
        Step(RollDuration);
        Assert.That(_player.ApplyDamage(10f, RollDuration).Blocked, Is.True, "Roll i-frames still cover the trail.");
        Assert.That(_player.Charge.IsGuarding, Is.True);
        Step(0.75f);
        Assert.That(_player.ApplyDamage(0f, 0.75f).Blocked, Is.False);
        Assert.That(_player.ApplyDamage(10f, 0.75f).Blocked, Is.True);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(2));
        Assert.That(_events.Of<PlayerDamaged>()[1].Blocked, Is.True);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_player.Charge.IsGuarding, Is.False);
    }

    [Test]
    public void Guard_SecondHitLands()
    {
        OpenGuard();
        Assert.That(_player.ApplyDamage(10f, 0.75f).Blocked, Is.True);
        Assert.That(_player.ApplyDamage(10f, 0.75f).ToHp, Is.EqualTo(10f));
    }

    [Test]
    public void Guard_ExpiresAfterOneSecond()
    {
        OpenGuard();
        Step(1.49f);
        Assert.That(_player.Charge.IsGuarding, Is.True);
        Step(1.5f);
        Assert.That(_player.Charge.IsGuarding, Is.False);
        Assert.That(_player.ApplyDamage(10f, 1.5f).ToHp, Is.EqualTo(10f));
    }

    [Test]
    public void Guard_RefusesDebuffs()
    {
        OpenGuard();
        _status.Post(_debuff, isBuff: false, 2f, stacks: 1);
        Assert.That(_status.Count, Is.EqualTo(1));
        Assert.That(_status[0].Id, Is.EqualTo(_guardStatus));

        _player.ApplyDamage(10f, 0.75f);
        _status.Post(_debuff, isBuff: false, 2f, stacks: 1);
        Assert.That(_status.Count, Is.EqualTo(1));
        Assert.That(_status[0].Id, Is.EqualTo(_debuff));
    }

    [Test]
    public void Guard_PostsAndClearsStatus()
    {
        OpenGuard();
        Assert.That(_status.Count, Is.EqualTo(1));
        Assert.That(_status[0].Id, Is.EqualTo(_guardStatus));
        Assert.That(_status[0].IsBuff, Is.True);
        Assert.That(_status[0].Remaining, Is.EqualTo(0.75f));
        _player.ApplyDamage(10f, 0.75f);
        Assert.That(_status.Count, Is.Zero);

        Roll(1.5f);
        Step(2f);
        Assert.That(_status[0].Remaining, Is.EqualTo(1f));
        Step(3f);
        Assert.That(_status.Count, Is.Zero);
    }

    [Test]
    public void Guard_NewRollRestartsWindow()
    {
        GrantRank(3);
        Roll(0f);
        Step(0.75f);
        Assert.That(_player.Charge.IsGuarding, Is.True);
        Roll(0.75f);
        Assert.That(_player.Charge.IsGuarding, Is.False);
        Assert.That(_status.Count, Is.Zero);
        Step(1.5f);
        Assert.That(_player.Charge.IsGuarding, Is.True);
        Step(2.25f);
        Assert.That(_player.Charge.IsGuarding, Is.False);
    }

    [Test]
    public void StageBoundary_RefillsAndCloses()
    {
        CharacterSpec ranger = Ranger();
        SkillSpec secondWind = Passive("skill.test.second-wind",
            new ModifyStat(PlayerStat.MovementSkillCooldown, ModifierKind.Flat, -1f),
            new ModifyStat(PlayerStat.RollGuardSeconds, ModifierKind.Flat, 1f),
            new ModifyStat(PlayerStat.MovementSkillCharges, ModifierKind.Flat, 1f));
        SkillSpec fillerA = Passive("skill.test.a", new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f));
        SkillSpec fillerB = Passive("skill.test.b", new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f));
        var tree = new SkillTreeSpec(new ContentId("tree.test"), ranger.Id,
            new[] { Branch(secondWind.Id), Branch(fillerA.Id), Branch(fillerB.Id) });
        var body = new EnemySpec(new ContentId("enemy.test"), new LocKey("enemy.test.name"),
            100f, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
        var scaling = new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(Capacity, 1000), new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"), 1, true, 0,
            scaling, Scalings.Xp(), new[] { new RosterEntry(body.Id, 1) });
        var catalog = new ContentCatalog(new[] { ranger }, new[] { body }, new[] { mode },
            new[] { secondWind, fillerA, fillerB }, new[] { tree });
        var random = new FixedRandom(99);
        var session = new RunSession(catalog, random, _events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, ranger.Id, 99, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { secondWind.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, ranger.Id, 99, 1, SpawnPlan.Empty, saved));
        _snapshot.Dt = 0.125f;
        _snapshot.SpawnPoints = new[] { new Vector3(0f, 0f, 7f) };
        for (int i = 0; i < 160 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(_snapshot);
        }

        EnemySpawned enemy = _events.Single<EnemySpawned>();
        session.MovementSkill();
        for (int i = 0; i <= 4; i++)
        {
            session.Tick(_snapshot);
        }

        var charges = _events.Of<ChargeChargesChanged>();
        Assert.That(charges[^1].Ready, Is.EqualTo(1));
        Assert.That(session.State.StatusAt(0).Id, Is.EqualTo(_guardStatus));
        Assert.That(session.State.StatusAt(0).Remaining, Is.EqualTo(1f));
        _snapshot.EnemyCount = 1;
        _snapshot.Enemies[0] = new EnemySense { Id = enemy.Id, Position = enemy.Position, HasLineOfSight = true };
        var hits = new[] { enemy.Id };
        for (int i = 0; i < 7 && _events.Count<StageCleared>() == 0; i++)
        {
            session.Tick(_snapshot);
            session.ReportConeHits(hits);
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
        Assert.That(_events.Of<ChargeChargesChanged>()[^1].Ready, Is.EqualTo(2));
        Assert.That(session.State.MovementSkillCooldownFraction, Is.Zero);
        Assert.That(session.State.StatusCount, Is.Zero);
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var intents = new RecordingIntents();
        _player = new PlayerCombat(Ranger(), new SilentEvents(), intents, Capacity);
        _player.AttachStatus(_status);
        GrantRank(3);
        float now = 0f;
        AllocationAssert.None(() =>
        {
            Roll(now);
            Step(now + 0.75f);
            Roll(now + 0.75f);
            Step(now + 1.5f);
            _player.ApplyDamage(10f, now + 1.5f);
            Step(now + 3f);
            intents.Clear();
            now += 4f;
        });
    }

    private void AssertCooldown(int rank, float expected)
    {
        GrantRank(rank);
        Roll(0f);
        Step(expected - 0.01f);
        Assert.That(_player.Charge.IsReady, Is.False);
        Step(expected);
        Assert.That(_player.Charge.IsReady, Is.True);
    }

    private void OpenGuard()
    {
        GrantRank(2);
        Roll(0f);
        Step(0.75f);
    }

    private void GrantRank(int rank)
    {
        var stats = new PlayerStats(_player, new PlayerMotor(Ranger().Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        stats.Resolve(PlayerStat.MovementSkillCooldown).Add(new Modifier(ModifierKind.Flat, -0.5f, this));
        if (rank >= 2)
        {
            stats.Resolve(PlayerStat.MovementSkillCooldown).Add(new Modifier(ModifierKind.Flat, -0.5f, this));
            stats.Resolve(PlayerStat.RollGuardSeconds).Add(new Modifier(ModifierKind.Flat, 1f, this));
        }

        if (rank >= 3)
        {
            stats.Resolve(PlayerStat.MovementSkillCharges).Add(new Modifier(ModifierKind.Flat, 1f, this));
        }
    }

    private void Roll(float now)
    {
        _player.Charge.Request(now);
        Step(now);
    }

    private void Step(float now) =>
        _player.Tick(0f, now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);

    private static PlayerCombat CreatePlayer(IDomainEvents events) =>
        new(Ranger(), events, new RecordingIntents(), Capacity);

    private static SkillSpec Passive(string id, params IEffect[] effects) =>
        new(new ContentId(id), new LocKey("skill.test.name"), new LocKey("skill.test.desc"), SkillKind.Passive, effects);

    private static SkillBranchSpec Branch(ContentId id) =>
        new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });

    private static CharacterSpec Ranger() => new(new ContentId("character.ranger"), new LocKey("character.ranger.name"),
        new LocKey("character.ranger.desc"), 100f, new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 150f, 1.5f, 12f, 360f, 0.15f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, RollDuration, Cooldown, 0.15f, 0f, 0f, 0.05f), null, 0.5f);
}
