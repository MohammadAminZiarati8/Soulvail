using System;

namespace Soulvail.Core.Content;

/// <summary>The led throw and retreat of a slinging Spitter (GD §8.5).</summary>
public sealed class SlingerSpec
{
    public SlingerSpec(float maxLead, float retreatSpeedFraction, float retreatTime)
    {
        MaxLead = Positive(maxLead, nameof(maxLead));
        if (!(retreatSpeedFraction > 0f) || retreatSpeedFraction > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retreatSpeedFraction), retreatSpeedFraction, "The retreat speed fraction must be in (0, 1].");
        }

        RetreatSpeedFraction = retreatSpeedFraction;
        RetreatTime = Positive(retreatTime, nameof(retreatTime));
    }

    public float MaxLead { get; }
    public float RetreatSpeedFraction { get; }
    public float RetreatTime { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
