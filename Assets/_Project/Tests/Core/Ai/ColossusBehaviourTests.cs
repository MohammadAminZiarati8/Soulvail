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
public sealed class ColossusBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private ColossusStats _stats;
    private ColossusBehaviour _boss;
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
        _stats = new ColossusStats();
        _boss = new ColossusBehaviour(_agent, _stats);
        _now = 0f;
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 8f;
    }

    [Test]
    public void Plates_UseVisibleAxisAndRepairAtTheEndOfBothInvulnerableBeats()
    {
        EnemySpec body = Body(EnemyBehaviourKind.Boss);
        var id = new ContentId("boss.colossus");
        var spec = new BossSpec(id, body.Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) },
            3f, usesAuthoredMaxHp: true);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { body }, bosses: new[] { spec });
        var enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8, (agent, _) => new ColossusBehaviour(agent, _stats));
        EnemyAgent boss = enemies.SpawnBoss(id, Vector3.Zero);
        var world = new WorldSnapshot(8);
        world.AddEnemy() = new EnemySense { Id = boss.Id, ShoulderAxis = -Vector2.UnitY };
        enemies.Ingest(world);

        DamageResult Hit(Vector3 source, float amount, float now = 0f) =>
            enemies.ApplyDamageFrom(boss.Id, amount, now, _player, source);
        void BeatTick(float dt, float now) =>
            enemies.Tick(new EnemyTickContext(dt, now, _player, _intents, _events, _projectiles, enemies));

        float scale = 1f - _stats.PlateDamageReduction.Value;
        float worn = 1f - 20f / _stats.PlateDurability.Value;
        Assert.That(Hit(new Vector3(4f, 0f, 0.2f), 20f).ToHp, Is.EqualTo(20f * scale));
        Assert.That(_events.Single<ColossusPlateHit>().RemainingFraction, Is.EqualTo(1f));
        float halfPlate = _stats.PlateDurability.Value * 0.5f;
        Assert.That(Hit(Vector3.UnitZ, halfPlate).ToHp, Is.EqualTo(halfPlate * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().Shoulder, Is.EqualTo(ColossusShoulder.Left));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.EqualTo(0.5f));
        Assert.That(Hit(Vector3.UnitZ, halfPlate).ToHp, Is.EqualTo(halfPlate * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.Zero);
        Assert.That(Hit(Vector3.UnitZ, 115f).ToHp, Is.EqualTo(115f));

        Assert.That(Hit(-Vector3.UnitZ, 20f).ToHp, Is.EqualTo(20f * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.EqualTo(worn));
        BeatTick(0f, 0f);
        Assert.That(_events.Count<BossBeatStarted>(), Is.EqualTo(1));
        int plateHits = _events.Count<ColossusPlateHit>();
        float beatHp = boss.Health.Current;
        BeatTick(2.99f, 2.99f);
        Assert.That(Hit(Vector3.UnitZ, 20f, 2.99f).Blocked, Is.True);
        Assert.That(Hit(-Vector3.UnitZ, 20f, 2.99f).Blocked, Is.True);
        Assert.That(boss.Health.Current, Is.EqualTo(beatHp));
        Assert.That(_events.Count<ColossusPlateHit>(), Is.EqualTo(plateHits));
        Assert.That(_events.Count<BossBeatEnded>(), Is.Zero);
        BeatTick(0.02f, 3.01f);
        Assert.That(Hit(Vector3.UnitZ, 20f, 3.01f).ToHp, Is.EqualTo(20f * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.EqualTo(worn));

        // The archer has crossed to the left before this arrow arrives from the right.
        _projectiles.Fire(new Projectile(new ContentId("shot.test"), 0, -3f * Vector3.UnitZ,
            Vector3.Zero, 20f, 0.2f, 20f, ShotSide.AtEnemies), 3.01f);
        _projectiles.Tick(4f, Vector3.UnitZ, _player, enemies);
        ColossusPlateHit arrow = _events.Of<ColossusPlateHit>().Last();
        Assert.That(arrow.Shoulder, Is.EqualTo(ColossusShoulder.Right));
        Assert.That(arrow.RemainingFraction, Is.EqualTo(worn));

        Assert.That(Hit(Vector3.UnitZ, _stats.PlateDurability.Value, 4f).ToHp,
            Is.EqualTo(_stats.PlateDurability.Value * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.Zero);
        BeatTick(0f, 4f);
        Assert.That(_events.Count<BossBeatStarted>(), Is.EqualTo(2));
        Assert.That(Hit(Vector3.UnitZ, 20f, 4f).Blocked, Is.True);
        BeatTick(3f, 7f);
        Assert.That(_events.Count<BossBeatEnded>(), Is.EqualTo(2));
        Assert.That(Hit(Vector3.UnitZ, 20f, 7f).ToHp, Is.EqualTo(20f * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.EqualTo(worn));
        Assert.That(Hit(-Vector3.UnitZ, 20f, 7f).ToHp, Is.EqualTo(20f * scale));
        Assert.That(_events.Of<ColossusPlateHit>().Last().RemainingFraction, Is.EqualTo(worn));
    }

    [Test]
    public void Ray_OpensAtRangeAndRecursAfterCooldown()
    {
        _agent.Blackboard.HasLineOfSight = true;
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitY;
        Place(0f, 11f);
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.SundialRay));
        Tick(1.2f);
        Assert.That(LastImpact().RecoverySeconds, Is.EqualTo(0.8f));
        Tick(0.79f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.Not.EqualTo(Vector3.Zero));
        _boss.OnPhaseEntered(1);
        Tick(7.98f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(1));
        Tick(0.02f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(2));
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.SundialRay));
    }

    [TestCase(4f, true, false, false, ColossusMove.Sweep)]
    [TestCase(11f, false, false, false, null)]
    [TestCase(11f, true, true, false, null)]
    [TestCase(11f, true, false, true, null)]
    [TestCase(14.01f, true, false, false, null)]
    public void Ray_RequiresAnEligibleRangedPlayer(float distance, bool sight, bool decoy, bool pet,
        ColossusMove? expected)
    {
        _agent.Blackboard.HasLineOfSight = sight;
        _agent.Blackboard.QuarryIsADecoy = decoy;
        _agent.Blackboard.QuarryIsAPet = pet;
        Place(0f, distance);
        Tick(0f);
        Assert.That(_events.Of<ColossusArmed>().Select(e => (ColossusMove?)e.Move),
            Is.EqualTo(expected.HasValue ? new[] { expected } : Array.Empty<ColossusMove?>()));
    }

    [TestCase(0f, 11f, true)]
    [TestCase(0.65f, 14f, true)]
    [TestCase(-0.66f, 11f, false)]
    [TestCase(0.66f, 11f, false)]
    [TestCase(0f, 14.01f, false)]
    [TestCase(0f, 0.39f, false)]
    public void Ray_UsesTheLockedLaneAndHitsOnlyAtImpact(float across, float along, bool hit)
    {
        EnemyBlackboard board = _agent.Blackboard;
        board.HasLineOfSight = true;
        board.PlayerPosition = Vector3.UnitX * 11f;
        Tick(0f);
        ColossusArmed ray = LastArm();
        Assert.That((ray.Move, ray.Origin, ray.Facing, ray.Seconds, ray.Radius, ray.LaneStart, ray.LaneHalfWidth),
            Is.EqualTo((ColossusMove.SundialRay, Vector3.Zero, Vector2.UnitX, 1.2f, 14f, 0.4f, 0.65f)));
        board.SelfPosition = new Vector3(-7f, 0f, 12f);
        board.PlayerPosition = new Vector3(along, 0f, across);
        _stats.RayLength.Base = 1f;
        _stats.RayHalfWidth.Base = 0.1f;
        Tick(1.19f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<ColossusImpact>(), Is.Zero);
        Tick(0.01f + 0.0001f);
        Assert.That(LastProgress(), Is.EqualTo(1f));
        Assert.That(LastImpact().Move, Is.EqualTo(ColossusMove.SundialRay));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX));
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 76f : 100f));
        board.PlayerPosition = Vector3.UnitX * 11f;
        Tick(0.4f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(1));
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 76f : 100f));
    }

    [Test]
    public void Sweep_PathsAndLocksHeading()
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 origin = new(4f, 2f, -3f);
        board.SelfPosition = origin;
        board.PlayerPosition = origin + 10f * Vector3.UnitX;
        board.DirectionToPlayer = Vector2.UnitX;
        board.PathDirectionToPlayer = -Vector2.UnitY;
        board.HasLineOfSight = false;
        Tick(1f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(-2.2f * Vector3.UnitZ));
        Assert.That(_events.Count<ColossusArmed>(), Is.Zero);

        board.PlayerPosition = origin + 9.5f * Vector3.UnitX;
        Tick(0f);
        ColossusArmed sweep = LastArm();
        Assert.That(sweep.BossId, Is.EqualTo(_agent.Id));
        Assert.That(sweep.Move, Is.EqualTo(ColossusMove.Sweep));
        Assert.That(sweep.Origin, Is.EqualTo(origin));
        Assert.That(sweep.Facing, Is.EqualTo(Vector2.UnitX));
        Assert.That(sweep.Radius, Is.EqualTo(9.5f));
        Assert.That(sweep.SectorDegrees, Is.EqualTo(20f));

        board.SelfPosition = origin + 30f * Vector3.UnitX;
        board.PlayerPosition = origin + 9.5f * Vector3.UnitX + 40f * Vector3.UnitY;
        _stats.SweepRadius.Base = 1f;
        _stats.SectorDegrees.Base = 1f;
        Tick(1.35f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(0.35f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(sweep.Facing));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [TestCase(-30f, 9.5f, 0)]
    [TestCase(-20f, 8f, 0)]
    [TestCase(-10f, 8f, 1)]
    [TestCase(0f, 9.5f, 1)]
    [TestCase(0f, 0f, 1)]
    [TestCase(10f, 8f, 2)]
    [TestCase(20f, 8f, 2)]
    [TestCase(30f, 9.5f, 2)]
    [TestCase(-30.01f, 8f, -1)]
    [TestCase(30.01f, 8f, -1)]
    [TestCase(0f, 9.51f, -1)]
    [TestCase(180f, 8f, -1)]
    public void Sweep_ResolvesSlotsAndDefence(float degrees, float radius, int hitSector)
    {
        Tick(0f);
        _agent.Blackboard.HasLineOfSight = false;
        Place(degrees, radius);
        for (int sector = 0; sector < 3; sector++)
        {
            Tick(sector == 0 ? 1.35f : 0.35f);
            Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(sector + 1));
            ColossusImpact impact = LastImpact();
            Assert.That(impact.BossId, Is.EqualTo(_agent.Id));
            Assert.That(impact.Move, Is.EqualTo(ColossusMove.Sweep));
            Assert.That(impact.Sector, Is.EqualTo(sector));
            Assert.That(impact.RecoverySeconds, Is.EqualTo(sector == 2 ? 1.8f : 0f));
            Assert.That(_player.Health.Current, Is.EqualTo(hitSector >= 0 && sector >= hitSector ? 80f : 100f));
        }
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Sweep_ResolvesSlotsAndDefence_WhenQuarryChanges(bool decoy, bool pet)
    {
        Tick(0f);
        Place(-20f, 8f);
        _agent.Blackboard.QuarryIsADecoy = decoy;
        _agent.Blackboard.QuarryIsAPet = pet;
        Tick(1.35f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));

        _agent.Blackboard.QuarryIsADecoy = false;
        _agent.Blackboard.QuarryIsAPet = false;
        Place(0f, 8f);
        Tick(0.35f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Place(20f, 8f);
        Tick(0.8f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Sweep_ResolvesSlotsAndDefence_SpendsDefendedAttempt(bool roll)
    {
        Tick(0f);
        Tick(1.34f);
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
        Place(-20f, 8f);
        Tick(0.02f);
        Assert.That(_player.Health.Current, Is.EqualTo(health));
        if (roll)
        {
            _player.Tick(1f, _now + 1f, new WorldSnapshot(8), ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);
            Assert.That(_player.Charge.IsInvulnerable, Is.False);
        }

        Place(0f, 8f);
        Tick(1f);
        Assert.That(_player.Health.Current, Is.EqualTo(health));
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(2));
        _player.ApplyDamage(1f, _now);
        Assert.That(_player.Health.Current, Is.EqualTo(health - 1f));
    }

    [TestCase(2.8f, 3.3f, false, false, true)]
    [TestCase(2.8f, 3.31f, false, false, false)]
    [TestCase(2.8f, 3.3f, true, false, false)]
    [TestCase(2.8f, 3.3f, false, true, false)]
    [TestCase(2.81f, 0f, false, false, false)]
    public void Stomp_TriggersOnlyAfterSweep(float triggerDistance, float impactDistance, bool decoy, bool pet,
        bool hit)
    {
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 2f;
        Tick(0f);
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.Sweep));
        Place(180f, 8f);
        FinishPass(0.35f);
        _agent.Blackboard.PlayerPosition = Vector3.UnitX * triggerDistance;
        Tick(1.79f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);

        ColossusArmed armed = LastArm();
        Assert.That(armed.Facing, Is.EqualTo(Vector2.UnitX));
        if (triggerDistance > 2.8f)
        {
            Assert.That(armed.Move, Is.EqualTo(ColossusMove.Sweep));
            return;
        }

        Assert.That(armed.Move, Is.EqualTo(ColossusMove.Stomp));
        Assert.That(armed.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(armed.Radius, Is.EqualTo(3.3f));
        Assert.That(armed.SectorDegrees, Is.Zero);
        Assert.That(armed.Seconds, Is.EqualTo(1f));
        _agent.Blackboard.SelfPosition = 20f * Vector3.UnitZ;
        _agent.Blackboard.PlayerPosition = new Vector3(-impactDistance, 40f, 0f);
        _agent.Blackboard.QuarryIsADecoy = decoy;
        _agent.Blackboard.QuarryIsAPet = pet;
        Tick(0.99f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(3));
        Tick(0.02f);
        Assert.That(_player.Health.Current, Is.EqualTo(hit ? 84f : 100f));
        Assert.That(LastImpact().Move, Is.EqualTo(ColossusMove.Stomp));
        Assert.That(LastImpact().Sector, Is.EqualTo(-1));
        Assert.That(LastImpact().RecoverySeconds, Is.EqualTo(1.25f));

        _agent.Blackboard.SelfPosition = Vector3.Zero;
        Tick(1.24f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(2));
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ;
        Tick(0.02f);
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.Sweep));
    }

    [TestCase(0, 0.35f)]
    [TestCase(1, 0.22f)]
    [TestCase(2, 0.22f)]
    public void FinalSweep_ReversesWithFullTiming(int phase, float interval)
    {
        _boss.OnPhaseEntered(phase);
        Tick(100f);
        ColossusArmed forward = LastArm();
        Assert.That(forward.Seconds, Is.EqualTo(1.35f));
        Assert.That(LastProgress(), Is.Zero);
        Place(20f, 8f);
        AssertTellAndFirstImpact(0);
        AssertNextSector(interval, 1);
        AssertNextSector(interval, 2);
        if (phase < 2)
        {
            Assert.That(LastImpact().RecoverySeconds, Is.EqualTo(1.8f));
            Tick(1.79f);
            Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(1));
            Tick(0.02f);
            Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.Sweep));
            return;
        }

        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(LastImpact().RecoverySeconds, Is.Zero);
        _agent.Blackboard.SelfPosition = 6f * Vector3.UnitX;
        _stats.SweepRadius.Base = 1f;
        _stats.SectorDegrees.Base = 1f;
        Tick(0.29f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(1));
        Tick(100f);
        ColossusArmed reverse = LastArm();
        Assert.That(reverse.Move, Is.EqualTo(ColossusMove.ReverseSweep));
        Assert.That(reverse.Seconds, Is.EqualTo(1.35f));
        Assert.That(reverse.Origin, Is.EqualTo(forward.Origin));
        Assert.That(reverse.Facing, Is.EqualTo(forward.Facing));
        Assert.That(reverse.Radius, Is.EqualTo(forward.Radius));
        Assert.That(reverse.SectorDegrees, Is.EqualTo(forward.SectorDegrees));
        Assert.That(LastProgress(), Is.Zero);
        AssertTellAndFirstImpact(2);
        Assert.That(_player.Health.Current, Is.EqualTo(60f));
        AssertNextSector(interval, 1);
        AssertNextSector(interval, 0);
        Assert.That(LastImpact().Move, Is.EqualTo(ColossusMove.ReverseSweep));
        Assert.That(LastImpact().RecoverySeconds, Is.EqualTo(2.4f));
        _agent.Blackboard.PlayerPosition = _agent.Blackboard.SelfPosition;
        Tick(2.39f);
        Assert.That(_events.Count<ColossusArmed>(), Is.EqualTo(2));
        Tick(100f);
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.Stomp));
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(6));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(3, false)]
    [TestCase(4, false)]
    [TestCase(7, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(7, true)]
    public void PhaseAndReset_ClearPendingWork(int steps, bool reset)
    {
        _boss.OnPhaseEntered(2);
        _agent.Blackboard.PlayerPosition = Vector3.UnitX * 8f;
        Tick(0f);
        for (int i = 0; i < steps; i++)
        {
            Tick(2f);
        }

        if (reset)
            _boss.Reset();
        else
            _boss.OnPhaseEntered(1);

        _events.Clear();
        float health = _player.Health.Current;
        _agent.Blackboard.PlayerPosition = _agent.Blackboard.SelfPosition;
        Tick(100f);
        Assert.That(_events.Count<ColossusImpact>(), Is.Zero);
        Assert.That(LastArm().Move, Is.EqualTo(ColossusMove.Sweep));
        Assert.That(LastArm().Facing, Is.EqualTo(reset ? Vector2.UnitY : Vector2.UnitX));
        Assert.That(_player.Health.Current, Is.EqualTo(health));
        Tick(1.35f);
        Tick(0.22f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(reset ? 1 : 2));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        _agent.Blackboard.PathDirectionToPlayer = Vector2.UnitY;
        AllocationAssert.None(() =>
        {
            player.Health.Reset();
            _boss.Reset();
            _boss.OnPhaseEntered(2);
            _intents.Clear();
            _agent.Blackboard.PlayerPosition = 10f * Vector3.UnitZ;
            _boss.Tick(new EnemyTickContext(2f, 0f, player, _intents, silent, _projectiles, _enemies));
            for (int i = 0; i < 12; i++)
            {
                Place(i == 1 || i == 7 ? -20f : 20f, 2f);
                _boss.Tick(new EnemyTickContext(2f, 2f * i, player, _intents, silent, _projectiles, _enemies));
            }
        }, 100);
    }

    [Test]
    public void InvalidInputs_AreRejectedOrIgnored()
    {
        Assert.Throws<ArgumentNullException>(() => new ColossusBehaviour(null, _stats));
        Assert.Throws<ArgumentNullException>(() => new ColossusBehaviour(_agent, null));
        Assert.Throws<ArgumentException>(() => _boss.Tick(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColossusTellProgress(1, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ColossusImpact(1, ColossusMove.Sweep, 0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ColossusArmed(1, ColossusMove.Sweep, Vector3.Zero, Vector2.UnitY, float.NaN, 20f, 1f));

        _agent.Blackboard.PlayerPosition = new Vector3(float.NaN, 0f, 2f);
        Tick(1f);
        Assert.That(_events.Count<ColossusArmed>(), Is.Zero);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ;
        Tick(0f);
        _agent.Blackboard.PlayerPosition = new Vector3(float.PositiveInfinity, 0f, 2f);
        FinishPass(0.35f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(3));
    }

    private void AssertTellAndFirstImpact(int sector)
    {
        int impacts = _events.Count<ColossusImpact>();
        Tick(0.675f);
        Assert.That(LastProgress(), Is.EqualTo(0.5f).Within(1e-6f));
        Tick(0.67f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(impacts));
        Tick(100f);
        Assert.That(LastProgress(), Is.EqualTo(1f));
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(impacts + 1));
        Assert.That(LastImpact().Sector, Is.EqualTo(sector));
    }

    private void AssertNextSector(float interval, int sector)
    {
        int impacts = _events.Count<ColossusImpact>();
        Tick(interval - 0.01f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(impacts));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(100f);
        Assert.That(_events.Count<ColossusImpact>(), Is.EqualTo(impacts + 1));
        Assert.That(LastImpact().Sector, Is.EqualTo(sector));
        if (sector == 1)
            Assert.That(LastImpact().RecoverySeconds, Is.Zero);
    }

    private void FinishPass(float interval)
    {
        Tick(1.35f);
        Tick(interval);
        Tick(interval);
    }

    private void Tick(float dt)
    {
        int moves = _intents.EnemyMoves.Count;
        _now += dt;
        _boss.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(moves + 1));
    }

    private void Place(float degrees, float radius)
    {
        float radians = degrees * (MathF.PI / 180f);
        _agent.Blackboard.PlayerPosition = new Vector3(MathF.Sin(radians) * radius, 0f,
            MathF.Cos(radians) * radius);
    }

    private ColossusArmed LastArm() => _events.Of<ColossusArmed>().Last();
    private ColossusImpact LastImpact() => _events.Of<ColossusImpact>().Last();
    private float LastProgress() => _events.Of<ColossusTellProgress>().Last().Fraction;

    private static EnemySpec Body(EnemyBehaviourKind behaviour = EnemyBehaviourKind.Static) =>
        new(new ContentId("enemy.colossus"), new LocKey("enemy.colossus.name"),
            500f, 2.2f, 8, 40, 300f, false, 20f, 9.5f, 1.35f, 1.8f, 40f, behaviour);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
