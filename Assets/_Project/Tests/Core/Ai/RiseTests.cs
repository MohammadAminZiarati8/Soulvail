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
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// CH §3.2's Rise: the chance, the stream it is drawn from, the cap it runs into, where a Wight
/// stands up — and what a death has to carry for any of that to be possible.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two subjects, one file, and the seam between them is the point.</b> The <c>Deaths_</c> rows are
/// about <c>EnemySystem</c> banking <em>where</em> something died beside the two numbers it already
/// banked; the <c>Rise_</c> rows are about what the passive does with one. They are together because
/// neither is worth anything alone: a drain nothing reads is a buffer, and a passive nothing feeds is
/// a probability.
/// </para>
/// <para>
/// <b>The two <c>Run_</c> rows are the ones M5-04a could not write, and that is
/// <see href="../../../../../Docs/plan/ROADMAP.md">ledger row 9(i)</see>.</b> That task's
/// <c>Run_MinionsTickAfterTheEnemiesAndAboveTheDeathCheck</c> needed a Wight standing inside a live
/// <c>RunSession</c>, nothing produced one, and <c>RunState.Minions</c> is <c>internal</c> because
/// AR §18.2 says a live object is never handed out — so the claim shipped untested rather than
/// shipping an invariant violation for one row. Rise is the producer, so the row is written here,
/// against the whole frame: the player's Censer kills a Frail through <c>ReportConeHits</c>, the
/// drain raises a Wight on the corpse, the Wight strikes the Bomb standing next to it, and the
/// Bomb's blast kills the player — <b>all on one tick, EnemyDied before PlayerDied, and the rise
/// still happening in between</b>.
/// </para>
/// <para>
/// <b>Nothing here is reachable in play.</b> The Gravecaller is not selectable until M5-07 and a
/// Wight has no view until M5-05a, so every row is about a mechanism rather than about something a
/// player can see. It is M5-02's and M5-04a's bargain again.
/// </para>
/// <para>
/// <b>The fixture's Gravecaller carries a <c>Cone</c> where the asset carries a
/// <c>Projectile</c></b> — <c>MinionSystemTests</c>' choice, for <c>StageFlowTests.KillTheWave</c>'s
/// reason: <c>ReportConeHits</c> is the only door a core test has into a live session's damage, since
/// <c>RunState.Combat</c> and <c>RunState.Enemies</c> are both <c>internal</c>. What the rows are
/// about is the tick order, and the weapon that starts it is not part of the claim.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RiseTests
{
    private const string WightId = "minion.wight";
    private const string HuskId = "enemy.husk";

    /// <summary>A dummy with more hit points than a row can spend, for the allocation sweep.</summary>
    private const string AnvilId = "enemy.anvil";

    /// <summary>Five hit points and nothing else: one Censer swing, one corpse, one Wight.</summary>
    private const string FrailId = "enemy.frail";

    /// <summary>Five hit points and a blast that kills whoever raised the thing that killed it.</summary>
    private const string BombId = "enemy.bomb";

    /// <summary>A body authored <c>Boss</c>, for the one rule <c>EnemyDeath.WasBoss</c> exists for.</summary>
    private const string WardenBodyId = "enemy.warden";

    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string DescentId = "mode.descent";

    // The Gravecaller's authored minion block, from Gravecaller.asset (M5-02).
    private const int Cap = 3;
    private const float Lifespan = 20f;
    private const float RiseChance = 0.25f;
    private const float MinionMaxHp = 20f;
    private const float MinionSpeed = 3f;
    private const float MinionDamage = 8f;
    private const float AttackInterval = 1f;
    private const float Reach = 1.5f;

    private const float HuskMaxHp = 36f;

    /// <summary>Under the Wight's 8, so one strike finishes it.</summary>
    private const float FrailMaxHp = 5f;

    /// <summary>Far past the Gravecaller's 80, and it reaches the player at two metres.</summary>
    private const float BombDamage = 500f;

    private const float BombRadius = 6f;

    private const int Capacity = 32;
    private const int ProjectileCapacity = 8;
    private const int Seed = 99;

    private const float Frame = 0.1f;
    private const float RunFrame = 1f / 60f;
    private const float Tolerance = 1e-4f;

    /// <summary>
    /// Long enough that the allocation row never exhausts its script. A spent script stops advancing
    /// and answers 0.5 for ever, which would silently stop measuring the spawn branch.
    /// </summary>
    private const int ScriptLength = 16_384;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private FixedRandom _random;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private MinionSystem _minions;
    private EnemyDeath[] _buffer;

    /// <summary>The counter the allocation row drives from — <c>MinionSystemTests</c>' field.</summary>
    private int _allocationStep;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = NewEnemies(_events);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        MinionSpec wight = Wight();

        // The recipe a run would build from this spec (M5-06a rule 3) — one per army, so the
        // bodies this fixture raises are born from the numbers a Legion node would move.
        _minions = new MinionSystem(wight, new MinionRecipe(wight), _events, _intents);
        _buffer = new EnemyDeath[Capacity];
        _allocationStep = 0;
    }

    // ---- Rule 1: the draw, and the stream it comes from -------------------------------------------

    [Test]
    public void Rise_AQuarterOfKillsStandBackUp()
    {
        const int Deaths = 400;

        // Below 0.25 exactly once in four. IRandomStream.Chance is `NextFloat() < probability`, so
        // 0.1 succeeds against the authored chance and 0.6 does not.
        var script = new float[Deaths];

        for (int i = 0; i < Deaths; i++)
        {
            script[i] = i % 4 == 0 ? 0.1f : 0.6f;
        }

        RisePassive rise = Rise(script);

        // The cap lifted to the ceiling and the army expired between deaths, so the *only* thing
        // deciding how many stand up is the draw. Measured against a cap of three this row would be
        // measuring the cap, which is the next row's subject.
        _minions.Cap.Add(new Modifier(ModifierKind.Flat, MinionSystem.MaxConcurrent, this));

        float now = 0f;

        for (int i = 0; i < Deaths; i++)
        {
            now += Lifespan + 1f;

            _minions.Tick(Frame, now, _enemies, _player);

            Offer(rise, now, Death(Vector3.Zero));
        }

        Assert.That(rise.Raised, Is.EqualTo(100), "A quarter of four hundred — CH §3.2's 25 %.");
        Assert.That(_events.Count<MinionSpawned>(), Is.EqualTo(100), "And each of them was announced.");
    }

    [Test]
    public void Rise_DrawsOncePerDeathWhateverTheOutcome()
    {
        const int Deaths = 50;

        RisePassive rise = Rise(Repeated(0.9f, Deaths));

        for (int i = 0; i < Deaths; i++)
        {
            Offer(rise, 0f, Death(Vector3.Zero));
        }

        Assert.That(rise.Raised, Is.Zero, "Sanity: 0.9 is not under 0.25, so none of them succeeded.");

        // **Rule 1's reproducibility claim, and the whole reason the draw comes before every reason
        // a raise might be refused.** A sequence that skipped a draw when nothing came of it would
        // make a seeded run's later rises depend on how crowded an earlier fight happened to be.
        Assert.That(_random.Capture().Drops, Is.EqualTo((ulong)Deaths));
    }

    [Test]
    public void Rise_DrawsFromTheDropsStream()
    {
        const int Deaths = 20;

        RisePassive rise = Rise(Repeated(0.1f, Deaths));

        RandomState before = _random.Capture();

        for (int i = 0; i < Deaths; i++)
        {
            // Expired between each, so the cap never refuses — this row is about the stream, and a
            // refusal at the cap would still have drawn but would muddy what it is saying.
            float now = (i + 1) * (Lifespan + 1f);

            _minions.Tick(Frame, now, _enemies, _player);

            Offer(rise, now, Death(Vector3.Zero));
        }

        RandomState after = _random.Capture();

        // **`Drops` and no other, which is what makes this task cost no sixth stream and no
        // RunSnapshot v4** (rule 1, AR §18.3). It is also why every seeded run played before today
        // replays identically: nothing else has ever drawn from this stream.
        Assert.That(after.Drops - before.Drops, Is.EqualTo((ulong)Deaths));

        Assert.That(after.Spawn, Is.EqualTo(before.Spawn), "Spawn: what appears and where, untouched.");
        Assert.That(after.Offers, Is.EqualTo(before.Offers), "Offers: the tree's draws, untouched.");
        Assert.That(after.Affixes, Is.EqualTo(before.Affixes), "Affixes, untouched.");
        Assert.That(after.Misc, Is.EqualTo(before.Misc), "Misc, untouched.");
    }

    // ---- Rules 3 and 11: where one stands up, and what does not -----------------------------------

    [Test]
    public void Rise_WhereTheEnemyFell()
    {
        var fell = new Vector3(7f, 0f, -3f);

        RisePassive rise = Rise(0.1f);

        Offer(rise, 4f, Death(fell));

        MinionSpawned spawned = _events.Single<MinionSpawned>();

        // CH §3.2 is "25 % of enemies killed rise as Wights" — the corpse *is* the Wight, so there is
        // no other position it could stand up at (rule 3).
        Assert.That(spawned.Position, Is.EqualTo(fell));
        Assert.That(spawned.SpecId, Is.EqualTo(Id(WightId)));
        Assert.That(rise.Raised, Is.EqualTo(1));

        // And on the tick it fell: the clock it expires on is measured from the moment it was
        // offered, not from the next retarget or the next wave.
        Assert.That(_minions.Alive[0].ExpiresAt, Is.EqualTo(4f + Lifespan).Within(Tolerance));
    }

    [Test]
    public void Rise_ASuccessAtTheCapIsSpent()
    {
        RisePassive rise = Rise(Repeated(0.1f, 8));

        for (int i = 0; i < Cap; i++)
        {
            Offer(rise, 0f, Death(Vector3.Zero));
        }

        Assert.That(rise.Raised, Is.EqualTo(Cap), "Sanity: the army is full at the authored cap.");
        Assert.That(_minions.Count, Is.EqualTo(Cap));

        ulong before = _random.Capture().Drops;

        Assert.DoesNotThrow(() => Offer(rise, 0f, Death(new Vector3(9f, 0f, 9f))));

        // **A cap is not a queue** (rule 3). The draw succeeded, the raise was refused silently, and
        // nothing is banked for later — which is the honest reading of "base cap 3".
        Assert.That(rise.Raised, Is.EqualTo(Cap), "Raised counts bodies that stood up, not draws that won.");
        Assert.That(_minions.Count, Is.EqualTo(Cap));
        Assert.That(_events.Count<MinionSpawned>(), Is.EqualTo(Cap), "A refused raise announces nothing.");

        Assert.That(
            _random.Capture().Drops - before,
            Is.EqualTo(1UL),
            "And the draw was spent all the same, which is rule 1 holding at the one place it costs "
                + "something.");
    }

    [Test]
    public void Rise_ABossDoesNotRise()
    {
        RisePassive rise = Rise(0.1f);

        // Spawned through the ordinary door and never ticked: EnemySystem.Tick refuses a Boss-authored
        // agent with no behaviour, and this row is about a death rather than about a fight.
        EnemyAgent warden = _enemies.Spawn(Id(WardenBodyId), new Vector3(2f, 0f, 0f));

        _enemies.ApplyDamage(warden.Id, 10_000f, now: 0f, _player);

        int drained = _enemies.DrainDeaths(_buffer);

        Assert.That(drained, Is.EqualTo(1));
        Assert.That(
            _buffer[0].WasBoss,
            Is.True,
            "Read off EnemySpec.Behaviour, which is the one thing SpawnBoss authors that an ordinary "
                + "spawn cannot fake.");

        rise.OnDeaths(new ReadOnlySpan<EnemyDeath>(_buffer, 0, drained), now: 0f);

        // **The rule EnemyDeath.WasBoss exists for** (rule 11). A 4 200 HP Warden raised as a 20 HP
        // Wight is either absurd or free depending on which numbers it kept.
        Assert.That(rise.Raised, Is.Zero);
        Assert.That(_events.Count<MinionSpawned>(), Is.Zero);

        Assert.That(_random.Capture().Drops, Is.EqualTo(1UL), "The draw happened all the same (rule 1).");
    }

    // ---- Rule 5: the chance is a Stat, read through a clamp ---------------------------------------

    [Test]
    public void Rise_ChanceIsAStat()
    {
        RisePassive rise = Rise(0.4f, 0.4f);

        Assert.That(rise.Chance.Base, Is.EqualTo(RiseChance).Within(Tolerance), "The authored 0.25.");

        // The control, and the reason this row has two halves: at base, 0.4 is not under 0.25.
        Offer(rise, 0f, Death(Vector3.Zero));

        Assert.That(rise.Raised, Is.Zero);

        // Where CH §3.2's Legion nodes go — a Flat modifier on a live Stat, not a second mechanism
        // (rule 5, ADR-0008).
        rise.Chance.Add(new Modifier(ModifierKind.Flat, 0.25f, this));

        Offer(rise, 0f, Death(Vector3.Zero));

        Assert.That(rise.Raised, Is.EqualTo(1), "The same draw, against a chance a node moved.");
    }

    [Test]
    public void Rise_RefusesAnUnreadableChance()
    {
        const int Deaths = 100;

        // Every draw at the floor, so the *only* thing that can refuse a raise here is the clamp.
        AssertNothingRises("NaN", chance => Unreadable(chance, nan: true), Deaths);
        AssertNothingRises("+∞", chance => Unreadable(chance, nan: false), Deaths);
        AssertNothingRises(
            "−1",
            chance => chance.Add(new Modifier(ModifierKind.Flat, -1.25f, this)),
            Deaths);
    }

    [Test]
    public void Rise_AlwaysAtOrAboveOne()
    {
        const int Deaths = 10;

        // 0.999 is as close to certain as NextFloat can ever come — it is in [0, 1) — so a chance
        // driven to 1.25 has to take every one of them.
        RisePassive rise = Rise(Repeated(0.999f, Deaths));

        rise.Chance.Add(new Modifier(ModifierKind.Flat, 1f, this));

        _minions.Cap.Add(new Modifier(ModifierKind.Flat, MinionSystem.MaxConcurrent, this));

        float now = 0f;

        for (int i = 0; i < Deaths; i++)
        {
            // Expired between each, so "under the cap" is true of every one of the ten.
            now += Lifespan + 1f;

            _minions.Tick(Frame, now, _enemies, _player);

            Offer(rise, now, Death(Vector3.Zero));
        }

        Assert.That(rise.Raised, Is.EqualTo(Deaths));
    }

    // ---- Rule 12: the frame path ------------------------------------------------------------------

    [Test]
    public void Rise_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        EnemySystem enemies = NewEnemies(silent);
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);

        // A short life and the full ceiling, so the army is genuinely recycled under the probe rather
        // than standing full and refusing every raise — which would measure the cheap branch only.
        MinionSpec spec = Wight(cap: MinionSystem.MaxConcurrent, lifespan: 1f);
        var minions = new MinionSystem(spec, new MinionRecipe(spec), silent, intents);

        var script = new float[ScriptLength];

        for (int i = 0; i < script.Length; i++)
        {
            script[i] = i % 4 == 0 ? 0.1f : 0.6f;
        }

        var random = new FixedRandom(Seed).SetDrops(script);
        var rise = new RisePassive(Wight(), minions, random.Drops);
        var buffer = new EnemyDeath[Capacity];

        float clock = 0f;

        void Step()
        {
            _allocationStep++;

            clock += Frame;

            // One kill through the one door a death comes through, then the pull and the offer —
            // the whole of what Rise puts behind a kill, measured together.
            EnemyAgent body = enemies.Spawn(Id(AnvilId), Vector3.Zero);

            enemies.ApplyDamage(body.Id, 2_000_000f, clock, player);
            enemies.Despawn(body.Id);

            minions.Tick(Frame, clock, enemies, player);

            int drained = enemies.DrainDeaths(buffer);

            rise.OnDeaths(new ReadOnlySpan<EnemyDeath>(buffer, 0, drained), clock);
        }

        // Warmed outside the measurement, so the jit, the modifier lists, the registry's agent pool
        // and the army's eight bodies are not what is being counted.
        for (int i = 0; i < 600; i++)
        {
            intents.Clear();

            Step();
        }

        Assert.That(rise.Raised, Is.GreaterThan(100), "Sanity: the spawn branch is inside the probe.");

        _allocationStep = 0;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            Step();
        });

        Assert.That(_allocationStep, Is.GreaterThan(10_000), "The probe is live.");
        Assert.That(
            random.Capture().Drops,
            Is.LessThan((ulong)ScriptLength),
            "The script outlasted the probe — a spent one answers 0.5 for ever without advancing, "
                + "which would stop measuring the spawn branch halfway through.");
    }

    // ---- Rule 2: what a death carries, and what drains it -----------------------------------------

    [Test]
    public void Deaths_AreDrainedOldestFirstAndEmptied()
    {
        EnemyAgent first = SpawnHusk(new Vector3(1f, 0f, 0f));
        EnemyAgent second = SpawnHusk(new Vector3(2f, 0f, 0f));
        EnemyAgent third = SpawnHusk(new Vector3(3f, 0f, 0f));

        Kill(second);
        Kill(third);
        Kill(first);

        int drained = _enemies.DrainDeaths(_buffer);

        Assert.That(drained, Is.EqualTo(3));

        // Kill order, which is the order Rise raises in — and therefore the order a seeded run's
        // draws are consumed in.
        Assert.That(_buffer[0].Position.X, Is.EqualTo(2f).Within(Tolerance));
        Assert.That(_buffer[1].Position.X, Is.EqualTo(3f).Within(Tolerance));
        Assert.That(_buffer[2].Position.X, Is.EqualTo(1f).Within(Tolerance));

        // Take-and-clear in one call, for DrainXp's reason: two calls is a pair a future caller can
        // get half of, and the half that is forgotten raises a stage's dead every tick for ever.
        Assert.That(_enemies.DrainDeaths(_buffer), Is.Zero);
    }

    [Test]
    public void Deaths_CarryTheSpecAndThePosition()
    {
        var stood = new Vector3(-4f, 0f, 11f);

        EnemyAgent husk = SpawnHusk(stood);

        Kill(husk);

        Assert.That(_enemies.DrainDeaths(_buffer), Is.EqualTo(1));

        Assert.That(_buffer[0].SpecId, Is.EqualTo(Id(HuskId)));
        Assert.That(_buffer[0].Position, Is.EqualTo(stood));
        Assert.That(_buffer[0].WasBoss, Is.False);

        // Banked beside the two counters and on the same line, so "every death pays" stays one rule
        // with three currencies rather than three rules that can drift apart.
        Assert.That(_enemies.DrainKills(), Is.EqualTo(1));
    }

    [Test]
    public void Deaths_ADespawnIsNotADeath()
    {
        EnemyAgent queued = SpawnHusk(new Vector3(5f, 0f, 0f));

        _enemies.DespawnAtEndOfTick(queued.Id);
        _enemies.Tick(Context());

        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(1), "Sanity: it was retired.");
        Assert.That(_enemies.DrainDeaths(_buffer), Is.Zero, "Only a kill is a death.");

        SpawnHusk(new Vector3(6f, 0f, 0f));

        _enemies.Clear();

        Assert.That(_enemies.DrainDeaths(_buffer), Is.Zero);

        // And the other half of Clear: a death that *was* banked goes with the arena that produced
        // it, because its position belongs to a room that no longer exists.
        EnemyAgent doomed = SpawnHusk(new Vector3(7f, 0f, 0f));

        Kill(doomed);

        _enemies.Clear();

        Assert.That(_enemies.DrainDeaths(_buffer), Is.Zero);
    }

    [Test]
    public void Deaths_AFullBufferDropsTheOldest()
    {
        // One more than the buffer holds, and never drained. Unreachable in a live run — RunSession
        // drains every tick, whatever class it is running — and asserted rather than left to be
        // discovered, which is what rule 2 asks for.
        //
        // Spawned, killed and retired one at a time, because the registry holds corpses and would
        // otherwise be full long before the buffer is.
        for (int i = 0; i <= Capacity; i++)
        {
            EnemyAgent body = SpawnHusk(new Vector3(i, 0f, 0f));

            Kill(body);

            _enemies.Despawn(body.Id);
        }

        var wide = new EnemyDeath[Capacity * 2];

        int drained = _enemies.DrainDeaths(wide);

        Assert.That(drained, Is.EqualTo(Capacity), "It does not grow — growing would allocate on the kill path.");

        Assert.That(
            wide[0].Position.X,
            Is.EqualTo(1f).Within(Tolerance),
            "The oldest fell off the front. Losing the newest instead would make the arena's most "
                + "recent death the one thing that cannot be acted on.");

        Assert.That(wide[Capacity - 1].Position.X, Is.EqualTo((float)Capacity).Within(Tolerance));
    }

    // ---- Rules 4 and 10: inside a live run --------------------------------------------------------

    [Test]
    public void Run_MinionsTickAfterTheEnemiesAndAboveTheDeathCheck()
    {
        // **The row M5-04a could not write** (ledger row 9(i)). A Wight is standing, it strikes
        // inside the minion step, its quarry's blast kills the player — and the whole of that lands
        // on one tick, in an order the rest of the frame depends on.
        Type[] types = TypesOf(PlayTheDeathTick());

        int enemyDied = IndexOf(types, typeof(EnemyDied));
        int struck = IndexOf(types, typeof(MinionStruck));
        int playerDied = IndexOf(types, typeof(PlayerDied));
        int runEnded = IndexOf(types, typeof(RunEnded));

        Assert.That(
            enemyDied,
            Is.GreaterThanOrEqualTo(0),
            "A Wight killed its quarry on this tick — which is the minion step running at all.");

        Assert.That(
            playerDied,
            Is.GreaterThan(enemyDied),
            "EnemyDied before PlayerDied: the kill goes through EnemySystem.ApplyDamage, and the "
                + "blast that follows it is what reaches the person who raised the Wight "
                + "(M5-04a rule 11).");

        Assert.That(
            struck,
            Is.GreaterThan(playerDied),
            "MinionStruck is published after the damage, so the enemy's death and the blast behind "
                + "it are on the wire first and the strike says the extra thing that happened.");

        Assert.That(
            runEnded,
            Is.GreaterThan(playerDied),
            "And the death check is still below the whole pass: the run ends on the tick it should.");
    }

    [Test]
    public void Run_ARiseOnTheTickThePlayerDies()
    {
        Type[] types = TypesOf(PlayTheDeathTick());

        int playerDied = IndexOf(types, typeof(PlayerDied));
        int spawned = IndexOf(types, typeof(MinionSpawned));
        int runEnded = IndexOf(types, typeof(RunEnded));

        // **Rule 4's half that has a cost.** The drain sits above the death check, so a kill landing
        // on the tick the player dies is still worth its rise — the alternative is a kill silently
        // losing one depending on when the player happened to die.
        Assert.That(
            spawned,
            Is.GreaterThanOrEqualTo(0),
            "The Wight raised by the kill that killed the player.");

        Assert.That(spawned, Is.GreaterThan(playerDied), "Raised after the death it was earned by…");
        Assert.That(runEnded, Is.GreaterThan(spawned), "…and before the run ended.");
    }

    [Test]
    public void Run_AGravecallerRunDrawsFromDrops()
    {
        // The wiring RunSession.Start does and no test above can see: this run's passive was handed
        // `_random.Drops` and not one of the other four streams (rule 1).
        var random = new FixedRandom(Seed).SetDrops(Repeated(0.1f, 64));

        RandomState before = random.Capture();

        PlayARun(GravecallerId, random);

        RandomState after = random.Capture();

        Assert.That(after.Drops, Is.GreaterThan(before.Drops), "Kills happened and each one drew.");
        Assert.That(_events.Count<MinionSpawned>(), Is.GreaterThan(0), "And some of them rose.");

        Assert.That(after.Spawn, Is.EqualTo(before.Spawn), "Spawn: a mode with no roster composes nothing.");
        Assert.That(after.Offers, Is.EqualTo(before.Offers));
        Assert.That(after.Affixes, Is.EqualTo(before.Affixes));
        Assert.That(after.Misc, Is.EqualTo(before.Misc));
    }

    [Test]
    public void Run_AnOathboundRunDrawsNothing()
    {
        // **Rule 10, and the row that keeps this task's blast radius at one class.** An Oathbound run
        // holds no MinionSystem and no RisePassive, so it draws nothing, ingests nothing and ticks
        // nothing — its whole share of M5-04b is one array write per kill and a copy of an empty
        // buffer.
        var random = new FixedRandom(Seed).SetDrops(Repeated(0.1f, 64));

        RandomState before = random.Capture();

        PlayARun(OathboundId, random);

        Assert.That(_events.Count<EnemyDied>(), Is.GreaterThan(0), "Sanity: something did die.");

        Assert.That(
            random.Capture().Drops,
            Is.EqualTo(before.Drops),
            "Every seeded Oathbound run ever played replays identically — which is what a sixth "
                + "stream would have cost, and a RunSnapshot v4 with it (rule 1).");

        Assert.That(_events.Count<MinionSpawned>(), Is.Zero);
        Assert.That(_intents.MinionMoves, Is.Empty);
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Rise_Guards()
    {
        IRandomStream stream = new FixedRandom(Seed).Drops;

        Assert.Throws<ArgumentNullException>(() => new RisePassive(null, _minions, stream));
        Assert.Throws<ArgumentNullException>(() => new RisePassive(Wight(), null, stream));
        Assert.Throws<ArgumentNullException>(() => new RisePassive(Wight(), _minions, null));

        var rise = new RisePassive(Wight(), _minions, stream);

        // A non-finite clock is an expiry that can never be compared against — every Wight immortal
        // and silent — so it is refused where the caller that invented it is still on the stack.
        Assert.Throws<ArgumentOutOfRangeException>(() => rise.OnDeaths(default, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => rise.OnDeaths(default, float.PositiveInfinity));

        // An empty offer is the ordinary case on almost every tick of every run, and says nothing.
        Assert.DoesNotThrow(() => rise.OnDeaths(default, 1f));
        Assert.That(rise.Raised, Is.Zero);
    }

    [Test]
    public void Deaths_Guards()
    {
        // Nothing pending and nowhere to put it is not an error: RunSession drains unconditionally,
        // including on the overwhelming majority of ticks where nothing died.
        Assert.That(_enemies.DrainDeaths(Span<EnemyDeath>.Empty), Is.Zero);

        Kill(SpawnHusk(Vector3.Zero));

        // And a span too short is loud rather than truncating: a silently dropped death is a kill
        // that never rises, and the caller sized its buffer from the same capacity this one was.
        Assert.Throws<ArgumentException>(() => _enemies.DrainDeaths(Span<EnemyDeath>.Empty));

        Assert.That(_enemies.DrainDeaths(_buffer), Is.EqualTo(1), "And nothing was lost by the refusal.");
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>
    /// Plays the scenario both <c>Run_</c> ordering rows are about, and answers the last tick's
    /// events — the one the player died on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Frail at one metre and a Bomb at two, with the player at the origin. The Censer swings, the
    /// fixture answers the swing the way Unity would, and the Frail dies between ticks. The next
    /// tick's drain raises a Wight <em>on the corpse</em> — a metre from the Bomb, inside its
    /// 1.5 m reach — and the tick after that is the one every assertion is about.
    /// </para>
    /// <para>
    /// <b>Both enemies are <c>Static</c> and the Bomb explodes anyway</b>, which is M2-08 rule 1: an
    /// explosion is triggered by the spec carrying an <c>ExplosionSpec</c> and never by the behaviour
    /// kind. It is what lets this row be about an ordering rather than about a Bloater's fuse.
    /// </para>
    /// </remarks>
    private IReadOnlyList<object> PlayTheDeathTick()
    {
        var random = new FixedRandom(Seed).SetDrops(Repeated(0.1f, 64));

        RunSession session = Session(random);

        session.Start(Config(GravecallerId));

        var snapshot = new WorldSnapshot(Capacity);
        var reported = false;

        for (int i = 0; i < 600; i++)
        {
            _events.Clear();
            _intents.Clear();

            snapshot.Clear();
            snapshot.Dt = RunFrame;
            snapshot.PlayerPosition = Vector3.Zero;

            session.Tick(snapshot);

            if (!session.IsRunning)
            {
                Assert.That(
                    reported,
                    Is.True,
                    "The run ended before the fixture had killed anything, so this is not the tick "
                        + "the rows are about.");

                return _events.All;
            }

            if (reported || _intents.ConeHits.Count == 0)
            {
                continue;
            }

            // The fact phase: the body reports who the wedge touched, one step after the tick that
            // decided to swing. Only the Frail — the Bomb is the Wight's to kill.
            Span<int> hits = stackalloc int[1];
            hits[0] = 1;

            session.ReportConeHits(hits);

            reported = true;
        }

        Assert.Fail("The player never died: six hundred ticks and the Wight never reached the Bomb.");

        return null;
    }

    /// <summary>
    /// Starts <paramref name="characterId"/>, kills the Frail through the one door a core test has,
    /// and ticks until the run ends or three hundred frames have gone by.
    /// </summary>
    private void PlayARun(string characterId, FixedRandom random)
    {
        RunSession session = Session(random);

        session.Start(Config(characterId));

        var snapshot = new WorldSnapshot(Capacity);
        var reported = false;

        for (int i = 0; i < 300 && session.IsRunning; i++)
        {
            _intents.Clear();

            snapshot.Clear();
            snapshot.Dt = RunFrame;
            snapshot.PlayerPosition = Vector3.Zero;

            session.Tick(snapshot);

            if (reported || !session.IsRunning || _intents.ConeHits.Count == 0)
            {
                continue;
            }

            Span<int> hits = stackalloc int[1];
            hits[0] = 1;

            session.ReportConeHits(hits);

            reported = true;
        }

        Assert.That(reported, Is.True, "Sanity: the weapon swung and the fixture answered it.");
    }

    /// <summary>The passive, over the fixture's army and a <c>Drops</c> stream scripted for the row.</summary>
    /// <remarks>
    /// Scripted through <c>SetDrops</c> rather than through the shared constructor, so that
    /// <c>Rise_DrawsFromTheDropsStream</c> can watch the other four stand still.
    /// </remarks>
    private RisePassive Rise(params float[] drops)
    {
        _random = new FixedRandom(Seed).SetDrops(drops);

        return new RisePassive(Wight(), _minions, _random.Drops);
    }

    /// <summary>
    /// Offers <paramref name="deaths"/> to <paramref name="rise"/> — the one-line form of what
    /// <c>RunSession.Tick</c> does with a drained buffer.
    /// </summary>
    private static void Offer(RisePassive rise, float now, params EnemyDeath[] deaths) =>
        rise.OnDeaths(new ReadOnlySpan<EnemyDeath>(deaths), now);

    private static EnemyDeath Death(Vector3 at) => new EnemyDeath(Id(HuskId), at, wasBoss: false);

    /// <summary>
    /// Drives <paramref name="chance"/> to a value no <see cref="Modifier"/> is allowed to state.
    /// </summary>
    /// <remarks>
    /// Both <c>Stat.Base</c> and <c>Modifier</c> refuse a non-finite input, so the only route to a
    /// non-finite <c>Value</c> is arithmetic — which is precisely rule 5's "a modifier stack nobody
    /// can read", reached the way a stack of Legion nodes would reach it rather than by assigning a
    /// number the API refuses. <c>Stat.Recompute</c> applies each <c>PercentMult</c> in turn
    /// (ADR-0008), so two at <see cref="float.MaxValue"/> overflow the product to +∞, and a third at
    /// −1 multiplies that infinity by zero, which is NaN.
    /// </remarks>
    private void Unreadable(Stat chance, bool nan)
    {
        chance.Add(new Modifier(ModifierKind.PercentMult, float.MaxValue, this));
        chance.Add(new Modifier(ModifierKind.PercentMult, float.MaxValue, this));

        if (!nan)
        {
            Assert.That(float.IsPositiveInfinity(chance.Value), Is.True, "Sanity: the stack overflowed.");

            return;
        }

        chance.Add(new Modifier(ModifierKind.PercentMult, -1f, this));

        Assert.That(float.IsNaN(chance.Value), Is.True, "Sanity: infinity times zero.");
    }

    /// <summary>
    /// A fresh passive whose chance <paramref name="corrupt"/> has broken, offered
    /// <paramref name="deaths"/> deaths at the floor of the stream, raising nothing.
    /// </summary>
    private void AssertNothingRises(string what, Action<Stat> corrupt, int deaths)
    {
        _minions.Clear();
        _events.Clear();

        RisePassive rise = Rise(Repeated(0f, deaths));

        corrupt(rise.Chance);

        for (int i = 0; i < deaths; i++)
        {
            Offer(rise, 0f, Death(Vector3.Zero));
        }

        // **The direction every comparison in core takes** (rule 5, AR §18.3): a modifier stack
        // nobody can read must not become a guaranteed army. A draw of exactly zero is the most
        // generous one there is, and it still raises nobody.
        Assert.That(rise.Raised, Is.Zero, $"A live chance of {what} raised something.");
        Assert.That(_events.Count<MinionSpawned>(), Is.Zero, $"A live chance of {what} announced something.");
    }

    private EnemyAgent SpawnHusk(Vector3 at) => _enemies.Spawn(Id(HuskId), at);

    private void Kill(EnemyAgent agent) => _enemies.ApplyDamage(agent.Id, 10_000f, now: 0f, _player);

    private EnemyTickContext Context() => new EnemyTickContext(
        Frame, 0f, _player, _intents, _events, new ProjectileSystem(_events, ProjectileCapacity), _enemies);

    private static float[] Repeated(float value, int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = value;
        }

        return values;
    }

    private static Type[] TypesOf(IReadOnlyList<object> events)
    {
        var types = new Type[events.Count];

        for (int i = 0; i < events.Count; i++)
        {
            types[i] = events[i].GetType();
        }

        return types;
    }

    private static int IndexOf(Type[] types, Type wanted) => Array.IndexOf(types, wanted);

    private EnemySystem NewEnemies(IDomainEvents events) => new EnemySystem(
        Catalog(),
        events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        Capacity);

    private RunSession Session(FixedRandom random) => new RunSession(
        Catalog(),
        random,
        _events,
        _intents,
        new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
        Capacity,
        Capacity,
        ProjectileCapacity);

    /// <summary>A Frail a metre away and a Bomb two, with nothing else in the arena.</summary>
    private static RunConfig Config(string characterId) => new RunConfig(
        Id(DescentId),
        Id(characterId),
        Seed,
        1,
        new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(Id(FrailId), new Vector3(0f, 0f, 1f)),
            new SpawnPlan.Entry(Id(BombId), new Vector3(0f, 0f, 2f)),
        }),
        restore: null);

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound(), Gravecaller() },
        new[] { Husk(), Anvil(), Frail(), Bomb(), WardenBody() },
        new[] { Descent() });

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>The Gravecaller's authored minion block (M5-02), unless a row needs it wider.</summary>
    private static MinionSpec Wight(int cap = Cap, float lifespan = Lifespan) => new MinionSpec(
        Id(WightId),
        new LocKey("minion.wight.name"),
        cap,
        lifespan,
        RiseChance,
        MinionMaxHp,
        MinionSpeed,
        MinionDamage,
        AttackInterval,
        Reach);

    private static EnemySpec Husk() => Static(HuskId, HuskMaxHp);

    /// <summary>A dummy nothing can kill in one blow, for the allocation sweep.</summary>
    private static EnemySpec Anvil() => Static(AnvilId, 1_000_000f);

    /// <summary>What one Censer swing kills, so the integration rows raise their Wight on tick one.</summary>
    private static EnemySpec Frail() => Static(FrailId, FrailMaxHp);

    /// <summary>What one Wight strike kills, and whose blast then kills the player.</summary>
    private static EnemySpec Bomb() => Static(
        BombId, FrailMaxHp, contactDamage: BombDamage, explosion: new ExplosionSpec(BombRadius));

    /// <summary>
    /// A body authored <c>Boss</c> — the one thing <see cref="EnemyDeath.WasBoss"/> reads.
    /// </summary>
    /// <remarks>
    /// Never ticked by any row here: <c>EnemySystem.Tick</c> refuses a <c>Boss</c> agent with no
    /// behaviour, and a behaviour needs a <c>BossSpec</c> that this fixture has no other use for.
    /// </remarks>
    private static EnemySpec WardenBody() =>
        Static(WardenBodyId, 4_200f, behaviour: EnemyBehaviourKind.Boss);

    private static EnemySpec Static(
        string id,
        float maxHp,
        float contactDamage = 8f,
        EnemyBehaviourKind behaviour = EnemyBehaviourKind.Static,
        ExplosionSpec explosion = null) => new EnemySpec(
            Id(id),
            new LocKey($"{id}.name"),
            maxHp,
            moveSpeed: 2f,
            targetPriority: 1,
            threatCost: 4,
            xpValue: 12f,
            isElite: false,
            contactDamage,
            reach: 1.2f,
            windupTime: 0.4f,
            recoverTime: 0.6f,
            aggroRange: 30f,
            behaviour,
            projectile: null,
            explosion);

    /// <summary>
    /// A mode with an empty roster, so a run composes nothing and has no stage to pace — the arena
    /// is exactly what the spawn plan dressed into it and stays that way.
    /// </summary>
    private static ModeSpec Descent() => new ModeSpec(
        Id(DescentId),
        new LocKey("mode.descent.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    private static CharacterSpec Oathbound() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>The one class in the game that raises the dead — see this fixture's remarks.</summary>
    private static CharacterSpec Gravecaller() => new CharacterSpec(
        Id(GravecallerId),
        new LocKey("character.gravecaller.name"),
        80f,
        new MovementSpec(3.1f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 9f, 4f, 12f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Shroudstep, 6f, 0.05f, 2.5f, 0.15f, 0f, 0f, 0.05f, decoyDuration: 3f),
        shield: null,
        hitIFrames: 0.5f,
        minions: Wight());
}
