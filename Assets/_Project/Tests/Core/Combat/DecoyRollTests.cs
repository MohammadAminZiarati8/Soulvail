using System;
using System.Collections.Generic;
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
public sealed class DecoyRollTests
{
    private const int Capacity = 8;
    private const float Frame = 0.125f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private WorldSnapshot _snapshot;
    private PlayerCombat _player;
    private LureSystem _lures;
    private ShadeSystem _shades;
    private PlayerStats _stats;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [Test]
    public void Roll_Rank1_LeavesDecoyAtStart_TwoSeconds()
    {
        LearnRank(1);
        Vector3 start = new(2f, 0.5f, 4f);
        _snapshot.PlayerPosition = start;
        Roll(1f);
        _snapshot.PlayerPosition += 8f * Vector3.UnitX;
        Step(1.25f);

        ShadeAppeared appeared = _events.Single<ShadeAppeared>();
        Assert.That(appeared.Kind, Is.EqualTo(ShadeKind.Decoy));
        Assert.That(appeared.Position, Is.EqualTo(start));
        Assert.That(appeared.ExpiresAt, Is.EqualTo(3f));
        Assert.That(_shades.Standing[0].DamageOfWeapon, Is.Zero);
        Assert.That(_events.Single<DecoySpawned>().Duration, Is.EqualTo(2f));

        _shades.Tick(2.875f, null, _player);
        _lures.Tick(2.875f);
        Assert.That(_shades.Standing.Length, Is.EqualTo(1));
        Assert.That(_lures.Count, Is.EqualTo(1));

        _shades.Tick(3f, null, _player);
        _lures.Tick(3f);
        Assert.That(_events.Single<ShadeFaded>().Expired, Is.True);
        Assert.That(_shades.Standing.Length, Is.Zero);
        Assert.That(_lures.Count, Is.Zero);
    }

