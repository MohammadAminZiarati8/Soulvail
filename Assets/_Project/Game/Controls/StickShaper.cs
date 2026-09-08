using UnityEngine;

namespace Soulvail.Game.Controls;

/// <summary>
/// The floating stick's arithmetic, with no Unity object, no state and no frame behind it.
/// CC §2.1–2.3 in three functions: shape a drag into a movement vector, keep the origin within
/// reach of the thumb, and convert device pixels to density-independent ones.
/// </summary>
/// <remarks>
/// <para>
/// Split out of <c>FloatingStick</c> because this is the only part of the control that can be
/// tested at all: the pointer half needs an <c>EventSystem</c>, a canvas and a live touch, while
/// every number a player can feel lives here and is pinned to the decimal point by
/// <c>StickShaperTests</c>. The component below is then thin enough to read for correctness.
/// </para>
/// <para>
/// Everything is in **dp** — 1/160 inch — because a thumb is the same physical size on every
/// phone while a pixel is not. The caller converts once, at the screen boundary, with
/// <see cref="PixelsPerDp"/>.
/// </para>
/// </remarks>
public static class StickShaper
{
    /// <summary>Below this much drag the stick reports nothing. CC §2.1.</summary>
    public const float DeadzoneDp = 8f;

    /// <summary>At and beyond this much drag the stick reports full speed. CC §2.2.</summary>
    public const float FullSpeedDp = 40f;

    /// <summary>How far the thumb may get from the origin before the origin follows it. CC §2.3.</summary>
    public const float MaxRadiusDp = 60f;

    /// <summary>Reference density: 160 dpi is 1 pixel per dp, by definition.</summary>
    private const float ReferenceDpi = 160f;

    /// <summary>
    /// Turns a drag — <c>touch − origin</c>, in dp — into a movement vector: the drag's direction
    /// scaled by a magnitude in [0, 1].
    /// </summary>
    /// <remarks>
    /// Zero up to <see cref="DeadzoneDp"/>, linear from there to <see cref="FullSpeedDp"/>, flat
    /// at 1 beyond it (CC §2.2). Direction is preserved exactly — the result is always the input
    /// times a scalar, never a re-derived unit vector, so a diagonal drag stays exactly diagonal.
    /// </remarks>
    public static Vector2 Shape(Vector2 dragDp)
    {
        float distance = dragDp.magnitude;

        // Spelled as a negated positive test, not "distance <= DeadzoneDp": every comparison
        // against NaN is false, so the natural spelling would let a NaN drag through and put a
        // permanent NaN into the player's velocity (M0-07).
        if (!(distance > DeadzoneDp))
        {
            return Vector2.zero;
        }

        if (distance >= FullSpeedDp)
        {
            return dragDp / distance;
        }

        float speed = (distance - DeadzoneDp) / (FullSpeedDp - DeadzoneDp);
        return dragDp * (speed / distance);
    }

    /// <summary>
    /// Keeps the origin within <see cref="MaxRadiusDp"/> of the thumb: beyond that it is dragged
    /// along so it sits exactly <see cref="MaxRadiusDp"/> behind the touch, on the same line.
    /// Inside the radius the origin is returned untouched.
    /// </summary>
    /// <remarks>
    /// This is the rule CC §2.3 says not to skip. Without it, a thumb that has travelled 200 dp
    /// right must travel 200 dp back before the player moves left at all; the controls read as
    /// laggy and nobody can say why.
    /// </remarks>
    public static Vector2 Recenter(Vector2 originDp, Vector2 touchDp)
    {
        Vector2 delta = touchDp - originDp;
        float distance = delta.magnitude;

        if (!(distance > MaxRadiusDp))
        {
            return originDp;
        }

        return touchDp - (delta * (MaxRadiusDp / distance));
    }

    /// <summary>
    /// Device pixels per dp: <c>dpi / 160</c>. A non-positive or unknown dpi — which is what the
    /// Editor and some emulators report — falls back to 1, so the stick behaves as if it were on
    /// a 160 dpi screen rather than collapsing to a divide by zero.
    /// </summary>
    public static float PixelsPerDp(float dpi)
    {
        // Same NaN-safe spelling as Shape: an unknown dpi must land on the fallback, not sail
        // through and scale every threshold to NaN.
        return dpi > 0f ? dpi / ReferenceDpi : 1f;
    }
}
