using System;

namespace Soulvail.Core.Content;

/// <summary>Authored front shield, steering rate and walking cone for a Bulwark (JG-14a).</summary>
public sealed class BulwarkSpec
{
    public BulwarkSpec(float shieldHalfArcDegrees, float turnRateDegrees, float walkConeDegrees)
    {
        if (!(shieldHalfArcDegrees > 0f && shieldHalfArcDegrees < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(shieldHalfArcDegrees));
        }

        if (!(turnRateDegrees > 0f) || float.IsInfinity(turnRateDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(turnRateDegrees));
        }

        if (!(walkConeDegrees > 0f && walkConeDegrees <= 90f))
        {
            throw new ArgumentOutOfRangeException(nameof(walkConeDegrees));
        }

        ShieldHalfArcDegrees = shieldHalfArcDegrees;
        TurnRateDegrees = turnRateDegrees;
        WalkConeDegrees = walkConeDegrees;
    }

    /// <summary>Half-angle of the protected front, in degrees, strictly between 0 and 180.</summary>
    public float ShieldHalfArcDegrees { get; }

    /// <summary>Maximum degrees turned per second while approaching.</summary>
    public float TurnRateDegrees { get; }

    /// <summary>Maximum angle to the path at which it may walk, in degrees, in (0, 90].</summary>
    public float WalkConeDegrees { get; }
}
