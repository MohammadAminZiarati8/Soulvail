using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>ST-03a: poison stacks, shares a timer, and pulses through the damage door.</summary>
[TestFixture]
public sealed class PoisonTests
{
    private const string DummyId = "enemy.dummy";
    private const string BodyId = "enemy.boss_body";
    private const string BossId = "boss.test";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;

    private float _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, Capacity);
        _lanes = new LaneSystem(_events);
        _clock = 0f;
    }

    [Test]
    public void Rule_DebuffPriority25_FullOnBoss()
    {
        StatusRule rule = _system.Statuses.Poisoned;

        Assert.That(rule.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(rule.Priority, Is.EqualTo(25));
        Assert.That(rule.BossScale, Is.EqualTo(1f));
        Assert.That(rule.Id.Value, Is.EqualTo(EnemyStatuses.PoisonedKey));
    }

    [Test]
    public void Apply_AddsStacks_UpToCap()
    {
        EnemyAgent dummy = Dummy();

        for (int i = 0; i < 6; i++)
        {
            Assert.That(_system.Poison(dummy.Id, 1f, 4f), Is.True);
        }

        Assert.That(_system.PoisonStacks(dummy.Id), Is.EqualTo(Poison.MaxStacks));
        Assert.That(_system.PoisonStacks(-1), Is.Zero);
    }

    [Test]
    public void Apply_RefreshesToLonger()
    {
        EnemyAgent dummy = Dummy();

        _system.Poison(dummy.Id, 1f, 4f);
        _system.Poison(dummy.Id, 1f, 2f);

        Assert.That(dummy.Poison.Remaining, Is.EqualTo(4f).Within(Tolerance));
    }

    [Test]
    public void Apply_PastCap_StillRefreshes()
    {
        EnemyAgent dummy = Dummy();

        for (int i = 0; i < Poison.MaxStacks; i++)
        {
            _system.Poison(dummy.Id, 0.1f, 4f);
        }

        Tick(3f);
        Assert.That(dummy.Poison.Remaining, Is.EqualTo(1f).Within(Tolerance));

        _system.Poison(dummy.Id, 0.1f, 3f);

        Assert.That(dummy.Poison.Stacks, Is.EqualTo(Poison.MaxStacks));
        Assert.That(dummy.Poison.Remaining, Is.EqualTo(3f).Within(Tolerance));
    }

    [Test]
    public void Dps_StrongestWins_WeakerStillStacks()
    {
        EnemyAgent dummy = Dummy();

        _system.Poison(dummy.Id, 4f, 4f);
        _system.Poison(dummy.Id, 2f, 4f);

        Assert.That(dummy.Poison.Stacks, Is.EqualTo(2));
        Assert.That(dummy.Poison.DamagePerStackPerSecond, Is.EqualTo(4f));
    }

    [Test]
    public void Pulse_DamageIsStacksTimesDpsTimesHalfSecond()
    {
        EnemyAgent dummy = Dummy();

        for (int i = 0; i < 3; i++)
        {
            _system.Poison(dummy.Id, 4f, 4f);
        }

        Tick(0.5f);

        IReadOnlyList<EnemyDamaged> damaged = _events.Of<EnemyDamaged>();

        Assert.That(damaged, Has.Count.EqualTo(1));
        Assert.That(damaged[0].Amount, Is.EqualTo(6f).Within(Tolerance));
    }

    [Test]
    public void Pulse_EndsWithTimer()
    {
        EnemyAgent dummy = Dummy();

        _system.Poison(dummy.Id, 1f, 1.2f);

        for (int i = 0; i < 4; i++)
        {
            Tick(0.5f);
        }

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(2));
        Assert.That(_system.PoisonStacks(dummy.Id), Is.Zero);
    }

    [Test]
    public void Pulse_StunnedStillPoisoned()
    {
        EnemyAgent dummy = Dummy();

        _system.Stun(dummy.Id, 3f);
        _system.Poison(dummy.Id, 4f, 3f);

        Tick(0.5f);

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(1));
    }

    [Test]
    public void Pulse_Boss_FullDuration()
    {
        EnemyAgent boss = _system.SpawnBoss(new ContentId(BossId), Vector3.Zero);

        _system.Poison(boss.Id, 1f, 4f);

        Assert.That(boss.Poison.Remaining, Is.EqualTo(4f).Within(Tolerance));
    }

    [Test]
    public void Changed_StacksAndExpiry()
    {
        EnemyAgent dummy = Dummy();

        _system.Poison(dummy.Id, 1f, 4f);
        _system.Poison(dummy.Id, 1f, 4f);

        for (int i = 0; i < 9; i++)
        {
            Tick(0.5f);
        }

        EnemyStatusChanged[] poisoned = _events.Of<EnemyStatusChanged>()
            .Where(e => e.Status.Value == EnemyStatuses.PoisonedKey)
            .ToArray();

        Assert.That(poisoned.Select(e => e.Stacks), Is.EqualTo(new[] { 1, 2, 0 }));
        Assert.That(poisoned[0].Seconds, Is.EqualTo(4f).Within(Tolerance));
        Assert.That(poisoned[2].Seconds, Is.Zero);
    }

    [Test]
    public void Death_ClearsSilently()
    {
        EnemyAgent dummy = Dummy();

        _system.Poison(dummy.Id, 4f, 4f);
        _system.ApplyDamage(dummy.Id, 9999f, _clock, _player);

        int damaged = _events.Count<EnemyDamaged>();
        int changed = _events.Count<EnemyStatusChanged>();

        for (int i = 0; i < 10; i++)
        {
            Tick(0.5f);
        }

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(damaged), "No pulse after the death.");
        Assert.That(_events.Count<EnemyStatusChanged>(), Is.EqualTo(changed), "No poisoned event after the death.");
        Assert.That(dummy.Poison.Stacks, Is.Zero);
    }

    [Test]
    public void Kill_ByPoison_PaysLikeAHit()
    {
        EnemyAgent dummy = Dummy();

        _system.ApplyDamage(dummy.Id, dummy.Health.MaxHp.Value - 1f, _clock, _player);
        _system.Poison(dummy.Id, 4f, 4f);

        Tick(0.5f);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(_system.PendingXp, Is.EqualTo(12f));
        Assert.That(_system.PendingKills, Is.EqualTo(1));
    }

    [Test]
    public void Tick_Poisoned_NoAllocation()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);

        for (int i = 0; i < 20; i++)
        {
            EnemyAgent agent = system.Spawn(new ContentId(DummyId), new Vector3(i, 0f, 6f));

            system.Poison(agent.Id, 0.01f, 1000f);
        }

        float clock = 0f;
        const float frame = 1f / 60f;

        for (int i = 0; i < 60; i++)
        {
            clock += frame;
            system.Tick(new EnemyTickContext(frame, clock, player, intents, silent, projectiles, system, lanes));
        }

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                intents.Clear();
                clock += frame;
                system.Tick(new EnemyTickContext(frame, clock, player, intents, silent, projectiles, system, lanes));
            }
        });
    }

    private EnemyAgent Dummy() => _system.Spawn(new ContentId(DummyId), Vector3.Zero);

    private void Tick(float dt)
    {
        _clock += dt;
        _system.Tick(new EnemyTickContext(dt, _clock, _player, _intents, _events, _projectiles, _system, _lanes));
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Body(DummyId, EnemyBehaviourKind.Static, 36f), Body(BodyId, EnemyBehaviourKind.Boss, 900f) },
        bosses: new[] { Boss() });

    private static EnemySpec Body(string id, EnemyBehaviourKind behaviour, float maxHp) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: maxHp,
        moveSpeed: 1f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: behaviour);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId),
        new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) },
        1.5f);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        null,
        0.5f);
}
