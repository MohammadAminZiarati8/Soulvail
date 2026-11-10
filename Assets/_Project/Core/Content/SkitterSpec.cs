using System;

namespace Soulvail.Core.Content;

/// <summary>The fan approach and locked tail strike of a skittering Chaser (GD §8.4).</summary>
public sealed class SkitterSpec
{
    public const int SlotCount = 4;

    public SkitterSpec(float ringRadius, float fanDegrees, float spreadTimeout, float zoneLength, float zoneHalfWidth)
    {
        RingRadius = Positive(ringRadius, nameof(ringRadius));
        FanDegrees = ValidFan(fanDegrees);
        SpreadTimeout = Positive(spreadTimeout, nameof(spreadTimeout));
        ZoneLength = Positive(zoneLength, nameof(zoneLength));
        ZoneHalfWidth = Positive(zoneHalfWidth, nameof(zoneHalfWidth));
    }

    public float RingRadius { get; }

    public float FanDegrees { get; }

    public float SpreadTimeout { get; }

    public float ZoneLength { get; }

    public float ZoneHalfWidth { get; }

    /// <summary>Maps the agent id to one of four evenly spaced offsets across the fan.</summary>
    public static float SlotOffsetDegrees(int agentId, float fanDegrees)
    {
        if (agentId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(agentId), agentId, "The agent id must be non-negative.");
        }

        int slot = agentId % SlotCount;
        return ((2 * slot) - (SlotCount - 1)) * (ValidFan(fanDegrees) / (2 * (SlotCount - 1)));
    }

    private static float ValidFan(float fanDegrees)
    {
        if (!(fanDegrees > 0f) || fanDegrees > 180f)
        {
            throw new ArgumentOutOfRangeException(nameof(fanDegrees), fanDegrees, "The fan must be in (0, 180] degrees.");
        }

        return fanDegrees;
    }

    private static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite and greater than zero.");
        }

        return value;
    }
}
