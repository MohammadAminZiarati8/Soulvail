using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class ApexDashTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private ApexBehaviour _apex;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _projectiles = new ProjectileSystem(_events, 8);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _lanes = new LaneSystem(_events);
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _apex = new ApexBehaviour(_agent, new ApexStats(), new FixedRandom(0.5f).Misc, _lanes);
        _now = 0f;
        Place(Vector3.Zero, Vector3.UnitZ * 10f);
    }

    // Rule 3.
    [Test]
    public void Dash_ProbesThenOpensLane_FiresAtPointSeven_ThenDashes()
    {
        Place(new Vector3(2f, 0f, 4f), new Vector3(12f, 0f, 4f));
        Tick(1f);
        float armedAt = _now;
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Duration, Is.EqualTo(0.7f));
        Assert.That(telegraph.Move, Is.EqualTo(1));
        LaneProbeIntent probe = _intents.LaneProbes[0];
        Assert.That(probe.EnemyId, Is.EqualTo(_agent.Id));
        Assert.That(probe.From, Is.EqualTo(new Vector3(2f, 0f, 4f)));
        Assert.That(probe.Direction, Is.EqualTo(Vector3.UnitX));
        Assert.That(probe.Reach, Is.EqualTo(10f));
        Assert.That(probe.Radius, Is.EqualTo(0.9f));
        Assert.That(_events.Count<LaneArmed>(), Is.Zero);

        _apex.OnLaneStop(false, 0f);
        Tick(0.1f);
        LaneArmed lane = _events.Single<LaneArmed>();
        Assert.That(lane.Origin, Is.EqualTo(new Vector3(2f, 0f, 4f)));
        Assert.That(lane.Direction, Is.EqualTo(Vector2.UnitX));
        Assert.That(lane.Length, Is.EqualTo(10f));
        Assert.That(lane.HalfWidth, Is.EqualTo(0.9f));
        Assert.That(_now - armedAt + lane.ArmSeconds, Is.EqualTo(0.7f).Within(1e-5f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        _lanes.Tick(armedAt + 0.69f, new Vector3(8f, 0f, 4f), _player);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        _lanes.Tick(armedAt + 0.71f, new Vector3(8f, 0f, 4f), _player);
        Assert.That(_player.Health.Current, Is.EqualTo(70f));

        Tick(0.61f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.UnitX * 22f));
        Place(new Vector3(11.95f, 0f, 4f), new Vector3(14f, 0f, 4f));
        Tick(0.01f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_apex.IsRecovering, Is.True);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.79f);
        Assert.That(_apex.IsRecovering, Is.True);
        Tick(0.02f);
        Assert.That(_apex.IsRecovering, Is.False);
    }

    [Test]
    public void Dash_NoProbeAnswer_OpensAfterTwoTicks()
    {
        Tick(1f);
        Tick(0.1f);
        Assert.That(_events.Count<LaneArmed>(), Is.Zero);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(10f));
        _apex.OnLaneStop(true, 2f);
        Tick(0.51f);
        Assert.That(_events.Count<LaneArmed>(), Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(22f), "A late answer is ignored.");
    }

    [Test]
    public void Dash_ThroughSession_ReportLaneStopReachesHer()
    {
        var random = new FixedRandom(0.5f);
        var session = new RunSession(Catalog(), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), 8, 8, 8);
        session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        var enemies = (EnemySystem)typeof(RunState)
            .GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session.State);
        EnemyAgent agent = enemies.SpawnBoss(new ContentId("boss.apex"), Vector3.Zero);
        var apex = (ApexBehaviour)((BossBehaviour)agent.Behaviour).Inner;
        SessionTick(session, agent, 1f);
        Assert.That(apex.Move, Is.EqualTo(ApexMove.Dash));
        session.ReportLaneStop(agent.Id, true, 6f);
        SessionTick(session, agent, 0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(6f));
    }

    // Rule 4.
    [Test]
    public void Dash_ProbeHitsStone_LaneAndDashStopAtStone_NoDaze()
    {
        Tick(1f);
        _apex.OnLaneStop(true, 6f);
        Tick(0.1f);
        Assert.That(_events.Single<LaneArmed>().Length, Is.EqualTo(6f));
        Tick(0.61f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(22f));
        Place(Vector3.UnitZ * 5.95f, Vector3.UnitZ * 10f);
        Tick(0.01f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_apex.IsRecovering, Is.True);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Count<WarlordDazed>(), Is.Zero);
        Tick(0.81f);
        Assert.That(_apex.IsRecovering, Is.False, "An ordinary recovery, no daze.");
        Tick(0.1f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(5.5f), "She hunts again at once.");
    }

    private void Tick(float dt)
    {
        _now += dt;
        _apex.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes));
    }

    private void SessionTick(RunSession session, EnemyAgent agent, float dt)
    {
        var snapshot = new WorldSnapshot(8) { Dt = dt, PlayerPosition = Vector3.UnitZ * 10f };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = agent.Id;
        sense.Position = Vector3.Zero;
        sense.PathDirectionToPlayer = Vector2.UnitY;
        sense.HasLineOfSight = true;
        _intents.Clear();
        session.Tick(snapshot);
    }

    private void Place(Vector3 self, Vector3 player)
    {
        Vector3 delta = player - self;
        var flat = new Vector2(delta.X, delta.Z);
        _agent.Blackboard.SelfPosition = self;
        _agent.Blackboard.PlayerPosition = player;
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
    }

    private static ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.apex"), Body().Id, new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.apex"), new LocKey("enemy.apex.name"),
        3200f, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
