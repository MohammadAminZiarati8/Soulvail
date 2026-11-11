using System.Numerics;

namespace Soulvail.Core.Events;

public enum AntlionPhase
{
    Dig,
    Tunnel,
    Arm,
    Surfaced,
}

public readonly struct AntlionPhaseChanged
{
    public readonly int Id;
    public readonly AntlionPhase Phase;
    public readonly float Seconds;

    public AntlionPhaseChanged(int id, AntlionPhase phase, float seconds)
    {
        Id = id;
        Phase = phase;
        Seconds = seconds;
    }
}

public readonly struct AntlionRingArmed
{
    public readonly int Id;
    public readonly Vector3 Centre;
    public readonly float Radius;
    public readonly float Seconds;

    public AntlionRingArmed(int id, Vector3 centre, float radius, float seconds)
    {
        Id = id;
        Centre = centre;
        Radius = radius;
        Seconds = seconds;
    }
}
