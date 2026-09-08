using NUnit.Framework;
using Soulvail.Game.Controls;
using Soulvail.Tests.Core.Support;
using UnityEngine;

namespace Soulvail.Tests.Game.Controls;

/// <summary>
/// Every number a thumb can feel, pinned to the decimal point. CC §2.1–2.3 and §7 are the source
/// of the constants; this fixture is what stops them drifting.
/// </summary>
/// <remarks>
/// The pointer half of the stick — capture, recentring across frames, visuals, the zero on
/// disable — is not reachable from EditMode: it needs an <c>EventSystem</c>, a canvas and a live
/// touch. Those rules are covered by the spec's Device Simulator walkthrough instead, which is
/// exactly why the arithmetic was split into a static class that owes Unity nothing.
/// </remarks>
[TestFixture]
public sealed class StickShaperTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void Shape_BelowDeadzone_IsZero()
    {
        Assert.That(StickShaper.Shape(new Vector2(5f, 0f)), Is.EqualTo(Vector2.zero));
        Assert.That(StickShaper.Shape(new Vector2(0f, 7.9f)), Is.EqualTo(Vector2.zero));
    }

    /// <remarks>
    /// The boundary is closed: exactly <c>DeadzoneDp</c> of drag is still nothing. A resting
    /// thumb that trembles onto the threshold must not twitch the player.
    /// </remarks>
    [Test]
    public void Shape_AtDeadzone_IsZero()
    {
        Assert.That(StickShaper.Shape(new Vector2(StickShaper.DeadzoneDp, 0f)), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void Shape_MidBand_IsLinear()
    {
        // 24 dp is the midpoint of the 8 → 40 band, so it must read as exactly half speed.
        Vector2 shaped = StickShaper.Shape(new Vector2(24f, 0f));

        Assert.That(shaped.x, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(shaped.y, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void Shape_AtFullSpeed_IsUnit()
    {
        Vector2 shaped = StickShaper.Shape(new Vector2(StickShaper.FullSpeedDp, 0f));

        Assert.That(shaped.x, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(shaped.y, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void Shape_BeyondFullSpeed_IsUnit_DirectionKept()
    {
        Vector2 straight = StickShaper.Shape(new Vector2(0f, 100f));
        Assert.That(straight.x, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(straight.y, Is.EqualTo(1f).Within(Tolerance));

        // 3-4-5: a 50 dp drag, well past full speed, still points exactly where the thumb does.
        Vector2 diagonal = StickShaper.Shape(new Vector2(30f, 40f));
        Assert.That(diagonal.x, Is.EqualTo(0.6f).Within(Tolerance));
        Assert.That(diagonal.y, Is.EqualTo(0.8f).Within(Tolerance));
        Assert.That(diagonal.magnitude, Is.EqualTo(1f).Within(Tolerance));
    }

    /// <remarks>
    /// The band is where direction is easiest to lose: the naive implementation renormalises and
    /// then scales, which is fine here but drifts once the scalar is folded in differently. The
    /// assertion is on both the direction and the magnitude so a regression cannot hide in either.
    /// </remarks>
    [Test]
    public void Shape_PreservesDirectionInBand()
    {
        // (12, 16) is 20 dp along the 3-4-5 diagonal: (20 − 8) / 32 = 0.375 of full speed.
        Vector2 shaped = StickShaper.Shape(new Vector2(12f, 16f));

        Assert.That(shaped.magnitude, Is.EqualTo(0.375f).Within(Tolerance));
        Assert.That(shaped.normalized.x, Is.EqualTo(0.6f).Within(Tolerance));
        Assert.That(shaped.normalized.y, Is.EqualTo(0.8f).Within(Tolerance));
    }

    [Test]
    public void Recenter_WithinRadius_Unchanged()
    {
        Vector2 origin = new Vector2(100f, 100f);

        Assert.That(StickShaper.Recenter(origin, new Vector2(150f, 100f)), Is.EqualTo(origin));
    }

    /// <remarks>
    /// The boundary is open, the mirror of the deadzone being closed: at exactly
    /// <c>MaxRadiusDp</c> nothing moves yet. Recentring one float early would drag the origin on
    /// every frame a player holds the stick at full extension.
    /// </remarks>
    [Test]
    public void Recenter_AtRadius_Unchanged()
    {
        Vector2 origin = new Vector2(100f, 100f);
        Vector2 touch = origin + new Vector2(StickShaper.MaxRadiusDp, 0f);

        Assert.That(StickShaper.Recenter(origin, touch), Is.EqualTo(origin));
    }

    [Test]
    public void Recenter_BeyondRadius_MovesOrigin()
    {
        Vector2 touch = new Vector2(100f, 0f);
        Vector2 moved = StickShaper.Recenter(Vector2.zero, touch);

        Assert.That(moved.x, Is.EqualTo(40f).Within(Tolerance));
        Assert.That(moved.y, Is.EqualTo(0f).Within(Tolerance));

        // The property that matters at runtime: the origin trails the thumb by exactly the radius,
        // so the very next frame of drag-back registers immediately.
        Assert.That((touch - moved).magnitude, Is.EqualTo(StickShaper.MaxRadiusDp).Within(Tolerance));
    }

    [Test]
    public void Recenter_BeyondRadius_Diagonal()
    {
        // (60, 80) is 100 dp out; the origin lands 60 dp behind it along the same line.
        Vector2 touch = new Vector2(60f, 80f);
        Vector2 moved = StickShaper.Recenter(Vector2.zero, touch);

        Assert.That(moved.x, Is.EqualTo(24f).Within(Tolerance));
        Assert.That(moved.y, Is.EqualTo(32f).Within(Tolerance));
        Assert.That((touch - moved).magnitude, Is.EqualTo(StickShaper.MaxRadiusDp).Within(Tolerance));
    }

    [Test]
    public void PixelsPerDp_Values()
    {
        Assert.That(StickShaper.PixelsPerDp(160f), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(StickShaper.PixelsPerDp(320f), Is.EqualTo(2f).Within(Tolerance));

        // Screen.dpi answers 0 on platforms that do not know, and the Editor is one of them.
        Assert.That(StickShaper.PixelsPerDp(0f), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(StickShaper.PixelsPerDp(-1f), Is.EqualTo(1f).Within(Tolerance));
    }

    /// <remarks>
    /// Not in the spec's Tests table, and here on purpose: behaviour rule 9 ("no per-frame
    /// allocation") is the one rule the table leaves unmeasured, because it belongs to the drag
    /// path and the drag path cannot be driven from EditMode. This covers the half of that path
    /// that can be — every call the component makes per drag frame — so the claim in PROGRESS is
    /// measured rather than asserted from reading the code. What it does *not* cover is the
    /// component's own frame: the <c>RectTransformUtility</c> calls and the event dispatch.
    /// </remarks>
    [Test]
    public void ShapeAndRecenter_DoNotAllocate()
    {
        Vector2 origin = Vector2.zero;
        Vector2 touch = new Vector2(70f, 90f);

        AllocationAssert.None(() =>
        {
            Vector2 recentred = StickShaper.Recenter(origin, touch);
            StickShaper.Shape(touch - recentred);
            StickShaper.PixelsPerDp(420f);
        });
    }
}
