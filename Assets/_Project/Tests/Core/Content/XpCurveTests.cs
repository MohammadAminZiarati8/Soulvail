using System;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// <c>XpCurve</c>: CH §5.2's formula, the guards that stop a zeroed one hanging a run, and the
/// pacing the shipped numbers actually produce against GD §12.1's budget.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every number here is written out in full rather than taken from <c>Scalings</c></b>, for the
/// reason <c>ThreatBudgetTests</c> and <c>DepthScalingTests</c> give: a test of the arithmetic
/// against a shared helper is a test of a copy against itself. The fixtures that want "a valid
/// curve" use the helper; this one is about the curve.
/// </para>
/// <para>
/// <b>The two pacing rows are the reason this fixture is worth more than the formula rows.</b>
/// CH §5.2 publishes both a formula and a table of what it should produce, and past stage 10 they
/// disagree — the tree fills around stage 20 where the table says 30. The owner ruled at M3-00a
/// that the numbers ship as authored and M3-15 measures a real run before anyone retunes, so the
/// divergence is <em>pinned</em> here rather than fixed: changing the exponent turns
/// <see cref="Pacing_TreeFullByStageTwenty"/> red, which makes a retune a visible diff instead of
/// a silent drift.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XpCurveTests
{
    /// <summary>CH §5.2's authored curve: ToReach(N) = 20 + 12·N^1.4.</summary>
    private const float Base = 20f;
    private const float PerLevel = 12f;
    private const float Exponent = 1.4f;

    [Test]
    public void Curve_LevelOneIsFree()
    {
        var curve = new XpCurve(Base, PerLevel, Exponent);

        // Level 1 is where every run starts, so "what did it cost to get here" has exactly one
        // honest answer. Zero and the negatives get the same one because they are the same
        // statement — a level nobody had to earn — which is deliberately not CurveGuard.Stage's
        // behaviour, and the remarks on ToReach argue the difference.
        Assert.That(curve.ToReach(1), Is.EqualTo(0f));
        Assert.That(curve.ToReach(0), Is.EqualTo(0f));
        Assert.That(curve.ToReach(-3), Is.EqualTo(0f));
    }

    [Test]
    public void Curve_MatchesCharacters52()
    {
        var curve = new XpCurve(Base, PerLevel, Exponent);

        // Computed from the formula, not copied from CH §5.2's table — the table is what the next
        // row proves cannot also be true.
        Assert.That(curve.ToReach(2), Is.EqualTo(51.7f).Within(0.5f));
        Assert.That(curve.ToReach(8), Is.EqualTo(240.5f).Within(0.5f));
        Assert.That(curve.ToReach(13), Is.EqualTo(455.2f).Within(0.5f));
        Assert.That(curve.ToReach(22), Is.EqualTo(929f).Within(0.5f));
        Assert.That(curve.ToReach(30), Is.EqualTo(1423f).Within(0.5f));
    }

    [Test]
    public void Curve_Guards()
    {
        // A negative base makes the early levels cost less than nothing, which is a tracker that
        // levels on a grant of one point and reports nothing wrong.
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(-1f, PerLevel, Exponent));

        // Zero perLevel is the one that hangs: every level then costs the base alone, and a base
        // of zero with it costs nothing at all. IsAuthored is spelled off this field for that
        // reason.
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, 0f, Exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, -1f, Exponent));

        // At exponent zero every level costs the same and the curve stops being one.
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, PerLevel, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, PerLevel, -1f));

        // NaN and infinity at all three doors. NaN is the one a natural `< 0` spelling waves
        // through, because every comparison against it is false (AR §18.3); infinity passes a
        // `> 0` test and is asked about separately.
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(float.NaN, PerLevel, Exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(float.PositiveInfinity, PerLevel, Exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, float.NaN, Exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, float.PositiveInfinity, Exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, PerLevel, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpCurve(Base, PerLevel, float.PositiveInfinity));

        // A base of zero is legal: a curve that is pure exponent, whose first levels are nearly
        // free. Only perLevel and the exponent are load-bearing.
        Assert.DoesNotThrow(() => new XpCurve(0f, PerLevel, Exponent));
    }

    [Test]
    public void Curve_DefaultIsUnauthored()
    {
        // A struct always has a zeroed form that carried no guard, and this one's consequence is
        // the sharpest in the project: every level costs 0, so LevelTracker.Grant's loop never
        // terminates. Both ends check (AR §18.3) — here, on the mode, and again on the tracker.
        Assert.That(default(XpCurve).IsAuthored, Is.False);
        Assert.That(new XpCurve(Base, PerLevel, Exponent).IsAuthored, Is.True);

        Assert.Throws<ArgumentException>(
            () => new ModeSpec(
                new ContentId("mode.test"),
                new LocKey("mode.test.name"),
                1,
                true,
                0,
                Scalings.Design(),
                default,
                Array.Empty<RosterEntry>()),
            "ModeSpec refuses a default curve the way ScalingSpec.Require refuses a default "
                + "BudgetCurve.");
    }

    [Test]
    public void Curve_AllocatesNothing()
    {
        var curve = new XpCurve(Base, PerLevel, Exponent);

        // A struct read through a local, so nothing here can be boxed by the closure and quietly
        // pass. The row exists because ToReach is called once per threshold crossed inside a
        // per-tick grant, which is the path that must not allocate.
        AllocationAssert.None(() => curve.ToReach(17));
    }

    [Test]
    public void Pacing_LevelEightByStageFive()
    {
        // GD §12.1's shipped budget: B(n) = 40 + 12(n−1) + 0.9(n−1)². Written out rather than
        // taken from Scalings, for this fixture's reason.
        var budget = new BudgetCurve(40f, 12f, 0.9f);

        LevelTracker tracker = Play(budget, throughStage: 5);

        // CH §5.2's own table says 8 at stage 5, and the formula agrees — which is the half of
        // rule 9 that holds, and the band GD §4.4's "a level every 25–35 seconds" rests on. Stage
        // 1 alone is two levels, from ten Husks.
        Assert.That(
            tracker.Level,
            Is.EqualTo(8),
            "CH §5.2's table and its formula agree through stage 5.");
    }

    [Test]
    public void Pacing_TreeFullByStageTwenty()
    {
        var budget = new BudgetCurve(40f, 12f, 0.9f);

        LevelTracker tracker = Play(budget, throughStage: 20);

        // **The divergence, pinned rather than fixed.** CH §5.2's table says 22 at stage 20 and a
        // 27-node tree full at stage 30; the formula gives 27 by stage 20, so the tree fills ten
        // stages early. The curve and its own table cannot both be true against a quadratic budget
        // with flat XP per kill.
        //
        // A band rather than an exact number because the point is the *shape* — this fails if
        // anybody retunes the exponent (≈1.6 fits the table's own 5 / 10 / 20 rows), which is
        // exactly the change M3-15 is expected to make once it has measured a run with a
        // stopwatch. It is not here to be satisfied; it is here so the retune shows up.
        Assert.That(
            tracker.Level,
            Is.InRange(26, 28),
            "The tree fills around stage 20, not stage 30 — CH §5.2's table and its formula "
                + "disagree past stage 10 and the numbers ship as authored (M3-00a ruling).");
    }

    /// <summary>
    /// Grants a tracker every stage's whole budget as experience, stages 1 through
    /// <paramref name="throughStage"/>, and hands it back.
    /// </summary>
    /// <remarks>
    /// <b>XP = 3 × threat cost is what makes this a composer-free calculation</b>, and it is the
    /// whole reason the ratio was chosen: a stage's experience is <c>3 · B(n)</c> less whatever
    /// the composer could not spend, whatever mix of archetypes the seed drew. Granting the full
    /// budget is therefore the ceiling of what a stage can pay and within a few threat of what it
    /// does pay — close enough that the level it lands on is the level the game produces, and
    /// exact enough that no wave composer has to be built to check the design's arithmetic.
    /// </remarks>
    private static LevelTracker Play(BudgetCurve budget, int throughStage)
    {
        var tracker = new LevelTracker(new XpCurve(Base, PerLevel, Exponent), new SilentEvents());

        for (int stage = 1; stage <= throughStage; stage++)
        {
            tracker.Grant(3f * budget.At(stage));
        }

        return tracker;
    }
}
