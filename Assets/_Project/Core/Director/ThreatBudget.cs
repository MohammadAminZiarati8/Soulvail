using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Director;

/// <summary>
/// GD §12's curves bound to one device ceiling: how much threat a stage may cost, how many waves
/// it comes in, how many enemies may stand in it at once, and how much tougher, harder-hitting and
/// faster each of them is for being deep. What M2-04's composer and M2-05's director ask.
/// </summary>
/// <remarks>
/// <para>
/// <b>It computes and holds no run state.</b> Every method takes the stage as an argument, so two
/// callers asking about two depths in the same frame — a composer looking ahead while the director
/// paces the current stage — cannot disagree about which one this object is "on". The run's live
/// depth is <c>RunState.StageIndex</c>, and there is exactly one of those.
/// </para>
/// <para>
/// <b>The one thing it does own is the device cap</b>, and that is the whole reason the type
/// exists rather than callers reading <see cref="ScalingSpec"/> directly: the cap is a fact about
/// the phone (GD §11.1's 18 / 28 / 40) while the curves are authored content, and pairing them
/// once here is what stops every downstream call site having to carry both and agree about them.
/// GD §11.2's device-independence rule survives that pairing intact — a bigger cap buys a bigger
/// crowd, never a harder stage, and the surplus budget a low-tier phone cannot spend on bodies is
/// spent on quality instead (M2-04).
/// </para>
/// <para>
/// <b>Not what scales an enemy.</b> That is <c>DepthScaling</c>, which takes the curves and not
/// this — putting a modifier on a Husk needs h, d and s and has no business knowing how many of
/// them a phone can draw. In M2-03 nothing in a live run constructs a <see cref="ThreatBudget"/>
/// at all: the cap is not chosen until M2-04, where the arithmetic that prices it lives.
/// </para>
/// </remarks>
public sealed class ThreatBudget
{
    private readonly ScalingSpec _scaling;

    /// <param name="scaling">The mode's curves — <see cref="ModeSpec.Scaling"/>.</param>
    /// <param name="deviceCap">
    /// The most enemies this device may have alive at once: GD §11.1's 18 (low), 28 (mid) or 40
    /// (high). A constant chosen in M2-04 until M8-03 detects a tier.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="scaling"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deviceCap"/> is below 1 — a device that may hold no enemies is an arena
    /// nothing can be composed for, and the failure would surface as a stage that spawns nothing.
    /// </exception>
    public ThreatBudget(ScalingSpec scaling, int deviceCap)
    {
        _scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));

        if (deviceCap < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceCap),
                deviceCap,
                "deviceCap must be at least 1. It is GD §11.1's device tier — 18, 28 or 40 — not "
                    + "a difficulty setting.");
        }

        DeviceCap = deviceCap;
    }

    /// <summary>
    /// The most enemies that may be alive at once on this device, whatever the depth.
    /// </summary>
    /// <remarks>
    /// Exposed because M2-05's spawn safety and M1-19's pooling both size buffers against it, and
    /// re-deriving it from a tier enum in two places is how two answers to one question start.
    /// </remarks>
    public int DeviceCap { get; }

    /// <summary>How much threat <paramref name="stage"/> may be composed from — GD §12.1's B(n).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float Budget(int stage) => _scaling.Budget.At(stage);

    /// <summary>How many waves <paramref name="stage"/> is delivered in — GD §12.2's W(n).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public int Waves(int stage) => _scaling.Waves.At(stage);

    /// <summary>
    /// How many enemies may be alive at <paramref name="stage"/> — GD §12.2's C(n), already
    /// capped at <see cref="DeviceCap"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public int Concurrency(int stage) => _scaling.Concurrency.At(stage, DeviceCap);

    /// <summary>The hit-point multiplier at <paramref name="stage"/> — GD §12.3's h(n).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float HpMultiplier(int stage) => _scaling.Hp.At(stage);

    /// <summary>The damage multiplier at <paramref name="stage"/> — GD §12.3's d(n).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float DamageMultiplier(int stage) => _scaling.Damage.At(stage);

    /// <summary>The move-speed multiplier at <paramref name="stage"/> — GD §12.3's s(n).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float SpeedMultiplier(int stage) => _scaling.Speed.At(stage);
}
