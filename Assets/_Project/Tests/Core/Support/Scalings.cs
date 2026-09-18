using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Support;

/// <summary>
/// The authored curves a <see cref="ModeSpec"/> requires — GD §12's five and CH §5.2's one — for
/// the fixtures that need a mode and are not about either model.
/// </summary>
/// <remarks>
/// <para>
/// It exists because <see cref="ScalingSpec"/> became a required <see cref="ModeSpec"/> argument in
/// M2-03 and nine fixtures build a mode. Eight of them build one because a run needs one — they
/// are about targeting, cone hits, the Charge, the catalog or a session's lifecycle — and eight
/// copies of GD §12's twenty-one numbers in test code is precisely the drift the project's own
/// comments keep warning about: a second copy of a number is a number that will disagree.
/// </para>
/// <para>
/// <b><see cref="Xp"/> joined it in M3-01a for exactly that reason, at four times the scale.</b>
/// <see cref="XpCurve"/> became a required argument too, and thirty-three call sites across
/// sixteen fixtures would have been thirty-three copies of CH §5.2's three numbers — in a
/// milestone whose own spec says the exponent is flagged for a retune at M3-15. One copy is one
/// edit when that happens; thirty-three is a search.
/// </para>
/// <para>
/// <b>The fixtures that are about the curves do not use this.</b> <c>ThreatBudgetTests</c>,
/// <c>DepthScalingTests</c> and <c>XpCurveTests</c> write the numbers out in full, deliberately,
/// because a test of the arithmetic against a shared helper would be a test of a copy against
/// itself. This is for the fixtures whose answer to "what should the curve be" is "anything
/// valid".
/// </para>
/// </remarks>
internal static class Scalings
{
    /// <summary>
    /// GD §12's five curves as the game ships them: B(n) = 40 + 12(n−1) + 0.9(n−1)²,
    /// W(n) = clamp(2 + n/5, 2, 5), C(n) = min(10 + n/2, cap), h/d/s as GD §12.3.
    /// </summary>
    /// <remarks>
    /// A fresh instance per call, like every other spec factory in these fixtures: a shared
    /// immutable one would be safe today and would be the thing a future mutable field was noticed
    /// through, one fixture at a time.
    /// </remarks>
    internal static ScalingSpec Design() => new ScalingSpec(
        new BudgetCurve(40f, 12f, 0.9f),
        new WaveCurve(2, 5, 2, 5),
        new ConcurrencyCurve(10, 2),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    /// <summary>
    /// CH §5.2's levelling curve as the game ships it: ToReach(N) = 20 + 12·N^1.4.
    /// </summary>
    /// <remarks>
    /// A struct, so "a fresh instance per call" is what the language already guarantees. The
    /// numbers are the shipped ones rather than round test values on purpose: a fixture that
    /// happens to level mid-test levels at the pace the game does.
    /// </remarks>
    internal static XpCurve Xp() => new XpCurve(20f, 12f, 1.4f);
}
