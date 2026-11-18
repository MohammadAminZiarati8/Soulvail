using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class ShardbackStats
{
    public Stat MoveSpeed { get; } = new(2f);
    public Stat CastRange { get; } = new(10f);
    public Stat FanLength { get; } = new(12f);
    public Stat FanHalfWidth { get; } = new(0.6f);
    public Stat FanAngleDegrees { get; } = new(25f);
    public Stat FanWindupSeconds { get; } = new(1f);
    public Stat FanDamage { get; } = new(14f);
    public Stat FanRecoverySeconds { get; } = new(1.25f);
    public Stat BurstRadius { get; } = new(4f);
    public Stat BurstWindupSeconds { get; } = new(1.2f);
    public Stat BurstDamage { get; } = new(16f);
    public Stat BurstRecoverySeconds { get; } = new(2f);
}
