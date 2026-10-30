using System;
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

/// <summary>JG-14a: one test method per Behaviour rule; cases exercise each rule's boundaries.</summary>
[TestFixture]
public sealed class BulwarkBehaviourTests
{
    private const float Frame = 1f / 120f;
    private const float Tolerance = 1e-4f;
    private const float Windup = 0.7f;
    private const float Recover = 1.5f;
    private const float Damage = 16f;
    private const float Speed = 1.6f;
    private const float Reach = 1.6f;
    private const string BulwarkId = "enemy.bulwark";

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemyAgent _agent;
    private BulwarkBehaviour _bulwark;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        _player = new PlayerCombat(Character(), _events, _intents, 16);
        _projectiles = new ProjectileSystem(_events, 16);
        _agent = _system.Spawn(new ContentId(BulwarkId), Vector3.Zero);
        Assert.That(_agent.Behaviour, Is.InstanceOf<BulwarkBehaviour>());
        _bulwark = (BulwarkBehaviour)_agent.Behaviour;
        Bearing(0f, 4f);
        _now = 0f;
    }

    // Rule 1: the live player bearing, after the tick's steering, controls both flags.
    [TestCase(30f, 0f, true, TestName = "Bulwark_PlayerInFront_IsShielded")]
    [TestCase(90f, 0f, false, TestName = "Bulwark_PlayerBeside_IsOpen")]
    [TestCase(60f, 0f, true)]
    [TestCase(-60f, 0f, true)]
    [TestCase(60.1f, 0f, false)]
    [TestCase(180f, 0f, false)]
    [TestCase(90f, 0.5f, true)]
    public void Bulwark_ShieldFollowsTheBearing(float degrees, float dt, bool shielded)
    {
        Tick(0f);
        Bearing(degrees, 4f);
        Tick(dt);
        Assert.That(_agent.Health.IsInvulnerable, Is.EqualTo(shielded));
        Assert.That(_agent.IsVulnerable, Is.EqualTo(!shielded));
    }

    // Rule 2: through the public damage door, including the fact the views will consume.
    [TestCase(0f, true, TestName = "Bulwark_ShieldedHit_IsBlocked")]
    [TestCase(180f, false, TestName = "Bulwark_HitFromBehind_Lands")]
    public void Bulwark_HitRespectsTheShield(float degrees, bool blocked)
    {
        Tick(0f);
        Bearing(degrees, 4f);
        Tick(0f);
        _events.Clear();
        DamageResult result = _system.ApplyDamage(_agent.Id, 20f, _now, _player);
        EnemyDamaged hit = _events.Single<EnemyDamaged>();
        Assert.That(_agent.Health.Current, Is.EqualTo(blocked ? 90f : 70f));
        Assert.That(result.Blocked, Is.EqualTo(blocked));
        Assert.That(hit.Blocked, Is.EqualTo(blocked));
        Assert.That(hit.Id, Is.EqualTo(_agent.Id));
        Assert.That(hit.Amount, Is.EqualTo(blocked ? 0f : 20f));
        Assert.That(hit.HpFraction, Is.EqualTo(_agent.Health.Fraction));
        Assert.That(new EnemyDamaged(1, 1f, 1f, false).Blocked, Is.False, "Old callers default to an unblocked hit.");
    }

    // Rule 3: shortest steering, path priority, cone gate and stat-backed forward speed.
    [TestCase(180f, false, 0.5f, false, TestName = "Bulwark_TurnsAtItsRate")]
    [TestCase(90f, true, 0f, false, TestName = "Bulwark_PathOutsideCone_Stands")]
    [TestCase(90f, true, 0.5f, false)]
    [TestCase(-90f, true, 0.5f, false)]
    [TestCase(45f, true, 0f, true)]
    [TestCase(30f, false, 0.5f, true)]
    [TestCase(0f, false, 0.5f, true)]
    public void Bulwark_ApproachSteersAndWalks(float degrees, bool path, float dt, bool walks)
    {
        Tick(0f);
        Vector2 target = Direction(degrees);
        if (path)
        {
            _agent.Blackboard.PathDirectionToPlayer = target;
        }
        else
        {
            Bearing(degrees, 4f);
        }

        Tick(dt);
        EnemyMoveIntent move = _intents.LastEnemyMove;
        float turned = MathF.Acos(MathF.Min(1f, Vector2.Dot(Vector2.UnitY, move.FacingXZ))) * 180f / MathF.PI;
        Assert.That(turned, Is.EqualTo(MathF.Min(MathF.Abs(degrees), 75f * dt)).Within(Tolerance));
        Assert.That(Vector2.Dot(move.FacingXZ, target), Is.GreaterThanOrEqualTo(Vector2.Dot(Vector2.UnitY, target)));
        Assert.That(move.Velocity.Length(), Is.EqualTo(walks ? Speed : 0f).Within(Tolerance));
        if (walks)
        {
            Assert.That(move.Velocity.X, Is.EqualTo(move.FacingXZ.X * Speed).Within(Tolerance));
            Assert.That(move.Velocity.Z, Is.EqualTo(move.FacingXZ.Y * Speed).Within(Tolerance));
        }
    }

    // Rule 4: recovery, the shield and the cooldown cannot leak into a pooled life.
    [Test]
    public void Bulwark_Reset_FacesThePlayer()
    {
        Bash();
        Tick(Frame);
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Recover));
        _bulwark.Reset();
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Approach));
        Assert.That(_agent.IsVulnerable, Is.True);
        Assert.That(_agent.Health.IsInvulnerable, Is.False);
        Assert.That(_agent.Blackboard.NextAttackAt, Is.Zero);
        Assert.That(_agent.Blackboard.StateTimer, Is.Zero);
        Bearing(-90f, 4f);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitY;
        Tick(0f);
        Assert.That(Vector2.Distance(_intents.LastEnemyMove.FacingXZ, Direction(-90f)), Is.LessThan(Tolerance));
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Approach));
        Assert.That(_agent.Health.IsInvulnerable, Is.True);
        Assert.That(_agent.IsVulnerable, Is.False);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.Zero;
        Bearing(-90f, 1.2f);
        Tick(0f);
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Windup), "No inherited cooldown.");
    }

    // Rule 5: all three gates; the warning's frozen front is the front of the bash.
    [TestCase(1.2f, 0f, 0f, true, TestName = "Bulwark_InReach_Telegraphs")]
    [TestCase(1.7f, 0f, 0f, false)]
    [TestCase(1.2f, 90f, 0f, false)]
    [TestCase(1.2f, 0f, 10f, false)]
    public void Bulwark_WindupRequiresReachArcAndCooldown(float distance, float degrees, float cooldown, bool warns)
    {
        Tick(0f);
        Bearing(degrees, distance);
        _agent.Blackboard.NextAttackAt = cooldown;
        _events.Clear();
        Tick(0f);
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(warns ? 1 : 0));
        Assert.That(_bulwark.State, Is.EqualTo(warns ? BulwarkState.Windup : BulwarkState.Approach));
        if (warns)
        {
            EnemyTelegraph tell = _events.Single<EnemyTelegraph>();
            Assert.That(tell.Id, Is.EqualTo(_agent.Id));
            Assert.That(tell.Duration, Is.EqualTo(Windup));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Vector2 front = _intents.LastEnemyMove.FacingXZ;
            Bearing(90f, distance);
            Tick(Windup / 2f);
            Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Windup));
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(front));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Assert.That(_agent.IsVulnerable, Is.True, "A frozen front still opens when the player circles.");
        }
    }

    // Rule 6: resolve on the last wind-up tick, rechecking range and bearing, once only.
    [TestCase(0f, 1.2f, false, true)]
    [TestCase(90f, 1.2f, false, false)]
    [TestCase(0f, 2f, false, false)]
    [TestCase(0f, 1.2f, true, false)]
    public void Bulwark_Bash_HitsOnlyInFront(float degrees, float distance, bool decoy, bool hits)
    {
        Bearing(0f, 1.2f);
        Tick(0f);
        Bearing(degrees, distance);
        _agent.Blackboard.QuarryIsADecoy = decoy;
        Tick(Windup - 0.01f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Tick(0.02f);
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Bash));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hits ? 1 : 0));
        Assert.That(_player.Health.Current, Is.EqualTo(hits ? 140f - Damage : 140f));
        Tick(Frame);
        Tick(Frame);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hits ? 1 : 0));
    }

    // Rule 7: recover from the bash instant, holding its old front for the whole interval.
    [Test]
    public void Bulwark_Recover_DoesNotTurn()
    {
        Bash();
        float bashAt = _now;
        Vector2 front = _intents.LastEnemyMove.FacingXZ;
        Assert.That(_agent.Blackboard.NextAttackAt, Is.EqualTo(bashAt + Recover).Within(Tolerance));
        Bearing(90f, 1.2f);
        Tick(Recover - 0.01f);
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Recover));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(front));
        Assert.That(_agent.IsVulnerable, Is.True);
        Tick(0.02f);
        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Approach));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(front));
        Tick(Frame);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.Not.EqualTo(front));
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
    }

    // Rule 8: includes each state and every transition, through EnemySystem's dispatch.
    [Test]
    public void Bulwark_OneIntentATick()
    {
        var seen = new bool[4];
        Tick(Frame);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(Speed).Within(Tolerance));
        Bearing(0f, 1.2f);
        for (int i = 0; i < 600; i++)
        {
            BulwarkState before = _bulwark.State;
            seen[(int)before] = true;
            Tick(Frame);
            Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1));
            Assert.That(_intents.LastEnemyMove.Id, Is.EqualTo(_agent.Id));
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero),
                $"In reach: {before} -> {_bulwark.State}.");
        }

        Assert.That(seen, Is.All.True, "All four states were observed.");
    }

    // Rule 9: refuse a missing block or an unreadably short tell at the data boundary.
    [Test]
    public void Spec_Bulwark_Refuses()
    {
        Assert.Throws<ArgumentException>(() => Spec(bulwark: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(windup: 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(windup: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(windup: float.PositiveInfinity));
        Assert.DoesNotThrow(() => Spec(windup: WardenBehaviour.MinTelegraphSeconds));
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BulwarkSpec(invalid, 75f, 45f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BulwarkSpec(60f, invalid, 45f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BulwarkSpec(60f, 75f, invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new BulwarkSpec(180f, 75f, 45f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulwarkSpec(60f, 75f, 90.1f));
        var valid = new BulwarkSpec(60f, 75f, 90f);
        Assert.That(valid.ShieldHalfArcDegrees, Is.EqualTo(60f));
        Assert.That(valid.TurnRateDegrees, Is.EqualTo(75f));
        Assert.That(valid.WalkConeDegrees, Is.EqualTo(90f));
    }

    // Rule 10: simultaneous walkers and repeated whole attack cycles, without boxing events.
    [Test]
    public void Bulwark_Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        var player = new PlayerCombat(Character(), silent, intents, 16);
        var projectiles = new ProjectileSystem(silent, 16);
        EnemyAgent near = system.Spawn(new ContentId(BulwarkId), Vector3.Zero);
        EnemyAgent far = system.Spawn(new ContentId(BulwarkId), Vector3.Zero);
        near.Blackboard.DistanceToPlayer = 1.2f;
        near.Blackboard.DirectionToPlayer = Vector2.UnitY;
        far.Blackboard.DistanceToPlayer = 4f;
        far.Blackboard.DirectionToPlayer = Vector2.UnitX;
        float clock = 0f;
        Action tick = () =>
        {
            intents.Clear();
            clock += Frame;
            system.Tick(new EnemyTickContext(Frame, clock, player, intents, silent, projectiles, system));
        };
        for (int i = 0; i < 1200; i++)
        {
            tick();
        }

        AllocationAssert.None(tick);
    }

    private void Bearing(float degrees, float distance)
    {
        _agent.Blackboard.DirectionToPlayer = Direction(degrees);
        _agent.Blackboard.DistanceToPlayer = distance;
    }

    private void Tick(float dt)
    {
        _now += dt;
        _intents.Clear();
        _system.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _system));
    }

    private void Bash()
    {
        Bearing(0f, 1.2f);
        Tick(0f);
        for (int i = 0; i < 200 && _bulwark.State == BulwarkState.Windup; i++)
        {
            Tick(Frame);
        }

        Assert.That(_bulwark.State, Is.EqualTo(BulwarkState.Bash));
    }

    private static Vector2 Direction(float degrees) =>
        new Vector2(MathF.Sin(degrees * MathF.PI / 180f), MathF.Cos(degrees * MathF.PI / 180f));

    private static ContentCatalog Catalog() => new ContentCatalog(new[] { Character() }, new[] { Spec() });

    private static EnemySpec Spec(float windup = Windup, bool bulwark = true) => new EnemySpec(
        new ContentId(BulwarkId), new LocKey("enemy.bulwark.name"),
        maxHp: 90f, moveSpeed: Speed, targetPriority: 3, threatCost: 14, xpValue: 42f,
        isElite: false, contactDamage: Damage, reach: Reach, windupTime: windup,
        recoverTime: Recover, aggroRange: 30f, behaviour: EnemyBehaviourKind.Bulwark,
        bulwark: bulwark ? new BulwarkSpec(60f, 75f, 45f) : null);

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.oathbound"), new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"), 140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
