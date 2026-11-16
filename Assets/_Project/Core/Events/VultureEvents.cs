using System.Numerics;

namespace Soulvail.Core.Events;

public enum VulturePhase
{
    Climb,
    Circle,
    Lock,
    Dive,
    Grounded,
}

public readonly struct VulturePhaseChanged
{
    public readonly int Id;
    public readonly VulturePhase Phase;
    public readonly float Seconds;

    public VulturePhaseChanged(int id, VulturePhase phase, float seconds)
    {
        Id = id;
        Phase = phase;
        Seconds = seconds;
    }
}

public readonly struct VultureDiveLocked
{
    public readonly int Id;
    public readonly int LaneId;
    public readonly Vector3 Origin;
    public readonly Vector2 Direction;
    public readonly Vector3 Landing;
    public readonly float Seconds;

    public VultureDiveLocked(int id, int laneId, Vector3 origin, Vector2 direction, Vector3 landing, float seconds)
    {
        Id = id;
        LaneId = laneId;
        Origin = origin;
        Direction = direction;
        Landing = landing;
        Seconds = seconds;
    }
}
