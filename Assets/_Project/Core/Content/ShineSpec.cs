using System;

namespace Soulvail.Core.Content;

/// <summary>Timing and damage for a chaser's shell defence and its reflected bolts.</summary>
public sealed class ShineSpec
{
    public ShineSpec(float glintSeconds, float shineSeconds, float openSeconds, float cooldown, float triggerRange,
        float openDamageTaken, float boltDamage, float boltSpeed, float boltRadius, float boltMinFlight, float boltInterval)
    {
        GlintSeconds = Positive(glintSeconds, nameof(glintSeconds));
        ShineSeconds = Positive(shineSeconds, nameof(shineSeconds));
        OpenSeconds = Positive(openSeconds, nameof(openSeconds));
        Cooldown = Positive(cooldown, nameof(cooldown));
        TriggerRange = Positive(triggerRange, nameof(triggerRange));
        OpenDamageTaken = Positive(openDamageTaken, nameof(openDamageTaken));
        BoltDamage = Positive(boltDamage, nameof(boltDamage));
        BoltSpeed = Positive(boltSpeed, nameof(boltSpeed));
        BoltRadius = Positive(boltRadius, nameof(boltRadius));
        BoltMinFlight = Positive(boltMinFlight, nameof(boltMinFlight));
        BoltInterval = Positive(boltInterval, nameof(boltInterval));

        if (OpenDamageTaken < 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(openDamageTaken), openDamageTaken, "Open damage taken must be at least one.");
        }
    }

    public float GlintSeconds { get; }
    public float ShineSeconds { get; }
    public float OpenSeconds { get; }
    public float Cooldown { get; }
    public float TriggerRange { get; }
    public float OpenDamageTaken { get; }
    public float BoltDamage { get; }
    public float BoltSpeed { get; }
    public float BoltRadius { get; }
    public float BoltMinFlight { get; }
    public float BoltInterval { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
