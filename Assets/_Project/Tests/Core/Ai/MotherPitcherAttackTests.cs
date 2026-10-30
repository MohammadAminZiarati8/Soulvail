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

/// <summary>JG-12c rules 1–8, in specification order; JG-13a rule 1's 2.5 s spray.</summary>
[TestFixture]
public sealed class MotherPitcherAttackTests
{
    private const string BodyId = "enemy.mother_pitcher";
    private const string BudId = "enemy.pitcher_bud";
    private const string FrogId = "enemy.frog";
    private const string OathboundId = "character.oathbound";
    private const int Capacity = 32;
    private const float Tolerance = 1e-4f;
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private MotherPitcherBehaviour _mother;
    private FixedRandom _random;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, 32);
        _random = new FixedRandom().SetMisc(0.125f, 0.25f);
        _enemies = new EnemySystem(Catalog(), _events, _random,
            new DepthScaling(Scalings.Design()), Capacity);
        _agent = _enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        _mother = Mother(_agent, _projectiles);
        Place(new Vector3(0f, 0f, 5f));
        _events.Clear();
    }

    // Rule 1.
    [Test]
    public void Mother_Loop_BarrageSprayPod()
    {
        Arm();
        Finish();
        Arm();
        Finish();
        Arm();
        Finish();
        Arm();
        var moves = _events.Of<EnemyTelegraph>();
        Assert.That(moves.Count, Is.EqualTo(4));
        Assert.That(new[] { moves[0].Move, moves[1].Move, moves[2].Move, moves[3].Move },
            Is.EqualTo(new[] { 1, 2, 3, 1 }));
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(1));
    }

    [Test]
    public void Mother_Pod_SkippedAt3Frogs()
    {
        for (int i = 0; i < 3; i++)
            _enemies.Spawn(new ContentId(FrogId), Vector3.Zero);
        Arm();
        Finish();
        Arm();
        Finish();
        Arm();
        Assert.That(_events.Of<EnemyTelegraph>()[2].Move, Is.EqualTo(1));
        Finish();
        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(3));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(10));
        // Registered corpses do not count toward the cap.
        foreach (EnemyAgent agent in _enemies.Registry.Alive)
        {
            if (agent.Spec.Id == new ContentId(FrogId))
                _enemies.ApplyDamage(agent.Id, 100f, _now, _player);
        }
        Arm();
        Finish();
        Arm();
        Finish();
        Arm();
        Assert.That(_events.Of<EnemyTelegraph>()[5].Move, Is.EqualTo(3));
        Finish();
        Step(1f);
        Assert.That(_events.Count<PodHatched>(), Is.EqualTo(1));
    }

    // Rule 2: the centre is read on release, and one Misc draw turns the entire cross.
    [Test]
    public void Mother_Barrage_TelegraphsThenLobsFive()
    {
        Step(1.99f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
        Step(0.02f);
        Assert.That(_events.Single<EnemyTelegraph>().Duration, Is.EqualTo(0.8f));
        Assert.That(_events.Single<EnemyTelegraph>().Id, Is.EqualTo(_agent.Id));
        Step(0.79f);
        Assert.That(_projectiles.InFlightCount, Is.Zero);
        Place(new Vector3(4f, 2f, 6f));
        Step(0.02f);
        var shots = _events.Of<ProjectileFired>();
        Assert.That(shots.Count, Is.EqualTo(5));
        Assert.That(shots[0].Target, Is.EqualTo(_agent.Blackboard.PlayerPosition));
        for (int i = 1; i < 5; i++)
        {
            float angle = MathF.PI / 4f + ((i - 1) * MathF.PI / 2f);
            Vector3 expected = shots[0].Target + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 3.5f;
            Assert.That(Vector3.Distance(shots[i].Target, expected), Is.LessThan(Tolerance));
        }
        Assert.That(_random.Capture().Misc, Is.EqualTo(1));
    }

    [TestCase(1.59f, true)]
    [TestCase(1.61f, false)]
    public void Mother_Barrage_LobsAreSpit(float offset, bool hits)
    {
        Arm();
        Finish();
        var shots = _events.Of<ProjectileFired>();
        foreach (ProjectileFired shot in shots)
        {
            Assert.That(shot.SpecId, Is.EqualTo(_agent.Spec.Id));
            Assert.That(shot.SourceId, Is.EqualTo(_agent.Id));
            Assert.That(shot.Origin, Is.EqualTo(Vector3.Zero));
            Assert.That(shot.FlightTime, Is.EqualTo(shot.Target.Length() / 7f).Within(Tolerance));
        }
        _events.Clear();
        // 1.59 metres from the central impact, outside every surrounding lob.
        _projectiles.Tick(_now + 10f, new Vector3(0f, 0f, 5f + offset), _player, _enemies);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hits ? 1 : 0));
        if (hits)
        {
            var damage = _events.Single<PlayerDamaged>();
            Assert.That(damage.ToHp + damage.ToShield, Is.EqualTo(_agent.ContactDamage.Value));
        }
        Assert.That(_events.Count<ProjectileImpacted>(), Is.EqualTo(5));
    }

    // Rule 3.
    [Test]
    public void Mother_Spray_HoldsItsAim()
    {
        Spray();
        SprayArmed armed = _events.Single<SprayArmed>();
        Assert.That(armed.Id, Is.EqualTo(_agent.Id));
        Assert.That(armed.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(armed.Direction, Is.EqualTo(Vector2.UnitY));
        Assert.That(armed.HalfAngleDeg, Is.EqualTo(40f));
        Assert.That(armed.Range, Is.EqualTo(12f));
        Place(new Vector3(5f, 0f, 0f));
        Step(2.49f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
        Step(0.02f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.False);
    }

    // JG-13a rule 1: the bite is decided at 2.5 s and on no tick before.
    [Test]
    public void Mother_Spray_WindsUpForTwoAndAHalfSeconds()
    {
        Spray();
        Assert.That(_events.Single<EnemyTelegraph>().Duration, Is.EqualTo(2.5f));
        Assert.That(_events.Single<SprayArmed>().Seconds, Is.EqualTo(2.5f));
        Step(2.49f);
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
        Step(0.02f);
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(1));
    }

    [TestCase(0f, 5f)]
    [TestCase(40f, 12f)]
    [TestCase(0f, 0f)]
    public void Mother_Spray_InTheWedgeInSight_Hits(float angle, float distance)
    {
        Spray();
        float radians = angle * MathF.PI / 180f;
        Place(new Vector3(MathF.Sin(radians) * distance, 50f, MathF.Cos(radians) * distance));
        _events.Clear();
        Step(2.5f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.True);
        Assert.That(_events.Single<SprayFired>().Id, Is.EqualTo(_agent.Id));
        var hit = _events.Single<PlayerDamaged>();
        Assert.That(hit.ToHp + hit.ToShield, Is.EqualTo(_agent.ContactDamage.Value * 2f));
        Step(0.99f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    [Test]
    public void Mother_Spray_BehindCover_Misses()
    {
        Spray();
        _agent.Blackboard.HasLineOfSight = false;
        Step(2.5f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.False);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [TestCase(41f, 5f)]
    [TestCase(180f, 5f)]
    [TestCase(0f, 12.01f)]
    public void Mother_Spray_OutsideTheWedge_Misses(float angle, float distance)
    {
        Spray();
        float radians = angle * MathF.PI / 180f;
        Place(new Vector3(MathF.Sin(radians) * distance, 0f, MathF.Cos(radians) * distance));
        Step(2.5f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.False);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    // Rule 5: even a large gap tick cannot also finish a wind-up.
    [Test]
    public void Mother_EveryWindup_AtLeastPointSix()
    {
        for (int i = 0; i < 3; i++)
        {
            Step(10f);
            EnemyTelegraph telegraph = _events.Of<EnemyTelegraph>()[i];
            Assert.That(telegraph.Duration, Is.GreaterThanOrEqualTo(0.6f));
            int shots = _events.Count<ProjectileFired>();
            int sprays = _events.Count<SprayFired>();
            int frogs = _events.Count<EnemySpawned>();
            Step(0.59f);
            Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(shots));
            Assert.That(_events.Count<SprayFired>(), Is.EqualTo(sprays));
            Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(frogs));
            Step(10f);
            Step(1f);
        }
    }

    // Rule 6.
    [Test]
    public void Mother_Crossing_ClearsAnArmedSpray()
    {
        Spray();
        Assert.That(_projectiles.InFlightCount, Is.EqualTo(5));
        _mother.OnPhaseEntered(1);
        _mother.OnPhaseEntered(2);
        Assert.That(_events.Single<SprayCleared>().Id, Is.EqualTo(_agent.Id));
        Assert.That(_projectiles.InFlightCount, Is.EqualTo(5));
        _events.Clear();
        Step(1.99f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
        Step(0.02f);
        Assert.That(_events.Single<EnemyTelegraph>().Move, Is.EqualTo(1));
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
        _projectiles.Tick(_now + 10f, new Vector3(50f, 0f, 50f), _player, _enemies);
        Assert.That(_events.Count<ProjectileImpacted>(), Is.EqualTo(5));
    }

    [Test]
    public void Boss_Crossing_TellsAListeningInner()
    {
        var listener = new ListeningInner();
        var spec = new BossSpec(new ContentId("boss.mother_pitcher"), new ContentId(BodyId),
            new[] { new BossPhaseSpec(1f, Array.Empty<AddWave>()),
                new BossPhaseSpec(0.66f, Array.Empty<AddWave>()),
                new BossPhaseSpec(0.33f, Array.Empty<AddWave>()) }, 1.5f);
        var boss = new BossBehaviour(_agent, spec, listener);
        boss.Tick(Context(0.1f));
        Assert.That(listener.Calls, Is.Zero);
        _enemies.ApplyDamage(_agent.Id, 350f, _now, _player);
        boss.Tick(Context(0.1f));
        Assert.That(listener.Calls, Is.EqualTo(1));
        Assert.That(listener.Phase, Is.EqualTo(1));
        Assert.That(listener.Ticks, Is.EqualTo(1));
        boss.Tick(Context(2f));
        _enemies.ApplyDamage(_agent.Id, 350f, _now, _player);
        boss.Tick(Context(0.1f));
        Assert.That(listener.Calls, Is.EqualTo(2));
        Assert.That(listener.Phase, Is.EqualTo(2));
        Assert.That(typeof(IBossPhaseListener).IsAssignableFrom(typeof(WardenBehaviour)), Is.False);
        Assert.That(typeof(IBossPhaseListener).IsAssignableFrom(typeof(CroakerBehaviour)), Is.False);
    }

    // Rule 7: traverse two complete loops, including every wind-up and recovery.
    [Test]
    public void Mother_NeverMoves()
    {
        for (int i = 0; i < 600; i++)
        {
            Step(0.1f);
            Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1));
            Assert.That(_intents.LastEnemyMove.Id, Is.EqualTo(_agent.Id));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        }
    }

    // Rule 8: a silent bus avoids the recording fake's deliberate boxing.
    [Test]
    public void Mother_Attacks_AllocateNothing()
    {
        var silent = new SilentEvents();
        var projectiles = new ProjectileSystem(silent, 32);
        var enemies = new EnemySystem(Catalog(), silent, _random,
            new DepthScaling(Scalings.Design()), Capacity);
        var player = new PlayerCombat(Oathbound(), silent, _intents, Capacity);
        EnemyAgent agent = enemies.Spawn(new ContentId(BodyId), Vector3.Zero);
        MotherPitcherBehaviour mother = Mother(agent, projectiles);
        agent.Blackboard.HasLineOfSight = true;
        player.Health.SetExternalInvulnerable(true);
        agent.Blackboard.DirectionToPlayer = Vector2.UnitY;
        agent.Blackboard.PlayerPosition = new Vector3(0f, 0f, 5f);
        // Warm the Frog pool before measuring the pod's actual spawn path.
        EnemyAgent frog = enemies.Spawn(new ContentId(FrogId), Vector3.Zero);
        enemies.Registry.Despawn(frog.Id);
        AllocationAssert.None(() =>
        {
            mother.Reset();
            for (int i = 0; i < 3; i++)
            {
                _intents.Clear();
                mother.Tick(new EnemyTickContext(2f, 0f, player, _intents, silent, projectiles, enemies));
                _intents.Clear();
                mother.Tick(new EnemyTickContext(2.5f, 1f, player, _intents, silent, projectiles, enemies));
                _intents.Clear();
                mother.Tick(new EnemyTickContext(1f, 2f, player, _intents, silent, projectiles, enemies));
            }
            _intents.Clear();
            mother.Tick(new EnemyTickContext(1f, 4f, player, _intents, silent, projectiles, enemies));
            _intents.Clear();
            mother.Tick(new EnemyTickContext(1f, 5f, player, _intents, silent, projectiles, enemies));
            projectiles.Clear();
            // Return the spawned Frog to its warmed pool, keeping the pod path live.
            ReadOnlySpan<EnemyAgent> alive = enemies.Registry.Alive;
            enemies.Registry.Despawn(alive[alive.Length - 1].Id);
        });
    }

    private void Place(Vector3 position)
    {
        _agent.Blackboard.PlayerPosition = position;
        var flat = new Vector2(position.X, position.Z);
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.HasLineOfSight = true;
    }

    private void Step(float dt)
    {
        _now += dt;
        _intents.Clear();
        _mother.Tick(Context(dt));
    }

    private EnemyTickContext Context(float dt) => new EnemyTickContext(
        dt, _now, _player, _intents, _events, _projectiles, _enemies);
    private void Arm() => Step(2f);
    private void Finish() { Step(MotherPitcherBehaviour.SprayTelegraphSeconds); Step(1f); }
    private void Spray() { Arm(); Finish(); _events.Clear(); Arm(); }

    private MotherPitcherBehaviour Mother(EnemyAgent agent, ProjectileSystem projectiles) =>
        new MotherPitcherBehaviour(agent, projectiles, _random.Misc, new ContentId(BudId), new ContentId(FrogId));
    private static ContentCatalog Catalog() => new ContentCatalog(new[] { Oathbound() },
        new[] { Body(BodyId, 1000f), Body(BudId, 10f), Body(FrogId, 10f) });

    private sealed class ListeningInner : IEnemyBehaviour, IBossPhaseListener
    {
        public int Calls;
        public int Phase;
        public int Ticks;
        public void OnPhaseEntered(int phase) { Calls++; Phase = phase; }
        public void Tick(in EnemyTickContext ctx) { Ticks++; }
        public void Reset() { }
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
