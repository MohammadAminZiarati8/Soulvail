using System;

namespace Soulvail.Core.Content;

/// <summary>The rear turn and braced shot of a bombardier Spitter (GD §8.6).</summary>
public sealed class BombardierSpec
{
    public BombardierSpec(float turnRateDegrees, float startConeDegrees, float dropConeDegrees)
    {
        if (!(turnRateDegrees > 0f) || float.IsInfinity(turnRateDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(turnRateDegrees), turnRateDegrees, "The turn rate must be finite and positive.");
        }

        if (!(startConeDegrees > 0f && startConeDegrees < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(startConeDegrees), startConeDegrees, "The start cone must be in (0, 180).");
        }

        if (!(dropConeDegrees >= startConeDegrees && dropConeDegrees < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(dropConeDegrees), dropConeDegrees, "The drop cone must be at least the start cone and less than 180.");
        }

        TurnRateDegrees = turnRateDegrees;
        StartConeDegrees = startConeDegrees;
        DropConeDegrees = dropConeDegrees;
    }

    public float TurnRateDegrees { get; }
    public float StartConeDegrees { get; }
    public float DropConeDegrees { get; }
}
