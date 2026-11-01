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

[TestFixture]
public sealed class ShamanBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private EnemyAgent _agent;
    private float _now;
    private ShamanBehaviour Shaman => (ShamanBehaviour)_agent.Behaviour;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        _player = new PlayerCombat(Character(), _events, _intents, 16);
        _projectiles = new ProjectileSystem(_events, 16);
        _agent = _system.Spawn(new ContentId("enemy.shaman"), new Vector3(0, 0, 12));
        _now = 0;
        Bearing(12);
    }

    [Test]
    public void Anchor_PrefersBulwark()
    {
        Ally(9);
        EnemyAgent guard = _system.Spawn(new ContentId("enemy.guard"), new Vector3(0, 0, 3));
        Tick(0);
        Assert.That(Shaman.AnchorId, Is.EqualTo(guard.Id));
        _system.Despawn(guard.Id);
        Tick(0);
        Assert.That(Shaman.AnchorId, Is.Not.EqualTo(guard.Id));
    }

    [Test]
    public void Anchor_NonePast12()
    { Ally(25); Tick(0); Assert.That(Shaman.AnchorId, Is.EqualTo(-1)); }

    [TestCase(12f, 14.5f, TestName = "Moves_BehindAnchor")]
    [TestCase(6f, 11f, TestName = "Moves_NeverInsideHold")]
    public void Moves_ToRangeEdge(float anchor, float goal)
    {
        Ally(anchor);
        Tick(0.1f);
        float direction = MathF.Sign(goal - 12f);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(direction * 2f).Within(0.001f));
    }

    [Test]
    public void Moves_AloneAt12()
    { Tick(0); Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero)); }

    [Test]
    public void Cast_WaitsForAlly()
    { Ally(21); Tick(5); Assert.That(_events.Count<ShamanCast>(), Is.Zero); }

    [Test]
    public void Cast_StandsThenPulses()
    {
        Ally(7);
        Tick(2);
        Assert.That(Shaman.State, Is.EqualTo(ShamanState.Cast));
        Assert.That(_events.Single<ShamanCast>().ShamanId, Is.EqualTo(_agent.Id));
        Tick(0.59f);
        Assert.That(_events.Count<ShamanPulsed>(), Is.Zero);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);
        Assert.That(_events.Single<ShamanPulsed>().Touched, Is.EqualTo(1));
    }

    [Test]
    public void Pulse_HealsAndHastes()
    {
        EnemyAgent ally = Ally(7);
        _system.ApplyDamage(ally.Id, 50f, 0, _player);
        Tick(2);
        Tick(0.61f);
        Assert.That(ally.Health.Fraction, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(_events.Single<EnemyHealed>().Amount, Is.EqualTo(10f));
        Assert.That(_system.Has(ally.Id, EnemyStatus.Hasted), Is.True);
        Assert.That(ally.MoveSpeed.Value, Is.EqualTo(2.5f));
        Assert.That(_system.Remaining(ally.Id, EnemyStatus.Hasted), Is.EqualTo(3.5f));
    }

    [Test]
    public void Pulse_SkipsNonAllies()
    {
        _system.Spawn(new ContentId("enemy.shaman"), new Vector3(0, 0, 11));
        _system.Registry.Spawn(Spec("boss", EnemyBehaviourKind.Boss), new Vector3(0, 0, 10));
        Ally(7);
        Tick(2);
        Tick(0.61f);
        Assert.That(_events.Single<ShamanPulsed>().Touched, Is.EqualTo(1));
    }

    [Test]
    public void Flee_CancelsCast()
    {
        Ally(7);
        Tick(2);
        Bearing(3);
        Tick(0.1f);
        Assert.That(Shaman.State, Is.EqualTo(ShamanState.Flee));
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(2.8f).Within(0.001f));
        Tick(1.1f);
        Assert.That(_events.Count<ShamanPulsed>(), Is.Zero);
    }

    [Test]
    public void NeverDamagesPlayer()
    { Bearing(0); for (int i = 0; i < 100; i++) { Tick(0.1f); } Assert.That(_player.Health.Fraction, Is.EqualTo(1)); }

    [Test]
    public void Haste_StrongestWinsEnds()
    {
        EnemyAgent ally = Ally(7);
        _system.Haste(ally.Id, 0.25f, 1);
        _system.Haste(ally.Id, 0.1f, 2);
        Assert.That(ally.MoveSpeed.Value, Is.EqualTo(2.5f));
        _system.Slow(ally.Id, 0.5f, 3);
        Assert.That(ally.MoveSpeed.Value, Is.EqualTo(1.25f));
        _events.Clear();
        _system.Haste(ally.Id, 0.25f, 0.5f);
        Assert.That(_events.Count<EnemyStatusChanged>(), Is.Zero, "Nothing grew, nothing announced.");
        _system.Tick(new EnemyTickContext(1, 1, _player, _intents, _events, _projectiles, _system));
        Assert.That(ally.MoveSpeed.Value, Is.EqualTo(1f), "The weaker, longer haste did not stretch it.");
        Assert.That(_system.Has(ally.Id, EnemyStatus.Hasted), Is.False);
        Assert.That(_events.Count<EnemyStatusChanged>(), Is.EqualTo(1));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), new DepthScaling(Scalings.Design()), 16);
        for (int i = 0; i < 16; i++)
        { system.Spawn(new ContentId(i == 0 ? "enemy.shaman" : "enemy.ally"), new Vector3(0, 0, 12)); }
        EnemyAgent shaman = system.Registry.Alive[0];
        shaman.Blackboard.DistanceToPlayer = 12;
        shaman.Blackboard.DirectionToPlayer = -Vector2.UnitY;
        float now = 0;
        Action tick = () => { _intents.Clear(); now += 0.1f; shaman.Behaviour.Tick(new EnemyTickContext(0.1f, now, _player, _intents, silent, _projectiles, system)); };
        for (int i = 0; i < 100; i++)
        { tick(); }
        AllocationAssert.None(tick);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void Spec_RejectsInvalidNumbers(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShamanSpec(value, 3, 0.6f, 0.1f, 0.25f, 3.5f, 11, 2.5f, 4, 1.2f, 1.4f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _system.Haste(_agent.Id, value, 1));
    }

    private EnemyAgent Ally(float z) => _system.Spawn(new ContentId("enemy.ally"), new Vector3(0, 0, z));
    private void Bearing(float distance)
    { _agent.Blackboard.DistanceToPlayer = distance; _agent.Blackboard.DirectionToPlayer = -Vector2.UnitY; _agent.Blackboard.PlayerPosition = new Vector3(0, 0, 12 - distance); }
    private void Tick(float dt)
    { _now += dt; _intents.Clear(); _agent.Behaviour.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _system)); }
    private static ContentCatalog Catalog() => new ContentCatalog(new[] { Character() }, new[] { Spec("shaman", EnemyBehaviourKind.Shaman), Spec("ally", EnemyBehaviourKind.Static), Spec("guard", EnemyBehaviourKind.Bulwark) });
    private static EnemySpec Spec(string name, EnemyBehaviourKind kind) => new EnemySpec(new ContentId("enemy." + name), new LocKey("enemy." + name + ".name"),
        100f, 2f, 3, 16, 32, false, 0f, 1f, 0.7f, 1f, 30f, kind,
        bulwark: kind == EnemyBehaviourKind.Bulwark ? new BulwarkSpec(60, 75, 45) : null,
        shaman: kind == EnemyBehaviourKind.Shaman ? new ShamanSpec(8, 3, 0.6f, 0.1f, 0.25f, 3.5f, 11, 2.5f, 4, 1.2f, 1.4f) : null);
    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.oathbound"), new LocKey("character.oathbound.name"), new LocKey("character.oathbound.description"), 140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
