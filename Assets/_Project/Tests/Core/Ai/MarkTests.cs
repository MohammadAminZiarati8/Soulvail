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

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class MarkTests
{
    private const int Capacity = 8;
    private const float MarkSeconds = 4f;
    private static readonly ContentId BodyId = new("enemy.mark_target");
    private static readonly ContentId BossBodyId = new("enemy.mark_boss");
    private static readonly ContentId BossId = new("boss.mark_test");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        CharacterSpec character = Character();
        _enemies = Enemies(character, _events);
        _player = new PlayerCombat(character, _events, _intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _projectiles = new ProjectileSystem(_events, Capacity);
        _lanes = new LaneSystem(_events);
        _now = 0f;
    }

    [Test]
    public void Mark_Applies_IsMarked()
    {
        EnemyAgent enemy = Spawn();
        StatusRule rule = _enemies.Statuses.Marked;
        Assert.That(rule.Id.Value, Is.EqualTo("status.enemy.marked"));
        Assert.That(rule.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(rule.Priority, Is.EqualTo(5));

        Assert.That(_enemies.Mark(enemy.Id, MarkSeconds), Is.True);
        Assert.That(_enemies.IsMarked(enemy.Id), Is.True);
        Assert.That(_enemies.Has(enemy.Id, rule.Id), Is.True);
        Assert.That(_enemies.Remaining(enemy.Id, rule.Id), Is.EqualTo(MarkSeconds));

        EnemyStatusChanged change = _events.Single<EnemyStatusChanged>();
        Assert.That(change.EnemyId, Is.EqualTo(enemy.Id));
        Assert.That(change.Status, Is.EqualTo(rule.Id));
        Assert.That(change.Seconds, Is.EqualTo(MarkSeconds));
        Assert.That(change.Stacks, Is.EqualTo(1));
    }

    [Test]
    public void Mark_Again_RefreshesNotStacks()
    {
        EnemyAgent enemy = Spawn();
        _enemies.Mark(enemy.Id, MarkSeconds);
        Tick(1f);
        _events.Clear();

        Assert.That(_enemies.Mark(enemy.Id, 2f), Is.True);
        Assert.That(_enemies.Remaining(enemy.Id, _enemies.Statuses.Marked.Id), Is.EqualTo(3f));
        Assert.That(_events.Count<EnemyStatusChanged>(), Is.Zero);

        Assert.That(_enemies.Mark(enemy.Id, MarkSeconds), Is.True);
        Assert.That(_enemies.Remaining(enemy.Id, _enemies.Statuses.Marked.Id), Is.EqualTo(MarkSeconds));
        Assert.That(_events.Single<EnemyStatusChanged>().Stacks, Is.EqualTo(1));
    }

    [Test]
    public void Mark_Expires()
    {
        EnemyAgent enemy = Spawn();
        _enemies.Mark(enemy.Id, MarkSeconds);
        _events.Clear();

        Tick(3.5f);
        Assert.That(_enemies.IsMarked(enemy.Id), Is.True);
        Tick(0.5f);
        Tick(0.5f);

        Assert.That(_enemies.IsMarked(enemy.Id), Is.False);
        EnemyStatusChanged change = _events.Single<EnemyStatusChanged>();
        Assert.That(change.Status, Is.EqualTo(_enemies.Statuses.Marked.Id));
        Assert.That(change.Seconds, Is.Zero);
    }

    [Test]
    public void Mark_Boss_FullLength()
    {
        EnemyAgent boss = _enemies.SpawnBoss(BossId, Vector3.Zero);
        Assert.That(_enemies.Mark(boss.Id, MarkSeconds), Is.True);
        Assert.That(_enemies.Remaining(boss.Id, _enemies.Statuses.Marked.Id), Is.EqualTo(MarkSeconds));
    }

    [Test]
    public void Mark_RefusesInvalidTargetsAndDurations()
    {
        EnemyAgent enemy = Spawn();
        Assert.That(_enemies.Mark(enemy.Id, 0f), Is.False);
        Assert.That(_enemies.Mark(enemy.Id, -1f), Is.False);
        Assert.That(_enemies.Mark(enemy.Id, float.NaN), Is.False);
        Assert.That(_enemies.Mark(enemy.Id, float.PositiveInfinity), Is.False);
        Assert.That(_enemies.IsMarked(enemy.Id), Is.False);
        Assert.That(_enemies.Mark(-1, MarkSeconds), Is.False);
        Assert.That(_enemies.IsMarked(-1), Is.False);

        _enemies.Mark(enemy.Id, MarkSeconds);
        _enemies.ApplyDamage(enemy.Id, 1000f, _now, _player);
        Assert.That(_enemies.Mark(enemy.Id, MarkSeconds), Is.False);
        Assert.That(_enemies.IsMarked(enemy.Id), Is.False);
    }

    [Test]
    public void AxeHit_Marks_BothPasses()
    {
        EnemyAgent enemy = Spawn();
        AxeSystem axes = ThrowAxe(MarkSeconds);

        axes.Tick(1f, 1f, Vector3.Zero, _player, _enemies, null);
        Assert.That(_events.Single<AxeHit>().Pass, Is.EqualTo(AxePass.Out));
        Assert.That(_enemies.Remaining(enemy.Id, _enemies.Statuses.Marked.Id), Is.EqualTo(MarkSeconds));
        Tick(1f);
        _events.Clear();

        axes.Tick(1f, 2f, Vector3.Zero, _player, _enemies, null);
        Assert.That(_events.Single<AxeHit>().Pass, Is.EqualTo(AxePass.Back));
        Assert.That(_enemies.Remaining(enemy.Id, _enemies.Statuses.Marked.Id), Is.EqualTo(MarkSeconds));
    }

    [Test]
    public void MarkSecondsZero_NoMark()
    {
        EnemyAgent enemy = Spawn();
        AxeSystem axes = ThrowAxe(0f);
        axes.Tick(1f, 1f, Vector3.Zero, _player, _enemies, null);
        axes.Tick(1f, 2f, Vector3.Zero, _player, _enemies, null);

        Assert.That(_events.Count<AxeHit>(), Is.EqualTo(2));
        Assert.That(_events.Count<EnemyStatusChanged>(), Is.Zero);
        Assert.That(_enemies.IsMarked(enemy.Id), Is.False);
    }

    [Test]
    public void WeaponSpec_MarkSeconds_NonFiniteRefused(
        [Values(WeaponKind.Cone, WeaponKind.Projectile, WeaponKind.ReturningAxe)] WeaponKind kind,
        [Values(float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f)] float seconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Weapon(kind, seconds));
        Assert.That(exception.ParamName, Is.EqualTo("markSeconds"));
    }

    [Test]
    public void Mark_DoesNotAllocate()
    {
        EnemySystem enemies = Enemies(Character(), new SilentEvents());
        EnemyAgent enemy = enemies.Spawn(BodyId, Vector3.Zero);
        StatusRule rule = enemies.Statuses.Marked;
        float seconds = MarkSeconds;
        AllocationAssert.None(() =>
        {
            enemies.Mark(enemy.Id, seconds++);
            enemies.IsMarked(enemy.Id);
            enemies.Has(enemy.Id, rule.Id);
        });
    }

    private EnemyAgent Spawn() => _enemies.Spawn(BodyId, 4f * Vector3.UnitX);

    private void Tick(float dt)
    {
        _now += dt;
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes));
    }

    private AxeSystem ThrowAxe(float seconds)
    {
        var axes = new AxeSystem(_events, _stats, Weapon(WeaponKind.ReturningAxe, seconds));
        Assert.That(axes.TryThrow(Vector3.Zero, Vector3.UnitX, 10f, 0f), Is.True);
        return axes;
    }

    private static EnemySystem Enemies(CharacterSpec character, IDomainEvents events)
    {
        var boss = new BossSpec(BossId, BossBodyId, new[] { new BossPhaseSpec(1f) }, 1.5f);
        var catalog = new ContentCatalog(new[] { character },
            new[] { Body(BodyId, EnemyBehaviourKind.Static), Body(BossBodyId, EnemyBehaviourKind.Boss) },
            bosses: new[] { boss });
        return new EnemySystem(catalog, events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
    }

    private static EnemySpec Body(ContentId id, EnemyBehaviourKind behaviour) => new(
        id, new LocKey($"{id.Value}.name"), 100f, 3.5f, 1, threatCost: 4, xpValue: 12f,
        isElite: false, 8f, 1.2f, 0.4f, 0.6f, aggroRange: 30f, behaviour);

    private static CharacterSpec Character() => new(
        new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
        new LocKey("character.beastmaster.description"), 100f,
        new MovementSpec(5f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        Weapon(WeaponKind.ReturningAxe, MarkSeconds), new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f),
        null, 0.5f);

    private static WeaponSpec Weapon(WeaponKind kind, float seconds) => kind switch
    {
        WeaponKind.Cone => new WeaponSpec(kind, 10f, 2f, 8f, 60f, 0.4f, markSeconds: seconds),
        WeaponKind.Projectile => new WeaponSpec(kind, 10f, 2f, 8f, 360f, 0.4f,
            shotSpeed: 8f, shotRadius: 0.3f, markSeconds: seconds),
        WeaponKind.ReturningAxe => new WeaponSpec(kind, 10f, 2f, 8f, 360f, 0.4f,
            shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3, markSeconds: seconds),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
