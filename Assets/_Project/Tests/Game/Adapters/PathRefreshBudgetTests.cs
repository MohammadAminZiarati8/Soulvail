using System;
using NUnit.Framework;
using Soulvail.Game.Adapters;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The arithmetic ledger row 5 was missing: how many routes a frame may recompute, at a population
/// and a frame time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than in <c>NavPathSenseTests</c>, because there is no such fixture and could not
/// usefully be one.</b> An EditMode scene has no baked NavMesh, so every <c>CalculatePath</c> fails
/// instantly and answers the straight line — which means the sense around this arithmetic can only
/// be verified by playing the game (M1-19 left it that way deliberately). Pulling the number out
/// into a class that holds no Unity type is what makes the part that can be tested, testable.
/// </para>
/// <para>
/// <b>The numbers below are the ones M2-04 measured against.</b> Twenty-eight enemies is the device
/// cap; 60 and 30 fps are the two frame rates the project tunes for; and the old fixed budget of
/// four is what these rows exist to replace — it sustained 24 enemies at 60 fps and 12 at 30, both
/// below the cap and the second below GD §11.1's <em>low</em> tier.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PathRefreshBudgetTests
{
    /// <summary>CC's cadence for things the player cannot see the seams of — <c>NavPathSense</c>'s.</summary>
    private const float RefreshHz = 10f;

    /// <summary>M2-04's device cap, and what M2-05's budget has to keep up with.</summary>
    private const int DeviceCap = 28;

    private const float At60 = 1f / 60f;
    private const float At30 = 1f / 30f;

    [Test]
    public void Budget_ScalesWithPopulationAndDt()
    {
        var budget = new PathRefreshBudget(RefreshHz, PathRefreshBudget.DefaultMaxPerFrame);

        // 28 · 10 · (1/60) = 4.67, rounded up. The old constant was 4, which is why 28 enemies
        // could not all be pathed: they need five a frame and were allowed four.
        Assert.That(budget.ForFrame(DeviceCap, At60), Is.EqualTo(5));

        // Half the frame rate, twice the work per frame — the same 280 recomputes a second. A
        // budget that did not scale would halve the cadence instead, which is what made the old
        // constant sustain only twelve enemies at 30 fps.
        Assert.That(budget.ForFrame(DeviceCap, At30), Is.EqualTo(10));
    }

    [Test]
    public void Budget_AtLeastOne()
    {
        var budget = new PathRefreshBudget(RefreshHz, PathRefreshBudget.DefaultMaxPerFrame);

        // 1 · 10 · (1/60) = 0.17, which rounds up to one: a lone enemy is refreshed every sixth
        // frame rather than never, because a budget that floors to zero paths nothing at all.
        Assert.That(budget.ForFrame(1, At60), Is.EqualTo(1));
    }

    [Test]
    public void Budget_ClampsAtMax()
    {
        var budget = new PathRefreshBudget(RefreshHz, PathRefreshBudget.DefaultMaxPerFrame);

        // A half-second frame is a hitch, and the arithmetic alone would answer 140 synchronous A*
        // searches — on the frame that has already gone wrong, which would make the next one
        // worse. The ceiling is what stops a hitch feeding itself; the shortfall shows up as
        // NavPathSense.StalePathCount instead of disappearing.
        Assert.That(budget.ForFrame(DeviceCap, 0.5f), Is.EqualTo(PathRefreshBudget.DefaultMaxPerFrame));
    }

    [Test]
    public void Budget_ZeroActive_IsOne()
    {
        var budget = new PathRefreshBudget(RefreshHz, PathRefreshBudget.DefaultMaxPerFrame);

        // An empty arena, and the first frame of a run — which has no previous frame to measure a
        // step against, so it asks with a population of nothing. Neither divides and neither throws.
        Assert.That(budget.ForFrame(0, At60), Is.EqualTo(1));
        Assert.That(budget.ForFrame(0, 0f), Is.EqualTo(1));
    }

    [Test]
    public void Ctor_Guards()
    {
        // Negated positive rather than `<= 0`, because every comparison against NaN is false: the
        // natural spelling would admit one, and a NaN rate makes every budget NaN (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathRefreshBudget(0f, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathRefreshBudget(-1f, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathRefreshBudget(float.NaN, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathRefreshBudget(float.PositiveInfinity, 16));

        // A ceiling of zero never recomputes anything, ever — every enemy would keep the route it
        // was born with, which is no route at all.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathRefreshBudget(RefreshHz, 0));
    }

    [Test]
    public void ForFrame_Guards()
    {
        var budget = new PathRefreshBudget(RefreshHz, PathRefreshBudget.DefaultMaxPerFrame);

        Assert.Throws<ArgumentOutOfRangeException>(() => budget.ForFrame(-1, At60));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.ForFrame(DeviceCap, -At60));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.ForFrame(DeviceCap, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.ForFrame(DeviceCap, float.PositiveInfinity));
    }
}
