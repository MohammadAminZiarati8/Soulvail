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

    /// <summary>An id of the right shape that the catalog does not hold.</summary>
    private const string UnknownId = "character.nobody";

    private const int Seed = 99;

    /// <summary>
    /// Small on purpose. Every row here is about the player, and the enemy system a run now
    /// composes needs only to exist — M1-06's own fixture is where its capacity matters.
    /// </summary>
    private const int EnemyCapacity = 8;

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
        _catalog = new ContentCatalog(new[] { Oathbound() });
        _session = new RunSession(_catalog, _random, _events, _intents, EnemyCapacity);
    }

    [Test]
    public void Config_DefaultId_Throws()
    {
        // Built in M0-09, tested here because that task was contracts with no fixture of its own.
        // `default(ContentId)` means nobody chose a class — a composition mistake — so it is
        // named here rather than left to surface as the catalog's "no character with id ''".
        Assert.Throws<ArgumentException>(() => new RunConfig(default, SpawnPlan.Empty));

        // The other half of the pair, and the reason the guard is narrow: a well-formed id the
        // catalog happens not to hold is missing *content*, which is the catalog's question to
        // answer at Start. See Start_UnknownCharacter_Throws_NotRunning.
        Assert.DoesNotThrow(() => new RunConfig(new ContentId(UnknownId), SpawnPlan.Empty));
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        // Beyond the spec's Tests table. The constructor is public and called from another
        // assembly (M0-12's RunInstaller), so it is a boundary; without these a forgotten
        // registration would surface a frame later, inside Tick, as an NRE that names the tick.
        Assert.Throws<ArgumentNullException>(() => new RunSession(null, _random, _events, _intents, EnemyCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, null, _events, _intents, EnemyCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, _random, null, _intents, EnemyCapacity));
        Assert.Throws<ArgumentNullException>(() => new RunSession(_catalog, _random, _events, null, EnemyCapacity));

        // Same family, added with the capacity in M1-06: a registry that can hold no enemies is a
        // configuration mistake, and checking it here rather than at the first Start means a
        // mis-wired scope fails while it is being built instead of one scene later.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunSession(_catalog, _random, _events, _intents, 0));
    }

    [Test]
    public void Config_NullSpawnPlan_Throws()
    {
        // Required rather than optional (M1-06): a run that starts empty says so with
        // SpawnPlan.Empty. An omitted plan and a broken spawner look identical in a playtest.
        Assert.Throws<ArgumentNullException>(
            () => new RunConfig(new ContentId(OathboundId), null));
    }

    [Test]
    public void Start_PublishesRunStarted_WithSeed()
    {
        StartRun();

        RunStarted started = _events.Single<RunStarted>();

        Assert.That(started.CharacterId, Is.EqualTo(new ContentId(OathboundId)));

        // The seed comes off IRandom, not out of the config — one source of truth, so the number
        // a bug report quotes is provably the one the streams draw from.
        Assert.That(started.Seed, Is.EqualTo(Seed));
        Assert.That(_session.IsRunning, Is.True);
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
            () => _session.Start(new RunConfig(new ContentId(UnknownId), SpawnPlan.Empty)));

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
        var session = new RunSession(_catalog, _random, events, _intents, EnemyCapacity);
        object payload = null;
        bool? runningDuringEvent = null;

        events.OnPublish = evt =>
        {
            payload = evt;
            runningDuringEvent = session.IsRunning;
        };

        session.Start(new RunConfig(new ContentId(OathboundId), SpawnPlan.Empty));

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
        var session = new RunSession(_catalog, _random, events, _intents, EnemyCapacity);
        session.Start(new RunConfig(new ContentId(OathboundId), SpawnPlan.Empty));

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

    private void StartRun()
    {
        _session.Start(new RunConfig(new ContentId(OathboundId), SpawnPlan.Empty));
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
