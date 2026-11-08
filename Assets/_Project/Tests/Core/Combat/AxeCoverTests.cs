using System;
using System.Linq;
using System.Numerics;
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

namespace Soulvail.Tests.Core.Combat;

/// <summary>BT-02: an out pass turns at cover, the way back flies through it, cover hides a target.</summary>
[TestFixture]
public sealed class AxeCoverTests
{
    private const int EnemyCapacity = 8;
    private const float Damage = 10f;
    private const float Range = 8f;
    private const float Speed = 8f;
    private const float WallX = 4f;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private static readonly ContentId ModeId = new("mode.descent");

    private RecordingEvents _events;
    private PlayerCombat _player;
    private EnemySystem _enemies;
    private AxeSystem _axes;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        CharacterSpec character = Beastmaster();
        _player = new PlayerCombat(character, _events, new RecordingIntents(), EnemyCapacity);
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);
        var stats = new PlayerStats(_player, motor, new LevelTracker(Scalings.Xp(), _events));
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(), new DepthScaling(Scalings.Design()), EnemyCapacity);
        _axes = new AxeSystem(_events, stats, character.Weapon);
        _now = 0f;
    }

    [Test]
    public void OutPass_CoverAtFourMetres_TurnsAtFour()
    {
        Throw(Vector3.Zero);
        Tick(1f, Vector3.Zero, Wall);

        var cover = new Vector3(WallX, 0f, 0f);
        Assert.That(_events.Single<AxeTurned>().Position, Is.EqualTo(cover));
        Assert.That(_axes.InFlight[0].Pass, Is.EqualTo(AxePass.Back));
        Assert.That(_axes.InFlight[0].Position, Is.EqualTo(cover));
        Assert.That(_axes.InFlight[0].TurnPoint, Is.EqualTo(cover));
    }

    [Test]
    public void OutPass_EnemyBeforeCover_Hit_EnemyBehind_NotHit()
    {
        EnemyAgent before = Spawn(2f);
        EnemyAgent behind = Spawn(6f);
        Throw(Vector3.Zero);
        Tick(1f, Vector3.Zero, Wall);

        Assert.That(_events.Of<AxeHit>().Select(hit => hit.EnemyId), Is.EqualTo(new[] { before.Id }));
        Assert.That(behind.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void BackPass_ThroughCover_ReachesHand()
    {
        // Thrown from beyond the wall, away from it: the out pass never meets cover.
        var thrownFrom = new Vector3(WallX + 1f, 0f, 0f);
        Throw(thrownFrom);
        Tick(1f, thrownFrom, Wall);
        Assert.That(_axes.InFlight[0].Pass, Is.EqualTo(AxePass.Back));

        // He crosses to the near side; the axe homes straight through the wall.
        for (int i = 0; i < 40 && _axes.InFlight.Length > 0; i++)
        {
            Tick(0.1f, Vector3.Zero, Wall);
        }

        Assert.That(_events.Count<AxeCaught>(), Is.EqualTo(1));
        Assert.That(_axes.InFlight.Length, Is.Zero);
        Assert.That(_axes.InHand, Is.EqualTo(1));
    }

    [Test]
    public void Target_BehindCover_NotChosen()
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
            BehindCover = true,
        };

        for (int i = 0; i < 30; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(_events.Count<AxeThrown>(), Is.Zero);
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);
    }

    [Test]
    public void NoTrace_FullRange()
    {
        EnemyAgent behind = Spawn(6f);
        Throw(Vector3.Zero);
        Tick(1f, Vector3.Zero, null);

        Assert.That(_events.Single<AxeTurned>().Position, Is.EqualTo(Range * Vector3.UnitX));
        Assert.That(_events.Single<AxeHit>().EnemyId, Is.EqualTo(behind.Id));
    }

    /// <summary>A wall across the arena at x = <see cref="WallX"/>, met from either side.</summary>
    private static Vector3? Wall(Vector3 from, Vector3 to)
    {
        if ((from.X < WallX) == (to.X < WallX))
        {
            return null;
        }

        float t = (WallX - from.X) / (to.X - from.X);
        return Vector3.Lerp(from, to, t);
    }

    private void Throw(Vector3 hand) =>
        Assert.That(_axes.TryThrow(hand, hand + Vector3.UnitX, Damage, _now), Is.True);

    private void Tick(float dt, Vector3 hand, Func<Vector3, Vector3, Vector3?> trace)
    {
        _now += dt;
        _axes.Tick(dt, _now, hand, _player, _enemies, trace);
    }

    private EnemyAgent Spawn(float x) => _enemies.Registry.Spawn(Husk(), new Vector3(x, 0f, 0f));

    private static CharacterSpec Beastmaster() => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(5f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.ReturningAxe, Damage, 2f, Range, 360f, 0.4f,
            shotRadius: 0.3f, axeSpeed: Speed, axeCarry: 1, axeHitsPerPass: 3),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f),
        null, 0.5f);

    private static EnemySpec Husk() => new(
        EnemyId, new LocKey("enemy.husk.name"), 100f, 3.5f, 1, threatCost: 4, xpValue: 12f,
        isElite: false, 8f, 1.2f, 0.4f, 0.6f, aggroRange: 30f, EnemyBehaviourKind.Static);

    private static ContentCatalog Catalog() => new(
        new[] { Beastmaster() }, new[] { Husk() },
        new[] { new ModeSpec(ModeId, new LocKey("mode.descent.name"), 1, true, 0,
            Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>()) });
}
