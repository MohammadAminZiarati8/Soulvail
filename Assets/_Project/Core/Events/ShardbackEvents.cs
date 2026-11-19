using System;
using System.Numerics;
using Soulvail.Core.Run;

namespace Soulvail.Core.Events;

public enum ShardbackMove { Fan, Burst, Charge }

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
    public readonly ShardbackProbeKey Key;

    public ShardbackArmed(int bossId, ShardbackMove move, Vector3 origin, Vector2 facing,
        float reach, float halfWidth, float angleDegrees, float seconds)
        : this(bossId, move, origin, facing, reach, halfWidth, angleDegrees, seconds, default) { }

    public ShardbackArmed(int bossId, ShardbackMove move, Vector3 origin, Vector2 facing,
        float reach, float halfWidth, float angleDegrees, float seconds, ShardbackProbeKey key)
    {
        BossId = bossId;
        Move = move;
        Origin = origin;
        Facing = facing;
        Reach = reach;
        HalfWidth = halfWidth;
        AngleDegrees = angleDegrees;
        Seconds = seconds;
        Key = key;
    }
}

public readonly struct ShardbackImpact
{
    public readonly int BossId;
    public readonly ShardbackMove Move;
    public readonly Vector3 Origin;
    public readonly float RecoverySeconds;
    public readonly ShardbackProbeKey Key;
    public readonly float TravelDistance;

    public ShardbackImpact(int bossId, ShardbackMove move, Vector3 origin, float recoverySeconds)
        : this(bossId, move, origin, recoverySeconds, default) { }

    public ShardbackImpact(int bossId, ShardbackMove move, Vector3 origin, float recoverySeconds,
        ShardbackProbeKey key)
        : this(bossId, move, origin, recoverySeconds, key, 0f) { }

    public ShardbackImpact(int bossId, ShardbackMove move, Vector3 origin, float recoverySeconds,
        ShardbackProbeKey key, float travelDistance)
    {
        if (!(travelDistance >= 0f) || !float.IsFinite(travelDistance))
            throw new ArgumentOutOfRangeException(nameof(travelDistance));

        BossId = bossId;
        Move = move;
        Origin = origin;
        RecoverySeconds = recoverySeconds;
        Key = key;
        TravelDistance = travelDistance;
    }
}

public readonly struct ShardbackChargeAdvanced
{
    public readonly int BossId;
    public readonly ShardbackProbeKey Key;
    public readonly float Distance;

    public ShardbackChargeAdvanced(int bossId, ShardbackProbeKey key, float distance)
    {
        if (!(distance > 0f) || !float.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(distance));

        BossId = bossId;
        Key = key;
        Distance = distance;
    }
}

public readonly struct ShardbackChargeStarted
{
    public readonly int BossId;
    public readonly ShardbackProbeKey Key;

    public ShardbackChargeStarted(int bossId, ShardbackProbeKey key)
    {
        BossId = bossId;
        Key = key;
    }
}

public readonly struct ShardbackTellProgress
{
    public readonly int BossId;
    public readonly ShardbackProbeKey Key;
    public readonly float Fraction;

    public ShardbackTellProgress(int bossId, ShardbackProbeKey key, float fraction)
    {
        if (!(fraction >= 0f && fraction <= 1f))
            throw new ArgumentOutOfRangeException(nameof(fraction));

        BossId = bossId;
        Key = key;
        Fraction = fraction;
    }
}

public readonly struct ShardbackFightStarted
{
    public readonly int BossId;
    public readonly Guid Lifetime;

    public ShardbackFightStarted(int bossId, Guid lifetime)
    {
        BossId = bossId;
        Lifetime = lifetime;
    }
}

public readonly struct ShardbackPillarBroken
{
    public readonly int BossId;
    public readonly Guid Lifetime;
    public readonly int Slot;

    public ShardbackPillarBroken(int bossId, Guid lifetime, int slot)
    {
        BossId = bossId;
        Lifetime = lifetime;
        Slot = slot;
    }
}

public readonly struct ShardbackClipped
{
    public readonly int BossId;
    public readonly ShardbackProbeKey Key;
    public readonly float Centre;
    public readonly float Plus;
    public readonly float Minus;

    public ShardbackClipped(int bossId, ShardbackProbeKey key, float centre, float plus, float minus)
    {
        BossId = bossId;
        Key = key;
        Centre = centre;
        Plus = plus;
        Minus = minus;
    }
}
