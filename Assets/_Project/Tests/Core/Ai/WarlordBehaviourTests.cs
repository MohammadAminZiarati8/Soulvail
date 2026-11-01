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

[TestFixture]
public sealed class WarlordBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WarlordBehaviour _warlord;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        var body = new EnemySpec(new ContentId("enemy.warlord"), new LocKey("enemy.warlord.name"),
            3200f, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { body }, Array.Empty<ModeSpec>());
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(body, Vector3.Zero);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        _warlord = new WarlordBehaviour(_agent, _projectiles, new FixedRandom(0.5f).Misc,
            new ContentId("enemy.mossguard"));
        _now = 0f;
        Place(Vector2.UnitY, 10f);
    }

    // Rule 1: perception and damage agree on the frontal guard.
    [Test]
    public void Guard_PlayerInFront_Invulnerable()
    {
        Tick(0f);
        AssertGuard(true);
    }

    [Test]
    public void Guard_PlayerBehind_Hurtable()
    {
        Tick(0f);
        Place(-Vector2.UnitY, 10f);
        Tick(0f);
        AssertGuard(false);
    }

    [Test]
    public void Guard_Edge61_Hurtable()
    {
        Tick(0f);
        Place(Direction(61f), 10f);
        Tick(0f);
        AssertGuard(false);
    }

    // Rule 2: path turning happens only while approaching.
    [Test]
    public void Turn_IsAtMostFortyDegreesPerSecond()
    {
        Tick(0f);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitX;
        Tick(0.5f);
        Assert.That(Angle(_warlord.Facing), Is.EqualTo(20f).Within(0.001f));
    }

    [Test]
    public void Turn_FrozenDuringWindup()
    {
        Arm();
        Vector2 aim = _warlord.Facing;
        Place(Vector2.UnitX, 3f);
        Tick(0.4f);
        Assert.That(_warlord.Facing, Is.EqualTo(aim));
        Tick(0.4f);
        Tick(0.4f);
        Assert.That(_warlord.Facing, Is.EqualTo(aim), "Recovery also holds the caught aim.");
    }

    // Rule 3: speed follows facing, gated by the path cone.
    [Test]
    public void Approach_PathOutsideCone_Stands()
    {
        Tick(0f);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitX;
        Tick(0f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Approach_PathInCone_Walks()
    {
        Tick(0f);
        _agent.Blackboard.PathDirectionToPlayer = Direction(40f);
        Tick(0f);
        Assert.That(_intents.LastEnemyMove.Velocity,
            Is.EqualTo(new Vector3(0f, 0f, _agent.MoveSpeed.Value)));
    }

    // Rule 4: a newly armed warning always gets its full windup.
    [Test]
    public void Sweep_InReachAfterGap_ArmsAndTelegraphsPointEight()
    {
        Arm();
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Move, Is.EqualTo(1));
        Assert.That(telegraph.Duration, Is.EqualTo(0.8f));
        SprayArmed cone = _events.Single<SprayArmed>();
        Assert.That(cone.Id, Is.EqualTo(_agent.Id));
        Assert.That(cone.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(cone.Direction, Is.EqualTo(Vector2.UnitY));
        Assert.That(cone.HalfAngleDeg, Is.EqualTo(60f));
        Assert.That(cone.Range, Is.EqualTo(3.5f));
        Assert.That(cone.Seconds, Is.EqualTo(0.8f));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
    }

    [Test]
    public void Sweep_BeforeGap_DoesNotArm()
    {
        Place(Vector2.UnitY, 3f);
        Tick(1.39f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero);
    }

    // Rule 5: the caught XZ cone resolves once, even without sight.
    [Test]
    public void Sweep_InArc_HitsOnce()
    {
        Arm();
        Place(Direction(59f), 3.5f);
        _agent.Blackboard.PlayerPosition += new Vector3(0f, 100f, 0f);
        _agent.Blackboard.HasLineOfSight = false;
        Tick(0.79f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.02f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.True);
        Assert.That(_player.Health.Current, Is.EqualTo(70f));
        Tick(0.79f);
        Tick(0.02f);
        Tick(1.39f);
        Assert.That(_events.Count<SprayFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<SprayArmed>(), Is.EqualTo(1), "Recovery is followed by a full gap.");
        Tick(0.02f);
        Assert.That(_events.Count<SprayArmed>(), Is.EqualTo(2));
    }

    [Test]
    public void Sweep_Beside61_Misses() => AssertMiss(Direction(61f), 3f);

    [Test]
    public void Sweep_Behind_Misses() => AssertMiss(-Vector2.UnitY, 3f);

    [Test]
    public void Sweep_PlayerLeftReach_Misses() => AssertMiss(Vector2.UnitY, 3.51f);

    // Rule 6: the view learns the arc on spawn and on changes, never every frame.
    [Test]
    public void GuardArc_PublishedFirstTickAndOnChange()
    {
        Tick(0f);
        WarlordGuardArc first = _events.Single<WarlordGuardArc>();
        Assert.That(first.BossId, Is.EqualTo(_agent.Id));
        Assert.That(first.HalfArcDeg, Is.EqualTo(60f));
        Tick(0f);
        Assert.That(_events.Count<WarlordGuardArc>(), Is.EqualTo(1));
        // The arc is derived each tick (JG-15c); a daze drops it to zero without a public port.
        typeof(WarlordBehaviour).GetProperty(nameof(WarlordBehaviour.IsDazed))
            .SetValue(_warlord, true);
        Tick(0f);
        Assert.That(_events.Of<WarlordGuardArc>()[1].HalfArcDeg, Is.Zero);
        AssertGuard(false);
        Tick(0f);
        Assert.That(_events.Count<WarlordGuardArc>(), Is.EqualTo(2));
    }

    // Rule 7: phase cancellation and pooling cannot leave a warning or shield behind.
    [Test]
    public void Phase_DropsArmedSweep_ClearsCone()
    {
        Arm();
        _warlord.OnPhaseEntered(1);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Single<SprayCleared>().Id, Is.EqualTo(_agent.Id));
        Tick(1.39f);
        Assert.That(_events.Count<SprayFired>(), Is.Zero);
        Assert.That(_events.Count<SprayArmed>(), Is.EqualTo(1));
        Tick(0.02f);
        Assert.That(_events.Count<SprayArmed>(), Is.EqualTo(2));
        _warlord.OnPhaseEntered(2);
        _warlord.OnPhaseEntered(3);
        Assert.That(_events.Count<SprayCleared>(), Is.EqualTo(2));
    }

    [Test]
    public void Reset_LowersGuard()
    {
        Arm();
        _warlord.Reset();
        AssertGuard(false);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_events.Count<SprayCleared>(), Is.EqualTo(1));
        Place(Vector2.UnitX, 10f);
        Tick(0f);
        Assert.That(_warlord.Facing, Is.EqualTo(Vector2.UnitX));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(_agent.MoveSpeed.Value));
        Assert.That(_events.Count<WarlordGuardArc>(), Is.EqualTo(2));
    }

    // Rule 8: exercise approach, turn, arm, release, recovery and phase cancellation.
    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        var ctx = new EnemyTickContext(0.2f, 1f, player, _intents, silent, projectiles, _enemies);
        AllocationAssert.None(() =>
        {
            _warlord.Reset();
            for (int i = 0; i < 40; i++)
            {
                Place(Direction(i * 10f), i < 2 ? 10f : 3f);
                _intents.Clear();
                _warlord.Tick(ctx);
            }
            _warlord.OnPhaseEntered(1);
        }, 100);
    }

    private void AssertGuard(bool guarded)
    {
        Assert.That(_agent.IsVulnerable, Is.EqualTo(!guarded));
        Assert.That(_agent.Health.IsInvulnerable, Is.EqualTo(guarded));
        float before = _agent.Health.Current;
        _agent.Health.ApplyDamage(1f, _now);
        Assert.That(_agent.Health.Current, Is.EqualTo(before - (guarded ? 0f : 1f)));
    }

    private void AssertMiss(Vector2 direction, float distance)
    {
        Arm();
        Place(direction, distance);
        Tick(0.8f);
        Assert.That(_events.Single<SprayFired>().Hit, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    private void Arm()
    {
        Place(Vector2.UnitY, 3f);
        Tick(1.4f);
    }

    private void Tick(float dt)
    {
        _now += dt;
        var ctx = new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies);
        _warlord.Tick(ctx);
    }

    private void Place(Vector2 direction, float distance)
    {
        EnemyBlackboard board = _agent.Blackboard;
        board.DirectionToPlayer = direction;
        board.PathDirectionToPlayer = direction;
        board.DistanceToPlayer = distance;
        board.SelfPosition = Vector3.Zero;
        board.PlayerPosition = new Vector3(direction.X * distance, 0f, direction.Y * distance);
    }

    private Vector2 Direction(float degrees) =>
        new Vector2(MathF.Sin(degrees * MathF.PI / 180f), MathF.Cos(degrees * MathF.PI / 180f));

    private float Angle(Vector2 direction) => MathF.Atan2(direction.X, direction.Y) * 180f / MathF.PI;

    private CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
