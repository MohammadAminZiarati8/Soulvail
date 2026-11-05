using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>Whirlwind has begun, with its first origin and the geometry of its rings.</summary>
public readonly struct WhirlwindCast
{
    public WhirlwindCast(Vector3 origin, int arrows, int rings, float gap)
    {
        Origin = origin;
        Arrows = arrows;
        Rings = rings;
        Gap = gap;
    }

    public Vector3 Origin { get; }
    public int Arrows { get; }
    public int Rings { get; }
    public float Gap { get; }
}

/// <summary>A ring of Whirlwind arrows launched from the player's position.</summary>
public readonly struct WhirlwindRingFired
{
    public WhirlwindRingFired(Vector3 origin) => Origin = origin;

    public Vector3 Origin { get; }
}
