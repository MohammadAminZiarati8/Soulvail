using System;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Director;

/// <summary>
/// The container's own rules: its capacity, what it refuses to answer, and that refilling it for a
/// new stage leaves no trace of the last one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every composition here arrives through a real <see cref="WaveComposer"/>.</b>
/// <see cref="WavePlan"/>'s write side is <see langword="internal"/> — the same bargain
/// <c>RunState</c> makes (AR §18.2) — and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c>, so a wave of exactly two entries is <em>arranged</em> by scripting the
/// draws rather than by poking the plan. That is the harder route and the right one: it is also the
/// only route M2-05 will have.
/// </para>
/// <para>
/// The scripts read as compositions: 0.9 takes the last affordable archetype and 0.1 the first, so
/// <c>0.9, 0.9, 0.1, …</c> against a flat budget of 108 is "two Bloaters, then Husks until the money
/// runs out".
/// </para>
/// </remarks>
[TestFixture]
public sealed class WavePlanTests
{
    private const string HuskId = "enemy.husk";
    private const string BloaterId = "enemy.bloater";

    private const int HuskCost = 4;
    private const int BloaterCost = 8;

    private const int MidTierCap = 28;

    /// <summary>The wave curve's ceiling (GD §12.2) — what a run sizes its one plan to.</summary>
    private const int MaxWaves = 5;

    private const float Tolerance = 0.01f;

    // ---- Capacity ------------------------------------------------------------------------------

    [Test]
    public void Constructor_RefusesNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavePlan(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavePlan(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavePlan(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavePlan(5, -1));
    }

    [Test]
    public void Plan_BeforeComposition_HoldsNothingAndRefusesEveryWave()
    {
        var plan = new WavePlan(MaxWaves, 2);

        Assert.That(plan.Stage, Is.Zero);
        Assert.That(plan.WaveCount, Is.Zero);
        Assert.That(plan.Concurrency, Is.Zero);
        Assert.That(plan.UnspentThreat, Is.Zero);

        // Wave 1 of an uncomposed plan is the interesting one: the storage for it exists and is
        // full of default(WaveEntry), which names no archetype. Answering instead of throwing would
        // surface one layer down as the catalog's "no enemy with id ''" (rule 10).
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => plan.EntryCount(1));

        Assert.That(thrown.Message, Does.Contain("Compose"),
            "The message must say the plan has not been composed, or it reads as a bad index.");

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Entry(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.BodyCount(1));
    }

