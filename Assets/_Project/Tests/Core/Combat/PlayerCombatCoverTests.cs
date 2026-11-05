using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class PlayerCombatCoverTests
{
    private const int Capacity = 8;
    private const float Frame = 1f / 60f;
    private const float RetargetPeriod = 0.1f;

    private readonly ContentId _enemyId = new("enemy.husk");
    private RecordingEvents _events;
    private EnemySystem _enemies;
    private PlayerCombat _combat;
    private WorldSnapshot _snapshot;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        var ranger = new CharacterSpec(
            new ContentId("character.ranger"), new LocKey("character.ranger.name"),
            new LocKey("character.ranger.desc"), 100f, new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, RetargetPeriod),
            new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f),
            null, 0.5f);
        var enemy = new EnemySpec(
            _enemyId, new LocKey("enemy.husk.name"), 36f, 2f, 1, 4, 12f, false,
            8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
        var catalog = new ContentCatalog(new[] { ranger }, new[] { enemy });

        _events = new RecordingEvents();
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _combat = new PlayerCombat(ranger, _events, new RecordingIntents(), Capacity);
        _snapshot = new WorldSnapshot(Capacity);
        _now = 0f;
    }

    [Test]
    public void Covered_IsNeverTarget()
    {
        EnemyAgent enemy = SpawnWithSense(3f, behindCover: true);

        Tick();

        Assert.That(_combat.Targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(enemy.IsVulnerable, Is.True);
    }

    [Test]
    public void NearestCovered_VisibleFartherIsTarget()
    {
        SpawnWithSense(3f, behindCover: true);
        EnemyAgent visible = SpawnWithSense(5f, behindCover: false);

        Tick();

        Assert.That(_combat.Targeter.CurrentTargetId, Is.EqualTo(visible.Id));
    }

    [Test]
    public void AllCovered_NoBasicAttack()
    {
        SpawnWithSense(3f, behindCover: true);
        SpawnWithSense(5f, behindCover: true);

        // One second is longer than the Ranger's basic attack interval.
        for (int frame = 0; frame < 60; frame++)
        {
            Tick();
        }

        Assert.That(_combat.Targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);
        Assert.That(_combat.PendingShot, Is.Null);
    }

    [Test]
    public void LeavesCover_TargetedNextDecision()
    {
        EnemyAgent enemy = SpawnWithSense(3f, behindCover: true);
        Tick();

        _snapshot.Enemies[0].BehindCover = false;
        Tick(RetargetPeriod + Frame);

        Assert.That(_combat.Targeter.CurrentTargetId, Is.EqualTo(enemy.Id));
    }

    [Test]
    public void Spawned_NoSense_Targetable()
    {
        EnemyAgent enemy = _enemies.Spawn(_enemyId, Vector3.UnitZ * 3f);

        Tick();

        Assert.That(_combat.Targeter.CurrentTargetId, Is.EqualTo(enemy.Id));
    }

    [Test]
    public void Candidates_DoNotAllocate()
    {
        SpawnWithSense(3f, behindCover: true);
        SpawnWithSense(5f, behindCover: true);
        Tick();

        AllocationAssert.None(() => Tick());
    }

    private EnemyAgent SpawnWithSense(float distance, bool behindCover)
    {
        Vector3 position = Vector3.UnitZ * distance;
        EnemyAgent enemy = _enemies.Spawn(_enemyId, position);
        _snapshot.AddEnemy() = new EnemySense
        {
            Id = enemy.Id,
            Position = position,
            BehindCover = behindCover,
        };

        return enemy;
    }

    private void Tick(float dt = Frame)
    {
        _now += dt;
        _snapshot.Dt = dt;
        _enemies.Ingest(_snapshot);
        _combat.Tick(dt, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
    }
}
