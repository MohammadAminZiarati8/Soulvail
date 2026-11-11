using System;

namespace Soulvail.Core.Content;

/// <summary>Movement, timing and damage for an antlion's burrow cycle.</summary>
public sealed class BurrowSpec
{
    public BurrowSpec(float tunnelSpeed, float behindDistance, float lockDistance, float minTunnelSeconds,
        float maxTunnelSeconds, float ringRadius, float armSeconds, float eruptionDamage,
        float surfacedSeconds, float digSeconds)
    {
        TunnelSpeed = Positive(tunnelSpeed, nameof(tunnelSpeed));
        BehindDistance = Positive(behindDistance, nameof(behindDistance));
        LockDistance = Positive(lockDistance, nameof(lockDistance));
        MinTunnelSeconds = Positive(minTunnelSeconds, nameof(minTunnelSeconds));
        MaxTunnelSeconds = Positive(maxTunnelSeconds, nameof(maxTunnelSeconds));
        RingRadius = Positive(ringRadius, nameof(ringRadius));
        ArmSeconds = Positive(armSeconds, nameof(armSeconds));
        EruptionDamage = Positive(eruptionDamage, nameof(eruptionDamage));
        SurfacedSeconds = Positive(surfacedSeconds, nameof(surfacedSeconds));
        DigSeconds = Positive(digSeconds, nameof(digSeconds));

        if (!(MinTunnelSeconds < MaxTunnelSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(maxTunnelSeconds), maxTunnelSeconds,
                "The maximum tunnel duration must exceed the minimum.");
        }
    }

    public float TunnelSpeed { get; }
    public float BehindDistance { get; }
    public float LockDistance { get; }
    public float MinTunnelSeconds { get; }
    public float MaxTunnelSeconds { get; }
    public float RingRadius { get; }
    public float ArmSeconds { get; }
    public float EruptionDamage { get; }
    public float SurfacedSeconds { get; }
    public float DigSeconds { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
