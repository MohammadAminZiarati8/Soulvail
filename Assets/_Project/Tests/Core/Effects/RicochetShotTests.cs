using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class RicochetShotTests
{
    private const int Capacity = 16;
    private static readonly ContentId SkillId = new("skill.ricochet-shot");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private RecordingEvents _events;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private LevelTracker _progression;
    private EffectRegistry _effects;
    private RicochetShotHandler _handler;
    private RicochetShot _shot;
    private SkillRunner _runner;
    private SimulatedClock _clock;
    private float _wallX;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [Test]
    public void Cast_PicksTarget_ThenNearestClear()
    {
        EnemyAgent nearest = Spawn(0f, 3f);
        EnemyAgent tied = Spawn(-3f);
        EnemyAgent current = Spawn(7f);
        _player.Blackboard.CurrentTargetId = current.Id;

        Cast();
        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(current.Position));

        _projectiles.Clear();
        _events.Clear();
        current.Blackboard.IsBehindCover = true;
        Cast();
        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(nearest.Position));

        _projectiles.Clear();
        _events.Clear();
        nearest.Blackboard.IsBehindCover = true;
        Cast();
        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(tied.Position));
    }

    [Test]
    public void Cast_NoneInRange_Refused_CooldownKept()
    {
        Spawn(13f);
        Spawn(2f).Blackboard.IsBehindCover = true;
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    [Test]
    public void AutoCast_RequiresThreeEnemiesAndSpendsEightSeconds()
    {
        Spawn(3f);
        _player.Blackboard.EnemiesInAcquireRange = 2;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);

        _player.Blackboard.EnemiesInAcquireRange = 3;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(8f));
        Assert.That(_runner.IsReady(0), Is.False);
    }

    [Test]
    public void Hit_DamageMatchesFormula()
    {
        Spawn(3f);
        Spawn(6f);
        _ = new LevelGrowth(new LevelGrowthSpec(0f, 0.02f), _progression, _effects, _player.Health);
        _progression.Grant(_progression.XpToNext);
        Upgrade(PlayerStat.RicochetDamage, 0.25f);
        _player.HitAndRun.MomentumPerMetre.Base = 0.02f;
        _player.HitAndRun.OnMoved(2f, 0f);
        _player.HitAndRun.OnMoved(3f, 0f);

        _handler.Apply(new RicochetShot(1.5f), this);
        Step();
        Step();

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(2));
        foreach (EnemyDamaged hit in _events.Of<EnemyDamaged>())
        {
            Assert.That(hit.Amount, Is.EqualTo(1.5f * 17f * 1.02f * 1.25f * 1.10f).Within(0.001f));
        }
    }

    [Test]
    public void Bounce_ToNearestUnhit_WithinFive()
    {
        EnemyAgent first = Spawn(3f);
        EnemyAgent nearest = Spawn(3f, 2f);
        EnemyAgent tied = Spawn(3f, -2f);
        Spawn(9f);
        Upgrade(PlayerStat.RicochetHits, 2f);

        Cast();
        Drain();

        AssertHits(first, nearest, tied);
    }

    [Test]
    public void Bounce_NeverRepeatsAnEnemy()
    {
        EnemyAgent first = Spawn(3f);
        EnemyAgent next = Spawn(1f, 2f);
        _player.Blackboard.CurrentTargetId = first.Id;

        Cast();
        Drain();

        AssertHits(first, next);
    }

    [Test]
    public void Bounce_CoverBlocksOnlyTheSegmentFromTheImpact()
    {
        EnemyAgent first = Spawn(3f);
        Spawn(5f);
        EnemyAgent clear = Spawn(2f, 3f);
        clear.Blackboard.IsBehindCover = true;
        _wallX = 4f;

        Cast();
        Drain();

        AssertHits(first, clear);
    }

    [Test]
    public void Chain_EndsWhenCoverAppearsInFlight()
    {
        Spawn(3f);
        Spawn(6f);
        Cast();
        _wallX = 2f;

        Step();

        Assert.That(_events.Single<ProjectileImpacted>().Position.X, Is.EqualTo(2f));
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.False);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    [TestCase(false, TestName = "Chain_EndsOnMiss")]
    [TestCase(true, TestName = "Chain_EndsOnGuard")]
    public void Chain_EndsOnMissOrGuard(bool blocked)
    {
        EnemyAgent first = Spawn(3f);
        Spawn(7f);
        Cast();
        if (blocked)
        {
            first.Health.SetExternalInvulnerable(true);
        }
        else
        {
            var snapshot = new WorldSnapshot(Capacity);
            ref EnemySense sense = ref snapshot.AddEnemy();
            sense.Id = first.Id;
            sense.Position = new Vector3(3f, 0f, 3f);
            _enemies.Ingest(snapshot);
        }

        Step();

        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(1));
        Assert.That(_projectiles.InFlightCount, Is.Zero);
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.EqualTo(blocked));
        if (blocked)
        {
            Assert.That(_events.Single<EnemyDamaged>().Blocked, Is.True);
        }
    }

    [Test]
    public void ChainHit_StartsHitAndRunBurst()
    {
        Spawn(3f);
        Spawn(6f);
        Upgrade(PlayerStat.HitSpeedBurst, 0.2f);

        Cast();
        Step();
        Assert.That(_player.HitAndRun.SpeedBonus(_clock.Now), Is.EqualTo(0.2f));
        Step(10f);
        Assert.That(_player.HitAndRun.SpeedBonus(_clock.Now), Is.EqualTo(0.2f));
    }

    [Test]
    public void Fired_FlagsAndIndex()
    {
        Spawn(3f);
        Spawn(6f);
        Spawn(9f);

        Cast();
        Drain();
        FireOrdinary();

        var fired = _events.Of<ProjectileFired>();
        Assert.That(fired.Select(arrow => arrow.IsRicochet), Is.EqualTo(new[] { true, true, true, false }));
        Assert.That(fired.Select(arrow => arrow.ChainIndex), Is.EqualTo(new[] { 0, 1, 2, 0 }));
        Assert.That(fired[1].Origin, Is.EqualTo(_events.Of<ProjectileImpacted>()[0].Position));
    }

    [TestCase(1, 3, 8f, TestName = "Rank1_ThreeHits_EightSeconds")]
    [TestCase(2, 4, 8f, TestName = "Rank2_FourHits")]
    [TestCase(4, 5, 6f, TestName = "Rank4_FiveHits_SixSeconds")]
    public void Ranks_ControlHitsAndCooldown(int rank, int hits, float cooldown)
    {
        for (int i = 1; i <= 6; i++)
        {
            Spawn(2f * i);
        }

        if (rank >= 2)
        {
            Upgrade(PlayerStat.RicochetHits, 1f);
        }

        if (rank == 4)
        {
            _effects.Apply(new ModifyStat(PlayerStat.RicochetHits, ModifierKind.Flat, 1f), new object());
            _effects.Apply(new ModifySkillCooldown(SkillId, ModifierKind.Flat, -2f), this);
        }

        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(cooldown));
        Drain();
        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(hits));
    }

    [Test]
    public void StageBoundary_ClearsChains()
    {
        EnemyAgent first = Spawn(3f);
        EnemyAgent next = Spawn(6f);
        Cast();
        Step();
        int bounceId = _events.Of<ProjectileFired>()[1].Id;
        FireOrdinary();

        _projectiles.ClearRicochets();

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(1));
        Assert.That(_events.Of<ProjectileImpacted>()[1].Id, Is.EqualTo(bounceId));
        Assert.That(_events.Of<ProjectileImpacted>()[1].Hit, Is.False);
        Drain();
        AssertHits(first);
        Assert.That(next.IsAlive, Is.True);
    }

    [Test]
    public void Bounce_DoesNotAllocate()
    {
        Initialise(new SilentEvents(), hp: 1_000_000f);
        Spawn(3f);
        Spawn(6f);
        Spawn(9f);
        AllocationAssert.None(() =>
        {
            Cast();
            Step();
            Step();
            Step();
        });
        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    private void Initialise(IDomainEvents events, float hp = 1000f)
    {
        CharacterSpec character = Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { Body(hp) });
        _player = new PlayerCombat(character, events, new RecordingIntents(), Capacity);
        _progression = new LevelTracker(Scalings.Xp(), events);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ), _progression);
        _enemies = new EnemySystem(catalog, events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        _wallX = float.PositiveInfinity;
        _projectiles = new ProjectileSystem(events, Capacity, traceCover: TraceCover);
        _clock = new SimulatedClock();
        _shot = new RicochetShot(1f);
        _handler = new RicochetShotHandler(_projectiles, _enemies, _stats, _player.Blackboard, character, _clock);
        _effects = new EffectRegistry();
        _effects.Register<RicochetShot>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, events);
        _runner.Add(new SkillSpec(SkillId, new LocKey("skill.ricochet.name"), new LocKey("skill.ricochet.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(8f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 3f) }),
                new IEffect[] { _shot })));
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    private Vector3? TraceCover(Vector3 from, Vector3 to)
    {
        if ((from.X < _wallX && to.X >= _wallX) || (to.X <= _wallX && from.X > _wallX))
        {
            return Vector3.Lerp(from, to, (_wallX - from.X) / (to.X - from.X));
        }

        return null;
    }

    private void Cast() => _handler.Apply(_shot, this);

    private void Step(float seconds = 1f)
    {
        _clock.Now += seconds;
        _projectiles.Tick(_clock.Now, _player.Blackboard.PlayerPosition, _player, _enemies);
    }

    private void Drain()
    {
        for (int i = 0; i < 6 && _projectiles.InFlightCount > 0; i++)
        {
            Step();
        }

        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    private void AssertHits(params EnemyAgent[] enemies) =>
        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Id), Is.EqualTo(enemies.Select(enemy => enemy.Id)));

    private void FireOrdinary() => _projectiles.Fire(
        new Projectile(EnemyId, 1, Vector3.Zero, -Vector3.UnitX, 25f, 0.1f, 0f), _clock.Now);

    private void Upgrade(PlayerStat stat, float value) =>
        _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, value), this);

    private EnemyAgent Spawn(float x, float z = 0f) => _enemies.Spawn(EnemyId, new Vector3(x, 0f, z));

    private static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    private static EnemySpec Body(float hp) => new(EnemyId, new LocKey("enemy.husk.name"),
        hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
}
