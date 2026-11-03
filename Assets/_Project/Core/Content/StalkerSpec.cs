using System;

namespace Soulvail.Core.Content;

/// <summary>Where a Chameleon stalker lurks, when it hides, and the lane it strikes down (JG-22a).</summary>
public sealed class StalkerSpec
{
    public StalkerSpec(float behindDistance, float cloakDelay, float strikeRange,
        float behindAngleDegrees, float strikeLength, float strikeSpeed, float strikeHalfWidth,
        float revealSeconds, float strikeCooldown)
    {
        BehindDistance = Positive(behindDistance, nameof(behindDistance));
        CloakDelay = Positive(cloakDelay, nameof(cloakDelay));
        StrikeRange = Positive(strikeRange, nameof(strikeRange));
        BehindAngleDegrees = Positive(behindAngleDegrees, nameof(behindAngleDegrees));
        if (behindAngleDegrees > 180f)
        { throw new ArgumentOutOfRangeException(nameof(behindAngleDegrees)); }
        StrikeLength = Positive(strikeLength, nameof(strikeLength));
        StrikeSpeed = Positive(strikeSpeed, nameof(strikeSpeed));
        StrikeHalfWidth = Positive(strikeHalfWidth, nameof(strikeHalfWidth));
        RevealSeconds = Positive(revealSeconds, nameof(revealSeconds));
        StrikeCooldown = Positive(strikeCooldown, nameof(strikeCooldown));
    }

    /// <summary>Metres behind the player's facing it lurks at.</summary>
    public float BehindDistance { get; }

    /// <summary>Seconds standing still before it hides.</summary>
    public float CloakDelay { get; }

    /// <summary>Metres from the player it strikes from.</summary>
    public float StrikeRange { get; }

    /// <summary>Least angle between the player's facing and the stalker's bearing, to strike.</summary>
    public float BehindAngleDegrees { get; }

    public float StrikeLength { get; }
    public float StrikeSpeed { get; }
    public float StrikeHalfWidth { get; }

    /// <summary>Seconds any damage keeps it visible.</summary>
    public float RevealSeconds { get; }

    /// <summary>Seconds from a strike's end before the next may wind up.</summary>
    public float StrikeCooldown { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        { throw new ArgumentOutOfRangeException(name); }
        return value;
    }
}
