using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// <c>LevelTracker</c>: what a grant does, what it says, and where in a frame a kill is paid for.
/// </summary>
/// <remarks>
/// <para>
/// Three sections, because M3-01a's rules split three ways. The first is the tracker alone — the
/// arithmetic of a grant and the two events it publishes. The second is <c>EnemySystem</c>
/// <em>banking</em> a kill, which is where experience is earned. The third drives a real
/// <c>RunSession</c>, because the rule that matters most is an <em>ordering</em> and an ordering
/// cannot be observed from either end on its own (AR §18.1).
/// </para>
/// <para>
/// <b>The session rows read the run through <c>RunState</c>'s public scalars and nothing else.</b>
/// <c>Enemies</c> and <c>Progression</c> are both <c>internal</c> and <c>Soulvail.Tests.Core</c>
/// has no <c>InternalsVisibleTo</c>, deliberately (<c>RunSessionTests</c>' remarks) — so
/// <c>PendingXp</c> is asserted directly in the banking section, where the system is built by
/// hand, and through <c>Level</c> and the events everywhere else.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LevelTrackerTests
{
    private const string HuskId = "enemy.husk";
    private const string BombId = "enemy.bomb";
    private const string RichId = "enemy.rich";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    /// <summary>GD §8.1's Husk: 4 threat, and three times that in experience.</summary>
    private const int HuskCost = 4;
    private const float HuskXp = 12f;

    /// <summary>The Bloater's, on the same 3× convention.</summary>
    private const float BombXp = 24f;

    /// <summary>
    /// One kill worth more than <c>ToReach(2)</c>'s 51.67, so a single body levels the player.
    /// </summary>
    /// <remarks>
    /// The ordering rows need a level crossed by a stage's <em>last</em> kill, and a stage that
    /// takes nine Husks to pay for one level could not say which kill did it.
    /// </remarks>
    private const float RichXp = 60f;

    private const int Capacity = 32;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>60 fps — the rate every session row ticks at.</summary>
    private const float Frame = 1f / 60f;

    /// <summary>Comfortably outside <c>SpawnDirector.MinPlayerDistance</c> of the origin.</summary>
    private const float Ring = 10f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;

    /// <summary>Simulated seconds, for the rows that drive an <c>EnemySystem</c> by hand.</summary>
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _random = new FixedRandom(99, Alternating(8_192));
        _clock = new FixedClock(Instant);
        _now = 0f;
    }

    // ---- The tracker: granting and levelling (rules 4, 5, 8, 11) --------------------------------

    [Test]
    public void Grant_AddsXp()
    {
        LevelTracker tracker = Tracker();

        tracker.Grant(10f);

        Assert.That(tracker.Xp, Is.EqualTo(10f).Within(1e-3f));
        Assert.That(tracker.Level, Is.EqualTo(1));
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(0));

        // One event and only one: nothing was crossed, so there is no LeveledUp to come first.
        Assert.That(_events.Count<LeveledUp>(), Is.EqualTo(0));

        XpChanged changed = _events.Single<XpChanged>();

        Assert.That(changed.Gained, Is.EqualTo(10f).Within(1e-3f));
        Assert.That(changed.Level, Is.EqualTo(1));
        Assert.That(changed.Fraction, Is.EqualTo(10f / 51.668f).Within(1e-3f));
    }

    [Test]
    public void Grant_LevelsOnce()
    {
        LevelTracker tracker = Tracker();

        // ToReach(2) is 51.67, so 60 buys the level and leaves 8.33 towards ToReach(3)'s 75.87.
        tracker.Grant(60f);

        Assert.That(tracker.Level, Is.EqualTo(2));
        Assert.That(tracker.Xp, Is.EqualTo(8.33f).Within(0.01f));
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(1));

        LeveledUp levelled = _events.Single<LeveledUp>();

        Assert.That(levelled.Level, Is.EqualTo(2));
        Assert.That(levelled.PendingLevelUps, Is.EqualTo(1));

        XpChanged changed = _events.Single<XpChanged>();

        Assert.That(changed.Gained, Is.EqualTo(60f).Within(1e-3f));
        Assert.That(changed.Level, Is.EqualTo(2));
        Assert.That(changed.Fraction, Is.EqualTo(8.33f / 75.866f).Within(1e-2f));

        // **Rule 5, and the reason the order is a rule rather than an accident.** The bar wants the
        // settled state: an XpChanged published before the threshold was resolved would carry a
        // fraction above 1, which is a number a fill cannot draw.
        AssertOrder(typeof(LeveledUp), typeof(XpChanged));
    }

    [Test]
    public void Grant_LevelsTwiceInOneGrant()
    {
        LevelTracker tracker = Tracker();

        // 51.67 + 75.87 = 127.53, so 140 crosses two thresholds and leaves 12.47 of the third.
        // Ordinary rather than exceptional: a stage's last wave is paid for in one drain.
        tracker.Grant(140f);

        Assert.That(tracker.Level, Is.EqualTo(3));
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(2), "Two levels are two picks.");

        IReadOnlyList<LeveledUp> levels = _events.Of<LeveledUp>();

        Assert.That(levels.Count, Is.EqualTo(2));
        Assert.That(levels[0].Level, Is.EqualTo(2));
        Assert.That(levels[0].PendingLevelUps, Is.EqualTo(1));
        Assert.That(levels[1].Level, Is.EqualTo(3));
        Assert.That(levels[1].PendingLevelUps, Is.EqualTo(2));

        Assert.That(_events.Count<XpChanged>(), Is.EqualTo(1), "One grant, one XpChanged.");

        AssertOrder(typeof(LeveledUp), typeof(LeveledUp), typeof(XpChanged));
    }

    [Test]
    public void Grant_NonPositiveIsSilent()
    {
        LevelTracker tracker = Tracker();

        tracker.Grant(0f);
        tracker.Grant(-5f);
        tracker.Grant(float.NaN);

        // Infinity is not in rule 4's list and is refused all the same: it passes a `> 0` test, so
        // without the second clause the while loop below Grant's guard would never terminate —
        // which is a hung frame rather than a wrong number.
        tracker.Grant(float.PositiveInfinity);

        Assert.That(tracker.Xp, Is.EqualTo(0f));
        Assert.That(tracker.Level, Is.EqualTo(1));
        Assert.That(tracker.PendingLevelUps, Is.EqualTo(0));
        Assert.That(_events.All.Count, Is.EqualTo(0), "A grant worth nothing says nothing.");
    }

    [Test]
    public void Grant_ScaledByXpGain()
    {
        LevelTracker tracker = Tracker();

        tracker.XpGain.Add(new Modifier(ModifierKind.PercentAdd, 0.5f, this));

        tracker.Grant(10f);

        Assert.That(tracker.Xp, Is.EqualTo(15f).Within(1e-3f));
        Assert.That(
            _events.Single<XpChanged>().Gained,
            Is.EqualTo(15f).Within(1e-3f),
            "The event carries what was actually awarded — what a floating '+15' would show — not "
                + "the archetype's authored value.");
    }

    [Test]
    public void Grant_NegativeXpGainGrantsNothing()
    {
        LevelTracker tracker = Tracker();

        // −100 %: experience is switched off, reversibly, which is what rule 11 means by a stack
        // that can silence it.
        tracker.XpGain.Add(new Modifier(ModifierKind.PercentMult, -1f, this));

        tracker.Grant(10f);

        Assert.That(tracker.Xp, Is.EqualTo(0f));
        Assert.That(_events.All.Count, Is.EqualTo(0));

        tracker.XpGain.RemoveAll(this);

        // −150 %: the stat goes negative, and the floor is what stops a kill *costing* the player
        // experience. MathF.Max(0, …) rather than a clamp to a range, so the intent is one-sided.
        tracker.XpGain.Add(new Modifier(ModifierKind.PercentMult, -1.5f, this));

        Assert.That(tracker.XpGain.Value, Is.LessThan(0f), "Sanity: the stack really did invert.");

        tracker.Grant(10f);

        Assert.That(tracker.Xp, Is.EqualTo(0f));
        Assert.That(tracker.Level, Is.EqualTo(1));
        Assert.That(_events.All.Count, Is.EqualTo(0));
    }

    [Test]
    public void Spend_DecrementsPending()
    {
        LevelTracker tracker = Tracker();

        tracker.Grant(140f);

        Assert.That(tracker.PendingLevelUps, Is.EqualTo(2));

        tracker.SpendLevelUp();

        Assert.That(tracker.PendingLevelUps, Is.EqualTo(1), "One pick handed over, one still owed.");
    }

    [Test]
    public void Spend_AtZeroThrows()
    {
        LevelTracker tracker = Tracker();

        // A bug in the caller rather than a state to absorb: swallowing it would let a
        // double-tapped offer screen hand out two nodes for one level, silently.
        Assert.Throws<InvalidOperationException>(() => tracker.SpendLevelUp());
    }

    [Test]
    public void Ctor_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new LevelTracker(Scalings.Xp(), null));

        // The other end of AR §18.3's "a struct with an invariant needs the check at both ends".
        // ModeSpec refuses a default curve too; this is the end that would hang.
        Assert.Throws<ArgumentException>(() => new LevelTracker(default, new SilentEvents()));
    }

    [Test]
    public void Grant_AllocatesNothing()
    {
        // SilentEvents, never RecordingEvents: the recorder stores each payload in a
        // List<object> and so boxes every struct, which would report the fake's allocation as
        // core's (Traps §7).
        var tracker = new LevelTracker(Scalings.Xp(), new SilentEvents());

        // Grant(1) ten thousand times climbs to about level 24, so the measured window covers
        // twenty-odd threshold crossings and their events rather than only the flat path.
        AllocationAssert.None(() => tracker.Grant(1f));
    }

    // ---- Banking a kill (rules 2, 3, 7) --------------------------------------------------------

    [Test]
    public void Kill_AccruesPendingXp()
    {
        EnemySystem system = System();
        var player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);

        EnemyAgent husk = system.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, 5f));

        Assert.That(system.PendingXp, Is.EqualTo(0f), "Sanity: spawning pays nothing.");

        system.ApplyDamage(husk.Id, 10_000f, _now, player);

        Assert.That(system.PendingXp, Is.EqualTo(HuskXp));

        Assert.That(system.DrainXp(), Is.EqualTo(HuskXp));
        Assert.That(system.PendingXp, Is.EqualTo(0f), "Take-and-clear in one call, never two.");
        Assert.That(system.DrainXp(), Is.EqualTo(0f), "And a second drain pays nothing again.");
    }

    [Test]
    public void Kill_ByItsOwnFusePays()
    {
        EnemySystem system = System();
        var player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);
        var projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        var intents = new RecordingIntents();
        var snapshot = new WorldSnapshot(Capacity);

        EnemyAgent bomb = system.Spawn(new ContentId(BombId), new Vector3(0f, 0f, 5f));

        // Through Ingest rather than by writing the blackboard, which is Traps §7's fixture trap:
        // a blast is resolved against the position the *system* last ingested, so hand-written
        // perception would leave the row believing it had moved the player.
        for (int i = 0; i < 600 && bomb.IsAlive; i++)
        {
            snapshot.Clear();
            snapshot.Dt = Frame;
            snapshot.PlayerPosition = Vector3.Zero;

            ReadOnlySpan<EnemyAgent> agents = system.Registry.Alive;

            for (int a = 0; a < agents.Length; a++)
            {
                // `ref` on both sides, or the caller silently gets a copy and the row passes while
                // proving nothing (Traps §7).
                ref EnemySense sense = ref snapshot.AddEnemy();

                sense.Id = agents[a].Id;
                sense.Position = agents[a].Position;
                sense.Velocity = Vector3.Zero;
                sense.PathDirectionToPlayer = Vector2.Zero;
                sense.HasLineOfSight = true;
            }

            system.Ingest(snapshot);

            system.Tick(new EnemyTickContext(Frame, _now, player, intents, _events, projectiles, system));

            _now += Frame;
        }

        Assert.That(bomb.IsAlive, Is.False, "Sanity: the fixture failed to detonate the Bloater.");

        // **Rule 3: every death pays, whoever caused it.** Nobody swung at this one — it lit its
        // own fuse and went through the same ApplyDamage door a cone hit does, which is why a
        // rule about *who* killed it was never needed.
        Assert.That(system.PendingXp, Is.EqualTo(BombXp));
    }

    [Test]
    public void Clear_DropsPendingXp()
    {
        EnemySystem system = System();
        var player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);

        EnemyAgent husk = system.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, 5f));

        system.ApplyDamage(husk.Id, 10_000f, _now, player);

        Assert.That(system.PendingXp, Is.EqualTo(HuskXp));

        system.Clear();

        // Nothing is actually lost in production — at a boundary the drain has already run this
        // tick, and at the end of a run there is nobody left to pay (rule 7). The row is here so
        // that the day something clears mid-tick, the loss is a documented one.
        Assert.That(system.PendingXp, Is.EqualTo(0f));
    }

    // ---- The tick's drain (rules 6, 10) --------------------------------------------------------

    [Test]
    public void Session_StartBuildsTheTrackerFromTheMode()
    {
        Build(OneBodyStage(RichId));

        Start(1);

        RunState state = _session.State;

        Assert.That(state.Level, Is.EqualTo(1), "Every run starts at 1, and level 1 is free.");
        Assert.That(state.XpFraction, Is.EqualTo(0f));
        Assert.That(state.PendingLevelUps, Is.EqualTo(0));
    }

    [Test]
    public void State_HandsOutNoTracker()
    {
        // The fifth time AR §18.2's question has been asked, and the same answer: Grant and
        // SpendLevelUp are public on the tracker, so a public handle would let a view level the
        // player. Asserted by reflection rather than by "it does not compile", for the reason
        // every other seal in this project is: a future refactor that widened it would otherwise
        // be caught by nothing.
        Assert.That(
            typeof(RunState).GetProperty("Progression"),
            Is.Null,
            "Progression must not be public — like Combat, Motor, Enemies and Projectiles.");

        // And the three reads that stand in for it are.
        Assert.That(typeof(RunState).GetProperty(nameof(RunState.Level)), Is.Not.Null);
        Assert.That(typeof(RunState).GetProperty(nameof(RunState.XpFraction)), Is.Not.Null);
        Assert.That(typeof(RunState).GetProperty(nameof(RunState.PendingLevelUps)), Is.Not.Null);
    }

    [Test]
    public void Tick_FactKillIsPaidNextTick()
    {
        Build(OneBodyStage(RichId));

        Start(1);

        int id = SpawnTheBody();

        // The kill lands between ticks, which is what every fact does (ADR-0003): ReportConeHits
        // is answered by Unity after core has already decided this frame.
        KillByFact(id);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1), "Sanity: the body is down.");
        Assert.That(
            _events.Count<XpChanged>(),
            Is.EqualTo(0),
            "Nothing is granted on the fact — the tick is where experience is decided.");
        Assert.That(_session.State.Level, Is.EqualTo(1));

        _session.Tick(Snapshot(Vector3.Zero));

        Assert.That(
            _events.Count<XpChanged>(),
            Is.EqualTo(1),
            "Paid on the next tick, at most a frame late — the lag every fact already has.");
        Assert.That(_session.State.Level, Is.EqualTo(2), $"{RichXp} clears ToReach(2)'s 51.67.");
    }

    [Test]
    public void Tick_LeveledUpPrecedesStageCleared()
    {
        Build(OneBodyStage(RichId));

        Start(1);

        int id = SpawnTheBody();

        KillByFact(id);

        // The tick that pays for the kill is also the tick the director sweeps the corpse and the
        // flow notices the stage is done — which is the whole point: all three happen in one frame
        // and the order between them is the rule.
        for (int i = 0; i < 600 && _events.Count<StageCleared>() == 0; i++)
        {
            _session.Tick(Snapshot(Vector3.Zero));
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1), "Sanity: the stage never cleared.");

        // **Rule 6's second half, and the reason the drain sits above the director.** A LeveledUp
        // earned by a stage's last kill precedes that tick's StageCleared and the boundary snapshot
        // taken with it — which is what lets M3-01b's write carry the level the player just earned
        // rather than the one they had a frame ago.
        AssertOrder(typeof(EnemyDied), typeof(LeveledUp), typeof(XpChanged), typeof(StageCleared));
    }

    [Test]
    public void Tick_GrantsAfterTheDeathCheck()
    {
        // A blast that kills the player and the Bloater on the same tick. The Bloater banks its
        // experience inside the behaviour pass; the player's death is noticed one step later.
        Build(OneBodyStage(BombId), player: Oathbound());

        Start(1);

        for (int i = 0; i < 1_200 && _session.IsRunning; i++)
        {
            _session.Tick(Snapshot(Vector3.Zero));
        }

        Assert.That(_session.IsRunning, Is.False, "Sanity: the blast failed to kill the player.");
        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyDied>(), Is.GreaterThanOrEqualTo(1), "Sanity: the Bloater died too.");

        // **Rule 6's first half.** The drain sits below the death check, so a run that ended this
        // tick levels nobody: a LeveledUp published one line after End() would land in a scope
        // being torn down, where no screen could ever show it.
        Assert.That(_events.Count<LeveledUp>(), Is.EqualTo(0));
        Assert.That(_events.Count<XpChanged>(), Is.EqualTo(0));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>A fresh tracker on CH §5.2's curve, publishing into this fixture's recorder.</summary>
    private LevelTracker Tracker() => new LevelTracker(Scalings.Xp(), _events);

    /// <summary>
    /// Asserts that the named event types appear in <paramref name="types"/>' order, ignoring
    /// whatever else the run published in between.
    /// </summary>
    /// <remarks>
    /// Positions rather than an exact sequence: a real tick publishes telegraphs, spawns and
    /// damage around these, and a row that pinned the whole stream would fail every time an
    /// unrelated system gained an event. What M3-01a fixes is the order of these against each
    /// other.
    /// </remarks>
    private void AssertOrder(params Type[] types)
    {
        int searchFrom = 0;

        for (int t = 0; t < types.Length; t++)
        {
            int found = -1;

            for (int i = searchFrom; i < _events.All.Count; i++)
            {
                if (_events.All[i].GetType() == types[t])
                {
                    found = i;
                    break;
                }
            }

            Assert.That(
                found,
                Is.GreaterThanOrEqualTo(0),
                $"No {types[t].Name} after position {searchFrom}; the expected order was "
                    + string.Join(" → ", Array.ConvertAll(types, x => x.Name)) + ".");

            searchFrom = found + 1;
        }
    }

    private EnemySystem System() => new EnemySystem(
        Catalog(Oathbound()),
        _events,
        new FixedRandom(),
        new DepthScaling(Scalings.Design()),
        Capacity);

    private void Build(ModeSpec mode, CharacterSpec player = null)
    {
        _catalog = Catalog(player ?? Oathbound(), mode);

        _session = new RunSession(
            _catalog,
            _random,
            _events,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private void Start(int stage) => _session.Start(new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        _random.Seed,
        stage,
        SpawnPlan.Empty,
        restore: null));

    /// <summary>Ticks until the stage's one body is standing, and answers its id.</summary>
    private int SpawnTheBody()
    {
        for (int i = 0; i < 900 && _events.Count<EnemySpawned>() == 0; i++)
        {
            _session.Tick(Snapshot(Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(1), "The fixture failed to land the wave.");

        return _events.Of<EnemySpawned>()[0].Id;
    }

    /// <summary>
    /// Kills <paramref name="id"/> through <c>ReportConeHits</c> — the only route a core test has.
    /// </summary>
    /// <remarks>
    /// <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c> on <c>RunState</c>, so
    /// the player is walked onto the body and the weapon swings at what is under its nose, and the
    /// fixture answers the swing the way Unity would (<c>RunRecorderTests</c>' shape).
    /// </remarks>
    private void KillByFact(int id)
    {
        Vector3 where = _events.Of<EnemySpawned>()[0].Position;
        int[] report = { id };

        for (int i = 0; i < 900 && _events.Count<EnemyDied>() == 0; i++)
        {
            _session.Tick(Snapshot(where));
            _session.ReportConeHits(report);
        }

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1), "The fixture failed to kill the body.");
    }

    private WorldSnapshot Snapshot(Vector3 playerPosition) => new WorldSnapshot(Capacity)
    {
        Dt = Frame,
        PlayerPosition = playerPosition,
        HasGate = true,
        GatePosition = Door,
        SpawnPoints = Points(8),
    };

    // ---- Content -------------------------------------------------------------------------------

    private ContentCatalog Catalog(CharacterSpec player, ModeSpec mode = null) => new ContentCatalog(
        new[] { player },
        new[] { Husk(), Bomb(), Rich() },
        new[] { mode ?? OneBodyStage(HuskId) });

    /// <summary>
    /// A stage of exactly one body of <paramref name="specId"/>: one wave, a budget that buys one,
    /// and no growth with depth.
    /// </summary>
    private static ModeSpec OneBodyStage(string specId) => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        Scalings.Xp(),
        new[] { new RosterEntry(new ContentId(specId), 1) });

    /// <summary>GD §8.1's Husk, authored <c>Static</c> so it stands where these rows put it.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskXp,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>
    /// The same body, worth a whole level — so an ordering row can say which kill crossed the
    /// threshold.
    /// </summary>
    private static EnemySpec Rich() => new EnemySpec(
        new ContentId(RichId),
        new LocKey("enemy.rich.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: RichXp,
        isElite: false,
        contactDamage: 0f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>
    /// A Bloater that commits the moment it notices, and whose blast is fatal.
    /// </summary>
    /// <remarks>
    /// The reach is the arena rather than the Bloater's two metres, so the fuse starts on the
    /// first tick instead of after five seconds of waddling — these rows are about a frame's
    /// ordering, not about an approach that M2-08's own fixture already covers. The damage is what
    /// makes the tick fatal; the radius is what makes sure it catches a player standing at the
    /// origin whichever spawn point the seed drew.
    /// </remarks>
    private static EnemySpec Bomb() => new EnemySpec(
        new ContentId(BombId),
        new LocKey("enemy.bomb.name"),
        maxHp: 1_000f,
        moveSpeed: 0.01f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: BombXp,
        isElite: false,
        contactDamage: 1_000f,
        reach: 40f,
        windupTime: 0.05f,
        recoverTime: 0.6f,
        aggroRange: 60f,
        behaviour: EnemyBehaviourKind.Bloater,
        explosion: new ExplosionSpec(40f));

    /// <summary>CC §7's Oathbound, with the Focus ramp switched off for <c>RunSessionTests</c>' reason.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        shield: null);

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
}
