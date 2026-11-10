using System;

namespace Soulvail.Core.Content;

/// <summary>How a solo Lunger recovers after a miss and circles its quarry after a dash.</summary>
public sealed class SidewinderSpec
{
    public SidewinderSpec(float missRecoverTime, float speed, float arcDegrees, float radius, float timeout)
    {
        MissRecoverTime = Positive(missRecoverTime, nameof(missRecoverTime));
        Speed = Positive(speed, nameof(speed));
        ArcDegrees = Positive(arcDegrees, nameof(arcDegrees));
        Radius = Positive(radius, nameof(radius));
        Timeout = Positive(timeout, nameof(timeout));

        if (arcDegrees > 180f)
        {
            throw new ArgumentOutOfRangeException(nameof(arcDegrees), arcDegrees, "The arc must be at most 180 degrees.");
        }
    }

    /// <summary>Seconds of recovery after a missed strike.</summary>
    public float MissRecoverTime { get; }

    /// <summary>Circling speed in metres per second.</summary>
    public float Speed { get; }

    /// <summary>Degrees to circle before stalking again.</summary>
    public float ArcDegrees { get; }

    /// <summary>Preferred distance from the quarry in metres on XZ.</summary>
    public float Radius { get; }

    /// <summary>Maximum seconds spent circling after one strike.</summary>
    public float Timeout { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
