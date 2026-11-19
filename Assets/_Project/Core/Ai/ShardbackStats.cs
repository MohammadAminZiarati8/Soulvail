using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class ShardbackStats
{
    public Stat MoveSpeed { get; } = new(2f);
    public Stat TurnDegreesPerSecond { get; } = new(360f);
    public Stat CastRange { get; } = new(10f);
    public Stat FanLength { get; } = new(12f);
    public Stat FanHalfWidth { get; } = new(0.6f);
    public Stat FanAngleDegrees { get; } = new(25f);
    public Stat FanWindupSeconds { get; } = new(1f);
    public Stat FanDamage { get; } = new(14f);
    public Stat FanRecoverySeconds { get; } = new(1.25f);
    public Stat ChargeLength { get; } = new(12f);
    public Stat ChargeHalfWidth { get; } = new(1f);
    public Stat ChargeFrontReach { get; } = new(1.45f);
    public Stat ChargeWindupSeconds { get; } = new(1.2f);
    public Stat ChargeSpeed { get; } = new(10f);
    public Stat ChargeDamage { get; } = new(18f);
    public Stat ChargeRecoverySeconds { get; } = new(1.5f);
    public Stat PillarRecoverySeconds { get; } = new(3f);
    public Stat ChargeStallSeconds { get; } = new(0.25f);
    public Stat BurstRadius { get; } = new(5.5f);
    public Stat BurstWindupSeconds { get; } = new(1.2f);
    public Stat BurstDamage { get; } = new(16f);
    public Stat BurstRecoverySeconds { get; } = new(2f);
}
