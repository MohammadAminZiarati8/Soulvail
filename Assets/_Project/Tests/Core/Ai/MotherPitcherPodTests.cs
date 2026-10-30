using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-13e rules 1–7: flight, planting, hatching and bounded pending pods.</summary>
[TestFixture]
public sealed class MotherPitcherPodTests
{
    private const string BodyId = "enemy.mother_pitcher", BudId = "enemy.pitcher_bud";
    private const string FrogId = "enemy.frog", OathboundId = "character.oathbound";
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private MotherPitcherBehaviour _mother;
    private float _now;
    private Vector3[] _ground;

    [SetUp]
    public void SetUp()
    {
        _now = 0f;
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, 32);
        _projectiles = new ProjectileSystem(_events, 128);
        var random = new FixedRandom();
        _enemies = new EnemySystem(new ContentCatalog(new[] { Oathbound() },
            new[] { Body(BodyId, 1000f), Body(BudId, 10f), Body(FrogId, 10f) }),
            _events, random, new DepthScaling(Scalings.Design()), 32);
        _agent = _enemies.Spawn(new ContentId(BodyId), new Vector3(0f, 20f, 0f));
        _agent.Blackboard.SelfPosition = _agent.Position;
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitY;
        _agent.Blackboard.PlayerPosition = new Vector3(0f, 0f, 5f);
        _player.Blackboard.PlayerPosition = _agent.Blackboard.PlayerPosition;
        _mother = new MotherPitcherBehaviour(_agent, _projectiles, random.Misc,
            new ContentId(BudId), new ContentId(FrogId));
        _ground = new[] { new Vector3(7f, 0f, 0f) };
        _events.Clear();
    }

    [Test]
    public void Mother_Pod_LobsAFrogShotOntoOpenGround()
    {
        Pod();
        var shot = _events.Single<ProjectileFired>();
        Assert.That(shot.SpecId, Is.EqualTo(new ContentId(FrogId)));
        Assert.That(shot.SourceId, Is.EqualTo(_agent.Id));
        Assert.That(shot.Origin, Is.EqualTo(_agent.Position));
        Assert.That(shot.Target, Is.EqualTo(_ground[0]));
        Assert.That(shot.FlightTime, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void Mother_Pod_NoGround_LobsFourMetresAway()
    {
        _ground = Array.Empty<Vector3>();
        Pod();
        Assert.That(_events.Single<ProjectileFired>().Target,
            Is.EqualTo(_agent.Position - new Vector3(0f, 0f, 4f)));
    }

    [Test]
    public void Mother_Pod_PlantsWhenTheFlightEnds()
    {
        Pod();
        int id = _events.Single<ProjectileFired>().Id;
        Step(0.99f);
        Assert.That(_events.Count<PodPlanted>(), Is.Zero);
        Step(0.02f);
        var planted = _events.Single<PodPlanted>();
        Assert.That(planted.Id, Is.EqualTo(id));
        Assert.That(planted.BossId, Is.EqualTo(_agent.Id));
        Assert.That(planted.At, Is.EqualTo(_ground[0]));
        Assert.That(planted.Seconds, Is.EqualTo(1f));
        Step(0.1f);
        Assert.That(_events.Count<PodPlanted>(), Is.EqualTo(1));
    }

    [Test]
    public void Mother_Pod_HatchesAFrogASecondLater()
    {
        Pod();
        Step(1.01f);
        Step(0.99f);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
        Step(0.02f);
        var frog = _events.Single<EnemySpawned>();
        Assert.That(frog.SpecId, Is.EqualTo(new ContentId(FrogId)));
        Assert.That(frog.Position, Is.EqualTo(_ground[0]));
        Assert.That(_events.Single<PodHatched>().Id, Is.EqualTo(_events.Single<PodPlanted>().Id));
        Step(1f);
        Assert.That(_events.Count<PodHatched>(), Is.EqualTo(1));
    }

    [Test]
    public void Mother_Pod_FullRegistry_HatchesNothing()
    {
        Pod();
        Step(1.01f);
        while (_enemies.Registry.AliveCount < _enemies.Registry.Capacity)
            _enemies.Spawn(new ContentId(BudId), Vector3.Zero);
        _events.Clear();
        Step(1.01f);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
        Assert.That(_events.Count<PodHatched>(), Is.EqualTo(1));
    }

    [Test]
    public void Mother_Pod_PendingPodsCountAgainstTheCap()
    {
        for (int i = 0; i < 2; i++)
            _enemies.Spawn(new ContentId(FrogId), Vector3.Zero);
        Pod();
        // Keep the clock on release while advancing the attack loop: the pending pod stays in flight.
        for (int i = 0; i < 3; i++)
        {
            Tick(1f, _now);
            Tick(2f, _now);
            if (i < 2)
                Tick(2.5f, _now);
        }
        Assert.That(_events.Of<EnemyTelegraph>()[2].Move, Is.EqualTo(MotherPitcherBehaviour.BarrageMove));
        Assert.That(_events.Count<PodHatched>(), Is.Zero);
    }

    [Test]
    public void Mother_Pod_PendingSpotsAreReserved()
    {
        _ground = new[] { new Vector3(7f, 0f, 0f), new Vector3(-7f, 0f, 0f) };
        Pod();
        Vector3 first = _events.Single<ProjectileFired>().Target;
        _mother.OnPhaseEntered(1);
        // A frozen clock keeps the first pod pending while a second move cycle releases.
        for (int i = 0; i < 3; i++)
        {
            Tick(2f, _now);
            Tick(2.5f, _now);
            if (i < 2)
                Tick(1f, _now);
        }
        var shots = _events.Of<ProjectileFired>();
        Assert.That(shots[shots.Count - 1].SpecId, Is.EqualTo(new ContentId(FrogId)));
        Assert.That(shots[shots.Count - 1].Target, Is.Not.EqualTo(first));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Mother_Pod_SurvivesACrossing_ResetDropsIt(bool planted)
    {
        Pod();
        if (planted)
            Step(1.01f);
        _mother.OnPhaseEntered(1);
        if (!planted)
            Step(1.01f);
        Step(1.01f);
        Assert.That(_events.Count<PodHatched>(), Is.EqualTo(1));
        SetUp();
        Pod();
        if (planted)
            Step(1.01f);
        _mother.Reset();
        _events.Clear();
        Step(1.1f);
        Step(1.1f);
        Assert.That(_events.Count<PodPlanted>(), Is.Zero);
        Assert.That(_events.Count<PodHatched>(), Is.Zero);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void Mother_Pod_LandingOnThePlayerHurts()
    {
        Pod();
        _projectiles.Tick(_now + 1.01f, _ground[0], _player, _enemies);
        var damage = _events.Single<PlayerDamaged>();
        Assert.That(damage.ToHp + damage.ToShield, Is.EqualTo(_agent.ContactDamage.Value));
        Assert.That(_events.Single<ProjectileImpacted>().Id, Is.EqualTo(_events.Single<ProjectileFired>().Id));
    }

    [Test]
    public void Mother_Pod_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var random = new FixedRandom();
        var projectiles = new ProjectileSystem(silent, 32);
        var enemies = new EnemySystem(new ContentCatalog(new[] { Oathbound() },
            new[] { Body(BodyId, 1000f), Body(FrogId, 10f), Body(BudId, 10f) }),
            silent, random, new DepthScaling(Scalings.Design()), 32);
        EnemyAgent agent = enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        var mother = new MotherPitcherBehaviour(agent, projectiles, random.Misc,
            new ContentId(BudId), new ContentId(FrogId));
        EnemyAgent warm = enemies.Spawn(new ContentId(FrogId), Vector3.Zero);
        enemies.Registry.Despawn(warm.Id);
        AllocationAssert.None(() =>
        {
            mother.Reset();
            float now = 0f;
            for (int i = 0; i < 3; i++)
            {
                _intents.Clear();
                now += 2f;
                mother.Tick(new EnemyTickContext(2f, now, _player, _intents, silent, projectiles, enemies, openGround: _ground));
                _intents.Clear();
                now += 2.5f;
                mother.Tick(new EnemyTickContext(2.5f, now, _player, _intents, silent, projectiles, enemies, openGround: _ground));
                _intents.Clear();
                now += 1.01f;
                mother.Tick(new EnemyTickContext(1.01f, now, _player, _intents, silent, projectiles, enemies, openGround: _ground));
            }
            _intents.Clear();
            mother.Tick(new EnemyTickContext(1.01f, now + 1.01f, _player, _intents, silent, projectiles, enemies));
            projectiles.Clear();
            enemies.Registry.Despawn(enemies.Registry.Alive[enemies.Registry.AliveCount - 1].Id);
        });
    }

    private void Pod()
    {
        for (int i = 0; i < 2; i++)
        {
            Step(2f);
            Step(2.5f);
            Step(1f);
        }
        Step(2f);
        _projectiles.Tick(_now, new Vector3(100f, 0f, 100f), _player, _enemies);
        _events.Clear();
        Step(0.81f);
    }

    private void Step(float dt) { _now += dt; Tick(dt, _now); }
    private void Tick(float dt, float now)
    {
        _intents.Clear();
        _mother.Tick(new EnemyTickContext(dt, now, _player, _intents, _events,
            _projectiles, _enemies, openGround: _ground));
    }
    private static EnemySpec Body(string id, float hp) => new EnemySpec(
        new ContentId(id), new LocKey(id + ".name"), hp, 0f, 1, 4, 12f, false,
        10f, 1f, 0.6f, 0.6f, 40f, id == BodyId ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
