using System;
using System.Numerics;

namespace Soulvail.Core.Events;

public enum ColossusMove { Sweep, Stomp, ReverseSweep, SundialRay }

public readonly struct ColossusArmed
{
    public readonly int BossId;
    public readonly ColossusMove Move;
    public readonly Vector3 Origin;
    public readonly Vector2 Facing;
    public readonly float Radius;
    public readonly float SectorDegrees;
    public readonly float Seconds;
    public readonly float LaneStart;
    public readonly float LaneHalfWidth;

    public ColossusArmed(int bossId, ColossusMove move, Vector3 origin, Vector2 facing,
        float radius, float sectorDegrees, float seconds, float laneStart = 0f, float laneHalfWidth = 0f)
    {
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!float.IsFinite(facing.X) || !float.IsFinite(facing.Y))
            throw new ArgumentOutOfRangeException(nameof(facing));
        if (!float.IsFinite(radius))
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(sectorDegrees))
            throw new ArgumentOutOfRangeException(nameof(sectorDegrees));
        if (!float.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!float.IsFinite(laneStart))
            throw new ArgumentOutOfRangeException(nameof(laneStart));
        if (!float.IsFinite(laneHalfWidth))
            throw new ArgumentOutOfRangeException(nameof(laneHalfWidth));

        BossId = bossId;
        Move = move;
        Origin = origin;
        Facing = facing;
        Radius = radius;
        SectorDegrees = sectorDegrees;
        Seconds = seconds;
        LaneStart = laneStart;
        LaneHalfWidth = laneHalfWidth;
    }
}

public enum ColossusShoulder { Left, Right }

public readonly struct ColossusPlateHit
{
    public readonly int BossId;
    public readonly ColossusShoulder Shoulder;
    public readonly float RemainingFraction;

    public ColossusPlateHit(int bossId, ColossusShoulder shoulder, float remainingFraction)
    {
        BossId = bossId;
        Shoulder = shoulder;
        RemainingFraction = remainingFraction;
    }
}

public readonly struct ColossusTellProgress
{
    public readonly int BossId;
    public readonly float Fraction;

    public ColossusTellProgress(int bossId, float fraction)
    {
        if (!float.IsFinite(fraction))
            throw new ArgumentOutOfRangeException(nameof(fraction));

        BossId = bossId;
        Fraction = fraction;
    }
}

public readonly struct ColossusImpact
{
    public readonly int BossId;
    public readonly ColossusMove Move;
    public readonly int Sector;
    public readonly float RecoverySeconds;

    public ColossusImpact(int bossId, ColossusMove move, int sector, float recoverySeconds)
    {
        if (!float.IsFinite(recoverySeconds))
            throw new ArgumentOutOfRangeException(nameof(recoverySeconds));

        BossId = bossId;
        Move = move;
        Sector = sector;
        RecoverySeconds = recoverySeconds;
    }
}
