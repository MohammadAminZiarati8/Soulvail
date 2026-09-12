using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Save;

/// <summary>
/// What a run writes down, when it writes it, and the one property the whole thing exists for:
/// that every capture is taken <em>before</em> anything has drawn for the stage it describes, so a
/// resumed run composes the same waves the interrupted one was about to fight.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row drives a real <c>RunSession</c> rather than building a <c>RunState</c>.</b>
/// <c>RunState</c>'s constructor is <c>internal</c> and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c>, deliberately — <c>RunSessionTests</c>' remarks named this task as the
/// likely first fixture to want one, and the answer is no: opening core's internals so that a save
/// test can skip starting a run would make this fixture stop testing the thing that actually
/// writes the file. The cost is a fixture that has to play the game to get a state, which is the
/// same cost <c>StageFlowTests</c> pays for its session rows and for the same reason.
/// </para>
/// <para>
/// <b>The clock is stated, never real.</b> A row that stamped a snapshot with
/// <c>DateTimeOffset.UtcNow</c> would be a test that ages.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RunRecorderTests
{
    private const string HuskId = "enemy.husk";
    private const string ExecutionerId = "enemy.executioner";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    /// <summary>GD §8.1's threat cost for the one archetype these rows compose from.</summary>
    private const int HuskCost = 4;

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>The wave curve's ceiling — what a run sizes its one plan to.</summary>
    private const int MaxWaves = 5;

    /// <summary>The Aegis this fixture's class carries. Twelve points, and never twelve of anything else.</summary>
    private const float ShieldMax = 12f;

    /// <summary>60 fps — the rate every row here ticks at.</summary>
    private const float Frame = 1f / 60f;

    /// <summary>Comfortably outside <c>SpawnDirector.MinPlayerDistance</c> of the origin.</summary>
    private const float Ring = 10f;

    /// <summary>Where every row's door is.</summary>
    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    /// <summary>The instant every snapshot in this fixture is stamped with.</summary>
    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 12, 21, 30, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private ModeSpec _mode;
    private RunRecorder _recorder;
    private RunSession _session;

    // ---- What a snapshot carries (rule 4) ------------------------------------------------------

    [Test]
    public void Take_RecordsTheRun()
    {
        Build(OneHuskStage(), seed: -7);
        StartAt(3);

        // Something on the simulated clock, so RunTime is a number rather than a zero that every
        // uninitialised field also is.
        TickFor(90);

        RunState state = _session.State;

        // The fixture's own claim, checked before the row's: a snapshot of a run whose shield is
        // empty could not tell the absolute from the fraction, which is the one thing this row is
        // really about.
        Assert.That(state.PlayerShield, Is.EqualTo(ShieldMax), "The fixture's class carries an Aegis.");
        Assert.That(state.Time, Is.GreaterThan(0f));

        _events.Clear();

        _recorder.Take(state, 4);

        RunSnapshotTaken taken = _events.Single<RunSnapshotTaken>();
        RunSnapshot snapshot = taken.Snapshot;

        Assert.That(snapshot.ModeId, Is.EqualTo(new ContentId(ModeId)));
        Assert.That(snapshot.CharacterId, Is.EqualTo(new ContentId(OathboundId)));

        // Negative on purpose: every int is a legal seed, and a row that only ever passed a
        // positive one would not say so.
        Assert.That(snapshot.Seed, Is.EqualTo(-7));
        Assert.That(snapshot.Seed, Is.EqualTo(state.Seed));

        // The stage the player is about to play, handed in — never the one behind them (rule 3).
        Assert.That(snapshot.StageIndex, Is.EqualTo(4));
        Assert.That(snapshot.StageIndex, Is.Not.EqualTo(state.StageIndex));

        Assert.That(snapshot.PlayerHp, Is.EqualTo(state.PlayerHp));
        Assert.That(snapshot.RunTime, Is.EqualTo(state.Time));

        // **The rule-4 trap, and the reason RunState grew a second shield read.** A fraction cannot
        // be restored without the maximum that produced it, and M3's tree moves that maximum — so
        // the snapshot has to carry twelve points rather than "full".
        Assert.That(snapshot.PlayerShield, Is.EqualTo(ShieldMax));
        Assert.That(
            snapshot.PlayerShield,
            Is.Not.EqualTo(state.PlayerShieldFraction),
            "The snapshot must carry the absolute Aegis, not the fraction the HUD fills a ring to.");
    }

    [Test]
    public void Take_RecordsAWoundedRun()
    {
        // The other half of the row above, which a fresh run cannot state: HP and the Aegis are
        // reads of what is *left*, not of the maxima. A run at full health cannot tell the two
        // apart, because on a fresh run they are the same number.
        Build(OneHuskStage(), character: Oathbound(shield: true));
        StartAt(1, executionerAt: new Vector3(1.5f, 0f, 0f));

        for (int i = 0; i < 600 && _session.State.PlayerHp >= _session.State.PlayerMaxHp; i++)
        {
            TickFor(1);
        }

        RunState state = _session.State;

        Assert.That(state.PlayerHp, Is.LessThan(state.PlayerMaxHp), "The fixture failed to land a hit.");
        Assert.That(state.PlayerShield, Is.LessThan(ShieldMax), "The Aegis absorbs before hit points do.");

        _events.Clear();

        _recorder.Take(state, 2);

        RunSnapshot snapshot = _events.Single<RunSnapshotTaken>().Snapshot;

        Assert.That(snapshot.PlayerHp, Is.EqualTo(state.PlayerHp));
        Assert.That(snapshot.PlayerHp, Is.LessThan(state.PlayerMaxHp));
        Assert.That(snapshot.PlayerShield, Is.EqualTo(state.PlayerShield));
    }

    [Test]
    public void Take_StampsTheWallClock()
    {
        Build(OneHuskStage());
        StartAt(1);
        TickFor(120);

        _events.Clear();

        _recorder.Take(_session.State, 2);

        RunSnapshot snapshot = _events.Single<RunSnapshotTaken>().Snapshot;

        Assert.That(snapshot.WrittenAt, Is.EqualTo(Instant));

        // Two numbers, neither derived from the other: two seconds of simulated run against a
        // wall-clock instant in 2026. A snapshot that computed one from the other would be wrong on
        // the first backgrounded app and on the first date change (M2-01 rule 1).
        Assert.That(snapshot.RunTime, Is.EqualTo(_session.State.Time));
        Assert.That(snapshot.RunTime, Is.GreaterThan(1.5f).And.LessThan(2.5f));

        // And it is read fresh, not cached at construction.
        _clock.Advance(TimeSpan.FromDays(3));
        _events.Clear();

        _recorder.Take(_session.State, 2);

        Assert.That(_events.Single<RunSnapshotTaken>().Snapshot.WrittenAt, Is.EqualTo(Instant.AddDays(3)));
    }

    [Test]
    public void Take_CapturesEveryStream()
    {
        Build(OneHuskStage());

        // Scripted individually, so the five streams move independently — a shared one would make
        // "every stream" a claim about one number written five times.
        _random.SetSpawn(Alternating(64)).SetOffers(Alternating(64)).SetAffixes(Alternating(64))
            .SetDrops(Alternating(64)).SetMisc(Alternating(64));

        StartAt(1);

        Draw(_random.Offers, 3);
        Draw(_random.Affixes, 5);
        Draw(_random.Drops, 7);
        Draw(_random.Misc, 11);

        _events.Clear();

        _recorder.Take(_session.State, 2);

        RandomState expected = _random.Capture();
        RandomState recorded = _events.Single<RunSnapshotTaken>().Snapshot.Random;

        Assert.That(recorded.Spawn, Is.EqualTo(expected.Spawn));
        Assert.That(recorded.Offers, Is.EqualTo(expected.Offers));
        Assert.That(recorded.Affixes, Is.EqualTo(expected.Affixes));
        Assert.That(recorded.Drops, Is.EqualTo(expected.Drops));
        Assert.That(recorded.Misc, Is.EqualTo(expected.Misc));

        // Not five copies of one number, which is what makes the five assertions above worth
        // writing separately.
        Assert.That(recorded.Offers, Is.EqualTo(3UL));
        Assert.That(recorded.Misc, Is.EqualTo(11UL));

        // A capture is a reading, never a draw: taking one must not move what it is reading.
        _recorder.Take(_session.State, 2);

        Assert.That(_random.Capture().Misc, Is.EqualTo(11UL));
    }

    [Test]
    public void Take_VersionIsCurrent()
    {
        Build(OneHuskStage());
        StartAt(1);

        _events.Clear();

        _recorder.Take(_session.State, 2);

        Assert.That(
            _events.Single<RunSnapshotTaken>().Snapshot.Version,
            Is.EqualTo(RunSnapshot.CurrentVersion));
    }

    [Test]
    public void Take_AllocatesNothing()
    {
        Build(OneHuskStage());
        StartAt(1);

        RunState state = _session.State;

        // A silent sink, never RecordingEvents: that one stores each payload in a List<object> and
        // would box every snapshot, so the row would measure the fake (Traps §7). The real
        // DomainEventHub does not box.
        var recorder = new RunRecorder(_random, _clock, new SilentEvents());

        AllocationAssert.None(() => recorder.Take(state, 2));
    }

    // ---- The opening of a run (rules 1, 2, 3, 5) -----------------------------------------------

    [Test]
    public void Session_TakesAtTheOpening()
    {
        Build(OneHuskStage());

        StartAt(1);

        Assert.That(_events.Count<RunSnapshotTaken>(), Is.EqualTo(1));
        Assert.That(_events.Single<RunSnapshotTaken>().Snapshot.StageIndex, Is.EqualTo(1));

        // After RunStarted, and the order is the contract: a run has to be announced before the
        // things inside it are, and a snapshot is one of them.
        Assert.That(
            IndexOf<RunSnapshotTaken>(),
            Is.GreaterThan(IndexOf<RunStarted>()),
            "The snapshot must be announced after the run it describes.");
    }

    [Test]
    public void Session_OpeningStageIsTheConfigsStage()
    {
        Build(OneHuskStage());

        StartAt(7);

        Assert.That(
            _events.Single<RunSnapshotTaken>().Snapshot.StageIndex,
            Is.EqualTo(7),
            "A run that begins at 7 resumes at 7. GD §4.5: nothing is entitled to assume stage 1.");
    }

    [Test]
    public void Session_OpeningPrecedesComposition()
    {
        Build(OneHuskStage());

        RandomState before = _random.Capture();

        // Stage 2 rather than 1, and the reason is worth knowing: at a stage that *introduces* an
        // archetype the composer buys it before any draw (M2-04 rule 5), so a one-Husk stage 1
        // composes without touching the Spawn stream at all — and the second half of this row,
        // "the composition did draw", would be false against perfectly correct code.
        StartAt(2);

        RandomState recorded = _events.Single<RunSnapshotTaken>().Snapshot.Random;

        Assert.That(
            recorded.Spawn,
            Is.EqualTo(before.Spawn),
            "The opening snapshot must carry the streams as they stood before the opening stage "
                + "was composed — a resume restores this position and composes that same stage "
                + "from it (rule 5).");

        // And the composition did draw, which is what stops the assertion above being vacuous.
        Assert.That(
            _random.Capture().Spawn,
            Is.GreaterThan(before.Spawn),
            "Start composes the opening stage, so the Spawn stream has moved since the capture.");
    }

    [Test]
    public void Started_HandlerDrawing_DoesNotMoveTheSnapshot()
    {
        // The rule as the spec states it — "nothing may draw from a RunStarted handler" — is about
        // a capture taken *after* the announcement. This code takes it before, because M2-02 put
        // the opening composition inside Start's validation block so that an ineligible mode throws
        // with nothing announced (ledger row 3, AR §18.1). So a handler that draws lands after the
        // capture rather than inside it, and the resumed run replays the same sequence of
        // operations from the same position: the draw is harmless, and this row is what says so
        // rather than leaving the next reader to assume the opposite.
        Build(OneHuskStage());

        RandomState before = _random.Capture();

        var watcher = new WatchingEvents(_events);
        watcher.On<RunStarted>(_ => _random.Spawn.NextFloat());

        RunSession session = SessionOver(watcher);

        session.Start(Config(1));

        Assert.That(
            _events.Single<RunSnapshotTaken>().Snapshot.Random.Spawn,
            Is.EqualTo(before.Spawn),
            "The opening capture precedes both the composition and the announcement, so nothing a "
                + "RunStarted handler does can reach it.");
    }

    // ---- The stage boundary (rules 1, 3, 5, 6) -------------------------------------------------

    [Test]
    public void Session_TakesOnEnteringClear()
    {
        Build(OneHuskStage());
        StartAt(1);

        Assert.That(_events.Count<RunSnapshotTaken>(), Is.EqualTo(1), "The opening write.");

        ClearTheStage();

        Assert.That(
            _events.Count<RunSnapshotTaken>(),
            Is.EqualTo(2),
            "The boundary write lands on the same tick as StageCleared — ClearTheStage stops the "
                + "instant that event appears.");

        RunSnapshot snapshot = _events.Of<RunSnapshotTaken>()[1].Snapshot;

        Assert.That(snapshot.StageIndex, Is.EqualTo(2), "The stage about to be played, not the one cleared.");
        Assert.That(_events.Single<StageCleared>().Stage, Is.EqualTo(1));
    }

    [Test]
    public void Session_TakesOncePerStage()
    {
        Build(OneHuskStage());
        StartAt(1);
        ClearTheStage();

        // Through the rest of Clear and deep into the Gate, which waits for as long as the player
        // likes. The door is eighteen metres away and the player never moves.
        TickFor(200);

        Assert.That(
            _events.Count<RunSnapshotTaken>(),
            Is.EqualTo(2),
            "The edge into Clear is the write point, not the phase: a stage parked at its own door "
                + "must not write once a frame.");
    }

    [Test]
    public void Session_TakesAgainAtTheNextBoundary()
    {
        Build(OneHuskStage());
        StartAt(1);

        ClearTheStage();
        CrossTheBoundary();
        ClearTheStage();

        IReadOnlyList<RunSnapshotTaken> taken = _events.Of<RunSnapshotTaken>();

        Assert.That(taken, Has.Count.EqualTo(3));
        Assert.That(taken[0].Snapshot.StageIndex, Is.EqualTo(1));
        Assert.That(taken[1].Snapshot.StageIndex, Is.EqualTo(2));
        Assert.That(taken[2].Snapshot.StageIndex, Is.EqualTo(3));
    }

    [Test]
    public void Session_DoesNotTakeOnALaterArrival()
    {
        Build(OneHuskStage());
        StartAt(1);
        ClearTheStage();

        int atTheDoor = _events.Count<RunSnapshotTaken>();

        CrossTheBoundary();

        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(2), "The fixture failed to cross.");
        Assert.That(
            _events.Count<RunSnapshotTaken>(),
            Is.EqualTo(atTheDoor),
            "Stage 2's snapshot was written at stage 1's Clear. Writing again on arrival would be "
                + "a second file describing the same stage, one composition later.");
    }

    [Test]
    public void Snapshot_PrecedesEveryDrawForTheNextStage()
    {
        Build(OneHuskStage());
        StartAt(1);
        ClearTheStage();

        RandomState atClear = _events.Of<RunSnapshotTaken>()[1].Snapshot.Random;

        Assert.That(_random.Capture().Spawn, Is.EqualTo(atClear.Spawn));

        // Every tick of Clear, Gate and Transition, up to and including the one that crosses.
        // Nothing in any of them may draw, or the position the snapshot carries is not the position
        // the resumed run will compose from.
        for (int i = 0; i < 600 && _events.Count<StageArrived>() < 2; i++)
        {
            Assert.That(
                _random.Capture().Spawn,
                Is.EqualTo(atClear.Spawn),
                "Something drew between the capture and the recompose.");

            _session.Tick(Snapshot(Frame, Door));
        }

        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(2), "The fixture failed to cross.");

        // And the crossing itself did draw — the composition is what the Spawn stream is for.
        Assert.That(_random.Capture().Spawn, Is.GreaterThan(atClear.Spawn));
    }

    [Test]
    public void Cleared_HandlerDrawing_IsRefused()
    {
        // The window is the clearing tick: the capture is taken immediately after StageCleared is
        // published, so a draw from a handler lands between the recompose's starting position and
        // the position written down. Everything else about that tick draws nothing, which is what
        // the first half of this row proves.
        Build(OneHuskStage());

        var watcher = new WatchingEvents(_events);
        var guard = new GuardedRandom(_random);

        RunSession clean = SessionOver(watcher, guard);

        clean.Start(Config(1));

        KillTheWave(clean);

        guard.Refuse = true;

        Assert.DoesNotThrow(
            () => TickUntil(clean, () => _events.Count<StageCleared>() == 1),
            "Nothing in an ordinary clearing tick draws, so the guard must not fire on its own.");

        // And now the same tick with a subscriber that draws once.
        Build(OneHuskStage());

        var watching = new WatchingEvents(_events);
        var armed = new GuardedRandom(_random);

        watching.On<StageCleared>(_ => armed.Spawn.NextFloat());

        RunSession session = SessionOver(watching, armed);

        session.Start(Config(1));

        KillTheWave(session);

        armed.Refuse = true;

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => TickUntil(session, () => _events.Count<StageCleared>() == 1));

        Assert.That(refused.Message, Does.Contain("draw"));
    }

    [Test]
    public void Session_ModeCompleteWritesNothing()
    {
        Build(FinalStage(3));
        StartAt(3);

        Assert.That(_events.Count<RunSnapshotTaken>(), Is.EqualTo(1), "The opening write.");

        ClearTheStage();

        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1), "A finite mode out of stages ends the run.");
        Assert.That(
            _events.Count<RunSnapshotTaken>(),
            Is.EqualTo(1),
            "A snapshot of a finished run would be a resume into a stage the mode does not have.");
    }

    // ---- The property all of it exists for (rule 5) --------------------------------------------

    [Test]
    public void Resume_RecomposesIdenticalWaves()
    {
        const int Seed = 4_242;

        // Run A: begun at stage 3, cleared, and walked through the door into stage 4.
        Build(RealCurves(), seed: Seed);
        StartAt(3);
        ClearTheStage();

        RunSnapshot snapshot = _events.Of<RunSnapshotTaken>()[1].Snapshot;

        Assert.That(snapshot.StageIndex, Is.EqualTo(4));

        CrossTheBoundary();

        // Long enough for the whole of stage 4's first wave to arrive, and nothing is killed — so
        // the second wave cannot start in either run and both see exactly the same population.
        TickFor(600);

        IReadOnlyList<string> continuous = BodiesOfTheLastStage();

        Assert.That(continuous, Is.Not.Empty, "The fixture failed to land stage 4's first wave.");

        // Run B: a second run seeded the same way, put back where the snapshot says the streams
        // stood, and started at the stage the snapshot names. Everything else about it is fresh.
        Build(RealCurves(), seed: Seed);

        _random.Restore(snapshot.Random);

        StartAt(snapshot.StageIndex);
        TickFor(600);

        IReadOnlyList<string> resumed = BodiesOfTheLastStage();

        Assert.That(
            resumed,
            Is.EqualTo(continuous),
            "A resumed run must fight the stage the interrupted one was about to fight — same "
                + "archetypes, same positions, in the same order. This is the whole of ledger "
                + "row 1.");
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        var random = new FixedRandom(0);
        var clock = new FixedClock(Instant);
        var events = new RecordingEvents();

        Assert.Throws<ArgumentNullException>(() => new RunRecorder(null, clock, events));
        Assert.Throws<ArgumentNullException>(() => new RunRecorder(random, null, events));
        Assert.Throws<ArgumentNullException>(() => new RunRecorder(random, clock, null));
    }

    [Test]
    public void Take_NullState_Throws()
    {
        Build(OneHuskStage());

        Assert.Throws<ArgumentNullException>(() => _recorder.Take(null, 1));
        Assert.Throws<ArgumentNullException>(() => _recorder.Take(null, 1, default));
    }

    [Test]
    public void Take_StageBelowOne_Throws()
    {
        Build(OneHuskStage());
        StartAt(1);

        // From RunSnapshot's own guard rather than a second copy of it here: stages are numbered
        // from 1, and one rule in one place cannot disagree with itself (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => _recorder.Take(_session.State, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _recorder.Take(_session.State, -1));
    }

    // ---- Fixture -------------------------------------------------------------------------------

    /// <summary>Builds a whole run-sized world: a catalog, a generator, a clock and a session.</summary>
    private void Build(ModeSpec mode, CharacterSpec character = null, int seed = 0)
    {
        _events = new RecordingEvents();

        // Long enough that no row here can exhaust the script and start reading the fake's default
        // 0.5f, which would make every position after that point stop moving.
        _random = new FixedRandom(seed, Alternating(8_192));
        _clock = new FixedClock(Instant);
        _mode = mode;

        _catalog = new ContentCatalog(
            new[] { character ?? Oathbound(shield: true) },
            new[] { Husk(), TheExecutioner() },
            new[] { mode });

        _recorder = new RunRecorder(_random, _clock, _events);

        _session = new RunSession(
            _catalog,
            _random,
            _events,
            new RecordingIntents(),
            _recorder,
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    /// <summary>A second session over this fixture's world, publishing through <paramref name="events"/>.</summary>
    private RunSession SessionOver(IDomainEvents events, IRandom random = null)
    {
        IRandom generator = random ?? _random;

        return new RunSession(
            _catalog,
            generator,
            events,
            new RecordingIntents(),
            new RunRecorder(generator, _clock, events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private void StartAt(int stage, Vector3? executionerAt = null)
    {
        _session.Start(Config(stage, executionerAt));
    }

    private RunConfig Config(int stage, Vector3? executionerAt = null) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        _random.Seed,
        stage,
        new SpawnPlan(
            executionerAt is null
                ? Array.Empty<SpawnPlan.Entry>()
                : new[] { new SpawnPlan.Entry(new ContentId(ExecutionerId), executionerAt.Value) }));

    private void TickFor(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _session.Tick(Snapshot(Frame, Vector3.Zero));
        }
    }

    private static WorldSnapshot Snapshot(float dt, Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = dt,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    /// <summary>
    /// Plays the whole stage out — every body of every wave — and stops on the tick
    /// <c>StageCleared</c> lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every body, not the first one.</b> A flat mode delivers one Husk and a real budget curve
    /// delivers nine, so a helper that killed one and then waited would time out on exactly the
    /// rows that need a real composition.
    /// </para>
    /// <para>
    /// The kills go through <c>ReportConeHits</c>, which is the only route a core test has: a
    /// session's <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c> on
    /// <c>RunState</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>,
    /// deliberately. So the player is walked onto each body in turn and the weapon swings at what
    /// is under its nose, and the fixture answers the swing the way Unity would.
    /// </para>
    /// <para>
    /// The target is read once, before the loop: a predicate that recomputed <c>count + 1</c> on
    /// every evaluation would never be satisfied, and the row would fail somewhere else entirely.
    /// </para>
    /// </remarks>
    private void ClearTheStage()
    {
        int target = _events.Count<StageCleared>() + 1;

        // Spawns and deaths advance together here — every body in this fixture dies to a reported
        // cone hit and nothing else — so one index walks both lists.
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

            _session.Tick(Snapshot(Frame, where));

            // The tick that clears the stage can also be the tick that ends the run, for a finite
            // mode out of stages. Reporting a hit into an ended run throws, and correctly so.
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

    /// <summary>Waits out the clear beat, walks into the door and lets the fade run out.</summary>
    private void CrossTheBoundary()
    {
        int arrivals = _events.Count<StageArrived>();

        for (int i = 0; i < 900 && _events.Count<StageArrived>() == arrivals; i++)
        {
            _session.Tick(Snapshot(Frame, Door));
        }

        Assert.That(
            _events.Count<StageArrived>(),
            Is.GreaterThan(arrivals),
            "The fixture failed to cross the boundary, so whatever this row asserts next is about "
                + "the fixture rather than about the run.");
    }

    /// <summary>
    /// Stands the stage's one body up and cuts it down, leaving the stage a sweep from complete.
    /// </summary>
    /// <remarks>
    /// The kill goes through <c>ReportConeHits</c>, which is the only route a core test has: a
    /// session's <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c> on
    /// <c>RunState</c>. So the player is walked onto the body and the weapon swings at what is
    /// under its nose, and the fixture answers the swing the way Unity would.
    /// </remarks>
    private void KillTheWave(RunSession session)
    {
        int before = _events.Count<EnemySpawned>();
        int deaths = _events.Count<EnemyDied>();

        for (int i = 0; i < 900 && _events.Count<EnemySpawned>() == before; i++)
        {
            session.Tick(Snapshot(Frame, Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before), "The fixture failed to land the wave.");

        EnemySpawned spawned = _events.Of<EnemySpawned>()[before];
        int[] report = { spawned.Id };

        for (int i = 0; i < 900 && _events.Count<EnemyDied>() == deaths; i++)
        {
            session.Tick(Snapshot(Frame, spawned.Position));
            session.ReportConeHits(report);
        }

        Assert.That(_events.Count<EnemyDied>(), Is.GreaterThan(deaths), "The fixture failed to kill the wave.");
    }

    /// <summary>Ticks until <paramref name="until"/> answers true, standing on the corpse.</summary>
    private void TickUntil(RunSession session, Func<bool> until)
    {
        for (int i = 0; i < 900 && !until(); i++)
        {
            session.Tick(Snapshot(Frame, Vector3.Zero));
        }

        Assert.That(until(), Is.True, "The fixture ran out of ticks before the thing it was waiting for.");
    }

    /// <summary>
    /// Every body spawned since the most recent <c>StageArrived</c>, as archetype-and-position
    /// strings — what a wave plan turns into, which is the only part of it a session lets a test
    /// see.
    /// </summary>
    private IReadOnlyList<string> BodiesOfTheLastStage()
    {
        var bodies = new List<string>();

        int arrivals = 0;

        foreach (object published in _events.All)
        {
            if (published is StageArrived)
            {
                arrivals++;
                bodies.Clear();
                continue;
            }

            if (published is EnemySpawned spawned)
            {
                bodies.Add($"{spawned.SpecId} @ {spawned.Position}");
            }
        }

        Assert.That(arrivals, Is.GreaterThan(0), "No stage ever arrived.");

        return bodies;
    }

    /// <summary>Where <typeparamref name="T"/> was first published, as an index into the whole log.</summary>
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

    private static void Draw(IRandomStream stream, int times)
    {
        for (int i = 0; i < times; i++)
        {
            stream.NextFloat();
        }
    }

    // ---- Content -------------------------------------------------------------------------------

    /// <summary>A stage of exactly one Husk: one wave, one body, and a budget that buys it.</summary>
    private static ModeSpec OneHuskStage() => Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap);

    /// <summary>The same, but finite and out of stages after <paramref name="finalStage"/>.</summary>
    private static ModeSpec FinalStage(int finalStage) =>
        Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap, endless: false, finalStage: finalStage);

    /// <summary>
    /// GD §12's curves as the game ships them, for the row that compares two compositions.
    /// </summary>
    /// <remarks>
    /// A flat mode would compose the same one body at every depth, and two runs agreeing about one
    /// Husk is not evidence that a stream was restored. This one grows a budget with depth, so
    /// stage 4 is several bodies of a real composition.
    /// </remarks>
    private static ModeSpec RealCurves() => Mode(
        budget: HuskCost,
        waves: 2,
        concurrency: DeviceCap,
        curve: new BudgetCurve(20f, 6f, 0.04f));

    private static ModeSpec Mode(
        float budget,
        int waves,
        int concurrency,
        bool endless = true,
        int finalStage = 0,
        BudgetCurve? curve = null)
    {
        var scaling = new ScalingSpec(
            curve ?? new BudgetCurve(budget, 0f, 0f),
            new WaveCurve(waves, 1000, waves, waves),
            new ConcurrencyCurve(concurrency, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: endless,
            finalStage: finalStage,
            scaling,
            new[] { new RosterEntry(new ContentId(HuskId), 1) });
    }

    /// <summary>
    /// The Husk, authored <c>Static</c> on purpose: these rows are about when a run writes itself
    /// down, and a Chaser would walk into the player during the beats they are timing.
    /// </summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>
    /// A Husk that reaches four metres and hits hard enough to be seen, for the one row that needs
    /// the player to have been hurt. <c>StageFlowTests</c>' shape, tuned not to kill.
    /// </summary>
    private static EnemySpec TheExecutioner() => new EnemySpec(
        new ContentId(ExecutionerId),
        new LocKey("enemy.executioner.name"),
        maxHp: 1_000f,
        moveSpeed: 0.01f,
        targetPriority: 1,
        threatCost: 40,
        isElite: false,
        contactDamage: 16f,
        reach: 4f,
        windupTime: 0.05f,
        recoverTime: 5f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// The class every row plays. CC §7's numbers, plus an Aegis, which is the one thing about it
    /// this fixture actually cares about.
    /// </summary>
    private static CharacterSpec Oathbound(bool shield = true) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it would
        // put a modifier and a stream of events into fixtures measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        shield ? new ShieldSpec(ShieldMax, 3f, 1f) : null);

    /// <summary><paramref name="count"/> points on a ring, clear of the origin and of each other.</summary>
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

    /// <summary><paramref name="count"/> values alternating between the first and last candidate.</summary>
    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>
    /// A <see cref="IDomainEvents"/> that records like <see cref="RecordingEvents"/> and also hands
    /// each payload to whoever asked for that type — a subscriber, without a hub.
    /// </summary>
    /// <remarks>
    /// Nested and private because it exists for the two rows that need to <em>do something</em>
    /// from inside a publish. <c>DomainEventHub</c> is the real thing and lives in
    /// <c>Soulvail.Game</c>, which this assembly does not reference and must not.
    /// </remarks>
    private sealed class WatchingEvents : IDomainEvents
    {
        private readonly RecordingEvents _log;
        private readonly Dictionary<Type, Action<object>> _handlers = new();

        public WatchingEvents(RecordingEvents log)
        {
            _log = log;
        }

        public void On<T>(Action<T> handler)
            where T : struct
        {
            _handlers[typeof(T)] = boxed => handler((T)boxed);
        }

        public void Publish<T>(in T evt)
            where T : struct
        {
            _log.Publish(in evt);

            if (_handlers.TryGetValue(typeof(T), out Action<object> handler))
            {
                handler(evt);
            }
        }
    }

    /// <summary>
    /// A generator that can be told to refuse every draw, so that "nothing draws here" is a
    /// failure rather than something a row has to infer from a number that did not move.
    /// </summary>
    /// <remarks>
    /// It forwards <see cref="Capture"/> and <see cref="Restore"/> unrefused: a capture is a
    /// reading of where a stream stands, not a draw out of it, and refusing one would break the
    /// very write point this fixture is guarding.
    /// </remarks>
    private sealed class GuardedRandom : IRandom
    {
        private readonly IRandom _inner;
        private readonly GuardedStream _spawn;
        private readonly GuardedStream _offers;
        private readonly GuardedStream _affixes;
        private readonly GuardedStream _drops;
        private readonly GuardedStream _misc;

        public GuardedRandom(IRandom inner)
        {
            _inner = inner;

            _spawn = new GuardedStream(this, inner.Spawn, nameof(Spawn));
            _offers = new GuardedStream(this, inner.Offers, nameof(Offers));
            _affixes = new GuardedStream(this, inner.Affixes, nameof(Affixes));
            _drops = new GuardedStream(this, inner.Drops, nameof(Drops));
            _misc = new GuardedStream(this, inner.Misc, nameof(Misc));
        }

        /// <summary>While true, any draw throws.</summary>
        public bool Refuse { get; set; }

        public int Seed => _inner.Seed;

        public IRandomStream Spawn => _spawn;

        public IRandomStream Offers => _offers;

        public IRandomStream Affixes => _affixes;

        public IRandomStream Drops => _drops;

        public IRandomStream Misc => _misc;

        public RandomState Capture() => _inner.Capture();

        public void Restore(in RandomState state) => _inner.Restore(in state);

        private sealed class GuardedStream : IRandomStream
        {
            private readonly GuardedRandom _owner;
            private readonly IRandomStream _inner;
            private readonly string _name;

            public GuardedStream(GuardedRandom owner, IRandomStream inner, string name)
            {
                _owner = owner;
                _inner = inner;
                _name = name;
            }

            public float NextFloat()
            {
                Check();
                return _inner.NextFloat();
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                Check();
                return _inner.NextInt(minInclusive, maxExclusive);
            }

            public float Range(float minInclusive, float maxInclusive)
            {
                Check();
                return _inner.Range(minInclusive, maxInclusive);
            }

            public bool Chance(float probability)
            {
                Check();
                return _inner.Chance(probability);
            }

            private void Check()
            {
                if (!_owner.Refuse)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"A draw from the {_name} stream landed between a snapshot's capture and the "
                        + "composition it must precede. Nothing may draw there — the resumed run "
                        + "would start one draw further on than a continuous one.");
            }
        }
    }
}
