using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

public sealed class BuriedKingLeapStats
{
    private readonly Stat _length = new(8f);
    private readonly Stat _width = new(4f);

    public float Length => _length.Value;
    public float Width => _width.Value;
}
