using System.Numerics;

namespace Soulvail.Core.Events;

public enum MatriarchMove { RuinShove, CarrionPounce, FeatherRain, CarrionDive }

public readonly struct MatriarchFlightChanged
{
    public readonly int BossId;
    public readonly bool Airborne;

    public MatriarchFlightChanged(int bossId, bool airborne)
    {
        BossId = bossId;
        Airborne = airborne;
    }
}

/// <summary>The fixed lane from origin to target, or the disc centred on target.</summary>
public readonly struct MatriarchArmed
{
    public readonly int BossId;
    public readonly MatriarchMove Move;
    public readonly Vector3 Origin;
    public readonly Vector3 Target;
    public readonly float Width;
    public readonly float Radius;
    public readonly float Seconds;

    public MatriarchArmed(int bossId, MatriarchMove move, Vector3 origin, Vector3 target,
        float width, float radius, float seconds)
    {
        BossId = bossId;
        Move = move;
        Origin = origin;
        Target = target;
        Width = width;
        Radius = radius;
        Seconds = seconds;
    }
}

public readonly struct MatriarchTellProgress
{
    public readonly int BossId;
    public readonly float Fraction;

    public MatriarchTellProgress(int bossId, float fraction)
    {
        BossId = bossId;
        Fraction = fraction;
    }
}

public readonly struct MatriarchImpact
{
    public readonly int BossId;
    public readonly MatriarchMove Move;
    public readonly Vector3 Position;

    public MatriarchImpact(int bossId, MatriarchMove move, Vector3 position)
    {
        BossId = bossId;
        Move = move;
        Position = position;
    }
}

public readonly struct MatriarchTellCleared
{
    public readonly int BossId;

    public MatriarchTellCleared(int bossId) => BossId = bossId;
}
