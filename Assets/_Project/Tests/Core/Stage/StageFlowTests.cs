using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Stage;

/// <summary>
/// Every rule of a stage's life: the two seconds of arrival, the waves, the beat after the last
/// body, the door that waits for ever, the fade, and what a boundary resets.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fixture ticks the frame in the session's order</b> — enemies ingested, the player, then
/// the director, then the flow — because that ordering <em>is</em> rule 13, and a fixture that
/// ticked the flow first would let every phase row pass while the real game read
/// <c>IsStageComplete</c> a frame stale. <see cref="Step"/> is the one place it is written down.
/// </para>
/// <para>
/// <b>It deliberately does not tick the enemy behaviours.</b> Two things depend on that: a corpse
/// stays registered, which is what <see cref="Advance_ClearsCorpses"/> is about; and a Husk standing
/// next to the player cannot kill them during an arrival these rows are timing. The rows that need
/// a live fight run a whole <c>RunSession</c> instead, further down.
/// </para>
/// <para>
/// <b>Time is stated, never accumulated by the caller.</b> <see cref="Step"/> takes a duration and
/// the fixture keeps the clock, so a row reads as a timeline and a failure names a moment rather
/// than a frame count — <c>SpawnDirectorTests</c>' convention, one level up.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StageFlowTests
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

    /// <summary>Comfortably outside <see cref="SpawnDirector.MinPlayerDistance"/> of the origin.</summary>
    private const float Ring = 10f;

    /// <summary>Where every row's door is, unless it says otherwise.</summary>
    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    /// <summary>Where the one row that needs a death dresses the thing that deals it.</summary>
    private static readonly Vector3 Executioner = new Vector3(-12f, 0f, 0f);

    private RecordingEvents _events;
    private ContentCatalog _catalog;
    private ModeSpec _mode;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private PlayerCombat _player;
    private SpawnDirector _director;
    private WaveComposer _composer;
    private WavePlan _plan;
    private StageFlow _flow;
    private WorldSnapshot _snapshot;
    private IRandomStream _spawn;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- Arrival (rules 1, 2, 4) ---------------------------------------------------------------

    [Test]
    public void Begin_EntersArrivalAndAnnouncesIt()
    {
        Build(OneHuskStage());

        BeginAt(3);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));
        Assert.That(_flow.Stage, Is.EqualTo(3));

        StageArrived arrived = _events.Single<StageArrived>();

        Assert.That(arrived.Stage, Is.EqualTo(3),
            "The opening stage is the run's, never assumed to be 1 (GD §4.5).");
        Assert.That(arrived.ArenaId, Is.EqualTo(default(ContentId)),
            "Nothing to index until M2-11a authors a roster (rule 5).");
    }

    [Test]
    public void Arrival_SpawnsNothing()
    {
        Build(OneHuskStage());
        BeginAt(1);

        for (int i = 0; i < 19; i++)
        {
            Step(0.1f);
        }

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));
        Assert.That(_events.Count<WaveStarted>(), Is.Zero);
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero,
            "Not even a ring. This is the pause M2-05 rule 1 refused to insert, and it is only a "
                + "beat rather than a stall because something seals and announces it.");
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void Arrival_EndsAtTwoSeconds()
    {
        Build(OneHuskStage());
        BeginAt(1);

        for (int i = 0; i < 20; i++)
        {
            Step(0.1f);
        }

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));
        Assert.That(_events.Count<WaveStarted>(), Is.EqualTo(1),
            "The director is handed its plan the instant arrival ends, and Begin starts wave 1.");

        // Nothing has been telegraphed yet on that tick: the director ran *before* the flow that
        // begins it (rule 13), so its first look at the plan is the frame after.
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero);

        Step(1f / 60f);

        Assert.That(_events.Count<WaveStarted>(), Is.EqualTo(1), "And the wave is not restarted.");
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "One frame of delay against a two-second arrival, named rather than discovered.");
    }

    [Test]
    public void Waves_ClearsTheDirectorBeforeBeginning()
    {
        Build(OneHuskStage());
        BeginAt(1);

        CrossTheBoundary();

        int ringsBefore = _events.Count<SpawnTelegraphed>();
        int spawnedBefore = _events.Count<EnemySpawned>();

        // Exactly to the end of the new arrival, which is the frame Clear-then-Begin runs on.
        Step(StageFlow.ArrivalTime);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));
        Assert.That(_director.Stage, Is.EqualTo(2), "The director was re-begun on the new plan.");
        Assert.That(_director.Wave, Is.EqualTo(1), "From wave 1, not from wherever stage 1 finished.");
        Assert.That(_director.PendingCount, Is.Zero,
            "Nothing is owed on the frame the new stage opens — a stage that inherited a pending "
                + "ring would spawn stage 1's body into stage 2's arena.");
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(ringsBefore),
            "And nothing was promised on the frame the plan changed hands.");

        // The claims were released too, which is what the explicit Clear buys: a claim outlives its
        // stage by 1.6 s otherwise, and that is exactly long enough to refuse the opening position.
        Step(1f / 60f);

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(ringsBefore + 1));
        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(spawnedBefore));
    }

    [Test]
    public void Waves_HandTheDirectorTheArenasSpawnPoints()
    {
        Build(OneHuskStage());
        BeginAt(1);

        // A room with one place in it, reported the way a raised arena reports one: on the frame
        // (M2-11a rule 6). Where a body may go is a fact about the arena that is standing, so this
        // is the only channel that can change its answer at a boundary.
        var only = new Vector3(0f, 0f, Ring);

        _snapshot.SpawnPoints = new[] { only };

        Step(StageFlow.ArrivalTime);
        Step(1f / 60f);

        Assert.That(_events.Single<SpawnTelegraphed>().Position, Is.EqualTo(only),
            "The director places this stage's wave at this arena's points and at no others.");
    }

    [Test]
    public void Waves_NextStageTakesTheNextArenasPoints()
    {
        Build(OneHuskStage());
        BeginAt(1);

        var first = new Vector3(0f, 0f, Ring);

        _snapshot.SpawnPoints = new[] { first };

        CrossTheBoundary();

        // A different room on the other side of the door, which is the whole reason the points are
        // handed over per stage rather than held for the run: a director built with the opening
        // arena's points would put stage 2's wave in stage 1's floor.
        var second = new Vector3(Ring, 0f, 0f);

        _snapshot.SpawnPoints = new[] { second };

        int before = _events.Count<SpawnTelegraphed>();

        Step(StageFlow.ArrivalTime);
        Step(1f / 60f);

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(before + 1));
        Assert.That(_events.Of<SpawnTelegraphed>()[before].Position, Is.EqualTo(second));
    }

    // ---- Clear (rules 3, 5, 6, 7) --------------------------------------------------------------

    [Test]
    public void Waves_EndOnStageComplete()
    {
        Build(OneHuskStage());
        BeginAt(1);

        int id = SpawnTheWave();

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves),
            "One alive: the stage is not over while anything the director issued is breathing.");
        Assert.That(_events.Count<StageCleared>(), Is.Zero);

        Kill(id);
        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear));
        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
    }

    [Test]
    public void Cleared_CarriesGateAndNextArena()
    {
        Build(OneHuskStage());
        BeginAt(3);

        ClearTheStage();

        StageCleared cleared = _events.Single<StageCleared>();

        Assert.That(cleared.Stage, Is.EqualTo(3));
        Assert.That(cleared.GatePosition, Is.EqualTo(Door),
            "Read off this frame's snapshot, so whatever draws a door does not have to ask core "
                + "where one is.");
        Assert.That(cleared.NextArenaId, Is.EqualTo(default(ContentId)),
            "Stage 4's arena, which is nothing until M2-11a — but named at the one moment the "
                + "answer is useful (rule 5).");
    }

    [Test]
    public void ArenaFor_IsSeedAndStageOnly()
    {
        // One flow's stream is forty draws further along than the other's, which is what a stage of
        // refused spawn positions does to it. The arena must not move because of that (rule 6).
        Build(OneHuskStage(), seed: 1234);
        BeginAt(3);
        ClearTheStage();

        ContentId untouched = _events.Single<StageCleared>().NextArenaId;

        _events = new RecordingEvents();
        Build(OneHuskStage(), seed: 1234);

        for (int i = 0; i < 40; i++)
        {
            _spawn.NextFloat();
        }

        BeginAt(3);
        ClearTheStage();

        Assert.That(_events.Single<StageCleared>().NextArenaId, Is.EqualTo(untouched),
            "Derived from the seed and the depth, never drawn — so a run resumed at stage 7 lands "
                + "in the arena stage 7 always had, with no saved stream position (ledger row 1).");
    }

    [Test]
    public void Clear_LastsClearTime()
    {
        Build(OneHuskStage());
        BeginAt(1);
        ClearTheStage();

        Step(1.4f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear),
            "A tenth of a second short, and the door is still shut — which is the half of this row "
                + "that says 1.5 is a real wait rather than a rounding.");

        Step(0.1f + (1f / 1000f));

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Gate),
            "1.5 s is the one number in this class that no design document gives — a pacing knob "
                + "for the phone, flagged as invented rather than derived (rule 7).");
    }

    // ---- Gate and transition (rules 8, 9, 15) --------------------------------------------------

    [Test]
    public void Gate_WaitsIndefinitely()
    {
        Build(OneHuskStage());
        BeginAt(1);
        ClearTheStage();
        Step(ClearTimeAndABit());

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Gate));

        // Eight metres out, which is nowhere near the door.
        _snapshot.PlayerPosition = new Vector3(0f, 0f, 10f);
        _events.Clear();

        for (int i = 0; i < 600; i++)
        {
            Step(1f / 60f);
        }

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Gate),
            "Ten seconds of standing still, and nothing happens — which is why a stage is GD "
                + "§7.3's commute unit rather than a timer.");
        Assert.That(_events.All, Is.Empty);
    }

    [Test]
    public void Gate_TripsInsideTheRadius()
    {
        Build(OneHuskStage());
        BeginAt(1);
        ClearTheStage();
        Step(ClearTimeAndABit());

        // 1.4 m short of the door, inside the 1.5 m reach.
        _snapshot.PlayerPosition = new Vector3(0f, 0f, Door.Z - 1.4f);

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition));

        StageTransitionStarted started = _events.Single<StageTransitionStarted>();

        Assert.That(started.Stage, Is.EqualTo(1), "The stage being left, not the one being entered.");
        Assert.That(started.Duration, Is.EqualTo(StageFlow.FadeTime).Within(1e-6f));
    }

    [Test]
    public void Gate_IsDecidedOnXz()
    {
        Build(OneHuskStage());
        BeginAt(1);
        ClearTheStage();
        Step(ClearTimeAndABit());

        // One metre away on the floor and four metres up in the air. AR §18.4: the height between a
        // player capsule's centre and a door's anchor is a rendering detail, and counting it would
        // make a door on a plinth unreachable from the floor in front of it.
        _snapshot.PlayerPosition = new Vector3(0f, 4f, Door.Z - 1f);

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition));
    }

    [Test]
    public void Transition_AdvancesAfterFadeTime()
    {
        Build(OneHuskStage());
        BeginAt(1);
        WalkIntoTheDoor();

        _events.Clear();

        Step(0.29f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition));
        Assert.That(_events.Count<StageArrived>(), Is.Zero,
            "The screen is not covered yet, and the world must not be swapped underneath a player "
                + "who can still see it.");

        Step(0.01f + (1f / 1000f));

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));

        StageArrived arrived = _events.Single<StageArrived>();

        Assert.That(arrived.Stage, Is.EqualTo(2));
    }

    [Test]
    public void Gate_NoGate_ParksInGate()
    {
        Build(OneHuskStage(), hasGate: false);
        BeginAt(1);
        ClearTheStage();

        Assert.That(_events.Single<StageCleared>().GatePosition, Is.EqualTo(Vector3.Zero),
            "There is no door, so there is no position — and zero is what a listener with nothing "
                + "to draw reads.");

        Step(ClearTimeAndABit());

        _events.Clear();

        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 600; i++)
            {
                Step(1f / 60f);
            }
        });

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Gate),
            "M2-05 rule 12's bargain, for its reason: the M0 grey box and every core fixture that "
                + "never intends to leave stage 1 are legal arenas, and neither should throw.");
        Assert.That(_events.Count<StageArrived>(), Is.Zero);
    }

    // ---- Crossing the boundary (rules 10, 11, 12, 17) ------------------------------------------

    [Test]
    public void Advance_MovesStageAndDepth()
    {
        Build(OneHuskStage());
        BeginAt(3);

        CrossTheBoundary();

        Assert.That(_flow.Stage, Is.EqualTo(4));
        Assert.That(_enemies.Depth, Is.EqualTo(4),
            "M2-03 rule 12: depth is a property of the arena bodies appear in, so nothing can "
                + "spawn into stage 4 priced at stage 3.");
    }

    [Test]
    public void Advance_SetsDepthBeforeAnythingCanSpawn()
    {
        // Rule 10's ordering, asserted by its consequence rather than by a spy: EnemySystem is
        // sealed, both writes have happened by the time the caller gets control back, and nothing
        // the clears do publishes anything to hang an observer off. What *is* observable is the
        // thing the ordering exists to guarantee — that the first body of the new stage is priced
        // at the new depth — and a wrong order makes this row fail with a stage-3 Husk.
        Build(OneHuskStage());
        BeginAt(3);

        float atThree = _enemies.Spawn(new ContentId(HuskId), new Vector3(20f, 0f, 0f)).Health.MaxHp.Value;

        CrossTheBoundary();

        Assert.That(_enemies.Depth, Is.EqualTo(4));
        Assert.That(_enemies.Registry.AliveCount, Is.Zero, "And the arena was emptied.");

        float atFour = _enemies.Spawn(new ContentId(HuskId), new Vector3(20f, 0f, 0f)).Health.MaxHp.Value;

        Assert.That(atFour, Is.GreaterThan(atThree),
            "The very first thing that can exist in the new arena is already scaled to it.");
    }

    [Test]
    public void Advance_ClearsProjectilesInFlight()
    {
        Build(OneHuskStage());
        BeginAt(1);
        ClearTheStage();

        // Slow and far, so both are unambiguously still flying when the boundary comes: 40 m at
        // 2 m/s is twenty seconds of flight against a crossing that takes under two.
        _projectiles.Fire(Shot(1), _now);
        _projectiles.Fire(Shot(2), _now);

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(2));

        _events.Clear();

        WalkThroughTheDoor();

        // Well past the twenty seconds those two would have taken to land, so "nothing was damaged"
        // is a fact about them having been dropped rather than about them still being in the air.
        for (int i = 0; i < 1_800; i++)
        {
            Step(1f / 60f);
        }

        Assert.That(_projectiles.InFlightCount, Is.Zero,
            "A shot fired at the old arena's floor, landing after the swap, would damage the "
                + "player at coordinates that no longer mean anything (rule 11).");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Advance_ClearsCorpses()
    {
        Build(OneHuskStage());
        BeginAt(1);

        ClearTheStage();

        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1),
            "Registered is not breathing: the corpse is still in the registry, waiting for its "
                + "despawn frame (AR §18.4).");

        WalkThroughTheDoor();

        Assert.That(_enemies.Registry.AliveCount, Is.Zero,
            "Otherwise it stands in the next arena — which is exactly the kind of thing that "
                + "survives a boundary (rule 11).");
    }

    [Test]
    public void Advance_ResetsTheTargetOnly()
    {
        Build(OneHuskStage());
        BeginAt(1);

        // A second Husk the director never issued, so it is not part of what makes the stage
        // complete — and so it is still alive, and still the target, at the moment of the crossing.
        // Without it the wave's own body would already be a corpse and this row would assert
        // nothing.
        _enemies.Spawn(new ContentId(HuskId), new Vector3(4f, 0f, 0f));

        ClearTheStage();

        _player.ApplyDamage(60f, _now);

        Assert.That(_player.Health.Current, Is.EqualTo(40f).Within(1e-3f));
        Assert.That(_player.Targeter.CurrentTargetId, Is.GreaterThanOrEqualTo(0),
            "The fixture failed to give the player a target, so the reset below would assert "
                + "nothing.");

        WalkThroughTheDoor();

        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(-1),
            "An id from an arena that has just been torn down.");
        Assert.That(_player.Health.Current, Is.EqualTo(40f).Within(1e-3f),
            "And no heal. PlayerCombat.Reset would refill it, which deletes the attrition GD "
                + "§12.5's death horizon is made of and pre-empts the Sanctum (rule 12).");
    }

    [Test]
    public void Advance_RecomposesThePlan()
    {
        // GD §12.1's real budget curve, so stage 4 is worth measurably more than stage 3.
        Build(Mode(budget: 0f, waves: 1, concurrency: DeviceCap, curve: new BudgetCurve(40f, 12f, 0.9f)));
        BeginAt(3);

        Assert.That(_plan.Stage, Is.EqualTo(3));

        int before = TotalBodies(_plan);

        ClearTheWholeStage();
        WalkThroughTheDoor();

        Assert.That(_plan.Stage, Is.EqualTo(4));
        Assert.That(TotalBodies(_plan), Is.GreaterThan(before),
            "B(4) = 40 + 36 + 8.1 against B(3) = 40 + 24 + 3.6, so the new stage buys more.");
    }

    [Test]
    public void Advance_ReusesTheOnePlan()
    {
        Build(OneHuskStage());
        BeginAt(1);

        WavePlan original = _plan;

        for (int i = 0; i < 20; i++)
        {
            ClearTheStage();
            WalkThroughTheDoor();
        }

        Assert.That(_flow.Stage, Is.EqualTo(21));
        Assert.That(_plan, Is.SameAs(original),
            "Twenty boundaries and one plan. A transition is the worst moment in a run to "
                + "allocate, and WavePlan.Begin is written to be refilled (rule 17, M2-04).");
    }

    [Test]
    public void Advance_ClearsTheDirectorBeforeRecomposing()
    {
        // **The regression row for the crash the owner hit walking out of stage 4.** Every other row
        // in this fixture composes from a *flat* mode — one wave, a fixed concurrency — so the plan
        // the director is holding never changes shape across a boundary and nothing could go wrong.
        // GD §12.2's real curves do: W(n) is two waves up to stage 4 and three from stage 5, and the
        // run owns **one** WavePlan that every stage is composed into. Recomposing it while the
        // director still held it left `SweepTheDead` walking `w < _plan.WaveCount` off the end of
        // arrays sized for the stage before, on the first tick of the new arrival.
        Build(Design(), openingStage: 4);
        BeginAt(4);

        Assert.That(_plan.WaveCount, Is.EqualTo(2), "Stage 4 is two waves — GD §12.2's W(4).");

        ClearTheWholeStage();
        WalkThroughTheDoor();

        Assert.That(_flow.Stage, Is.EqualTo(5));
        Assert.That(_plan.WaveCount, Is.EqualTo(3),
            "And stage 5 is three, which is the growth that used to walk off the end.");

        // The whole of the new arrival, ticking the director every frame exactly as the session
        // does. Before the fix this threw on the first of them.
        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                Step(1f / 60f);
            }
        });

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves), "And the new stage then runs.");
    }

    [Test]
    public void Advance_SurvivesManyBoundariesOnTheRealCurves()
    {
        // The same fix from the other side: every curve in GD §12 moving at once, six stages deep,
        // with the director ticked on every frame. The budget, the wave count and the concurrency
        // all grow, and each of them sizes something the director indexes.
        Build(Design(), openingStage: 1);
        BeginAt(1);

        Assert.DoesNotThrow(() =>
        {
            for (int stage = 1; stage <= 6; stage++)
            {
                ClearTheWholeStage();
                WalkThroughTheDoor();
            }
        });

        Assert.That(_flow.Stage, Is.EqualTo(7));
    }

    // ---- The mode's own end (rule 14) ----------------------------------------------------------

    [Test]
    public void Mode_FinalStageCompletesTheMode()
    {
        Build(Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap, endless: false, finalStage: 3));
        BeginAt(3);

        ClearTheStage();

        Assert.That(_flow.IsModeComplete, Is.True);
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear));

        _events.Clear();

        for (int i = 0; i < 600; i++)
        {
            Step(1f / 60f);
        }

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear),
            "No door opens onto a stage the mode says does not exist.");
        Assert.That(_events.Count<StageArrived>(), Is.Zero);
        Assert.That(_events.Count<RunEnded>(), Is.Zero,
            "Ending a run is the session's word, read off the flag (rule 14).");
    }

    [Test]
    public void Mode_EndlessNeverCompletes()
    {
        Build(OneHuskStage());
        BeginAt(60);

        ClearTheStage();
        WalkThroughTheDoor();

        Assert.That(_flow.Stage, Is.EqualTo(61));
        Assert.That(_flow.IsModeComplete, Is.False,
            "Descent is endless, so this is inert in V1 — and written anyway, because "
                + "ModeSpec.FinalStage exists.");
    }

    // ---- Cost (rules 17, 6) --------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        Build(OneHuskStage());
        BeginAt(1);

        // Mid-Waves, which is where a run spends nearly all of its time: one property read on the
        // director and a comparison. The warm-up is what stops the measurement catching a one-off.
        Step(2f);
        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Waves));

        for (int i = 0; i < 10_000; i++)
        {
            _now += 1f / 60f;
            _flow.Tick(_now, _snapshot, _spawn);
        }

        AllocationAssert.None(
            () =>
            {
                _now += 1f / 60f;
                _flow.Tick(_now, _snapshot, _spawn);
            },
            10_000);
    }

    [Test]
    public void Tick_DrawsNoRandomOutsideComposition()
    {
        Build(OneHuskStage());
        BeginAt(1);

        // Arrival, Gate and Transition run against a stream that throws on any draw. Waves is not
        // in the list and cannot be: the director draws a position per spawn attempt, which is what
        // it is for.
        var forbidden = new ThrowingStream();

        Step(1f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival));
        Assert.DoesNotThrow(() => _flow.Tick(_now, _snapshot, forbidden), "Arrival.");

        ClearTheStage();
        Step(ClearTimeAndABit());

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Gate));
        Assert.DoesNotThrow(() => _flow.Tick(_now, _snapshot, forbidden), "Gate.");

        _snapshot.PlayerPosition = Door;

        Assert.DoesNotThrow(() => _flow.Tick(_now, _snapshot, forbidden), "Gate, tripping.");
        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition));
        Assert.DoesNotThrow(() => _flow.Tick(_now + 0.1f, _snapshot, forbidden), "Transition.");

        // And the boundary itself does draw, which is the other half of the claim: composing a
        // stage is what the Spawn stream is for (ADR-0011).
        Assert.Throws<InvalidOperationException>(() => _flow.Tick(_now + StageFlow.FadeTime, _snapshot, forbidden));
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        Build(OneHuskStage());

        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(null, _composer, _director, _enemies, _projectiles, _player, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, null, _director, _enemies, _projectiles, _player, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, null, _enemies, _projectiles, _player, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, _director, null, _projectiles, _player, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, _director, _enemies, null, _player, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, _director, _enemies, _projectiles, null, _events, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, _director, _enemies, _projectiles, _player, null, _plan, 0));
        Assert.Throws<ArgumentNullException>(
            () => new StageFlow(_mode, _composer, _director, _enemies, _projectiles, _player, _events, null, 0));

        // Every int is a legal seed — it is a bit pattern, not a quantity — so there is nothing
        // there for a guard to reject and this row does not pretend otherwise.
        Assert.DoesNotThrow(
            () => new StageFlow(
                _mode, _composer, _director, _enemies, _projectiles, _player, _events, _plan, int.MinValue));
    }

    [Test]
    public void Begin_Guards()
    {
        Build(OneHuskStage());

        Assert.Throws<ArgumentOutOfRangeException>(() => _flow.Begin(0, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _flow.Begin(1, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _flow.Begin(1, float.PositiveInfinity));

        BeginAt(1);

        Assert.Throws<InvalidOperationException>(() => _flow.Begin(2, 0f));
    }

    [Test]
    public void Tick_Guards()
    {
        Build(OneHuskStage());

        Assert.Throws<InvalidOperationException>(() => _flow.Tick(0f, _snapshot, _spawn),
            "A flow ticked without a stage would sit at depth zero for ever with nothing in the log.");

        BeginAt(1);

        Assert.Throws<ArgumentNullException>(() => _flow.Tick(0f, null, _spawn));
        Assert.Throws<ArgumentNullException>(() => _flow.Tick(0f, _snapshot, null));

        // Guarded on a per-frame path, unusually, because the failure it prevents is silence: every
        // comparison against a NaN is false, so a NaN clock parks the flow in whatever phase it was
        // in and the run simply never advances (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => _flow.Tick(float.NaN, _snapshot, _spawn));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _flow.Tick(float.PositiveInfinity, _snapshot, _spawn));
    }

    // ---- The session ticks it in the right place (rules 13, 14, 16) ----------------------------

    [Test]
    public void Session_TicksFlowAfterDirectorBeforeMotor()
    {
        // The stage completes on the tick the director sweeps the body this fixture has just
        // killed. The flow must read that answer on the same tick rather than a frame later, and
        // the motor must still run afterwards.
        var intents = new RecordingIntents();
        RunSession session = Session(intents);

        session.Start(SessionConfig(1));

        Vector3 body = KillTheWave(session);

        intents.Clear();
        _events.Clear();

        session.Tick(SessionSnapshot(1f / 60f, body));

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1),
            "The director decided the stage was over on this very tick, and the flow read it on "
                + "the same one (rule 13).");
        Assert.That(intents.PlayerMoves.Count, Is.EqualTo(1),
            "And the motor still ran afterwards — the stage is part of the world the player moves "
                + "through, not part of the move.");
    }

    [Test]
    public void Session_DeadPlayerDoesNotAdvance()
    {
        // A run that ends on the tick the gate would have tripped. The death check sits before the
        // flow, so what this row watches for is a transition that must never be started.
        RunSession session = Session(new RecordingIntents());

        // The executioner is dressed twelve metres out, stands still, reaches four and kills. The
        // player is nowhere near it until this row puts them there.
        session.Start(SessionConfig(1, Executioner));

        KillTheWave(session);

        Assert.That(_events.Count<StageCleared>(), Is.Zero, "The sweep is still a tick away.");

        // Step onto the executioner with the door still far away: it starts its windup, and the
        // gate cannot trip because the player is eighteen metres from it.
        session.Tick(SessionSnapshot(1f / 60f, Executioner));

        _events.Clear();

        // Now the door is underfoot as well. Whichever tick the windup lands on, the run ends on it
        // — and the flow, which would otherwise trip the gate, never gets to run.
        for (int i = 0; i < 120 && _events.Count<RunEnded>() == 0; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, Executioner, gate: Executioner));
        }

        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1), "The fixture failed to kill the player.");
        Assert.That(_events.Count<StageTransitionStarted>(), Is.Zero,
            "The player was standing in the door on the tick they died. A stage must not begin to "
                + "be left by somebody who is not playing any more (rule 13).");
        Assert.That(_events.Count<StageArrived>(), Is.Zero);
        Assert.That(session.State.StageIndex, Is.EqualTo(1));
    }

    [Test]
    public void Session_NothingRespawns()
    {
        // The one behavioural assertion that the second spawner is gone (rule 16). A stage whose
        // plan is exhausted and whose bodies are all dead used to refill to KeepAlive for ever.
        RunSession session = Session(new RecordingIntents());

        session.Start(SessionConfig(1));

        Vector3 body = KillTheWave(session);

        int spawnedByTheStage = _events.Count<EnemySpawned>();

        // Ten seconds of standing still, nowhere near the door.
        for (int i = 0; i < 600; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, body));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(spawnedByTheStage),
            "Nothing trickled back in. RespawnPolicy is deleted, not disabled.");
        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1),
            "And the stage still reached Clear, which is the half that says an arena emptying is "
                + "now how a stage ends rather than how it stalls.");
        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(1),
            "One arrival — the run's own. The player never walked into the door.");
    }

    [Test]
    public void Session_AdvancesRunStateStageIndex()
    {
        // Rule 10's other half, and the one StageFlow cannot assert on its own: the depth the run
        // reports and saves is moved by the session, from the flow's own number.
        RunSession session = Session(new RecordingIntents());

        session.Start(SessionConfig(3));

        Assert.That(session.State.StageIndex, Is.EqualTo(3));

        KillTheWave(session);

        // Through Clear, into the doorway, and out through the fade.
        for (int i = 0; i < 600 && _events.Count<StageArrived>() < 2; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, Door));
        }

        Assert.That(_events.Count<StageArrived>(), Is.EqualTo(2), "The fixture failed to cross.");
        Assert.That(session.State.StageIndex, Is.EqualTo(4));
    }

    // ---- Fixture -------------------------------------------------------------------------------

    /// <summary>
    /// One frame, in <c>RunSession.Tick</c>'s order — which is the ordering rule 13 states.
    /// </summary>
    /// <remarks>
    /// The enemy behaviours are the one step left out, deliberately: see the fixture's remarks.
    /// </remarks>
    private void Step(float dt)
    {
        _now += dt;

        _snapshot.Dt = dt;

        _enemies.Ingest(_snapshot);

        _player.Tick(dt, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

        _projectiles.Tick(_now, _snapshot.PlayerPosition, _player);

        _director.Tick(_now, _snapshot.PlayerPosition, _spawn);

        _flow.Tick(_now, _snapshot, _spawn);
    }

    /// <summary>Runs the stage's first wave until its body is standing, and answers its id.</summary>
    /// <remarks>
    /// Counted from where the ledger already stood rather than from its start: a row that dressed a
    /// body of its own before calling this would otherwise be handed <em>that</em> one, kill it, and
    /// watch a stage that never completes.
    /// </remarks>
    private int SpawnTheWave()
    {
        int before = _events.Count<EnemySpawned>();

        Step(2f);

        for (int i = 0; i < 40 && _events.Count<EnemySpawned>() == before; i++)
        {
            Step(SpawnDirector.SpawnInterval);
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before),
            "The fixture failed to get the wave standing, so whatever this row asserts next is "
                + "about the fixture rather than about the flow.");

        return _events.Of<EnemySpawned>()[before].Id;
    }

    /// <summary>Takes a one-body stage from <c>Arrival</c> to <c>Clear</c>.</summary>
    private void ClearTheStage()
    {
        Kill(SpawnTheWave());

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to clear the stage.");
    }

    /// <summary>
    /// Takes a stage of any size to <c>Clear</c>, killing whatever the director issues as it goes.
    /// </summary>
    private void ClearTheWholeStage()
    {
        Step(2f);

        for (int i = 0; i < 2_000 && _flow.Phase == StagePhase.Waves; i++)
        {
            Step(SpawnDirector.SpawnInterval);

            ReadOnlySpan<EnemyAgent> alive = _enemies.Registry.Alive;

            for (int a = 0; a < alive.Length; a++)
            {
                if (alive[a].IsAlive)
                {
                    Kill(alive[a].Id);
                }
            }
        }

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Clear), "The fixture failed to clear the stage.");
    }

    /// <summary>From <c>Clear</c> into the doorway, so the flow is in <c>Transition</c>.</summary>
    private void WalkIntoTheDoor()
    {
        ClearTheStage();

        Step(ClearTimeAndABit());

        _snapshot.PlayerPosition = Door;

        Step(1f / 60f);

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Transition),
            "The fixture failed to reach the door.");
    }

    /// <summary>From <c>Clear</c> all the way to the next stage's <c>Arrival</c>.</summary>
    private void WalkThroughTheDoor()
    {
        Step(ClearTimeAndABit());

        _snapshot.PlayerPosition = Door;

        Step(1f / 60f);
        Step(StageFlow.FadeTime + (1f / 60f));

        // Back to the middle of the arena, so the next stage's door does not trip the instant it
        // opens — every row that crosses twice would otherwise cross both at once.
        _snapshot.PlayerPosition = Vector3.Zero;

        Assert.That(_flow.Phase, Is.EqualTo(StagePhase.Arrival),
            "The fixture failed to cross the boundary.");
    }

    /// <summary>A whole crossing, from <c>Arrival</c> to the next one.</summary>
    private void CrossTheBoundary()
    {
        ClearTheStage();
        WalkThroughTheDoor();
    }

    /// <summary>Just past <see cref="StageFlow.ClearTime"/>, so the door has opened.</summary>
    /// <remarks>
    /// <b>Past rather than exactly on, and every phase boundary here is written the same way.</b>
    /// The clock is a float accumulated a step at a time, so by the twentieth stage of
    /// <see cref="Advance_ReusesTheOnePlan"/> a sum that should read 1.500000 reads 1.499999 — and a
    /// row asserting on the constant would be asserting about rounding. The rows that care about
    /// <em>when</em> a phase ends say so by checking the tick before it as well.
    /// </remarks>
    private static float ClearTimeAndABit() => StageFlow.ClearTime + (1f / 60f);

    private void Kill(int id)
    {
        _enemies.ApplyDamage(id, 10_000f, _now, _player);
    }

    /// <summary>A shot slow enough and far enough to still be flying at any boundary.</summary>
    private static Projectile Shot(int sourceId) => new Projectile(
        new ContentId("projectile.test"),
        sourceId,
        new Vector3(40f, 0f, 0f),
        Vector3.Zero,
        speed: 2f,
        radius: 1f,
        damage: 10f);

    private static int TotalBodies(WavePlan plan)
    {
        int total = 0;

        for (int w = 1; w <= plan.WaveCount; w++)
        {
            total += plan.BodyCount(w);
        }

        return total;
    }

    /// <summary>Builds the whole run-sized world one row needs.</summary>
    /// <remarks>
    /// <paramref name="openingStage"/> is only read by the rows that compose on the real curves:
    /// the plan is sized at the wave curve's ceiling either way, and a flat mode's dimensions do not
    /// depend on which stage composed it.
    /// </remarks>
    private void Build(ModeSpec mode, int seed = 0, bool hasGate = true, int openingStage = 1)
    {
        _ = openingStage;

        _mode = mode;
        _catalog = Catalog(mode);
        _now = 0f;

        _enemies = new EnemySystem(
            _catalog,
            _events,
            new FixedRandom(seed),
            new DepthScaling(mode.Scaling),
            Capacity);

        _snapshot = new WorldSnapshot(Capacity)
        {
            HasGate = hasGate,
            GatePosition = Door,

            // The arena's own points, on the frame, as of M2-11a: the flow reads them when a stage
            // leaves arrival and hands them to the director, so a fixture whose snapshot carried
            // none would compose stages into a room with nowhere to put a body.
            SpawnPoints = Points(8),
        };

        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);
        _director = new SpawnDirector(_enemies, _events);
        _composer = new WaveComposer(_catalog, new ThreatBudget(_mode.Scaling, DeviceCap));
        _plan = new WavePlan(MaxWaves, Math.Max(1, _mode.Roster.Count));
        _spawn = new FixedRandom(seed, Alternating(8_192)).Spawn;

        _flow = Flow();
    }

    /// <summary>
    /// Composes <paramref name="stage"/> and opens it — <c>RunSession.Start</c>'s two lines, in its
    /// own order.
    /// </summary>
    /// <remarks>
    /// <c>Begin</c> adopts a composition rather than making one, so a row that opened stage 3
    /// against a plan composed for stage 1 would be running stage 1's waves under stage 3's name.
    /// The depth moves with it for the same reason the session moves it: nothing may spawn into a
    /// stage priced at another one.
    /// </remarks>
    private void BeginAt(int stage)
    {
        _composer.Compose(stage, _mode, _plan, _spawn);

        _enemies.Depth = stage;

        _flow.Begin(stage, _now);
    }

    /// <summary>The fixture's <see cref="StageFlow"/>, over everything <see cref="Build"/> made.</summary>
    private StageFlow Flow() => new StageFlow(
        _mode,
        _composer,
        _director,
        _enemies,
        _projectiles,
        _player,
        _events,
        _plan,
        seed: 0);

    /// <summary>A whole run, for the rows that are about where the session ticks the flow.</summary>
    private RunSession Session(RecordingIntents intents)
    {
        _mode = Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap);
        _catalog = Catalog(_mode);

        return new RunSession(
            _catalog,
            new FixedRandom(0, Alternating(8_192)),
            _events,
            intents,
            new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private static RunConfig SessionConfig(int stage, Vector3? executionerAt = null) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        stage,
        new SpawnPlan(
            executionerAt is null
                ? Array.Empty<SpawnPlan.Entry>()
                : new[] { new SpawnPlan.Entry(new ContentId(ExecutionerId), executionerAt.Value) }));

    private static WorldSnapshot SessionSnapshot(float dt, Vector3 playerPosition, Vector3? gate = null) =>
        new WorldSnapshot(Capacity)
        {
            Dt = dt,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = gate ?? Door,
            SpawnPoints = Points(8),
        };

    /// <summary>
    /// Ticks a run out of arrival, stands the wave's one body up and cuts it down — leaving the
    /// stage one sweep away from complete. Answers where the body was.
    /// </summary>
    /// <remarks>
    /// The kill goes through <c>ReportConeHits</c>, which is the only route a core test has: a
    /// session's <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c> on
    /// <c>RunState</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>, deliberately
    /// (<c>RunSessionTests</c>' remarks). So the player is walked onto the body, the Censer swings
    /// at what is under its nose, and the fixture answers the swing the way Unity would.
    /// </remarks>
    private Vector3 KillTheWave(RunSession session)
    {
        // Counted from where the ledger stood, not from zero: a run whose plan dressed a body into
        // the arena has already published a spawn before the first wave is even composed.
        int before = _events.Count<EnemySpawned>();

        for (int i = 0; i < 600 && _events.Count<EnemySpawned>() == before; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.GreaterThan(before),
            "The fixture failed to land the wave, so whatever this row asserts next is about the "
                + "fixture rather than about the flow.");

        EnemySpawned spawned = _events.Of<EnemySpawned>()[before];
        int[] report = { spawned.Id };

        for (int i = 0; i < 600 && _events.Count<EnemyDied>() == 0; i++)
        {
            session.Tick(SessionSnapshot(1f / 60f, spawned.Position));
            session.ReportConeHits(report);
        }

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1), "The fixture failed to kill the wave.");

        return spawned.Position;
    }

    /// <summary>A stage of exactly one Husk: one wave, one body, and a budget that buys it.</summary>
    private static ModeSpec OneHuskStage() => Mode(budget: HuskCost, waves: 1, concurrency: DeviceCap);

    /// <summary>
    /// GD §12's curves as the game ships them, for the two rows that are about a plan changing
    /// shape across a boundary.
    /// </summary>
    /// <remarks>
    /// Every other row here composes from a flat mode on purpose — a stage whose contents are stated
    /// rather than derived is what makes a pacing assertion readable. These two need the opposite:
    /// the wave count and the concurrency have to actually move, because the bug they pin was a
    /// dimension growing underneath something that had already sized an array from it.
    /// </remarks>
    private static ModeSpec Design() => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        new[] { new RosterEntry(new ContentId(HuskId), 1) });

    /// <summary>
    /// A mode whose curves compose exactly the stage a row wants — <c>SpawnDirectorTests</c>'
    /// helper, with the two members this fixture also has to state.
    /// </summary>
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

    private static ContentCatalog Catalog(ModeSpec mode) => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), TheExecutioner() },
        new[] { mode });

    /// <summary>
    /// The Husk, and it is authored <c>Static</c> on purpose: this fixture is about pacing, and a
    /// Chaser would walk into the player during the two seconds of arrival these rows are timing.
    /// Ten hit points, so one swing of the Censer ends it and a session row can kill by reporting a
    /// cone hit rather than by reaching into core's internals.
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
    /// A Husk that does not move, reaches four metres and kills outright.
    /// </summary>
    /// <remarks>
    /// It exists for one row, and every number on it is chosen so that a death lands on a tick the
    /// fixture picks: it stands still, so it is only ever where it was dressed; it reaches further
    /// than the gate's 1.5 m, so stepping onto it does not also have to mean stepping into a door;
    /// and it kills in one strike, so the run ends on the frame the windup does rather than after a
    /// fight nobody is timing.
    /// </remarks>
    private static EnemySpec TheExecutioner() => new EnemySpec(
        new ContentId(ExecutionerId),
        new LocKey("enemy.executioner.name"),
        maxHp: 1_000f,
        moveSpeed: 0.01f,
        targetPriority: 1,
        threatCost: 40,
        isElite: false,
        contactDamage: 10_000f,
        reach: 4f,
        windupTime: 0.05f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>The class every row plays. CC §7's numbers; no row here is about the player.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it would
        // put a modifier and a stream of events into fixtures measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

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

    /// <summary>A stream that refuses to be drawn from, so a draw is a failure rather than a value.</summary>
    private sealed class ThrowingStream : IRandomStream
    {
        public float NextFloat() => throw Refuse();

        public int NextInt(int minInclusive, int maxExclusive) => throw Refuse();

        public float Range(float minInclusive, float maxInclusive) => throw Refuse();

        public bool Chance(float probability) => throw Refuse();

        private static InvalidOperationException Refuse() =>
            new InvalidOperationException("This phase must not draw.");
    }
}
