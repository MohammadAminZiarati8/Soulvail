using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

/// <summary>
/// The run's experience and levels: what a kill is worth, when a threshold is crossed, and how
/// many picks the player is owed for it. One per run, built by <c>RunSession.Start</c> from the
/// mode's <see cref="XpCurve"/>. See CH §5.2, GD §15 and AR §5.
/// </summary>
/// <remarks>
/// <para>
/// <b>It banks levels rather than spending them.</b> Crossing a threshold raises
/// <see cref="PendingLevelUps"/> and publishes; nothing here decides what a pick <em>is</em>.
/// <see cref="SpendLevelUp"/> is called by whoever hands one out — M3-08's offer screen, or an
/// overflow level once the tree is full (CH §5.2) — and until those exist the counter simply
/// climbs, which is what makes the shape testable a task before its first consumer.
/// </para>
/// <para>
/// <b>It is reached through <c>RunState</c> as three reads and never as a handle</b> (AR §18.2):
/// <see cref="Grant"/> and <see cref="SpendLevelUp"/> are public, so a public handle would let a
/// view level the player or consume a pick they had not been shown. The same question
/// <c>Motor</c>, <c>Combat</c>, <c>Enemies</c> and <c>Projectiles</c> all answered the same way.
/// </para>
/// <para>
/// Allocates nothing on any path. <see cref="Grant"/> runs once a tick for the whole arena's
/// kills, and the events it publishes are structs crossing <see cref="IDomainEvents"/> by
/// <see langword="in"/>.
/// </para>
/// </remarks>
public sealed class LevelTracker
{
    /// <summary>The level every run starts at, and the one that costs nothing to reach.</summary>
    private const int StartingLevel = 1;

    private readonly XpCurve _curve;
    private readonly IDomainEvents _events;

    /// <param name="curve">
    /// The mode's levelling curve. Must be authored — see <see cref="XpCurve.IsAuthored"/> for
    /// what a zeroed one would do to <see cref="Grant"/>'s loop.
    /// </param>
    /// <param name="events">Where <see cref="LeveledUp"/> and <see cref="XpChanged"/> go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="curve"/> is <c>default(XpCurve)</c> and so never passed a constructor.
    /// Refused here as well as on <see cref="ModeSpec"/>, because a struct always has a zeroed
    /// form and this is the end that would hang (AR §18.3).
    /// </exception>
    public LevelTracker(XpCurve curve, IDomainEvents events)
    {
        if (!curve.IsAuthored)
        {
            throw new ArgumentException(
                "curve is a default value and never passed a constructor, so it answers 0 for "
                    + "every level. A tracker fed one would cross a threshold on every grant "
                    + "without end — the loop in Grant would never terminate.",
                nameof(curve));
        }

        _curve = curve;
        _events = events ?? throw new ArgumentNullException(nameof(events));

        XpGain = new Stat(1f);
    }

    /// <summary>
    /// A multiplier on every grant, base 1 — the address M3-05's <c>PlayerStat.XpGain</c> resolves
    /// to and where M3-12's "+XP" nodes land (ADR-0008).
    /// </summary>
    /// <remarks>
    /// Nothing in M3-01a puts a modifier on it. A stack that drives it to zero silences experience
    /// reversibly rather than throwing, which is <see cref="Grant"/>'s rule 4 — the stat is the
    /// one door every source has to come through, so a debuff that switches XP off and a node that
    /// doubles it are the same mechanism.
    /// </remarks>
    public Stat XpGain { get; }

    /// <summary>The player's level, from 1.</summary>
    public int Level { get; private set; } = StartingLevel;

    /// <summary>
    /// Experience into the current level, in <c>[0, <see cref="XpToNext"/>)</c> — not the run's
    /// total, which nothing asks for.
    /// </summary>
    public float Xp { get; private set; }

    /// <summary>What the next level costs: <c>curve.ToReach(Level + 1)</c>.</summary>
    /// <remarks>
    /// Always greater than zero, which is what <see cref="XpCurve.IsAuthored"/> buys and what
    /// makes <see cref="XpFraction"/> safe to divide and <see cref="Grant"/>'s loop safe to run.
    /// </remarks>
    public float XpToNext => _curve.ToReach(Level + 1);

