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
public sealed class KillshotTests
{
    private const int Capacity = 32;
    private const int MinHits = 3;
    private const float Hp = 1_000f;
    private static readonly ContentId SkillId = new("skill.killshot");
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private PlayerMotor _motor;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private SimulatedClock _clock;
    private KillshotHandler _handler;
    private EffectRegistry _effects;
    private SkillRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, Capacity);
        _motor = new PlayerMotor(Character().Movement, Vector3.UnitZ);
        _stats = new PlayerStats(_player, _motor, new LevelTracker(Scalings.Xp(), _events));
        _enemies = Enemies(_events);
        _clock = new SimulatedClock();
        _handler = new KillshotHandler(_enemies, _player, _motor, _stats, _clock, _events);
        _effects = new EffectRegistry();
        _effects.Register<Killshot>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, _events);
        _runner.Add(Skill());
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
        _events.Clear();
    }

    [Test]
    public void Pick_MostCrowdedLine()
    {
        var points = new[] { Point(1, 1f, 0f), Point(2, 0f, 3f), Point(3, 0f, 5f), Point(4, 0f, 8f) };
        Assert.That(KillshotLine.TryPick(points, Vector3.Zero, 0.5f, 30f, out Vector3 direction), Is.True);
        Assert.That(direction, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void Pick_TieBreaks()
    {
        var points = new[] { Point(1, 0f, 8f), Point(8, 4f, 0f), Point(2, -4f, 0f) };
        KillshotLine.TryPick(points, Vector3.Zero, 0.5f, 30f, out Vector3 direction);
        Assert.That(direction, Is.EqualTo(-Vector3.UnitX));
        Array.Reverse(points);
        KillshotLine.TryPick(points, Vector3.Zero, 0.5f, 30f, out Vector3 reversed);
        Assert.That(reversed, Is.EqualTo(direction));
    }

    [Test]
    public void Pick_XZForwardCorridor_ClosedEdges()
    {
        EnemyAgent aim = Spawn(4f);
        Cast();
        EnemyAgent edge = Spawn(30f, 1.5f, 100f);
        EnemyAgent outside = Spawn(30f, 1.501f);
        EnemyAgent beyond = Spawn(30.001f);
        EnemyAgent behind = Spawn(-0.001f);
        Fire();
        Assert.That(aim.Health.Current, Is.LessThan(Hp));
        Assert.That(edge.Health.Current, Is.LessThan(Hp));
        Assert.That(outside.Health.Current, Is.EqualTo(Hp));
        Assert.That(beyond.Health.Current, Is.EqualTo(Hp));
        Assert.That(behind.Health.Current, Is.EqualTo(Hp));
        Assert.That(_events.Single<KillshotFired>().Hits, Is.EqualTo(2));
    }

    [Test]
    public void Pick_OverlappingCandidates_StableAcrossInputOrder()
    {
        var points = new[] { Point(1, 4f, 0f), Point(2, 8f, 0.3f) };
        KillshotLine.TryPick(points, Vector3.Zero, 0.5f, 30f, out Vector3 direction);
        Array.Reverse(points);
        KillshotLine.TryPick(points, Vector3.Zero, 0.5f, 30f, out Vector3 reversed);
        Assert.That(direction, Is.EqualTo(Vector3.UnitX));
        Assert.That(reversed, Is.EqualTo(direction));
        Assert.That(KillshotLine.TryPick(new[] { Point(1, 0f, 0f) }, Vector3.Zero,
            0.5f, 30f, out Vector3 overlap), Is.True);
        Assert.That(overlap, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void MotorHold_Expires_AndStatsRejectEnemyAddresses()
    {
        _motor.Hold(0.1f);
        _motor.Tick(0.1f, Vector2.UnitX, null);
        Assert.That(_motor.Velocity, Is.EqualTo(Vector3.Zero));
        _motor.Tick(0.1f, Vector2.UnitX, null);
        Assert.That(_motor.Velocity.Length(), Is.GreaterThan(0f));
        var enemyStats = new CombatantStats(Spawn(3f));
        foreach (PlayerStat stat in new[] { PlayerStat.KillshotExecute, PlayerStat.KillshotDamage })
        {
            Assert.That(_stats.Has(stat), Is.True);
            Assert.That(_stats.Resolve(stat).Value, Is.Zero);
            Assert.That(enemyStats.Has(stat), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => enemyStats.Resolve(stat));
        }
    }

    [Test]
    public void NoEnemy_Refused()
    {
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.False);
        EnemyAgent corpse = Spawn(3f);
        _enemies.ApplyDamage(corpse.Id, Hp, 0f, _player);
        Spawn(30.01f);
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_events.Count<KillshotWindUp>(), Is.Zero);
        Spawn(4f);
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
    }

    [Test]
    public void CanCast_ThreeInLine_True()
    {
        Spawn(4f);
        Spawn(8f);
        Spawn(12f, y: 100f);
        Spawn(0f, 10f);
        Assert.That(_handler.CanCast(Shot()), Is.True);
    }

    [Test]
    public void CanCast_TwoLeftBothInLine_True()
    {
        Spawn(4f);
        Spawn(8f);
        Assert.That(_handler.CanCast(Shot()), Is.True);
    }

    [Test]
    public void CanCast_OneLeft_True()
    {
        Guard(false);
        Assert.That(_handler.CanCast(Shot()), Is.True);
    }

    [Test]
    public void CanCast_ThreeInReachBestLineTwo_False()
    {
        Spawn(4f);
        Spawn(8f);
        Spawn(0f, 10f);
        Assert.That(_handler.CanCast(Shot()), Is.False);
    }

    [Test]
    public void CanCast_FarBodyIgnored_True()
    {
        Killshot shot = Shot();
        Spawn(4f);
        Spawn(0f, shot.Length + 5f);
        Assert.That(_handler.CanCast(shot), Is.True);
    }

    [Test]
    public void CanCast_DoesNotAllocate()
    {
        Killshot shot = Shot();
        Spawn(4f);
        Spawn(8f);
        Assert.That(_handler.CanCast(shot), Is.True);
        AllocationAssert.None(() => { _handler.CanCast(shot); });
    }

    [Test]
    public void Killshot_MinHitsBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Killshot(0.7f, 1.5f, 30f, 8f, 0.15f, 0.4f, 0.15f, 0));
    }

    [Test]
    public void Direction_LockedAtCast()
    {
        EnemyAgent moved = Spawn(3f);
        Cast();
        Move(moved, 0f, 3f);
        EnemyAgent arrival = Spawn(5f);
        Fire();
        Assert.That(moved.Health.Current, Is.EqualTo(Hp));
        Assert.That(arrival.Health.Current, Is.EqualTo(Hp - 136f));
        Assert.That(_events.Single<KillshotFired>().Direction, Is.EqualTo(Vector3.UnitX));
    }

    [Test]
    public void WindUp_HoldsPlayer_NoShots()
    {
        Spawn(3f);
        _motor.Tick(1f, Vector2.UnitX, null);
        Cast();
        Assert.That(_motor.Velocity, Is.EqualTo(Vector3.Zero));
        KillshotWindUp wind = _events.Single<KillshotWindUp>();
        Assert.That(wind.Seconds, Is.EqualTo(0.7f));
        Assert.That(wind.Direction, Is.EqualTo(Vector3.UnitX));
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.05f };
        for (int i = 1; i <= 13; i++)
        {
            float now = i * 0.05f;
            _player.Charge.Request(now);
            _player.Tick(0.05f, now, snapshot, _enemies.Registry.Alive, Vector3.UnitX);
            _handler.Tick(now);
            _motor.Tick(0.05f, Vector2.UnitX, null);
            Assert.That(_motor.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(_player.TryTakeShot(out _), Is.False);
            Assert.That(_player.Charge.IsActive, Is.False);
        }

        Assert.That(_events.Count<KillshotFired>(), Is.Zero);
        Fire();
        Assert.That(_player.IsHeld, Is.False);
        _motor.Tick(0.05f, Vector2.UnitX, null);
        Assert.That(_motor.Velocity.Length(), Is.GreaterThan(0f));
    }

    [Test]
    public void Immunity_StartsWithWindUp_AndEndsAtRelease()
    {
        Spawn(3f);
        Assert.That(_player.ApplyDamage(10f, -1f).ToHp, Is.EqualTo(10f));
        Cast();
        Assert.That(_player.ApplyDamage(10f, 0f).Blocked, Is.True);
        _handler.Tick(0.699f);
        Assert.That(_player.ApplyDamage(10f, 0.699f).Blocked, Is.True);
        Fire();
        Assert.That(_events.Count<KillshotFired>(), Is.EqualTo(1));
        Assert.That(_player.ApplyDamage(10f, 0.7f).ToHp, Is.EqualTo(10f));
    }

    [Test]
    public void Immunity_BlocksMeleeProjectilesBossAttacksAndHazards()
    {
        EnemyAgent attacker = Spawn(3f);
        Cast();
        Assert.That(_player.ApplyDamage(attacker.ContactDamage.Value, 0.2f).Blocked, Is.True);

        var projectiles = new ProjectileSystem(_events, Capacity);
        projectiles.Fire(new Projectile(attacker.Spec.Id, attacker.Id, Vector3.UnitX, Vector3.Zero,
            10f, 0.1f, 20f, ShotSide.AtPlayer), 0.2f);
        projectiles.Tick(0.4f, Vector3.Zero, _player, _enemies);

        var bossAttack = new ShockwaveSystem(_events);
        bossAttack.Emit(Vector3.UnitX, 10f, 5f, 20f, 1f, 0.4f);
        bossAttack.Tick(0.5f, Vector3.Zero, _player);

        var hazard = new FissureSystem(_events);
        hazard.Open(Vector3.Zero, 1f, 0.05f, 1f, 20f, 0.5f);
        hazard.Tick(0.6f, Vector3.Zero, _player);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(4));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void Immunity_RejectsNewDebuffsAndRefreshes_WhileAllowingBuffs()
    {
        var status = new StatusBoard();
        _player.AttachStatus(status);
        var stun = new ContentId(EnemyStatuses.StunnedKey);
        var slow = new ContentId(EnemyStatuses.SlowedKey);
        var root = new ContentId(EnemyStatuses.RootedKey);
        var poison = new ContentId(EnemyStatuses.PoisonedKey);
        var buff = new ContentId("status.test-buff");
        status.Post(poison, false, 2f, 1);

        Spawn(3f);
        Cast();
        status.Post(stun, false, 5f, 1);
        status.Post(slow, false, 5f, 1);
        status.Post(root, false, 5f, 1);
        status.Post(poison, false, 5f, 2);
        status.Post(buff, true, 5f, 1);

        Assert.That(status.Count, Is.EqualTo(2));
        Assert.That(status[0].Id, Is.EqualTo(poison));
        Assert.That(status[0].Remaining, Is.EqualTo(2f));
        Assert.That(status[0].Stacks, Is.EqualTo(1));
        Assert.That(status[1].Id, Is.EqualTo(buff));
        Fire();
        status.Post(stun, false, 5f, 1);
        Assert.That(status.Count, Is.EqualTo(3));
    }

    [Test]
    public void Immunity_ReusesBlockedPlayerDamageEvent()
    {
        Spawn(3f);
        Cast();
        _player.ApplyDamage(10f, 0.2f);
        PlayerDamaged blocked = _events.Single<PlayerDamaged>();
        Assert.That(blocked.Blocked, Is.True);
        Assert.That(blocked.ToHp, Is.Zero);
        Assert.That(blocked.ToShield, Is.Zero);
    }

    [Test]
    public void Immunity_CombinesWithRollProtection()
    {
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.01f };
        _player.Charge.Request(0f);
        _player.Tick(0.01f, 0f, snapshot, _enemies.Registry.Alive, Vector3.UnitX);
        Assert.That(_player.Charge.IsInvulnerable, Is.True);
        Spawn(3f);
        _handler.Apply(Shot(), this);
        Assert.That(_handler.IsWindingUp, Is.False);
        _handler.Clear();
        Assert.That(_player.Health.IsInvulnerable, Is.True);

        _player.Tick(0.01f, 0.51f, snapshot, _enemies.Registry.Alive, Vector3.UnitX);
        Assert.That(_player.Charge.IsActive, Is.False);
        Assert.That(_player.Charge.IsInvulnerable, Is.True);
        _clock.Now = 0.51f;
        _handler.Apply(Shot(), this);
        Assert.That(_handler.IsWindingUp, Is.True);
        _handler.Clear();
        Assert.That(_player.Health.IsInvulnerable, Is.True, "Clearing Killshot preserves the roll trail.");

        _handler.Apply(Shot(), this);
        _player.Tick(0.1f, 0.61f, snapshot, _enemies.Registry.Alive, Vector3.UnitX);
        Assert.That(_player.Charge.IsInvulnerable, Is.False);
        Assert.That(_player.Health.IsInvulnerable, Is.True, "The roll trail can expire during Killshot.");
        _handler.Tick(1.22f);
        Assert.That(_player.Health.IsInvulnerable, Is.False);
    }

    [Test]
    public void Fire_FalloffPerHit_FloorsAtMin()
    {
        var bodies = new EnemyAgent[7];
        for (int i = bodies.Length - 1; i >= 0; i--)
        { bodies[i] = Spawn(i + 1f); }
        Cast();
        Fire();
        for (int i = 0; i < bodies.Length; i++)
        {
            Assert.That(bodies[i].Health.Current,
                Is.EqualTo(Hp - 136f * MathF.Max(0.4f, 1f - 0.15f * i)).Within(0.001f));
            Assert.That(_events.Of<EnemyDamaged>()[i].Id, Is.EqualTo(bodies[i].Id));
        }
    }

    [Test]
    public void Fire_EqualDistance_UsesLowestId()
    {
        EnemyAgent first = Spawn(3f);
        EnemyAgent second = Spawn(3f);
        Cast();
        Fire();
        Assert.That(first.Health.Current, Is.EqualTo(Hp - 136f));
        Assert.That(second.Health.Current, Is.EqualTo(Hp - 136f * 0.85f).Within(0.001f));
    }

    [Test]
    public void Fire_IgnoresWalls()
    {
        EnemyAgent body = Spawn(3f);
        Move(body, 3f, blocked: true);
        Cast();
        Fire();
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 136f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Fire_PiercesFrontalGuard_WithoutLoweringIt(bool boss)
    {
        EnemyAgent guard = Guard(boss);
        Assert.That(guard.Health.IsInvulnerable, Is.True);
        Assert.That(_enemies.ApplyDamage(guard.Id, 17f, 0f, _player).Blocked, Is.True,
            "Ordinary arrows must still be blocked.");
        guard.DamageTaken.Base = 0.5f;
        Cast();
        Fire();
        Assert.That(guard.Health.Current, Is.EqualTo(Hp - 68f));
        Assert.That(guard.Health.IsInvulnerable, Is.True, "Piercing is scoped to this hit.");
        Assert.That(_enemies.ApplyDamage(guard.Id, 17f, 0.8f, _player).Blocked, Is.True);
        Assert.That(_events.Count<KillshotExecuted>(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Fire_BossPhaseBeatStillBlocks(bool warlord)
    {
        EnemyAgent boss = warlord ? Guard(true)
            : _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(3f, 0f, 0f));
        boss.Health.SetExternalInvulnerable(false);
        _enemies.ApplyDamage(boss.Id, 600f, 0f, _player);
        boss.Behaviour.Tick(Context());
        Assert.That(((BossBehaviour)boss.Behaviour).IsInBeat, Is.True);
        Cast();
        Fire();
        Assert.That(boss.Health.Current, Is.EqualTo(400f));
        Assert.That(boss.Health.IsInvulnerable, Is.True);
        Assert.That(_events.Count<KillshotExecuted>(), Is.Zero);
        Assert.That(_events.Of<EnemyDamaged>()[_events.Count<EnemyDamaged>() - 1].Blocked, Is.True);
    }

    [Test]
    public void Fire_OtherExternalImmunityStillBlocks()
    {
        EnemyAgent body = Spawn(3f);
        body.Health.SetExternalInvulnerable(true);
        Cast();
        Fire();
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
    }

    [Test]
    public void Execute_GuardStillPaysRewards_AndConsumesFalloff()
    {
        EnemyAgent guard = Guard(false);
        guard.Health.SetExternalInvulnerable(false);
        _enemies.ApplyDamage(guard.Id, 850f, 0f, _player);
        guard.Behaviour.Tick(Context());
        EnemyAgent behind = Spawn(6f);
        Cast();
        Fire();
        Assert.That(guard.IsAlive, Is.False);
        Assert.That(_events.Single<KillshotExecuted>().EnemyId, Is.EqualTo(guard.Id));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(_enemies.DrainKills(), Is.EqualTo(1));
        Assert.That(_enemies.DrainXp(), Is.EqualTo(12f));
        Assert.That(behind.Health.Current, Is.EqualTo(Hp - 136f * 0.85f).Within(0.001f));
    }

    private EnemyAgent Guard(bool boss)
    {
        EnemyAgent agent = boss
            ? _enemies.SpawnBoss(new ContentId("boss.warlord"), new Vector3(3f, 0f, 0f))
            : _enemies.Spawn(GuardBody().Id, new Vector3(3f, 0f, 0f));
        agent.Blackboard.DirectionToPlayer = -Vector2.UnitX;
        agent.Blackboard.PathDirectionToPlayer = -Vector2.UnitX;
        agent.Blackboard.DistanceToPlayer = 3f;
        agent.Blackboard.SelfPosition = agent.Position;
        agent.Behaviour.Tick(Context());
        return agent;
    }

    private EnemyTickContext Context() => new(0f, 0f, _player, _intents, _events,
        new ProjectileSystem(_events, Capacity), _enemies);

    [Test]
    public void Execute_NonBossBelowThreshold()
    {
        EnemyAgent body = Spawn(3f);
        _enemies.ApplyDamage(body.Id, 850f, 0f, _player);
        body.DamageTaken.Base = 0f;
        body.Health.SetExternalInvulnerable(true);
        body.Health.GrantShield(500f, this);
        _events.Clear();
        Cast();
        Fire();
        Assert.That(body.IsAlive, Is.False, "Execute bypasses damage immunity and shields.");
        Assert.That(_events.Single<KillshotExecuted>().EnemyId, Is.EqualTo(body.Id));
        int executed = Index<KillshotExecuted>();
        KillshotHit hit = _events.Single<KillshotHit>();
        Assert.That(hit.EnemyId, Is.EqualTo(body.Id));
        Assert.That(hit.At, Is.EqualTo(body.Position));
        Assert.That(Index<KillshotHit>(), Is.EqualTo(executed - 1));
        Assert.That(executed, Is.LessThan(Index<EnemyDied>()));
        Assert.That(_enemies.DrainXp(), Is.EqualTo(12f));
        Assert.That(_enemies.DrainKills(), Is.EqualTo(1));
        var deaths = new EnemyDeath[Capacity];
        Assert.That(_enemies.DrainDeaths(deaths), Is.EqualTo(1));
        Assert.That(deaths[0].WasBoss, Is.False);
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
    }

    [Test]
    public void Execute_NeverBoss()
    {
        EnemyAgent boss = _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(3f, 0f, 0f));
        _enemies.ApplyDamage(boss.Id, 850f, 0f, _player);
        Cast();
        Fire();
        Assert.That(boss.Health.Current, Is.EqualTo(14f));
        Assert.That(_events.Count<KillshotExecuted>(), Is.Zero);
    }

    [Test]
    public void Fired_PublishesOnce()
    {
        Spawn(3f);
        Cast();
        _enemies.Clear();
        Fire();
        _handler.Tick(100f);
        KillshotFired fired = _events.Single<KillshotFired>();
        Assert.That(fired.Hits, Is.Zero);
        Assert.That(fired.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(fired.Length, Is.EqualTo(30f));
    }

    [TestCase(0f, 0.15f)]
    [TestCase(0.05f, 0.20f)]
    [TestCase(0.10f, 0.25f)]
    public void Rank_ExecuteRises(float upgrade, float fraction)
    {
        Upgrade(PlayerStat.KillshotExecute, upgrade);
        EnemyAgent at = Spawn(3f);
        EnemyAgent above = Spawn(4f);
        _enemies.ApplyDamage(at.Id, Hp * (1f - fraction), 0f, _player);
        _enemies.ApplyDamage(above.Id, Hp * (1f - fraction) - 1f, 0f, _player);
        Cast();
        Fire();
        Assert.That(_events.Single<KillshotExecuted>().EnemyId, Is.EqualTo(at.Id));
        Assert.That(above.IsAlive, Is.True);
    }

    [Test]
    public void Rank_DamageAndCooldown()
    {
        Upgrade(PlayerStat.KillshotDamage, 0.25f);
        Upgrade(PlayerStat.WeaponDamage, 3f);
        EnemyAgent body = Spawn(3f);
        _effects.Apply(new ModifySkillCooldown(SkillId, ModifierKind.Flat, -8f), this);
        Cast();
        Fire();
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 8f * 1.25f * 20f));
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(22f));
    }

    [Test]
    public void Auto_EnemyInAcquireRange_ThirtySecondCooldown()
    {
        Spawn(3f);
        _player.Blackboard.EnemiesInAcquireRange = 0;
        _runner.Tick(0f, 0f);
        Assert.That(_handler.IsWindingUp, Is.False);
        _player.Blackboard.EnemiesInAcquireRange = 1;
        _runner.Tick(0f, 0f);
        Assert.That(_handler.IsWindingUp, Is.True);
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(30f));
        Fire();
        _runner.Tick(0f, 29.99f);
        Assert.That(_handler.IsWindingUp, Is.False);
        _clock.Now = 30f;
        _runner.Tick(0f, 30f);
        Assert.That(_events.Count<KillshotWindUp>(), Is.EqualTo(2));
    }

    [Test]
    public void Death_CancelsWindUp()
    {
        Spawn(3f);
        Cast();
        _player.Health.MaxHp.Base = 0f;
        _player.AnnounceDeath(0.3f);
        Assert.That(_player.Health.IsInvulnerable, Is.False);
        _handler.Tick(0.3f);
        Fire();
        Assert.That(_handler.IsWindingUp, Is.False);
        Assert.That(_motor.IsHeld, Is.False);
        Assert.That(_events.Count<KillshotFired>(), Is.Zero);
        Assert.That(_events.Count<KillshotExecuted>(), Is.Zero);
    }

    [Test]
    public void Boundary_Cancels()
    {
        RunSession session = Session(windUp: 10f, waves: true, hp: 17f);
        session.SetAutoCast(SkillId, false);
        var snapshot = new WorldSnapshot(Capacity)
        {
            Dt = 0.05f,
            HasGate = true,
            GatePosition = Vector3.Zero,
            SpawnPoints = new[] { new Vector3(0f, 0f, 7f) },
        };
        for (int i = 0; i < 100 && _events.Count<ProjectileFired>() == 0; i++)
        {
            if (_events.Count<EnemySpawned>() > 0)
            {
                EnemySpawned enemy = _events.Single<EnemySpawned>();
                snapshot.EnemyCount = 1;
                snapshot.Enemies[0] = new EnemySense { Id = enemy.Id, Position = enemy.Position, HasLineOfSight = true };
            }
            session.Tick(snapshot);
        }

        Assert.That(_events.Count<ProjectileFired>(), Is.GreaterThan(0));
        session.CastSkillNow(SkillId);
        Assert.That(_events.Count<KillshotWindUp>(), Is.EqualTo(1));
        snapshot.MoveInput = Vector2.UnitX;
        for (int i = 0; i < 180 && _events.Count<StageCleared>() == 0; i++)
        {
            session.Tick(snapshot);
        }
        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
        Assert.That(session.State.StageIndex, Is.EqualTo(1));
        Assert.That(_intents.LastPlayerMove.Velocity.Length(), Is.GreaterThan(0f));
        for (int i = 0; i < 180 && session.State.StageIndex == 1; i++)
        {
            session.Tick(snapshot);
            if (session.State.IsSanctumOpen)
            { session.LeaveSanctum(); }
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        snapshot.EnemyCount = 0;
        snapshot.HasGate = false;
        snapshot.MoveInput = Vector2.UnitX;
        for (int i = 0; i < 220; i++)
        { session.Tick(snapshot); }
        Assert.That(_events.Count<KillshotFired>(), Is.Zero);
        Assert.That(_intents.LastPlayerMove.Velocity.Length(), Is.GreaterThan(0f));
        session.End();
    }

    [Test]
    public void Clear_CancelsWindUpAndDropsImmunity()
    {
        Spawn(3f);
        Cast();
        Assert.That(_player.Health.IsInvulnerable, Is.True);
        _handler.Clear();
        Assert.That(_player.Health.IsInvulnerable, Is.False);
        Fire();
        Assert.That(_events.Count<KillshotFired>(), Is.Zero);
    }

    [Test]
    public void Session_WindUp_HoldsRollAndMovement_ThenDrainsDeaths()
    {
        RunSession session = Session();
        session.SetAutoCast(SkillId, false);
        session.CastSkillNow(SkillId);
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.05f, MoveInput = Vector2.UnitX };
        for (int i = 0; i < 13; i++)
        {
            session.MovementSkill();
            session.Tick(snapshot);
            Assert.That(_intents.LastPlayerMove.Velocity, Is.EqualTo(Vector3.Zero));
        }

        Assert.That(_events.Count<ChargeStarted>(), Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<KillshotFired>(), Is.Zero);
        session.Tick(snapshot);
        session.Tick(snapshot);
        Assert.That(_events.Single<KillshotFired>().Hits, Is.EqualTo(1));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(session.State.IsEnemyAlive(1), Is.False);
        session.End();
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, new RecordingIntents(), Capacity);
        var motor = new PlayerMotor(Character().Movement, Vector3.UnitZ);
        var stats = new PlayerStats(player, motor, new LevelTracker(Scalings.Xp(), silent));
        EnemySystem enemies = Enemies(silent);
        EnemyAgent body = enemies.Spawn(Body().Id, new Vector3(3f, 0f, 0f));
        body.Health.MaxHp.Base = 1e8f;
        body.Health.Reset();
        var clock = new SimulatedClock();
        var handler = new KillshotHandler(enemies, player, motor, stats, clock, silent);
        var snapshot = new WorldSnapshot(Capacity);
        Killshot shot = Shot();
        int casts = 0;
        AllocationAssert.None(() =>
        {
            handler.Apply(shot, this);
            player.Tick(0.01f, clock.Now, snapshot, enemies.Registry.Alive, Vector3.UnitX);
            motor.Tick(0.01f, Vector2.UnitX, null);
            handler.Tick(clock.Now + 0.3f);
            handler.Tick(clock.Now + 0.7f);
            clock.Now += 1f;
            casts++;
        });
        Assert.That(casts, Is.GreaterThan(10_000));
        Assert.That(body.Health.Current, Is.LessThan(1e8f - 1e6f));
    }

    [Test]
    public void Guards()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(bad, 0.5f, 30f, 8f, 0.15f, 0.4f, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, bad, 30f, 8f, 0.15f, 0.4f, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, 0.5f, bad, 8f, 0.15f, 0.4f, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, 0.5f, 30f, bad, 0.15f, 0.4f, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => _motor.Hold(bad));
        }
        foreach (float bad in new[] { -1f, float.NaN, 1.01f, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, 0.5f, 30f, 8f, bad, 0.4f, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, 0.5f, 30f, 8f, 0.15f, bad, 0.15f, MinHits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Killshot(0.7f, 0.5f, 30f, 8f, 0.15f, 0.4f, bad, MinHits));
        }
        Assert.Throws<ArgumentNullException>(() => _handler.CanCast(null));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(Shot(), null));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(null, this));
    }

    private int Index<T>() where T : struct
    {
        for (int i = 0; i < _events.All.Count; i++)
        { if (_events.All[i] is T) { return i; } }
        return -1;
    }

    private void Cast() => Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
    private void Fire() => _handler.Tick(0.7f);
    private void Upgrade(PlayerStat stat, float amount) => _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, amount), this);
    private EnemyAgent Spawn(float x, float z = 0f, float y = 0f) => _enemies.Spawn(Body().Id, new Vector3(x, y, z));
    private static EnemyPoint Point(int id, float x, float z) => new(id, new Vector3(x, 100f, z), 0.4f, false);
    private void Move(EnemyAgent body, float x, float z = 0f, bool blocked = false)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = body.Id;
        sense.Position = new Vector3(x, 0f, z);
        sense.HasLineOfSight = !blocked;
        _enemies.Ingest(snapshot);
    }

    private RunSession Session(float windUp = 0.7f, bool waves = false, float hp = 100f)
    {
        SkillSpec active = Skill(windUp);
        SkillSpec b = Passive("skill.filler.b");
        SkillSpec c = Passive("skill.filler.c");
        var tree = new SkillTreeSpec(new ContentId("tree.test"), Character().Id,
            new[] { Branch(active.Id), Branch(b.Id), Branch(c.Id) });
        var scaling = new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(8, 1000), new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"), 1, true, 0,
            scaling, Scalings.Xp(), waves ? new[] { new RosterEntry(Body().Id, 1) } : Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body(hp: hp) }, new[] { mode },
            new[] { active, b, c }, new[] { tree });
        var random = new FixedRandom(99);
        var session = new RunSession(catalog, random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, Character().Id, 99, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { active.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, Character().Id, 99, 1, waves ? SpawnPlan.Empty
            : new SpawnPlan(new[] { new SpawnPlan.Entry(Body().Id, new Vector3(3f, 0f, 0f)) }), saved));
        return session;
    }

    private static Killshot Shot(float windUp = 0.7f) => new(windUp, 1.5f, 30f, 8f, 0.15f, 0.4f, 0.15f, MinHits);
    private static SkillSpec Skill(float windUp = 0.7f) => new(SkillId, new LocKey("skill.killshot.name"),
        new LocKey("skill.killshot.desc"), SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) }),
            new IEffect[] { Shot(windUp) }));
    private static SkillBranchSpec Branch(ContentId id) => new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });
    private static SkillSpec Passive(string id) => new(new ContentId(id), new LocKey("skill.test.name"),
        new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
    private static EnemySystem Enemies(IDomainEvents events) => new(new ContentCatalog(new[] { Character() },
        new[] { Body(), Body(true), GuardBody() }, bosses: new[] {
            new BossSpec(new ContentId("boss.test"), Body(true).Id,
                new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f),
            new BossSpec(new ContentId("boss.warlord"), Body(true).Id,
                new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f) }),
        events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity,
        bossInner: (agent, spec) => spec.Id.Value == "boss.warlord"
            ? new WarlordBehaviour(agent, new ProjectileSystem(events, Capacity), new FixedRandom(7).Misc, GuardBody().Id)
            : null);
    private static EnemySpec GuardBody() => new(new ContentId("enemy.mossguard"), new LocKey("enemy.mossguard.name"),
        Hp, 2f, 3, 14, 12f, false, 8f, 1.2f, 0.7f, 1.5f, 30f, EnemyBehaviourKind.Bulwark,
        bulwark: new BulwarkSpec(60f, 75f, 45f));
    private static CharacterSpec Character() => new(new ContentId("character.ranger"), new LocKey("character.ranger.name"),
        new LocKey("character.ranger.desc"), 100f, new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f, firesWhileMoving: false),
        new FocusSpec(0.4f, 1f, 1f), new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);
    private static EnemySpec Body(bool boss = false, float hp = Hp) => new(new ContentId(boss ? "enemy.boss" : "enemy.husk"),
        new LocKey("enemy.husk.name"), hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f,
        boss ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);
}