    [TestCase(8.875f, 1, TestName = "Roll_WithinInterval_NoDecoy")]
    [TestCase(9f, 2, TestName = "Roll_AfterInterval_Decoy")]
    public void Roll_RespectsInterval(float nextRoll, int expectedShades)
    {
        LearnRank(1);
        Roll(1f);
        Roll(nextRoll);

        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(2));
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(expectedShades));
    }

    [Test]
    public void BufferedRoll_DropsOnlyWhenRollStarts()
    {
        Roll(1f);
        LearnRank(1);

        Roll(3.375f);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1));
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);

        _snapshot.PlayerPosition = Vector3.UnitX;
        Step(3.5f);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(2));
        Assert.That(_events.Single<ShadeAppeared>().Position, Is.EqualTo(Vector3.UnitX));
    }

    [Test]
    public void Off_NoDecoy()
    {
        Roll(1f);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1));
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);
        Assert.That(_lures.Count, Is.Zero);
    }

    [Test]
    public void Decoy_DropsLure_EnemiesWalkAtIt()
    {
        var body = new EnemySpec(new ContentId("enemy.test.chaser"), new LocKey("enemy.test.name"),
            100f, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Chaser);
        var enemies = new EnemySystem(new ContentCatalog(new[] { ShadeSystemTests.Character() }, new[] { body }),
            _events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        EnemyAgent enemy = enemies.Spawn(body.Id, 10f * Vector3.UnitZ);
        _shades.Spawn(ShadeKind.Decoy, Vector3.Zero, 0f, 2f, 0f);
        _snapshot.PlayerPosition = enemy.Position + 8f * Vector3.UnitX;
        _snapshot.AddEnemy() = new EnemySense
        {
            Id = enemy.Id,
            Position = enemy.Position,
            PathDirectionToPlayer = Vector2.UnitX,
            HasLineOfSight = true,
        };
        var projectiles = new ProjectileSystem(_events, Capacity);

        for (int i = 1; i <= 2; i++)
        {
            enemies.Ingest(_snapshot, _lures);
            enemies.Tick(new EnemyTickContext(Frame, i * Frame, _player, _intents, _events, projectiles, enemies));
        }

        Assert.That(enemy.Blackboard.QuarryIsADecoy, Is.True);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(-2f * Vector3.UnitZ));
    }

    [Test]
    public void FullLureStore_DecoyShadeStillStands()
    {
        for (int i = 0; i < LureSystem.Capacity; i++)
        {
            _lures.Drop(Vector3.UnitX * i, 0f, 5f);
        }

        LearnRank(1);
        Roll(1f);

        Assert.That(_events.Single<ShadeAppeared>().Kind, Is.EqualTo(ShadeKind.Decoy));
        Assert.That(_shades.Standing.Length, Is.EqualTo(1));
        Assert.That(_events.Count<DecoySpawned>(), Is.EqualTo(LureSystem.Capacity));
    }

    [Test]
    public void ReplacedDecoy_LureEndsOnItsOwnTime()
    {
        int id = _shades.Spawn(ShadeKind.Decoy, Vector3.Zero, 0f, 2f, 0f);
        for (int i = 0; i < ShadeSystem.Capacity; i++)
        {
            _shades.Spawn(ShadeKind.Image, Vector3.UnitX * i, 0.5f, 3f, 0f);
        }

        ShadeFaded faded = _events.Single<ShadeFaded>();
        Assert.That(faded.Id, Is.EqualTo(id));
        Assert.That(faded.Expired, Is.False);
        _lures.Tick(1.875f);
        Assert.That(_lures.TryGetLure(Vector3.UnitX, out Vector3 position), Is.True);
        Assert.That(position, Is.EqualTo(Vector3.Zero));
        _lures.Tick(2f);
        Assert.That(_lures.Count, Is.Zero);
    }

    [TestCase(2, 6f, TestName = "Rank2_ThreeSeconds_EverySix")]
    [TestCase(3, 5f, TestName = "Rank3_ThreeSeconds_EveryFive")]
    public void HigherRank_ChangesDurationAndInterval(int rank, float interval)
    {
        LearnRank(rank);
        Roll(1f);
        Roll(3.5f);
        Roll(1f + interval);

        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(3));
        var appeared = _events.Of<ShadeAppeared>();
        Assert.That(appeared.Count, Is.EqualTo(2));
        Assert.That(appeared[0].ExpiresAt, Is.EqualTo(4f));
        Assert.That(appeared[1].ExpiresAt, Is.EqualTo(4f + interval));
    }

    [Test]
    public void StageBoundary_ReadiesDecoy()
    {
        CharacterSpec ranger = ShadeSystemTests.Character();
        EnemySpec enemy = ShadeSystemTests.Body(hp: 10f);
        var mode = new ModeSpec(new ContentId("mode.test.decoy"), new LocKey("mode.test.name"), 1, true, 0,
            new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
                new ConcurrencyCurve(Capacity, 1000), new StatCurve(0.06f, 4f, 1, 1),
                new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0)),
            Scalings.Xp(), new[] { new RosterEntry(enemy.Id, 1) });
        var decoy = new SkillSpec(new ContentId("skill.test.decoy"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { new ModifyStat(PlayerStat.DecoySeconds, ModifierKind.Flat, 2f) });
        var fillerA = new SkillSpec(new ContentId("skill.test.a"), decoy.NameKey, decoy.DescriptionKey,
            SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var fillerB = new SkillSpec(new ContentId("skill.test.b"), decoy.NameKey, decoy.DescriptionKey,
            SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var tree = new SkillTreeSpec(new ContentId("tree.test.decoy"), ranger.Id, new[]
        {
            Branch("tree.test.a", decoy.Id),
            Branch("tree.test.b", fillerA.Id),
            Branch("tree.test.c", fillerB.Id),
        });
        var catalog = new ContentCatalog(new[] { ranger }, new[] { enemy }, new[] { mode },
            new[] { decoy, fillerA, fillerB }, new[] { tree });
        var random = new FixedRandom(7);
        var session = new RunSession(catalog, random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, ranger.Id, 7, 1,
            new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0, new[] { decoy.Id },
            new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, ranger.Id, 7, 1, SpawnPlan.Empty, saved));

        _snapshot.Dt = Frame;
        _snapshot.SpawnPoints = new[] { 7f * Vector3.UnitZ };
        for (int i = 0; i < 160 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(_snapshot);
        }

        EnemySpawned spawned = _events.Single<EnemySpawned>();
        session.MovementSkill();
        session.Tick(_snapshot);
        float firstRoll = session.State.Time;
        var lures = (LureSystem)typeof(RunState).GetProperty("Lures", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session.State);
        Assert.That(lures.Count, Is.EqualTo(1));
        int rollLure = _events.Single<DecoySpawned>().Id;
        Vector3 otherPosition = Vector3.UnitX;
        lures.Drop(otherPosition, session.State.Time, 6f);
        _snapshot.AddEnemy() = new EnemySense { Id = spawned.Id, Position = spawned.Position, HasLineOfSight = true };
        for (int i = 0; i < 16 && _events.Count<StageCleared>() == 0; i++)
        {
            session.Tick(_snapshot);
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
        Assert.That(session.State.Time, Is.LessThan(firstRoll + 2f));
        Assert.That(_events.Single<DecoyExpired>().Id, Is.EqualTo(rollLure));
        Assert.That(lures.Count, Is.EqualTo(1));
        Assert.That(lures.TryGetLure(Vector3.Zero, out Vector3 remaining), Is.True);
        Assert.That(remaining, Is.EqualTo(otherPosition));
        Assert.That(_events.Single<ShadeFaded>().Expired, Is.False);
        session.MovementSkill();
        session.Tick(_snapshot);

        Assert.That(session.State.Time, Is.LessThan(firstRoll + 8f));
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(2));
    }

    [Test]
    public void Reset_ReadiesDecoy()
    {
        LearnRank(1);
        Roll(1f);
        _player.Reset();
        Roll(2f);
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(2));
    }

    [Test]
    public void Roll_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        LearnRank(1);
        float now = 0f;
        AllocationAssert.None(() =>
        {
            _lures.Tick(now);
            _shades.Tick(now, null, _player);
            Roll(now);
            _intents.Clear();
            now += 8f;
        });
    }

    private void Initialise(IDomainEvents events)
    {
        CharacterSpec character = ShadeSystemTests.Character();
        _snapshot = new WorldSnapshot(Capacity);
        _intents = new RecordingIntents();
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        _lures = new LureSystem(events);
        _shades = new ShadeSystem(new ProjectileSystem(events, Capacity), _lures, _stats, character, events);
        // Keep the runtime attachment internal. RunSession's wiring is exercised by the stage test.
        typeof(PlayerCombat).GetMethod("AttachShades", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_player, new object[] { _shades });
    }

    private void LearnRank(int rank)
    {
        _stats.Resolve(PlayerStat.DecoySeconds).Add(new Modifier(ModifierKind.Flat, 2f, this));
        if (rank >= 2)
        {
            _stats.Resolve(PlayerStat.DecoySeconds).Add(new Modifier(ModifierKind.Flat, 1f, this));
            _stats.Resolve(PlayerStat.DecoyInterval).Add(new Modifier(ModifierKind.Flat, -2f, this));
        }

        if (rank >= 3)
        {
            _stats.Resolve(PlayerStat.DecoyInterval).Add(new Modifier(ModifierKind.Flat, -1f, this));
        }
    }

    private void Roll(float now)
    {
        _player.Charge.Request(now);
        Step(now);
    }

    private void Step(float now) =>
        _player.Tick(0f, now, _snapshot, ReadOnlySpan<EnemyAgent>.Empty, Vector3.UnitZ);

    private static SkillBranchSpec Branch(string id, ContentId skill) =>
        new(new LocKey(id), new IReadOnlyList<ContentId>[] { new[] { skill } });
}
