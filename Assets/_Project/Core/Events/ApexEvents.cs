using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>The Apex planted for a pounce, or a ringless feint before a dash.</summary>
public readonly struct ApexCrouched
{
    public int Id { get; }

    public bool IsFeint { get; }

    public float Seconds { get; }

    public ApexCrouched(int id, bool isFeint, float seconds)
    {
        Id = id;
        IsFeint = isFeint;
        Seconds = seconds;
    }
}

/// <summary>A pounce landed at its fixed ring and opened the stationary punish window.</summary>
public readonly struct ApexLanded
{
    public int Id { get; }

    public Vector3 At { get; }

    public float WindowSeconds { get; }

    public ApexLanded(int id, Vector3 at, float windowSeconds)
    {
        Id = id;
        At = at;
        WindowSeconds = windowSeconds;
    }
}
