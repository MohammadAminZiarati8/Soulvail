using System.Numerics;

namespace Soulvail.Core.Events;

public enum StormCallerPhase { Hold, Flee, Channel }

public readonly struct StormCallerPhaseChanged
{
    public readonly int Id;
    public readonly StormCallerPhase Phase;
    public readonly float Seconds;

    public StormCallerPhaseChanged(int id, StormCallerPhase phase, float seconds)
    {
        Id = id;
        Phase = phase;
        Seconds = seconds;
    }
}

public readonly struct StormChannelBroken
{
    public readonly int Id;

    public StormChannelBroken(int id)
    {
        Id = id;
    }
}

public readonly struct TornadoRose
{
    public readonly int TornadoId;
    public readonly int OwnerId;
    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly float Seconds;

    public TornadoRose(int tornadoId, int ownerId, Vector3 position, float radius, float seconds)
    {
        TornadoId = tornadoId;
        OwnerId = ownerId;
        Position = position;
        Radius = radius;
        Seconds = seconds;
    }
}

public readonly struct TornadoEnded
{
    public readonly int TornadoId;
    public readonly int OwnerId;
    public readonly bool Collapsed;

    public TornadoEnded(int tornadoId, int ownerId, bool collapsed)
    {
        TornadoId = tornadoId;
        OwnerId = ownerId;
        Collapsed = collapsed;
    }
}
