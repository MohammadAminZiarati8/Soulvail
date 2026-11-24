using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class ColossusStats
{
    public Stat PlateDurability { get; } = new(120f);
    public Stat PlateDamageReduction { get; } = new(0.22f);
    public Stat PlateFlankDegrees { get; } = new(35f);
    public Stat MoveSpeed { get; } = new(2.2f);
    public Stat SweepRadius { get; } = new(9.5f);
    public Stat SectorDegrees { get; } = new(20f);
    public Stat SweepTellSeconds { get; } = new(1.35f);
    public Stat SectorIntervalSeconds { get; } = new(0.35f);
    public Stat FastSectorIntervalSeconds { get; } = new(0.22f);
    public Stat SweepDamage { get; } = new(20f);
    public Stat SweepRecoverySeconds { get; } = new(1.8f);
    public Stat ReturnGapSeconds { get; } = new(0.30f);
    public Stat ReturnTellSeconds { get; } = new(1.35f);
    public Stat ReturnRecoverySeconds { get; } = new(2.4f);
    public Stat StompTriggerRadius { get; } = new(2.8f);
    public Stat StompRadius { get; } = new(3.3f);
    public Stat StompTellSeconds { get; } = new(1.0f);
    public Stat StompDamage { get; } = new(16f);
    public Stat StompRecoverySeconds { get; } = new(1.25f);
    public Stat RayMinimumRange { get; } = new(6f);
    public Stat RayLength { get; } = new(14f);
    public Stat RayStart { get; } = new(0.4f);
    public Stat RayHalfWidth { get; } = new(0.65f);
    public Stat RayTellSeconds { get; } = new(1.2f);
    public Stat RayDamage { get; } = new(24f);
    public Stat RayRecoverySeconds { get; } = new(0.8f);
    public Stat RayCooldownSeconds { get; } = new(10f);
}
