using System;
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
public sealed class RainOfArrowsTests
{
    private const int Capacity = 32;
    private const float Hp = 1_000f;
    private const float WeaponDamage = 17f;
    private const float PulseDamage = 0.6f * WeaponDamage;
    private const float Tolerance = 1e-3f;
    private static readonly ContentId RainId = new("skill.rain_of_arrows");
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private ZoneSystem _zones;
    private SimulatedClock _clock;
    private EffectRegistry _effects;
    private SkillRunner _runner;
    private RainOfArrowsHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, Capacity);
        _stats = Stats(_player, _events);
        _enemies = Enemies(_events);
        _zones = new ZoneSystem(_player.Health, _player.Blackboard, _events, _enemies, _player);
        _clock = new SimulatedClock();
        _handler = new RainOfArrowsHandler(_zones, _clock, _enemies, _stats, _player.Blackboard, _events);
        _effects = new EffectRegistry();
        _effects.Register<RainOfArrows>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, _events);
        _runner.Add(Skill());
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
        _events.Clear();
    }

    [Test]
    public void Pick_DensestGroupWins()
    {
        var points = new[] { Point(1, 1f), Point(2, 6f), Point(3, 6.5f), Point(4, 7f) };
        Assert.That(DensestPoint.TryPick(points, Vector3.Zero, 12f, 1.5f, out Vector3 centre), Is.True);
        Assert.That(centre, Is.EqualTo(points[1].Position));
    }

    [Test]
    public void Pick_TieBreaks()
    {
        var points = new[] { Point(3, 8f), Point(8, 4f), Point(2, -4f) };
        Assert.That(DensestPoint.TryPick(points, Vector3.Zero, 12f, 1f, out Vector3 centre), Is.True);
        Assert.That(centre, Is.EqualTo(points[2].Position));
        Array.Reverse(points);
        DensestPoint.TryPick(points, Vector3.Zero, 12f, 1f, out Vector3 reversed);
        Assert.That(reversed, Is.EqualTo(centre), "Input order never breaks a tie.");
    }

    [Test]
    public void Pick_UsesXZAndIncludesNeighboursBeyondAcquireRange()
    {
        var points = new[] { Point(1, 1f), Point(2, 12f, 100f), Point(3, 13f), Point(4, 13.5f) };
        Assert.That(DensestPoint.TryPick(points, Vector3.Zero, 12f, 1.5f, out Vector3 centre), Is.True);
        Assert.That(centre, Is.EqualTo(points[1].Position));
        Assert.That(DensestPoint.TryPick(Array.Empty<EnemyPoint>(), Vector3.Zero, 12f, 1.5f, out _), Is.False);
    }

    [Test]
    public void Pick_NoneInRange_Refused_CooldownKept()
    {
        EnemyAgent corpse = Spawn(2f);
        _enemies.ApplyDamage(corpse.Id, Hp * 2f, 0f, _player);
        Spawn(13f);
        _events.Clear();
        Assert.That(_runner.Cast(0, 1f, auto: false), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_zones.Count, Is.Zero);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_events.Count<RainOfArrowsCast>(), Is.Zero);

        Spawn(3f);
        _player.Blackboard.EnemiesInAcquireRange = 3;
        _runner.Tick(0f, 1f);
        Assert.That(_zones.Count, Is.EqualTo(1), "Refusal did not spend the auto gap either.");
    }

    [Test]
    public void Zone_AtCentre_DamagesInside()
    {
        EnemyAgent centre = Spawn(6f);
        EnemyAgent edge = Spawn(7.5f);
        EnemyAgent outside = Spawn(7.51f);
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
        // The densest centre is at 7.5, so put the edge and outsider around that fixed placement.
        Vector3 at = _zones.PositionAt(0);
        Move(centre, at.X - 1.5f);
        Move(edge, at.X + 1.5f, 80f);
        Move(outside, at.X + 1.51f);
        for (int i = 1; i <= 8; i++)
        {
            _zones.Tick(i * 0.25f);
        }

        Assert.That(centre.Health.Current, Is.EqualTo(Hp - 8f * PulseDamage).Within(Tolerance));
        Assert.That(edge.Health.Current, Is.EqualTo(centre.Health.Current).Within(Tolerance));
        Assert.That(outside.Health.Current, Is.EqualTo(Hp));
        Assert.That(_events.Count<ZoneBurned>(), Is.EqualTo(16));
        Assert.That(_zones.Count, Is.Zero);
    }

    [Test]
    public void Rank2_Damage()
    {
        Upgrade(PlayerStat.RainDamage, 0.25f);
        Upgrade(PlayerStat.WeaponDamage, 3f);
        EnemyAgent body = Spawn(3f);
        _runner.Cast(0, 0f, auto: false);
        _zones.Tick(2f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 8f * 0.6f * 20f * 1.25f).Within(Tolerance));
    }

    [Test]
    public void Rank3_WiderAndSlows()
    {
        Upgrade(PlayerStat.RainRadius, 0.5f);
        Upgrade(PlayerStat.RainSlow, 0.30f);
        EnemyAgent body = Spawn(3f);
        _runner.Cast(0, 0f, auto: false);
        EnemyAgent edge = Spawn(5f);
        _zones.Tick(0.25f);
        Assert.That(_zones.RadiusAt(0), Is.EqualTo(2f));
        Assert.That(edge.Health.Current, Is.EqualTo(Hp - PulseDamage).Within(Tolerance));
        Assert.That(edge.MoveSpeed.Value, Is.EqualTo(1.4f).Within(Tolerance));
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(1.4f).Within(Tolerance));
        Assert.That(_enemies.Remaining(edge.Id, EnemyStatus.Slowed), Is.EqualTo(0.35f).Within(Tolerance));
    }

    [Test]
    public void Rank3_RadiusAlsoChangesThePickedCluster()
    {
        Spawn(1f);
        EnemyAgent group = Spawn(7f);
        Spawn(9f);
        _handler.CanCast(Rain());
        Upgrade(PlayerStat.RainRadius, 0.5f);
        _runner.Cast(0, 0f, auto: false);
        Assert.That(_zones.PositionAt(0), Is.EqualTo(group.Position));
    }

    [Test]
    public void Slow_EndsAfterLeaving()
    {
        Upgrade(PlayerStat.RainSlow, 0.30f);
        EnemyAgent body = Spawn(3f);
        _runner.Cast(0, 0f, auto: false);
        _zones.Tick(0.25f);
        Move(body, 30f);
        StepEnemies(0.34f, 0.59f);
        _zones.Tick(0.59f);
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(1.4f).Within(Tolerance));
        StepEnemies(0.02f, 0.61f);
        _zones.Tick(0.61f);
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(2f));
        Assert.That(_enemies.Has(body.Id, EnemyStatus.Slowed), Is.False);
    }

    [Test]
    public void Cast_PublishesOnce()
    {
        Upgrade(PlayerStat.RainRadius, 0.5f);
        _clock.Now = 4f;
        EnemyAgent body = Spawn(6f);
        _runner.Cast(0, 4f, auto: false);
        _zones.Tick(4.25f);
        Assert.That(_events.Count<RainOfArrowsCast>(), Is.EqualTo(1));
        RainOfArrowsCast cast = _events.Single<RainOfArrowsCast>();
        Assert.That(cast.Centre, Is.EqualTo(body.Position));
        Assert.That(cast.Radius, Is.EqualTo(2f));
        Assert.That(cast.Duration, Is.EqualTo(2f));
        Assert.That(body.Health.Current, Is.EqualTo(Hp - PulseDamage).Within(Tolerance));
        _zones.Tick(6f);
        Assert.That(_events.Count<RainOfArrowsCast>(), Is.EqualTo(1));
    }

    [Test]
    public void Auto_ThreeInRangeAndTenSecondCooldown()
    {
        Spawn(3f);
        Spawn(3.5f);
        Spawn(4f);
        _player.Blackboard.EnemiesInAcquireRange = 2;
        _runner.Tick(0f, 0f);
        Assert.That(_zones.Count, Is.Zero);
        _player.Blackboard.EnemiesInAcquireRange = 3;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(10f));
        _zones.Tick(2f);
        _runner.Tick(0f, 9.99f);
        Assert.That(_zones.Count, Is.Zero);
        _clock.Now = 10f;
        _runner.Tick(0f, 10f);
        Assert.That(_zones.Count, Is.EqualTo(1));
    }

    [Test]
    public void Auto_RefusesStaleRangeCountWithNoLivingTarget()
    {
        _player.Blackboard.EnemiesInAcquireRange = 3;
        _runner.Tick(0f, 0f);
        Assert.That(_zones.Count, Is.Zero);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
    }

    [Test]
    public void Rank4_Cooldown()
    {
        _effects.Apply(new ModifySkillCooldown(RainId, ModifierKind.Flat, -2f), this);
        Spawn(3f);
        _runner.Cast(0, 0f, auto: false);
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(8f));
    }

    [Test]
    public void Zone_DoesNotFollow()
    {
        EnemyAgent body = Spawn(6f);
        _runner.Cast(0, 0f, auto: false);
        _player.Blackboard.PlayerPosition = new Vector3(30f, 0f, 0f);
        Move(body, 30f);
        _zones.Tick(0.25f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        Assert.That(_zones.PositionAt(0), Is.EqualTo(new Vector3(6f, 0f, 0f)));
        EnemyAgent newcomer = Spawn(6f);
        _zones.Tick(0.5f);
        Assert.That(newcomer.Health.Current, Is.EqualTo(Hp - PulseDamage).Within(Tolerance));
    }

    [Test]
    public void Zone_CompactionKeepsSlowWithItsZone()
    {
        EnemyAgent body = Spawn(3f);
        _zones.Spawn(1f, 0.25f, 1f, 0.25f, 0f, this, ZoneSide.BurnsEnemies);
        Upgrade(PlayerStat.RainSlow, 0.3f);
        _runner.Cast(0, 0f, auto: false);
        _zones.Tick(0.25f);
        Assert.That(_zones.Count, Is.EqualTo(1));
        _zones.Tick(0.5f);
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(1.4f).Within(Tolerance));
        _zones.Clear();
        _zones.Spawn(5f, 1f, 1f, 0.25f, 0.5f, this, ZoneSide.BurnsEnemies);
        StepEnemies(0.36f, 0.86f);
        _zones.Tick(0.86f);
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(2f), "A plain burn never inherits a retired rain's slow.");
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Character(), silent, intents, Capacity);
        PlayerStats stats = Stats(player, silent);
        stats.Resolve(PlayerStat.RainSlow).Add(new Modifier(ModifierKind.Flat, 0.3f, this));
        EnemySystem enemies = Enemies(silent);
        EnemyAgent body = enemies.Spawn(new ContentId("enemy.husk"), new Vector3(3f, 0f, 0f));
        // Keep damage, events, slow renewal and expiry live over the entire allocation probe.
        body.Health.MaxHp.Base = 1e7f;
        body.Health.Reset();
        var zones = new ZoneSystem(player.Health, player.Blackboard, silent, enemies, player);
        var clock = new SimulatedClock();
        var effects = new EffectRegistry();
        effects.Register<RainOfArrows>(new RainOfArrowsHandler(zones, clock, enemies, stats, player.Blackboard, silent));
        var runner = new SkillRunner(effects, player.Blackboard, silent);
        runner.Add(Skill());
        var projectiles = new ProjectileSystem(silent, Capacity);
        var lanes = new LaneSystem(silent);
        player.Blackboard.EnemiesInAcquireRange = 3;
        int casts = 0;
        AllocationAssert.None(() =>
        {
            intents.Clear();
            clock.Now += 10f;
            runner.Tick(0.25f, clock.Now);
            casts += zones.Count;
            zones.Tick(clock.Now + 0.25f);
            enemies.Tick(new EnemyTickContext(0.25f, clock.Now + 0.25f, player, intents, silent, projectiles, enemies, lanes));
            zones.Tick(clock.Now + 2f);
        });
        Assert.That(casts, Is.GreaterThan(10_000));
        Assert.That(body.Health.Current, Is.LessThan(1e7f - 100_000f));
        Assert.That(body.MoveSpeed.Value, Is.EqualTo(1.4f).Within(Tolerance));
    }

    [Test]
    public void Guards()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RainOfArrows(bad, 2f, 0.25f, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RainOfArrows(1.5f, bad, 0.25f, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RainOfArrows(1.5f, 2f, bad, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RainOfArrows(1.5f, 2f, 0.25f, bad));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new RainOfArrows(1.5f, 2f, 0.000001f, 0.6f));
        Assert.Throws<ArgumentNullException>(() => _handler.CanCast(null));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(Rain(), null));
        foreach (float bad in new[] { -1f, float.NaN, 1f, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _zones.Spawn(1f, 2f, 1f, 0.25f, 0f, this, ZoneSide.BurnsEnemies, slow: bad));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _zones.Spawn(1f, 2f, 1f, 0.25f, 0f, this, slow: 0.3f));
    }

    private static EnemyPoint Point(int id, float x, float y = 0f) => new(id, new Vector3(x, y, 0f), 0.4f, false);
    private EnemyAgent Spawn(float x) => _enemies.Spawn(new ContentId("enemy.husk"), new Vector3(x, 0f, 0f));
    private void Upgrade(PlayerStat stat, float value) => _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, value), this);
    private static RainOfArrows Rain() => new(1.5f, 2f, 0.25f, 0.6f);

    private void Move(EnemyAgent agent, float x, float y = 0f)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = agent.Id;
        sense.Position = new Vector3(x, y, 0f);
        sense.HasLineOfSight = true;
        _enemies.Ingest(snapshot);
    }

    private void StepEnemies(float dt, float now) => _enemies.Tick(new EnemyTickContext(
        dt, now, _player, _intents, _events, new ProjectileSystem(_events, Capacity), _enemies, new LaneSystem(_events)));

    private static SkillSpec Skill() => new(RainId, new LocKey("skill.rain.name"), new LocKey("skill.rain.desc"),
        SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(10f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 3f) }),
            new IEffect[] { Rain() }));

    private static PlayerStats Stats(PlayerCombat player, IDomainEvents events) => new(player,
        new PlayerMotor(Character().Movement, Vector3.UnitZ), new LevelTracker(Scalings.Xp(), events));

    private static EnemySystem Enemies(IDomainEvents events) => new(
        new ContentCatalog(new[] { Character() }, new[] { Husk() }, new[] { Mode() }), events,
        new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);

    private static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, WeaponDamage, 1.5f, 12f, 360f, 0.15f, 25f, 3f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    private static EnemySpec Husk() => new(new ContentId("enemy.husk"), new LocKey("enemy.husk.name"),
        Hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);

    private static ModeSpec Mode() => new(new ContentId("mode.jungle"), new LocKey("mode.jungle.name"),
        1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
}
