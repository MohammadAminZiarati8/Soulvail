using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Content;

/// <summary>Where a Thornweaver holds and the thorn walls it plants beyond the player (JG-24a).</summary>
public sealed class ThornweaverSpec
{
    public ThornweaverSpec(float holdDistance, float castRange, float castInterval,
        float castSeconds, float wallLength, float wallHalfWidth, float armSeconds,
        float standSeconds, float beyondDistance, int maxWalls)
    {
        HoldDistance = Positive(holdDistance, nameof(holdDistance));
        CastRange = Positive(castRange, nameof(castRange));
        CastInterval = Positive(castInterval, nameof(castInterval));
        CastSeconds = Positive(castSeconds, nameof(castSeconds));
        WallLength = Positive(wallLength, nameof(wallLength));
        WallHalfWidth = Positive(wallHalfWidth, nameof(wallHalfWidth));
        if (!(armSeconds >= ThornSystem.MinArmSeconds) || float.IsInfinity(armSeconds))
        { throw new ArgumentOutOfRangeException(nameof(armSeconds), armSeconds, $"At least {ThornSystem.MinArmSeconds} s (GD §9.1 rule 1)."); }
        ArmSeconds = armSeconds;
        StandSeconds = Positive(standSeconds, nameof(standSeconds));
        BeyondDistance = Positive(beyondDistance, nameof(beyondDistance));
        if (maxWalls < 1 || maxWalls > ThornSystem.Capacity)
        { throw new ArgumentOutOfRangeException(nameof(maxWalls), maxWalls, $"Between 1 and {ThornSystem.Capacity}."); }
        MaxWalls = maxWalls;
    }

    /// <summary>Metres from the player it holds at; it backs off inside three less (rule 1).</summary>
    public float HoldDistance { get; }

    /// <summary>Metres from the player within which it casts (rule 2).</summary>
    public float CastRange { get; }

    /// <summary>Seconds from one cast's start to the next's (rules 2, 4).</summary>
    public float CastInterval { get; }

    /// <summary>Seconds it stands telegraphing before the wall is placed (rule 2).</summary>
    public float CastSeconds { get; }

    public float WallLength { get; }
    public float WallHalfWidth { get; }

    /// <summary>The wall's warning before it rises; never under <see cref="ThornSystem.MinArmSeconds"/>.</summary>
    public float ArmSeconds { get; }

    /// <summary>Seconds the wall stands after rising.</summary>
    public float StandSeconds { get; }

    /// <summary>Metres past the player, away from the weaver, the wall is centred at (rule 3).</summary>
    public float BeyondDistance { get; }

    /// <summary>How many of its own walls may be arming or standing at once (rule 2).</summary>
    public int MaxWalls { get; }

    private static float Positive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        { throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number greater than zero."); }
        return value;
    }
}
