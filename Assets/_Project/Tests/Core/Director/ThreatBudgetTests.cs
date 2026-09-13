using System;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;

namespace Soulvail.Tests.Core.Director;

/// <summary>
/// GD §12's five curves against GD §12's own numbers, plus the guards that keep a mis-authored
/// curve from making deep enemies weaker than shallow ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every expectation here is computed from GD §12's formulas, and one of them contradicts
/// GD §12.1's table on purpose.</b> B(40) is 1,876.9; the table says 1,772 and the prose beneath
/// it says "44×" — both follow from the same wrong number. Stages 5, 10 and 20 agree to a
/// rounding, which is what makes the table row the error rather than the formula.
/// See <see cref="Budget_Stage40_FollowsFormulaNotTable"/>.
/// </para>
/// <para>
/// Asserted through <see cref="ThreatBudget"/> wherever it has a method for the question, because
/// that is the surface M2-04 and M2-05 depend on and a curve reached any other way is one a
/// forwarding mistake could hide. The curve constructors are exercised directly for the guard
/// rows, which is the only place a caller ever names them.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ThreatBudgetTests
{
    /// <summary>GD §11.1's mid tier — the cap the game is designed at.</summary>
    private const int MidTierCap = 28;

    /// <summary>GD §11.1's low tier.</summary>
    private const int LowTierCap = 18;

    /// <summary>
    /// The spec's tolerance for the budget rows. Generous on purpose: GD §12.1's table is rounded
    /// to whole points, so the assertion is "the formula produces the published number" rather
    /// than a pin on float arithmetic.
    /// </summary>
    private const float BudgetTolerance = 0.05f;

    /// <summary>Tight, because the stat multipliers are published to three decimals.</summary>
    private const float MultiplierTolerance = 1e-4f;

    // ---- GD §12.1: the threat budget ------------------------------------------------------------

    [Test]
    public void Budget_MatchesDesignTable()
    {
        ThreatBudget budget = Design();

        // B(n) = 40 + 12·(n−1) + 0.9·(n−1)². The four rows GD §12.1's table gets right.
        Assert.That(budget.Budget(1), Is.EqualTo(40f).Within(BudgetTolerance));
        Assert.That(budget.Budget(5), Is.EqualTo(102.4f).Within(BudgetTolerance));
        Assert.That(budget.Budget(10), Is.EqualTo(220.9f).Within(BudgetTolerance));
        Assert.That(budget.Budget(20), Is.EqualTo(592.9f).Within(BudgetTolerance));
    }

    [Test]
    public void Budget_Stage40_FollowsFormulaNotTable()
    {
        // 40 + 12·39 + 0.9·39² = 40 + 468 + 1368.9 = 1876.9.
        //
        // GD §12.1's table says 1,772 at stage 40, and the "44×" in the prose beneath it is the
        // same error carried forward — the real ratio against stage 1 is ~46.9×. The three rows
        // above agree with the formula to a rounding, so it is the row that is stale, not the
        // arithmetic. This task deliberately does not edit GameDesign.md: the correction is one
        // line and it is the owner's (M2-03 rule 1, flagged in the spec's As built).
        Assert.That(Design().Budget(40), Is.EqualTo(1876.9f).Within(BudgetTolerance));
    }

    [Test]
    public void Budget_QuadraticOutgrowsHp()
    {
        // Beyond the spec's table, and it is the row that says why the numbers above are the
        // numbers: GD §12.3's "compare the shapes" claim is the design thesis — difficulty comes
        // from more things doing more different things at once, not from enemies absorbing more
        // hits. If a future tuning pass ever made HP scale faster than the budget, every other
        // assertion here would still pass and the game would have quietly become a bullet sponge.
        ThreatBudget budget = Design();

        float budgetRatio = budget.Budget(40) / budget.Budget(1);
        float hpRatio = budget.HpMultiplier(40);

        Assert.That(budgetRatio, Is.GreaterThan(10f * hpRatio),
            "GD §12.3: at stage 40 the budget is tens of times stage 1's while enemy HP is barely "
                + "three. That gap is the whole design thesis.");
    }

    [Test]
    public void Budget_StageZero_Throws()
    {
        ThreatBudget budget = Design();

        // Stages are numbered from 1 (GD §8.2), so stage 0 is a depth the game never reaches.
        // Answering it would mean B(0) = 40 − 12 + 0.9 = 28.9, a plausible-looking budget for a
        // stage that does not exist.
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Budget(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Budget(-1));

        // Every curve, not just the budget — one rule about what a legal stage is.
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Waves(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Concurrency(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.HpMultiplier(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.DamageMultiplier(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.SpeedMultiplier(0));
    }

    // ---- GD §12.2: pacing -----------------------------------------------------------------------

    [Test]
    public void Waves_ClampsBothEnds()
    {
        ThreatBudget budget = Design();

        // W(n) = clamp(2 + floor(n/5), 2, 5). Integer division, so stages 1–4 all give 2.
        Assert.That(budget.Waves(1), Is.EqualTo(2));
        Assert.That(budget.Waves(4), Is.EqualTo(2), "Integer division: 4/5 is 0.");
        Assert.That(budget.Waves(5), Is.EqualTo(3));
        Assert.That(budget.Waves(15), Is.EqualTo(5));

        // Past the ceiling the budget keeps growing and the wave count does not. Surplus becomes
        // quality rather than a stage that outlasts the player's patience (GD §11.2).
        Assert.That(budget.Waves(40), Is.EqualTo(5));
        Assert.That(budget.Waves(999), Is.EqualTo(5));
    }

    [Test]
    public void Concurrency_RisesThenCaps()
    {
        ThreatBudget budget = Design(MidTierCap);

        // C(n) = min(10 + floor(n/2), deviceCap).
        Assert.That(budget.Concurrency(1), Is.EqualTo(10));
        Assert.That(budget.Concurrency(20), Is.EqualTo(20));
        Assert.That(budget.Concurrency(36), Is.EqualTo(MidTierCap), "10 + 18 lands exactly on the cap.");
        Assert.That(budget.Concurrency(80), Is.EqualTo(MidTierCap), "10 + 40 would be 50.");

        Assert.That(budget.DeviceCap, Is.EqualTo(MidTierCap));
    }

    [Test]
    public void Concurrency_HonoursLowTierCap()
    {
        // GD §11.2's device-independence rule seen from the low end: the same mode, the same
        // stage, a smaller crowd — and *not* an easier stage, because the budget the cap refuses
        // to spend on bodies is spent on quality instead (M2-04). Nothing here may read a bigger
        // cap as a harder game.
        ThreatBudget low = Design(LowTierCap);

        Assert.That(low.Concurrency(40), Is.EqualTo(LowTierCap));

        // Same depth, same authored curves, same budget — only the crowd differs.
        Assert.That(low.Budget(40), Is.EqualTo(Design(MidTierCap).Budget(40)).Within(BudgetTolerance));
        Assert.That(low.Waves(40), Is.EqualTo(Design(MidTierCap).Waves(40)));
    }

    // ---- GD §12.3: stat scaling -----------------------------------------------------------------

    [Test]
    public void Hp_MatchesDesign()
    {
        ThreatBudget budget = Design();

        // h(n) = min(1 + 0.06·(n−1), 4.0), soft-capping at stage 51.
        Assert.That(budget.HpMultiplier(1), Is.EqualTo(1f).Within(MultiplierTolerance));
        Assert.That(budget.HpMultiplier(40), Is.EqualTo(3.34f).Within(MultiplierTolerance));
        Assert.That(budget.HpMultiplier(51), Is.EqualTo(4f).Within(MultiplierTolerance),
            "GD §12.3: 1 + 0.06·50 reaches the cap exactly at stage 51.");
        Assert.That(budget.HpMultiplier(99), Is.EqualTo(4f).Within(MultiplierTolerance));
    }

    [Test]
    public void Damage_CapsAtThree()
    {
        ThreatBudget budget = Design();

        // d(n) = min(1 + 0.035·(n−1), 3.0) — a hard cap, and the number GD §12.4's one-shot rule
        // (no non-boss attack past 35 % of max HP at any depth) is checked against at M2-15.
        Assert.That(budget.DamageMultiplier(1), Is.EqualTo(1f).Within(MultiplierTolerance));
        Assert.That(budget.DamageMultiplier(20), Is.EqualTo(1.665f).Within(MultiplierTolerance));
        Assert.That(budget.DamageMultiplier(99), Is.EqualTo(3f).Within(MultiplierTolerance));
    }

    [Test]
    public void Speed_StepsEveryFifthStage()
    {
        ThreatBudget budget = Design();

        // s(n) = min(1 + 0.02·floor(n/5), 1.3). The one curve with offset 0, which is why s(5) is
        // already 1.02 while h(1) and d(1) are exactly 1 — a staircase, not a ramp.
        Assert.That(budget.SpeedMultiplier(4), Is.EqualTo(1f).Within(MultiplierTolerance));
        Assert.That(budget.SpeedMultiplier(5), Is.EqualTo(1.02f).Within(MultiplierTolerance));
        Assert.That(budget.SpeedMultiplier(9), Is.EqualTo(1.02f).Within(MultiplierTolerance),
            "Stages 5 through 9 share a step; the tread is meant to be felt.");
        Assert.That(budget.SpeedMultiplier(10), Is.EqualTo(1.04f).Within(MultiplierTolerance));
        Assert.That(budget.SpeedMultiplier(99), Is.EqualTo(1.3f).Within(MultiplierTolerance));
    }

    // ---- The guards ------------------------------------------------------------------------------

    [Test]
    public void Curve_NegativeRate_Throws()
    {
        // A negative rate is the mis-authoring with no symptom: deep stages would simply be
        // cheaper and deep enemies weaker, and every other number in the game would look right.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(-0.1f, 4f, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(40f, -12f, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(40f, 12f, -0.9f));
    }

    [Test]
    public void Curve_CapBelowOne_Throws()
    {
        // 0.5 is finite, positive and halves every deep enemy's hit points — which is why the
        // floor is 1 rather than 0.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, 0.5f, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, 0f, 1, 1));
    }

    [Test]
    public void Curve_NonFinite_Throws()
    {
        // An implied guard row: every float door in this project refuses NaN and infinity. Here
        // the damage would be permanent — one NaN multiplier makes an enemy's Stat.Value NaN,
        // which silences its Changed event rather than reporting anything (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(float.NaN, 12f, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(float.PositiveInfinity, 12f, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(40f, float.NaN, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(40f, 12f, float.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(float.NaN, 4f, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, float.NaN, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, float.PositiveInfinity, 1, 1));
    }

    [Test]
    public void Curve_NonPositiveStep_Throws()
    {
        // A zero step is a division by zero on the first stage composed — a DivideByZeroException
        // from inside a curve, one call stack away from the asset that was wrong.
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveCurve(2, 0, 2, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveCurve(2, -5, 2, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrencyCurve(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, 4f, 0, 1));
    }

    [Test]
    public void Curve_NonPositiveBase_Throws()
    {
        // A stage that can afford nothing is an empty arena, and an arena that may hold nobody is
        // a stage nothing can be composed for. Both read as a director bug.
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(0f, 12f, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetCurve(-40f, 12f, 0.9f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveCurve(0, 5, 2, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrencyCurve(0, 2));
    }

    [Test]
    public void WaveCurve_MaxBelowMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveCurve(2, 5, 4, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveCurve(2, 5, 0, 5));

        // Equal is legal: a mode whose stages always have exactly three waves is a mode, not a
        // mistake.
        Assert.That(() => new WaveCurve(3, 5, 3, 3), Throws.Nothing);
        Assert.That(new WaveCurve(3, 5, 3, 3).At(99), Is.EqualTo(3));
    }

    [Test]
    public void StatCurve_OffsetOutsidePair_Throws()
    {
        // The offset exists to spell the difference between GD §12.3's h and d, which step from
        // n−1, and s, which steps on n. It is not a free shift of the whole curve — a 3 here would
        // silently give stages 1–3 no scaling at all on a step-1 curve.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, 4f, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatCurve(0.06f, 4f, 1, -1));

        Assert.That(() => new StatCurve(0.06f, 4f, 1, 0), Throws.Nothing);
        Assert.That(() => new StatCurve(0.02f, 1.3f, 5, 0), Throws.Nothing);
    }

    [Test]
    public void StatCurve_ZeroRate_IsFlat()
    {
        // Legal, and it means "depth does not touch this stat" — which is a design choice a mode
        // is allowed to make, not a curve that failed to be authored. IsAuthored keys off the cap
        // for exactly this reason.
        var flat = new StatCurve(0f, 4f, 1, 1);

        Assert.That(flat.At(1), Is.EqualTo(1f).Within(MultiplierTolerance));
        Assert.That(flat.At(999), Is.EqualTo(1f).Within(MultiplierTolerance));
    }

    [Test]
    public void ScalingSpec_DefaultCurve_Throws()
    {
        // A struct always has a zeroed form and it carries no guard (AR §18.3) — and each of these
        // fails in silence rather than loudly. A zeroed BudgetCurve affords nothing at every
        // depth; a zeroed StatCurve caps every multiplier at 0, which gives every enemy in the
        // game no hit points at all; a zeroed WaveCurve divides by zero on the first stage.
        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            default, Waves(), Concurrency(), Hp(), Damage(), Speed()));

        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            Budget(), default, Concurrency(), Hp(), Damage(), Speed()));

        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            Budget(), Waves(), default, Hp(), Damage(), Speed()));

        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            Budget(), Waves(), Concurrency(), default, Damage(), Speed()));

        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            Budget(), Waves(), Concurrency(), Hp(), default, Speed()));

        Assert.Throws<ArgumentException>(() => new ScalingSpec(
            Budget(), Waves(), Concurrency(), Hp(), Damage(), default));
    }

    [Test]
    public void ScalingSpec_HoldsWhatItWasGiven()
    {
        ScalingSpec scaling = DesignScaling();

        // The properties M2-04's composer reads, and the reason ThreatBudget can forward rather
        // than recompute: a spec that reordered its own curves would make h(n) scale damage.
        Assert.That(scaling.Budget.At(1), Is.EqualTo(40f).Within(BudgetTolerance));
        Assert.That(scaling.Waves.At(1), Is.EqualTo(2));
        Assert.That(scaling.Concurrency.At(1, MidTierCap), Is.EqualTo(10));
        Assert.That(scaling.Hp.At(51), Is.EqualTo(4f).Within(MultiplierTolerance));
        Assert.That(scaling.Damage.At(99), Is.EqualTo(3f).Within(MultiplierTolerance));
        Assert.That(scaling.Speed.At(5), Is.EqualTo(1.02f).Within(MultiplierTolerance));
    }

    [Test]
    public void ThreatBudget_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new ThreatBudget(null, MidTierCap));

        // A device that may hold no enemies is an arena nothing can be composed for, and the
        // symptom would be a stage that spawns nothing rather than anything anyone could read.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThreatBudget(DesignScaling(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThreatBudget(DesignScaling(), -28));

        // And the cap reaches the curve rather than merely being stored: ConcurrencyCurve refuses
        // its own bad cap too, which is what stops a caller bypassing this one.
        Assert.Throws<ArgumentOutOfRangeException>(() => Concurrency().At(1, 0));
    }

    // ---- Fixture helpers -------------------------------------------------------------------------

    /// <summary>GD §12's curves, bound to a device cap.</summary>
    private static ThreatBudget Design(int deviceCap = MidTierCap) =>
        new ThreatBudget(DesignScaling(), deviceCap);

    /// <summary>
    /// GD §12's five curves, written out here rather than loaded from <c>Descent.asset</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not the asset: this fixture is about whether the arithmetic matches the design
    /// document, and reading the numbers from the same place the game reads them would make it a
    /// test of a copy against itself. That the asset carries these values is
    /// <c>ModeDefinitionTests.Descent_MatchesDesign</c>'s row, in the assembly that can see an
    /// asset at all.
    /// </remarks>
    private static ScalingSpec DesignScaling() =>
        new ScalingSpec(Budget(), Waves(), Concurrency(), Hp(), Damage(), Speed());

    private static BudgetCurve Budget() => new BudgetCurve(40f, 12f, 0.9f);

    private static WaveCurve Waves() => new WaveCurve(2, 5, 2, 5);

    private static ConcurrencyCurve Concurrency() => new ConcurrencyCurve(10, 2);

    private static StatCurve Hp() => new StatCurve(0.06f, 4f, 1, 1);

    private static StatCurve Damage() => new StatCurve(0.035f, 3f, 1, 1);

    private static StatCurve Speed() => new StatCurve(0.02f, 1.3f, 5, 0);
}
