using System;

namespace Soulvail.Core.Content;

/// <summary>Movement, timing and damage for a vulture's dive cycle.</summary>
public sealed class DiveSpec
{
    public DiveSpec(float circleRadius, float circleSpeed, float approachSpeed, float minIntervalSeconds,
        float maxIntervalSeconds, float lockSeconds, float lineLength, float lineHalfWidth,
        float diveSeconds, float diveDamage, float groundedSeconds, float climbSeconds)
    {
        CircleRadius = Positive(circleRadius, nameof(circleRadius));
        CircleSpeed = Positive(circleSpeed, nameof(circleSpeed));
        ApproachSpeed = Positive(approachSpeed, nameof(approachSpeed));
        MinIntervalSeconds = Positive(minIntervalSeconds, nameof(minIntervalSeconds));
        MaxIntervalSeconds = Positive(maxIntervalSeconds, nameof(maxIntervalSeconds));
        LockSeconds = Positive(lockSeconds, nameof(lockSeconds));
        LineLength = Positive(lineLength, nameof(lineLength));
        LineHalfWidth = Positive(lineHalfWidth, nameof(lineHalfWidth));
        DiveSeconds = Positive(diveSeconds, nameof(diveSeconds));
        DiveDamage = Positive(diveDamage, nameof(diveDamage));
        GroundedSeconds = Positive(groundedSeconds, nameof(groundedSeconds));
        ClimbSeconds = Positive(climbSeconds, nameof(climbSeconds));

        if (MaxIntervalSeconds < MinIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIntervalSeconds), maxIntervalSeconds,
                "The maximum interval must be at least the minimum.");
        }

        if (LineLength <= CircleRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(lineLength), lineLength,
                "The dive line must extend through the hero from the circle.");
        }
    }

    public float CircleRadius { get; }
    public float CircleSpeed { get; }
    public float ApproachSpeed { get; }
    public float MinIntervalSeconds { get; }
    public float MaxIntervalSeconds { get; }
    public float LockSeconds { get; }
    public float LineLength { get; }
    public float LineHalfWidth { get; }
    public float DiveSeconds { get; }
    public float DiveDamage { get; }
    public float GroundedSeconds { get; }
    public float ClimbSeconds { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
