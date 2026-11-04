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

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class TrapSystemTests
{
    private const int Capacity = 8;
    private const float Hp = 1_000f;
    private const float Tolerance = 1e-3f;
    private static readonly ContentId SnareId = new("skill.hunters_snare");
    private RecordingEvents _events;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private TrapSystem _traps;
    private SimulatedClock _clock;
    private PlaceSnareHandler _handler;
    private EffectRegistry _effects;
    private SkillRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _player = new PlayerCombat(Character(), _events, new RecordingIntents(), Capacity);
        _stats = Stats(_player, _events);
        _enemies = Enemies(_events);
        _traps = new TrapSystem(_enemies, _player, _events);
        _clock = new SimulatedClock();
        _handler = new PlaceSnareHandler(_traps, _clock, _stats, _player.Blackboard);
        _effects = new EffectRegistry();
        _effects.Register<PlaceSnare>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, _events);
        _runner.Add(Skill());
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    [Test]
    public void Place_AtPlayer_ArmsAfterDelay()
    {
        var at = new Vector3(4f, 20f, 5f);
        _player.Blackboard.PlayerPosition = at;
        EnemyAgent body = Spawn(at);
        _clock.Now = 2f;
        _handler.Apply(Snare(), this);
        TrapPlaced placed = _events.Single<TrapPlaced>();
        Assert.That(placed.At, Is.EqualTo(at));
        Assert.That(placed.Id, Is.Positive);
        _player.Blackboard.PlayerPosition = Vector3.Zero;
        _traps.Tick(2.299f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        Assert.That(_traps.Count, Is.EqualTo(1));
        _traps.Tick(2.3f);
        Assert.That(_traps.Count, Is.Zero);
        Assert.That(_events.Single<TrapSprung>().Id, Is.EqualTo(placed.Id));
    }

    [Test]
    public void Spring_FirstEnemyOnly_RootsAndHurts()
    {
        EnemyAgent corpse = Spawn(Vector3.Zero);
        _enemies.ApplyDamage(corpse.Id, Hp * 2f, 0f, _player);
        EnemyAgent first = Spawn(new Vector3(0f, 100f, 1.2f));
        EnemyAgent second = Spawn(Vector3.Zero);
        _handler.Apply(Snare(), this);
        _traps.Tick(0.3f);
        _traps.Tick(0.6f);
        Assert.That(first.Health.Current, Is.EqualTo(Hp - 51f));
        Assert.That(_enemies.Remaining(first.Id, new ContentId(EnemyStatuses.RootedKey)), Is.EqualTo(2f));
        Assert.That(second.Health.Current, Is.EqualTo(Hp));
        Assert.That(_enemies.Has(second.Id, new ContentId(EnemyStatuses.RootedKey)), Is.False);
        Assert.That(_events.Single<TrapSprung>().EnemyId, Is.EqualTo(first.Id));
        Assert.That(_events.Count<TrapExpired>(), Is.Zero);
    }

    [Test]
    public void Spring_Boss_NotRooted_StillHurt()
    {
        EnemyAgent boss = _enemies.SpawnBoss(new ContentId("boss.test"), Vector3.Zero);
        Upgrade(PlayerStat.SnareRoot, 1f);
        _handler.Apply(Snare(), this);
        _traps.Tick(0.3f);
        Assert.That(_enemies.Remaining(boss.Id, new ContentId(EnemyStatuses.RootedKey)), Is.Zero);
        Assert.That(boss.Health.Current, Is.EqualTo(Hp - 51f));
    }

    [Test]
    public void Spring_OutsideRadiusWaits_ThenEnteringSprings()
    {
        EnemyAgent body = Spawn(new Vector3(1.201f, 0f, 0f));
        _handler.Apply(Snare(), this);
        _traps.Tick(0.3f);
        Assert.That(_traps.Count, Is.EqualTo(1));
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = body.Id;
        sense.Position = new Vector3(1.2f, -100f, 0f);
        _enemies.Ingest(snapshot);
        _traps.Tick(0.4f);
        Assert.That(_events.Single<TrapSprung>().EnemyId, Is.EqualTo(body.Id));
    }

    [Test]
    public void Spring_LethalHit_UsesNormalDeathDrain()
    {
        EnemyAgent body = Spawn(Vector3.Zero);
        _traps.Place(Vector3.Zero, 2f, Hp * 2f, 10f, 0f);
        _traps.Tick(0.3f);
        Assert.That(body.IsAlive, Is.False);
        Assert.That(_events.Single<EnemyDied>().Id, Is.EqualTo(body.Id));
        Assert.That(_enemies.DrainDeaths(new EnemyDeath[Capacity]), Is.EqualTo(1));
        Assert.That(_traps.Count, Is.Zero);
    }

    [Test]
    public void Expire_AfterLifetime()
    {
        int id = _traps.Place(Vector3.Zero, 2f, 51f, 10f, 4f);
        _traps.Tick(13.999f);
        Assert.That(_traps.Count, Is.EqualTo(1));
        // An enemy arriving on the expiry instant cannot spring an expired trap.
        EnemyAgent body = Spawn(Vector3.Zero);
        _traps.Tick(14f);
        _traps.Tick(20f);
        Assert.That(_traps.Count, Is.Zero);
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        Assert.That(_events.Single<TrapExpired>().Id, Is.EqualTo(id));
        Assert.That(_events.Count<TrapSprung>(), Is.Zero);
    }

    [Test]
    public void Third_ReplacesOldest()
    {
        int first = _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f);
        int second = _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f);
        int third = _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f);
        Assert.That(_traps.Count, Is.EqualTo(TrapSystem.Capacity));
        Assert.That(_events.Single<TrapExpired>().Id, Is.EqualTo(first));
        _events.Clear();
        Spawn(Vector3.Zero);
        _traps.Tick(0.3f);
        IReadOnlyList<TrapSprung> sprung = _events.Of<TrapSprung>();
        Assert.That(sprung.Count, Is.EqualTo(2));
        Assert.That(sprung[0].Id, Is.EqualTo(second));
        Assert.That(sprung[1].Id, Is.EqualTo(third));
    }

    [Test]
    public void Trigger_NearestEnemyBelow3m()
    {
        Assert.That(_player.Blackboard.NearestEnemyDistance, Is.EqualTo(float.PositiveInfinity));
        Census();
        _runner.Tick(0f, 0f);
        Assert.That(_traps.Count, Is.Zero);
        Spawn(new Vector3(3f, 200f, 0f));
        Census();
        _runner.Tick(0f, 0f);
        Assert.That(_traps.Count, Is.Zero, "Below is strict, and perception is XZ.");
        EnemyAgent near = Spawn(new Vector3(2.99f, -200f, 0f));
        Census();
        _runner.Tick(0f, 0f);
        Assert.That(_traps.Count, Is.EqualTo(1));
        _runner.Tick(9.99f, 9.99f);
        Assert.That(_events.Count<TrapPlaced>(), Is.EqualTo(1));
        _runner.Tick(0.01f, 10f);
        Assert.That(_events.Count<TrapPlaced>(), Is.EqualTo(2));
        _enemies.ApplyDamage(near.Id, Hp * 2f, 10f, _player);
        Census();
        Assert.That(_player.Blackboard.NearestEnemyDistance, Is.EqualTo(3f));
        _enemies.Clear();
        Census();
        Assert.That(_player.Blackboard.NearestEnemyDistance, Is.EqualTo(float.PositiveInfinity));
        _player.Blackboard.NearestEnemyDistance = 1f;
        _player.Blackboard.Reset();
        Assert.That(_player.Blackboard.NearestEnemyDistance, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void Ranks_AddUp()
    {
        Assert.That(_stats.Has(PlayerStat.SnareRoot), Is.True);
        Assert.That(_stats.Has(PlayerStat.SnareDamage), Is.True);
        Assert.That(_stats.Resolve(PlayerStat.SnareRoot).Value, Is.Zero);
        Assert.That(_stats.Resolve(PlayerStat.SnareDamage).Value, Is.Zero);
        Upgrade(PlayerStat.SnareRoot, 0.5f);
        Upgrade(PlayerStat.SnareDamage, 0.33f);
        Upgrade(PlayerStat.SnareRoot, 0.5f);
        Upgrade(PlayerStat.WeaponDamage, 3f);
        _effects.Apply(new ModifySkillCooldown(SnareId, ModifierKind.Flat, -2f), this);
        EnemyAgent body = Spawn(Vector3.Zero);
        _runner.Cast(0, 0f, auto: false);
        _traps.Tick(0.3f);
        Assert.That(_enemies.Remaining(body.Id, new ContentId(EnemyStatuses.RootedKey)), Is.EqualTo(3f));
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 3f * 20f * 1.33f).Within(Tolerance));
        _runner.Tick(7.99f, 7.99f);
        Assert.That(_runner.IsReady(0), Is.False);
        _runner.Tick(0.01f, 8f);
        Assert.That(_runner.IsReady(0), Is.True);
        var enemyStats = new CombatantStats(body);
        Assert.That(enemyStats.Has(PlayerStat.SnareRoot), Is.False);
        Assert.That(enemyStats.Has(PlayerStat.SnareDamage), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => enemyStats.Resolve(PlayerStat.SnareRoot));
        Assert.Throws<ArgumentOutOfRangeException>(() => enemyStats.Resolve(PlayerStat.SnareDamage));
    }

    [Test]
    public void Cast_SnapshotsNumbers_AndHonoursAuthoredGeometry()
    {
        EnemyAgent body = Spawn(new Vector3(2f, 0f, 0f));
        var effect = new PlaceSnare(2f, 0.6f, 3f, 2f, 3f);
        _handler.Apply(effect, this);
        Upgrade(PlayerStat.SnareDamage, 1f);
        Upgrade(PlayerStat.SnareRoot, 1f);
        _handler.Remove(effect, this);
        _traps.Tick(0.3f);
        Assert.That(_traps.Count, Is.EqualTo(1));
        _traps.Tick(0.6f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 51f));
        Assert.That(_enemies.Remaining(body.Id, new ContentId(EnemyStatuses.RootedKey)), Is.EqualTo(2f));
        _handler.Apply(effect, this);
        _traps.Tick(3f);
        Assert.That(_events.Count<TrapExpired>(), Is.EqualTo(1));
    }

    [Test]
    public void Clear_Silent_AndIdsNotReused()
    {
        int first = _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f);
        _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f);
        _events.Clear();
        _traps.Clear();
        _traps.Tick(20f);
        Assert.That(_traps.Count, Is.Zero);
        Assert.That(_events.All, Is.Empty);
        Assert.That(_traps.Place(Vector3.Zero, 2f, 51f, 10f, 20f), Is.GreaterThan(first + 1));
    }

    [Test]
    public void Boundary_Clears()
    {
        var scaling = new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(8, 1000), new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, scaling, Scalings.Xp(), new[] { new RosterEntry(Body().Id, 1) });
        RunSession session = Session(mode, SpawnPlan.Empty, hp: 17f);
        session.SetAutoCast(SnareId, false);
        // Hold fire until the snare is placed far from the real enemy, then kill it with an arrow.
        var snapshot = new WorldSnapshot(Capacity)
        {
            Dt = 0.05f,
            HasGate = true,
            GatePosition = Vector3.Zero,
            MoveInput = Vector2.UnitX,
            SpawnPoints = new[] { new Vector3(0f, 0f, 7f) },
        };
        for (int i = 0; i < 100 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(snapshot);
        }

        EnemySpawned spawned = _events.Single<EnemySpawned>();
        snapshot.EnemyCount = 1;
        snapshot.Enemies[0] = new EnemySense { Id = spawned.Id, Position = spawned.Position, HasLineOfSight = true };
        session.CastSkillNow(SnareId);
        Assert.That(_events.Count<TrapPlaced>(), Is.EqualTo(1), "RunSession registered the primitive.");
        snapshot.MoveInput = Vector2.Zero;
        for (int i = 0; i < 100 && session.State.StageIndex == 1; i++)
        {
            session.Tick(snapshot);
            if (session.State.IsSanctumOpen)
            { session.LeaveSanctum(); }
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(session.State.Time, Is.LessThan(10f), "The trap had not expired naturally.");
        // Old ids are gone. Run past the old lifetime to expose a missed boundary sweep via expiry.
        snapshot.EnemyCount = 0;
        snapshot.HasGate = false;
        for (int i = 0; i < 240; i++)
        { session.Tick(snapshot); }
        Assert.That(_events.Count<TrapExpired>(), Is.Zero);
        Assert.That(_events.Count<TrapSprung>(), Is.Zero);
        session.End();
    }

    [Test]
    public void Session_AutoCastAtThisFramesFeet_SpringsAfterArming()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var at = new Vector3(5f, 0f, 5f);
        var enemyAt = new Vector3(5.5f, 100f, 5f);
        RunSession session = Session(mode, new SpawnPlan(new[] { new SpawnPlan.Entry(Body().Id, enemyAt) }));
        EnemySpawned body = _events.Single<EnemySpawned>();
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.1f, PlayerPosition = at, MoveInput = Vector2.UnitX };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = body.Id;
        sense.Position = enemyAt;
        session.Tick(snapshot);
        Assert.That(_events.Single<TrapPlaced>().At, Is.EqualTo(at));
        Assert.That(_events.Single<SkillCast>().WasAuto, Is.True);
        snapshot.Dt = 0.29f;
        snapshot.PlayerPosition = Vector3.Zero;
        session.Tick(snapshot);
        Assert.That(_events.Count<TrapSprung>(), Is.Zero);
        snapshot.Dt = 0.02f;
        session.Tick(snapshot);
        Assert.That(_events.Single<TrapSprung>().EnemyId, Is.EqualTo(body.Id));
        Assert.That(_events.Single<EnemyDamaged>().Amount, Is.EqualTo(51f));
        session.End();
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var player = new PlayerCombat(Character(), events, new RecordingIntents(), Capacity);
        PlayerStats stats = Stats(player, events);
        EnemySystem enemies = Enemies(events);
        EnemyAgent body = enemies.Spawn(Body().Id, Vector3.Zero);
        body.Health.MaxHp.Base = 1e8f;
        body.Health.Reset();
        var traps = new TrapSystem(enemies, player, events);
        var clock = new SimulatedClock();
        var handler = new PlaceSnareHandler(traps, clock, stats, player.Blackboard);
        var registry = new EffectRegistry();
        registry.Register<PlaceSnare>(handler);
        var runner = new SkillRunner(registry, player.Blackboard, events);
        runner.Add(Skill());
        var snapshot = new WorldSnapshot(Capacity) { MoveInput = Vector2.UnitX };
        PlaceSnare effect = Snare();
        int springs = 0;
        AllocationAssert.None(() =>
        {
            traps.Clear();
            clock.Now += 20f;
            stats.SnareDamage.Base = stats.SnareDamage.Base == 0f ? 0.33f : 0f;
            player.Tick(0.01f, clock.Now, snapshot, enemies.Registry.Alive, Vector3.UnitZ);
            runner.Tick(20f, clock.Now);
            // Whole-second probes stay distinct throughout this deliberately long run.
            traps.Tick(clock.Now);
            springs += traps.Count;
            traps.Tick(clock.Now + 1f);
            handler.Apply(effect, this);
            handler.Apply(effect, this);
            handler.Apply(effect, this);
            traps.Tick(clock.Now + 10f);
        });
        Assert.That(springs, Is.GreaterThan(10_000));
        Assert.That(body.Health.Current, Is.LessThan(1e8f - 100_000f));
        Assert.That(enemies.Has(body.Id, new ContentId(EnemyStatuses.RootedKey)), Is.True);
    }

    [Test]
    public void Guards()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceSnare(bad, 0.3f, 10f, 2f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceSnare(0.8f, bad, 10f, 2f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceSnare(0.8f, 0.3f, bad, 2f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceSnare(0.8f, 0.3f, 10f, bad, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceSnare(0.8f, 0.3f, 10f, 2f, bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, bad, 51f, 10f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, 2f, bad, 10f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, 2f, 51f, bad, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f, radius: bad, armDelay: 0.3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, 2f, 51f, 10f, 0f, radius: 0.8f, armDelay: bad));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Tick(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(Vector3.Zero, 2f, 51f, 10f, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => _traps.Place(new Vector3(float.NaN), 2f, 51f, 10f, 0f));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(null, this));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(Snare(), null));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(null, this));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(Snare(), null));
        Assert.That(_traps.Count, Is.Zero);
    }

    private void Census() => _player.Tick(0f, 0f, new WorldSnapshot(Capacity), _enemies.Registry.Alive, Vector3.UnitZ);

    private RunSession Session(ModeSpec mode, SpawnPlan plan, float hp = Hp)
    {
        SkillSpec active = Skill();
        SkillSpec b = Passive("skill.filler.b");
        SkillSpec c = Passive("skill.filler.c");
        var tree = new SkillTreeSpec(new ContentId("tree.test"), Character().Id,
            new[] { Branch(active.Id), Branch(b.Id), Branch(c.Id) });
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body(hp: hp) }, new[] { mode },
            new[] { active, b, c }, new[] { tree });
        var random = new FixedRandom(99);
        var session = new RunSession(catalog, random, _events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, Character().Id, 99, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { active.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, Character().Id, 99, 1, plan, saved));
        return session;
    }

    private EnemyAgent Spawn(Vector3 at) => _enemies.Spawn(Body().Id, at);
    private void Upgrade(PlayerStat stat, float amount) => _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, amount), new object());
    private static PlaceSnare Snare() => new(0.8f, 0.3f, 10f, 2f, 3f);
    private static SkillSpec Skill() => new(SnareId, new LocKey("skill.snare.name"), new LocKey("skill.snare.desc"),
        SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(10f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.NearestEnemyDistance, TriggerComparison.Below, 3f) }),
            new IEffect[] { Snare() }));
    private static SkillBranchSpec Branch(ContentId id) => new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });
    private static SkillSpec Passive(string id) => new(new ContentId(id), new LocKey("skill.test.name"),
        new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
    private static PlayerStats Stats(PlayerCombat player, IDomainEvents events) => new(player,
        new PlayerMotor(Character().Movement, Vector3.UnitZ), new LevelTracker(Scalings.Xp(), events));
    private static EnemySystem Enemies(IDomainEvents events) => new(new ContentCatalog(new[] { Character() },
        new[] { Body(), Body(true) }, bosses: new[] { new BossSpec(new ContentId("boss.test"), new ContentId("enemy.boss"),
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f) }),
        events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
    private static CharacterSpec Character() => new(new ContentId("character.ranger"), new LocKey("character.ranger.name"),
        new LocKey("character.ranger.desc"), 100f, new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f, firesWhileMoving: false),
        new FocusSpec(0.4f, 1f, 1f), new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);
    private static EnemySpec Body(bool boss = false, float hp = Hp) => new(new ContentId(boss ? "enemy.boss" : "enemy.husk"),
        new LocKey("enemy.husk.name"), hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f,
        boss ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);
}
