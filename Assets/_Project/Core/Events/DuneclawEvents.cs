using System.Numerics;

namespace Soulvail.Core.Events;

public enum DuneclawMove { Sweep, Slam }

public readonly struct DuneclawArmed
{
    public readonly int BossId;
    public readonly DuneclawMove Move;
    public readonly Vector3 Origin;
    public readonly Vector2 Facing;
    public readonly Vector3 Target;
    public readonly float Radius;
    public readonly float ArcDegrees;
    public readonly float Seconds;

    public DuneclawArmed(int bossId, DuneclawMove move, Vector3 origin, Vector2 facing,
        Vector3 target, float radius, float arcDegrees, float seconds)
    {
        BossId = bossId;
        Move = move;
        Origin = origin;
        Facing = facing;
        Target = target;
        Radius = radius;
        ArcDegrees = arcDegrees;
        Seconds = seconds;
    }
}

public readonly struct DuneclawImpact
{
    public readonly int BossId;
    public readonly DuneclawMove Move;
    public readonly Vector3 Target;
    public readonly float RecoverySeconds;

    public DuneclawImpact(int bossId, DuneclawMove move, Vector3 target, float recoverySeconds)
    {
        BossId = bossId;
        Move = move;
        Target = target;
        RecoverySeconds = recoverySeconds;
    }
}

public readonly struct DuneclawSlabPlaced
{
    public readonly int BossId;
    public readonly int Slot;
    public readonly Vector3 Position;

    public DuneclawSlabPlaced(int bossId, int slot, Vector3 position)
    {
        BossId = bossId;
        Slot = slot;
        Position = position;
    }
}

public readonly struct DuneclawSlabShattered
{
    public readonly int BossId;
    public readonly int Slot;

    public DuneclawSlabShattered(int bossId, int slot)
    {
        BossId = bossId;
        Slot = slot;
    }
}
