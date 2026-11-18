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
public sealed class DuneclawBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private DuneclawStats _stats;
    private DuneclawBehaviour _boss;
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
        _stats = new DuneclawStats();
        _boss = new DuneclawBehaviour(_agent, _stats);
        _now = 0f;
        Place(Vector3.UnitZ * 2f);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Approach_AlternatesWithinReach(int phase)
    {
        _boss.OnPhaseEntered(phase);
        Place(Vector3.UnitZ * 3.21f);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitX;
        Tick(0.1f);
        Assert.That(_events.Count<DuneclawArmed>(), Is.Zero);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.UnitX * 1.8f));
        Place(Vector3.UnitZ * 3.2f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Sweep));
        Assert.That(LastArm().Seconds, Is.EqualTo(1f));
        Tick(0.5f);
        Assert.That(_events.Count<DuneclawImpact>(), Is.Zero);
        AssertStanding();
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(88f));
        Assert.That(_events.Single<DuneclawImpact>().RecoverySeconds, Is.EqualTo(0.8f));
        Place(Vector3.UnitZ * 4.51f);
        Tick(0.4f);
        AssertStanding();
        Tick(0.4f);
        Assert.That(_boss.IsArmed, Is.False);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(1.8f));
        Place(Vector3.UnitZ * 4.5f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Slam));
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(72f));
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(2f));
        Place(Vector3.UnitZ * 2f);
        Tick(1f);
        AssertStanding();
        Assert.That(_events.Count<DuneclawArmed>(), Is.EqualTo(2));
        Tick(1f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Sweep));
    }

    [TestCase(49f, 3.19f, true)]
    [TestCase(51f, 2f, false)]
    [TestCase(-49f, 2f, true)]
    [TestCase(-51f, 2f, false)]
    [TestCase(0f, 3.21f, false)]
    [TestCase(180f, 2f, false)]
    [TestCase(0f, 0f, true)]
    public void Sweep_UsesLockedArc_AtExpiryOnly(float degrees, float radius, bool hit)
    {
        Tick(0f);
        DuneclawArmed armed = LastArm();
        Assert.That(armed.BossId, Is.EqualTo(_agent.Id));
        Assert.That(armed.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(armed.Facing, Is.EqualTo(Vector2.UnitY));
        Assert.That(armed.Radius, Is.EqualTo(3.2f));
        Assert.That(armed.ArcDegrees, Is.EqualTo(100f));
        float radians = degrees * MathF.PI / 180f;
        Place(new Vector3(MathF.Sin(radians) * radius, 70f, MathF.Cos(radians) * radius));
        _agent.Blackboard.SelfPosition = Vector3.UnitX * 20f;
        _stats.SweepReach.Base = 100f; // The promised area is a value, not a live stat read.
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        AssertStanding();
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 88f : 100f));
        Tick(0.4f);
        Assert.That(_events.Count<DuneclawImpact>(), Is.EqualTo(1));
        AssertStanding();
    }

    [TestCase(0f, true)]
    [TestCase(1.5f, true)]
    [TestCase(1.51f, false)]
    public void Slam_UsesLockedCentre_AtExpiryOnly(float offset, bool hit)
    {
        ArmSlam();
        Vector3 target = LastArm().Target;
        Assert.That(LastArm().Radius, Is.EqualTo(1.5f));
        Assert.That(LastArm().ArcDegrees, Is.Zero);
        Place(target + new Vector3(offset, 40f, 0f));
        _agent.Blackboard.SelfPosition = Vector3.UnitX * 10f;
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.5f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 84f : 100f));
        DuneclawImpact impact = _events.Single<DuneclawImpact>();
        Assert.That(impact.Target, Is.EqualTo(target));
        Assert.That(impact.RecoverySeconds, Is.EqualTo(2f));
        Tick(1f);
        AssertStanding();
        Assert.That(_events.Count<DuneclawImpact>(), Is.EqualTo(1));
        Tick(0.9f);
        AssertStanding();
        Assert.That(_boss.IsRecovering, Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Attack_IFramesAndRollUseNormalDamagePath(bool slam, bool roll)
    {
        if (slam)
            ArmSlam();
        else
            Tick(0f);
        Tick(0.9f);
        if (roll)
        {
            _player.Charge.Request(_now);
            _player.Tick(0f, _now, new WorldSnapshot(8), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(_player.Charge.IsInvulnerable, Is.True);
        }
        else
        {
            _player.ApplyDamage(1f, _now);
        }
        float health = _player.Health.Current;
        Tick(0.11f);
        Assert.That(_player.Health.Current, Is.EqualTo(health));
        Assert.That(_events.Count<DuneclawImpact>(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PhaseTwo_SweepThenFreshSlam_AfterGapAndInRange(bool far)
    {
        _boss.OnPhaseEntered(2);
        Tick(0f);
        Tick(1f);
        Assert.That(_events.Single<DuneclawImpact>().RecoverySeconds, Is.EqualTo(0.35f));
        Place(new Vector3(3f, 0f, far ? 6f : 0f));
        Tick(0.25f);
        AssertStanding();
        Assert.That(_events.Count<DuneclawArmed>(), Is.EqualTo(1));
        Tick(0.11f);
        if (far)
        {
            Assert.That(_boss.IsArmed, Is.False);
            Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(1.8f).Within(1e-5f));
            Place(Vector3.UnitX * 4.5f);
            Tick(0f);
        }
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Slam));
        Assert.That(LastArm().Target, Is.EqualTo(_agent.Blackboard.PlayerPosition));
        Assert.That(LastArm().Seconds, Is.EqualTo(1f));
        Tick(0.5f);
        Assert.That(_events.Count<DuneclawImpact>(), Is.EqualTo(1));
        Tick(0.5f);
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(2f));
        Tick(1f);
        AssertStanding();
        Tick(1f);
        Place(Vector3.UnitZ * 2f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Sweep));
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void PhaseEntryOrReset_DropsPendingHitAndRestartsAtSweep(bool reset, int pending)
    {
        _boss.OnPhaseEntered(2);
        Tick(0f);
        if (pending > 0)
            Tick(1f); // Combo gap.
        if (pending > 1)
            Tick(0.35f); // Armed slam.
        _events.Clear();
        if (reset)
            _boss.Reset();
        else
            _boss.OnPhaseEntered(1);
        Assert.That(_boss.Phase, Is.EqualTo(reset ? 0 : 1));
        Assert.That(_boss.IsArmed, Is.False);
        Assert.That(_boss.IsRecovering, Is.False);
        Place(Vector3.UnitZ * 10f);
        float hp = _player.Health.Current;
        Tick(3f);
        Assert.That(_player.Health.Current, Is.EqualTo(hp));
        Assert.That(_events.Count<DuneclawImpact>(), Is.Zero);
        Place(Vector3.UnitZ * 2f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Sweep));
        Tick(0.5f);
        Assert.That(_events.Count<DuneclawImpact>(), Is.Zero);
    }

    [Test]
    public void DuneclawTick_NoAllocations()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            player.Health.Reset();
            _boss.Reset();
            Place(Vector3.UnitZ * 10f);
            _boss.Tick(new EnemyTickContext(1f, 0f, player, _intents, silent, _projectiles, _enemies));
            Place(Vector3.UnitZ * 2f);
            for (int phase = 0; phase < 3; phase++)
            {
                _boss.OnPhaseEntered(phase);
                for (int i = 0; i < 12; i++)
                {
                    _boss.Tick(new EnemyTickContext(1f, i, player, _intents, silent, _projectiles, _enemies));
                }
            }
        }, 100);
    }

    private void ArmSlam()
    {
        Tick(0f);
        Place(Vector3.UnitZ * 4f);
        Tick(1f); // Miss the sweep.
        _events.Clear();
        Tick(0.8f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Slam));
    }

    private DuneclawArmed LastArm() => _events.Of<DuneclawArmed>().Last();
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

    private static EnemySpec Body() => new(new ContentId("enemy.duneclaw"), new LocKey("enemy.duneclaw.name"),
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
