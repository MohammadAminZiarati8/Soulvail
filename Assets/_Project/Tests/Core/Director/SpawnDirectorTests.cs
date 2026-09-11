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
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Director;

/// <summary>
/// Every pacing rule: waves that start and overlap, bodies announced before they exist, positions
/// that refuse the player and each other, the concurrency cap, and the stage that is over only
/// when the last body of it is dead.
/// </summary>
/// <remarks>
/// <para>
/// <b>A wave's contents are composed rather than stated</b>, because <see cref="WavePlan"/>'s
/// writers are <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>
/// (M2-04, deliberately). So each row authors a mode whose curves compose exactly the wave it
/// wants — a flat budget, a fixed wave count and a fixed concurrency — and <see cref="Wave"/>'s
/// helpers do the arithmetic in one place. The Husk costs 4, so "a wave of 8" is spelled as a
/// budget of 32 for that wave.
/// </para>
/// <para>
/// <b>Draws are scripted, never seeded</b>, for <c>WaveComposerTests</c>' reason: a 0.1 always
/// takes the first candidate and a 0.9 the last, which makes a row's intended choice readable from
/// its script. <see cref="Tick_Deterministic"/> is what says "the same stream twice gives the same
/// stage", which is the claim this boundary can actually make.
/// </para>
/// <para>
/// <b>Time is stated, never accumulated.</b> Every <c>Tick</c> is handed the absolute second it
/// happens at, so a row reads as a timeline — <c>0</c>, <c>0.35</c>, <c>0.8</c> — and a failure
/// names the moment rather than a frame count.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SpawnDirectorTests
{
    private const string HuskId = "enemy.husk";
    private const string BloaterId = "enemy.bloater";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    /// <summary>GD §8.1's threat costs for the two archetypes these rows compose from.</summary>
    private const int HuskCost = 4;
    private const int BloaterCost = 8;

    /// <summary>Room for every row's population, and the cap a stage is composed under.</summary>
    private const int Capacity = 64;
    private const int DeviceCap = 28;

    /// <summary>
    /// Room for every shot a row here puts in the air, which is none: nothing fires one until
    /// M2-07b. Required by <c>RunSession</c> since M2-07a, and guarded positive, so it is a
    /// number rather than a zero.
    /// </summary>
    private const int ProjectileCapacity = 8;

    /// <summary>The wave curve's ceiling — what a run sizes its one plan to.</summary>
    private const int MaxWaves = 5;

    /// <summary>Comfortably outside <see cref="SpawnDirector.MinPlayerDistance"/> of the origin.</summary>
    private const float Ring = 10f;

    private RecordingEvents _events;
    private ContentCatalog _catalog;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _catalog = Catalog();
    }

    // ---- Waves (rules 1, 2, 4, 5, 6) -----------------------------------------------------------

    [Test]
    public void Begin_StartsWaveOne()
    {
        ModeSpec mode = Mode(budget: 120f, waves: 3, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        Assert.That(director.Stage, Is.EqualTo(1));
        Assert.That(director.Wave, Is.EqualTo(1), "Begin starts wave 1 immediately (rule 1).");

        WaveStarted started = _events.Single<WaveStarted>();

        Assert.That(started.Stage, Is.EqualTo(1));
        Assert.That(started.Wave, Is.EqualTo(1));
        Assert.That(started.WaveCount, Is.EqualTo(3),
            "The event carries the whole stage, because 'wave 3' alone says nothing about how " +
            "much is left.");

        // Nothing of the wave has been announced yet: a wave opening and its first body being
        // telegraphed are separate moments, and the ring is Tick's to publish.
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero);
    }

    [Test]
    public void Wave_InterleavesItsEntries()
    {
        // Both archetypes eligible at stage 3, and nothing introduced there — so the composition is
        // entirely the draws, which alternate between the cheapest and the dearest affordable.
        ModeSpec mode = Mode(budget: 48f, waves: 1, concurrency: DeviceCap, (HuskId, 1), (BloaterId, 2));
        WavePlan plan = Composed(mode, 3, Alternating(16));

        Assert.That(plan.EntryCount(1), Is.GreaterThan(1),
            "This row is about mixing two archetypes; a single-entry wave could not fail it.");

        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(plan, 0f);

        TickThrough(director, enemies, plan.BodyCount(1));

        Assert.That(Telegraphed(), Is.EqualTo(RoundRobin(plan, 1)),
            "A mixed wave must arrive interleaved rather than as every Husk followed by every " +
            "Bloater (rule 2). The plan says what; this says when.");
    }

    [Test]
    public void Wave_OverlapsAtQuarterRemaining()
    {
        // B = 96 over two waves: wave 1 gets a third of it — 32, which is 8 Husks.
        ModeSpec mode = Mode(budget: 96f, waves: 2, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));
        WavePlan plan = Composed(mode, 1);

        Assert.That(plan.BodyCount(1), Is.EqualTo(8), "B(1)/3 = 32, and a Husk costs 4.");

        director.Begin(plan, 0f);

        float now = TickThrough(director, enemies, 8);

        List<int> ids = SpawnedIds();

        Kill(enemies, ids, 5, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(_events.Count<WaveStarted>(), Is.EqualTo(1),
            "Three of eight are still standing, which is above the quarter (rule 4).");

        Kill(enemies, ids, 1, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(_events.Count<WaveStarted>(), Is.EqualTo(2));
        Assert.That(director.Wave, Is.EqualTo(2),
            "Two of eight is the quarter exactly, and the next wave starts on it rather than " +
            "after it.");
    }

    [Test]
    public void Wave_OverlapRoundsUp()
    {
        // B = 36 over two waves: wave 1 gets 12, which is 3 Husks. A quarter of 3 is 0.75.
        ModeSpec mode = Mode(budget: 36f, waves: 2, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));
        WavePlan plan = Composed(mode, 1);

        Assert.That(plan.BodyCount(1), Is.EqualTo(3));

        director.Begin(plan, 0f);

        float now = TickThrough(director, enemies, 3);

        Kill(enemies, SpawnedIds(), 2, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(director.Wave, Is.EqualTo(2),
            "Rounded up, so a wave of three overlaps at one survivor. Rounded down it would wait " +
            "for a total wipe, which is the rhythm GD §7.3 exists to remove.");
    }

    [Test]
    public void Wave_WaitsForItsOwnQueue()
    {
        ModeSpec mode = Mode(budget: 96f, waves: 2, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        // One spawn point, which is what makes this row possible: the claim on it holds for a
        // telegraph after each body lands, so the wave arrives one at a time and can be stopped
        // part-way through. Eight points would have five rings in the air by now.
        SpawnDirector director = Director(enemies, Points(1));

        director.Begin(Composed(mode, 1), 0f);

        // A ring at 0, 1.6 and 3.2; a body at 0.8, 2.4 and 4.0. Three of the wave's eight have
        // arrived and five are still queued.
        float now = 0f;

        for (int i = 0; i < 6; i++)
        {
            director.Tick(now, Vector3.Zero, Stream());

            now += SpawnDirector.TelegraphTime;
        }

        Assert.That(enemies.Registry.AliveCount, Is.EqualTo(3));
        Assert.That(director.PendingCount, Is.Zero);

        Kill(enemies, SpawnedIds(), 3, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(_events.Count<WaveStarted>(), Is.EqualTo(1),
            "Nothing of wave 1 is standing, but most of it has not arrived yet — a wave that is " +
            "barely born is not nearly dead (rule 4).");
    }

    [Test]
    public void Wave_ClearedWhenLastBodyDies()
    {
        ModeSpec mode = Mode(budget: 36f, waves: 2, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        float now = TickThrough(director, enemies, 3);
        List<int> wave1 = SpawnedIds();

        Kill(enemies, wave1, 2, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(director.Wave, Is.EqualTo(2), "Wave 2 is running while wave 1 has one left.");
        Assert.That(_events.Count<WaveCleared>(), Is.Zero);

        Kill(enemies, wave1, 1, now);
        director.Tick(now, Vector3.Zero, Stream());

        WaveCleared cleared = _events.Single<WaveCleared>();

        Assert.That(cleared.Stage, Is.EqualTo(1));
        Assert.That(cleared.Wave, Is.EqualTo(1));
        Assert.That(IndexOf<WaveCleared>(), Is.GreaterThan(LastIndexOf<WaveStarted>()),
            "A wave clears after the next one has started, routinely — that is what overlapping " +
            "means (rule 5).");
    }

    // ---- The stage ends, and says nothing about it (rule 6) -------------------------------------

    [Test]
    public void Stage_CompleteOnlyWhenEmpty()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        Assert.That(director.IsStageComplete, Is.False, "Nothing has even arrived yet.");

        float now = TickThrough(director, enemies, 2);
        List<int> ids = SpawnedIds();

        Kill(enemies, ids, 1, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.False, "One is still standing.");

        Kill(enemies, ids, 1, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.True);
    }

    [Test]
    public void Stage_PublishesNoStageEvent()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        float now = TickThrough(director, enemies, 2);

        Kill(enemies, SpawnedIds(), 2, now);
        director.Tick(now, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.True);

        // The director's whole vocabulary, plus what EnemySystem says about the bodies it was
        // asked for. A StageCleared here would be a second answer to the question M2-10's gate,
        // door and depth all hang off — which is why IsStageComplete is asked and never announced.
        var allowed = new HashSet<Type>
        {
            typeof(WaveStarted),
            typeof(WaveCleared),
            typeof(SpawnTelegraphed),
            typeof(EnemySpawned),
            typeof(EnemyDamaged),
            typeof(EnemyDied),
        };

        foreach (object published in _events.All)
        {
            Assert.That(allowed, Does.Contain(published.GetType()),
                $"'{published.GetType().Name}' was published during a stage. The director owns " +
                "three events and ends nothing (rule 6).");
        }
    }

    // ---- Spawns (rules 7, 8, 9, 10) ------------------------------------------------------------

    [Test]
    public void Spawn_IsTelegraphedFirst()
    {
        ModeSpec mode = Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        SpawnTelegraphed ring = _events.Single<SpawnTelegraphed>();

        Assert.That(ring.SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(ring.FiresAt, Is.EqualTo(SpawnDirector.TelegraphTime).Within(1e-4f));
        Assert.That(director.PendingCount, Is.EqualTo(1));
        Assert.That(enemies.Registry.AliveCount, Is.Zero,
            "A body is announced before it exists, never alongside it (rule 7).");

        director.Tick(SpawnDirector.TelegraphTime, Vector3.Zero, Stream());

        Assert.That(director.PendingCount, Is.Zero);
        Assert.That(enemies.Registry.AliveCount, Is.EqualTo(1));
        Assert.That(_events.Single<EnemySpawned>().Position, Is.EqualTo(ring.Position),
            "Exactly where the ring was, not approximately — a ring the player dodged that " +
            "produced something two metres away teaches them not to trust the next one.");
    }

    [Test]
    public void Spawn_TelegraphNotCancelledByCap()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: 4, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(director.PendingCount, Is.EqualTo(1));

        // The arena fills from somewhere else entirely while the ring is up — an arena's dressed
        // enemies, or M2-10's next stage. The promise was made before any of it happened.
        for (int i = 0; i < 4; i++)
        {
            enemies.Spawn(new ContentId(HuskId), new Vector3(Ring, 0f, i));
        }

        director.Tick(SpawnDirector.TelegraphTime, Vector3.Zero, Stream());

        Assert.That(enemies.LivingCount(), Is.EqualTo(5),
            "The cap refuses new telegraphs; it never eats one already in flight (rule 7).");
        Assert.That(director.PendingCount, Is.Zero);
    }

    [Test]
    public void Spawn_RespectsInterval()
    {
        ModeSpec mode = Mode(budget: 12f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        director.Tick(0f, Vector3.Zero, Stream());
        director.Tick(0f, Vector3.Zero, Stream());
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "Three ticks in one instant is one body. A wave that dumps its rings in a frame is " +
            "unreadable (rule 3).");

        director.Tick(SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(2));

        director.Tick(2f * SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(3));
    }

    [Test]
    public void Spawn_StopsAtConcurrency()
    {
        ModeSpec mode = Mode(budget: 16f, waves: 1, concurrency: 4, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        for (int i = 0; i < 3; i++)
        {
            enemies.Spawn(new ContentId(HuskId), new Vector3(Ring, 0f, i));
        }

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(director.PendingCount, Is.EqualTo(1));

        director.Tick(SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "Three alive and one pending is four, which is the cap — counting only the living " +
            "would let every ring in flight overshoot it (rule 8).");
    }

    [Test]
    public void Spawn_ResumesWhenRoomAppears()
    {
        ModeSpec mode = Mode(budget: 16f, waves: 1, concurrency: 4, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));
        var standing = new List<int>();

        for (int i = 0; i < 3; i++)
        {
            standing.Add(enemies.Spawn(new ContentId(HuskId), new Vector3(Ring, 0f, i)).Id);
        }

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());
        director.Tick(SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1));

        Kill(enemies, standing, 1, SpawnDirector.SpawnInterval);

        director.Tick(2f * SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(2),
            "A spawn refused by the cap is deferred, not dropped — the queue kept it (rule 8).");
    }

    [Test]
    public void Spawn_RefusesInsidePlayerClearance()
    {
        ModeSpec mode = Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        var near = new Vector3(3f, 0f, 0f);
        var far = new Vector3(12f, 0f, 0f);

        SpawnDirector director = Director(enemies, new[] { near, far });

        director.Begin(Composed(mode, 1), 0f);

        // 0.1 picks index 0, which is the point three metres from the player.
        director.Tick(0f, Vector3.Zero, Stream(0.1f));

        Assert.That(_events.Single<SpawnTelegraphed>().Position, Is.EqualTo(far),
            "GD §12.4: nothing appears within six metres of the player, so the walk from the " +
            "drawn index takes the next point that qualifies (rule 9).");
    }

    [Test]
    public void Spawn_NoSafePoint_TriesAgainNextTick()
    {
        ModeSpec mode = Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        SpawnDirector director = Director(
            enemies,
            new[] { new Vector3(3f, 0f, 0f), new Vector3(0f, 0f, 3f) });

        director.Begin(Composed(mode, 1), 0f);

        Assert.DoesNotThrow(() => director.Tick(0f, Vector3.Zero, Stream()));
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero,
            "The player is standing in the middle of the spawn ring. A pause is better than a " +
            "spawn on top of them, and nothing is said about it.");

        director.Tick(0.1f, new Vector3(100f, 0f, 0f), Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "They moved, so the same point is fine now — the attempt is retried rather than the " +
            "body dropped.");
    }

    [Test]
    public void Spawn_OneDrawPerAttempt()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        // Every point is inside the clearance, so every attempt walks the whole list and refuses.
        SpawnDirector director = Director(
            enemies,
            new[] { new Vector3(3f, 0f, 0f), new Vector3(0f, 0f, 3f) });

        director.Begin(Composed(mode, 1), 0f);

        var counted = new CountingStream(Stream());

        director.Tick(0f, Vector3.Zero, counted);
        director.Tick(0.1f, Vector3.Zero, counted);
        director.Tick(0.2f, Vector3.Zero, counted);

        Assert.That(counted.Draws, Is.EqualTo(3),
            "One draw per attempt however it ends. A stream whose consumption depended on where " +
            "the player was standing is the one thing a seed cannot survive (rule 9).");
    }

    [Test]
    public void Spawn_NeverReusesAClaimedPoint()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        // A metre apart, which is inside MinSpawnSeparation, and both well clear of the player.
        SpawnDirector director = Director(
            enemies,
            new[] { new Vector3(Ring, 0f, 0f), new Vector3(Ring + 1f, 0f, 0f) });

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1));

        director.Tick(SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "The second point is a metre from a ring that is already up, so it is refused while " +
            "the claim holds (ledger row 9).");
    }

    [Test]
    public void Spawn_ClaimExpires()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        var only = new Vector3(Ring, 0f, 0f);
        SpawnDirector director = Director(enemies, new[] { only });

        director.Begin(Composed(mode, 1), 0f);

        director.Tick(0f, Vector3.Zero, Stream());
        director.Tick(SpawnDirector.TelegraphTime, Vector3.Zero, Stream());

        Assert.That(enemies.Registry.AliveCount, Is.EqualTo(1));
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "The claim outlives the body's arrival by a telegraph, so the point is still held.");

        director.Tick(2f * SpawnDirector.TelegraphTime, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(2),
            "A claim expires on a clock rather than being held for the enemy's life — a Husk " +
            "walks away from where it arrived (rule 10).");

        director.Tick(3f * SpawnDirector.TelegraphTime, Vector3.Zero, Stream());

        Assert.That(enemies.Registry.AliveCount, Is.EqualTo(2));

        foreach (EnemySpawned spawned in _events.Of<EnemySpawned>())
        {
            Assert.That(spawned.Position, Is.EqualTo(only));
        }
    }

    // ---- Determinism and cost (rules 9, 11) ----------------------------------------------------

    [Test]
    public void Tick_Deterministic()
    {
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));

        List<string> first = RunTimeline(mode);
        List<string> second = RunTimeline(mode);

        Assert.That(second, Is.EqualTo(first),
            "The same plan and the same stream produce the same positions at the same moments — " +
            "which is the whole of what a seed promises about a stage.");
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        // Silent events, because a recorder boxes every struct it is handed: what is being measured
        // is the director's own frame, not the fixture's bookkeeping.
        var events = new SilentEvents();
        ModeSpec mode = Mode(budget: 112f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode, events);
        var director = new SpawnDirector(enemies, events, Points(8));
        WavePlan plan = Composed(mode, 1);

        Assert.That(plan.BodyCount(1), Is.EqualTo(DeviceCap), "A full 28-body stage (M2-04's cap).");

        director.Begin(plan, 0f);

        // The whole wave arrives during the warm-up, so every array this object grows has been
        // grown and every agent in the registry has lived once — a first life is the one spawn
        // that allocates, for its three depth modifiers (EnemySystem).
        float now = 0f;
        IRandomStream stream = Stream();

        while (enemies.LivingCount() < DeviceCap)
        {
            now += 1f / 60f;
            director.Tick(now, Vector3.Zero, stream);
        }

        // The steady state of a full arena: every id swept, the cap consulted, the overlap rule
        // asked. This is the frame a run actually spends its time in — a telegraph is once per
        // body, and the warm-up above is where those ran.
        AllocationAssert.None(() =>
        {
            now += 1f / 60f;
            director.Tick(now, Vector3.Zero, stream);
        });
    }

    // ---- Clear (rule 16) -----------------------------------------------------------------------

    [Test]
    public void Clear_ForgetsEverything()
    {
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);
        SpawnDirector director = Director(enemies, Points(8));

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());
        director.Tick(SpawnDirector.SpawnInterval, Vector3.Zero, Stream());

        Assert.That(director.PendingCount, Is.EqualTo(2));

        _events.Clear();

        director.Clear();

        Assert.That(director.Wave, Is.Zero);
        Assert.That(director.Stage, Is.Zero);
        Assert.That(director.PendingCount, Is.Zero);
        Assert.That(director.IsStageComplete, Is.False);
        Assert.That(_events.All, Is.Empty,
            "Silent for EnemySystem.Clear's reason: the scope is going away and with it every " +
            "subscriber an event could reach (rule 16).");

        director.Tick(10f, Vector3.Zero, Stream());

        Assert.That(_events.All, Is.Empty, "A director with no plan does nothing and says nothing.");
    }

    [Test]
    public void Clear_ReleasesClaims()
    {
        ModeSpec mode = Mode(budget: 8f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        EnemySystem enemies = Enemies(mode);

        var only = new Vector3(Ring, 0f, 0f);
        SpawnDirector director = Director(enemies, new[] { only });

        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1));

        director.Clear();
        director.Begin(Composed(mode, 1), 0f);
        director.Tick(0f, Vector3.Zero, Stream());

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(2),
            "A claim survives its stage by 1.6 s otherwise, which is exactly long enough to " +
            "refuse the next stage's opening wave.");
    }

    // ---- The session composes, begins and ticks it (rules 12, 13, 14) ---------------------------

    [Test]
    public void Session_ComposesAndBeginsOpeningStage()
    {
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        RunSession session = Session(mode);

        var plan = new SpawnPlan(
            new[] { new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(Ring, 0f, 0f)) },
            Points(8));

        session.Start(Config(4, plan));

        Assert.That(_events.Count<WaveStarted>(), Is.Zero,
            "Wave 1 waits out GD §7.1's arrival as of M2-10 — the director is handed its plan when "
                + "the two seconds are up, not when the run starts.");

        TickThroughArrival(session);

        WaveStarted started = _events.Single<WaveStarted>();

        Assert.That(started.Stage, Is.EqualTo(4),
            "The run's opening stage is the config's, not stage 1 — GD §4.5 forbids assuming it.");
        Assert.That(started.Wave, Is.EqualTo(1));

        Assert.That(IndexOf<WaveStarted>(), Is.GreaterThan(IndexOf<EnemySpawned>()),
            "The arena's dressed-in enemies are standing before wave 1 arrives (rule 13), so the " +
            "director's cap counts them.");

        session.Tick(Snapshot(1f / 60f));

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1),
            "And the session ticks it, or a composed stage would never happen.");
    }

    [Test]
    public void Session_NoSpawnPoints_DirectorInert()
    {
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        RunSession session = Session(mode);

        session.Start(Config(1, SpawnPlan.Empty));

        // Well past the two seconds of arrival, or this row would be asserting that nothing spawns
        // during a pause — which is rule 1's job and true of every arena, inert or not.
        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 400; i++)
            {
                session.Tick(Snapshot(1f / 60f));
            }
        });

        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero,
            "An arena with nowhere to put anything is inert rather than broken (rule 12) — M0's " +
            "empty grey box and every core fixture that starts a run both mean it.");
    }

    [Test]
    public void Session_UsesSpawnStreamOnly()
    {
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));

        _catalog = Catalog(mode: mode);

        // Every draw takes the last candidate, through the composition and the position alike.
        var random = new CountingRandom(new FixedRandom(0, Repeated(0.99f, 64)));
        var session = new RunSession(
            _catalog, random, _events, new RecordingIntents(), Capacity, DeviceCap, ProjectileCapacity);

        IReadOnlyList<Vector3> points = Points(8);

        session.Start(Config(1, new SpawnPlan(Array.Empty<SpawnPlan.Entry>(), points)));

        TickThroughArrival(session);

        session.Tick(Snapshot(1f / 60f));

        Assert.That(_events.Single<SpawnTelegraphed>().Position, Is.EqualTo(points[points.Count - 1]));

        Assert.That(random.SpawnDraws, Is.GreaterThan(0));
        Assert.That(random.OtherDraws, Is.Zero,
            "What appears and where is exactly what the Spawn stream is for; drawing from " +
            "another would make a new mechanic elsewhere shift every seeded run's spawns " +
            "(ADR-0011).");
    }

    [Test]
    public void Session_TicksDirectorAfterDeathCheck()
    {
        // One strike kills, so the death lands on a stated tick rather than after a fight.
        ModeSpec mode = Mode(budget: 32f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        RunSession session = Session(mode, contactDamage: 10_000f);

        // Dressed well out of reach, and the player walks onto it rather than it onto them: an
        // enemy in a core fixture never moves, because a move is an *intent* and only a body
        // reporting back through the snapshot changes a position. Out of reach matters as of
        // M2-10 — a Husk standing next to the player would kill them during the two seconds of
        // arrival, ending the run on a tick no wave had ever been due on, which is the one thing
        // this row must not be measuring.
        var ambush = new Vector3(12f, 0f, 0f);

        var plan = new SpawnPlan(
            new[] { new SpawnPlan.Entry(new ContentId(HuskId), ambush) },
            Points(8));

        session.Start(Config(1, plan));

        TickThroughArrival(session);

        // Half a second a tick, which is longer than the spawn interval: once the director is
        // running, a body is due on every one of them. So the tick the run ends on is unambiguously
        // a tick the director would have telegraphed on.
        int before = 0;

        for (int i = 0; i < 40 && _events.Count<RunEnded>() == 0; i++)
        {
            before = _events.Count<SpawnTelegraphed>();

            session.Tick(Snapshot(0.5f, ambush));
        }

        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1), "The strike landed and the run ended.");
        Assert.That(before, Is.GreaterThan(0),
            "Nothing had been telegraphed before the death, so this row would pass whatever the " +
            "director did on the tick that mattered.");
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(before),
            "A run that ended this tick spawns nothing, though a body was due (rule 14).");
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        EnemySystem enemies = Enemies(Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1)));

        Assert.Throws<ArgumentNullException>(() => new SpawnDirector(null, _events, Points(1)));
        Assert.Throws<ArgumentNullException>(() => new SpawnDirector(enemies, null, Points(1)));
        Assert.Throws<ArgumentNullException>(() => new SpawnDirector(enemies, _events, null));
    }

    [Test]
    public void Ctor_NonFiniteSpawnPoint_Throws()
    {
        EnemySystem enemies = Enemies(Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1)));

        // Not a wrong position — a silent one. Every comparison against a NaN is false, so the
        // point is neither accepted nor refused out loud: it simply never receives anything.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpawnDirector(enemies, _events, new[] { new Vector3(float.NaN, 0f, 0f) }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpawnDirector(enemies, _events, new[] { new Vector3(0f, 0f, float.PositiveInfinity) }));
    }

    [Test]
    public void Begin_Guards()
    {
        ModeSpec mode = Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        SpawnDirector director = Director(Enemies(mode), Points(8));

        Assert.Throws<ArgumentNullException>(() => director.Begin(null, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => director.Begin(Composed(mode, 1), float.NaN));

        // A plan nothing has been composed into reports no waves at all, so it would leave this
        // object claiming a stage it cannot run.
        Assert.Throws<ArgumentException>(() => director.Begin(new WavePlan(MaxWaves, 1), 0f));
    }

    [Test]
    public void Tick_Guards()
    {
        ModeSpec mode = Mode(budget: 4f, waves: 1, concurrency: DeviceCap, (HuskId, 1));
        SpawnDirector director = Director(Enemies(mode), Points(8));

        director.Begin(Composed(mode, 1), 0f);

        Assert.Throws<ArgumentNullException>(() => director.Tick(0f, Vector3.Zero, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => director.Tick(float.NaN, Vector3.Zero, Stream()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => director.Tick(float.PositiveInfinity, Vector3.Zero, Stream()));

        // Guarded on a per-frame path because the failure is silence rather than a crash: a NaN
        // player position refuses every spawn point for ever, with nothing in the log.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => director.Tick(0f, new Vector3(float.NaN, 0f, 0f), Stream()));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Ticks a run past GD §7.1's arrival, so the director has been handed its plan.
    /// </summary>
    /// <remarks>
    /// Exactly to the two seconds and not past them. The flow ticks after the director (M2-10 rule
    /// 13), so the tick that <em>ends</em> arrival hands the plan over and the director's first look
    /// at it is the one after — which is what lets a caller assert on that one tick.
    /// </remarks>
    private static void TickThroughArrival(RunSession session)
    {
        for (int i = 0; i < 4; i++)
        {
            session.Tick(Snapshot(0.5f));
        }
    }

    /// <summary>Ticks until <paramref name="bodies"/> of them are standing, and answers the clock.</summary>
    /// <remarks>
    /// Steps by the spawn interval rather than by a frame, so a row that wants a whole wave
    /// standing does not have to count frames — and the telegraph delay is waited out at the end
    /// so every body has actually arrived.
    /// </remarks>
    private float TickThrough(SpawnDirector director, EnemySystem enemies, int bodies)
    {
        float now = 0f;
        IRandomStream stream = Stream();

        for (int i = 0; i < bodies * 4 && enemies.Registry.AliveCount < bodies; i++)
        {
            director.Tick(now, Vector3.Zero, stream);

            now += SpawnDirector.SpawnInterval;
        }

        now += SpawnDirector.TelegraphTime;

        director.Tick(now, Vector3.Zero, stream);

        Assert.That(enemies.Registry.AliveCount, Is.EqualTo(bodies),
            "The fixture failed to get the wave standing, so whatever this row asserts next is " +
            "about the fixture rather than about the director.");

        return now;
    }

    /// <summary>One director's whole timeline, as strings a failure can be read from.</summary>
    private List<string> RunTimeline(ModeSpec mode)
    {
        var events = new RecordingEvents();
        EnemySystem enemies = Enemies(mode, events);
        var director = new SpawnDirector(enemies, events, Points(8));

        director.Begin(Composed(mode, 1), 0f);

        IRandomStream stream = Stream(Alternating(64));

        for (int i = 0; i < 200; i++)
        {
            director.Tick(i / 60f, Vector3.Zero, stream);
        }

        var timeline = new List<string>();

        foreach (SpawnTelegraphed ring in events.Of<SpawnTelegraphed>())
        {
            timeline.Add($"{ring.SpecId} at {ring.Position} firing at {ring.FiresAt:F3}");
        }

        return timeline;
    }

    /// <summary>The archetypes telegraphed so far, in order.</summary>
    private List<ContentId> Telegraphed()
    {
        var order = new List<ContentId>();

        foreach (SpawnTelegraphed ring in _events.Of<SpawnTelegraphed>())
        {
            order.Add(ring.SpecId);
        }

        return order;
    }

    /// <summary>Every id spawned so far, in spawn order.</summary>
    private List<int> SpawnedIds()
    {
        var ids = new List<int>();

        foreach (EnemySpawned spawned in _events.Of<EnemySpawned>())
        {
            ids.Add(spawned.Id);
        }

        return ids;
    }

    /// <summary>Kills <paramref name="count"/> of the still-breathing <paramref name="ids"/>.</summary>
    private static void Kill(EnemySystem enemies, List<int> ids, int count, float now)
    {
        int killed = 0;

        for (int i = 0; i < ids.Count && killed < count; i++)
        {
            if (!enemies.Registry.TryGet(ids[i], out EnemyAgent agent) || !agent.IsAlive)
            {
                continue;
            }

            // The blast target ApplyDamage requires as of M2-08 rule 3. Every archetype this fixture
            // spawns is a Husk, which carries no explosion block, so the player is inert here — but
            // the parameter is required rather than optional precisely so that a fixture which
            // starts killing Bloaters has to say who is standing nearby.
            enemies.ApplyDamage(ids[i], 10_000f, now, Bystander());

            killed++;
        }

        Assert.That(killed, Is.EqualTo(count), "The fixture ran out of living enemies to kill.");
    }

    /// <summary>A player for <c>EnemySystem.ApplyDamage</c> to resolve a blast against.</summary>
    /// <remarks>
    /// Silent ports, because nothing in this fixture asserts about the player: it spawns Husks, and
    /// a Husk does not explode. It exists only to satisfy the signature M2-08 widened.
    /// </remarks>
    private static PlayerCombat Bystander() =>
        new PlayerCombat(Oathbound(), new SilentEvents(), new RecordingIntents(), Capacity);

    /// <summary>What rule 2 says a wave's telegraph order must be: round-robin across its entries.</summary>
    private static List<ContentId> RoundRobin(WavePlan plan, int wave)
    {
        var remaining = new int[plan.EntryCount(wave)];

        for (int i = 0; i < remaining.Length; i++)
        {
            remaining[i] = plan.Entry(wave, i).Count;
        }

        var order = new List<ContentId>();

        while (order.Count < plan.BodyCount(wave))
        {
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] == 0)
                {
                    continue;
                }

                order.Add(plan.Entry(wave, i).SpecId);
                remaining[i]--;
            }
        }

        return order;
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

    private int LastIndexOf<T>()
        where T : struct
    {
        for (int i = _events.All.Count - 1; i >= 0; i--)
        {
            if (_events.All[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    private SpawnDirector Director(EnemySystem enemies, IReadOnlyList<Vector3> points) =>
        new SpawnDirector(enemies, _events, points);

    private EnemySystem Enemies(ModeSpec mode) => Enemies(mode, _events);

    private EnemySystem Enemies(ModeSpec mode, IDomainEvents events) => new EnemySystem(
        _catalog,
        events,
        new FixedRandom(0),
        new DepthScaling(mode.Scaling),
        Capacity);

    private RunSession Session(ModeSpec mode, float contactDamage = 8f)
    {
        // Rebuilt with the mode in it, because a run resolves its mode through the catalog — and
        // kept as this fixture's catalog, so a director built alongside reads the same content.
        _catalog = Catalog(contactDamage, mode);

        return new RunSession(
            _catalog, new FixedRandom(0), _events, new RecordingIntents(), Capacity, DeviceCap, ProjectileCapacity);
    }

    private static RunConfig Config(int stage, SpawnPlan plan) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        stage,
        plan);

    private static WorldSnapshot Snapshot(float dt, Vector3 playerPosition = default)
    {
        var snapshot = new WorldSnapshot(Capacity);

        snapshot.Dt = dt;
        snapshot.PlayerPosition = playerPosition;

        return snapshot;
    }

    /// <summary><paramref name="count"/> points on a ring, every one clear of the origin and of each other.</summary>
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

    /// <summary>
    /// A mode whose curves compose exactly the stage a row wants: a flat budget, a fixed wave
    /// count and a fixed concurrency.
    /// </summary>
    /// <remarks>
    /// The stat curves are GD §12.3's, because nothing here is about depth scaling and a mode
    /// needs valid ones. The wave and concurrency curves are pinned by making their step larger
    /// than any stage these rows compose at, which is how "always this many" is spelled in curves
    /// that are defined to rise.
    /// </remarks>
    private static ModeSpec Mode(
        float budget,
        int waves,
        int concurrency,
        params (string Id, int Stage)[] roster)
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(budget, 0f, 0f),
            new WaveCurve(waves, 1000, waves, waves),
            new ConcurrencyCurve(concurrency, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        var entries = new RosterEntry[roster.Length];

        for (int i = 0; i < roster.Length; i++)
        {
            entries[i] = new RosterEntry(new ContentId(roster[i].Id), roster[i].Stage);
        }

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            entries);
    }

    /// <summary>The stage, composed as a run composes it: one plan, sized at the curve's ceiling.</summary>
    private WavePlan Composed(ModeSpec mode, int stage, params float[] draws)
    {
        var plan = new WavePlan(MaxWaves, Math.Max(1, mode.Roster.Count));

        new WaveComposer(_catalog, new ThreatBudget(mode.Scaling, DeviceCap))
            .Compose(stage, mode, plan, Stream(draws));

        return plan;
    }

    private static ContentCatalog Catalog(float contactDamage = 8f, ModeSpec mode = null) =>
        new ContentCatalog(
            new[] { Oathbound() },
            new[]
            {
                Archetype(HuskId, HuskCost, contactDamage),
                Archetype(BloaterId, BloaterCost, contactDamage),
            },
            mode is null ? Array.Empty<ModeSpec>() : new[] { mode });

    private static EnemySpec Archetype(string id, int threatCost, float contactDamage) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: threatCost,
        isElite: false,
        contactDamage: contactDamage,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// The class every session row plays. CC §7's numbers where they matter and nothing invented:
    /// no row here is about the player, beyond being alive and then not.
    /// </summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it
        // would put a modifier and a stream of events into fixtures measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    private static IRandomStream Stream(params float[] values) => new FixedRandom(values).Spawn;

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

    private static float[] Repeated(float value, int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = value;
        }

        return values;
    }

    /// <summary>A stream that says how many times it was drawn from.</summary>
    private sealed class CountingStream : IRandomStream
    {
        private readonly IRandomStream _inner;

        public CountingStream(IRandomStream inner)
        {
            _inner = inner;
        }

        public int Draws { get; private set; }

        public float NextFloat()
        {
            Draws++;

            return _inner.NextFloat();
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            Draws++;

            return _inner.NextInt(minInclusive, maxExclusive);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            Draws++;

            return _inner.Range(minInclusive, maxInclusive);
        }

        public bool Chance(float probability)
        {
            Draws++;

            return _inner.Chance(probability);
        }
    }

    /// <summary>Every stream counted separately, so "the Spawn stream and no other" is checkable.</summary>
    private sealed class CountingRandom : IRandom
    {
        private readonly CountingStream _spawn;
        private readonly CountingStream _offers;
        private readonly CountingStream _affixes;
        private readonly CountingStream _drops;
        private readonly CountingStream _misc;

        public CountingRandom(IRandom inner)
        {
            Seed = inner.Seed;

            _spawn = new CountingStream(inner.Spawn);
            _offers = new CountingStream(inner.Offers);
            _affixes = new CountingStream(inner.Affixes);
            _drops = new CountingStream(inner.Drops);
            _misc = new CountingStream(inner.Misc);
        }

        public int Seed { get; }

        public IRandomStream Spawn => _spawn;

        public IRandomStream Offers => _offers;

        public IRandomStream Affixes => _affixes;

        public IRandomStream Drops => _drops;

        public IRandomStream Misc => _misc;

        public int SpawnDraws => _spawn.Draws;

        public int OtherDraws => _offers.Draws + _affixes.Draws + _drops.Draws + _misc.Draws;
    }

    /// <summary>
    /// An event sink that allocates nothing, for the one row that measures allocation.
    /// </summary>
    /// <remarks>
    /// <c>RecordingEvents</c> boxes every struct it is handed into a <c>List&lt;object&gt;</c>,
    /// which is exactly right for a fixture that reads events back and exactly wrong inside an
    /// allocation measurement — it would report the recorder's cost as the director's.
    /// </remarks>
    private sealed class SilentEvents : IDomainEvents
    {
        public void Publish<T>(in T evt)
            where T : struct
        {
        }
    }
}
