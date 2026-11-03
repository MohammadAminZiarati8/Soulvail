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

    /// <summary>How long the eruption's throw takes (JG-25b).</summary>
    public Stat ThrowSeconds { get; } = new(0.25f);

    /// <summary>How far outside the ring an unobstructed throw lands, metres.</summary>
    public Stat ThrowMargin { get; } = new(1f);

    /// <summary>How long after the eruption the window's move starts its telegraph (JG-25c).</summary>
    public Stat MoveStartSeconds { get; } = new(0.4f);

    /// <summary>How long the tremor and the spit telegraph before release.</summary>
    public Stat MoveTelegraphSeconds { get; } = new(0.8f);

    /// <summary>How fast the tremor's ring grows, metres per second.</summary>
    public Stat TremorSpeed { get; } = new(7f);

    /// <summary>How far the tremor's ring reaches, metres.</summary>
    public Stat TremorMaxRadius { get; } = new(9f);

    /// <summary>The tremor band's width, metres.</summary>
    public Stat TremorThickness { get; } = new(1f);

    /// <summary>The tremor's damage as a multiple of <c>ContactDamage</c>.</summary>
    public Stat TremorDamageScale { get; } = new(1f);

    /// <summary>How many rocks one spit throws.</summary>
    public Stat SpitSpears { get; } = new(3f);

    /// <summary>How far across the line the outer rocks land, metres.</summary>
    public Stat SpitSpread { get; } = new(2.5f);

    /// <summary>The rocks' flight speed, metres per second.</summary>
    public Stat SpitSpeed { get; } = new(9f);

    /// <summary>Each rock's landing radius, metres.</summary>
    public Stat SpitRadius { get; } = new(1.4f);

    /// <summary>Each rock's damage as a multiple of <c>ContactDamage</c>.</summary>
    public Stat SpitDamageScale { get; } = new(1f);
}
