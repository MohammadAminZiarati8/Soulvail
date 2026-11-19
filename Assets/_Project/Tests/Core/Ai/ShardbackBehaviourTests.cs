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
        Tick(0.125f);
        ShardbackArmed armed = LastArm();
        Assert.That(armed.BossId, Is.EqualTo(_agent.Id));
        Assert.That(armed.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(armed.Facing, Is.EqualTo(Vector2.UnitY));
        Assert.That(armed.Reach, Is.EqualTo(12f));
        Assert.That(armed.HalfWidth, Is.EqualTo(0.6f));
        Assert.That(armed.AngleDegrees, Is.EqualTo(25f));
        Assert.That(armed.Seconds, Is.EqualTo(1f));
        AnswerFan();
        Place(Vector3.UnitZ * 12f);
        _agent.Blackboard.SelfPosition = Vector3.UnitX * 50f;
        _stats.FanLength.Base = 1f;
        _stats.FanDamage.Base = 1f;
        _stats.FanRecoverySeconds.Base = 1f;
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(86f));
        Assert.That(_events.Single<ShardbackImpact>().Origin, Is.EqualTo(armed.Origin));
        Assert.That(_events.Single<ShardbackImpact>().RecoverySeconds, Is.EqualTo(1.25f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(armed.Facing));
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(3));
        AssertStanding();
    }

    [Test]
    public void Facing_TurnsThroughRecoveryAndApproach_ThenLocksThroughHit()
    {
        Tick(0f);
        AnswerFan();
        Place(Vector3.UnitX * 4f);
        Tick(1f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));

        Tick(0.125f);
        Assert.That(_intents.LastEnemyMove.FacingXZ.X, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.0001f));
        AssertStanding();
        Tick(0.125f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX));
        Tick(0.875f);

        Place(-Vector3.UnitX * 4f);
        Tick(0.125f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(1));
        Tick(0.4f);
        ShardbackArmed charge = LastArm();
        Assert.That(charge.Move, Is.EqualTo(ShardbackMove.Charge));
        Assert.That(charge.Facing, Is.EqualTo(-Vector2.UnitX));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(charge.Facing));
        Answer(0, ShardbackStopKind.None, charge.Reach);

        Place(Vector3.UnitZ * 4f);
        Tick(1.2f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(charge.Facing));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(-Vector3.UnitX * 10f));
        Place(-Vector3.UnitX * 4f);
        _agent.Blackboard.SelfPosition = -Vector3.UnitX * charge.Reach;
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(82f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(charge.Facing));
    }

    [Test]
    public void TellProgress_UsesTheCastDeadline_AndCompletesBeforeImpact()
    {
        Tick(0f);
        ShardbackArmed armed = LastArm();
        Assert.That(_events.Count<ShardbackTellProgress>(), Is.Zero);
        AnswerFan();
        Tick(armed.Seconds * 0.5f);
        ShardbackTellProgress half = _events.Single<ShardbackTellProgress>();
        Assert.That((half.BossId, half.Key, half.Fraction), Is.EqualTo((armed.BossId, armed.Key, 0.5f)));
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);

        Tick(armed.Seconds * 0.5f);
        var order = _events.All.Where(e => e is ShardbackTellProgress or ShardbackImpact).ToArray();
        Assert.That(order.Length, Is.EqualTo(3));
        Assert.That(((ShardbackTellProgress)order[1]).Fraction, Is.EqualTo(1f));
        Assert.That(((ShardbackImpact)order[2]).Key, Is.EqualTo(armed.Key));
    }

    [TestCase(-0.01f)]
    [TestCase(1.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void TellProgress_RejectsFractionsOutsideTheUnitInterval(float fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShardbackTellProgress(_agent.Id, default, fraction));
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
        AnswerFan();
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
    public void Rotation_AllPhases_FullTells(int phase)
    {
        _boss.OnPhaseEntered(phase);
        ShardbackMove[] rotation = phase == 0
            ? new[] { ShardbackMove.Fan, ShardbackMove.Charge }
            : new[] { ShardbackMove.Fan, ShardbackMove.Charge, ShardbackMove.Burst };
        Tick(100f);
        for (int i = 0; i < rotation.Length * 2; i++)
        {
            ShardbackArmed arm = LastArm();
            Assert.That(arm.Move, Is.EqualTo(rotation[i % rotation.Length]));
            Assert.That(arm.Key.Cast, Is.EqualTo((ulong)i + 1));
            if (arm.Move == ShardbackMove.Fan)
                AnswerFan();
            if (arm.Move == ShardbackMove.Charge)
            {
                Assert.That(arm.Facing, Is.EqualTo(Vector2.UnitX), "Reaim after the fan.");
                Answer(0, ShardbackStopKind.Cover, 0f);
            }
            Place(arm.Move == ShardbackMove.Fan ? Vector3.UnitX * 8f : Vector3.UnitZ * 8f);
            float tell = arm.Move == ShardbackMove.Fan ? 1f : 1.2f;
            Assert.That(arm.Seconds, Is.EqualTo(tell));
            Tick(tell - 0.125f);
            AssertStanding();
            Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(i));
            Tick(100f);
            ShardbackImpact impact = _events.Of<ShardbackImpact>().Last();
            Assert.That(impact.Key, Is.EqualTo(arm.Key));
            Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(i + 1));
            float recovery = arm.Move switch
            {
                ShardbackMove.Fan => phase == 2 ? 0f : 1.25f,
                ShardbackMove.Charge => 1.5f,
                _ => 2f
            };
            Assert.That(impact.RecoverySeconds, Is.EqualTo(recovery));
            if (recovery > 0f)
            {
                Tick(recovery - 0.125f);
                AssertStanding();
                Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(i + 1));
            }
            Tick(100f);
        }
        Assert.That(_events.Count<ShardbackFightStarted>(), Is.EqualTo(1));
    }

    [TestCase(0f, 0f, false, true)]
    [TestCase(0f, 0f, true, false)]
    [TestCase(0f, 3f, false, false)]
    [TestCase(0f, 2.99f, false, true)]
    [TestCase(-2.53571f, 5.43785f, false, true)] // Plus lane reaches 7 m.
    [TestCase(2.53571f, 5.43785f, false, false)] // Minus lane stops at 5 m.
    public void Fan_ThreeStops_UnionOnce(float x, float z, bool decoy, bool hit)
    {
        Tick(0f);
        Assert.That(_intents.LaneProbes.Select(p => p.Lane), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(_intents.LaneProbes.Select(p => p.Key), Is.All.EqualTo(LastArm().Key));
        Assert.That(_intents.LaneProbes.Select(p => p.Radius), Is.All.EqualTo(0.6f));
        Answer(1, ShardbackStopKind.Cover, 3f);
        Answer(2, ShardbackStopKind.Pillar, 7f, 1);
        Tick(1f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.Zero);
        Assert.That(_events.Count<ShardbackClipped>(), Is.Zero);
        Answer(3, ShardbackStopKind.Boundary, 5f);
        Assert.That(_events.Count<ShardbackClipped>(), Is.Zero);
        Place(new Vector3(x, 90f, z));
        _agent.Blackboard.QuarryIsADecoy = decoy;
        Tick(0f);
        ShardbackClipped clip = _events.Single<ShardbackClipped>();
        Assert.That((clip.BossId, clip.Key, clip.Centre, clip.Plus, clip.Minus),
            Is.EqualTo((_agent.Id, LastArm().Key, 3f, 7f, 5f)));
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 86f : 100f));
        Assert.That(_events.Count<ShardbackPillarBroken>(), Is.Zero);
    }

    [TestCase(0f, true)]
    [TestCase(5.5f, true)]
    [TestCase(5.501f, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void Burst_PhasesAndDisc_Edges(float distance, bool hit)
    {
        ArmBurst();
        Assert.That(LastArm().Reach, Is.EqualTo(5.5f));
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
        {
            Tick(0f);
            AnswerFan();
        }
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
        {
            Tick(0f);
            AnswerFan();
        }
        Tick(100f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
        float recovery = _events.Single<ShardbackImpact>().RecoverySeconds;
        Tick(recovery - 0.25f);
        AssertStanding();
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(1));
        Tick(100f);
        Assert.That(_events.Count<ShardbackArmed>(), Is.EqualTo(2));
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
        ShardbackArmed arm = LastArm();
        Assert.That(arm.Move, Is.EqualTo(burst ? ShardbackMove.Fan : ShardbackMove.Charge));
        if (burst)
            AnswerFan();
        else
            Answer(0, ShardbackStopKind.Cover, 0f);
        Tick(arm.Seconds - 0.25f);
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
        {
            AnswerFan();
            Tick(1f);
        }
        if (pending > 1)
        {
            Tick(0f);
            Answer(0, ShardbackStopKind.None, 12f);
            Tick(1.2f);
        }
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
        Assert.That(_events.Count<ShardbackFightStarted>(), Is.EqualTo(reset ? 1 : 0));
        AnswerFan();
        Tick(1f);
        Tick(1.25f);
        Assert.That(LastArm().Move, Is.EqualTo(ShardbackMove.Charge));
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
        AnswerFan();
        Tick(1f);
        Assert.That(_events.Count<ShardbackImpact>(), Is.EqualTo(1));
    }

    private void ArmBurst()
    {
        _boss.OnPhaseEntered(1);
        Tick(0f);
        AnswerFan();
        Place(Vector3.UnitX * 8f); // Miss the fan.
        Tick(1f);
        Place(Vector3.UnitZ * 2f);
        Tick(1.25f);
        Answer(0, ShardbackStopKind.Cover, 0f);
        Tick(1.2f);
        _events.Clear();
        Tick(1.5f);
    }

    private void AnswerFan()
    {
        for (int lane = 1; lane <= 3; lane++)
            Answer(lane, ShardbackStopKind.None, LastArm().Reach);
    }

    private void Answer(int lane, ShardbackStopKind kind, float distance, int slot = -1) =>
        _boss.OnShardbackStop(new ShardbackStop(_agent.Id, LastArm().Key, lane, kind, distance, slot));

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

    internal static EnemySpec Body() => new(new ContentId("enemy.shardback"), new LocKey("enemy.shardback.name"),
        500f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);

    internal static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
