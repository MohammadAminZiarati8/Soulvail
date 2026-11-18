using System.Numerics;

namespace Soulvail.Core.Events;

public enum ShardbackMove { Fan, Burst }

public readonly struct ShardbackArmed
{
    public readonly int BossId;
    public readonly ShardbackMove Move;
    public readonly Vector3 Origin;
    public readonly Vector2 Facing;
    public readonly float Reach;
    public readonly float HalfWidth;
    public readonly float AngleDegrees;
    public readonly float Seconds;

    public ShardbackArmed(int bossId, ShardbackMove move, Vector3 origin, Vector2 facing,
        float reach, float halfWidth, float angleDegrees, float seconds)
    {
        BossId = bossId;
        Move = move;
        Origin = origin;
        Facing = facing;
        Reach = reach;
        HalfWidth = halfWidth;
        AngleDegrees = angleDegrees;
        Seconds = seconds;
    }
}

public readonly struct ShardbackImpact
{
    public readonly int BossId;
    public readonly ShardbackMove Move;
    public readonly Vector3 Origin;
    public readonly float RecoverySeconds;

    public ShardbackImpact(int bossId, ShardbackMove move, Vector3 origin, float recoverySeconds)
    {
        BossId = bossId;
        Move = move;
        Origin = origin;
        RecoverySeconds = recoverySeconds;
    }
}
