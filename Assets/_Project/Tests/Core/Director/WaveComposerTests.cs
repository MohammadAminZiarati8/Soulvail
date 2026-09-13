using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Director;

/// <summary>
/// Every composition rule: the triangular split and its carry, the mode's vocabulary, the
/// introduction, the draws, the concurrency cap and what the cap makes the composer spend its
/// surplus on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Expectations are computed from GD §12's formulas, and one row is GD §12.1's own published
/// composition.</b> <see cref="Compose_StageOne_TenHusks"/> is the only row here that asserts a
/// number a design document states rather than one this fixture derives, and it is the row that
/// fails if a wave's remainder is dropped instead of carried.
/// </para>
/// <para>
/// <b>Draws are scripted, never seeded.</b> <see cref="WaveComposer.Compose"/> takes an
/// <see cref="IRandomStream"/>, and the only implementation <c>Soulvail.Tests.Core</c> can see is
/// <see cref="FixedRandom"/> — <c>SeededRandom</c> is a Unity adapter, and the seed→stream mapping
/// is its own fixture's to pin (<c>SeededRandomTests</c>, Tests.Game). So "the same seed" is
/// spelled here as "the same scripted stream", which is the claim this boundary can actually make:
/// the composition is a function of the stream's values and of nothing else. A scripted 0.1 always
/// picks the first affordable archetype and a 0.9 the last, which is what makes a row's intended
/// composition readable from its script.
/// </para>
/// <para>
/// <b>Two archetypes cannot share an introduction stage</b> (GD §8.2, enforced by
/// <see cref="ModeSpec"/>), so every mixed-roster fixture here introduces the Husk at 1 and the
/// second archetype at 2 — and composes at a stage that introduces nothing, unless the row is
/// about the introduction.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WaveComposerTests
{
    private const string HuskId = "enemy.husk";
    private const string SpitterId = "enemy.spitter";
    private const string BloaterId = "enemy.bloater";

    /// <summary>GD §8.1's threat costs for the three archetypes these rows use.</summary>
    private const int HuskCost = 4;
    private const int SpitterCost = 7;
    private const int BloaterCost = 8;

    /// <summary>GD §11.1's mid tier — the cap the game is designed at, and M2-04's choice.</summary>
    private const int MidTierCap = 28;

    /// <summary>
    /// The wave curve's ceiling (GD §12.2), which is what a run sizes its one plan to.
    /// </summary>
    private const int MaxWaves = 5;

    /// <summary>
    /// Loose enough to survive a budget computed in <see cref="float"/>, tight enough that no two
    /// of GD §12.1's stage budgets could satisfy each other's assertion.
    /// </summary>
    private const float Tolerance = 0.01f;

    // ---- The budget becomes waves (rules 2, 3) --------------------------------------------------

    [Test]
    public void Compose_WaveCountFromCurve()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(1, mode, plan, Stream());

        // W(n) = clamp(2 + n/5, 2, 5), integer division: W(1) = 2.
        Assert.That(plan.WaveCount, Is.EqualTo(2), "GD §12.2: W(1) = 2 + 1/5 = 2.");
        Assert.That(plan.Stage, Is.EqualTo(1));

        composer.Compose(15, mode, plan, Stream());

        Assert.That(plan.WaveCount, Is.EqualTo(5), "GD §12.2: W(15) = 2 + 3 = 5, the curve's cap.");
    }

    [Test]
    public void Compose_ConcurrencyFromCurveAndCap()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(10, mode, plan, Stream());

        // C(n) = min(10 + n/2, cap): below stage 36 the curve limits the screen, not the device.
        Assert.That(plan.Concurrency, Is.EqualTo(15), "GD §12.2: C(10) = 10 + 5 = 15, under the cap.");

        composer.Compose(60, mode, plan, Stream());

        Assert.That(plan.Concurrency, Is.EqualTo(MidTierCap),
            "C(60) = 40 is above the mid tier's 28, so the device cap is what binds (rule 2).");
    }

    [Test]
    public void Compose_BudgetRisesAcrossWaves()
    {
        ModeSpec mode = HuskOnly();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        composer.Compose(10, mode, plan, Stream());

        // B(10) = 220.9 over W = 4 waves, so the shares are 1/10, 2/10, 3/10, 4/10 of it — a stage
        // opens gently and closes hard (GD §4.2). The spends come out 20, 44, 60, 60: the last two
        // are equal because C(10) = 15 caps both, which is the cap doing its job and not a flat
        // curve.
        var spends = new List<int>();

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            spends.Add(Spend(plan, wave, catalog));
        }

        for (int i = 1; i < spends.Count; i++)
        {
            Assert.That(spends[i], Is.GreaterThanOrEqualTo(spends[i - 1]),
                $"Wave {i + 1} costs less than wave {i}, so the stage eases off rather than " +
                "building. The triangular split is what makes the last wave the hard one.");
        }

        Assert.That(spends[spends.Count - 1], Is.EqualTo(Max(spends)),
            "The last wave must be the most expensive one in the stage.");
        Assert.That(spends[0], Is.LessThan(spends[spends.Count - 1]),
            "Wave 1 must be strictly cheaper than the last, or the split is not rising at all.");
    }

    [Test]
    public void Compose_ConservesBudget()
    {
        ModeSpec mode = HuskOnly();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        composer.Compose(5, mode, plan, Stream());

        // Nothing evaporates at a wave boundary: 4 + 8 + 12 Husks is 96 threat, and the 6.4 the
        // concurrency cap refused in wave 3 is reported rather than dropped.
        int spent = Spend(plan, catalog);

        Assert.That(spent + plan.UnspentThreat, Is.EqualTo(102.4f).Within(Tolerance),
            "GD §12.1: B(5) = 40 + 48 + 14.4 = 102.4. Every point of it is either a body or " +
            "UnspentThreat — a rounding lost at a wave boundary is a stage quietly getting easier.");
    }

    [Test]
    public void Compose_StageOne_TenHusks()
    {
        ModeSpec mode = HuskOnly();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        composer.Compose(1, mode, plan, Stream());

        // GD §12.1's own "rough composition" for stage 1: a budget of 40 and a Husk at 4 is ten
        // Husks. This is the row that fails if a wave's unspent remainder is dropped instead of
        // carried (rule 3) — wave 1 can afford 3 Husks out of its share of 13.33 and leaves 1.33,
        // and without the carry wave 2 has 26.67 for six rather than 28 for seven. Nine Husks would
        // look perfectly plausible in a playtest.
        Assert.That(plan.BodyCount(1) + plan.BodyCount(2), Is.EqualTo(10),
            "GD §12.1: a stage-1 budget of 40 buys ten Husks at 4 threat each.");
        Assert.That(plan.BodyCount(1), Is.EqualTo(3), "Wave 1 is worth 1 share of 3: 13.33 → 3 Husks.");
        Assert.That(plan.BodyCount(2), Is.EqualTo(7), "Wave 2 is worth 2 shares plus the 1.33 carry.");

        Assert.That(plan.UnspentThreat, Is.EqualTo(0f).Within(Tolerance),
            "Ten Husks is exactly 40, so stage 1 spends its budget to the point.");
        Assert.That(Spend(plan, catalog), Is.EqualTo(40));
    }

    // ---- The vocabulary is the mode's (rules 4, 5) ----------------------------------------------

    [Test]
    public void Compose_OnlyEligibleArchetypes()
    {
        ModeSpec mode = Mode((HuskId, 1), (SpitterId, 2));
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(1, mode, plan, Stream());

        // GD §8.2: the Spitter arrives at stage 2 and the composer may not reach for it at 1, even
        // though the catalog holds it and its cost is affordable. What a mode is allowed to spawn is
        // the mode's statement, which is what makes a Boss Rush a data change (GD §4.5).
        Assert.That(CountOf(plan, SpitterId), Is.Zero,
            "The Spitter is introduced at stage 2; nothing may spawn one at stage 1.");
        Assert.That(CountOf(plan, HuskId), Is.GreaterThan(0), "The Husk is eligible and is all there is.");
    }

    [Test]
    public void Compose_IntroductionAppearsInWaveOne()
    {
        ModeSpec mode = Mode((HuskId, 1), (SpitterId, 2));
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        // Scripted to always pick the cheapest affordable archetype, so a Spitter in wave 1 can
        // only have come from the introduction rule and not from a lucky draw.
        composer.Compose(2, mode, plan, Stream(Repeated(0.1f, 40)));

        Assert.That(CountOf(plan, 1, SpitterId), Is.GreaterThanOrEqualTo(1),
            "GD §8.2: a new archetype arrives 'in a wave where they're the only new thing', so the " +
            "stage's introduction is bought before any draw (rule 5). Left to the draws, the " +
            "player can reach the stage that introduces the Spitter and never meet one.");
    }

    // ---- The draws (rules 6, 7, 8) --------------------------------------------------------------

    [Test]
    public void Compose_Deterministic()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);
        WavePlan first = Plan(mode);
        WavePlan second = Plan(mode);

        float[] script = Alternating(60);

        composer.Compose(10, mode, first, Stream(script));
        composer.Compose(10, mode, second, Stream(script));

        AssertSameComposition(first, second);
    }

    [Test]
    public void Compose_SeedChangesComposition()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);
        WavePlan cheapest = Plan(mode);
        WavePlan dearest = Plan(mode);

        // Two streams standing in for two seeds (see the fixture's remarks). One always takes the
        // first affordable archetype, the other the last — the two ends of what a seed can do.
        composer.Compose(10, mode, cheapest, Stream(Repeated(0.1f, 80)));
        composer.Compose(10, mode, dearest, Stream(Repeated(0.9f, 80)));

        Assert.That(Describe(cheapest), Is.Not.EqualTo(Describe(dearest)),
            "Two streams produced the same stage, so the draws are not reaching the composition " +
            "and every run of a mode is the same fight.");

        // And the thing that must *not* change with the stream: how much the stage is worth. The
        // split is arithmetic with no draw in it, so difficulty cannot be rerolled by killing the
        // app (rule 3).
        Assert.That(cheapest.WaveCount, Is.EqualTo(dearest.WaveCount));
        Assert.That(cheapest.Concurrency, Is.EqualTo(dearest.Concurrency));
    }

    [Test]
    public void Compose_UsesSpawnStreamOnly()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);

        // ADR-0011: every draw is the Spawn stream's. FixedRandom shares one scripted stream across
        // all five until one is set individually, so Spawn says "take the last affordable" while
        // Offers, Affixes, Drops and Misc all say "take the first".
        var random = new FixedRandom(Repeated(0.1f, 80));
        random.SetSpawn(Repeated(0.9f, 80));

        WavePlan actual = Plan(mode);
        composer.Compose(10, mode, actual, random.Spawn);

        WavePlan fromSpawnScript = Plan(mode);
        composer.Compose(10, mode, fromSpawnScript, Stream(Repeated(0.9f, 80)));

        WavePlan fromOtherStreams = Plan(mode);
        composer.Compose(10, mode, fromOtherStreams, Stream(Repeated(0.1f, 80)));

        AssertSameComposition(actual, fromSpawnScript);
        Assert.That(Describe(actual), Is.Not.EqualTo(Describe(fromOtherStreams)),
            "The composition followed a stream that is not Spawn. Streams exist so that adding a " +
            "drop roll cannot shift what a stage is made of (ADR-0011).");
    }

    [Test]
    public void Compose_NeverExceedsConcurrency()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        composer.Compose(60, mode, plan, Stream(Alternating(200)));

        bool capReached = false;

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            Assert.That(plan.BodyCount(wave), Is.LessThanOrEqualTo(MidTierCap),
                $"Wave {wave} of stage 60 holds more bodies than the device may draw. The cap is " +
                "a fact about the phone, so exceeding it is dropped frames rather than difficulty.");

            capReached |= plan.BodyCount(wave) == MidTierCap;
        }

        Assert.That(capReached, Is.True,
            "No wave reached the cap, so this row would pass with the cap deleted. B(60) = 3880.9 " +
            "is far more than 28 bodies can cost.");
    }

    [Test]
    public void Compose_CapBinding_SpendsOnQuality()
    {
        ModeSpec mode = Mixed();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        // Scripted to buy the cheapest archetype every time, so every body the fill loop adds is a
        // Husk at 4 — and anything dearer in the finished wave is the upgrade pass, not a draw.
        composer.Compose(60, mode, plan, Stream(Repeated(0.1f, 200)));

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            Assert.That(plan.BodyCount(wave), Is.EqualTo(MidTierCap), $"Wave {wave} is at the cap.");

            float meanCost = Spend(plan, wave, catalog) / (float)plan.BodyCount(wave);

            Assert.That(meanCost, Is.GreaterThan(HuskCost),
                $"Wave {wave}'s mean body costs {meanCost}, which is a Husk — so a stage-60 wave " +
                "is 28 Husks exactly like a stage-36 one, and GD §11.2's rule that a phone which " +
                "cannot draw more bodies gets better ones is not happening (rule 7).");
        }

        Assert.That(CountOf(plan, HuskId), Is.Zero,
            "With two archetypes and this much surplus, every Husk should have been upgraded into " +
            "a Bloater: the upgrade pass repeats while an upgrade is affordable.");
    }

    [Test]
    public void Compose_CapBinding_ReportsUnspent()
    {
        ModeSpec mode = Mixed();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        composer.Compose(60, mode, plan, Stream(Repeated(0.1f, 200)));

        // Five waves of 28 Bloaters is 1,120 threat against B(60) = 3,880.9. The 2,760.9 the cap
        // refused is not waste and is not discarded: M7-02's Elites are 2.5× cost for one body
        // (GD §8.3), which is what eventually spends it.
        int spent = Spend(plan, catalog);

        Assert.That(plan.UnspentThreat, Is.GreaterThan(0f),
            "A capped stage with nothing dearer to upgrade to must report its surplus.");
        Assert.That(spent + plan.UnspentThreat, Is.EqualTo(3880.9f).Within(Tolerance),
            "GD §12.1: B(60) = 40 + 708 + 3132.9 = 3880.9, all of it either a body or surplus.");
    }

    [Test]
    public void Compose_NoUpgradeAvailable_LeavesUnspent()
    {
        ModeSpec mode = HuskOnly();
        ContentCatalog catalog = Catalog();
        var composer = new WaveComposer(catalog, Budget(mode));
        WavePlan plan = Plan(mode);

        composer.Compose(60, mode, plan, Stream());

        // One archetype, so quality is not for sale at any price: every wave fills to the cap with
        // Husks and the rest is surplus. 5 × 28 × 4 = 560 of 3,880.9.
        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            Assert.That(plan.BodyCount(wave), Is.EqualTo(MidTierCap));
        }

        Assert.That(Spend(plan, catalog), Is.EqualTo(560));
        Assert.That(plan.UnspentThreat, Is.EqualTo(3880.9f - 560f).Within(Tolerance),
            "With nothing to upgrade to, the whole remainder is reported rather than spent on " +
            "bodies the device cannot draw.");
    }

    [Test]
    public void Compose_TinyBudget_StillBuysOne()
    {
        // A mode whose entire stage budget is 1, against a Husk at 4. Authored by nobody, but the
        // alternative to this rule is a stage with nothing in it — an arena the player stands in
        // waiting for a wave that never arrives, with no error anywhere (rule 8).
        ScalingSpec starved = Starved();

        ModeSpec introduces = Mode(starved, (HuskId, 1));
        WaveComposer composer = Composer(introduces);
        WavePlan plan = Plan(introduces);

        composer.Compose(1, introduces, plan, Stream());

        Assert.That(TotalBodies(plan), Is.EqualTo(1),
            "A stage whose budget cannot afford its cheapest archetype still opens with one body.");
        Assert.That(plan.UnspentThreat, Is.EqualTo(0f),
            "The one body a stage may overspend on is reported as no surplus, never as a debt.");

        // The other half of rule 8, and the reason it is "once per stage" rather than "once per
        // wave": at stage 2 this mode introduces nothing, so the forced buy is not the
        // introduction — and the waves after the first must still come up empty rather than each
        // handing out another free Husk.
        ModeSpec introducesNothing = Mode(starved, (HuskId, 1));
        WavePlan second = Plan(introducesNothing);

        Composer(introducesNothing).Compose(2, introducesNothing, second, Stream());

        Assert.That(TotalBodies(second), Is.EqualTo(1),
            "One body for the stage, not one per wave — otherwise a mis-authored budget becomes a " +
            "trickle of free enemies instead of a single declared overspend.");
        Assert.That(second.WaveCount, Is.EqualTo(2), "The wave count still comes from the curve.");
    }

    // ---- Allocation (rule 9) -------------------------------------------------------------------

    [Test]
    public void Compose_AllocatesNothing()
    {
        ModeSpec mode = Mixed();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);
        IRandomStream spawn = Stream();

        // A stage boundary is a moment the player is standing still, so a GC spike there is as
        // visible as one mid-fight. AllocationAssert's warm-up call is what absorbs the composer's
        // one-time buffer growth, which is the only allocation Compose is allowed (rule 9).
        AllocationAssert.None(() => composer.Compose(10, mode, plan, spawn), 1_000);
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Constructor_RefusesNulls()
    {
        ModeSpec mode = HuskOnly();

        Assert.Throws<ArgumentNullException>(() => new WaveComposer(null, Budget(mode)));
        Assert.Throws<ArgumentNullException>(() => new WaveComposer(Catalog(), null));
    }

    [Test]
    public void Compose_RefusesNulls()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        Assert.Throws<ArgumentNullException>(() => composer.Compose(1, null, plan, Stream()));
        Assert.Throws<ArgumentNullException>(() => composer.Compose(1, mode, null, Stream()));
        Assert.Throws<ArgumentNullException>(() => composer.Compose(1, mode, plan, null));
    }

    [Test]
    public void Compose_RefusesStageBelowOne()
    {
        ModeSpec mode = HuskOnly();
        WaveComposer composer = Composer(mode);
        WavePlan plan = Plan(mode);

        Assert.Throws<ArgumentOutOfRangeException>(() => composer.Compose(0, mode, plan, Stream()));
        Assert.Throws<ArgumentOutOfRangeException>(() => composer.Compose(-1, mode, plan, Stream()));
    }

    [Test]
    public void Compose_EmptyRoster_Throws()
    {
        // An empty roster is legal content — a mode whose arena population comes entirely from its
        // spawn plan (M2-02) — so this is refused at the composer rather than at the mode. Such a
        // mode is simply never composed.
        var mode = new ModeSpec(
            new ContentId("mode.empty"),
            new LocKey("mode.empty.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            Scalings.Design(),
            Array.Empty<RosterEntry>());

        var composer = new WaveComposer(Catalog(), Budget(mode));

        Assert.Throws<ArgumentException>(
            () => composer.Compose(1, mode, new WavePlan(MaxWaves, 1), Stream()));
    }

    [Test]
    public void Compose_NothingEligibleYet_Throws()
    {
        // Every archetype arrives later than the stage being composed, so there is nothing to buy
        // and rule 8 has no cheapest archetype to reach for. Loud, because the alternative is the
        // empty arena rule 8 exists to prevent.
        ModeSpec mode = Mode((HuskId, 5));
        WaveComposer composer = Composer(mode);

        Assert.Throws<ArgumentException>(() => composer.Compose(1, mode, Plan(mode), Stream()));
        Assert.DoesNotThrow(() => composer.Compose(5, mode, Plan(mode), Stream()));
    }

    [Test]
    public void Compose_UnauthoredArchetype_ThrowsNamingId()
    {
        // Unreachable through a run — RunSession.Start resolves every roster entry before
        // RunStarted (M2-02, ledger row 3) — and left as the catalog's own exception on purpose, so
        // the message names the id rather than the composer.
        ModeSpec mode = Mode((HuskId, 1), ("enemy.ghost", 2));
        var composer = new WaveComposer(Catalog(), Budget(mode));

        var thrown = Assert.Throws<KeyNotFoundException>(
            () => composer.Compose(2, mode, Plan(mode), Stream()));

        Assert.That(thrown.Message, Does.Contain("enemy.ghost"));
    }

    // ---- Fixtures ------------------------------------------------------------------------------

    /// <summary>GD §8.1's three cheapest archetypes, costs included, plus nothing else.</summary>
    /// <remarks>
    /// Every number that matters to a composition is the threat cost; the rest are the Husk's, so
    /// a row that fails names the cost rather than a plausible-looking stat.
    /// </remarks>
    private static ContentCatalog Catalog() => new ContentCatalog(
        Array.Empty<CharacterSpec>(),
        new[]
        {
            Archetype(HuskId, HuskCost),
            Archetype(SpitterId, SpitterCost),
            Archetype(BloaterId, BloaterCost),
        });

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

    /// <summary>Descent's roster as GD §8.2 opens it: the Husk alone.</summary>
    private static ModeSpec HuskOnly() => Mode((HuskId, 1));

    /// <summary>
    /// Two archetypes at four and eight threat, so the upgrade pass has somewhere to go.
    /// </summary>
    /// <remarks>
    /// Introduced at 1 and 2 rather than both at 1, because <see cref="ModeSpec"/> refuses two
    /// archetypes sharing an introduction stage (GD §8.2) — which is why every mixed row here
    /// composes at stage 3 or deeper.
    /// </remarks>
    private static ModeSpec Mixed() => Mode((HuskId, 1), (BloaterId, 2));

    private static ModeSpec Mode(params (string Id, int Stage)[] roster) =>
        Mode(Scalings.Design(), roster);

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
    /// GD §12's curves with the budget replaced by a flat 1 — less than the cheapest archetype
    /// costs, which is what rule 8 is about.
    /// </summary>
    private static ScalingSpec Starved() => new ScalingSpec(
        new BudgetCurve(1f, 0f, 0f),
        new WaveCurve(2, 5, 2, 5),
        new ConcurrencyCurve(10, 2),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    private static ThreatBudget Budget(ModeSpec mode) => new ThreatBudget(mode.Scaling, MidTierCap);

    private static WaveComposer Composer(ModeSpec mode) => new WaveComposer(Catalog(), Budget(mode));

    /// <summary>A plan sized as a run sizes it: the wave curve's ceiling by the roster's length.</summary>
    private static WavePlan Plan(ModeSpec mode) => new WavePlan(MaxWaves, Math.Max(1, mode.Roster.Count));

    /// <summary>
    /// A scripted <c>Spawn</c> stream. Past the scripted values every draw is 0.5, which is a
    /// defined middling pick rather than an exception (see <see cref="FixedRandom"/>).
    /// </summary>
    private static IRandomStream Stream(params float[] values) => new FixedRandom(values).Spawn;

    /// <summary>
    /// <paramref name="count"/> copies of <paramref name="value"/>. 0.1 always takes the first
    /// affordable archetype, 0.9 the last.
    /// </summary>
    private static float[] Repeated(float value, int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = value;
        }

        return values;
    }

    /// <summary>A script that alternates cheapest and dearest, for the rows about a mixed wave.</summary>
    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>Σ every body's threat cost across the whole plan.</summary>
    private static int Spend(WavePlan plan, ContentCatalog catalog)
    {
        int spent = 0;

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            spent += Spend(plan, wave, catalog);
        }

        return spent;
    }

    /// <summary>Σ every body's threat cost in one wave.</summary>
    private static int Spend(WavePlan plan, int wave, ContentCatalog catalog)
    {
        int spent = 0;

        for (int i = 0; i < plan.EntryCount(wave); i++)
        {
            WaveEntry entry = plan.Entry(wave, i);
            spent += catalog.Enemy(entry.SpecId).ThreatCost * entry.Count;
        }

        return spent;
    }

    private static int TotalBodies(WavePlan plan)
    {
        int bodies = 0;

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            bodies += plan.BodyCount(wave);
        }

        return bodies;
    }

    /// <summary>How many of one archetype the whole plan holds.</summary>
    private static int CountOf(WavePlan plan, string specId)
    {
        int count = 0;

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            count += CountOf(plan, wave, specId);
        }

        return count;
    }

    /// <summary>How many of one archetype a single wave holds.</summary>
    private static int CountOf(WavePlan plan, int wave, string specId)
    {
        int count = 0;

        for (int i = 0; i < plan.EntryCount(wave); i++)
        {
            WaveEntry entry = plan.Entry(wave, i);

            if (entry.SpecId.Value == specId)
            {
                count += entry.Count;
            }
        }

        return count;
    }

    /// <summary>
    /// The whole composition as text, so "these two stages differ" is one assertion with a readable
    /// failure message rather than a nested loop that reports the first mismatched index.
    /// </summary>
    private static string Describe(WavePlan plan)
    {
        var text = new System.Text.StringBuilder();

        text.Append("stage ").Append(plan.Stage)
            .Append(" C").Append(plan.Concurrency)
            .Append(" unspent ").Append(plan.UnspentThreat.ToString("F2"));

        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            text.Append(" | w").Append(wave).Append(':');

            for (int i = 0; i < plan.EntryCount(wave); i++)
            {
                WaveEntry entry = plan.Entry(wave, i);
                text.Append(' ').Append(entry.SpecId.Value).Append('×').Append(entry.Count);
            }
        }

        return text.ToString();
    }

    private static void AssertSameComposition(WavePlan expected, WavePlan actual)
    {
        Assert.That(Describe(actual), Is.EqualTo(Describe(expected)),
            "Two compositions from the same stream differ, so a resumed or replayed run does not " +
            "get the stage its seed promised.");
    }

    private static int Max(List<int> values)
    {
        int max = values[0];

        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }
}
