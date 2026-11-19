using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class ShardbackChargeTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private ShardbackStats _stats;
    private ShardbackBehaviour _boss;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        CharacterSpec character = ShardbackBehaviourTests.Character();
        EnemySpec body = ShardbackBehaviourTests.Body();
        _player = new PlayerCombat(character, _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        _enemies = new EnemySystem(new ContentCatalog(new[] { character }, new[] { body }),
            _events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(body, Vector3.Zero);
        _stats = new ShardbackStats();
        _boss = new ShardbackBehaviour(_agent, _stats);
        _now = 0f;
    }

    [Test]
    public void Charge_ObservedSweep_StopsDamage()
    {
        LaneProbeIntent probe = ArmCharge();
        Assert.That(probe.Direction, Is.EqualTo(Vector3.UnitZ));
        Assert.That(probe.Reach, Is.EqualTo(12f));
        Assert.That(probe.Radius, Is.EqualTo(1f));
        Answer(probe, ShardbackStopKind.Cover, 8f);
        _stats.ChargeLength.Base = 1f;
        _stats.ChargeHalfWidth.Base = 0.5f;
        _stats.ChargeSpeed.Base = 1f;
        _stats.ChargeDamage.Base = 1f;
        _stats.ChargeRecoverySeconds.Base = 1f;

        _agent.Blackboard.PlayerPosition = new Vector3(0f, 90f, 5f);
        Tick(1.2f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.UnitZ * (8f / 1.2f)));
        ShardbackClipped clip = _events.Of<ShardbackClipped>().Last();
        Assert.That((clip.BossId, clip.Key, clip.Centre, clip.Plus, clip.Minus),
            Is.EqualTo((_agent.Id, probe.Key, 8f, 0f, 0f)));
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "The tell has no hit.");
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);

        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 2f;
        Tick(0.1f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.UnitZ * 10f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "The requested travel outran the body.");

        _agent.Blackboard.PlayerPosition = new Vector3(1f, 80f, 4f);
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 6f;
        Tick(0.5f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.UnitZ * 4f));
        Assert.That(_player.Health.Current, Is.EqualTo(82f), "The body swept across the hero at the strip edge.");

        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 7f;
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 10f;
        Tick(1f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.Zero));
        Assert.That(_player.Health.Current, Is.EqualTo(82f), "The charge can hit only once even after i-frames.");
        ShardbackImpact impact = _events.Single<ShardbackImpact>();
        Assert.That((impact.Move, impact.Key, impact.RecoverySeconds),
            Is.EqualTo((ShardbackMove.Charge, probe.Key, 1.5f)));
    }

    [TestCase(ShardbackStopKind.None, 12f, 12f, 0f, true)]
    [TestCase(ShardbackStopKind.Cover, 6f, 6f, 0f, false)]
    [TestCase(ShardbackStopKind.Boundary, 6f, 6f, 0f, false)]
    [TestCase(ShardbackStopKind.Unknown, 0f, 0f, 0f, false)]
    [TestCase(ShardbackStopKind.Cover, 6f, 5f, 0f, true)]
    [TestCase(ShardbackStopKind.Boundary, 6f, 5f, 0f, true)]
    [TestCase(ShardbackStopKind.Boundary, 6f, 7f, 0f, false)]
    [TestCase(ShardbackStopKind.None, 12f, 6f, 1.001f, false)]
    public void Charge_EndpointsAndSidestep(ShardbackStopKind kind, float stop, float heroZ, float heroX, bool hit)
    {
        LaneProbeIntent probe = ArmCharge();
        Answer(probe, kind, stop);
        _agent.Blackboard.PlayerPosition = new Vector3(heroX, 0f, heroZ);
        Tick(1.2f);
        if (kind != ShardbackStopKind.Unknown)
            MoveTo(13f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 82f : 100f));
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(1.5f));
        Assert.That(_events.Count<ShardbackPillarBroken>(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Charge_Protection_UsesPlayerCombat(bool decoy)
    {
        LaneProbeIntent probe = ArmCharge();
        Answer(probe, ShardbackStopKind.None, probe.Reach);
        Tick(1.2f);
        _agent.Blackboard.QuarryIsADecoy = decoy;
        if (!decoy)
            _player.ApplyDamage(1f, _now);
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 3f;
        MoveTo(4f);
        Assert.That(_player.Health.Current, Is.EqualTo(decoy ? 100f : 99f));
    }

    [Test]
    public void Charge_StallShortOfPillar_RecoversWithoutBreak()
    {
        LaneProbeIntent probe = ArmCharge();
        Answer(probe, ShardbackStopKind.Pillar, 6f, 0);
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 5f;
        Tick(1.2f);
        MoveTo(2f);
        Tick(0.125f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);
        Tick(0.125f);
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(1.5f));
        Assert.That(_events.Count<ShardbackPillarBroken>(), Is.Zero);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(Velocity(), Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Charge_WorldEndpointRoundsShort_StillReachesPillar()
    {
        LaneProbeIntent probe = ArmCharge(_player, _events, originZ: 100f);
        _events.Clear();
        Answer(probe, ShardbackStopKind.Pillar, 3.1f, 0);
        Tick(1.2f);
        MoveTo(103.1f);
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(3f));
        Assert.That(_events.Single<ShardbackPillarBroken>().Slot, Is.Zero);
    }

    [Test]
    public void Pillars_FirstOnly_PersistAndReset()
    {
        LaneProbeIntent first = ArmCharge(_player, _events);
        var near = new ShardbackStop(_agent.Id, first.Key, 0, ShardbackStopKind.Pillar, 0f, 0);
        _boss.OnShardbackStop(near);
        Answer(first, ShardbackStopKind.Pillar, 6f, 1);
        Tick(1.2f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Of<ShardbackImpact>().Last().RecoverySeconds, Is.EqualTo(3f));
        Assert.That(_events.Single<ShardbackPillarBroken>().Slot, Is.Zero);
        Assert.That(_events.Single<ShardbackPillarBroken>().Lifetime, Is.EqualTo(first.Key.Lifetime));
        _boss.OnShardbackStop(near);
        Tick(2.875f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(2));
        Tick(0.125f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(3));

        _boss.OnPhaseEntered(1);
        LaneProbeIntent second = ArmCharge(_player, _events);
        Assert.That(second.Key.Lifetime, Is.EqualTo(first.Key.Lifetime));
        Assert.That(second.Key.Cast, Is.GreaterThan(first.Key.Cast));
        Answer(second, ShardbackStopKind.Pillar, 0f, 0);
        Tick(10f);
        Assert.That(_events.Count<ShardbackPillarBroken>(), Is.EqualTo(1));
        Assert.That(Velocity(), Is.EqualTo(Vector3.Zero));
        Answer(second, ShardbackStopKind.Pillar, 6f, 1);
        Tick(0.1f);
        Assert.That(Velocity(), Is.EqualTo(Vector3.UnitZ * 10f));
        MoveTo(6f);
        Assert.That(_events.Of<ShardbackPillarBroken>().Select(e => e.Slot), Is.EqualTo(new[] { 0, 1 }));

        _boss.Reset();
        LaneProbeIntent fresh = ArmCharge(_player, _events);
        Assert.That(fresh.Key.Lifetime, Is.Not.EqualTo(first.Key.Lifetime));
        Assert.That(fresh.EnemyId, Is.EqualTo(first.EnemyId));
        _boss.OnShardbackStop(near);
        Answer(fresh, ShardbackStopKind.Pillar, 0f, 0);
        Tick(1.2f);
        Assert.That(_events.Of<ShardbackPillarBroken>().Select(e => e.Slot), Is.EqualTo(new[] { 0, 1, 0 }));
        Assert.That(_events.Of<ShardbackFightStarted>().Select(e => e.Lifetime),
            Is.EqualTo(new[] { first.Key.Lifetime, fresh.Key.Lifetime }));
    }

    [Test]
    public void Fight_AllSpent_CompletesCycles()
    {
        RunAllSpent(_player, _events);
        Assert.That(_events.Of<ShardbackPillarBroken>().Select(e => e.Slot), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(_events.Of<ShardbackImpact>().Select(e => e.Move), Is.EqualTo(new[]
        {
            ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Fan, ShardbackMove.Charge,
            ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Burst,
            ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Burst,
            ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Burst
        }));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.Health.ApplyDamage(1f, _now).Applied, Is.EqualTo(1f));

        var silent = new SilentEvents();
        var player = new PlayerCombat(ShardbackBehaviourTests.Character(), silent, _intents, 8);
        AllocationAssert.None(() => RunAllSpent(player, silent), 100);
    }

    private void RunAllSpent(PlayerCombat player, IDomainEvents events)
    {
        _boss.Reset();
        _now = 0f;
        for (int slot = 0; slot < 3; slot++)
        {
            _boss.OnPhaseEntered(slot);
            LaneProbeIntent pillar = ArmCharge(player, events);
            Answer(pillar, ShardbackStopKind.Pillar, 0f, slot);
            Step(1.2f, player, events);
        }
        Step(3f, player, events);
        Step(1.2f, player, events);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            LaneProbeIntent probe = ArmCharge(player, events, 2f, missFan: true);
            for (int slot = 0; slot < 3; slot++)
                Answer(probe, ShardbackStopKind.Pillar, 0f, slot);
            Answer(probe, ShardbackStopKind.None, probe.Reach);
            _agent.Blackboard.PlayerPosition = new Vector3(2f, 0f, 8f);
            Step(1.2f, player, events);
            _agent.Blackboard.SelfPosition = Vector3.UnitZ * 12f;
            Step(1.2f, player, events);
            _agent.Blackboard.PlayerPosition = _agent.Blackboard.SelfPosition + Vector3.UnitZ * 8f;
            Step(1.5f, player, events);
            Step(1.2f, player, events);
        }
    }

    private LaneProbeIntent ArmCharge()
    {
        LaneProbeIntent probe = ArmCharge(_player, _events);
        _events.Clear();
        return probe;
    }

    private LaneProbeIntent ArmCharge(PlayerCombat player, IDomainEvents events, float recovery = 0f,
        bool missFan = false, float originZ = 0f)
    {
        Vector3 origin = Vector3.UnitZ * originZ;
        _agent.Blackboard.SelfPosition = origin;
        _agent.Blackboard.PlayerPosition = origin + Vector3.UnitZ * 8f;
        Step(recovery, player, events);
        for (int lane = 0; lane < 3; lane++)
        {
            LaneProbeIntent fan = _intents.LaneProbes[lane];
            Answer(fan, missFan && lane == 0 ? ShardbackStopKind.Cover : ShardbackStopKind.None,
                missFan && lane == 0 ? 3f : fan.Reach);
        }
        _agent.Blackboard.PlayerPosition = origin + (missFan ? new Vector3(2f, 0f, 8f) : Vector3.UnitX * 8f);
        Step(1f, player, events);
        _agent.Blackboard.PlayerPosition = origin + Vector3.UnitZ * 8f;
        Step(1.25f, player, events);
        return _intents.LaneProbes[0];
    }

    private void Answer(in LaneProbeIntent probe, ShardbackStopKind kind, float distance, int slot = -1) =>
        _boss.OnShardbackStop(new ShardbackStop(probe.EnemyId, probe.Key, probe.Lane, kind, distance, slot));

    private void MoveTo(float z)
    {
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * z;
        Tick(0.1f);
    }

    private Vector3 Velocity() => _intents.LastEnemyMove.Velocity;
    private void Tick(float dt) => Step(dt, _player, _events);

    private void Step(float dt, PlayerCombat player, IDomainEvents events)
    {
        _intents.Clear();
        _now += dt;
        _boss.Tick(new EnemyTickContext(dt, _now, player, _intents, events, _projectiles, _enemies));
    }
}
