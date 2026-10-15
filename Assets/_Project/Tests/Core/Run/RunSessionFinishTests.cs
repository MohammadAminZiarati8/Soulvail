using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// UI-11c: a run that clears its mode's final stage is paid like a death and says so, and nothing
/// else does. Every row drives a real session, for <c>RunSessionResumeTests</c>' reason —
/// <c>RunState</c> and the systems under it are <c>internal</c>, so the only route to a kill is
/// walking the player onto a body and answering its swing the way Unity would.
/// </summary>
[TestFixture]
public sealed class RunSessionFinishTests
{
    private const string FiniteId = "mode.finite";
    private const string EndlessId = "mode.endless";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string ExecutionerId = "enemy.executioner";
    private const string FirstArenaId = "arena.pillars";
    private const string SecondArenaId = "arena.tiered";

    private const int FinalStage = 2;
    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const int Seed = 7;
    private const float Frame = 1f / 60f;
    private const float Ring = 10f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private ContentCatalog _catalog;
    private RunSession _session;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();

        // Long enough that forty stages cannot exhaust the script and start reading the fake's
        // default, which would stop every position after that point from moving.
        _random = new FixedRandom(Seed, Alternating(65_536));

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk(), Executioner() },
            new[] { Mode(FiniteId, isEndless: false), Mode(EndlessId, isEndless: true) });

        _session = new RunSession(
            _catalog,
            _random,
            _events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    [Test]
    public void FinalClear_PublishesShardsThenFinishedThenEnded()
    {
        PlayToTheEnd();

        int awarded = IndexOf<ShardsAwarded>();
        int finished = IndexOf<ModeFinished>();
        int ended = IndexOf<RunEnded>();

        Assert.That(awarded, Is.GreaterThanOrEqualTo(0), "The final clear paid nothing.");
        Assert.That(finished, Is.GreaterThan(awarded), "ShardsAwarded, then ModeFinished.");
        Assert.That(ended, Is.GreaterThan(finished), "ModeFinished, then RunEnded.");

        Assert.That(_events.Count<ModeFinished>(), Is.EqualTo(1));
        Assert.That(_events.Single<ModeFinished>().FinalStage, Is.EqualTo(FinalStage));
        Assert.That(_events.Single<ModeFinished>().ModeId, Is.EqualTo(new ContentId(FiniteId)));
    }

    [Test]
    public void FinalClear_PaysAsDeathWould()
    {
        PlayToTheEnd();

        ShardsAwarded awarded = _events.Single<ShardsAwarded>();

        ModeSpec mode = _catalog.Mode(new ContentId(FiniteId));

        Assert.That(awarded.Total, Is.EqualTo(ShardPayout.For(FinalStage, mode)));
        Assert.That(awarded.Total, Is.GreaterThan(0));
        Assert.That(awarded.DeepestStage, Is.EqualTo(FinalStage));
        Assert.That(awarded.BossesKilled, Is.EqualTo(ShardPayout.BossesKilled(FinalStage, mode)));
        Assert.That(awarded.ModeId, Is.EqualTo(new ContentId(FiniteId)));
    }

    [Test]
    public void DeathOnFinalStage_NoFinished()
    {
        _session.Start(new RunConfig(
            new ContentId(FiniteId),
            new ContentId(OathboundId),
            Seed,
            FinalStage,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(ExecutionerId), Vector3.Zero) }),
            restore: null));

        for (int i = 0; i < 240 && _session.IsRunning; i++)
        {
            _session.Tick(Snapshot(Vector3.Zero));
        }

        Assert.That(_session.IsRunning, Is.False, "Sanity: the Executioner ended the run.");
        Assert.That(_events.Count<ShardsAwarded>(), Is.EqualTo(1), "A death still pays.");
        Assert.That(_events.Count<ModeFinished>(), Is.Zero, "But a death finishes nothing.");
    }

    [Test]
    public void FinalClear_TakesNoSnapshot()
    {
        StartFresh(FiniteId);
        ClearTheStage();
        CrossTheBoundary();

        int before = _events.Count<RunSnapshotTaken>();

        ClearTheStage();
        TickUntilEnded();

        Assert.That(_events.Count<ModeFinished>(), Is.EqualTo(1), "Sanity: the mode finished.");
        Assert.That(
            _events.Count<RunSnapshotTaken>(),
            Is.EqualTo(before),
            "A snapshot of a finished run is a resume into a stage the mode does not have.");
    }

    [Test]
    public void Endless_NeverFinished()
    {
        StartFresh(EndlessId);

        const int Stages = 40;

        for (int stage = 1; stage <= Stages; stage++)
        {
            ClearTheStage();
            CrossTheBoundary();
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(Stages));
        Assert.That(_session.IsRunning, Is.True);
        Assert.That(_events.Count<ModeFinished>(), Is.Zero);
    }

    // ---- Fixture --------------------------------------------------------------------------------

    private void StartFresh(string modeId)
    {
        _session.Start(new RunConfig(
            new ContentId(modeId),
            new ContentId(OathboundId),
            Seed,
            1,
            SpawnPlan.Empty,
            restore: null));
    }

    /// <summary>Starts the finite mode and plays both of its stages out.</summary>
    private void PlayToTheEnd()
    {
        StartFresh(FiniteId);
        ClearTheStage();
        CrossTheBoundary();
        ClearTheStage();
        TickUntilEnded();

        Assert.That(_session.IsRunning, Is.False, "The fixture failed to finish the mode.");
    }

    /// <summary>The flag is the flow's and the end is the session's, so allow the ticks between.</summary>
    private void TickUntilEnded()
    {
        for (int i = 0; i < 900 && _session.IsRunning; i++)
        {
            _session.Tick(Snapshot(Vector3.Zero));
        }
    }

    private int IndexOf<T>()
        where T : struct
    {
        for (int i = 0; i < _events.All.Count; i++)
        {
            if (_events.All[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Plays the stage out — every body of every wave — and stops on the tick
    /// <c>StageCleared</c> lands. <c>RunSessionResumeTests</c>' helper, for its reasons.
    /// </summary>
    private void ClearTheStage()
    {
        int target = _events.Count<StageCleared>() + 1;
        int killed = _events.Count<EnemyDied>();

        var report = new int[1];

        for (int i = 0; i < 9_000 && _events.Count<StageCleared>() < target; i++)
        {
            IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();

            bool standing = spawned.Count > killed;
            Vector3 where = standing ? spawned[killed].Position : Vector3.Zero;

            if (standing)
            {
                report[0] = spawned[killed].Id;
            }

            _session.Tick(Snapshot(where));

            if (!standing || !_session.IsRunning)
            {
                continue;
            }

            _session.ReportConeHits(report);

            if (_events.Count<EnemyDied>() > killed)
            {
                killed++;
            }
        }

        Assert.That(
            _events.Count<StageCleared>(),
            Is.GreaterThanOrEqualTo(target),
            "The fixture failed to clear the stage, so whatever this row asserts next is about the "
                + "fixture rather than about the run.");
    }

    /// <summary>Waits out the clear beat, leaves the Sanctum, walks into the door and lets the fade run out.</summary>
    private void CrossTheBoundary()
    {
        int arrivals = _events.Count<StageArrived>();

        for (int i = 0; i < 900 && _events.Count<StageArrived>() == arrivals; i++)
        {
            if (_session.IsSanctumOpen)
            {
                _session.LeaveSanctum();
            }

            _session.Tick(Snapshot(Door));
        }

        Assert.That(
            _events.Count<StageArrived>(),
            Is.GreaterThan(arrivals),
            "The fixture failed to cross the boundary, so whatever this row asserts next is about "
                + "the fixture rather than about the run.");
    }

    private static WorldSnapshot Snapshot(Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = Frame,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    // ---- Content ---------------------------------------------------------------------------------

    /// <summary>
    /// Flat curves, so stage 40 of the endless row is the same fight as stage 1 and the row is
    /// about the mode rather than about whether the fixture's player survives depth.
    /// </summary>
    private static ModeSpec Mode(string id, bool isEndless)
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(20f, 0f, 0f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0f, 1f, 1, 1),
            new StatCurve(0f, 1f, 1, 1),
            new StatCurve(0f, 1f, 5, 0));

        return new ModeSpec(
            new ContentId(id),
            new LocKey(id + ".name"),
            startingStage: 1,
            isEndless: isEndless,
            finalStage: FinalStage,
            scaling,
            Scalings.Xp(),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ArenaEntry(new ContentId(FirstArenaId)), new ArenaEntry(new ContentId(SecondArenaId)) },
            bossRoster: null);
    }

    /// <summary><c>Static</c> on purpose: these rows are about the end of a run, not about a fight.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
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
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>
    /// A Chaser that kills the Oathbound in one strike — <c>RunSessionTests</c>' device, for the
    /// one row that needs a death.
    /// </summary>
    private static EnemySpec Executioner() => new EnemySpec(
        new ContentId(ExecutionerId),
        new LocKey("enemy.executioner.name"),
        500f,
        3.5f,
        1,
        4,
        12f,
        false,
        400f,
        2f,
        0.05f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Chaser);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(12f, 3f, 1f));

    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }
}
