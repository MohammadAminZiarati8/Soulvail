using System;

namespace Soulvail.Core.Content;

// The vocabulary CC §6.3's trigger line is written from — keys and kinds of number, never text.
// Beside `TriggerSpec.cs`, whose enums it is addressed by, for the reason those four types share a
// file: a module's vocabulary read in one place is worth more than one type per file.

/// <summary>
/// What kind of number a <see cref="TriggerClause.Threshold"/> is, so a screen can format it
/// without a switch of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closed: a field is one of these or the table is wrong</b> (ADR-0010). The set is small on
/// purpose — it is the list of <em>ways a number is written down</em> rather than the list of
/// things a number can mean, so a tenth <see cref="TriggerField"/> almost always reuses one of
/// these four rather than adding a fifth.
/// </para>
/// <para>
/// <b>It says what a number <em>is</em> and never what it looks like.</b> Turning 0.6 into
/// <em>"60 %"</em> needs a culture and a format string, which are presentation; deciding that 0.6
/// <em>is</em> a fraction is content. That is the same split <see cref="LocKey"/> makes everywhere
/// else — core owns the identity of the thing, Game owns how it looks (ADR-0012).
/// </para>
/// </remarks>
public enum TriggerUnit
{
    /// <summary>A ratio in <c>[0, 1]</c>. A screen writes it as a percentage.</summary>
    Fraction,

    /// <summary>A whole number of things — bodies, bolts. Written without a decimal point.</summary>
    Count,

    /// <summary>A meter reading with a scale of its own. Veilrot's 0–100 (GD §13).</summary>
    Points,

    /// <summary>A duration in seconds.</summary>
    Seconds,
}

/// <summary>
/// The vocabulary a CC §6.3 trigger line is written from: one <see cref="LocKey"/> per
/// <c>(field, comparison)</c> pair, and the kind of number that goes beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys and kinds, never text.</b> Resolving a key is <c>ILocalizer</c>'s job and it has no
/// adapter until M6-10, so until then the Skills screen draws <c>trigger.hpFraction.below</c> with
/// <em>"60 %"</em> beside it (ADR-0012, ledger row 9). What this class buys is that the
/// <em>composition</em> — a key and a number — is decided now rather than at M6-10, because that is
/// the half a localiser cannot fix afterwards.
/// </para>
/// <para>
/// <b>A key per pair, and the threshold is the only substitution.</b> Eighteen keys for nine
/// fields, each resolving at M6-10 into a format string with one placeholder: <em>"Player HP below
/// {0}"</em>. A key per field with the comparison bolted on separately was the alternative and it
/// is wrong for exactly the reason word order is — <em>"below"</em> lands in different places in
/// different languages, and a screen that concatenates three fragments has decided English's order
/// for all of them.
/// </para>
/// <para>
/// Static and stateless, which is <see cref="Soulvail.Core.Combat.CooldownRules"/>' shape and its
/// reason: AR §7 bans static <em>mutable</em> state, and there is none here. Two jump tables.
/// </para>
/// </remarks>
public static class TriggerText
{
    /// <summary>
    /// The key for one clause — <c>trigger.hpFraction.below</c>,
    /// <c>trigger.incomingProjectiles.atLeast</c>.
    /// </summary>
    /// <param name="field">Which blackboard field the clause reads.</param>
    /// <param name="comparison">Which way round it reads.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either argument is a value with no arm below — which means a member was added to
    /// <see cref="TriggerField"/> or <see cref="TriggerComparison"/> and not to this table. Loud
    /// rather than a placeholder key, for <c>PlayerStats.Resolve</c>'s reason (M3-05 rule 4): a
    /// silent fallback is a trigger line that describes the wrong condition, and a player who acts
    /// on it has been lied to by the one screen whose whole job is to explain.
    /// </exception>
    /// <remarks>
    /// A switch over the <em>pair</em> with a loud <c>default</c>, and
    /// <c>Trigger_EveryPairHasAKey</c> walks the cross product — so a
    /// <see cref="TriggerField"/> added without its two keys fails in the suite rather than on the
    /// screen.
    /// </remarks>
    public static LocKey KeyFor(TriggerField field, TriggerComparison comparison)
    {
        return (field, comparison) switch
        {
            (TriggerField.HpFraction, TriggerComparison.Below) =>
                new LocKey("trigger.hpFraction.below"),
            (TriggerField.HpFraction, TriggerComparison.AtLeast) =>
                new LocKey("trigger.hpFraction.atLeast"),

            (TriggerField.ShieldFraction, TriggerComparison.Below) =>
                new LocKey("trigger.shieldFraction.below"),
            (TriggerField.ShieldFraction, TriggerComparison.AtLeast) =>
                new LocKey("trigger.shieldFraction.atLeast"),

            (TriggerField.EnemiesWithin6m, TriggerComparison.Below) =>
                new LocKey("trigger.enemiesWithin6m.below"),
            (TriggerField.EnemiesWithin6m, TriggerComparison.AtLeast) =>
                new LocKey("trigger.enemiesWithin6m.atLeast"),

            (TriggerField.EnemiesWithin8m, TriggerComparison.Below) =>
                new LocKey("trigger.enemiesWithin8m.below"),
            (TriggerField.EnemiesWithin8m, TriggerComparison.AtLeast) =>
                new LocKey("trigger.enemiesWithin8m.atLeast"),

            (TriggerField.EnemiesInAcquireRange, TriggerComparison.Below) =>
                new LocKey("trigger.enemiesInAcquireRange.below"),
            (TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast) =>
                new LocKey("trigger.enemiesInAcquireRange.atLeast"),

            (TriggerField.IncomingProjectiles, TriggerComparison.Below) =>
                new LocKey("trigger.incomingProjectiles.below"),
            (TriggerField.IncomingProjectiles, TriggerComparison.AtLeast) =>
                new LocKey("trigger.incomingProjectiles.atLeast"),

            (TriggerField.Veilrot, TriggerComparison.Below) =>
                new LocKey("trigger.veilrot.below"),
            (TriggerField.Veilrot, TriggerComparison.AtLeast) =>
                new LocKey("trigger.veilrot.atLeast"),

            (TriggerField.StationaryTime, TriggerComparison.Below) =>
                new LocKey("trigger.stationaryTime.below"),
            (TriggerField.StationaryTime, TriggerComparison.AtLeast) =>
                new LocKey("trigger.stationaryTime.atLeast"),

            (TriggerField.FocusRampLevel, TriggerComparison.Below) =>
                new LocKey("trigger.focusRampLevel.below"),
            (TriggerField.FocusRampLevel, TriggerComparison.AtLeast) =>
                new LocKey("trigger.focusRampLevel.atLeast"),

            _ => throw Unknown(field, comparison),
        };
    }

