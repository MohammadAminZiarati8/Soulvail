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
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class AxeSystemTests
{
    private const int EnemyCapacity = 8;
    private const float Damage = 10f;
    private const float Range = 8f;
    private const float Speed = 8f;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private static readonly ContentId ModeId = new("mode.descent");

    private RecordingEvents _events;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private AxeSystem _axes;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        (_player, _stats, _enemies, _axes) = Build(_events);
        _now = 0f;
    }

    [Test]
    public void Throw_TurnPointAtFullRange_InHandDrops()
    {
        Vector3 hand = new(1f, 1f, 2f);
        Assert.That(_axes.TryThrow(hand, hand + Vector3.UnitX, Damage, 0f), Is.True);

        Vector3 turn = hand + (Range * Vector3.UnitX);
        AxeThrown thrown = _events.Single<AxeThrown>();
        Assert.That(thrown.From, Is.EqualTo(hand));
        Assert.That(thrown.TurnPoint, Is.EqualTo(turn));
        Assert.That(_axes.InFlight[0].TurnPoint, Is.EqualTo(turn));
        Assert.That(_axes.InFlight[0].Pass, Is.EqualTo(AxePass.Out));
        Assert.That(_axes.InHand, Is.Zero);
    }

    [Test]
    public void Throw_NoAxe_RefusedSilently()
    {
        Throw();
        Assert.That(_axes.TryThrow(Vector3.Zero, Vector3.UnitX, Damage, 0f), Is.False);
        Assert.That(_axes.InFlight.Length, Is.EqualTo(1));
        Assert.That(_events.Count<AxeThrown>(), Is.EqualTo(1));
    }

    [Test]
    public void Combat_NoAxeInHand_NoSwing()
    {
        const int seed = 99;
        var random = new FixedRandom(seed);
        var session = new RunSession(Catalog(), random, _events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), _events), EnemyCapacity, EnemyCapacity, 8);
        session.Start(new RunConfig(ModeId, CharacterId, seed, 1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(EnemyId, new Vector3(3f, 0f, 0f)) }),
            restore: null));

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = 0.05f };
        snapshot.AddEnemy() = new EnemySense
        {
            Id = _events.Single<EnemySpawned>().Id,
            Position = new Vector3(3f, 0f, 0f),
        };

        for (int i = 0; i < 30; i++)
        {
            session.Tick(snapshot);
        }

        // More than one 0.5 s swing interval passed, and the axe is still on its 2 s trip.
        Assert.That(_events.Count<AxeCaught>(), Is.Zero);
        Assert.That(_events.Count<AxeThrown>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(1));
        Assert.That(_events.Count<AxeHit>(), Is.EqualTo(1));
    }

    [Test]
    public void OutPass_HitsEachEnemyOnce_CapThree_NearestFirst()
    {
        EnemyAgent fourth = Spawn(7f);
        EnemyAgent third = Spawn(5f);
        EnemyAgent second = Spawn(3f);
        EnemyAgent first = Spawn(2f, z: 0.2f);
        Spawn(2f, z: 4f);
        Throw();

        Tick(0.25f);
        Tick(0.125f);
        Tick(0.625f);

        Assert.That(_events.Of<AxeHit>().Select(hit => hit.EnemyId),
            Is.EqualTo(new[] { first.Id, second.Id, third.Id }));
        Assert.That(fourth.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void Turn_ClearsHitSet_ReturnHitsAgain()
    {
        EnemyAgent enemy = Spawn(4f);
        Throw();
        Tick(1f);

        Assert.That(_axes.InFlight[0].Pass, Is.EqualTo(AxePass.Back));
        Assert.That(_events.Single<AxeTurned>().Position, Is.EqualTo(Range * Vector3.UnitX));
        Tick(1f);
        Assert.That(_events.Of<AxeHit>().Select(hit => hit.Pass), Is.EqualTo(new[] { AxePass.Out, AxePass.Back }));
        Assert.That(enemy.Health.Current, Is.EqualTo(100f - (2f * Damage)));
    }

    [Test]
    public void Back_HomesOnMovedHand_Caught()
    {
        Throw();
        Tick(1f);
        Vector3 hand = new(Range, 0f, 2f);
        Tick(0.125f, hand);
        Assert.That(_axes.InFlight[0].Position, Is.EqualTo(new Vector3(Range, 0f, 1f)));
        Tick(0.1f, hand);
        Assert.That(_events.Single<AxeCaught>().Id, Is.EqualTo(_events.Single<AxeThrown>().Id));
        Assert.That(_axes.InFlight.Length, Is.Zero);
        Assert.That(_axes.InHand, Is.EqualTo(1));
    }

    [Test]
    public void Back_DamageTimesReturnStat()
    {
        EnemyAgent enemy = Spawn(4f);
        Throw();
        Tick(1f);
        _stats.AxeReturnDamage.Base = 2.5f;
        Tick(1f);

        Assert.That(_events.Of<AxeHit>()[1].Amount, Is.EqualTo(25f));
        Assert.That(enemy.Health.Current, Is.EqualTo(65f));
    }

    [Test]
    public void AxeKill_PaysXp()
    {
        EnemyAgent enemy = Spawn(3f, hp: Damage);
        Throw();
        Tick(1f);

        Assert.That(enemy.IsAlive, Is.False);
        Assert.That(_enemies.DrainXp(), Is.EqualTo(12f));
    }

    [Test]
    public void Carry_RaiseAndCut()
    {
        Throw();
        _stats.AxeCarry.Base = 99f;
        Assert.That(_axes.InHand, Is.EqualTo(AxeSystem.MaxCarry - 1));
        Throw();
        _stats.AxeCarry.Base = -5f;
        Assert.That(_axes.InHand, Is.Zero);
        Tick(1f);
        Tick(1f);
        Assert.That(_events.Count<AxeCaught>(), Is.EqualTo(2));
        Assert.That(_axes.InHand, Is.EqualTo(1));
    }

    [Test]
    public void Clear_RefillsHand_Silently()
    {
        _stats.AxeCarry.Base = 2f;
        Throw();
        _events.Clear();

        _axes.Clear();
        Assert.That(_axes.InFlight.Length, Is.Zero);
        Assert.That(_axes.InHand, Is.EqualTo(2));
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        (PlayerCombat player, _, EnemySystem enemies, AxeSystem axes) = Build(new SilentEvents());
        EnemyAgent enemy = enemies.Registry.Spawn(Husk(), 4f * Vector3.UnitX);
        AllocationAssert.None(() =>
        {
            enemy.Health.Reset();
            axes.TryThrow(Vector3.Zero, Vector3.UnitX, Damage, 0f);
            axes.Tick(1f, 1f, Vector3.Zero, player, enemies, null);
            axes.Tick(1f, 2f, Vector3.Zero, player, enemies, null);
        });
    }

    private void Throw() => Assert.That(_axes.TryThrow(Vector3.Zero, Vector3.UnitX, Damage, _now), Is.True);

    private void Tick(float dt, Vector3 hand = default)
    {
        _now += dt;
        _axes.Tick(dt, _now, hand, _player, _enemies, null);
    }

    private EnemyAgent Spawn(float x, float z = 0f, float hp = 100f) =>
        _enemies.Registry.Spawn(Husk(hp), new Vector3(x, 0f, z));

    private static (PlayerCombat, PlayerStats, EnemySystem, AxeSystem) Build(IDomainEvents events)
    {
        CharacterSpec character = Beastmaster();
        var player = new PlayerCombat(character, events, new RecordingIntents(), EnemyCapacity);
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);
        var stats = new PlayerStats(player, motor, new LevelTracker(Scalings.Xp(), events));
        var enemies = new EnemySystem(Catalog(), events, new FixedRandom(), new DepthScaling(Scalings.Design()), EnemyCapacity);
        return (player, stats, enemies, new AxeSystem(events, stats, character.Weapon));
    }

    private static CharacterSpec Beastmaster() => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(5f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.ReturningAxe, Damage, 2f, Range, 360f, 0.4f,
            shotRadius: 0.3f, axeSpeed: Speed, axeCarry: 1, axeHitsPerPass: 3),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f),
        null, 0.5f);

    private static EnemySpec Husk(float hp = 100f) => new(
        EnemyId, new LocKey("enemy.husk.name"), hp, 3.5f, 1, threatCost: 4, xpValue: 12f,
        isElite: false, 8f, 1.2f, 0.4f, 0.6f, aggroRange: 30f, EnemyBehaviourKind.Static);

    private static ContentCatalog Catalog() => new(
        new[] { Beastmaster() }, new[] { Husk() },
        new[] { new ModeSpec(ModeId, new LocKey("mode.descent.name"), 1, true, 0,
            Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>()) });
}
