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
public sealed class VultureBehaviourTests
{
    private const int Capacity = 8;
    private const float Climb = 0.6f;
    private const float Lock = 1f;
    private const float Dive = 0.4f;
    private const float Grounded = 2f;
    private const float Radius = 7f;
    private const float Damage = 14f;
    private static readonly ContentId Id = new("enemy.vulture");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private EnemyAgent _agent;
    private VultureBehaviour _vulture;
    private WorldSnapshot _snapshot;
    private PetSystem _pets;
    private IReadOnlyList<Vector3> _openGround;
    private Vector3 _body;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Start(_events);
    }

    [Test]
    public void Spec_Guards()
    {
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Vulture, null));
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Chaser, DiveData()));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(speed: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(speed: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(speed: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(speed: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(max: 3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiveData(length: Radius));
        DiveSpec fixedInterval = DiveData(max: 4f);
        Assert.That(Enemy(EnemyBehaviourKind.Vulture, fixedInterval).Dive, Is.SameAs(fixedInterval));
    }

    [Test]
    public void Spawn_ClimbsThenCircles()
    {
        AssertAloft();
        Assert.That(_enemies.ApplyDamage(_agent.Id, 100f, _now, _player).Blocked, Is.True);
        Step(0f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
        Step(Climb / 2f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
        Step(Climb / 2f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Circle));
        AssertAloft();
    }

    [Test]
    public void Aloft_IsUntouchable()
    {
        Step(Climb);
        AssertAloft();
        Assert.That(_enemies.ApplyDamage(_agent.Id, 100f, _now, _player).Blocked, Is.True);
        Assert.That(_enemies.Execute(_agent.Id, _now, _player).Killed, Is.False);
        _enemies.Stun(_agent.Id, 1f);
        _enemies.Root(_agent.Id, 1f);
        _enemies.Slow(_agent.Id, 0.5f, 1f);
        _enemies.Poison(_agent.Id, 1f, 1f);
        _enemies.Mark(_agent.Id, 1f);
        Assert.That(_agent.CrowdControl.IsStunned, Is.False);
        Assert.That(_agent.CrowdControl.IsRooted, Is.False);
        Assert.That(_agent.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(_enemies.PoisonStacks(_agent.Id), Is.Zero);
        Assert.That(_enemies.IsMarked(_agent.Id), Is.False);
        QueryTarget();
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(_player.Targeter.FocusedTargetId, Is.EqualTo(-1));
    }

    [Test]
    public void Circle_HoldsSevenMetres()
    {
        _body = 10f * Vector3.UnitZ;
        Step(Climb);
        Step(0.25f);
        Assert.That(_body.Z, Is.EqualTo(8.5f).Within(0.0001f));
        Step(0.25f);
        Assert.That(_body.Z, Is.EqualTo(Radius).Within(0.0001f));
        Step(1f);
        Step(1.5f);
        Assert.That(_body.Length(), Is.EqualTo(Radius).Within(0.0001f));
        float angle = MathF.Atan2(-_body.X, _body.Z);
        Assert.That(angle * Radius, Is.EqualTo(2.5f * 4f).Within(0.0001f));
    }

    [TestCase(2, 1)]
    [TestCase(3, -1)]
    public void Circle_DirectionByIdParity(int id, int sign)
    {
        Start(_events, id);
        Step(Climb);
        Step(1f);
        Assert.That(_body.X, Is.EqualTo(sign * Radius * MathF.Sin(4f / Radius)).Within(0.0001f));
    }

    [Test]
    public void Interval_FourFiveSix()
    {
        // Registry ids start at 1. Id 3 has the same interval offsets as id 0.
        Start(_events, 3);
        Step(Climb);
        for (int i = 0; i < 3; i++)
        {
            float interval = 4f + i;
            Step(interval - 0.125f);
            Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Circle));
            Step(0.125f);
            Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Lock));
            Step(Lock);
            Assert.That(_vulture.DiveCount, Is.EqualTo(i + 1));
            Step(Dive);
            Step(Grounded);
            Step(Climb);
        }
    }

    [Test]
    public void Lock_OpensLaneThroughHero()
    {
        LockLane();
        VultureDiveLocked locked = _events.Single<VultureDiveLocked>();
        LaneArmed lane = _events.Single<LaneArmed>();
        Assert.That(locked.Id, Is.EqualTo(_agent.Id));
        Assert.That(lane.Id, Is.EqualTo(locked.LaneId));
        Assert.That(lane.Origin, Is.EqualTo(_body));
        Assert.That(locked.Origin, Is.EqualTo(lane.Origin));
        Assert.That(locked.Direction, Is.EqualTo(lane.Direction));
        Assert.That(lane.Direction, Is.EqualTo(-Vector2.UnitY));
        Assert.That(lane.Length, Is.EqualTo(10f));
        Assert.That(lane.HalfWidth, Is.EqualTo(0.6f));
        Assert.That(lane.ArmSeconds, Is.EqualTo(Lock));
        Assert.That(locked.Seconds, Is.EqualTo(Lock));
        Assert.That(locked.Landing, Is.EqualTo(-3f * Vector3.UnitZ));
        Step(Lock / 2f);
        Assert.That(_body, Is.EqualTo(locked.Origin));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        AssertAloft();
    }

    [Test]
    public void Lock_LanesFullRetries()
    {
        Step(Climb);
        for (int i = 0; i < LaneSystem.Capacity; i++)
        {
            _lanes.Open(20f * Vector3.UnitX, Vector3.UnitZ, 1f, 1f, i == 0 ? 5.125f : 20f, 1f, _now);
        }

        Vector3 before = _body;
        Step(5f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Circle));
        Assert.That(_events.Count<VultureDiveLocked>(), Is.Zero);
        Assert.That(_body, Is.Not.EqualTo(before));
        Step(0.375f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Lock));
        Assert.That(_events.Count<VultureDiveLocked>(), Is.EqualTo(1));
    }

    [Test]
    public void Lock_LandingAvoidsCover()
    {
        Vector3 open = new(1f, 0f, -3f);
        _openGround = new[] { 4f * Vector3.UnitX, open };
        LockLane();
        Assert.That(_events.Single<VultureDiveLocked>().Landing, Is.EqualTo(open));
        Assert.That(_events.Single<LaneArmed>().Direction, Is.EqualTo(-Vector2.UnitY));
        Step(Lock);
        Step(Dive);
        Assert.That(Vector3.Distance(_body, open), Is.LessThan(0.0001f));
    }

    [Test]
    public void Dive_HitsInsideLine()
    {
        LockLane();
        Vector3 origin = _body;
        Step(Lock);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Dive));
        AssertAloft();
        Assert.That(_player.Health.Current, Is.EqualTo(100f - Damage));
        Assert.That(_events.Single<LaneFired>().Id, Is.EqualTo(_events.Single<VultureDiveLocked>().LaneId));
        Step(Dive / 2f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Dive));
        Assert.That(_body.Z, Is.EqualTo(origin.Z - 5f).Within(0.0001f));
        Step(0.25f);

        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Grounded));
        Assert.That(_body.Z, Is.EqualTo(-3f).Within(0.0001f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f - Damage));
        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1));
    }

    [Test]
    public void Dive_MissesASidestep()
    {
        LockLane();
        _snapshot.PlayerPosition += 0.7f * Vector3.UnitX;
        Step(Lock);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1));
    }

    [Test]
    public void Dive_IgnoresPets()
    {
        var hound = new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 1f, 1.4f, 1.5f);
        _pets = new PetSystem(hound, _events, _intents);
        _pets.SpawnBeside(Vector3.Zero, _now);
        PetAgent pet = _pets.Pets[0];
        _snapshot.PlayerPosition = pet.Position;
        _body = pet.Position + Radius * Vector3.UnitZ;
        LockLane();
        Step(Lock);
        Assert.That(_player.Health.Current, Is.EqualTo(100f - Damage));
        Assert.That(pet.Health.Current, Is.EqualTo(70f));
    }

    [Test]
    public void Grounded_ExposedForTwo()
    {
        LockLane();
        Step(Lock);
        Step(Dive);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Grounded));
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.IsHidden, Is.False);
        Assert.That(_agent.Health.IsInvulnerable, Is.False);
        Assert.That(_enemies.ApplyDamage(_agent.Id, 1f, _now, _player).Applied, Is.EqualTo(1f));
        QueryTarget();
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(_agent.Id));
        _snapshot.PlayerPosition += Vector3.UnitX;
        Step(0.125f);
        Assert.That(_intents.LastEnemyMove.FacingXZ.X, Is.GreaterThan(0f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        _enemies.Slow(_agent.Id, 0.5f, 3f);
        Step(1.875f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
        AssertAloft();
        Assert.That(_player.Health.Current, Is.EqualTo(100f - Damage));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Grounded_StunHoldsClock(bool root)
    {
        LockLane();
        Step(Lock);
        Step(Dive);
        Step(1f);
        if (root)
        {
            _enemies.Root(_agent.Id, 1f);
        }
        else
        {
            _enemies.Stun(_agent.Id, 1f);
        }

        Step(0.75f);
        Step(1f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Grounded));
        Step(0.25f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
    }

    [Test]
    public void Events_OnePerPhase()
    {
        LockLane();
        Step(Lock);
        Step(Dive);
        Step(Grounded);
        IReadOnlyList<VulturePhaseChanged> phases = _events.Of<VulturePhaseChanged>();
        Assert.That(phases.Select(p => p.Phase), Is.EqualTo(new[]
        {
            VulturePhase.Climb, VulturePhase.Circle, VulturePhase.Lock, VulturePhase.Dive,
            VulturePhase.Grounded, VulturePhase.Climb,
        }));
        Assert.That(phases.Select(p => p.Seconds), Is.EqualTo(new[] { Climb, 5f, Lock, Dive, Grounded, Climb }));
        Assert.That(phases.All(p => p.Id == _agent.Id), Is.True);
    }

    [Test]
    public void Reset_Aloft_Cleans()
    {
        LockLane();
        Step(Lock);
        Step(Dive);
        Step(Grounded);
        Step(Climb);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Circle));
        Assert.That(_vulture.DiveCount, Is.EqualTo(1));
        _vulture.Reset();
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
        Assert.That(_vulture.DiveCount, Is.Zero);
        AssertAloft();
        Step(Climb / 2f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Climb));
    }

    [Test]
    public void Tick_Cycle_NoAlloc()
    {
        var silent = new SilentEvents();
        Start(silent);
        AllocationAssert.None(() =>
        {
            _lanes.Clear();
            _vulture.Reset();
            _body = Radius * Vector3.UnitZ;
            _now = 0f;
            _player.Health.Reset();
            Step(Climb, silent);
            for (int i = 0; i < 2; i++)
            {
                Step(1f, silent);
                Step(4f + i, silent);
                Step(Lock, silent);
                Step(Dive, silent);
                Step(Grounded, silent);
                Step(Climb, silent);
            }
        }, 100);
        Assert.That(_vulture.DiveCount, Is.EqualTo(2));
    }

    private void LockLane()
    {
        Step(Climb);
        Step(5f);
        Assert.That(_vulture.State, Is.EqualTo(VulturePhase.Lock));
    }

    private void AssertAloft()
    {
        Assert.That(_agent.IsVulnerable, Is.False);
        Assert.That(_agent.IsHidden, Is.True);
        Assert.That(_agent.Health.IsInvulnerable, Is.True);
    }

    private void QueryTarget()
    {
        _player.FocusAt(_body, _enemies.Registry.Alive);
        _player.Tick(0f, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
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
        _lanes.Tick(_now, _snapshot.PlayerPosition, _player);
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, events ?? _events,
            _projectiles, _enemies, _lanes, _openGround, Vector2.UnitY, pets: _pets));
        _body += _intents.LastEnemyMove.Velocity * dt;
    }

    private void Start(IDomainEvents events, int id = 1)
    {
        CharacterSpec hero = Character();
        EnemySpec enemy = Enemy(EnemyBehaviourKind.Vulture, DiveData());
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { hero }, new[] { enemy }, new[] { mode });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), Capacity);
        _body = Radius * Vector3.UnitZ;
        for (int i = 1; i < id; i++)
        {
            _enemies.Despawn(_enemies.Spawn(Id, _body).Id);
        }

        _agent = _enemies.Spawn(Id, _body);
        _vulture = (VultureBehaviour)_agent.Behaviour;
        _intents = new RecordingIntents();
        _player = new PlayerCombat(hero, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _lanes = new LaneSystem(events);
        _snapshot = new WorldSnapshot(Capacity);
        _pets = null;
        _openGround = null;
        _now = 0f;
    }

    private static DiveSpec DiveData(float speed = 4f, float max = 6f, float length = 10f) =>
        new(Radius, speed, 6f, 4f, max, Lock, length, 0.6f, Dive, Damage, Grounded, Climb);

    private static EnemySpec Enemy(EnemyBehaviourKind kind, DiveSpec dive) => new(Id,
        new LocKey("enemy.vulture.name"), 50f, 0f, 6, 14, 31f, false, 0f, 1f, 0f, 0f, 40f, kind, dive: dive);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
