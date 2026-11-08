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

/// <summary>BT-01: two axes at once, attack speed sets the cadence, Swift Hands.</summary>
[TestFixture]
public sealed class AxePairTests
{
    private const int EnemyCapacity = 8;
    private const float Damage = 6f;
    private const float Rate = 0.8333f;
    private const float Range = 8f;
    private const float Speed = 14f;
    private const float Dt = 0.01f;
    private static readonly ContentId CharacterId = new("character.beastmaster");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private static readonly ContentId ModeId = new("mode.descent");
    private static readonly ContentId SwiftHandsId = new("skill.beastmaster.swift-hands");

    [Test]
    public void Carry_ModifierPastTwo_StaysTwo()
    {
        (_, PlayerStats stats, _, AxeSystem axes) = Build(new RecordingEvents());
        stats.AxeCarry.Base = 5f;
        axes.Clear();

        Assert.That(axes.InHand, Is.EqualTo(AxeSystem.MaxCarry));
        Assert.That(axes.TryThrowPair(Vector3.Zero, Vector3.UnitZ, Damage, 0f), Is.EqualTo(2));
    }

    [Test]
    public void ThrowPair_TwoInHand_BothFlySpread()
    {
        var events = new RecordingEvents();
        (_, _, _, AxeSystem axes) = Build(events);

        Assert.That(axes.TryThrowPair(Vector3.Zero, 3f * Vector3.UnitZ, Damage, 0f), Is.EqualTo(2));

        IReadOnlyList<AxeThrown> thrown = events.Of<AxeThrown>();
        Assert.That(thrown, Has.Count.EqualTo(2));
        float spread = AxeSystem.PairSpreadDegrees * MathF.PI / 180f;
        float side = Range * MathF.Sin(spread);
        float ahead = Range * MathF.Cos(spread);
        Assert.That(MathF.Abs(thrown[0].TurnPoint.X), Is.EqualTo(side).Within(1e-4f));
        Assert.That(thrown[0].TurnPoint.X, Is.EqualTo(-thrown[1].TurnPoint.X).Within(1e-4f));
        Assert.That(thrown[0].TurnPoint.Z, Is.EqualTo(ahead).Within(1e-4f));
        Assert.That(thrown[1].TurnPoint.Z, Is.EqualTo(ahead).Within(1e-4f));
        Assert.That(axes.InHand, Is.Zero);
    }

    [Test]
    public void ThrowPair_OneInHand_FliesStraight()
    {
        var events = new RecordingEvents();
        (_, PlayerStats stats, _, AxeSystem axes) = Build(events);
        stats.AxeCarry.Base = 1f;
        axes.Clear();

        Assert.That(axes.TryThrowPair(Vector3.Zero, 3f * Vector3.UnitZ, Damage, 0f), Is.EqualTo(1));
        Assert.That(events.Single<AxeThrown>().TurnPoint, Is.EqualTo(Range * Vector3.UnitZ));
        Assert.That(axes.TryThrowPair(Vector3.Zero, Vector3.UnitZ, Damage, 0f), Is.Zero, "Nothing left in hand.");
    }

    [Test]
    public void ThrowPair_CloseTarget_TurnsAtFullRange()
    {
        var events = new RecordingEvents();
        (_, _, _, AxeSystem axes) = Build(events);
        Vector3 hand = new(1f, 0f, 1f);

        axes.TryThrowPair(hand, hand + (0.5f * Vector3.UnitX), Damage, 0f);

        foreach (AxeThrown thrown in events.Of<AxeThrown>())
        {
            Assert.That(Vector3.Distance(thrown.From, thrown.TurnPoint), Is.EqualTo(Range).Within(1e-4f));
        }
    }

    [Test]
    public void OneAxeHome_NoSwing()
    {
        // A long throw, and the hand reported 6 m to the side once the pair has left, so the axe on
        // that side is caught well before the other: a window with exactly one axe home.
        var events = new RecordingEvents();
        RunSession session = Session(events, Character(range: 20f), new Vector3(0f, 0f, 8f), out WorldSnapshot snapshot);
        TickUntil(session, snapshot, () => events.Count<AxeThrown>() == 2, 1f);
        snapshot.PlayerPosition = new Vector3(6f, 0f, 0f);

        int swingsAtThrow = events.Count<PlayerAttacked>();
        int ticksWithOneHome = 0;
        for (int i = 0; i < 600 && events.Count<AxeCaught>() < 2; i++)
        {
            session.Tick(snapshot);
            if (events.Count<AxeCaught>() == 1)
            {
                ticksWithOneHome++;
                Assert.That(events.Count<PlayerAttacked>(), Is.EqualTo(swingsAtThrow), "No swing with one axe home.");
            }
        }

        Assert.That(ticksWithOneHome, Is.GreaterThan(5), "The fixture really had one axe home for a while.");
        TickUntil(session, snapshot, () => events.Count<AxeThrown>() == 4, 0.1f);
        Assert.That(events.Count<PlayerAttacked>(), Is.EqualTo(swingsAtThrow + 1), "Both home, he throws again.");
    }

