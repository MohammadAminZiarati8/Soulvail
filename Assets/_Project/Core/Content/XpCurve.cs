using System;

namespace Soulvail.Core.Content;

/// <summary>
/// How much experience each level costs: <c>ToReach(N) = base + perLevel · N^exponent</c> —
/// CH §5.2. Level 1 costs nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>It belongs to the mode, not to the character and not to the game.</b> GD §4.5 makes a mode a
/// data object, and levelling pace is the second-largest thing after difficulty that tells one
/// mode from another: a Boss Rush levels differently or not at all. It sits on
/// <see cref="ModeSpec"/> beside <see cref="ScalingSpec"/> for that reason, and
/// <see cref="Soulvail.Core.Progression.LevelTracker"/> is handed one rather than holding a curve
/// of its own.
/// </para>
/// <para>
/// <b>It lives in <c>Core/Content/</c>, not <c>Core/Progression/</c>.</b> AR §5's module table
/// lists it under <c>Progression</c>; it is authored data on the mode, the same kind of thing as
/// GD §12's five curves, and <see cref="ModeSpec"/> — which is Content — has to name it. Placing
/// it in <c>Progression</c> would make <c>Content</c> depend on <c>Progression</c> while
/// <c>Progression</c> depends on <c>Content</c> for M3-02a's skill specs. AR §10.1's "specs are
/// immutable records in the catalog" wins, and §5's row was corrected in M3-01a — a sketch is
/// fixed when it misleads, which is the move <see cref="ModeSpec"/> itself made in M2-02.
/// </para>
/// <para>
/// A <see langword="readonly"/> struct, for the reason GD §12's five curves are: three floats and
/// three lines of arithmetic, read once a level and held for a run. It also means it has a zeroed
/// form that carried no guard, which is what <see cref="IsAuthored"/> is for — a struct with an
/// invariant needs the check at both ends (AR §18.3).
/// </para>
/// <para>
/// <b>CH §5.2's own table and this formula disagree past stage 10, and the numbers ship as
/// authored.</b> Under 20 / 12 / 1.4 against GD §12.1's quadratic budget and a flat XP per kill,
/// the tree fills around stage 20 where the table says 30. The owner ruled at M3-00a that the
/// exponent stays until M3-15 has measured a real run with a stopwatch;
/// <c>XpCurveTests.Pacing_TreeFullByStageTwenty</c> pins the divergence so that a retune is a
/// visible diff rather than a silent drift.
/// </para>
/// </remarks>
public readonly struct XpCurve
{
    private readonly float _base;
    private readonly float _perLevel;
    private readonly float _exponent;

    /// <param name="base">
    /// Added to every level's cost. 20 in CH §5.2. Zero is legal and means a curve that is pure
    /// exponent — a mode whose first levels are nearly free.
    /// </param>
    /// <param name="perLevel">
    /// Multiplies the exponentiated level. 12 in CH §5.2. Must be positive: see
    /// <see cref="IsAuthored"/> for what a zero would do to the tracker that reads this.
    /// </param>
    /// <param name="exponent">
    /// How sharply the cost climbs. 1.4 in CH §5.2 — above 1, so each level costs more than the
    /// last by more than a constant. Must be positive; at zero every level costs the same and the
    /// curve stops being one.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="base"/> is negative, NaN or infinite, or either of
    /// <paramref name="perLevel"/> and <paramref name="exponent"/> is not a finite number greater
    /// than zero.
    /// </exception>
    public XpCurve(float @base, float perLevel, float exponent)
    {
        // CurveGuard for two of the three and not for the first, deliberately: its Positive is
        // exactly this question and its message is generic, while its NonNegative's message is
        // about a rate making deep stages easier than shallow ones — true of GD §12's curves and
        // nonsense about an XP base. A guard whose message describes a different type is worse
        // than a guard written out.
        if (!(@base >= 0f) || float.IsInfinity(@base))
        {
            throw new ArgumentOutOfRangeException(
                nameof(@base),
                @base,
                "base must be a finite number, zero or more. A negative base makes the early "
                    + "levels cost less than nothing, which is a tracker that levels on a grant "
                    + "of one experience point and reports nothing wrong.");
        }

        _base = @base;
        _perLevel = CurveGuard.Positive(perLevel, nameof(perLevel));
        _exponent = CurveGuard.Positive(exponent, nameof(exponent));
    }

    /// <summary>
    /// Whether this came from the constructor rather than from <c>default</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="ScalingSpec"/>'s curves carry the same member for the same shape of reason, and
    /// this one's consequence is the sharpest in the project: a zeroed curve answers <c>0</c> for
    /// every level, so <see cref="Soulvail.Core.Progression.LevelTracker.Grant"/>'s
    /// <c>while (Xp &gt;= XpToNext)</c> would never stop — one kill would level the player for
    /// ever and hang the frame. <see cref="ModeSpec"/> refuses a default the way
    /// <c>ScalingSpec.Require</c> does, and <c>_perLevel</c> being positive is what the
    /// constructor guarantees and a zeroed struct cannot fake.
    /// </remarks>
    public bool IsAuthored => _perLevel > 0f;

    /// <summary>
    /// The experience needed to go from <paramref name="level"/> − 1 to <paramref name="level"/>;
    /// <c>0</c> for level 1 and below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A level below 1 answers zero rather than throwing</b>, which is the opposite of
    /// <c>CurveGuard.Stage</c> and is a different question. A stage is a depth the player walks
    /// into, so stage 0 is a place that does not exist and a curve has no honest answer for it;
    /// level 1 is where every run starts and it is free, so "what did it cost to get here" has one
    /// correct answer and it is nothing. Zero and the negatives get the same answer because they
    /// are the same statement — a level nobody had to earn.
    /// </para>
    /// <para>
    /// Allocates nothing, and is read once per level crossed rather than per frame.
    /// </para>
    /// </remarks>
    public float ToReach(int level)
    {
        if (level <= 1)
        {
            return 0f;
        }

        return _base + (_perLevel * MathF.Pow(level, _exponent));
    }
}
