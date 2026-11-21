using System.Numerics;

namespace Soulvail.Core.Events;

public enum BuriedKingPose
{
    Held,
    SweepTell,
    CastTell,
    Outbound,
    Embedded,
    RecallTell,
    Returning,
    Cleared,
    SlamTell,
    FractureTell,
}

/// <summary>Locked floor geometry and the scepter position for the current pose.</summary>
public readonly struct BuriedKingStateChanged
{
    internal BuriedKingStateChanged(int enemyId, BuriedKingPose pose, Vector3 start, Vector3 end,
        float width, float sweepDegrees, Vector3 weaponPosition, float slamRadius, float fractureProgress)
    {
        EnemyId = enemyId;
        Pose = pose;
        Start = start;
        End = end;
        Width = width;
        SweepDegrees = sweepDegrees;
        WeaponPosition = weaponPosition;
        SlamRadius = slamRadius;
        FractureProgress = fractureProgress;
    }

    public int EnemyId { get; }
    public BuriedKingPose Pose { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
    public float Width { get; }
    public float SweepDegrees { get; }
    public Vector3 WeaponPosition { get; }
    public float SlamRadius { get; }
    public float FractureProgress { get; }
}
