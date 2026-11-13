using System;
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
public sealed class CobraBehaviourTests
{
    private const int Capacity = 8;
    private const float Rear = 0.7f;
    private const float Recover = 0.5f;
    private const float Cooldown = 3f;
    private static readonly ContentId Id = new("enemy.cobra");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemyAgent _agent;
    private CobraBehaviour _cobra;
    private WorldSnapshot _snapshot;
    private Vector3 _body;
    private bool _sight;
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
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Cobra, null));
        Assert.Throws<ArgumentException>(() => Enemy(EnemyBehaviourKind.Chaser, Cone()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(hold: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(hold: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(hold: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(close: 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(close: 9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(half: 90f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(half: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(fraction: 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(fraction: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(stacks: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cone(cooldown: -1f));
        Assert.That(Cone(fraction: 1f).PoisonFractionPerSecond, Is.EqualTo(1f));
        Assert.That(Cone().HoldDistance, Is.EqualTo(8f));
        Assert.That(Cone().PoisonMaxStacks, Is.EqualTo(3));
    }

    [Test]
    public void Hold_KeepsBand()
    {
        Spit();
        Step(Recover);
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Hold));
        Assert.That(_cobra.Cooldown, Is.GreaterThan(0f));

        Hero(12f);
        Step(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.GreaterThan(0f), "closes beyond 8 m");

        Hero(7f);
        Step(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "stands inside the band");
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));

        Hero(5f);
        Step(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.LessThan(0f), "backs off inside 5.6 m");
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Hold));
    }

    [Test]
    public void Close_ReadyRearsAt4()
    {
        Hero(8f);
        for (int i = 0; i < 60 && _cobra.State != CobraPhase.Rear; i++)
        {
            Step(0.1f);
        }

        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Rear));
        Assert.That(_body.Z, Is.EqualTo(4f).Within(0.01f));
        CobraConeArmed armed = _events.Single<CobraConeArmed>();
        Assert.That(armed.Id, Is.EqualTo(_agent.Id));
        Assert.That(armed.HalfAngleDegrees, Is.EqualTo(30f));
        Assert.That(armed.Length, Is.EqualTo(5f));
        Assert.That(armed.Seconds, Is.EqualTo(Rear));
        Assert.That(armed.Apex.Z, Is.EqualTo(4f).Within(0.01f));
        Assert.That(armed.Direction, Is.EqualTo(Vector2.UnitY));
    }

    [Test]
    public void Close_NoSightNoRear()
    {
        _sight = false;
        Hero(3f);
        for (int i = 0; i < 20; i++)
        {
            Step(0.1f);
        }

        Assert.That(_events.Count<CobraConeArmed>(), Is.Zero);
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Hold));
    }

    [Test]
    public void Rear_DirectionLocked()
    {
        Hero(4f);
        Step(0.1f);
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Rear));

        _snapshot.PlayerPosition = new Vector3(4f, 0f, 0f);
        Step(0.35f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        _snapshot.PlayerPosition = new Vector3(-4f, 0f, 0f);
        Step(0.35f);

        CobraConeArmed armed = _events.Single<CobraConeArmed>();
        Assert.That(armed.Direction, Is.EqualTo(Vector2.UnitY));
        Assert.That(_events.Single<CobraSpat>().Hit, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void Spit_HitsInCone()
    {
        Spit();

        Assert.That(_player.Health.Current, Is.EqualTo(92f));
        Assert.That(_player.PoisonStacks, Is.EqualTo(1));
        Assert.That(_events.Single<CobraSpat>().Hit, Is.True);
        Assert.That(_events.Single<CobraSpat>().Id, Is.EqualTo(_agent.Id));
    }

    [TestCase(0f, -1.2f)]
    [TestCase(2.294f, 3.276f)]
    public void Spit_Misses(float x, float z)
    {
        Hero(4f);
        Step(0.1f);
        _snapshot.PlayerPosition = new Vector3(x, 0f, z);
        Step(Rear);

        Assert.That(_events.Single<CobraSpat>().Hit, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_player.PoisonStacks, Is.Zero);
    }

    [Test]
    public void Spit_RollingHeroUntouched()
    {
        Hero(4f);
        Step(0.1f);
        Step(0.35f);
        _player.Charge.Request(_now);
        _player.Tick(0.01f, _now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
        Step(0.35f);

        Assert.That(_events.Count<CobraSpat>(), Is.EqualTo(1));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_player.PoisonStacks, Is.Zero);
    }

    [Test]
    public void Cooldown_FromTheSpit()
    {
        Hero(4f);
        float firstSpit = -1f;
        float secondArmed = -1f;
        float secondSpit = -1f;
        for (int i = 0; i < 100 && secondSpit < 0f; i++)
        {
            Step(0.1f);
            _snapshot.PlayerPosition = _body + 4f * Vector3.UnitZ;
            if (firstSpit < 0f && _events.Count<CobraSpat>() == 1)
            {
                firstSpit = _now;
                Assert.That(_cobra.Cooldown, Is.EqualTo(Cooldown));
            }

            if (secondArmed < 0f && _events.Count<CobraConeArmed>() == 2)
            {
                secondArmed = _now;
            }

            if (_events.Count<CobraSpat>() == 2)
            {
                secondSpit = _now;
            }
        }

        Assert.That(firstSpit, Is.GreaterThan(0f));
        Assert.That(secondArmed, Is.GreaterThanOrEqualTo(firstSpit + Cooldown - 0.0001f));
        Assert.That(secondSpit, Is.GreaterThanOrEqualTo(firstSpit + Cooldown + Rear - 0.0001f));
    }

    [Test]
    public void Events_OnePerPhase()
    {
        Spit();
        Step(Recover);
        var phases = _events.Of<CobraPhaseChanged>();
        Assert.That(phases.Count, Is.EqualTo(5));
        Assert.That(phases[4].Phase, Is.EqualTo(CobraPhase.Hold));
        Assert.That(phases[0].Phase, Is.EqualTo(CobraPhase.Hold));
        Assert.That(phases[1].Phase, Is.EqualTo(CobraPhase.Close));
        Assert.That(phases[2].Phase, Is.EqualTo(CobraPhase.Rear));
        Assert.That(phases[2].Seconds, Is.EqualTo(Rear));
        Assert.That(phases[3].Phase, Is.EqualTo(CobraPhase.Recover));
        Assert.That(phases[3].Seconds, Is.EqualTo(Recover));
    }

    [Test]
    public void Reset_Clears()
    {
        Hero(4f);
        Step(0.1f);
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Rear));
        _player.ApplyPoison(0.02f, 4f, 3);
        Assert.That(_player.PoisonStacks, Is.EqualTo(1));

        _cobra.Reset();
        _player.Reset();

        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Hold));
        Assert.That(_cobra.Cooldown, Is.Zero);
        Assert.That(_player.PoisonStacks, Is.Zero);
        Hero(12f);
        Step(0.1f);
        Assert.That(_cobra.State, Is.EqualTo(CobraPhase.Close));
    }

    [Test]
    public void Tick_Cycle_NoAlloc()
    {
        var silent = new SilentEvents();
        Start(silent);
        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 2; i++)
            {
                _cobra.Reset();
                _player.Reset();
                _body = Vector3.Zero;
                _snapshot.PlayerPosition = 4f * Vector3.UnitZ;
                Step(0.1f, silent);
                Step(Rear, silent);
                _player.Tick(0.5f, _now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
                Step(Recover, silent);
                _player.Tick(0.5f, _now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            }
        }, 100);
    }

    private void Spit()
    {
        Hero(4f);
        Step(0.1f);
        Step(Rear);
        Assert.That(_events.Count<CobraSpat>(), Is.EqualTo(1));
    }

    private void Hero(float distance)
    {
        _snapshot.PlayerPosition = _body + distance * Vector3.UnitZ;
    }

    private void Step(float dt, IDomainEvents events = null)
    {
        _now += dt;
        _snapshot.Dt = dt;
        _snapshot.EnemyCount = 0;
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense = new EnemySense { Id = _agent.Id, Position = _body, HasLineOfSight = _sight };
        _enemies.Ingest(_snapshot, now: _now);
        _intents.Clear();
        var ctx = new EnemyTickContext(dt, _now, _player, _intents, events ?? _events, _projectiles, _enemies);
        _agent.Behaviour.Tick(ctx);
        _body += _intents.LastEnemyMove.Velocity * dt;
    }

    private void Start(IDomainEvents events)
    {
        CharacterSpec hero = Character();
        EnemySpec enemy = Enemy(EnemyBehaviourKind.Cobra, Cone());
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { hero }, new[] { enemy }, new[] { mode });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(0.5f), new DepthScaling(Scalings.Design()), Capacity);
        _body = Vector3.Zero;
        _agent = _enemies.Spawn(Id, _body);
        _cobra = (CobraBehaviour)_agent.Behaviour;
        _player = new PlayerCombat(hero, events, _intents, Capacity);
        _player.AttachStatus(new StatusBoard());
        _projectiles = new ProjectileSystem(events, Capacity);
        _snapshot = new WorldSnapshot(Capacity) { PlayerPosition = 8f * Vector3.UnitZ };
        _sight = true;
        _now = 0f;
    }

    private static SpitConeSpec Cone(float hold = 8f, float close = 4f, float half = 30f, float fraction = 0.02f,
        int stacks = 3, float cooldown = Cooldown) =>
        new(hold, close, Rear, half, 5f, 8f, fraction, 4f, stacks, Recover, cooldown);

    private static EnemySpec Enemy(EnemyBehaviourKind kind, SpitConeSpec cone) => new(Id,
        new LocKey("enemy.cobra.name"), 56f, 2.4f, 5, 12, 30f, false, 0f, 1f, 0.9f, 1.2f, 40f, kind,
        spitCone: cone);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 1f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
