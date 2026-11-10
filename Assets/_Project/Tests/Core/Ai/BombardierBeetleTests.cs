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
public sealed class BombardierBeetleTests
{
    private const int Capacity = 16;
    private const float Windup = 0.7f;
    private const float Dt = 0.025f;
    private static readonly ContentId Beetle = new("enemy.beetle");
    private static readonly ContentId Pitcher = new("enemy.spitter");
    private static readonly BombardierSpec Bombardier = new(45f, 10f, 20f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private EnemyAgent _agent;
    private float _now;

    private SpitterBehaviour Behaviour => (SpitterBehaviour)_agent.Behaviour;
    private Vector2 Facing => _intents.LastEnemyMove.FacingXZ;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(Capacity);
        Build(_events);
    }

    [Test]
    public void NoBombardier_SpitterUnchanged()
    {
        _agent = _enemies.Spawn(Pitcher, Vector3.Zero);
        Ingest(9f, 0f);
        Tick();
        Tick();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Approach));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(-2.8f * Vector3.UnitZ));
        Assert.That(Facing, Is.EqualTo(Vector2.UnitY));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void Spec_OnChaserOrWithSlinger_Throws()
    {
        Assert.That(Assert.Throws<ArgumentException>(() =>
            Spec(Beetle, EnemyBehaviourKind.Chaser, Bombardier)).ParamName, Is.EqualTo("bombardier"));
        Assert.That(Assert.Throws<ArgumentException>(() =>
            Spec(Beetle, EnemyBehaviourKind.Spitter, Bombardier, new SlingerSpec(3f, 0.7f, 3f))).ParamName,
            Is.EqualTo("bombardier"));
    }

    [Test]
    public void Spec_RejectsInvalidTurnAndCones()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(0f, 10f, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(float.PositiveInfinity, 10f, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(45f, float.NaN, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(45f, 0f, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(45f, 180f, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(45f, 10f, 9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BombardierSpec(45f, 10f, 180f));
        Assert.That(new BombardierSpec(45f, 10f, 10f).DropConeDegrees, Is.EqualTo(10f));
    }

    [Test]
    public void Planted_HeroClose_HoldsGround()
    {
        Ingest(6f, 0f);
        for (int i = 0; i < 80; i++)
        {
            Tick();
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        }

        Ingest(13f, 90f);
        Tick();
        Assert.That(_intents.LastEnemyMove.Velocity.X, Is.EqualTo(2.8f).Within(1e-5f));
        Assert.That(Facing.X, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Planted_TurnsRearAtRate()
    {
        Ingest(6f, 90f);
        Tick();
        Tick(1f);

        Assert.That(Facing.X, Is.EqualTo(-MathF.Sqrt(0.5f)).Within(1e-5f));
        Assert.That(Facing.Y, Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-5f));
    }

    [Test]
    public void AboutFace_TakesAboutFourSeconds()
    {
        Ingest(13f, 0f);
        Tick();
        Tick();
        Ingest(12f, 0f);
        float plantedAt = _now;

        for (int i = 0; i < 160 && _events.Count<EnemyTelegraph>() == 0; i++)
        {
            Tick();
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
        Assert.That(_now - plantedAt, Is.InRange(3.7f, 4.01f));
    }

    [Test]
    public void OutsideStartCone_NeverAims()
    {
        Ingest(6f, 150f);
        Tick();
        for (int i = 0; i < 120; i++)
        {
            Ingest(6f, 150f - (i * 45f * Dt));
            Tick();
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void InsideStartCone_Aims()
    {
        Ingest(11f, 175f);
        Tick();
        Tick();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
        EnemyTelegraph tell = _events.Single<EnemyTelegraph>();
        Assert.That(tell.Id, Is.EqualTo(_agent.Id));
        Assert.That(tell.Duration, Is.EqualTo(Windup));
    }

    [Test]
    public void NoSight_NeverAims()
    {
        Ingest(12f, 180f, lineOfSight: false);
        for (int i = 0; i < 120; i++)
        {
            Tick();
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void Aim_FacingLocked()
    {
        Aim();
        Vector2 braced = Facing;
        Ingest(12f, 195f);
        Tick();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
        Assert.That(Facing, Is.EqualTo(braced));
    }

    [Test]
    public void Release_HeroInCone_FiresAtHero()
    {
        Aim();
        Vector3 target = Ingest(12f, 190f, velocity: Vector3.UnitX);
        Tick(Windup);

        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(target));
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Recover));

        Vector2 braced = Facing;
        Ingest(12f, 250f);
        Tick(1f);
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Recover));
        Assert.That(Vector2.Dot(braced, Facing), Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-5f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
    }

    [Test]
    public void Release_HeroCircledOut_Drops()
    {
        Aim();
        Ingest(6f, 205f);
        Tick(Windup);

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Single<EnemyAimDropped>().Id, Is.EqualTo(_agent.Id));
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Approach));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        Tick(0.5f);
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2));
    }

    [Test]
    public void CirclingAt5m_DropsEveryShot()
    {
        const float radius = 5f;
        const float speed = 3f;
        for (int i = 0; i < 600; i++)
        {
            float angle = -90f + (i * Dt * (speed / radius) * (180f / MathF.PI));
            Ingest(radius, angle);
            Tick();
        }

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<EnemyAimDropped>(), Is.GreaterThan(0));
    }

    [Test]
    public void StillHeroAt12m_Fires()
    {
        Ingest(12f, 0f);
        for (int i = 0; i < 240; i++)
        {
            Tick();
        }

        Assert.That(_events.Count<ProjectileFired>(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Reset_FromAim_Idle()
    {
        Ingest(12f, 90f);
        Tick();
        Tick(2f);
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
        Assert.That(Facing.X, Is.EqualTo(-1f).Within(1e-5f));

        Behaviour.Reset();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Idle));
        Ingest(31f, 90f);
        Tick();
        Assert.That(Facing, Is.EqualTo(Vector2.UnitY));
    }

    [Test]
    public void Tick_Bombardier_NoAlloc()
    {
        var silent = new SilentEvents();
        Build(silent);
        int step = 0;

        AllocationAssert.None(() =>
        {
            float angle = step < 60 ? 0f : step < 90 ? 25f : 0f;
            Ingest(6f, angle);
            Tick(0.075f, silent);
            step++;
        }, iterations: 300);

        Assert.That(_projectiles.InFlightCount, Is.GreaterThan(0));
    }

    private void Aim()
    {
        Ingest(12f, 180f);
        Tick();
        Tick();
        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
    }

    private Vector3 Ingest(float distance, float degrees, bool lineOfSight = true, Vector3 velocity = default)
    {
        float radians = degrees * (MathF.PI / 180f);
        Vector3 target = distance * new Vector3(MathF.Sin(radians), 0f, MathF.Cos(radians));
        _snapshot.Clear();
        _snapshot.PlayerPosition = target;
        _snapshot.PlayerVelocity = velocity;
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense = new EnemySense { Id = _agent.Id, HasLineOfSight = lineOfSight };
        _enemies.Ingest(_snapshot);
        return target;
    }

    private void Tick(float dt = Dt, IDomainEvents events = null)
    {
        _intents.Clear();
        _now += dt;
        Behaviour.Tick(new EnemyTickContext(dt, _now, _player, _intents, events ?? _events, _projectiles, _enemies));
    }

    private void Build(IDomainEvents events)
    {
        var character = new CharacterSpec(new ContentId("character.oathbound"),
            new LocKey("character.oathbound.name"), new LocKey("character.oathbound.description"), 140f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f));
        var catalog = new ContentCatalog(new[] { character }, new[]
        {
            Spec(Beetle, EnemyBehaviourKind.Spitter, Bombardier),
            Spec(Pitcher, EnemyBehaviourKind.Spitter),
        });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _agent = _enemies.Spawn(Beetle, Vector3.Zero);
        _now = 0f;
    }

    private static EnemySpec Spec(ContentId id, EnemyBehaviourKind kind, BombardierSpec bombardier = null,
        SlingerSpec slinger = null)
    {
        ProjectileSpec projectile = kind == EnemyBehaviourKind.Spitter
            ? bombardier is null ? new ProjectileSpec(14f, 7f, 1.6f) : new ProjectileSpec(12f, 6f, 1.6f)
            : null;
        return new EnemySpec(id, new LocKey(id.Value + ".name"), 28f, 2.8f, 3, 7, 21f, false, 12f,
            1.2f, Windup, 2.3f, 30f, kind, projectile: projectile, slinger: slinger, bombardier: bombardier);
    }
}
