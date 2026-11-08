using System;
using System.Numerics;
using System.Reflection;
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
public sealed class StampedeSystemTests
{
    private const int Capacity = 8;
    private const float Hp = 1000f;
    private const float Knockdown = 0.8f;
    private static readonly ContentId Light = new("enemy.stampede_light");
    private static readonly ContentId Heavy = new("enemy.stampede_heavy");
    private static readonly ContentId Fragile = new("enemy.stampede_fragile");
    private static readonly ContentId Guard = new("enemy.stampede_guard");
    private static readonly ContentId Boss = new("boss.stampede_test");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private CharacterSpec _character;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private StampedeSystem _stampede;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 10f, 2f, 8f, 360f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.None, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f));
        _combat = new PlayerCombat(_character, _events, _intents, Capacity);
        _stats = new PlayerStats(_combat, new PlayerMotor(_character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _enemies = Enemies(_events);
        _stampede = new StampedeSystem(_events, _stats);
    }

    [Test]
    public void Start_PicksCrowdedLine()
    {
        EnemyPoint[] points = { Point(1, Vector3.UnitZ), Point(2, 3f * Vector3.UnitX),
            Point(3, 5f * Vector3.UnitX), Point(4, 7f * Vector3.UnitX) };
        Assert.That(_stampede.Start(Vector3.Zero, points, 0f, Knockdown), Is.True);
        StampedeStarted started = _events.Single<StampedeStarted>();
        Assert.That(started.Direction, Is.EqualTo(Vector3.UnitX));
        Assert.That(started.From, Is.EqualTo(-2f * Vector3.UnitX));
        Assert.That(started.Length, Is.EqualTo(20f));
        Assert.That(started.HalfWidth, Is.EqualTo(1.5f));
    }

    [Test]
    public void NoLine_Refused_CooldownKept()
    {
        RunSession session = StartSession();
        EnemySystem enemies = ReadState<EnemySystem>(session.State, "Enemies");
        SkillRunner runner = ReadState<SkillRunner>(session.State, "Skills");
        enemies.Spawn(Heavy, 21f * Vector3.UnitX);

        Assert.That(runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(runner.PaidCasts, Is.Zero);
        Assert.That(_events.Count<StampedeStarted>(), Is.Zero);

        enemies.Spawn(Heavy, Vector3.UnitX);
        Assert.That(runner.Cast(0, 0f, auto: false), Is.True);
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(30f));
        session.End();
    }

    [Test]
    public void SixBoars_SpacedAtInterval()
    {
        _enemies.Spawn(Heavy, 5f * Vector3.UnitX);
        Start();
        Assert.That(_events.Single<StampedeStarted>().Boars, Is.EqualTo(6));
        for (int boar = 0; boar < 6; boar++)
        {
            float arrival = 0.5f + boar * 0.15f;
            Tick(arrival - 0.001f);
            Assert.That(_events.Count<BoarHit>(), Is.EqualTo(boar));
            Tick(arrival + 0.001f);
            Assert.That(_events.Count<BoarHit>(), Is.EqualTo(boar + 1));
        }
    }

    [Test]
    public void EachBoar_HitsEachEnemyOnce()
    {
        EnemyAgent first = _enemies.Spawn(Heavy, 3f * Vector3.UnitX);
        EnemyAgent removed = _enemies.Spawn(Heavy, 4f * Vector3.UnitX);
        EnemyAgent second = _enemies.Spawn(Heavy, new Vector3(5f, 100f, 1.9f));
        Start();
        Tick(0.51f);
        Tick(0.51f);
        _enemies.Despawn(removed.Id);
        EnemyAgent later = _enemies.Spawn(Heavy, 7f * Vector3.UnitX);
        Tick(3f);

        Assert.That(first.Health.Current, Is.EqualTo(Hp - 60f));
        Assert.That(second.Health.Current, Is.EqualTo(Hp - 60f));
        Assert.That(later.Health.Current, Is.EqualTo(Hp - 60f));
        Assert.That(_events.Count<BoarHit>(), Is.EqualTo(19));
    }

    [Test]
    public void Boss_HalfDamage()
    {
        EnemyAgent boss = _enemies.SpawnBoss(Boss, 5f * Vector3.UnitX);
        Start();
        Tick(0.5f);

        Assert.That(_events.Single<BoarHit>().Amount, Is.EqualTo(5f));
        Assert.That(boss.Health.Current, Is.EqualTo(Hp - 5f));
    }

    [Test]
    public void Light_KnockedDown()
    {
        EnemyAgent light = _enemies.Spawn(Light, 5f * Vector3.UnitX);
        EnemyAgent heavy = _enemies.Spawn(Heavy, 5f * Vector3.UnitX);
        Start();
        Tick(0.5f);

        Assert.That(light.CrowdControl.Remaining(_enemies.Statuses.Stunned), Is.EqualTo(Knockdown));
        Assert.That(heavy.CrowdControl.IsStunned, Is.False);
    }

    [Test]
    public void Guard_Passed_PhaseShield_Blocks()
    {
        EnemyAgent guard = _enemies.Spawn(Guard, 3f * Vector3.UnitX);
        guard.Blackboard.DirectionToPlayer = -Vector2.UnitX;
        guard.Blackboard.DirectionToRealPlayer = -Vector2.UnitX;
        guard.Blackboard.PathDirectionToPlayer = -Vector2.UnitX;
        guard.Blackboard.DistanceToPlayer = 3f;
        guard.Blackboard.SelfPosition = guard.Position;
        var context = new EnemyTickContext(0f, 0f, _combat, _intents, _events,
            new ProjectileSystem(_events, Capacity), _enemies);
        guard.Behaviour.Tick(context);
        Assert.That(_enemies.ApplyDamage(guard.Id, 10f, 0f, _combat).Blocked, Is.True);

        EnemyAgent boss = _enemies.SpawnBoss(Boss, 4f * Vector3.UnitX);
        _enemies.ApplyDamage(boss.Id, 600f, 0f, _combat);
        boss.Behaviour.Tick(context);
        Assert.That(((BossBehaviour)boss.Behaviour).IsInBeat, Is.True);

        EnemyAgent beyond = _enemies.Spawn(Heavy, 5f * Vector3.UnitX);
        Start();
        Tick(0.5f);
        Assert.That(guard.Health.Current, Is.EqualTo(Hp - 10f));
        Assert.That(boss.Health.Current, Is.EqualTo(400f));
        Assert.That(beyond.Health.Current, Is.EqualTo(Hp - 10f));
        Assert.That(_events.Count<BoarHit>(), Is.EqualTo(3));
    }

    [Test]
    public void Ended_AfterLastBoar()
    {
        Start();
        float end = 5f * 0.15f + 20f / 14f;
        Tick(end - 0.001f);
        Assert.That(_stampede.IsRunning, Is.True);
        Assert.That(_enemies.Registry.AliveCount, Is.Zero);
        Tick(end);
        Assert.That(_stampede.IsRunning, Is.False);
        Tick(end + 1f);
        Assert.That(_events.Count<StampedeEnded>(), Is.EqualTo(1));
    }

    [Test]
    public void Ranks_Numbers()
    {
        var ranks = new ModifyStatHandler(_stats);
        ranks.Apply(new ModifyStat(PlayerStat.StampedeBoars, ModifierKind.Flat, 3f), this);
        ranks.Apply(new ModifyStat(PlayerStat.StampedeDamage, ModifierKind.Flat, 0.3f), this);
        ranks.Apply(new ModifyStat(PlayerStat.StampedeHalfWidth, ModifierKind.Flat, 0.5f), this);
        EnemyAgent edge = _enemies.Spawn(Heavy, new Vector3(5f, 0f, 2.5f));
        EnemyAgent outside = _enemies.Spawn(Heavy, new Vector3(5f, 0f, 2.501f));
        Start();

        StampedeStarted started = _events.Single<StampedeStarted>();
        Assert.That(started.Boars, Is.EqualTo(9));
        Assert.That(started.HalfWidth, Is.EqualTo(2f));
        Tick(3f);
        Assert.That(edge.Health.Current, Is.EqualTo(Hp - 9f * 13f));
        Assert.That(outside.Health.Current, Is.EqualTo(Hp));
        Assert.That(_events.Count<BoarHit>(), Is.EqualTo(9));
    }

    [Test]
    public void BoarKill_PaysXp()
    {
        RunSession session = StartSession();
        EnemySystem enemies = ReadState<EnemySystem>(session.State, "Enemies");
        SkillRunner runner = ReadState<SkillRunner>(session.State, "Skills");
        EnemyAgent target = enemies.Spawn(Fragile, Vector3.UnitX);
        float xp = target.Spec.XpValue;
        Assert.That(runner.Cast(0, 0f, auto: false), Is.True);
        session.Tick(new WorldSnapshot(Capacity) { Dt = 0.25f });

        Assert.That(_events.Single<BoarHit>().EnemyId, Is.EqualTo(target.Id));
        Assert.That(target.IsAlive, Is.False);
        Assert.That(session.State.Xp, Is.EqualTo(xp));
        session.End();
        Assert.That(_events.Count<StampedeEnded>(), Is.Zero);
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        EnemySystem enemies = Enemies(events);
        var combat = new PlayerCombat(_character, events, _intents, Capacity);
        var stampede = new StampedeSystem(events, _stats);
        EnemyAgent target = enemies.Spawn(Heavy, 5f * Vector3.UnitX);
        EnemyPoint[] points = { Point(target.Id, target.Position) };

        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            stampede.Start(Vector3.Zero, points, 0f, Knockdown);
            stampede.Tick(0.49f, enemies, combat);
            stampede.Tick(0.5f, enemies, combat);
            stampede.Tick(3f, enemies, combat);
        });
    }

    private void Start()
    {
        EnemyPoint[] points = { Point(1, 5f * Vector3.UnitX) };
        Assert.That(_stampede.Start(Vector3.Zero, points, 0f, Knockdown), Is.True);
    }

    private void Tick(float now) => _stampede.Tick(now, _enemies, _combat);

    private RunSession StartSession()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var random = new FixedRandom(99);
        var session = new RunSession(Catalog(mode), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(mode.Id, _character.Id, 99, 1, SpawnPlan.Empty, restore: null));
        var trigger = new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) });
        ReadState<SkillRunner>(session.State, "Skills").Add(new SkillSpec(
            new ContentId("skill.stampede"), new LocKey("skill.stampede.name"), new LocKey("skill.stampede.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f, trigger, new IEffect[] { new Stampede(Knockdown) })));
        return session;
    }

    private static T ReadState<T>(RunState state, string property) =>
        (T)typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);

    private EnemySystem Enemies(IDomainEvents events) =>
        new(Catalog(), events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);

    private ContentCatalog Catalog(ModeSpec mode = null) =>
        new(new[] { _character }, new[]
        {
            Enemy(Light, Hp, EnemyBehaviourKind.Static, light: true),
            Enemy(Heavy, Hp, EnemyBehaviourKind.Static),
            Enemy(Fragile, 10f, EnemyBehaviourKind.Static),
            Enemy(Guard, Hp, EnemyBehaviourKind.Bulwark, bulwark: new BulwarkSpec(60f, 75f, 45f)),
            Enemy(new ContentId("enemy.stampede_boss"), Hp, EnemyBehaviourKind.Boss),
        }, mode is null ? null : new[] { mode }, bosses: new[]
        {
            new BossSpec(Boss, new ContentId("enemy.stampede_boss"),
                new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f),
        });

    private static EnemySpec Enemy(ContentId id, float hp, EnemyBehaviourKind kind, bool light = false,
        BulwarkSpec bulwark = null) =>
        new(id, new LocKey("enemy.test.name"), hp, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.7f, 1.5f, 30f, kind, bodyRadius: 0.5f, bulwark: bulwark, isLight: light);

    private static EnemyPoint Point(int id, Vector3 position) => new(id, position, 0.5f, false);
}
