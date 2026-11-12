using System;

namespace Soulvail.Core.Content;

/// <summary>Targeting, healing and recovery for a Shaman's single-ally channel.</summary>
public sealed class MendSpec
{
    public MendSpec(float range, float breakRange, float hurtBelow, float channelSeconds,
        float healPerSecond, float cooldown, float brokenSeconds)
    {
        Range = Positive(range, nameof(range));
        BreakRange = Positive(breakRange, nameof(breakRange));
        HurtBelow = Fraction(hurtBelow, nameof(hurtBelow));
        ChannelSeconds = Positive(channelSeconds, nameof(channelSeconds));
        HealPerSecond = Fraction(healPerSecond, nameof(healPerSecond));
        Cooldown = Positive(cooldown, nameof(cooldown));
        BrokenSeconds = Positive(brokenSeconds, nameof(brokenSeconds));
    }

    public float Range { get; }
    public float BreakRange { get; }
    public float HurtBelow { get; }
    public float ChannelSeconds { get; }
    public float HealPerSecond { get; }
    public float Cooldown { get; }
    public float BrokenSeconds { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }

    private static float Fraction(float value, string paramName)
    {
        if (!(value > 0f && value <= 1f))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The fraction must be greater than zero and at most one.");
        }

        return value;
    }
}