    /// <summary>
    /// How far into the current level the player is, in <c>[0, 1)</c> — what M3-10b's XP strip
    /// fills to.
    /// </summary>
    public float XpFraction => Xp / XpToNext;

    /// <summary>
    /// How many picks the player has earned and not yet been given. Zero for most of a run, and
    /// two at once whenever one grant crossed two thresholds.
    /// </summary>
    public int PendingLevelUps { get; private set; }

    /// <summary>
    /// Awards <paramref name="baseXp"/>, scaled by <see cref="XpGain"/>, crossing as many
    /// thresholds as it pays for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A grant worth nothing says nothing.</b> A non-positive amount, a NaN, an infinity, or a
    /// modifier stack that drove the gain to zero or below all leave the tracker untouched and
    /// publish neither event — spelled <c>!(amount &gt; 0f)</c> so that NaN is refused rather than
    /// waved through by a natural-looking <c>&lt;= 0</c> (AR §18.3), and with infinity asked about
    /// separately because it passes a <c>&gt; 0</c> test and would then make the loop below
    /// endless. <see cref="XpGain"/> is floored at zero rather than allowed to invert a grant: a
    /// stack that went negative means experience is switched off, not that killing something
    /// should cost the player a level.
    /// </para>
    /// <para>
    /// <b>Every <see cref="LeveledUp"/> first, exactly one <see cref="XpChanged"/> last.</b> The
    /// bar wants the settled state — an <see cref="XpChanged"/> carrying a fraction above 1 is a
    /// bar asked to draw a number it cannot — and a screen that pauses on <see cref="LeveledUp"/>
    /// (M3-08) wants the level before the bar is redrawn underneath it.
    /// </para>
    /// <para>
    /// Several thresholds in one call is legal and ordinary rather than a guarded edge: a stage's
    /// last wave is paid for in one drain, and at stage 1 a single Bloater is a quarter of a level.
    /// </para>
    /// </remarks>
    /// <param name="baseXp">
    /// The authored value before <see cref="XpGain"/> — the sum of every
    /// <c>EnemySpec.XpValue</c> banked since the last tick.
    /// </param>
    public void Grant(float baseXp)
    {
        // Floored, not clamped to a range: there is no ceiling a mode could not legitimately want,
        // and MathF.Max carries a NaN through on purpose so the guard below is the one place a
        // non-number is turned away.
        float amount = baseXp * MathF.Max(0f, XpGain.Value);

        if (!(amount > 0f) || float.IsInfinity(amount))
        {
            return;
        }

        Xp += amount;

        // Recomputed each step rather than hoisted: crossing a threshold changes what the next one
        // costs, so a cached XpToNext would let one enormous grant walk up the curve at the price
        // of its first level.
        while (Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
            PendingLevelUps++;

            _events.Publish(new LeveledUp(Level, PendingLevelUps));
        }

        _events.Publish(new XpChanged(amount, Level, XpFraction));
    }

    /// <summary>
    /// Consumes one of the picks the player is owed.
    /// </summary>
    /// <remarks>
    /// Called by whoever actually hands the pick over — M3-08's <c>ChooseOffer</c>, or the
    /// overflow path once the tree is full. Nothing in M3-01a calls it; it exists here so the
    /// shape is fixed before the screen that needs it is written.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Nothing is owed. Spending a pick nobody earned is a bug in the caller rather than a state
    /// the tracker should absorb — silently ignoring it would let a double-tapped offer screen
    /// hand out two nodes for one level, with nothing to report it.
    /// </exception>
    public void SpendLevelUp()
    {
        if (PendingLevelUps <= 0)
        {
            throw new InvalidOperationException(
                "No level-up is owed. SpendLevelUp is the act of handing one over, so calling it "
                    + "at zero means the caller believes the player earned a pick they did not.");
        }

        PendingLevelUps--;
    }
}
