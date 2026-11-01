using System;

namespace Soulvail.Core.Content;

/// <summary>Range-edge support, pulse and escape numbers for a Moss Shaman.</summary>
public sealed class ShamanSpec
{
    public ShamanSpec(float auraRadius, float pulseInterval, float castSeconds,
        float healFraction, float hasteFraction, float hasteSeconds, float holdDistance,
        float behindDistance, float fleeDistance, float fleeSeconds, float fleeSpeedMultiplier)
    {
        AuraRadius = Positive(auraRadius, nameof(auraRadius));
        PulseInterval = Positive(pulseInterval, nameof(pulseInterval));
        CastSeconds = Positive(castSeconds, nameof(castSeconds));
        HealFraction = Positive(healFraction, nameof(healFraction));
        if (healFraction > 1f)
        { throw new ArgumentOutOfRangeException(nameof(healFraction)); }
        HasteFraction = Positive(hasteFraction, nameof(hasteFraction));
        HasteSeconds = Positive(hasteSeconds, nameof(hasteSeconds));
        HoldDistance = Positive(holdDistance, nameof(holdDistance));
        BehindDistance = Positive(behindDistance, nameof(behindDistance));
        FleeDistance = Positive(fleeDistance, nameof(fleeDistance));
        if (fleeDistance >= holdDistance)
        { throw new ArgumentOutOfRangeException(nameof(fleeDistance)); }
        FleeSeconds = Positive(fleeSeconds, nameof(fleeSeconds));
        FleeSpeedMultiplier = Positive(fleeSpeedMultiplier, nameof(fleeSpeedMultiplier));
    }

    public float AuraRadius { get; }
    public float PulseInterval { get; }
    public float CastSeconds { get; }
    public float HealFraction { get; }
    public float HasteFraction { get; }
    public float HasteSeconds { get; }
    public float HoldDistance { get; }
    public float BehindDistance { get; }
    public float FleeDistance { get; }
    public float FleeSeconds { get; }
    public float FleeSpeedMultiplier { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        { throw new ArgumentOutOfRangeException(name); }
        return value;
    }
}