    [Test]
    public void Cadence_Base_OneThrowPer1_2s()
    {
        AssertCadence(swiftHands: false, expected: 1.2f);
    }

    [Test]
    public void Cadence_SwiftHands_OneThrowPerSecond()
    {
        AssertCadence(swiftHands: true, expected: 1f);
    }

    [Test]
    public void Pace_PlusTwentyPercent_FlightTwentyPercentFaster()
    {
        (PlayerCombat player, PlayerStats stats, EnemySystem enemies, AxeSystem axes) = Build(new RecordingEvents());
        Assert.That(axes.PaceFactor, Is.EqualTo(1f).Within(1e-5f));

        stats.Resolve(PlayerStat.FireRate).Base = Rate * 1.2f;
        Assert.That(axes.PaceFactor, Is.EqualTo(1.2f).Within(1e-5f));
        axes.TryThrowPair(Vector3.Zero, Vector3.UnitZ, Damage, 0f);
        axes.Tick(0.1f, 0.1f, Vector3.Zero, player, enemies, null);

        float travelled = axes.InFlight[0].Position.Length();
        Assert.That(travelled, Is.EqualTo(Speed * 1.2f * 0.1f).Within(1e-4f));
    }

    [TestCase(1f)]
    [TestCase(1.2f)]
    [TestCase(2f)]
    [TestCase(3.5f)]
    public void Pace_BothBackBeforeNextThrow(float bonus)
    {
        (PlayerCombat player, PlayerStats stats, EnemySystem enemies, AxeSystem axes) = Build(new RecordingEvents());
        Stat fireRate = stats.Resolve(PlayerStat.FireRate);
        fireRate.Base = Rate * bonus;
        float interval = 1f / fireRate.Value;

        axes.TryThrowPair(Vector3.Zero, Vector3.UnitZ, Damage, 0f);
        float now = 0f;
        while (axes.InHand < AxeSystem.MaxCarry && now < 10f)
        {
            now += Dt;
            axes.Tick(Dt, now, Vector3.Zero, player, enemies, null);
        }

        Assert.That(axes.InHand, Is.EqualTo(AxeSystem.MaxCarry));
        Assert.That(now, Is.LessThan(interval), $"Home at {now:F2} s, the next throw is due at {interval:F2} s.");
    }

    [Test]
    public void Moving_NoThrow_AxesStillReturn()
    {
        var events = new RecordingEvents();
        RunSession session = Session(events, Character(), new Vector3(0f, 0f, 5f), out WorldSnapshot snapshot);
        TickUntil(session, snapshot, () => events.Count<AxeThrown>() == 2, 1f);

        snapshot.MoveInput = Vector2.UnitX;
        snapshot.PlayerVelocity = 5f * Vector3.UnitX;
        for (int i = 0; i < 300; i++)
        {
            snapshot.PlayerPosition += snapshot.PlayerVelocity * Dt;
            session.Tick(snapshot);
        }

        Assert.That(events.Count<AxeCaught>(), Is.EqualTo(2), "Both came home to the moving hand.");
        Assert.That(events.Count<AxeThrown>(), Is.EqualTo(2), "And none left while he moved.");
    }

    [Test]
    public void Dps_StandingFrog_TwoThrowsKill()
    {
        (PlayerCombat player, _, EnemySystem enemies, AxeSystem axes) = Build(new RecordingEvents());
        EnemyAgent frog = enemies.Registry.Spawn(Husk(36f), new Vector3(0f, 0f, 3f));
        float now = 0f;

        for (int throws = 1; throws <= 2; throws++)
        {
            Assert.That(axes.TryThrowPair(Vector3.Zero, frog.Position, Damage, now), Is.EqualTo(2));
            while (axes.InFlight.Length > 0)
            {
                now += Dt;
                axes.Tick(Dt, now, Vector3.Zero, player, enemies, null);
            }

            Assert.That(frog.IsAlive, Is.EqualTo(throws == 1), $"After throw {throws}.");
        }
    }

