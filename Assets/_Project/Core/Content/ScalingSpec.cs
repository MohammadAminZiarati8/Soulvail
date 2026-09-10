using System;

namespace Soulvail.Core.Content;

// GD §12's five curves, grouped in one file on EnemyEvents.cs' precedent: each is three lines of
// arithmetic, and reading the whole difficulty model in one place is worth more than one type per
// file. See AR §11.1 and GD §12.1–12.4.
//
// They are `readonly struct`s rather than classes because a ScalingSpec is read once a stage and
// held for a run — a class per curve would be six allocations at boot and six references to chase
// for numbers that are three floats each. It also means every one of them has a zeroed form that
// carries no guard, which is why ScalingSpec looks at IsAuthored: a struct with an invariant needs
// the check at both ends (AR §18.3).
//
// **The formulas here are authoritative and GD §12.1's own table is not.** Its stage-40 row says
// 1,772 where B(40) is 1,876.9, and the "44×" in the prose beneath it follows from the same wrong
// number. Stages 5, 10 and 20 all agree to a rounding. The table row is the error; it wants a
// one-line correction this task deliberately does not make (M2-03 rule 1, flagged for the owner).

/// <summary>
/// The threat budget a stage may be composed from:
/// <c>B(n) = base + linear·(n−1) + quadratic·(n−1)²</c> — GD §12.1.
/// </summary>
/// <remarks>
/// Quadratic on purpose: early stages ramp gently and deep ones get genuinely oppressive, and it
/// is the curve GD §12.5's death horizon is drawn against. The budget buys enemies; what each one
/// costs is M2-04's question and lives on <see cref="EnemySpec"/>.
/// </remarks>
public readonly struct BudgetCurve
{
    private readonly float _base;
    private readonly float _linear;
    private readonly float _quadratic;

    /// <param name="base">The stage-1 budget. 40 in GD §12.1.</param>
    /// <param name="linear">Added per stage past the first. 12 in GD §12.1.</param>
    /// <param name="quadratic">Added per stage-past-the-first squared. 0.9 in GD §12.1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="base"/> is not a finite number greater than zero — a stage that can afford
    /// nothing is an empty arena — or either rate is negative, NaN or infinite. A negative rate
    /// would make deep stages cheaper than shallow ones, silently.
    /// </exception>
    public BudgetCurve(float @base, float linear, float quadratic)
    {
        _base = CurveGuard.Positive(@base, nameof(@base));
        _linear = CurveGuard.NonNegative(linear, nameof(linear));
        _quadratic = CurveGuard.NonNegative(quadratic, nameof(quadratic));
    }

    /// <summary>
    /// Whether this came from the constructor rather than from <c>default</c>.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="ScalingSpec"/> and nothing else. A zeroed <see cref="BudgetCurve"/>
    /// answers 0 for every stage, which is a mode that spawns nothing and reports no fault — so
    /// the base being positive, which the constructor guarantees and a zeroed struct cannot fake,
    /// is what "authored" is spelled as. Every curve here carries the same member for the same
    /// reason.
    /// </remarks>
    public bool IsAuthored => _base > 0f;

    /// <summary>The budget for <paramref name="stage"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float At(int stage)
    {
        // Stages are numbered from 1 and the curve is written from the first, so the variable is
        // "stages past the first" — B(1) is the base with nothing added to it.
        float steps = CurveGuard.Stage(stage) - 1;

        return _base + (_linear * steps) + (_quadratic * steps * steps);
    }
}

