using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

/// <summary>Per-boss tuning for the locked attacks (SS-15a).</summary>
public sealed class DuneclawStats
{
    public Stat MoveSpeed { get; } = new(1.8f);
    public Stat SweepReach { get; } = new(3.2f);
    public Stat SweepArcDegrees { get; } = new(100f);
    public Stat SweepWindupSeconds { get; } = new(1f);
    public Stat SweepRecoverySeconds { get; } = new(0.8f);
    public Stat SweepDamage { get; } = new(12f);
    public Stat SlamRange { get; } = new(4.5f);
    public Stat SlamRadius { get; } = new(1.5f);
    public Stat SlamWindupSeconds { get; } = new(1f);
    public Stat SlamRecoverySeconds { get; } = new(2f);
    public Stat SlamDamage { get; } = new(16f);
    public Stat SlabOffset { get; } = new(3.5f);
    public Stat SlabPlacementTolerance { get; } = new(1.5f);
    public Stat SlabMinimumSpacing { get; } = new(3f);
    public Stat SlabAimRadius { get; } = new(0.8f);
    public Stat SlabRecoverySeconds { get; } = new(3f);
    public Stat ComboGapSeconds { get; } = new(0.35f);
}
