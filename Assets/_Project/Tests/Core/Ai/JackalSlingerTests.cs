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
public sealed class JackalSlingerTests
{
    private const int Capacity = 16;
    private const float Windup = 0.7f;
    private static readonly ContentId Jackal = new("enemy.jackal");
    private static readonly ContentId Pitcher = new("enemy.spitter");
    private static readonly ContentId Husk = new("enemy.husk");
    private static readonly SlingerSpec Slinger = new(3f, 0.7f, 3f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private EnemyAgent _agent;
    private float _now;

    private SpitterBehaviour Behaviour => (SpitterBehaviour)_agent.Behaviour;
    private EnemyBlackboard Board => _agent.Blackboard;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _snapshot = new WorldSnapshot(Capacity);
        Build(_events);
    }

    [Test]
    public void NoSlinger_SpitterUnchanged()
    {
        _agent = _enemies.Spawn(Pitcher, Vector3.Zero);
        Ingest(9f);
        Tick();
        Tick();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Approach));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(-2.8f * Vector3.UnitZ));
    }

    [Test]
    public void SlingerSpec_RejectsInvalidValuesAndNonSpitter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlingerSpec(0f, 0.7f, 3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlingerSpec(3f, 1.01f, 3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlingerSpec(3f, 0.7f, float.PositiveInfinity));
        Assert.That(Assert.Throws<ArgumentException>(() =>
            Spec(Husk, EnemyBehaviourKind.Chaser, Slinger)).ParamName, Is.EqualTo("slinger"));
    }

    [Test]
    public void Ingest_HeroQuarry_CarriesVelocity()
    {
        Ingest(12f, velocity: new Vector3(3f, 5f, 0f));

        Assert.That(Board.PlayerVelocity, Is.EqualTo(new Vector2(3f, 0f)));
    }

    [Test]
    public void Ingest_PetOrNaN_ZeroVelocity()
    {
        Ingest(12f, velocity: Vector3.UnitX);
        Ingest(12f, velocity: new Vector3(float.NaN, 0f, 1f));
        Assert.That(Board.PlayerVelocity, Is.EqualTo(Vector2.Zero));

        var lures = new LureSystem(_events);
        lures.Drop(Vector3.UnitZ, 0f, 3f);
        Ingest(12f, velocity: Vector3.UnitX, lures: lures);
        Assert.That(Board.QuarryIsADecoy, Is.True);
        Assert.That(Board.PlayerVelocity, Is.EqualTo(Vector2.Zero));

        // Pet hunting is available to melee enemies. Perception must clear their hero velocity too.
        _agent = _enemies.Spawn(Husk, Vector3.Zero);
        Ingest(12f, velocity: Vector3.UnitX);
        Assert.That(Board.PlayerVelocity, Is.EqualTo(Vector2.UnitX));

        var pets = new PetSystem(new PetSpec(new ContentId("pet.hound"), 70f, 4f, 7f, 0.25f, 1.4f, 1.5f),
            _events, _intents);
        pets.SpawnBeside(Vector3.Zero, 0f);
        ref EnemySense pet = ref _snapshot.AddPet();
        pet.Id = pets.Pets[0].Id;
        pet.Position = Vector3.Zero;
        pets.Ingest(_snapshot);
        _enemies.Mark(_agent.Id, 5f);
        pets.Tick(0.1f, 0f, Vector3.Zero, _enemies, _player);

        Ingest(12f, velocity: Vector3.UnitX, pets: pets);
        Assert.That(Board.QuarryIsAPet, Is.True);
        Assert.That(Board.PlayerVelocity, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Rushed_EntersBackoff()
    {
        Rush(8f);

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Backoff));
        Assert.That(Board.StateTimer, Is.Zero);
    }

    [Test]
    public void Backoff_WalksAwaySlowly_FacesHero()
    {
        Rush(8f);
        Tick();

        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(-1.96f).Within(1e-5f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
    }

    [Test]
    public void Backoff_NeverAims()
    {
        Rush(6f);
        Tick(2.9f);

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Backoff));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
    }

    [Test]
    public void Backoff_Clear_ReturnsToApproach()
    {
        Rush(6f);
        Ingest(12f);
        Tick(3f);

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Approach));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void Backoff_Timeout_Throws()
    {
        Rush(6f);
        Tick(3f);

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Aim));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);

        Tick(Windup);

        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(1));
    }

    [Test]
    public void Backoff_TimeoutNoSight_KeepsBacking()
    {
        Rush(6f, lineOfSight: false);
        Tick(3f);

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Backoff));
        Assert.That(Board.StateTimer, Is.Zero);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.LessThan(0f));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
    }

    [Test]
    public void LeadPoint_Still_IsQuarry()
    {
        var quarry = new Vector3(0f, 4f, 6f);

        Assert.That(SpitterBehaviour.LeadPoint(Vector3.Zero, quarry, Vector2.Zero, 6f, 3f),
            Is.EqualTo(quarry));
    }

    [Test]
    public void LeadPoint_Running_LeadsByFlight()
    {
        var quarry = new Vector3(0f, 4f, 6f);

        Assert.That(SpitterBehaviour.LeadPoint(Vector3.Zero, quarry, Vector2.UnitX, 6f, 3f),
            Is.EqualTo(new Vector3(1f, 4f, 6f)));
    }

    [Test]
    public void LeadPoint_Capped()
    {
        Vector3 target = SpitterBehaviour.LeadPoint(Vector3.Zero, 12f * Vector3.UnitZ,
            new Vector2(3.2f, 0f), 7f, 3f);

        Assert.That(target.X, Is.EqualTo(3f).Within(1e-5f));
        Assert.That(target.Z, Is.EqualTo(12f));
    }

    [Test]
    public void Release_Slinger_TargetsLeadPoint()
    {
        Ingest(12f);
        Tick();
        Tick();
        Ingest(12f, velocity: 3.2f * Vector3.UnitX);
        Tick(Windup);

        Vector3 target = _events.Single<ProjectileFired>().Target;
        Assert.That(target.X, Is.EqualTo(3f).Within(1e-5f));
        Assert.That(target.Z, Is.EqualTo(12f));
    }

    [Test]
    public void Reset_FromBackoff_Idle()
    {
        Rush(6f);
        Tick(1f);

        Behaviour.Reset();

        Assert.That(Behaviour.State, Is.EqualTo(SpitterState.Idle));
        Assert.That(Board.StateTimer, Is.Zero);
    }

    [Test]
    public void Tick_Slinger_NoAlloc()
    {
        var silent = new SilentEvents();
        Build(silent);
        Ingest(6f, velocity: Vector3.UnitX);

        AllocationAssert.None(() =>
        {
            _intents.Clear();
            _now += 0.125f;
            Behaviour.Tick(new EnemyTickContext(0.125f, _now, _player, _intents, silent, _projectiles, _enemies));
        }, iterations: 300);

        Assert.That(_projectiles.InFlightCount, Is.GreaterThan(0));
    }

    private void Rush(float distance, bool lineOfSight = true)
    {
        Ingest(distance, lineOfSight);
        Tick();
        Tick();
    }

    private void Ingest(float distance, bool lineOfSight = true, Vector3 velocity = default,
        LureSystem lures = null, PetSystem pets = null)
    {
        _snapshot.Clear();
        _snapshot.PlayerPosition = distance * Vector3.UnitZ;
        _snapshot.PlayerVelocity = velocity;
        ref EnemySense sense = ref _snapshot.AddEnemy();
        sense = new EnemySense { Id = _agent.Id, HasLineOfSight = lineOfSight };
        _enemies.Ingest(_snapshot, lures, pets);
    }

    private void Tick(float dt = 0.125f)
    {
        _intents.Clear();
        _now += dt;
        Behaviour.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
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
            Spec(Jackal, EnemyBehaviourKind.Spitter, Slinger),
            Spec(Pitcher, EnemyBehaviourKind.Spitter),
            Spec(Husk, EnemyBehaviourKind.Chaser),
        });
        _enemies = new EnemySystem(catalog, events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
        _agent = _enemies.Spawn(Jackal, Vector3.Zero);
        _now = 0f;
    }

    private static EnemySpec Spec(ContentId id, EnemyBehaviourKind kind, SlingerSpec slinger = null)
    {
        ProjectileSpec projectile = kind == EnemyBehaviourKind.Spitter
            ? slinger is null ? new ProjectileSpec(14f, 12f, 1.6f) : new ProjectileSpec(12f, 7f, 1.5f)
            : null;
        return new EnemySpec(id, new LocKey(id.Value + ".name"), 28f, 2.8f, 3, 7, 21f, false, 12f,
            1.2f, Windup, 2.3f, 30f, kind, projectile: projectile, slinger: slinger);
    }
}
