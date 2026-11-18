using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class ShardbackBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private ShardbackStats _stats;
    private ShardbackBehaviour _boss;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _stats = new ShardbackStats();
        _boss = new ShardbackBehaviour(_agent, _stats);
        _now = 0f;
        Place(Vector3.UnitZ * 2f);
    }

    [Test]
    public void Approach_LocksGeometry()
    {
        Place(Vector3.UnitZ * 10.01f);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitX;
        Tick(0.1f);
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.UnitX * 2f));
        Place(Vector3.UnitZ * 10f);
        Tick(0f);
        ShardbackArmed armed = LastArm();
        Assert.That(armed.BossId, Is.EqualTo(_agent.Id));
        Assert.That(armed.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(armed.Facing, Is.EqualTo(Vector2.UnitY));
        Assert.That(armed.Reach, Is.EqualTo(12f));
        Assert.That(armed.HalfWidth, Is.EqualTo(0.6f));
        Assert.That(armed.AngleDegrees, Is.EqualTo(25f));
        Assert.That(armed.Seconds, Is.EqualTo(1f));
        Place(Vector3.UnitZ * 12f);
        _agent.Blackboard.SelfPosition = Vector3.UnitX * 50f;
        _stats.FanLength.Base = 1f;
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(86f));
        Assert.That(_events.Single<ShardbackImpact>().Origin, Is.EqualTo(armed.Origin));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(armed.Facing));
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(3));
        AssertStanding();
    }

    [TestCase(0f, 0f, true)] // Three overlapping strips, one hit.
    [TestCase(0.6f, 12f, true)]
    [TestCase(-0.6f, 12f, true)]
    [TestCase(0.601f, 12f, false)]
    [TestCase(0f, 12.001f, false)]
    [TestCase(0f, -0.001f, false)]
    [TestCase(2f, 8f, false)] // Gap between centre and side strip.
    [TestCase(3.380946f, 7.250462f, true)] // +25 degree strip.
    [TestCase(-3.380946f, 7.250462f, true)]
    [TestCase(float.NaN, 0f, false)]
    [TestCase(float.PositiveInfinity, 0f, false)]
    public void Fan_DeadlineAndEdges(float x, float z, bool hit)
    {
        Tick(0f);
        Place(new Vector3(x, 90f, z));
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 86f : 100f));
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(1.25f));
        AssertStanding();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Burst_PhasesAndDisc(int phase)
    {
        _boss.OnPhaseEntered(phase);
        for (int i = 0; i < 4; i++)
        {
            Tick(0f);
            bool burst = phase > 0 && i % 2 == 1;
            Assert.That(LastArm().Move, Is.EqualTo(burst ? ShardbackMove.Burst : ShardbackMove.Fan));
            Tick(LastArm().Seconds);
            Tick(_events.Of<ShardbackImpact>().Last().RecoverySeconds);
        }
    }

    [TestCase(0f, true)]
    [TestCase(4f, true)]
    [TestCase(4.001f, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void Burst_PhasesAndDisc_Edges(float distance, bool hit)
    {
        ArmBurst();
        Assert.That(LastArm().Reach, Is.EqualTo(4f));
        Assert.That(LastArm().HalfWidth, Is.Zero);
        Assert.That(LastArm().AngleDegrees, Is.Zero);
        Place(new Vector3(distance, 80f, 0f));
        _agent.Blackboard.SelfPosition = Vector3.UnitZ * 50f;
        _stats.BurstRadius.Base = 100f;
        Tick(0.6f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.6f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 84f : 100f));
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(2f));
        Assert.That(_events.Single<ShardbackImpact>().Origin, Is.EqualTo(Vector3.Zero));
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void Burst_PhasesAndDisc_Protection(bool burst, int protection)
    {
        if (burst)
            ArmBurst();
        else
            Tick(0f);
        float tell = LastArm().Seconds;
        Tick(tell - 0.1f);
        if (protection == 0)
            _agent.Blackboard.QuarryIsADecoy = true;
        else if (protection == 1)
        {
            _player.Charge.Request(_now);
            _player.Tick(0f, _now, new WorldSnapshot(8), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(_player.Charge.IsInvulnerable, Is.True);
        }
        else
            _player.ApplyDamage(1f, _now);
        float hp = _player.Health.Current;
        Tick(0.11f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Recovery_CoarseTick(bool burst)
    {
        if (burst)
            ArmBurst();
        else
            Tick(0f);
        Tick(100f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
        float recovery = _events.Single<ShardbackImpact>().RecoverySeconds;
        Tick(recovery - 0.25f);
        AssertStanding();
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(1));
        Tick(100f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(2));
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
        Tick(LastArm().Seconds - 0.25f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
        Tick(0.25f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(2));
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void PhaseOrReset_Cancels(bool reset, int pending)
    {
        _boss.OnPhaseEntered(2);
        Tick(0f);
        if (pending > 0)
            Tick(1f);
        if (pending > 1)
            Tick(1.25f);
        _events.Clear();
        float hp = _player.Health.Current;
        if (reset)
            _boss.Reset();
        else
            _boss.OnPhaseEntered(1);
        Tick(100f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);
        Assert.That(LastArm().Move, Is.EqualTo(ShardbackMove.Fan));
        Tick(1f);
        Tick(1.25f);
        Assert.That(LastArm().Move, Is.EqualTo(reset ? ShardbackMove.Fan : ShardbackMove.Burst));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            player.Health.Reset();
            _boss.Reset();
            Place(Vector3.UnitZ * 11f);
            _boss.Tick(new EnemyTickContext(1f, 0f, player, _intents, silent, _projectiles, _enemies));
            Place(Vector3.UnitZ * 2f);
            for (int phase = 0; phase < 3; phase++)
            {
                _boss.OnPhaseEntered(phase);
                for (int i = 0; i < 12; i++)
                    _boss.Tick(new EnemyTickContext(2f, i * 2f, player, _intents, silent, _projectiles, _enemies));
            }
        }, 100);
    }

    [Test]
    public void InvalidInputs_DoNotPoisonState()
    {
        Assert.Throws<ArgumentNullException>(() => new ShardbackBehaviour(null, _stats));
        Assert.Throws<ArgumentNullException>(() => new ShardbackBehaviour(_agent, null));
        Place(new Vector3(float.NaN, 0f, 0f));
        Tick(0f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.Zero);
        AssertStanding();
        Place(Vector3.UnitZ * 20f);
        _agent.Blackboard.PathDirectionToPlayer = new Vector2(float.PositiveInfinity, 0f);
        Tick(0f);
        AssertStanding();
        Place(Vector3.UnitZ * 2f);
        Tick(0f);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EnemyTickContext(float.NaN, _now, _player, _intents, _events, _projectiles, _enemies));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EnemyTickContext(float.PositiveInfinity, _now, _player, _intents, _events, _projectiles, _enemies));
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);
        Tick(1f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
    }

    private void ArmBurst()
    {
        _boss.OnPhaseEntered(1);
        Tick(0f);
        Place(Vector3.UnitX * 8f); // Miss the fan.
        Tick(1f);
        Place(Vector3.UnitZ * 2f);
        _events.Clear();
        Tick(1.25f);
    }

    private ShardbackArmed LastArm() => _events.Of<ShardbackArmed>().Last();
    private void AssertStanding() => Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

    private void Tick(float dt)
    {
        _now += dt;
        _boss.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
    }

    private void Place(Vector3 player)
    {
        _agent.Blackboard.SelfPosition = Vector3.Zero;
        _agent.Blackboard.PlayerPosition = player;
        var flat = new Vector2(player.X, player.Z);
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
    }

    private static EnemySpec Body() => new(new ContentId("enemy.shardback"), new LocKey("enemy.shardback.name"),
        500f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
