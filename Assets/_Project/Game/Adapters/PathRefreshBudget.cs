using System;

namespace Soulvail.Game.Adapters;

/// <summary>
/// How many enemies may have their route recomputed this frame: enough to keep the whole
/// population at its refresh cadence, and never so many that reacting to a slow frame extends it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a constant could not answer the question.</b> <c>NavPathSense</c> refreshed
/// at most four paths a frame against a 10 Hz cadence, which sustains
/// <b>24 enemies at 60 fps and 12 at 30</b> — below M2-04's cap of 28 and below GD §11.1's <em>low</em>
/// tier of 18. Above that the population simply falls behind: routes go stale, enemies walk into
/// pillars, and nothing anywhere reports it. Scaling the allowance with how many enemies there are
/// and how long the frame took is the fix, and <c>NavPathSense.StalePathCount</c> is the part that
/// makes the ceiling visible when it is hit anyway.
/// </para>
/// <para>
/// <b>Pure, and deliberately holding no Unity type.</b> The arithmetic is what is worth testing —
/// a <c>NavMesh</c> is needed to exercise the sense around it and an EditMode scene has none — so
/// the number is decided here and the engine call stays where it belongs.
/// </para>
/// </remarks>
public sealed class PathRefreshBudget
{
    /// <summary>
    /// The most paths that may be recomputed on any one frame, whatever the arithmetic asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sixteen. <c>NavMesh.CalculatePath</c> is a synchronous A*, so the ceiling is what stops a
    /// hitch feeding itself: a frame that took 100 ms would otherwise be told to recompute
    /// <c>28 · 10 · 0.1</c> = 28 routes at once, on the frame that was already too slow — and the
    /// next frame would inherit the same debt, one hitch longer.
    /// </para>
    /// <para>
    /// It is four times the constant it replaces, and that is affordable for the same reason the
    /// old one was not: 16 is a worst case reached only on a frame that has already gone wrong,
    /// while 4 was the ceiling on <em>every</em> frame. At 28 enemies and 60 fps the steady state
    /// is 5.
    /// </para>
    /// </remarks>
    public const int DefaultMaxPerFrame = 16;

    private readonly float _refreshHz;
    private readonly int _maxPerFrame;

    /// <param name="refreshHz">
    /// How often one enemy's route is recomputed, in times per second — the same cadence the
    /// sense caches against. Passing a different number here would budget for a rate nothing runs
    /// at, which is why <c>NavPathSense</c> builds this from its own.
    /// </param>
    /// <param name="maxPerFrame">The ceiling. See <see cref="DefaultMaxPerFrame"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="refreshHz"/> is not finite and positive, or <paramref name="maxPerFrame"/>
    /// is below 1. A ceiling of zero would path nothing at all, for ever, and every enemy would
    /// walk the straight line into the nearest pillar.
    /// </exception>
    public PathRefreshBudget(float refreshHz, int maxPerFrame)
    {
        // Negated positive, never `<= 0f`: every comparison against NaN is false, so the natural
        // spelling admits one — and a NaN rate makes every budget NaN, which casts to a number
        // nobody chose (AR §18.3).
        if (!(refreshHz > 0f) || float.IsInfinity(refreshHz))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshHz),
                refreshHz,
                "refreshHz must be finite and greater than zero.");
        }

        if (maxPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPerFrame),
                maxPerFrame,
                "maxPerFrame must be at least 1. A budget of zero never recomputes anything, so "
                    + "every enemy would keep whatever route it was born with.");
        }

        _refreshHz = refreshHz;
        _maxPerFrame = maxPerFrame;
    }

    /// <summary>
    /// How many paths may be recomputed this frame:
    /// <c>ceil(activeCount · refreshHz · dt)</c>, at least 1, never more than the ceiling.
    /// </summary>
    /// <param name="activeCount">How many enemies are asking. Zero is legal — an empty arena.</param>
    /// <param name="dt">Seconds the frame took. The snapshot's clamped step, not <c>Time.deltaTime</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>The product is the whole idea:</b> a population of <c>n</c> refreshing <c>refreshHz</c>
    /// times a second needs <c>n · refreshHz</c> recomputes a second however the frames are sliced,
    /// so a long frame owes more of them and a short one fewer. At 28 enemies and 10 Hz that is 5 a
    /// frame at 60 fps and 10 at 30 — where the fixed four sustained 24 and 12.
    /// </para>
    /// <para>
    /// <b>At least one, always.</b> Rounding up already guarantees it for any positive product, and
    /// the floor is what covers the two cases where the product is zero: an arena with nobody in
    /// it, and the first frame of a run, which has no previous frame to measure a step against.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="activeCount"/> is negative, or <paramref name="dt"/> is negative or not
    /// finite.
    /// </exception>
    public int ForFrame(int activeCount, float dt)
    {
        if (activeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeCount),
                activeCount,
                "activeCount is a population and cannot be negative.");
        }

        if (!(dt >= 0f) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "dt must be finite and not negative. Time does not run backwards, and a NaN step "
                    + "would make the budget NaN and the cast to int meaningless.");
        }

        // In double, so that a large count times a large step cannot overflow the cast below — the
        // clamp then makes the size of the number irrelevant.
        double raw = (double)activeCount * _refreshHz * dt;

        if (raw >= _maxPerFrame)
        {
            return _maxPerFrame;
        }

        int budget = (int)Math.Ceiling(raw);

        return budget < 1 ? 1 : budget;
    }
}
