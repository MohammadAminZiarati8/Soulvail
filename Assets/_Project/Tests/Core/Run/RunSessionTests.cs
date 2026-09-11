using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// The whole of M0's brain, and with it every contract M0-09 wrote and left untested: the port's
/// three methods, <c>RunConfig</c>'s guard, <c>RunState</c>'s fields and both run events.
/// </summary>
/// <remarks>
/// The state is only ever reached through <c>session.State</c> and only ever read, which is what
/// lets <c>RunState</c>'s constructor and setters stay <c>internal</c> with no
/// <c>InternalsVisibleTo</c> anywhere. A test that needs to *build* a run state — M2-14's resume
/// flow is the likely first — is a decision to make on purpose, not by adding an attribute here.
/// </remarks>
[TestFixture]
public sealed class RunSessionTests
{
    private const string OathboundId = "character.oathbound";

    private const string DescentId = "mode.descent";

    /// <summary>An id of the right shape that the catalog does not hold.</summary>
    private const string UnknownId = "character.nobody";

    private const string HuskId = "enemy.husk";

    /// <summary>An archetype id of the right shape that no fixture here authors.</summary>
    private const string GhostId = "enemy.ghost";

    private const int Seed = 99;

    /// <summary>
    /// Small on purpose. Every row here is about the player, and the enemy system a run now
    /// composes needs only to exist — M1-06's own fixture is where its capacity matters.
    /// </summary>
    private const int EnemyCapacity = 8;

    /// <summary>
    /// The device cap a run composes its stages under (M2-05). Equal to the capacity here, which
    /// is the largest it is allowed to be: a stage may not be allowed more bodies than the
    /// snapshot can carry back.
    /// </summary>
    private const int DeviceCap = 8;

    /// <summary>
    /// Room for every shot a row here puts in the air, which is none: nothing fires one until
    /// M2-07b. Required by <c>RunSession</c> since M2-07a, and guarded positive, so it is a
    /// number rather than a zero.
    /// </summary>
    private const int ProjectileCapacity = 8;

