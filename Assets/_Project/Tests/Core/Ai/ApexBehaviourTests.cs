using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class ApexBehaviourTests
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
        _apex = new ApexBehaviour(_agent, DashAndRakeStats(), new FixedRandom(0.5f).Misc, _lanes);
        _now = 0f;
        Place(Vector3.Zero, Vector3.UnitZ * 10f);
    }

    // Rule 1.
    [Test]
    public void Hunt_RunsAtPlayer_AtRunSpeed_NeverHidden()
    {
        Place(Vector3.Zero, Vector3.UnitX * 10f);
        Tick(0.1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.UnitX * 5.5f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX));
        // Through a dash and a rake she stays selectable.
        float[] steps = { 0.9f, 0.1f, 0.61f, 0.6f, 0.8f, 1f };
        foreach (float step in steps)
        {
            Tick(step);
            Assert.That(_agent.IsHidden, Is.False);
            Assert.That(_agent.IsVulnerable, Is.True);
        }

        Place(Vector3.Zero, Vector3.UnitZ * 2f);
        for (int i = 0; i < 20; i++)
        {
            Tick(0.1f);
            Assert.That(_agent.IsHidden, Is.False);
            Assert.That(_agent.IsVulnerable, Is.True);
        }
    }

    // Rule 2.
    [Test]
    public void Pick_Close_Rakes()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 3f);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        Assert.That(_intents.LaneProbes, Is.Empty);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Pick_Far_DashOffCooldown_Dashes()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 4f);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Pick_DashOnCooldown_Hunts()
    {
        Tick(1f);
        _apex.OnLaneStop(false, 0f);
        Tick(0.1f);
        Tick(0.61f);
        Place(Vector3.UnitZ * 10f, Vector3.UnitZ * 20f);
        Tick(0.01f);
        Assert.That(_apex.IsRecovering, Is.True);
        Tick(0.8f);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(5.5f));
        Tick(1.6f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Tick(0.2f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
    }

    [Test]
    public void Pick_WaitsMoveGap()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 2f);
        Tick(0.99f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(5.5f));
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        Tick(0.65f);
        Tick(0.4f);
        Assert.That(_apex.IsRecovering, Is.True);
        Tick(0.6f);
        Assert.That(_apex.IsRecovering, Is.False);
        Tick(0.99f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
    }

    // Rule 5.
    [Test]
    public void Rake_TelegraphsOneSecond_TwoConesPointFourApart()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 2f);
        Tick(1f);
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Duration, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(telegraph.Move, Is.EqualTo(2));
        Tick(0.55f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.1f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.3f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Tick(0.1f);
        Assert.That(_player.Health.Current, Is.EqualTo(60f));
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_apex.IsRecovering, Is.True);
    }

    [TestCase(2.5f, 0f, 0f)]
    [TestCase(0f, 0f, 2.7f)]
    public void Rake_PlayerOutsideCone_Unhurt(float x, float y, float z)
    {
        Place(Vector3.Zero, Vector3.UnitZ * 2f);
        Tick(1f);
        Place(Vector3.Zero, new Vector3(x, y, z));
        Tick(0.65f);
        Tick(0.4f);
        Assert.That(_apex.IsRecovering, Is.True);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    // Rule 6.
    [Test]
    public void PhaseEntry_DropsArmedMove()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 2f);
        Tick(1f);
        _apex.OnPhaseEntered(1);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Tick(0.9f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "The dropped rake never swipes.");

        Place(Vector3.Zero, Vector3.UnitZ * 10f);
        Tick(0.11f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        _apex.OnLaneStop(false, 0f);
        Tick(0.1f);
        float firesAt = _now + 0.6f;
        _apex.OnPhaseEntered(2);
        Tick(0.59f); // JG-30c shortens the hunt's move gap to 0.6 s.
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(7.15f).Within(1e-5f), "She hunts, without dashing the cancelled lane.");
        _lanes.Tick(firesAt + 0.01f, Vector3.UnitZ * 5f, _player);
        Assert.That(_player.Health.Current, Is.EqualTo(70f), "The opened lane still fires.");
    }

    // Rule 7.
    [Test]
    public void Telegraphs_AtLeastPointSix()
    {
        Tick(1f);
        float armedAt = _now;
        _apex.OnLaneStop(false, 0f);
        Tick(0.1f);
        LaneArmed lane = _events.Single<LaneArmed>();
        Assert.That(_now - armedAt + lane.ArmSeconds, Is.GreaterThanOrEqualTo(0.6f));
        _lanes.Tick(armedAt + 0.59f, Vector3.UnitZ * 5f, _player);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.61f);
        Place(Vector3.UnitZ * 10f, Vector3.UnitZ * 12f);
        Tick(0.01f);
        Tick(0.8f);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            Assert.That(telegraph.Duration, Is.GreaterThanOrEqualTo(0.6f));
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(2));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var lanes = new LaneSystem(silent);
        var apex = new ApexBehaviour(_agent, DashAndRakeStats(), new FixedRandom(0.5f).Misc, lanes);
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        AllocationAssert.None(() =>
        {
            apex.Reset();
            lanes.Clear();
            _intents.Clear();
            Place(Vector3.Zero, Vector3.UnitZ * 10f);
            apex.Tick(new EnemyTickContext(1f, 1f, player, _intents, silent, projectiles, _enemies));
            apex.OnLaneStop(true, 6f);
            apex.Tick(new EnemyTickContext(0.1f, 1.1f, player, _intents, silent, projectiles, _enemies));
            apex.Tick(new EnemyTickContext(0.61f, 1.71f, player, _intents, silent, projectiles, _enemies));
            Place(Vector3.UnitZ * 6f, Vector3.UnitZ * 8f);
            apex.Tick(new EnemyTickContext(0.01f, 1.72f, player, _intents, silent, projectiles, _enemies));
            apex.Tick(new EnemyTickContext(0.8f, 2.52f, player, _intents, silent, projectiles, _enemies));
            apex.Tick(new EnemyTickContext(1f, 3.52f, player, _intents, silent, projectiles, _enemies));
            apex.Tick(new EnemyTickContext(1.1f, 4.62f, player, _intents, silent, projectiles, _enemies));
            apex.OnPhaseEntered(1);
            lanes.Tick(10f, Vector3.Zero, player);
        }, 100);
    }

    [Test]
    public void Reset_ClearsTimers()
    {
        Tick(1f);
        _apex.OnLaneStop(false, 0f);
        Tick(0.1f);
        Tick(0.61f);
        Place(Vector3.UnitZ * 10f, Vector3.UnitZ * 12f);
        Tick(0.01f);
        Tick(0.8f);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        _apex.Reset();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_apex.IsRecovering, Is.False);
        Place(Vector3.Zero, Vector3.UnitZ * 10f);
        Tick(0.99f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None), "The move gap restarts.");
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash), "The dash cooldown is cleared.");
    }

    private void Tick(float dt)
    {
        _now += dt;
        _apex.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes));
    }

    private static ApexStats DashAndRakeStats()
    {
        var stats = new ApexStats();
        // These JG-30a rows isolate the hunt, dash and rake from JG-30b's higher-priority pounce.
        stats.PounceMinRange.Base = 20f;
        return stats;
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