/// <summary>
/// How many waves a stage is delivered in: <c>W(n) = clamp(base + n/stagesPerStep, min, max)</c>
/// — GD §12.2, integer division.
/// </summary>
/// <remarks>
/// Clamped at both ends, and both ends are pacing rather than arithmetic: below the minimum a
/// stage stops having a rhythm at all, and above the maximum it outlasts the player's patience
/// however much budget there is to spend. Surplus goes into the waves' *contents* (GD §11.2),
/// which is M2-04's to spend.
/// </remarks>
public readonly struct WaveCurve
{
    private readonly int _base;
    private readonly int _stagesPerStep;
    private readonly int _min;
    private readonly int _max;

    /// <param name="base">Waves at stage 0, before any step. 2 in GD §12.2.</param>
    /// <param name="stagesPerStep">Stages per extra wave. 5 in GD §12.2.</param>
    /// <param name="min">Fewest waves a stage may have. 2 in GD §12.2.</param>
    /// <param name="max">Most waves a stage may have. 5 in GD §12.2.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="base"/> or <paramref name="min"/> is below 1, <paramref name="max"/> is
    /// below <paramref name="min"/>, or <paramref name="stagesPerStep"/> is not positive — which
    /// would be a division by zero on the first call rather than a number anyone could read.
    /// </exception>
    public WaveCurve(int @base, int stagesPerStep, int min, int max)
    {
        _base = CurveGuard.AtLeast(@base, 1, nameof(@base));
        _stagesPerStep = CurveGuard.AtLeast(stagesPerStep, 1, nameof(stagesPerStep));
        _min = CurveGuard.AtLeast(min, 1, nameof(min));
        _max = CurveGuard.AtLeast(max, _min, nameof(max));
    }

    /// <inheritdoc cref="BudgetCurve.IsAuthored" />
    public bool IsAuthored => _stagesPerStep > 0;

    /// <summary>How many waves <paramref name="stage"/> is delivered in.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public int At(int stage)
    {
        int waves = _base + (CurveGuard.Stage(stage) / _stagesPerStep);

        if (waves < _min)
        {
            return _min;
        }

        return waves > _max ? _max : waves;
    }
}

/// <summary>
/// How many enemies may be alive at once: <c>C(n) = min(base + n/stagesPerStep, deviceCap)</c> —
/// GD §12.2, integer division.
/// </summary>
/// <remarks>
/// <b>The cap is a device number, never a difficulty one</b> (GD §11.1: 18 / 28 / 40). It does two
/// jobs — frame rate and readability — and GD §11.2's rule is that a stronger phone converts its
/// surplus budget into *quality* rather than into a bigger crowd, so nothing here may treat a
/// higher cap as a harder stage. Which tier a device is in is M8-03's; until then the number is a
/// constant chosen in M2-04 against the measured cost of a path refresh and an ally count.
/// </remarks>
public readonly struct ConcurrencyCurve
{
    private readonly int _base;
    private readonly int _stagesPerStep;

    /// <param name="base">Concurrent enemies at stage 0, before any step. 10 in GD §12.2.</param>
    /// <param name="stagesPerStep">Stages per extra concurrent enemy. 2 in GD §12.2.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="base"/> is below 1 — an arena that may hold nobody — or
    /// <paramref name="stagesPerStep"/> is not positive.
    /// </exception>
    public ConcurrencyCurve(int @base, int stagesPerStep)
    {
        _base = CurveGuard.AtLeast(@base, 1, nameof(@base));
        _stagesPerStep = CurveGuard.AtLeast(stagesPerStep, 1, nameof(stagesPerStep));
    }

    /// <inheritdoc cref="BudgetCurve.IsAuthored" />
    public bool IsAuthored => _stagesPerStep > 0;

    /// <summary>
    /// How many enemies may be alive at <paramref name="stage"/> on a device whose ceiling is
    /// <paramref name="deviceCap"/>.
    /// </summary>
    /// <param name="stage">The depth being composed.</param>
    /// <param name="deviceCap">
    /// The device's ceiling — GD §11.1's 18, 28 or 40. Passed rather than held, because the curve
    /// is authored content and the cap is a fact about the phone the game happens to be running
    /// on; <see cref="Soulvail.Core.Director.ThreatBudget"/> is where the two are bound together.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stage"/> is below 1, or <paramref name="deviceCap"/> is below 1.
    /// </exception>
    public int At(int stage, int deviceCap)
    {
        CurveGuard.AtLeast(deviceCap, 1, nameof(deviceCap));

        int concurrent = _base + (CurveGuard.Stage(stage) / _stagesPerStep);

        return concurrent > deviceCap ? deviceCap : concurrent;
    }
}

