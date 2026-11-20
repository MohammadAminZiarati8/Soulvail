using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class HierophantStats
{
    public Stat MoveSpeed { get; } = new(2.25f);
    public Stat TurnDegreesPerSecond { get; } = new(360f);
    public Stat FireReach { get; } = new(7f);
    // Maximum sideways extent of the original 4 m, 60-degree sweep.
    public Stat FireHalfWidth { get; } = new(3.4641016f);
    public Stat FireTellSeconds { get; } = new(1f);
    public Stat FireTravelSeconds { get; } = new(0.8f);
    public Stat FireDepth { get; } = new(0.9f);
    public Stat FireDamage { get; } = new(16f);
    public Stat FireRecoverySeconds { get; } = new(1.25f);
    public Stat FireChaseSeconds { get; } = new(2f);
    public Stat SealHalfWidth { get; } = new(2f);
    public Stat DunesHalfExtent { get; } = new(18f);
    public Stat SealTellSeconds { get; } = new(1.3f);
    public Stat SealDamage { get; } = new(20f);
    public Stat SealRecoverySeconds { get; } = new(1.5f);
    public Stat SealPairGapSeconds { get; } = new(0.3f);
    public Stat AddReplacementSeconds { get; } = new(18f);
    public Stat AddBonus { get; } = new(0.25f);
    public Stat WardDamageScale { get; } = new(0.5f);
    public Stat WardBreakSeconds { get; } = new(5f);
    public Stat HealChannelSeconds { get; } = new(1.5f);
    public Stat HealFraction { get; } = new(0.05f);
}
