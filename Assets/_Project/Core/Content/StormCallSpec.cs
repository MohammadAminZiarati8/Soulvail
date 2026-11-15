using System;

namespace Soulvail.Core.Content;

/// <summary>Movement, channel timing and the hazard raised by a storm caller.</summary>
public sealed class StormCallSpec
{
    public StormCallSpec(float holdDistance, float fleeDistance, float fleeSeconds, float fleeSpeedMultiplier,
        float channelReach, float channelSeconds, float brokenSeconds, float rechannelSeconds, float riseAhead,
        float driftSpeed, float turnDegreesPerSecond, float tornadoSeconds, float radius,
        float damagePerSecond, float pullRadius, float pullSpeed, float collapseSeconds)
    {
        HoldDistance = Positive(holdDistance, nameof(holdDistance));
        FleeDistance = Positive(fleeDistance, nameof(fleeDistance));
        FleeSeconds = Positive(fleeSeconds, nameof(fleeSeconds));
        FleeSpeedMultiplier = Positive(fleeSpeedMultiplier, nameof(fleeSpeedMultiplier));
        ChannelReach = Positive(channelReach, nameof(channelReach));
        ChannelSeconds = Positive(channelSeconds, nameof(channelSeconds));
        BrokenSeconds = Positive(brokenSeconds, nameof(brokenSeconds));
        RechannelSeconds = Positive(rechannelSeconds, nameof(rechannelSeconds));
        RiseAhead = Positive(riseAhead, nameof(riseAhead));
        DriftSpeed = Positive(driftSpeed, nameof(driftSpeed));
        TurnDegreesPerSecond = Positive(turnDegreesPerSecond, nameof(turnDegreesPerSecond));
        TornadoSeconds = Positive(tornadoSeconds, nameof(tornadoSeconds));
        Radius = Positive(radius, nameof(radius));
        DamagePerSecond = Positive(damagePerSecond, nameof(damagePerSecond));
        PullRadius = Positive(pullRadius, nameof(pullRadius));
        PullSpeed = Positive(pullSpeed, nameof(pullSpeed));
        CollapseSeconds = Positive(collapseSeconds, nameof(collapseSeconds));

        if (!(FleeDistance < HoldDistance && HoldDistance < ChannelReach))
        {
            throw new ArgumentException("Flee distance must be below hold distance, and hold distance below channel reach.");
        }

        if (!(PullRadius > Radius))
        {
            throw new ArgumentOutOfRangeException(nameof(pullRadius), "Pull radius must exceed the pulse radius.");
        }
    }

    public float HoldDistance { get; }
    public float FleeDistance { get; }
    public float FleeSeconds { get; }
    public float FleeSpeedMultiplier { get; }
    public float ChannelReach { get; }
    public float ChannelSeconds { get; }
    public float BrokenSeconds { get; }
    public float RechannelSeconds { get; }
    public float RiseAhead { get; }
    public float DriftSpeed { get; }
    public float TurnDegreesPerSecond { get; }
    public float TornadoSeconds { get; }
    public float Radius { get; }
    public float DamagePerSecond { get; }
    public float PullRadius { get; }
    public float PullSpeed { get; }
    public float CollapseSeconds { get; }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