/// <summary>
/// A capped linear multiplier on one enemy stat:
/// <c>min(1 + perStep · ((n − offset) / stageStep), cap)</c> — GD §12.3, integer division.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type for GD §12.3's three formulas, because they are two spellings of one arithmetic
/// rather than three functions.</b> HP and damage step every stage from <c>n−1</c>
/// (<c>stageOffset</c> 1, <c>stageStep</c> 1); speed steps on <c>floor(n/5)</c>
/// (<c>stageOffset</c> 0, <c>stageStep</c> 5), which is why <c>s(5)</c> is already 1.02 while
/// <c>h(1)</c> and <c>d(1)</c> are exactly 1. The offset exists for that difference and for
/// nothing else, which is why it is the only value the constructor restricts to a pair.
/// </para>
/// <para>
/// <b>Deliberately shallow, and the cap is the design thesis.</b> At stage 40 the budget is ~47×
/// stage 1's while enemy HP is 3.34× — difficulty comes from more things doing more different
/// things at once, not from enemies absorbing more hits, and GD §12.4's TTK invariant (a basic
/// enemy dies in 3–5 hits at every depth) is checked against exactly this at M2-15.
/// </para>
/// </remarks>
public readonly struct StatCurve
{
    /// <summary>
    /// The lowest legal multiplier cap. A cap below 1 would make a deep enemy *weaker* than a
    /// shallow one — the failure this whole type is most likely to be mis-authored into, and one
    /// with no symptom other than a stage 40 that plays like stage 1.
    /// </summary>
    private const float MinCap = 1f;

    private readonly float _perStep;
    private readonly float _cap;
    private readonly int _stageStep;
    private readonly int _stageOffset;

    /// <param name="perStep">
    /// Added to the multiplier per step. 0.06 for GD §12.3's HP, 0.035 for damage, 0.02 for speed.
    /// Zero is legal and means a stat depth does not touch.
    /// </param>
    /// <param name="cap">
    /// The multiplier's ceiling. 4.0 for HP, 3.0 for damage, 1.3 for speed. At least 1.
    /// </param>
    /// <param name="stageStep">Stages per step. 1 for HP and damage, 5 for speed.</param>
    /// <param name="stageOffset">
    /// Subtracted from the stage before the division: 1 for a curve that starts at stage 1
    /// (HP, damage), 0 for one that steps on the stage number itself (speed). Nothing else, and
    /// the pair is the point — see the remarks.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="perStep"/> is negative, NaN or infinite; <paramref name="cap"/> is below 1,
    /// NaN or infinite; <paramref name="stageStep"/> is not positive; or
    /// <paramref name="stageOffset"/> is neither 0 nor 1.
    /// </exception>
    public StatCurve(float perStep, float cap, int stageStep, int stageOffset)
    {
        _perStep = CurveGuard.NonNegative(perStep, nameof(perStep));

        // Guarded as its own question rather than through NonNegative, because the floor is 1 and
        // not 0: the number is a multiplier, so 0.5 is finite, non-negative and halves every deep
        // enemy's hit points.
        if (!(cap >= MinCap) || float.IsInfinity(cap))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cap),
                cap,
                $"cap must be a finite multiplier of at least {MinCap}. A cap below 1 makes deep "
                    + "enemies weaker than shallow ones, and nothing would report it.");
        }

        _cap = cap;
        _stageStep = CurveGuard.AtLeast(stageStep, 1, nameof(stageStep));

        if (stageOffset is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageOffset),
                stageOffset,
                "stageOffset must be 0 or 1. It exists only to spell the difference between "
                    + "GD §12.3's h and d, which step from n−1, and s, which steps on n — not as "
                    + "a free shift of the whole curve.");
        }

        _stageOffset = stageOffset;
    }

    /// <inheritdoc cref="BudgetCurve.IsAuthored" />
    public bool IsAuthored => _cap >= MinCap;

    /// <summary>The multiplier at <paramref name="stage"/>. Never below 1, never above the cap.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    public float At(int stage)
    {
        // Integer division, which is what makes the speed curve a staircase rather than a ramp:
        // stages 5 through 9 all give the same multiplier, and the step is meant to be felt.
        int steps = (CurveGuard.Stage(stage) - _stageOffset) / _stageStep;

        float multiplier = 1f + (_perStep * steps);

        return multiplier > _cap ? _cap : multiplier;
    }
}

