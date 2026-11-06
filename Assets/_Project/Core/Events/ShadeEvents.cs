using System.Numerics;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Events;

public readonly struct ShadeAppeared
{
    public ShadeAppeared(int id, ShadeKind kind, Vector3 position, float expiresAt)
    {
        Id = id;
        Kind = kind;
        Position = position;
        ExpiresAt = expiresAt;
    }

    public readonly int Id;
    public readonly ShadeKind Kind;
    public readonly Vector3 Position;
    public readonly float ExpiresAt;
}

public readonly struct ShadeLoosed
{
    public ShadeLoosed(int id, Vector3 target)
    {
        Id = id;
        Target = target;
    }

    public readonly int Id;
    public readonly Vector3 Target;
}

public readonly struct ShadeFaded
{
    public ShadeFaded(int id, Vector3 position, bool expired)
    {
        Id = id;
        Position = position;
        Expired = expired;
    }

    public readonly int Id;
    public readonly Vector3 Position;
    public readonly bool Expired;
}

public readonly struct ShadeBurst
{
    public ShadeBurst(Vector3 position, float radius, bool slows)
    {
        Position = position;
        Radius = radius;
        Slows = slows;
    }

    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly bool Slows;
}
