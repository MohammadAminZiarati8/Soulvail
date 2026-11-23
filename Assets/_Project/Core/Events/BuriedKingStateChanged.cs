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
    LungeTell,
    LungeTravel,
    LungeRecovery,
    LungeApproach,
    SweepImpact,
    SlamImpact,
    SpinTell,
    SpinActive,
    SpinPulse,
    SpinRecovery,
}

/// <summary>Floor geometry and sword position for the current pose. Spin events report the observed center in Start.</summary>
public readonly struct BuriedKingStateChanged
{
    internal BuriedKingStateChanged(int enemyId, BuriedKingPose pose, Vector3 start, Vector3 end,
        float width, float sweepDegrees, Vector3 weaponPosition, float slamRadius, float fractureProgress,
        float warningSeconds, float landingRadius)
        : this(enemyId, pose, start, end, width, sweepDegrees, weaponPosition, slamRadius, fractureProgress,
            warningSeconds, landingRadius, 0f)
    {
    }

    internal BuriedKingStateChanged(int enemyId, BuriedKingPose pose, Vector3 start, Vector3 end,
        float width, float sweepDegrees, Vector3 weaponPosition, float slamRadius, float fractureProgress,
        float warningSeconds, float landingRadius, float cutSeconds)
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
        WarningSeconds = warningSeconds;
        LandingRadius = landingRadius;
        CutSeconds = cutSeconds;
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
    public float WarningSeconds { get; }
    public float LandingRadius { get; }
    public float CutSeconds { get; }
}