/// <summary>
/// A mode's difficulty model, as authored data: GD §12's five curves with one implementation each.
/// Reached through <see cref="ModeSpec.Scaling"/>; bound to a device ceiling by
/// <see cref="Soulvail.Core.Director.ThreatBudget"/> and applied to an enemy by
/// <c>DepthScaling</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It belongs to the mode, not to the game.</b> GD §4.5 makes a mode a data object, and its
/// difficulty curve is the largest thing that distinguishes one from another: a Boss Rush would
/// have a flat budget and no concurrency ramp at all. Nothing in the director may hold a curve of
/// its own.
/// </para>
/// <para>
/// Immutable and shared, like every other spec. It holds no run state — a stage number is always
/// an argument here, never a field.
/// </para>
/// </remarks>
public sealed class ScalingSpec
{
    /// <param name="budget">GD §12.1's B(n).</param>
    /// <param name="waves">GD §12.2's W(n).</param>
    /// <param name="concurrency">GD §12.2's C(n).</param>
    /// <param name="hp">GD §12.3's h(n).</param>
    /// <param name="damage">GD §12.3's d(n).</param>
    /// <param name="speed">GD §12.3's s(n).</param>
    /// <exception cref="ArgumentException">
    /// Any curve is its <c>default</c> value and so never passed a constructor. A struct always has
    /// a zeroed form (AR §18.3), and each of these fails in silence rather than loudly: a zeroed
    /// <see cref="BudgetCurve"/> affords nothing at every depth, a zeroed <see cref="StatCurve"/>
    /// caps every multiplier at 0 and gives every enemy no hit points, and a zeroed
    /// <see cref="WaveCurve"/> divides by zero on the first stage composed.
    /// </exception>
    public ScalingSpec(
        BudgetCurve budget,
        WaveCurve waves,
        ConcurrencyCurve concurrency,
        StatCurve hp,
        StatCurve damage,
        StatCurve speed)
    {
        Require(budget.IsAuthored, nameof(budget));
        Require(waves.IsAuthored, nameof(waves));
        Require(concurrency.IsAuthored, nameof(concurrency));
        Require(hp.IsAuthored, nameof(hp));
        Require(damage.IsAuthored, nameof(damage));
        Require(speed.IsAuthored, nameof(speed));

        Budget = budget;
        Waves = waves;
        Concurrency = concurrency;
        Hp = hp;
        Damage = damage;
        Speed = speed;
    }

    /// <summary>How much threat a stage may be composed from — GD §12.1.</summary>
    public BudgetCurve Budget { get; }

    /// <summary>How many waves a stage is delivered in — GD §12.2.</summary>
    public WaveCurve Waves { get; }

    /// <summary>How many enemies may be alive at once — GD §12.2.</summary>
    public ConcurrencyCurve Concurrency { get; }

    /// <summary>The hit-point multiplier by depth — GD §12.3's h(n).</summary>
    public StatCurve Hp { get; }

    /// <summary>The damage multiplier by depth — GD §12.3's d(n).</summary>
    public StatCurve Damage { get; }

    /// <summary>The move-speed multiplier by depth — GD §12.3's s(n).</summary>
    public StatCurve Speed { get; }

    private static void Require(bool authored, string paramName)
    {
        if (authored)
        {
            return;
        }

        throw new ArgumentException(
            $"{paramName} is a default value and never passed a constructor, so it carries no "
                + "authored numbers. Build it explicitly — a zeroed curve fails silently rather "
                + "than loudly.",
            paramName);
    }
}

/// <summary>
/// The argument guards the five curves share.
/// </summary>
/// <remarks>
/// One copy rather than five, and <c>internal</c> because it is arithmetic plumbing with no
/// meaning outside this file's types. The spellings are AR §18.3's: <c>!(value &gt;= 0f)</c>
/// rather than <c>value &lt; 0f</c>, so NaN is refused instead of waved through, and infinity is
/// asked about separately because it passes every comparison a range check makes.
/// </remarks>
internal static class CurveGuard
{
    /// <summary>Refuses a stage below 1. Stages are numbered from 1 (GD §8.2).</summary>
    internal static int Stage(int stage)
    {
        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "stage must be at least 1. Stages are numbered from 1, so stage 0 is a depth the "
                    + "game never reaches and a curve has no honest answer for it.");
        }

        return stage;
    }

    internal static float Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero.");
        }

        return value;
    }

    internal static float NonNegative(float value, string paramName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number, zero or more. A negative rate would make "
                    + "deep stages easier than shallow ones, with nothing to report it.");
        }

        return value;
    }

    internal static int AtLeast(int value, int minimum, string paramName)
    {
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be at least {minimum}.");
        }

        return value;
    }
}