    [Test]
    public void Plan_TooFewWavesForStage_Throws()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);

        // W(15) = 5 and this plan holds 3. Refused rather than truncated: a stage quietly delivered
        // in three waves instead of five is a pacing change nothing reports.
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => composer.Compose(15, mode, new WavePlan(3, 1), Stream()));

        Assert.That(thrown.Message, Does.Contain("5"));
    }

    [Test]
    public void Plan_TooFewEntriesForRoster_Throws()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);

        // Sized for one archetype per wave and handed a two-archetype mode. Refused at the start of
        // the composition rather than on the entry that overflows, so the message can name the
        // capacity rather than whichever wave happened to draw both.
        Assert.Throws<ArgumentException>(
            () => composer.Compose(3, mode, new WavePlan(MaxWaves, 1), Stream()));
    }

    // ---- What it answers, and what it refuses to (rules 10, 11) ---------------------------------

    [Test]
    public void Plan_WaveOutOfRange_Throws()
    {
        ModeSpec mode = HuskOnly();
        WavePlan plan = Plan(mode);

        Composer(mode).Compose(1, mode, plan, Stream());

        Assert.That(plan.WaveCount, Is.EqualTo(2), "W(1) = 2 — the arrangement this row needs.");

        // Above the stage's wave count, and the plan's own storage holds five waves — so wave 3
        // exists as memory and must not exist as an answer.
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.EntryCount(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.BodyCount(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Entry(3, 0));

        // Waves are numbered from 1, so 0 is not "the first" — a reader that counted from zero
        // would otherwise read wave 1 as wave 0 and silently skip the last one.
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.EntryCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.BodyCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Entry(0, 0));
    }

    [Test]
    public void Plan_EntryOutOfRange_Throws()
    {
        WavePlan plan = TwoBloatersAndFiveHusks();

        Assert.That(plan.EntryCount(1), Is.EqualTo(2), "The arrangement: Husks and Bloaters, aggregated.");

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Entry(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Entry(1, -1));
        Assert.DoesNotThrow(() => plan.Entry(1, 0));
        Assert.DoesNotThrow(() => plan.Entry(1, 1));
    }

    [Test]
    public void Plan_BodyCountSumsEntries()
    {
        WavePlan plan = TwoBloatersAndFiveHusks();

        Assert.That(plan.BodyCount(1), Is.EqualTo(7), "5 Husks + 2 Bloaters is seven bodies.");

        int summed = 0;

        for (int i = 0; i < plan.EntryCount(1); i++)
        {
            summed += plan.Entry(1, i).Count;
        }

        Assert.That(plan.BodyCount(1), Is.EqualTo(summed),
            "BodyCount is stored rather than summed on demand, so this is the row that keeps the " +
            "two from drifting — the director asks for it every time it paces a wave.");
    }

    [Test]
    public void Plan_AggregatesEntriesPerArchetype()
    {
        WavePlan plan = TwoBloatersAndFiveHusks();

        // Rule 11: five Husks are one entry with a count of 5, not five entries. The plan says
        // what, not when — spacing them out is the director's (M2-05).
        Assert.That(plan.EntryCount(1), Is.EqualTo(2),
            "Two archetypes, so two entries however many bodies each contributes.");

        Assert.That(plan.Entry(1, 0).SpecId.Value, Is.EqualTo(HuskId),
            "Entries are in the mode's roster order, which is the one order that is not a draw.");
        Assert.That(plan.Entry(1, 0).Count, Is.EqualTo(5));
        Assert.That(plan.Entry(1, 1).SpecId.Value, Is.EqualTo(BloaterId));
        Assert.That(plan.Entry(1, 1).Count, Is.EqualTo(2));
    }

    [Test]
    public void Plan_NeverHandsOutAnEmptyEntry()
    {
        // An archetype the draws never picked is absent rather than present with a count of 0 — and
        // so is one the upgrade pass emptied, which is the live case: at stage 60 every Husk becomes
        // a Bloater, and a zero-count Husk entry would have the director spawning nothing.
        ModeSpec mode = Mixed();
        WavePlan plan = Plan(mode);

        Composer(mode).Compose(60, mode, plan, Stream(Repeated(0.1f, 200)));

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            for (int i = 0; i < plan.EntryCount(wave); i++)
            {
                Assert.That(plan.Entry(wave, i).Count, Is.GreaterThan(0),
                    $"Wave {wave} entry {i} holds no bodies.");
            }
        }
    }

    // ---- Reuse (rule 9) ------------------------------------------------------------------------

    [Test]
    public void Compose_ReusesPlan()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(1, mode, plan, Stream());

        Assert.That(plan.Stage, Is.EqualTo(1));
        Assert.That(plan.WaveCount, Is.EqualTo(2));
        Assert.That(plan.BodyCount(1), Is.EqualTo(3));
        Assert.That(plan.UnspentThreat, Is.EqualTo(0f).Within(Tolerance));

        composer.Compose(9, mode, plan, Stream());

        // Every field a reader can see is restated. B(9) = 193.6 over W(9) = 3 waves with C(9) = 14:
        // 8 Husks, then two waves at the cap, and 49.6 the cap refused. Nothing of stage 1 is
        // reachable — a plan that kept its old unspent threat or its old wave 1 would be a pooled
        // object remembering its last life, which is the failure AR §18.4 names for EnemyView.
        Assert.That(plan.Stage, Is.EqualTo(9));
        Assert.That(plan.WaveCount, Is.EqualTo(3), "W(9) = 2 + 1 = 3, one more wave than stage 1.");
        Assert.That(plan.Concurrency, Is.EqualTo(14), "C(9) = 10 + 4 = 14.");
        Assert.That(plan.BodyCount(1), Is.EqualTo(8), "Stage 9's wave 1, not stage 1's three Husks.");
        Assert.That(plan.BodyCount(3), Is.EqualTo(14), "A third wave, which stage 1 did not have.");
        Assert.That(plan.UnspentThreat, Is.EqualTo(49.6f).Within(Tolerance),
            "B(9) = 193.6 less the 144 threat 36 Husks cost.");
    }

    [Test]
    public void Compose_ShrinkingStage_ForgetsTheExtraWave()
    {
        // The direction the previous row cannot catch: deep stage first, shallow one second, so
        // every leftover is a wave that *exists* in the storage and must not be answerable. A plan
        // that only cleared the waves it was about to write would pass the row above and fail here.
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(15, mode, plan, Stream());

        Assert.That(plan.WaveCount, Is.EqualTo(5));

        composer.Compose(1, mode, plan, Stream());

        Assert.That(plan.WaveCount, Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.BodyCount(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.EntryCount(5));
    }

    // ---- WaveEntry's own guards ----------------------------------------------------------------

    [Test]
    public void Entry_RefusesNoArchetypeAndNoBodies()
    {
        Assert.Throws<ArgumentException>(() => new WaveEntry(default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveEntry(new ContentId(HuskId), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveEntry(new ContentId(HuskId), -1));

        var entry = new WaveEntry(new ContentId(HuskId), 9);

        Assert.That(entry.SpecId.Value, Is.EqualTo(HuskId));
        Assert.That(entry.Count, Is.EqualTo(9));
    }

    // ---- Fixtures ------------------------------------------------------------------------------

    /// <summary>
    /// A wave of exactly two entries — five Husks and two Bloaters — arranged through the composer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flat budget of 111 makes the arithmetic readable: W(3) = 2, so wave 1 is worth one share of
    /// three, which is 37. The script buys two Bloaters at 8 and then Husks at 4 until the money
    /// runs out — 36 of the 37 — and C(3) = 11 is above the seven bodies that buys, so nothing here
    /// is the cap's doing and the entries are exactly what the draws asked for.
    /// </para>
    /// <para>
    /// 37 rather than a round 36, and the odd number is the point: it leaves a whole point of slack
    /// above the last Husk, so the arrangement cannot be changed by the last bit of a float share.
    /// At 36 the seventh Husk costs exactly the 4 that are left, and this fixture would be pinned to
    /// how <c>B · 1/3</c> happens to round.
    /// </para>
    /// </remarks>
    private static WavePlan TwoBloatersAndFiveHusks()
    {
        ModeSpec mode = Mixed(Flat(111f));
        WavePlan plan = Plan(mode);

        Composer(mode).Compose(3, mode, plan, Stream(0.9f, 0.9f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f));

        return plan;
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        Array.Empty<CharacterSpec>(),
        new[] { Archetype(HuskId, HuskCost), Archetype(BloaterId, BloaterCost) });

    private static EnemySpec Archetype(string id, int threatCost) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: threatCost,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Chaser);

    private static ModeSpec HuskOnly() => Mode(Scalings.Design(), (HuskId, 1));

    private static ModeSpec Mixed() => Mixed(Scalings.Design());

    private static ModeSpec Mixed(ScalingSpec scaling) => Mode(scaling, (HuskId, 1), (BloaterId, 2));

    private static ModeSpec Mode(ScalingSpec scaling, params (string Id, int Stage)[] roster)
    {
        var entries = new RosterEntry[roster.Length];

        for (int i = 0; i < roster.Length; i++)
        {
            entries[i] = new RosterEntry(new ContentId(roster[i].Id), roster[i].Stage);
        }

        return new ModeSpec(
            new ContentId("mode.test"),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            entries);
    }

    /// <summary>
    /// GD §12's curves with a flat budget, so a wave's share is a number that can be read off the
    /// page rather than derived from B(n).
    /// </summary>
    private static ScalingSpec Flat(float budget) => new ScalingSpec(
        new BudgetCurve(budget, 0f, 0f),
        new WaveCurve(2, 5, 2, 5),
        new ConcurrencyCurve(10, 2),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    private static WaveComposer Composer(ModeSpec mode) =>
        new WaveComposer(Catalog(), new ThreatBudget(mode.Scaling, MidTierCap));

    private static WavePlan Plan(ModeSpec mode) => new WavePlan(MaxWaves, mode.Roster.Count);

    private static IRandomStream Stream(params float[] values) => new FixedRandom(values).Spawn;

    private static float[] Repeated(float value, int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = value;
        }

        return values;
    }
}
