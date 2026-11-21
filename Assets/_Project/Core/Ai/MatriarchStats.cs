using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class MatriarchStats
{
    internal const float TurnLimit = 270f;

    public Stat TurnDegreesPerSecond { get; } = new(TurnLimit);
    public Stat ShoveLength { get; } = new(10f);
    public Stat ShoveWidth { get; } = new(2.4f);
    public Stat ShoveTell { get; } = new(1f);
    public Stat ShoveDamage { get; } = new(18f);
    public Stat PounceRadius { get; } = new(2.5f);
    public Stat PounceTell { get; } = new(1.1f);
    public Stat PounceDamage { get; } = new(24f);
    public Stat ComboGap { get; } = new(0.5f);
    public Stat GroundRecovery { get; } = new(1.4f);
}
