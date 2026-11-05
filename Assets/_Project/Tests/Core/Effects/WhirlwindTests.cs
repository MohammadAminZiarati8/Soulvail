using System;
using System.Collections.Generic;
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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class WhirlwindTests
{
    private const int Capacity = 64;
    private static readonly ContentId SkillId = new("skill.skirmisher.whirlwind");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private readonly Whirlwind _effect = new(0.6f);

    private RecordingEvents _events;
    private ProjectileSystem _projectiles;
    private PlayerCombat _combat;
    private EnemySystem _enemies;
    private WhirlwindHandler _handler;
    private EffectRegistry _effects;
    private SkillRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [Test]
    public void Cast_FiresFirstRingNow_SecondAfterGap()
    {
        Cast();
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(12));
        _handler.Tick(0.299f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(12));
        _handler.Tick(0.3f, Vector3.Zero);
        _handler.Tick(1f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(24));
    }

    [Test]
    public void LaterRing_FromCurrentPosition()
    {
        _combat.Blackboard.PlayerPosition = Vector3.UnitZ;
        Cast();
        _handler.Tick(0.3f, Vector3.UnitZ + Vector3.UnitX);
        var fired = _events.Of<ProjectileFired>();
        Assert.That(fired[0].Origin, Is.EqualTo(Vector3.UnitZ));
        Assert.That(fired[12].Origin, Is.EqualTo(Vector3.UnitZ + Vector3.UnitX));
        var rings = _events.Of<WhirlwindRingFired>();
        Assert.That(rings, Has.Count.EqualTo(2));
        Assert.That(rings[0].Origin, Is.EqualTo(fired[0].Origin));
        Assert.That(rings[1].Origin, Is.EqualTo(fired[12].Origin));
    }

    [Test]
    public void Ring_EvenAngles_SecondTurnedHalfStep()
    {
        Cast();
        _handler.Tick(0.3f, Vector3.Zero);
        var fired = _events.Of<ProjectileFired>();
        for (int i = 0; i < fired.Count; i++)
        {
            float heading = ((i % 12) + (i / 12 * 0.5f)) * MathF.PI / 6f;
            Vector3 expected = 12f * new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
            Assert.That(Vector3.Distance(fired[i].Target, expected), Is.LessThan(0.001f));
        }
    }

    [Test]
    public void Arrow_DamageAndHitAndRun()
    {
        EnemyAgent first = _enemies.Spawn(EnemyId, 8f * Vector3.UnitZ);
        EnemyAgent behind = _enemies.Spawn(EnemyId, 8.1f * Vector3.UnitZ);
        var snapshot = new WorldSnapshot(Capacity);
        _combat.Tick(0f, 0f, snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
        Upgrade(PlayerStat.WeaponDamage, 3f);
        Upgrade(PlayerStat.HitSpeedBurst, 0.2f);
        Upgrade(PlayerStat.MomentumPerMetre, 0.1f);
        Upgrade(PlayerStat.SteadyAimPerStack, 0.1f);
        Upgrade(PlayerStat.SteadyAimStopCrit, 2f);
        Upgrade(PlayerStat.EagleEyeFarBonus, 0.5f);
        _combat.HitAndRun.OnMoved(2f, 0f);

        Cast();
        _projectiles.Tick(0.4f, Vector3.Zero, _combat, _enemies);

        EnemyDamaged hit = _events.Single<EnemyDamaged>();
        Assert.That(hit.Id, Is.EqualTo(first.Id));
        Assert.That(hit.Amount, Is.EqualTo(0.6f * 20f * 1.2f * 2f * 1.5f).Within(0.001f));
        Assert.That(behind.Health.Current, Is.EqualTo(behind.Health.MaxHp.Value));
        Assert.That(_combat.HitAndRun.SpeedBonus(0.4f), Is.EqualTo(0.2f));
    }

    [Test]
    public void Arrow_StopsAtCoverBeforeEnemy()
    {
        Initialise(_events, traceCover: (from, to) => from.Z < 2f && to.Z >= 2f
            ? Vector3.Lerp(from, to, (2f - from.Z) / (to.Z - from.Z)) : null);
        _enemies.Spawn(EnemyId, 4f * Vector3.UnitZ);

        Cast();
        _projectiles.Tick(0.1f, Vector3.Zero, _combat, _enemies);

        Assert.That(_events.Of<ProjectileImpacted>()[0].Position, Is.EqualTo(2f * Vector3.UnitZ));
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Cast_DoesNotStopMovementOrShot()
    {
        var intents = new RecordingIntents();
        RunSession session = Start(intents);
        EnemySpawned enemy = _events.Single<EnemySpawned>();
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.05f, MoveInput = Vector2.UnitX };
        snapshot.AddEnemy() = Sense(enemy);

        session.Tick(snapshot);
        session.MovementSkill();
        session.CastSkillNow(SkillId);
        Assert.That(_events.Count<WhirlwindCast>(), Is.EqualTo(1));
        for (int i = 0; i < 30; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(session.State.PlayerVelocity.X, Is.GreaterThan(0f));
        Assert.That(_events.Count<ProjectileFired>(), Is.GreaterThan(24));

        Assert.That(intents.Charges, Has.Count.EqualTo(1));
    }

    [Test]
    public void Cast_NoEnemies_StillFires()
    {
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
        WhirlwindCast cast = _events.Single<WhirlwindCast>();
        Assert.That(cast.Origin, Is.EqualTo(Vector3.Zero));
        Assert.That(cast.Arrows, Is.EqualTo(12));
        Assert.That(cast.Rings, Is.EqualTo(2));
        Assert.That(cast.Gap, Is.EqualTo(0.3f));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(12));
    }

    [Test]
    public void AutoCast_RequiresThreeWithinSixMetres()
    {
        _combat.Blackboard.EnemiesWithin6m = 2;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<WhirlwindCast>(), Is.Zero);

        _combat.Blackboard.EnemiesWithin6m = 3;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<WhirlwindCast>(), Is.EqualTo(1));
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(10f));
        Assert.That(_runner.IsReady(0), Is.False);
    }

    [Test]
    public void Rank2_SixteenArrows()
    {
        Upgrade(PlayerStat.WhirlwindArrows, 4f);
        Cast();
        Assert.That(_events.Single<WhirlwindCast>().Arrows, Is.EqualTo(16));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(16));
    }

    [Test]
    public void Rank3_ThreeRings_EightSeconds()
    {
        Upgrade(PlayerStat.WhirlwindArrows, 4f);
        Upgrade(PlayerStat.WhirlwindRings, 1f);
        _effects.Apply(new ModifySkillCooldown(SkillId, ModifierKind.Flat, -2f), this);

        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(8f));
        _handler.Tick(0.3f, Vector3.Zero);
        _handler.Tick(0.599f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(32));
        _handler.Tick(0.6f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(48));
    }

    [Test]
    public void Boundary_DropsPending()
    {
        RunSession session = Start(new RecordingIntents(), withWaves: true);
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f, SpawnPoints = new[] { 6f * Vector3.UnitZ } };
        for (int i = 0; i < 160 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(snapshot);
        }

        snapshot.AddEnemy() = Sense(_events.Single<EnemySpawned>());
        snapshot.Dt = 0.25f;
        session.CastSkillNow(SkillId);
        session.Tick(snapshot);
        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));

        session.Tick(snapshot);
        session.Tick(snapshot);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(12));
    }

    [Test]
    public void Death_DropsPending()
    {
        Cast();
        _combat.ApplyDamage(1000f, 0.1f);
        _handler.Tick(0.3f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(12));
    }

    [Test]
    public void PoolFull_Skips()
    {
        Initialise(_events, capacity: 3);
        Cast();
        _handler.Tick(0.3f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(3));

        _projectiles.Clear();
        _handler.Tick(1f, Vector3.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(3));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        AllocationAssert.None(() =>
        {
            Cast();
            _handler.Tick(0.3f, Vector3.Zero);
            _projectiles.Clear();
        });
    }

    private RunSession Start(RecordingIntents intents, bool withWaves = false)
    {
        CharacterSpec character = Character();
        ContentId modeId = new("mode.whirlwind-test");
        var scaling = new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(Capacity, 1000), new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(modeId, new LocKey("mode.whirlwind.name"), 1, true, 0, scaling,
            Scalings.Xp(), withWaves ? new[] { new RosterEntry(EnemyId, 1) } : Array.Empty<RosterEntry>());
        SkillSpec moving = Passive("skill.test.running-shot", PlayerStat.FireWhileMoving);
        SkillSpec filler = Passive("skill.test.filler", PlayerStat.MaxHp);
        var tree = new SkillTreeSpec(new ContentId("tree.test"), character.Id,
            new[] { Branch(SkillId), Branch(moving.Id), Branch(filler.Id) });
        var catalog = new ContentCatalog(new[] { character }, new[] { Body(withWaves ? 1f : 1000f) },
            new[] { mode }, new[] { Skill(), moving, filler }, new[] { tree });
        var random = new FixedRandom(7);
        var session = new RunSession(catalog, random, _events, intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, character.Id, 7, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 3, 0f, 0,
            new[] { SkillId, moving.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        var spawn = withWaves ? SpawnPlan.Empty
            : new SpawnPlan(new[] { new SpawnPlan.Entry(EnemyId, 4f * Vector3.UnitZ) });
        session.Start(new RunConfig(modeId, character.Id, 7, 1, spawn, saved));
        session.SetAutoCast(SkillId, false);
        return session;
    }

    private static EnemySense Sense(EnemySpawned enemy) =>
        new() { Id = enemy.Id, Position = enemy.Position, HasLineOfSight = true };

    private static SkillSpec Passive(string id, PlayerStat stat) =>
        new(new ContentId(id), new LocKey("skill.test.name"), new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { new ModifyStat(stat, ModifierKind.Flat, 1f) });

    private static SkillBranchSpec Branch(ContentId id) =>
        new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });

    private void Initialise(IDomainEvents events, int capacity = Capacity,
        Func<Vector3, Vector3, Vector3?> traceCover = null)
    {
        CharacterSpec character = Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { Body() });
        _combat = new PlayerCombat(character, events, new RecordingIntents(), Capacity);
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);
        var stats = new PlayerStats(_combat, motor, new LevelTracker(Scalings.Xp(), events));
        _enemies = new EnemySystem(catalog, events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        _projectiles = new ProjectileSystem(events, capacity, traceCover: traceCover);
        _handler = new WhirlwindHandler(_projectiles, _combat, motor, stats, character, new SimulatedClock(), events);
        _effects = new EffectRegistry();
        _effects.Register<Whirlwind>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(stats));
        _runner = new SkillRunner(_effects, _combat.Blackboard, events);
        _runner.Add(Skill());
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    private void Cast() => _handler.Apply(_effect, this);

    private void Upgrade(PlayerStat stat, float value) =>
        _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, value), this);

    private SkillSpec Skill() => new(SkillId, new LocKey("skill.whirlwind.name"), new LocKey("skill.whirlwind.desc"),
        SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(10f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesWithin6m, TriggerComparison.AtLeast, 3f) }),
            new IEffect[] { _effect }));

    private static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    private static EnemySpec Body(float hp = 1000f) => new(EnemyId, new LocKey("enemy.husk.name"),
        hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
}
