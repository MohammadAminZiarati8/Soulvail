using System;
using System.Numerics;

namespace Soulvail.Core.Events;

public enum HierophantMove { StaffSweep, SunSeal }

public readonly struct HierophantSealsPlaced
{
    public readonly int BossId;
    public readonly Vector2 West;
    public readonly Vector2 Centre;
    public readonly Vector2 East;

    public HierophantSealsPlaced(int bossId, Vector2 west, Vector2 centre, Vector2 east)
    {
        if (!float.IsFinite(west.X) || !float.IsFinite(west.Y))
            throw new ArgumentOutOfRangeException(nameof(west));
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y))
            throw new ArgumentOutOfRangeException(nameof(centre));
        if (!float.IsFinite(east.X) || !float.IsFinite(east.Y))
            throw new ArgumentOutOfRangeException(nameof(east));

        BossId = bossId;
        West = west;
        Centre = centre;
        East = east;
    }
}

public readonly struct HierophantArmed
{
    public readonly int BossId;
    public readonly HierophantMove Move;
    public readonly int SealSlot;
    public readonly Vector3 Origin;
    public readonly Vector2 Facing;
    public readonly float Reach;
    public readonly float HalfWidth;
    public readonly float HalfAngleDegrees;
    public readonly float Seconds;

    public HierophantArmed(int bossId, HierophantMove move, int sealSlot, Vector3 origin,
        Vector2 facing, float reach, float halfWidth, float halfAngleDegrees, float seconds)
    {
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!float.IsFinite(facing.X) || !float.IsFinite(facing.Y))
            throw new ArgumentOutOfRangeException(nameof(facing));
        if (!(reach > 0f) || !float.IsFinite(reach))
            throw new ArgumentOutOfRangeException(nameof(reach));
        if (!(halfWidth >= 0f) || !float.IsFinite(halfWidth))
            throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!(halfAngleDegrees >= 0f && halfAngleDegrees <= 180f))
            throw new ArgumentOutOfRangeException(nameof(halfAngleDegrees));
        if (!(seconds > 0f) || !float.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));

        BossId = bossId;
        Move = move;
        SealSlot = sealSlot;
        Origin = origin;
        Facing = facing;
        Reach = reach;
        HalfWidth = halfWidth;
        HalfAngleDegrees = halfAngleDegrees;
        Seconds = seconds;
    }
}

public readonly struct HierophantTellProgress
{
    public readonly int BossId;
    public readonly float Fraction;

    public HierophantTellProgress(int bossId, float fraction)
    {
        if (!(fraction >= 0f && fraction <= 1f))
            throw new ArgumentOutOfRangeException(nameof(fraction));

        BossId = bossId;
        Fraction = fraction;
    }
}

public readonly struct HierophantImpact
{
    public readonly int BossId;
    public readonly HierophantMove Move;
    public readonly int SealSlot;
    public readonly float RecoverySeconds;

    public HierophantImpact(int bossId, HierophantMove move, int sealSlot, float recoverySeconds)
    {
        if (!(recoverySeconds >= 0f) || !float.IsFinite(recoverySeconds))
            throw new ArgumentOutOfRangeException(nameof(recoverySeconds));

        BossId = bossId;
        Move = move;
        SealSlot = sealSlot;
        RecoverySeconds = recoverySeconds;
    }
}

public readonly struct HierophantWard
{
    public readonly int BossId;
    public readonly int PriestId;
    public readonly bool Active;
    public readonly Vector3 PriestPosition;

    public HierophantWard(int bossId, int priestId, bool active, Vector3 priestPosition)
    {
        BossId = bossId;
        PriestId = priestId;
        Active = active;
        PriestPosition = priestPosition;
    }
}
