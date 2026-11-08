using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class HawkSystemTests
{
    private const int Capacity = 8;
    private static readonly ContentId Light = new("enemy.hawk_light");
    private static readonly ContentId Heavy = new("enemy.hawk_heavy");
    private static readonly ContentId Fragile = new("enemy.hawk_fragile");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private CharacterSpec _character;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private HawkSystem _hawk;

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
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _hawk = new HawkSystem(_events, _stats);
    }

    [Test]
    public void Call_Aloft_ForSeconds()
    {
        Vector3 player = new(10f, 2f, -5f);
        var handler = new CallHawkHandler(_hawk, new SimulatedClock());
        var effect = new CallHawk(2f, 0.4f);
        Assert.That(handler.CanCast(effect), Is.True);
        handler.Apply(effect, this);

        _hawk.Tick(0f, 0f, player, _enemies, _combat);
        Vector3 first = _hawk.Position;
        _hawk.Tick(0.25f, 0.25f, player, _enemies, _combat);
        Vector3 offset = _hawk.Position - player;
        Assert.That(_hawk.Position, Is.Not.EqualTo(first));
        Assert.That(offset.Y, Is.EqualTo(HawkSystem.Height));
        Assert.That(new Vector2(offset.X, offset.Z).Length(), Is.EqualTo(HawkSystem.CircleRadius).Within(0.0001f));
        Assert.That(_events.Of<HawkArrived>(), Has.Count.EqualTo(1));

        Tick(7.99f);
        Assert.That(_hawk.IsAloft, Is.True);
        Tick(8f);
        Assert.That(_hawk.IsAloft, Is.False);
    }

    [Test]
    public void CallAgain_Refreshes()
    {
        Call();
        _hawk.Call(7f, 2f, 0.4f);
        Tick(8f);
        Assert.That(_hawk.IsAloft, Is.True);
        Assert.That(_events.Of<HawkArrived>(), Has.Count.EqualTo(1));
        Tick(14.99f);
        Assert.That(_hawk.IsAloft, Is.True);
        Tick(15f);
        Assert.That(_events.Of<HawkLeft>(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Dives_NearestMarked_AtInterval()
    {
        Marked(Heavy, 6f);
        EnemyAgent nearest = Marked(Light, 4f);
        Marked(Light, 9f);
        _enemies.Spawn(Heavy, Vector3.UnitX);
        Call();

        Tick(1.49f);
        Assert.That(_events.Of<HawkDove>(), Is.Empty);
        Tick(1.5f);
        HawkDove dive = _events.Single<HawkDove>();
        Assert.That(dive.EnemyId, Is.EqualTo(nearest.Id));
        Assert.That(dive.From, Is.EqualTo(_hawk.Position));
        Assert.That(dive.To, Is.EqualTo(nearest.Position));

        Tick(1.7f);
        Assert.That(_hawk.Position.Y, Is.EqualTo(2f).Within(0.0001f));
        Tick(1.9f);
        Tick(2.99f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(1));
        Tick(3f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(2));
    }

    [Test]
    public void NoMarked_Waits()
    {
        EnemyAgent nearby = _enemies.Spawn(Light, Vector3.UnitX);
        _enemies.Spawn(Heavy, 9f * Vector3.UnitX);
        Call();
        Tick(1.5f);
        Tick(2f);
        Assert.That(_events.Of<HawkDove>(), Is.Empty);

        _enemies.Mark(nearby.Id, 10f);
        Tick(2.01f);
        Assert.That(_events.Single<HawkDove>().EnemyId, Is.EqualTo(nearby.Id));
    }

    [Test]
    public void Strike_DoubleWeapon_StunsLight()
    {
        EnemyAgent light = Marked(Light, 2f);
        EnemyAgent heavy = Marked(Heavy, 4f);
        float before = light.Health.Current;
        Call();
        Tick(1.5f);
        Tick(1.89f);
        Assert.That(light.Health.Current, Is.EqualTo(before));

        _combat.Weapon.Damage.Base = 15f;
        Tick(1.9f);
        Assert.That(light.Health.Current, Is.EqualTo(before - 30f));
        Assert.That(light.CrowdControl.IsStunned, Is.True);
        Assert.That(_events.Single<HawkStruck>().Amount, Is.EqualTo(30f));
        Assert.That(_hawk.Position, Is.EqualTo(light.Position), "BT-03: the climb starts at the strike point.");

        _enemies.Despawn(light.Id);
        Tick(3f);
        Tick(3.4f);
        Assert.That(heavy.CrowdControl.IsStunned, Is.False);
        Assert.That(_events.Of<HawkStruck>(), Has.Count.EqualTo(2));
    }

    [Test]
    public void TargetDied_NoDamage()
    {
        EnemyAgent target = Marked(Light, 2f);
        EnemyAgent backup = Marked(Heavy, -3f); // Far from the hawk, which circles from +X.
        float before = backup.Health.Current;
        Call();
        Tick(1.5f);
        _enemies.ApplyDamage(target.Id, 10_000f, 1.6f, _combat);
        Tick(1.9f);

        Assert.That(_events.Of<HawkStruck>(), Is.Empty);
        Assert.That(backup.Health.Current, Is.EqualTo(before));
        Assert.That(_hawk.IsAloft, Is.True);
    }

    [Test]
    public void Hawk_NotInSnapshot_NotHunted()
    {
        RunSession session = StartSession();
        EnemySystem enemies = ReadState<EnemySystem>(session.State, "Enemies");
        EnemyAgent enemy = enemies.Spawn(Light, 2f * Vector3.UnitX);
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f, PlayerPosition = Vector3.UnitZ };
        snapshot.AddEnemy() = new EnemySense { Id = enemy.Id, Position = enemy.Position };
        Assert.That(ReadState<SkillRunner>(session.State, "Skills").Cast(0, 0f, auto: false), Is.True);
        session.Tick(snapshot);

        Assert.That(_events.Of<HawkArrived>(), Has.Count.EqualTo(1));
        Assert.That(session.State.EnemyCount, Is.EqualTo(1));
        Assert.That(snapshot.EnemyCount, Is.EqualTo(1));
        Assert.That(snapshot.MinionCount, Is.Zero);
        Assert.That(snapshot.PetCount, Is.Zero);
        Assert.That(enemy.Blackboard.QuarryIsAPet, Is.False);
        Assert.That(enemy.Blackboard.QuarryIsADecoy, Is.False);
        Assert.That(enemy.Blackboard.PlayerPosition, Is.EqualTo(snapshot.PlayerPosition));
        session.End();
    }

    [Test]
    public void TimeUp_FinishesDive_Leaves()
    {
        Call();
        Marked(Light, 2f);
        Tick(7.75f);
        Tick(8f);
        Assert.That(_hawk.IsAloft, Is.True);
        Assert.That(_events.Of<HawkStruck>(), Is.Empty);
        Assert.That(_events.Of<HawkLeft>(), Is.Empty);

        Tick(8.2f);
        Assert.That(_events.Of<HawkStruck>(), Has.Count.EqualTo(1));
        Assert.That(_events.Of<HawkLeft>(), Has.Count.EqualTo(1));
        Assert.That(_hawk.IsAloft, Is.False);
    }

    [Test]
    public void Clear_Leaves()
    {
        Marked(Light, 2f);
        Call();
        Tick(1.5f);
        _hawk.Clear();
        _hawk.Clear();
        Tick(2f);
        Assert.That(_hawk.IsAloft, Is.False);
        Assert.That(_events.Of<HawkLeft>(), Has.Count.EqualTo(1));
        Assert.That(_events.Of<HawkStruck>(), Is.Empty);
    }

    [Test]
    public void Trigger_MarkedEnemies()
    {
        RunSession session = StartSession();
        EnemySystem enemies = ReadState<EnemySystem>(session.State, "Enemies");
        CombatBlackboard board = ReadState<PlayerCombat>(session.State, "Combat").Blackboard;
        EnemyAgent close = enemies.Spawn(Light, 8f * Vector3.UnitX);
        EnemyAgent far = enemies.Spawn(Light, 9f * Vector3.UnitX);
        enemies.Mark(far.Id, 10f);
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f };

        session.Tick(snapshot);
        Assert.That(board.MarkedEnemies, Is.Zero);
        Assert.That(_events.Of<SkillCast>(), Is.Empty);

        enemies.Mark(close.Id, 10f);
        session.Tick(snapshot);
        Assert.That(board.MarkedEnemies, Is.EqualTo(1));
        Assert.That(_events.Single<SkillCast>().WasAuto, Is.True);
        Assert.That(_events.Of<HawkArrived>(), Has.Count.EqualTo(1));
        session.End();
    }

    [Test]
    public void Ranks_Numbers()
    {
        _stats.Resolve(PlayerStat.HawkSeconds).Add(new Modifier(ModifierKind.Flat, 2f, this));
        _stats.Resolve(PlayerStat.HawkDiveInterval).Add(new Modifier(ModifierKind.Flat, -0.5f, this));
        EnemyAgent target = Marked(Light, 2f);
        Call();

        Tick(0.99f);
        Assert.That(_events.Of<HawkDove>(), Is.Empty);
        Tick(1f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(1));
        Tick(1.4f);
        Tick(1.99f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(1));
        Tick(2f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(2));
        _enemies.Despawn(target.Id);
        Tick(9.99f);
        Assert.That(_hawk.IsAloft, Is.True);
        Tick(10f);
        Assert.That(_hawk.IsAloft, Is.False);
    }

    [Test]
    public void HawkKill_PaysXp()
    {
        RunSession session = StartSession();
        EnemySystem enemies = ReadState<EnemySystem>(session.State, "Enemies");
        EnemyAgent target = enemies.Spawn(Fragile, 2f * Vector3.UnitX);
        float xp = target.Spec.XpValue;
        enemies.Mark(target.Id, 10f);
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.125f };
        for (int i = 0; i < 18; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(_events.Single<HawkStruck>().EnemyId, Is.EqualTo(target.Id));
        Assert.That(target.IsAlive, Is.False);
        Assert.That(session.State.Xp, Is.EqualTo(xp));
        session.End();
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var events = new SilentEvents();
        var enemies = new EnemySystem(Catalog(), events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        var combat = new PlayerCombat(_character, events, _intents, Capacity);
        var hawk = new HawkSystem(events, _stats);
        EnemyAgent target = enemies.Spawn(Light, Vector3.UnitX);
        enemies.Mark(target.Id, 10f);

        AllocationAssert.None(() =>
        {
            target.Health.Reset();
            hawk.Call(0f, 2f, 0.4f);
            hawk.Tick(0.25f, 0.25f, Vector3.Zero, enemies, combat);
            hawk.Tick(0.25f, 1.5f, Vector3.Zero, enemies, combat);
            hawk.Tick(0.25f, 1.75f, Vector3.Zero, enemies, combat);
            hawk.Tick(0.25f, 2f, Vector3.Zero, enemies, combat);
            hawk.Tick(0.25f, 8f, Vector3.Zero, enemies, combat);
        });
    }

    private void Call() => _hawk.Call(0f, 2f, 0.4f);

    private void Tick(float now) => _hawk.Tick(0.01f, now, Vector3.Zero, _enemies, _combat);

    private EnemyAgent Marked(ContentId id, float x)
    {
        EnemyAgent enemy = _enemies.Spawn(id, x * Vector3.UnitX);
        _enemies.Mark(enemy.Id, 10f);
        return enemy;
    }

    private RunSession StartSession()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var random = new FixedRandom(99);
        var session = new RunSession(Catalog(mode), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        session.Start(new RunConfig(mode.Id, _character.Id, 99, 1, SpawnPlan.Empty, restore: null));
        var trigger = new TriggerSpec(new[] { new TriggerClause(TriggerField.MarkedEnemies, TriggerComparison.AtLeast, 1f) });
        ReadState<SkillRunner>(session.State, "Skills").Add(new SkillSpec(
            new ContentId("skill.hawk"), new LocKey("skill.hawk.name"), new LocKey("skill.hawk.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(5f, trigger, new IEffect[] { new CallHawk(2f, 0.4f) })));
        return session;
    }

    private static T ReadState<T>(RunState state, string property) =>
        (T)typeof(RunState).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);

    private ContentCatalog Catalog(ModeSpec mode = null) =>
        new(new[] { _character }, new[] { Enemy(Light, 1000f, true), Enemy(Heavy, 1000f, false), Enemy(Fragile, 10f, true) },
            mode is null ? null : new[] { mode });

    private static EnemySpec Enemy(ContentId id, float hp, bool light) =>
        new(id, new LocKey("enemy.test.name"), hp, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static, isLight: light);
}
