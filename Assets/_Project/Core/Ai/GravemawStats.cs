using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

/// <summary>
/// Every number Gravemaw's burrow cycle reads (JG-25a), each a <see cref="Stat"/> so a phase or a
/// later tuning pass can sit a modifier on it. Built once per boss by <c>RunSession</c>.
/// </summary>
public sealed class GravemawStats
{
    /// <summary>The least time a chase lasts before the ring may arm.</summary>
    public Stat ChaseSeconds { get; } = new(2.5f);

    /// <summary>The tunnel's speed as a multiple of <c>EnemyAgent.MoveSpeed</c>.</summary>
    public Stat TunnelSpeedScale { get; } = new(3f);

    /// <summary>How long the ring warns before the eruption.</summary>
    public Stat WarnSeconds { get; } = new(1f);

    /// <summary>The eruption's radius, metres on the ground plane.</summary>
    public Stat RingRadius { get; } = new(3f);

    /// <summary>The eruption's damage as a multiple of <c>ContactDamage</c>.</summary>
    public Stat RingDamageScale { get; } = new(1.5f);

    /// <summary>The time constant the quarry's velocity estimate is smoothed over.</summary>
    public Stat PredictWindowSeconds { get; } = new(0.4f);

    /// <summary>How far ahead of the quarry the ring is predicted.</summary>
    public Stat LeadSeconds { get; } = new(1f);

    /// <summary>The longest lead, metres from the quarry.</summary>
    public Stat MaxLead { get; } = new(6f);

    /// <summary>How long it stands surfaced and can be hurt.</summary>
    public Stat WindowSeconds { get; } = new(4f);

    /// <summary>How long it stands untargetable before it burrows again.</summary>
    public Stat DiveSeconds { get; } = new(0.5f);
}