    // The Oathbound's numbers (CC §7), so a failure reads as "the class we ship stopped moving".
    private const float Speed = 5.4f;
    private const float AccelTime = 0.06f;
    private const float DecelTime = 0.08f;
    private const float TurnSpeedDeg = 720f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private FixedRandom _random;
    private ContentCatalog _catalog;
    private RunSession _session;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _random = new FixedRandom(Seed);
        _catalog = new ContentCatalog(new[] { Oathbound() }, null, new[] { Descent() });
        _session = new RunSession(_catalog, _random, _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);
    }

    [Test]
    public void Config_DefaultId_Throws()
    {
        // Built in M0-09, tested here because that task was contracts with no fixture of its own.
        // `default(ContentId)` means nobody chose a class — a composition mistake — so it is
        // named here rather than left to surface as the catalog's "no character with id ''".
        Assert.Throws<ArgumentException>(
            () => new RunConfig(new ContentId(DescentId), default, Seed, 1, SpawnPlan.Empty));

        // The same guard on the mode, added with the field in M2-02: nobody chose a mode is the
        // same kind of mistake as nobody chose a class, and GD §4.5's whole point is that Descent
        // is not a default anything is entitled to assume.
        Assert.Throws<ArgumentException>(
            () => new RunConfig(default, new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty));

        // The other half of the pair, and the reason the guard is narrow: a well-formed id the
        // catalog happens not to hold is missing *content*, which is the catalog's question to
        // answer at Start. See Start_UnknownCharacter_Throws_NotRunning.
        Assert.DoesNotThrow(
            () => new RunConfig(
                new ContentId(DescentId), new ContentId(UnknownId), Seed, 1, SpawnPlan.Empty));
    }

    [Test]
    public void Config_RecordsAllFive()
    {
        var plan = new SpawnPlan(Array.Empty<SpawnPlan.Entry>());

        var config = new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), -7, 4, plan);

        Assert.That(config.ModeId, Is.EqualTo(new ContentId(DescentId)));
        Assert.That(config.CharacterId, Is.EqualTo(new ContentId(OathboundId)));

        // Negative on purpose. Every int is a legal seed — it is a bit pattern, not a quantity —
        // so there is nothing here for a guard to reject and a test that only ever passed 99
        // would not say so.
        Assert.That(config.Seed, Is.EqualTo(-7));
        Assert.That(config.StageIndex, Is.EqualTo(4));
        Assert.That(config.SpawnPlan, Is.SameAs(plan));
    }

    [Test]
    public void Config_StageBelowOne_Throws()
    {
        // Stages are numbered from 1 (GD §8.2), so a zero is a caller that meant "the first one"
        // and reached for an array index. Caught where the number was chosen rather than at the
        // depth scaling that would quietly compute a stage-zero curve from it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed, 0, SpawnPlan.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed, -1, SpawnPlan.Empty));
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        // Beyond the spec's Tests table. The constructor is public and called from another
        // assembly (M0-12's RunInstaller), so it is a boundary; without these a forgotten
        // registration would surface a frame later, inside Tick, as an NRE that names the tick.
        Assert.Throws<ArgumentNullException>(() => new RunSession(null, _random, _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, null, _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, _random, null, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, _random, _events, null, EnemyCapacity, DeviceCap, ProjectileCapacity));

        // Same family, added with the capacity in M1-06: a registry that can hold no enemies is a
        // configuration mistake, and checking it here rather than at the first Start means a
        // mis-wired scope fails while it is being built instead of one scene later.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunSession(_catalog, _random, _events, _intents, 0, DeviceCap, ProjectileCapacity));

        // The device cap joins the family in M2-05, and it owes two checks rather than one. Zero
        // is the same mistake as a zero capacity — every stage would compose an empty arena.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunSession(_catalog, _random, _events, _intents, EnemyCapacity, 0, ProjectileCapacity));

        // And a cap above the capacity is the mistake that would otherwise be silent: the stage
        // composes more bodies than the snapshot can carry back, so the surplus exists in core and
        // core is blind to where any of it is standing.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunSession(
                _catalog, _random, _events, _intents, EnemyCapacity, EnemyCapacity + 1, ProjectileCapacity));

        // The projectile capacity joins the family in M2-07a, and it owes only the zero check: it
        // is bounded by nothing the snapshot carries, because a shot has no body and is never
        // reported back in. A run allowed none would refuse every bolt in silence.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunSession(_catalog, _random, _events, _intents, EnemyCapacity, DeviceCap, 0));
    }

    [Test]
    public void Config_NullSpawnPlan_Throws()
    {
        // Required rather than optional (M1-06): a run that starts empty says so with
        // SpawnPlan.Empty. An omitted plan and a broken spawner look identical in a playtest.
        Assert.Throws<ArgumentNullException>(
            () => new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, null));
    }

    [Test]
    public void Start_PublishesRunStarted_WithSeed()
    {
        StartRun();

        RunStarted started = _events.Single<RunStarted>();

        Assert.That(started.CharacterId, Is.EqualTo(new ContentId(OathboundId)));

        // The seed now comes out of the config rather than off IRandom (M2-02). Still one truth:
        // Start refuses a config that disagrees with the generator, so the number a bug report
        // quotes is still provably the one the streams draw from — see Start_SeedMismatch_Throws.
        Assert.That(started.Seed, Is.EqualTo(Seed));
        Assert.That(_session.IsRunning, Is.True);
    }

    [Test]
    public void Start_RecordsConfigSeed()
    {
        StartRun();

        Assert.That(_session.State.Seed, Is.EqualTo(Seed));
        Assert.That(_events.Single<RunStarted>().Seed, Is.EqualTo(Seed));
    }

    [Test]
    public void Start_SeedMismatch_Throws()
    {
        // The generator was built with 99 and the config states 100. The only way that happens is
        // a composition mistake, and the symptom of letting it through would be a run whose
        // recorded seed does not replay it — which is the one number worth having in a bug report.
        Assert.Throws<ArgumentException>(
            () => _session.Start(new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed + 1, 1, SpawnPlan.Empty)));

        Assert.That(_session.IsRunning, Is.False);
        Assert.That(_session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_RecordsModeAndStage()
    {
        _session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 4, SpawnPlan.Empty));

        Assert.That(_session.State.ModeId, Is.EqualTo(new ContentId(DescentId)));

        // 4, not the mode's StartingStage of 1: a run begins where its caller says, which is what
        // makes M2-14b's resume a different argument rather than a different code path.
        Assert.That(_session.State.StageIndex, Is.EqualTo(4));
    }

    [Test]
    public void Start_UnknownMode_Throws_NotRunning()
    {
        Assert.Throws<KeyNotFoundException>(
            () => _session.Start(new RunConfig(
                new ContentId("mode.nothing"), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty)));

        Assert.That(_session.IsRunning, Is.False);
        Assert.That(_session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_StageNotInMode_Throws()
    {
        // A finite mode, so there is a stage past the end to ask for. Descent is endless and has
        // every stage from 1, which is exactly why this row cannot use it.
        var finite = new ModeSpec(
            new ContentId("mode.trial"),
            new LocKey("mode.trial.name"),
            1,
            false,
            5,
            Scalings.Design(),
            Array.Empty<RosterEntry>());

        var catalog = new ContentCatalog(new[] { Oathbound() }, null, new[] { finite });
        var session = new RunSession(catalog, _random, _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.Start(new RunConfig(
                new ContentId("mode.trial"), new ContentId(OathboundId), Seed, 6, SpawnPlan.Empty)));

        Assert.That(session.IsRunning, Is.False);
        Assert.That(session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);

        // The last stage it does have starts fine, so the guard is a boundary rather than a ban.
        Assert.DoesNotThrow(
            () => session.Start(new RunConfig(
                new ContentId("mode.trial"), new ContentId(OathboundId), Seed, 5, SpawnPlan.Empty)));
    }

    [Test]
    public void Start_UnknownPlanArchetype_NothingAnnounced()
    {
        // Ledger row 3. Until M2-02 this threw from SpawnAll — after RunStarted and after
        // IsRunning flipped — so the run was announced and half an arena was standing.
        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(GhostId), Vector3.Zero),
        });

        AssertStartRefusedCleanly(plan);
    }

    [Test]
    public void Start_UnknownRespawnArchetype_NothingAnnounced()
    {
        // The plan itself is legal and empty; the stranger is in the policy, which SpawnAll would
        // have adopted without reading and only tripped over a kill later.
        var plan = new SpawnPlan(
            Array.Empty<SpawnPlan.Entry>(),
            new RespawnPolicy(
                new ContentId(GhostId), new[] { Vector3.Zero }, 1, 1f, 0f));

        AssertStartRefusedCleanly(plan);
    }

    [Test]
    public void Start_UnknownRosterArchetype_NothingAnnounced()
    {
        // The half that has no other line of defence: nothing spawns from a roster until M2-05's
        // director does, so without this check the failure would arrive forty seconds into a run
        // looking like a director bug.
        var mode = new ModeSpec(
            new ContentId(DescentId),
            new LocKey("mode.descent.name"),
            1,
            true,
            0,
            Scalings.Design(),
            new[] { new RosterEntry(new ContentId(GhostId), 1) });

        var catalog = new ContentCatalog(new[] { Oathbound() }, null, new[] { mode });
        var session = new RunSession(catalog, _random, _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);

        Assert.Throws<KeyNotFoundException>(
            () => session.Start(new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty)));

        Assert.That(session.IsRunning, Is.False);
        Assert.That(session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_ValidationRunsBeforeState()
    {
        StartRun();
        _session.Tick(Snapshot(1f));
        _session.End();

        RunState finished = _session.State;
        _events.Clear();

        Assert.Throws<KeyNotFoundException>(
            () => _session.Start(new RunConfig(
                new ContentId("mode.nothing"), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty)));

        // The previous run's state is still there and still says what it said. A half-started run
        // that had overwritten State would take the run-end screen's numbers with it.
        Assert.That(_session.State, Is.SameAs(finished));
        Assert.That(_session.State.Time, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_BuildsState()
    {
        StartRun();

        Assert.That(_session.State, Is.Not.Null);
        Assert.That(_session.State.CharacterId, Is.EqualTo(new ContentId(OathboundId)));
        Assert.That(_session.State.Character.Id, Is.EqualTo(new ContentId(OathboundId)));
        Assert.That(_session.State.Character.Movement.Speed, Is.EqualTo(Speed));
        Assert.That(_session.State.Seed, Is.EqualTo(Seed), "The state records what the generator was seeded with.");
        Assert.That(_session.State.Time, Is.EqualTo(0f));
        Assert.That(_session.State.PlayerPosition, Is.EqualTo(Vector3.Zero));

        // +Z exactly: a run starts with the camera behind the character and nothing to aim at.
        Assert.That(_session.State.PlayerFacing, Is.EqualTo(Vector3.UnitZ));
        Assert.That(_session.State.PlayerVelocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Start_WhenRunning_Throws()
    {
        StartRun();

        Assert.Throws<InvalidOperationException>(StartRun);

        Assert.That(_session.IsRunning, Is.True, "The live run survives the rejected Start.");
        Assert.That(_events.Count<RunStarted>(), Is.EqualTo(1), "And the rejected Start announced nothing.");
    }

    [Test]
    public void Start_UnknownCharacter_Throws_NotRunning()
    {
        Assert.Throws<KeyNotFoundException>(
            () => _session.Start(new RunConfig(
                new ContentId(DescentId), new ContentId(UnknownId), Seed, 1, SpawnPlan.Empty)));

        // Nothing half-started: the catalog is read before anything is assigned, so a bad id
        // leaves the session exactly as it was.
        Assert.That(_session.IsRunning, Is.False);
        Assert.That(_session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_NullConfig_Throws()
    {
        // Beyond the spec's Tests table, and the same family as RunConfig's default-id guard: a
        // null config is composition forgetting to choose, not content going missing.
        Assert.Throws<ArgumentNullException>(() => _session.Start(null));

        Assert.That(_session.IsRunning, Is.False);
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Start_EventPublishedBeforeIsRunningFlips()
    {
        var events = new CapturingEvents();
        var session = new RunSession(_catalog, _random, events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);
        object payload = null;
        bool? runningDuringEvent = null;

        events.OnPublish = evt =>
        {
            payload = evt;
            runningDuringEvent = session.IsRunning;
        };

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty));

        Assert.That(payload, Is.InstanceOf<RunStarted>());

        // The run is announced, not yet live. A listener that flipped straight to "running" here
        // could tick or end a run whose composition is still mid-flight.
        Assert.That(runningDuringEvent, Is.False);
        Assert.That(session.IsRunning, Is.True, "…and it is running by the time Start returns.");
    }

    [Test]
    public void Tick_BeforeStart_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _session.Tick(Snapshot(Frame)));

        Assert.That(_intents.PlayerMoves, Is.Empty);
    }

    [Test]
    public void Tick_AdvancesTime_AndStoresPosition()
    {
        StartRun();

        _session.Tick(Snapshot(0.5f, position: new Vector3(1f, 0f, 2f)));

        Assert.That(_session.State.Time, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(_session.State.PlayerPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)));

        // Summed, not assigned — the half of the rule a single tick cannot tell apart. The
        // position, by contrast, is the latest report and replaces the last one.
        _session.Tick(Snapshot(0.25f, position: new Vector3(3f, 0f, 4f)));

        Assert.That(_session.State.Time, Is.EqualTo(0.75f).Within(1e-6f));
        Assert.That(_session.State.PlayerPosition, Is.EqualTo(new Vector3(3f, 0f, 4f)));
    }

    [Test]
    public void Tick_EmitsExactlyOnePlayerMove()
    {
        StartRun();

        _session.Tick(Snapshot(Frame, new Vector2(0f, 1f)));

        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(1));

        // One per tick, not one per run and not one per changed velocity.
        _session.Tick(Snapshot(Frame, new Vector2(0f, 1f)));
        _session.Tick(Snapshot(Frame, new Vector2(0f, 1f)));

        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(3));
    }

    [Test]
    public void Tick_ZeroInput_StillEmitsIntent()
    {
        StartRun();

        _session.Tick(Snapshot(Frame));

        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(1));
        Assert.That(_intents.LastPlayerMove.Velocity, Is.EqualTo(Vector3.Zero));

        // The rule's real payload. Released mid-speed, the body still needs a velocity every
        // frame — the decelerating one. A session that skipped the intent when the stick was
        // centred would leave the view applying whatever it last read, and the character would
        // slide at full speed forever.
        TickFor(4, new Vector2(0f, 1f));
        _intents.Clear();

        _session.Tick(Snapshot(Frame));

        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(1));
        Assert.That(_intents.LastPlayerMove.Velocity.Z, Is.GreaterThan(0f), "Still decelerating, not snapped to rest.");
        Assert.That(_intents.LastPlayerMove.Velocity.Z, Is.LessThan(Speed));
    }

    [Test]
    public void Tick_FullInput_IntentMatchesMotor()
    {
        StartRun();

        // A full second at 1/120 s, far past the 0.06 s ramp.
        TickFor(120, new Vector2(0f, 1f));

        PlayerMoveIntent last = _intents.LastPlayerMove;

        Assert.That(last.Velocity.Z, Is.EqualTo(Speed).Within(1e-3f));
        Assert.That(last.Velocity.X, Is.EqualTo(0f).Within(1e-6f));
        Assert.That(last.Facing.Z, Is.EqualTo(1f).Within(1e-3f));

        // Carried from the motor rather than recomputed: the intent is what core already decided,
        // and deriving either vector a second time is how the two drift apart.
        Assert.That(last.Velocity, Is.EqualTo(_session.State.PlayerVelocity));
        Assert.That(last.Facing, Is.EqualTo(_session.State.PlayerFacing));
    }

    [Test]
    public void End_PublishesRunEnded_WithTime()
    {
        StartRun();
        _session.Tick(Snapshot(1.5f));
        _session.Tick(Snapshot(0.5f));

        _session.End();

        Assert.That(_events.Single<RunEnded>().Time, Is.EqualTo(2f).Within(1e-6f));
        Assert.That(_session.IsRunning, Is.False);

        // Readable afterwards, holding the run that just finished — M4-06's run-end screen reads
        // its numbers from here rather than being handed a copy.
        Assert.That(_session.State, Is.Not.Null);
        Assert.That(_session.State.Time, Is.EqualTo(2f).Within(1e-6f));

        // Rule 4's other half: "not running" is not running, however it was arrived at.
        Assert.Throws<InvalidOperationException>(() => _session.Tick(Snapshot(Frame)));
    }

    [Test]
    public void End_WhenNotRunning_IsNoOp()
    {
        Assert.DoesNotThrow(() => _session.End());

        Assert.That(_events.All, Is.Empty);
        Assert.That(_session.IsRunning, Is.False);

        // The case that matters at teardown: RunScope disposing after a run has already ended
        // must not announce a second ending to everyone still subscribed.
        StartRun();
        _session.End();
        _events.Clear();

        _session.End();

        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void End_ThenStart_CreatesNewState()
    {
        StartRun();
        _session.Tick(Snapshot(1f));
        RunState first = _session.State;
        _session.End();

        StartRun();

        Assert.That(_session.State, Is.Not.SameAs(first), "A second run is a new state, not a reset one.");
        Assert.That(_session.State.Time, Is.EqualTo(0f));
        Assert.That(_session.IsRunning, Is.True);
        Assert.That(first.Time, Is.EqualTo(1f).Within(1e-6f), "And the finished run's state is left alone.");
    }

    [Test]
    public void End_EventPublishedBeforeIsRunningFlips()
    {
        // Beyond the spec's Tests table, pinning the second half of a symmetry the code
        // documents: during either lifecycle event the session still reports the state it is
        // leaving, so a handler reading IsRunning gets a consistent answer at both ends.
        var events = new CapturingEvents();
        var session = new RunSession(_catalog, _random, events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);
        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty));

        object payload = null;
        bool? runningDuringEvent = null;

        events.OnPublish = evt =>
        {
            payload = evt;
            runningDuringEvent = session.IsRunning;
        };

        session.End();

        Assert.That(payload, Is.InstanceOf<RunEnded>());
        Assert.That(runningDuringEvent, Is.True);
        Assert.That(session.IsRunning, Is.False);
    }

    [Test]
    public void Start_OrderUnchanged()
    {
        // AR §18.1, re-asserted because M2-02 moved everything *before* RunStarted and this is
        // the half that must not have moved: the run is announced first, IsRunning flips second,
        // and the opening population is spawned into a run that is already live.
        var catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Descent() });
        var events = new RecordingEvents();
        var session = new RunSession(catalog, _random, events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);

        var plan = new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(3f, 0f, 0f)),
        });

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, plan));

        Assert.That(events.All[0], Is.InstanceOf<RunStarted>());
        Assert.That(events.All[1], Is.InstanceOf<EnemySpawned>());
        Assert.That(events.All.Count, Is.EqualTo(2));
    }

    [Test]
    public void Start_IsRunningIsTrueInsideSpawnHandler()
    {
        // The other half of the same rule, and the one a recorded list cannot show: a handler that
        // resolves the session from inside EnemySpawned finds a live run, not one mid-composition.
        var catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Descent() });
        var events = new CapturingEvents();
        var session = new RunSession(catalog, _random, events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);
        bool? runningDuringSpawn = null;

        events.OnPublish = evt =>
        {
            if (evt is EnemySpawned)
            {
                runningDuringSpawn = session.IsRunning;
            }
        };

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(new[]
            {
                new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(3f, 0f, 0f)),
            })));

        Assert.That(runningDuringSpawn, Is.True);
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        StartRun();
        WorldSnapshot snapshot = Snapshot(Frame, new Vector2(0.7f, 0.7f), new Vector3(1f, 0f, 2f));

        // The recorder is grown past the measured window first and then cleared: List<T> keeps
        // its capacity across Clear, so the adds inside the measurement cannot be the thing that
        // allocates. Without this the test would be measuring the fake doubling its array, and
        // the real sink — M0-06's IntentBuffer — has no array to double.
        for (int i = 0; i < 20_000; i++)
        {
            _session.Tick(snapshot);
        }

        _intents.Clear();

        AllocationAssert.None(() => _session.Tick(snapshot));
    }

    private static CharacterSpec Oathbound() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        100f,
        new MovementSpec(Speed, AccelTime, DecelTime, TurnSpeedDeg),
        // Required as of M1-03, and irrelevant to every row in this fixture: the run session
        // does not target anything yet. CC §7's numbers rather than invented ones, so a future
        // row that does care starts from the real class.
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        // Required as of M1-10, and irrelevant here for the same reason one step along: with no
        // enemies in any of these rows there is never a target, so the weapon never swings.
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        // Required as of M1-13, and switched off with a MaxMultiplier of 1 for the same reason
        // one step along: these rows tick a stick that is usually centred, and a ramp would put
        // a modifier and a stream of events into a fixture measuring neither — including the
        // allocation row, which is about what one Tick costs when nothing is happening.
        new FocusSpec(0.4f, 1f, 1f),
        // Required as of M1-14, and irrelevant here for the same reason again: no row in this
        // fixture presses anything, so the dash never starts and the allocation row stays a
        // measurement of an idle Tick.
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    /// <summary>One snapshot per call, filled the way M0-16's builder will fill its single one.</summary>
    private static WorldSnapshot Snapshot(float dt, Vector2 input = default, Vector3 position = default)
    {
        var snapshot = new WorldSnapshot(8);
        snapshot.Dt = dt;
        snapshot.MoveInput = input;
        snapshot.PlayerPosition = position;
        return snapshot;
    }

    /// <summary>
    /// Descent as this fixture needs it: endless, from stage 1, and with an <b>empty roster</b>.
    /// </summary>
    /// <remarks>
    /// Empty because <c>RunSession.Start</c> resolves every roster id against the catalog, and
    /// this fixture's catalog holds no enemies in most of its rows — a Husk in the roster would
    /// make every one of them fail on content it is not about. The rows that do care about a
    /// roster build their own mode.
    /// </remarks>
    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    /// <summary>The Husk, for the two rows that need something to spawn (GD §8.1).</summary>
    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        3.5f,
        1,
        4,
        false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// Asserts that a plan naming an unauthored archetype is refused with nothing announced,
    /// nothing standing and no state built.
    /// </summary>
    private void AssertStartRefusedCleanly(SpawnPlan plan)
    {
        Assert.Throws<KeyNotFoundException>(
            () => _session.Start(new RunConfig(
                new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, plan)));

        Assert.That(_session.IsRunning, Is.False);
        Assert.That(_session.State, Is.Null);
        Assert.That(_events.All, Is.Empty);
    }

    private void StartRun()
    {
        _session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty));
    }

    /// <summary>
    /// Ticks the run <paramref name="ticks"/> times at <see cref="Frame"/>. Named <c>TickFor</c>
    /// rather than <c>Run</c>, which is what the motor's fixture calls its equivalent: the
    /// enclosing namespace ends in <c>Run</c>, and a method sharing that name reads as an
    /// ambiguity even where the compiler has none.
    /// </summary>
    private void TickFor(int ticks, Vector2 input)
    {
        for (int i = 0; i < ticks; i++)
        {
            _session.Tick(Snapshot(Frame, input));
        }
    }

    /// <summary>
    /// An <see cref="IDomainEvents"/> that hands each payload to a callback as it is published,
    /// so a test can look at the session from *inside* the publish.
    /// </summary>
    /// <remarks>
    /// Nested and private because it exists for two tests in this fixture and nowhere else.
    /// <c>RecordingEvents</c> cannot do this job — it records what was published, which says
    /// nothing about what was true at the moment it happened, and that ordering is the rule.
    /// </remarks>
    private sealed class CapturingEvents : IDomainEvents
    {
        /// <summary>Called with each payload, boxed. Boxing is fine here; this never ships.</summary>
        public Action<object> OnPublish { get; set; }

        public void Publish<T>(in T evt) where T : struct
        {
            OnPublish?.Invoke(evt);
        }
    }
}
