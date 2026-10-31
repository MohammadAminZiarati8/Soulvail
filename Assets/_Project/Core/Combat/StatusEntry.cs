using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>One status on the player, as the HUD reads it.</summary>
public readonly struct StatusEntry
{
    public readonly ContentId Id;
    public readonly bool IsBuff;
    public readonly float Remaining;
    public readonly float Duration;
    public readonly int Stacks;

    public StatusEntry(ContentId id, bool isBuff, float remaining, float duration, int stacks)
    {
        Id = id;
        IsBuff = isBuff;
        Remaining = remaining;
        Duration = duration;
        Stacks = stacks;
    }

    /// <summary>False for an untimed status and for <c>default</c>.</summary>
    public bool IsTimed => Duration > 0f && !float.IsPositiveInfinity(Duration);

    /// <summary><see cref="Remaining"/> over <see cref="Duration"/>, 0..1; 1 for an untimed status.</summary>
    public float Fraction => !IsTimed ? 1f : Math.Clamp(Remaining / Duration, 0f, 1f);
}
