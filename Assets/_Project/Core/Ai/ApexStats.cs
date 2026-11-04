using Soulvail.Core.Combat;

namespace Soulvail.Core.Ai;

/// <summary>
/// Every number the Apex's hunt, dash and rake read (JG-30a), each a <see cref="Stat"/> so a phase or a
/// later tuning pass can sit a modifier on it. Built once per boss by <c>RunSession</c>.
/// </summary>
public sealed class ApexStats
{
    /// <summary>Her hunting run, metres per second.</summary>
    public Stat RunSpeed { get; } = new(5.5f);

    /// <summary>The least time between one move ending and the next being picked.</summary>
    public Stat MoveGapSeconds { get; } = new(1f);

    /// <summary>How long the dash lane warns before it bites; never under 0.6 s.</summary>
    public Stat DashTelegraphSeconds { get; } = new(0.7f);

    /// <summary>The least distance to the player at which she dashes rather than hunts, metres.</summary>
    public Stat DashMinRange { get; } = new(4f);

    /// <summary>The dash lane's full length, metres; a stone shortens it.</summary>
    public Stat DashLength { get; } = new(10f);

    /// <summary>The dash lane's half-width, metres; also the probe's radius.</summary>
    public Stat DashHalfWidth { get; } = new(0.9f);

    /// <summary>Her speed along the lane, metres per second.</summary>
    public Stat DashSpeed { get; } = new(22f);

    /// <summary>The lane's damage as a multiple of <c>ContactDamage</c>.</summary>
    public Stat DashDamageScale { get; } = new(1.5f);

    /// <summary>How long she stands after a dash.</summary>
    public Stat DashRecoverSeconds { get; } = new(0.8f);

    /// <summary>How long after a dash ends before she may dash again.</summary>
    public Stat DashCooldownSeconds { get; } = new(3.5f);

    /// <summary>
    /// How long past the lane's travel time a blocked dash keeps pushing before it gives up, so a
    /// body that cannot move never runs forever.
    /// </summary>
    public Stat DashGraceSeconds { get; } = new(0.3f);

    /// <summary>The distance to the player within which she rakes, metres.</summary>
    public Stat RakeRange { get; } = new(3f);

    /// <summary>How long the rake winds up before its first swipe.</summary>
    public Stat RakeWindupSeconds { get; } = new(0.6f);

    /// <summary>How long after the first swipe the second lands.</summary>
    public Stat RakeSecondSeconds { get; } = new(0.4f);

    /// <summary>Each swipe's reach, metres.</summary>
    public Stat RakeReach { get; } = new(2.6f);

    /// <summary>Each swipe's half-angle either side of her aim, degrees.</summary>
    public Stat RakeHalfAngleDeg { get; } = new(50f);

    /// <summary>Each swipe's damage as a multiple of <c>ContactDamage</c>.</summary>
    public Stat RakeDamageScale { get; } = new(1f);

    /// <summary>How long she stands after the second swipe.</summary>
    public Stat RakeRecoverSeconds { get; } = new(0.6f);
}
