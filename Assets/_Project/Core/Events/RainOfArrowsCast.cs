using System.Numerics;

namespace Soulvail.Core.Events;

/// <summary>A rain was placed, with its final geometry, after the zone exists.</summary>
public readonly struct RainOfArrowsCast
{
    public RainOfArrowsCast(Vector3 centre, float radius, float duration)
    {
        Centre = centre;
        Radius = radius;
        Duration = duration;
    }

    public Vector3 Centre { get; }
    public float Radius { get; }
    public float Duration { get; }
}
