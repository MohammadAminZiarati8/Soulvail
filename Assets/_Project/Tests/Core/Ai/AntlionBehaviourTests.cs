using System;
using System.Collections.Generic;
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
public sealed class AntlionBehaviourTests
{
    private const int Capacity = 8;
    private const float Dig = 0.6f;
    private const float Arm = 0.8f;
    private const float Surfaced = 2.5f;
    private static readonly ContentId Id = new("enemy.antlion");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemyAgent _agent;
    private AntlionBehaviour _antlion;
    private WorldSnapshot _snapshot;
    private PetSystem _pets;
    private IReadOnlyList<Vector3> _openGround;
    private Vector3 _body;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        Start(_events);
    }

    [Test]
    public void Spec_Guards()
    {
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Antlion, null));
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Chaser, Burrow()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(speed: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(speed: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(speed: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(speed: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(max: 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burrow(max: 1f));
    }

    [Test]
    public void Spawn_DigsThenTunnels()
    {
        Step(Dig / 2f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_enemies.ApplyDamage(_agent.Id, 1f, _now, _player).Applied, Is.EqualTo(1f));

        Step(Dig / 2f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Tunnel));
    }

    [Test]
    public void Underground_IsUntouchable()
    {
        _player.FocusAt(_body, _enemies.Registry.Alive);
        _player.Tick(0f, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
        Assert.That(_player.Targeter.FocusedTargetId, Is.EqualTo(_agent.Id));

        Step(Dig);
        AssertUnderground();
        Step(1.5f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Arm));
        AssertUnderground();
    }

    [Test]
    public void Goal_BehindMovingHero()
    {
        Step(Dig);
        _snapshot.PlayerPosition += 0.75f * Vector3.UnitX;
        Step(0.25f);
        Assert.That(_antlion.Goal, Is.EqualTo(_snapshot.PlayerPosition - 2f * Vector3.UnitX));
    }

    [Test]
    public void Goal_BehindStillHerosFacing()
    {
        Step(Dig);
        _snapshot.PlayerPosition += 0.0625f * Vector3.UnitX;
        Step(0.25f);
        Assert.That(_antlion.Goal, Is.EqualTo(_snapshot.PlayerPosition - 2f * Vector3.UnitZ));
    }

    [Test]
    public void Tunnel_NoLockBeforeMin()
    {
        Step(Dig);
        Step(0.5f);
        Assert.That(_body, Is.EqualTo(_antlion.Goal));
        Step(0.9f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Tunnel));
        Assert.That(_events.Count<AntlionRingArmed>(), Is.Zero);
    }

    [Test]
    public void Tunnel_LocksNearGoal()
    {
        ArmRing();
        AntlionRingArmed ring = _events.Single<AntlionRingArmed>();
        Assert.That(ring.Id, Is.EqualTo(_agent.Id));
        Assert.That(ring.Centre, Is.EqualTo(_snapshot.PlayerPosition));
        Assert.That(ring.Radius, Is.EqualTo(1.6f));
        Assert.That(ring.Seconds, Is.EqualTo(Arm));
    }

    [Test]
    public void Tunnel_LocksAtMax()
    {
        Step(Dig);
        for (int i = 1; i <= 8; i++)
        {
            _snapshot.PlayerPosition += 4f * Vector3.UnitX;
            Step(0.5f);
            Assert.That(_antlion.State, Is.EqualTo(i < 8 ? AntlionPhase.Tunnel : AntlionPhase.Arm));
        }
    }

    [Test]
    public void Arm_RingAvoidsCover()
    {
        Vector3 open = _snapshot.PlayerPosition + new Vector3(0f, 0f, 2f);
        _openGround = new[] { open + 4f * Vector3.UnitX, open };

        ArmRing();
        AntlionRingArmed ring = _events.Single<AntlionRingArmed>();
        Assert.That(ring.Centre, Is.EqualTo(open));
        Assert.That(Vector3.Distance(ring.Centre, _snapshot.PlayerPosition), Is.GreaterThan(ring.Radius));
        Step(Arm);
        Assert.That(_body, Is.EqualTo(open));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void Eruption_HitsInsideRing()
    {
        ArmRing();
        _snapshot.PlayerPosition += 5f * Vector3.UnitY;
        Step(Arm / 2f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Arm));
        Step(Arm / 2f);
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
        Assert.That(_body, Is.EqualTo(_events.Single<AntlionRingArmed>().Centre));
        Step(0.5f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
    }

    [Test]
    public void Eruption_MissesOutside()
    {
        ArmRing();
        _snapshot.PlayerPosition += Vector3.UnitX;
        Step(Arm / 2f);
        _snapshot.PlayerPosition += Vector3.UnitX;
        Step(Arm / 2f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<AntlionRingArmed>(), Is.EqualTo(1));
    }

    [Test]
    public void Eruption_IgnoresPets()
    {
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);
        _pets = new PetSystem(hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, _now);
        PetAgent pet = _pets.Pets[0];
        _snapshot.PlayerPosition = pet.Position;
        Step(Dig);
        Step(4f);
        Assert.That(_events.Single<AntlionRingArmed>().Centre, Is.EqualTo(pet.Position));
        Step(Arm);
        Assert.That(pet.Health.Current, Is.EqualTo(70f));
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
    }

    [Test]
    public void Eruption_RespectsHeroInvulnerability()
    {
        ArmRing();
        _player.Health.SetExternalInvulnerable(true);
        Step(Arm);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Surfaced));
    }

    [Test]
    public void Surfaced_ExposedThenDigs()
    {
        ArmRing();
        Step(Arm);
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.IsHidden, Is.False);
        Assert.That(_enemies.ApplyDamage(_agent.Id, 1f, _now, _player).Applied, Is.EqualTo(1f));
        Step(Surfaced);
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
        Step(Dig);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Tunnel));
    }

    [Test]
    public void Surfaced_StunHoldsClock()
    {
        AssertSurfaceHold((id, seconds) => _enemies.Stun(id, seconds));
    }

    [Test]
    public void Surfaced_RootHoldsClock()
    {
        AssertSurfaceHold((id, seconds) => _enemies.Root(id, seconds));
    }

    [Test]
    public void Surfaced_SlowDoesNotHoldClock()
    {
        ArmRing();
        Step(Arm);
        _enemies.Slow(_agent.Id, 0.5f, 3f);
        Step(Surfaced);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
    }

    [Test]
    public void Events_OnePerPhase()
    {
        ArmRing();
        Step(Arm);
        Step(Surfaced);
        IReadOnlyList<AntlionPhaseChanged> phases = _events.Of<AntlionPhaseChanged>();
        Assert.That(phases.Select(p => p.Phase), Is.EqualTo(new[]
        {
            AntlionPhase.Dig, AntlionPhase.Tunnel, AntlionPhase.Arm, AntlionPhase.Surfaced, AntlionPhase.Dig,
        }));
        Assert.That(phases.Select(p => p.Seconds), Is.EqualTo(new[] { Dig, 4f, Arm, Surfaced, Dig }));
        Assert.That(phases.All(p => p.Id == _agent.Id), Is.True);
    }

    [Test]
    public void Reset_Underground_Cleans()
    {
        Step(Dig);
        _antlion.Reset();
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
        Assert.That(_antlion.Goal, Is.EqualTo(Vector3.Zero));
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.IsHidden, Is.False);
        Assert.That(_agent.Health.IsInvulnerable, Is.False);
        Step(Dig / 2f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
    }

    [Test]
    public void Tick_Cycle_NoAlloc()
    {
        var silent = new SilentEvents();
        Start(silent);
        AllocationAssert.None(() =>
        {
            _antlion.Reset();
            for (int i = 0; i < 2; i++)
            {
                _player.Health.Reset();
                Step(Dig, silent);
                Step(4f, silent);
                Step(Arm, silent);
                Step(Surfaced, silent);
            }
        }, 100);
    }

    private void AssertUnderground()
    {
        Assert.That(_agent.IsVulnerable, Is.False);
        Assert.That(_agent.Health.IsInvulnerable, Is.True);
        Assert.That(_enemies.ApplyDamage(_agent.Id, 100f, _now, _player).Blocked, Is.True);
        Assert.That(_enemies.Execute(_agent.Id, _now, _player).Killed, Is.False);
        _enemies.Stun(_agent.Id, 1f);
        _enemies.Root(_agent.Id, 1f);
        _enemies.Slow(_agent.Id, 0.5f, 1f);
        _enemies.Poison(_agent.Id, 1f, 1f);
        Assert.That(_agent.CrowdControl.IsStunned, Is.False);
        Assert.That(_agent.CrowdControl.IsRooted, Is.False);
        Assert.That(_agent.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(_enemies.PoisonStacks(_agent.Id), Is.Zero);

        _player.FocusAt(_body, _enemies.Registry.Alive);
        _player.Tick(0f, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
        Assert.That(_player.Targeter.FocusedTargetId, Is.EqualTo(-1));
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(-1));
    }

    private void AssertSurfaceHold(Action<int, float> hold)
    {
        ArmRing();
        Step(Arm);
        Step(1f);
        hold(_agent.Id, 1f);
        Step(0.75f);
        Step(1.5f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Surfaced));
        Step(0.25f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Dig));
    }

    private void ArmRing()
    {
        Step(Dig);
        Step(1.5f);
        Assert.That(_antlion.State, Is.EqualTo(AntlionPhase.Arm));
    }

    private void Step(float dt, IDomainEvents events = null)
    {
        _now += dt;
        _snapshot.Dt = dt;
        _snapshot.EnemyCount = 0;
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense = new EnemySense { Id = _agent.Id, Position = _body, HasLineOfSight = true };
        _enemies.Ingest(_snapshot, pets: _pets, now: _now);
        _intents.Clear();
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, events ?? _events,
            _projectiles, _enemies, openGround: _openGround, playerFacing: Vector2.UnitY, pets: _pets));
        _body += _intents.LastEnemyMove.Velocity * dt;
    }

    private void Start(IDomainEvents events)
    {
        CharacterSpec hero = Character();
        EnemySpec enemy = Enemy(EnemyBehaviourKind.Antlion, Burrow());
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { hero }, new[] { enemy }, new[] { mode });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), Capacity);
        _body = Vector3.Zero;
        _agent = _enemies.Spawn(Id, _body);
        _antlion = (AntlionBehaviour)_agent.Behaviour;
        _player = new PlayerCombat(hero, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _snapshot = new WorldSnapshot(Capacity) { PlayerPosition = 3f * Vector3.UnitZ };
        _pets = null;
        _openGround = null;
        _now = 0f;
    }

    private static BurrowSpec Burrow(float speed = 4.5f, float max = 4f) =>
        new(speed, 2f, 1.2f, 1.5f, max, 1.6f, Arm, 16f, Surfaced, Dig);

    private static EnemySpec Enemy(EnemyBehaviourKind kind, BurrowSpec burrow) => new(Id,
        new LocKey("enemy.antlion.name"), 56f, 2.4f, 5, 12, 30f, false, 0f, 1f, 0.9f, 1.2f, 40f, kind,
        burrow: burrow);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