    /// <summary>
    /// What kind of number <paramref name="field"/>'s threshold is.
    /// </summary>
    /// <param name="field">Which blackboard field the clause reads.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="field"/> has no arm below — <see cref="KeyFor"/>'s reason, and the same
    /// failure: a field added to the enum and not to the table that describes it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b><see cref="TriggerField.FocusRampLevel"/> answers <see cref="TriggerUnit.Count"/>, and
    /// the spec's rule 2 is what puts it there</b> — <em>"<c>Count</c> for the three enemy counts,
    /// <c>IncomingProjectiles</c> and <c>FocusRampLevel</c>"</em>. <c>CombatBlackboard</c> declares
    /// that field a <see langword="float"/> in <c>[0, 1]</c>, so on the reading this class itself
    /// argues for it is a <see cref="TriggerUnit.Fraction"/>, and a 0.6 threshold drawn as a count
    /// reads <em>"1"</em>. It is unreachable in the build that exists — nothing in
    /// <c>Data/Trees</c> authors a trigger at all until M3-12 — and it is one arm to move. Said
    /// here rather than quietly corrected because the spec is the contract and the arithmetic in
    /// its own sentence (five counts, nine fields) is what would otherwise look wrong.
    /// </para>
    /// </remarks>
    public static TriggerUnit UnitOf(TriggerField field)
    {
        return field switch
        {
            TriggerField.HpFraction => TriggerUnit.Fraction,
            TriggerField.ShieldFraction => TriggerUnit.Fraction,

            TriggerField.EnemiesWithin6m => TriggerUnit.Count,
            TriggerField.EnemiesWithin8m => TriggerUnit.Count,
            TriggerField.EnemiesInAcquireRange => TriggerUnit.Count,
            TriggerField.IncomingProjectiles => TriggerUnit.Count,
            TriggerField.FocusRampLevel => TriggerUnit.Count,

            TriggerField.Veilrot => TriggerUnit.Points,

            TriggerField.StationaryTime => TriggerUnit.Seconds,

            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "No unit is registered for this trigger field. A new TriggerField member needs a "
                    + "line here and two in KeyFor — see the remarks on both."),
        };
    }

    /// <summary>
    /// Which of the two arguments <see cref="KeyFor"/> could not place.
    /// </summary>
    /// <remarks>
    /// Asked by explicit comparison rather than through <c>Enum.IsDefined</c>, which boxes and
    /// reflects for a question about two members. The comparison is checked first because it is the
    /// smaller enum: a caller who passed a cast <see langword="int"/> for both gets told about the
    /// one they are least likely to have meant.
    /// </remarks>
    private static ArgumentOutOfRangeException Unknown(
        TriggerField field,
        TriggerComparison comparison)
    {
        if (comparison != TriggerComparison.Below && comparison != TriggerComparison.AtLeast)
        {
            return new ArgumentOutOfRangeException(
                nameof(comparison),
                comparison,
                "A trigger comparison must be Below or AtLeast — the pair is closed under negation "
                    + "at the threshold, so there is no third way to say either of them.");
        }

        return new ArgumentOutOfRangeException(
            nameof(field),
            field,
            "No key is registered for this trigger field. A new TriggerField member needs two "
                + "lines here, one per comparison, and one in UnitOf.");
    }
}
