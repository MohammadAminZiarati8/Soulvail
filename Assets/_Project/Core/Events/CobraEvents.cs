using System.Numerics;

namespace Soulvail.Core.Events;

public enum CobraPhase
{
    Hold,
    Close,
    Rear,
    Recover,
}

public readonly struct CobraPhaseChanged
{
    public readonly int Id;
    public readonly CobraPhase Phase;
    public readonly float Seconds;

    public CobraPhaseChanged(int id, CobraPhase phase, float seconds)
    {
        Id = id;
        Phase = phase;
        Seconds = seconds;
    }
}

public readonly struct CobraConeArmed
{
    public readonly int Id;
    public readonly Vector3 Apex;
    public readonly Vector2 Direction;
    public readonly float HalfAngleDegrees;
    public readonly float Length;
    public readonly float Seconds;

    public CobraConeArmed(int id, Vector3 apex, Vector2 direction, float halfAngleDegrees, float length,
        float seconds)
    {
        Id = id;
        Apex = apex;
        Direction = direction;
        HalfAngleDegrees = halfAngleDegrees;
        Length = length;
        Seconds = seconds;
    }
}

public readonly struct CobraSpat
{
    public readonly int Id;
    public readonly bool Hit;

    public CobraSpat(int id, bool hit)
    {
        Id = id;
        Hit = hit;
    }
}