    private static void AssertCadence(bool swiftHands, float expected)
    {
        var events = new RecordingEvents();
        RunSession session = Session(events, Character(), new Vector3(0f, 0f, 5f), out WorldSnapshot snapshot, swiftHands);
        var throwTimes = new List<float>();
        int thrown = 0;
        for (int i = 0; i < 700; i++)
        {
            session.Tick(snapshot);
            if (events.Count<AxeThrown>() > thrown)
            {
                Assert.That(events.Count<AxeThrown>() - thrown, Is.EqualTo(2), "The pair leaves together.");
                thrown = events.Count<AxeThrown>();
                throwTimes.Add(session.State.Time);
            }
        }

        Assert.That(throwTimes, Has.Count.GreaterThanOrEqualTo(4));
        for (int i = 1; i < throwTimes.Count; i++)
        {
            Assert.That(throwTimes[i] - throwTimes[i - 1], Is.EqualTo(expected).Within(Dt * 1.5f), $"Gap {i}.");
        }
    }

    private static void TickUntil(RunSession session, WorldSnapshot snapshot, Func<bool> done, float seconds)
    {
        for (int i = 0; i < seconds / Dt && !done(); i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(done(), Is.True, "The fixture reached its condition.");
    }

    private static RunSession Session(
        RecordingEvents events, CharacterSpec character, Vector3 enemyAt, out WorldSnapshot snapshot, bool swiftHands = false)
    {
        SkillSpec swift = SwiftHands();
        SkillSpec other = Filler("skill.test.other");
        SkillSpec third = Filler("skill.test.third");
        var tree = new SkillTreeSpec(new ContentId("tree.test.beastmaster"), character.Id, new[]
        {
            Branch("tree.test.a", swift.Id),
            Branch("tree.test.b", other.Id),
            Branch("tree.test.c", third.Id),
        });
        var catalog = new ContentCatalog(new[] { character }, new[] { Husk(100000f) }, new[] { Mode() },
            new[] { swift, other, third }, new[] { tree });

        const int seed = 99;
        var random = new FixedRandom(seed);
        var session = new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), EnemyCapacity, EnemyCapacity, 8);
        RunSnapshot? restore = swiftHands
            ? new RunSnapshot(RunSnapshot.CurrentVersion, ModeId, CharacterId, seed, 1,
                new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0,
                new[] { swift.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
                Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>())
            : null;
        session.Start(new RunConfig(ModeId, CharacterId, seed, 1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(EnemyId, enemyAt) }), restore));

        snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Dt };
        snapshot.AddEnemy() = new EnemySense
        {
            Id = events.Single<EnemySpawned>().Id,
            Position = enemyAt,
            HasLineOfSight = true,
        };
        return session;
    }

    private static (PlayerCombat, PlayerStats, EnemySystem, AxeSystem) Build(IDomainEvents events)
    {
        CharacterSpec character = Character();
        var player = new PlayerCombat(character, events, new RecordingIntents(), EnemyCapacity);
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);
        var stats = new PlayerStats(player, motor, new LevelTracker(Scalings.Xp(), events));
        var enemies = new EnemySystem(Catalog(), events, new FixedRandom(), new DepthScaling(Scalings.Design()), EnemyCapacity);
        return (player, stats, enemies, new AxeSystem(events, stats, character.Weapon));
    }

    /// <summary>The shipped Beastmaster's weapon numbers (Beastmaster.asset).</summary>
    private static CharacterSpec Character(float range = Range) => new(
        CharacterId, new LocKey("character.beastmaster.name"), new LocKey("character.beastmaster.description"),
        100f, new MovementSpec(5f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.ReturningAxe, Damage, Rate, range, 360f, 0f,
            shotRadius: 0.5f, firesWhileMoving: false, axeSpeed: Speed, axeCarry: 2, axeHitsPerPass: 3),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f),
        null, 0.5f);

    private static SkillSpec SwiftHands() => new(SwiftHandsId, new LocKey("skill.beastmaster.swift-hands.name"),
        new LocKey("skill.beastmaster.swift-hands.description"), SkillKind.Passive,
        new IEffect[] { new ModifyStat(PlayerStat.FireRate, ModifierKind.PercentAdd, 0.2f) });

    private static SkillSpec Filler(string id) => new(new ContentId(id), new LocKey("skill.test.name"),
        new LocKey("skill.test.desc"), SkillKind.Passive,
        new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });

    private static SkillBranchSpec Branch(string id, ContentId node) =>
        new(new LocKey(id), new IReadOnlyList<ContentId>[] { new[] { node } });

    private static EnemySpec Husk(float hp = 100f) => new(
        EnemyId, new LocKey("enemy.husk.name"), hp, 3.5f, 1, threatCost: 4, xpValue: 12f,
        isElite: false, 8f, 1.2f, 0.4f, 0.6f, aggroRange: 30f, EnemyBehaviourKind.Static);

    private static ModeSpec Mode() => new(ModeId, new LocKey("mode.descent.name"), 1, true, 0,
        Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());

    private static ContentCatalog Catalog() => new(new[] { Character() }, new[] { Husk() }, new[] { Mode() });
}
