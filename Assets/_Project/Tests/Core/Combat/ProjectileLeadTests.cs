using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// CC §3.7's lead as arithmetic: what it answers for a target standing still, crossing, fleeing and
/// outrunning the shot, what the second iteration is actually worth, and what it does with an input
/// nobody can read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convergence is measured rather than quoted, and the residual is how.</b> A solved aim point
/// claims "a shot at this speed reaches here at the same moment the target does", so the honest test
/// is to take the claim apart again: fly to the aim point, ask where the target is at that moment,
/// and measure the gap. <c>Residual</c> below is that number, and it is the only thing in this
/// fixture that distinguishes one iteration from two — a row asserting "the aim point is ahead of the
/// target" would pass for one iteration, for two, and for a bug that multiplied by ten.
/// </para>
/// <para>
/// The Gravecaller's numbers where a row has a choice (M5-02): 40 m/s of flight against a Husk's
/// 3.5 m/s walk, at the sort of range a class with a 12 m acquire range fights at. The rows about
/// refusal and about a target faster than the shot use whatever makes the case, and say so.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProjectileLeadTests
{
    /// <summary>The Gravecaller's bolt (M5-02): fast, and still slow enough to need leading.</summary>
    private const float ShotSpeed = 40f;

    /// <summary>GD §8.1's Husk walk, which is what most of what the player shoots at is doing.</summary>
    private const float TargetSpeed = 3f;

    /// <summary>Well inside a 12 m acquire range, so the geometry is a fight rather than a puzzle.</summary>
    private const float Range = 10f;

    /// <summary>
    /// A centimetre. The residual a solved aim point has to beat — which is a hundredth of a Husk's
    /// width, and two orders of magnitude inside <c>WeaponSpec.ShotRadius</c> on anything M5 authors.
    /// </summary>
    private const float OneCentimetre = 0.01f;

    // ---- The arithmetic --------------------------------------------------------------------------

    [Test]
    public void Lead_AStationaryTargetSolvesToItself()
    {
        Vector3 target = At(Range);

        // Exactly, not approximately, and at three speeds: a zero velocity makes the offset zero on
        // both iterations whatever the flight time was, so the second pass provably changes nothing.
        // This is the row that says "iterate twice" costs the common case nothing.
        foreach (float speed in new[] { 1f, ShotSpeed, 1000f })
        {
            Assert.That(
                ProjectileLead.Solve(Vector3.Zero, target, Vector3.Zero, speed),
                Is.EqualTo(target),
                $"at {speed} m/s");
        }
    }

    [Test]
    public void Lead_ACrossingTargetIsLedAhead()
    {
        // 10 m up +Z, walking across the line of fire at 3 m/s on +X. The hardest case for a shot
        // and the one the whole mechanic exists for: aimed at the target, the bolt passes behind it.
        Vector3 target = At(Range);
        var velocity = new Vector3(TargetSpeed, 0f, 0f);

        Vector3 aim = ProjectileLead.Solve(Vector3.Zero, target, velocity, ShotSpeed);

        Assert.That(aim.X, Is.GreaterThan(target.X), "Led along the velocity, not behind it.");
        Assert.That(Residual(Vector3.Zero, target, velocity, ShotSpeed, aim), Is.LessThan(OneCentimetre));
    }

    [Test]
    public void Lead_TheSecondIterationImprovesTheFirst()
    {
        Vector3 target = At(Range);
        var velocity = new Vector3(TargetSpeed, 0f, 0f);

        // One iteration, spelled out here rather than exposed on ProjectileLead: t = d / v,
        // aim = p + vel·t, once. The second pass is what Solve adds, and the point of this row is
        // that CC §3.7's "iterate twice" is worth something measurable rather than being a ritual.
        float flightTime = Vector3.Distance(Vector3.Zero, target) / ShotSpeed;
        Vector3 once = target + (velocity * flightTime);

        Vector3 twice = ProjectileLead.Solve(Vector3.Zero, target, velocity, ShotSpeed);

        float residualOnce = Residual(Vector3.Zero, target, velocity, ShotSpeed, once);
        float residualTwice = Residual(Vector3.Zero, target, velocity, ShotSpeed, twice);

        Assert.That(residualTwice, Is.LessThan(residualOnce));

        // And by a margin worth the pass: two orders of magnitude on this geometry, which is what
        // makes the answer to "why not three iterations" arithmetic rather than taste.
        Assert.That(residualTwice, Is.LessThan(residualOnce / 10f));
    }

    [Test]
    public void Lead_ARetreatingTargetIsStillReachable()
    {
        // Straight away up +Z at 3 m/s — a Husk that has lost aggro, or anything walking towards
        // the next wave. The aim point has to be *further* than the target, and finite.
        Vector3 target = At(Range);
        var velocity = new Vector3(0f, 0f, TargetSpeed);

        Vector3 aim = ProjectileLead.Solve(Vector3.Zero, target, velocity, ShotSpeed);

        Assert.That(IsFinite(aim), Is.True);
        Assert.That(aim.Z, Is.GreaterThan(target.Z));
        Assert.That(Residual(Vector3.Zero, target, velocity, ShotSpeed, aim), Is.LessThan(OneCentimetre));
    }

    [Test]
    public void Lead_ATargetFasterThanTheShotDoesNotDiverge()
    {
        // 60 m/s across a 40 m/s bolt: there is genuinely no solution, and the aim point recedes on
        // every pass. Two fixed iterations cannot run away from that — which is the argument for a
        // fixed count instead of a convergence loop, made as a number rather than as a claim.
        Vector3 target = At(Range);
        var velocity = new Vector3(60f, 0f, 0f);

        Vector3 aim = ProjectileLead.Solve(Vector3.Zero, target, velocity, ShotSpeed);

        Assert.That(IsFinite(aim), Is.True);
        Assert.That(aim.Length(), Is.LessThan(200f), "Bounded — an arena is 40 m across.");
    }

    [Test]
    public void Lead_IgnoresHeight()
    {
        // Two targets at the same place on the ground, four metres apart in the air. AR §18.4: a
        // height is a rendering detail, and counting it would inflate the flight time and therefore
        // over-lead every shot in the game by however tall the target happens to be.
        var velocity = new Vector3(TargetSpeed, 0f, 0f);

        Vector3 low = new(0f, 0f, Range);
        Vector3 high = new(0f, 4f, Range);

        Vector3 aimLow = ProjectileLead.Solve(Vector3.Zero, low, velocity, ShotSpeed);
        Vector3 aimHigh = ProjectileLead.Solve(new Vector3(0f, 2f, 0f), high, velocity, ShotSpeed);

        Assert.That(aimHigh.X, Is.EqualTo(aimLow.X).Within(1e-5f));
        Assert.That(aimHigh.Z, Is.EqualTo(aimLow.Z).Within(1e-5f));

        // The aim point keeps the target's own Y rather than flattening it to the ground, so nothing
        // here invents a ground plane the arrival would then have to agree with.
        Assert.That(aimHigh.Y, Is.EqualTo(4f));
        Assert.That(aimLow.Y, Is.EqualTo(0f));

        // The velocity's Y is not read either: a target rising at 9 m/s leads to the same XZ point.
        Assert.That(
            ProjectileLead.Solve(Vector3.Zero, low, new Vector3(TargetSpeed, 9f, 0f), ShotSpeed),
            Is.EqualTo(aimLow));
    }

    // ---- The refusals ----------------------------------------------------------------------------

    [Test]
    public void Lead_RefusesNonFiniteInputs()
    {
        Vector3 target = At(Range);
        var velocity = new Vector3(TargetSpeed, 0f, 0f);

        var nan = new Vector3(float.NaN, 0f, 0f);
        var infinite = new Vector3(float.PositiveInfinity, 0f, 0f);

        // Every refusal answers with the target's own position — the unled aim point — which is a
        // shot that may miss a moving target rather than a shot aimed at NaN. A NaN aim point is the
        // one outcome Projectile's constructor exists to refuse, and the reason it never reaches that
        // door is this method.
        Assert.That(ProjectileLead.Solve(Vector3.Zero, target, velocity, float.NaN), Is.EqualTo(target));
        Assert.That(ProjectileLead.Solve(Vector3.Zero, target, velocity, 0f), Is.EqualTo(target));
        Assert.That(ProjectileLead.Solve(Vector3.Zero, target, velocity, -5f), Is.EqualTo(target));
        Assert.That(
            ProjectileLead.Solve(Vector3.Zero, target, velocity, float.PositiveInfinity),
            Is.EqualTo(target));

        Assert.That(ProjectileLead.Solve(nan, target, velocity, ShotSpeed), Is.EqualTo(target));
        Assert.That(ProjectileLead.Solve(infinite, target, velocity, ShotSpeed), Is.EqualTo(target));
        Assert.That(ProjectileLead.Solve(Vector3.Zero, target, nan, ShotSpeed), Is.EqualTo(target));
        Assert.That(ProjectileLead.Solve(Vector3.Zero, target, infinite, ShotSpeed), Is.EqualTo(target));

        // And in every one of those the answer is a point a shot can be aimed at, which is the half
        // of the promise that matters: nothing here manufactures a NaN out of finite inputs.
        Assert.That(IsFinite(ProjectileLead.Solve(nan, target, nan, float.NaN)), Is.True);

        // A NaN *target* is the one case that comes back non-finite, and it has to: the contract is
        // "targetPosition unchanged", and there is no other honest answer to "where is the thing you
        // cannot locate". It is refused one door further on, by Projectile's own guard.
        //
        // Asserted component by component rather than with Is.EqualTo, because NaN does not equal
        // itself: comparing the two Vector3s fails while printing two identical values, which is
        // exactly how this row first failed.
        Vector3 unchanged = ProjectileLead.Solve(Vector3.Zero, nan, velocity, ShotSpeed);

        Assert.That(float.IsNaN(unchanged.X), Is.True);
        Assert.That(unchanged.Y, Is.EqualTo(nan.Y));
        Assert.That(unchanged.Z, Is.EqualTo(nan.Z));
    }

    [Test]
    public void Lead_IteratesTwice()
    {
        // The constant is public because CC §3.7 names it, and a lead that quietly became one pass
        // would still satisfy every row above except TheSecondIterationImprovesTheFirst.
        Assert.That(ProjectileLead.Iterations, Is.EqualTo(2));
    }

    // ---- The budget ------------------------------------------------------------------------------

    [Test]
    public void Lead_AllocatesNothing()
    {
        Vector3 target = At(Range);
        var velocity = new Vector3(TargetSpeed, 0f, 0f);

        // 100 000 solves, because this runs on the damage frame of every shot of every projectile
        // class — AR §4.3. Measured with AllocationAssert rather than a hand-rolled GC probe, which
        // on Unity's Mono reports zero for code that allocates freely (Traps §7).
        AllocationAssert.None(
            () => ProjectileLead.Solve(Vector3.Zero, target, velocity, ShotSpeed),
            iterations: 100_000);
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A point <paramref name="metres"/> up the +Z axis, on the ground.</summary>
    private static Vector3 At(float metres) => new(0f, 0f, metres);

    /// <summary>
    /// How far an aim point misses by: fly to it at <paramref name="speed"/>, and measure the gap
    /// between where it is and where the target has walked to in the meantime.
    /// </summary>
    /// <remarks>
    /// XZ, like the solve, so a target with a height does not fail a row about leading. This is the
    /// claim a lead makes, undone — see the fixture's remarks on why it is the only assertion that
    /// can tell one iteration from two.
    /// </remarks>
    private static float Residual(
        Vector3 origin,
        Vector3 targetPosition,
        Vector3 targetVelocity,
        float speed,
        Vector3 aim)
    {
        float dx = aim.X - origin.X;
        float dz = aim.Z - origin.Z;

        float flightTime = MathF.Sqrt((dx * dx) + (dz * dz)) / speed;

        float missX = aim.X - (targetPosition.X + (targetVelocity.X * flightTime));
        float missZ = aim.Z - (targetPosition.Z + (targetVelocity.Z * flightTime));

        return MathF.Sqrt((missX * missX) + (missZ * missZ));
    }

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
