using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class HierophantStats
{
    public Stat MoveSpeed { get; } = new(2.25f);
    public Stat TurnDegreesPerSecond { get; } = new(360f);
    public Stat SweepRange { get; } = new(3.25f);
    public Stat SweepHalfAngleDegrees { get; } = new(50f);
    public Stat SweepTellSeconds { get; } = new(1f);
    public Stat SweepDamage { get; } = new(16f);
    public Stat SweepRecoverySeconds { get; } = new(1.25f);
    public Stat SealHalfWidth { get; } = new(2f);
    public Stat SealHalfLength { get; } = new(9f);
    public Stat SealTellSeconds { get; } = new(1.3f);
    public Stat SealDamage { get; } = new(20f);
    public Stat SealRecoverySeconds { get; } = new(2f);
    public Stat SealPairGapSeconds { get; } = new(0.3f);
}
