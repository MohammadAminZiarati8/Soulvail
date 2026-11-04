using System;
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

/// <summary>ST-01: enemy statuses are ids with a side, a priority and a boss scale.</summary>
[TestFixture]
public sealed class EnemyStatusesTests
{
    private const string HuskId = "enemy.husk";
    private const string BodyId = "enemy.boss_body";
    private const string BossId = "boss.test";
    private const string OathboundId = "character.oathbound";
    private const string UnknownKey = "status.enemy.nope";

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private EnemySystem _system;
    private EnemyStatuses _statuses;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        _statuses = _system.Statuses;
    }

    [Test]
    public void Sides_DebuffsAndHaste()
    {
        Assert.That(_statuses.Stunned.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(_statuses.Rooted.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(_statuses.Slowed.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(_statuses.Hasted.Side, Is.EqualTo(StatusSide.Buff));
    }

    [Test]
    public void Priorities_StunRootSlowHaste()
    {
        Assert.That(_statuses.Stunned.Priority, Is.EqualTo(40));
        Assert.That(_statuses.Rooted.Priority, Is.EqualTo(30));
        Assert.That(_statuses.Slowed.Priority, Is.EqualTo(20));
        Assert.That(_statuses.Hasted.Priority, Is.EqualTo(10));
    }

    [Test]
    public void BossScale_ZeroForCrowdControl_FullForHaste()
    {
        EnemyAgent boss = _system.SpawnBoss(new ContentId(BossId), Vector3.Zero);

        _system.Stun(boss.Id, 2f);
        _system.Haste(boss.Id, 0.25f, 2f);

        Assert.That(_statuses.Stunned.BossScale, Is.Zero);
        Assert.That(_statuses.Rooted.BossScale, Is.Zero);
        Assert.That(_statuses.Slowed.BossScale, Is.Zero);
        Assert.That(_system.Remaining(boss.Id, _statuses.Stunned.Id), Is.Zero);
        Assert.That(_system.Remaining(boss.Id, _statuses.Hasted.Id), Is.EqualTo(2f).Within(Tolerance));
    }

    [Test]
    public void Changed_CarriesRuleAndOneStack()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        _system.Slow(husk.Id, 0.5f, 2f);

        EnemyStatusChanged changed = _events.Of<EnemyStatusChanged>()[0];

        Assert.That(changed.Rule.Side, Is.EqualTo(StatusSide.Debuff));
        Assert.That(changed.Stacks, Is.EqualTo(1));
        Assert.That(changed.Status, Is.EqualTo(_statuses.Slowed.Id));
    }

    [Test]
    public void TryGet_KnownAndUnknown()
    {
        Assert.That(_statuses.TryGet(new ContentId(EnemyStatuses.HastedKey), out StatusRule rule), Is.True);
        Assert.That(rule.Id, Is.EqualTo(_statuses.Hasted.Id));
        Assert.That(_statuses.TryGet(new ContentId(UnknownKey), out _), Is.False);
    }

    [Test]
    public void Has_UnknownStatusId_Throws()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        var unknown = new ContentId(UnknownKey);

        Assert.Throws<ArgumentOutOfRangeException>(() => _system.Has(husk.Id, unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => _system.Remaining(husk.Id, unknown));
        Assert.That(_system.Has(-1, _statuses.Stunned.Id), Is.False, "An unknown enemy is still just false.");
    }

    [Test]
    public void StatusRule_Default_Refused()
    {
        Assert.Throws<ArgumentException>(() => new EnemyStatusChanged(1, default, 1f, 1));
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), Body() },
        bosses: new[] { Boss() });

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    private static EnemySpec Body() => new EnemySpec(
        new ContentId(BodyId),
        new LocKey("enemy.boss_body.name"),
        maxHp: 900f,
        moveSpeed: 1f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: 120f,
        isElite: false,
        contactDamage: 20f,
        reach: 2f,
        windupTime: 1f,
        recoverTime: 1f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Boss);

    private static BossSpec Boss() => new BossSpec(
        new ContentId(BossId),
        new ContentId(BodyId),
        new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) },
        1.5f);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        null,
        0.5f);
}
