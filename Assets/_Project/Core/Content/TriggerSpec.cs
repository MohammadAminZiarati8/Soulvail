using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Content;

// CC §6.4's authored auto-cast condition, as data: which blackboard field, which way round, and
// what number. Four types in one file for `ModeSpec.cs`' and `RunEvents.cs`' reason — a module's
// vocabulary read in one place is worth more than one type per file, and none of these four says
// anything without the other three. See ADR-0005 and AR §9.

/// <summary>
/// The <see cref="CombatBlackboard"/> fields a CC §6.4 condition may read.
/// </summary>
/// <remarks>
/// <para>
/// A closed set, so an enum is the right shape — the distinction <see cref="ContentId"/> draws
/// between an ordinal and an identity. <b>These are <em>code</em>, not content:</b> every member
/// names a field <c>PlayerCombat.Tick</c> actually writes, so adding one is a change to what the
/// character perceives (ADR-0005's one acknowledged cost) rather than a thing a designer authors.
/// </para>
/// <para>
/// The order is the blackboard's own. Nothing persists a <see cref="TriggerField"/> — a trigger is
/// authored on a <c>SkillDefinition</c> asset and never written to a save — so unlike
/// <c>SeededRandom</c>'s stream indices these ordinals are free to move. A member added without a
/// case in <see cref="TriggerClause.IsMet"/> is caught by <c>Trigger_EveryFieldReads</c>, which
/// walks <see cref="Enum.GetValues(Type)"/> rather than a list somebody has to remember to extend.
/// </para>
/// </remarks>
public enum TriggerField
{
    /// <summary>Current HP over the live maximum, in <c>[0, 1]</c>. Consecrate fires below 0.6.</summary>
    HpFraction,

    /// <summary>Shield points over the shield's maximum, in <c>[0, 1]</c>; zero without one.</summary>
    ShieldFraction,

    /// <summary>Living enemies within 6 m — CC §6.4's Sever trigger.</summary>
    EnemiesWithin6m,

    /// <summary>Living enemies within 8 m, which is the Censer's cone range.</summary>
    EnemiesWithin8m,

    /// <summary>Living enemies within the class's acquire range — 12 m for the Oathbound.</summary>
    EnemiesInAcquireRange,

    /// <summary>Enemy projectiles currently inbound — CC §6.4's Bulwark trigger.</summary>
    IncomingProjectiles,

    /// <summary>Veilrot, GD §13's corruption meter. CH §4.2's Rot Nova wants ≥ 50.</summary>
    Veilrot,

    /// <summary>Seconds the stick has been continuously centred.</summary>
    StationaryTime,

    /// <summary>How far into CC §4.3's Focus ramp the character is, in <c>[0, 1]</c>.</summary>
    FocusRampLevel,

    /// <summary>
    /// Wights standing right now. CH §4.2's Exhume fires below half the cap; zero on a class with no
    /// minions, which is what makes the clause false rather than absent (M5-06a rule 7).
    /// </summary>
    MinionCount,
}

/// <summary>
/// Which way round a <see cref="TriggerClause"/> reads: <c>&lt;</c> or <c>&gt;=</c>.
/// </summary>
/// <remarks>
/// Two members and no more. Every condition in CH §4.2 and CC §6.4 is one of these two — "HP below
/// 60 %", "at least three enemies within 6 m" — and the pair is closed under negation at the
/// threshold, so <c>AtLeast 0.6</c> is exactly "not below 0.6". An <c>Above</c> or an <c>Equals</c>
/// would add a third and a fourth way to say something already sayable, and CC §6.3 has to write
/// every one of them out in plain language on the Skills screen.
/// </remarks>
public enum TriggerComparison
{
    /// <summary>Strictly less than the threshold.</summary>
    Below,

    /// <summary>Greater than or equal to the threshold.</summary>
    AtLeast,
}

/// <summary>
/// One authored condition over one blackboard field: <em>HP below 0.6</em>, <em>at least four
/// enemies within 8 m</em>.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="readonly"/> struct rather than a class, for <see cref="RosterEntry"/>'s reason
/// and one sharper: a <see cref="TriggerSpec"/> is a list of one or two of these, and M3-06 asks
/// <see cref="IsMet"/> of every owned active on every tick. A class per clause would be an object
/// per clause in a type read at 60 Hz.
/// </para>
/// <para>
/// <b>This is ADR-0005's <em>"pure predicates over the <c>CombatBlackboard</c>, carried by the
/// <c>SkillSpec</c> as data"</em> made authorable.</b> A lambda cannot come out of a
/// ScriptableObject; an enum, an enum and a float can, which is what lets CC §6.4's
/// <em>"conditions are authored per skill"</em> mean an asset rather than a line of C#.
/// </para>
/// <para>
/// <b><c>default(TriggerClause)</c> is legal, and it is <em>HpFraction Below 0</em></b> — a clause
/// that can never hold, rather than an invalid one. AR §18.3's "a struct with an invariant needs
/// the check at both ends" does not bite here because the invariant is only that
/// <see cref="Threshold"/> is finite, and zero is: there is nothing for a second check to catch.
/// A defaulted clause silently never fires, which is why <see cref="TriggerSpec"/> is the thing
/// content carries and a bare clause is never authored alone.
/// </para>
/// </remarks>
public readonly struct TriggerClause
{
    /// <param name="field">Which blackboard field to read.</param>
    /// <param name="comparison">Which way round to read it.</param>
    /// <param name="threshold">
    /// The number to compare against. The counts are whole numbers and the fractions are in
    /// <c>[0, 1]</c>, but both arrive here as a <see cref="float"/> — see <see cref="IsMet"/> for
    /// why the widening is the right way round.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="threshold"/> is NaN or infinite. Refused where the condition is
    /// <em>authored</em> rather than where it is evaluated, for <c>ModifyStat</c>'s reason:
    /// a NaN threshold makes every comparison against it false, so the skill would simply never
    /// fire and nothing on screen would say why — the silence M2-06 rule 11 refuses. An infinite
    /// one is the same fault with a direction: <c>AtLeast +∞</c> never holds and
    /// <c>Below +∞</c> always does.
    /// </exception>
    public TriggerClause(TriggerField field, TriggerComparison comparison, float threshold)
    {
        // Asked as "is it NaN or infinite?" rather than as a range comparison, for the reason
        // AR §18.3 gives: every comparison against NaN is false, so a range check waves it through.
        if (float.IsNaN(threshold) || float.IsInfinity(threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "A trigger threshold must be finite. A NaN one makes every comparison against it "
                    + "false, so the skill never fires and nothing says why.");
        }

        Field = field;
        Comparison = comparison;
        Threshold = threshold;
    }

    /// <summary>Which blackboard field this reads.</summary>
    public TriggerField Field { get; }

    /// <summary>Which way round it reads.</summary>
    public TriggerComparison Comparison { get; }

    /// <summary>The number the field is compared against.</summary>
    public float Threshold { get; }

    /// <summary>
    /// Whether <paramref name="blackboard"/> currently satisfies this clause.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="blackboard"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="Field"/> is a member with no case below — which means a field was added to the
    /// enum and not to the switch. Loud rather than false, for <c>EnemyBehaviourKind</c>'s reason
    /// (AR §18.4): a trigger that silently never held would ship as a skill that never auto-casts.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>A switch over a closed enum of code fields</b> — the <c>Stat.Pool</c> kind AR §13 permits,
    /// and not the <c>switch (effect.Type)</c> kind it bans. The distinction is that nothing is ever
    /// inserted here from content: <see cref="TriggerField"/> is the blackboard's own field list.
    /// </para>
    /// <para>
    /// <b>The int fields widen to <see cref="float"/> and <see cref="Threshold"/> does not narrow.</b>
    /// Six of the ten fields are counts and four are fractions; one <see cref="float"/> threshold
    /// says both, and every count this game can hold is exactly representable. Narrowing instead
    /// would have to decide what <em>"at least 3.5 enemies"</em> means, and either answer is a rule
    /// nobody authored.
    /// </para>
    /// <para>
    /// <b>A NaN field fails both comparisons, and that is deliberate rather than incidental.</b>
    /// Every comparison against NaN is false, so <c>value &lt; t</c> and <c>value &gt;= t</c> are
    /// both false and a broken blackboard casts nothing — where the tempting spelling of
    /// <see cref="TriggerComparison.AtLeast"/>, <c>!(value &lt; t)</c> as the negation of
    /// <see cref="TriggerComparison.Below"/>, would make a NaN <em>satisfy</em> every
    /// <c>AtLeast</c> clause the player owns and fire every active at once. The two comparisons are
    /// therefore written out rather than derived from each other. See AR §18.3, and the spec's
    /// <em>As built</em>.
    /// </para>
    /// <para>
    /// Allocates nothing: two jump tables and a compare. M3-06 asks this of every owned active on
    /// every tick.
    /// </para>
    /// </remarks>
    public bool IsMet(CombatBlackboard blackboard)
    {
        if (blackboard is null)
        {
            throw new ArgumentNullException(nameof(blackboard));
        }

        float value = Field switch
        {
            TriggerField.HpFraction => blackboard.HpFraction,
            TriggerField.ShieldFraction => blackboard.ShieldFraction,
            TriggerField.EnemiesWithin6m => blackboard.EnemiesWithin6m,
            TriggerField.EnemiesWithin8m => blackboard.EnemiesWithin8m,
            TriggerField.EnemiesInAcquireRange => blackboard.EnemiesInAcquireRange,
            TriggerField.IncomingProjectiles => blackboard.IncomingProjectiles,
            TriggerField.Veilrot => blackboard.Veilrot,
            TriggerField.StationaryTime => blackboard.StationaryTime,
            TriggerField.FocusRampLevel => blackboard.FocusRampLevel,
            TriggerField.MinionCount => blackboard.MinionCount,
            _ => throw new ArgumentOutOfRangeException(
                nameof(Field),
                Field,
                "No blackboard field is wired to this TriggerField. A member was added to the enum "
                    + "and not to the switch that reads it."),
        };

        return Comparison switch
        {
            TriggerComparison.Below => value < Threshold,
            TriggerComparison.AtLeast => value >= Threshold,
            _ => throw new ArgumentOutOfRangeException(
                nameof(Comparison),
                Comparison,
                "A trigger comparison must be Below or AtLeast."),
        };
    }
}

/// <summary>
/// CC §6.4's authored auto-cast condition: one or two <see cref="TriggerClause"/>s, all of which
/// must hold. Carried by an <see cref="ActiveSpec"/>; asked by M3-06's runner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two clauses, because CH §4.2's Rot Nova needs two</b> — <em>"Veilrot ≥ 50 <b>and</b> ≥ 4
/// enemies within 8 m"</em> — and one clause cannot say it. <b>Not three</b>, because CC §6.3
/// writes every condition out in plain language on the Skills screen, and a condition a designer
/// cannot read in one line is one a player cannot predict. Predictability is the whole of why Auto
/// feels deliberate rather than random (CH §4.2), so the cap is a design constraint rather than a
/// buffer size.
/// </para>
/// <para>
/// <b>Conjunction only.</b> There is no <c>Any</c> mode and no nesting: every condition either
/// design document names is an <em>and</em>, and an authored boolean algebra is the rules-engine UI
/// CC §6.4 explicitly refuses to ship. A skill that genuinely wants <em>or</em> is two skills or a
/// wider blackboard field.
/// </para>
/// <para>
/// Immutable and shared across every run, like every spec: the clause list is copied on
/// construction.
/// </para>
/// </remarks>
public sealed class TriggerSpec
{
    /// <summary>The most clauses one condition may hold. Two — see the remarks on this class.</summary>
    public const int MaxClauses = 2;

    /// <summary>
    /// The clauses as an array, for <see cref="IsMet"/>'s loop. <see cref="Clauses"/> hands out the
    /// wrapper.
    /// </summary>
    /// <remarks>
    /// Both, and not one, for <c>ModeSpec._roster</c>'s reason with a sharper edge: a
    /// <c>foreach</c> over an <see cref="IReadOnlyList{T}"/> boxes the enumerator on every call, and
    /// M3-06 calls <see cref="IsMet"/> per owned active per tick. Indexing the array costs neither
    /// the box nor an interface call. The wrapper exists so the public property cannot be cast back
    /// to the array and written through.
    /// </remarks>
    private readonly TriggerClause[] _clauses;

    private readonly ReadOnlyCollection<TriggerClause> _clausesView;

    /// <param name="clauses">
    /// One or two clauses, all of which must hold. Copied; the caller's list is not retained.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="clauses"/> is empty or holds more than <see cref="MaxClauses"/>. Empty is
    /// refused rather than read as "always" — an unconditional active is spelled by a clause that
    /// is always true, and a designer who left the field blank meant to type something.
    /// </exception>
    public TriggerSpec(IReadOnlyList<TriggerClause> clauses)
    {
        if (clauses is null)
        {
            throw new ArgumentNullException(nameof(clauses));
        }

        if (clauses.Count == 0)
        {
            throw new ArgumentException(
                "A trigger needs at least one clause. An empty condition is a blank field rather "
                    + "than a skill that always fires — spell that with a clause that always holds.",
                nameof(clauses));
        }

        if (clauses.Count > MaxClauses)
        {
            throw new ArgumentException(
                $"A trigger holds at most {MaxClauses} clauses and was given {clauses.Count}. "
                    + "CC §6.3 writes every condition out in plain language on the Skills screen, "
                    + "and a condition a designer cannot read in one line is one a player cannot "
                    + "predict.",
                nameof(clauses));
        }

        _clauses = new TriggerClause[clauses.Count];

        for (int i = 0; i < clauses.Count; i++)
        {
            _clauses[i] = clauses[i];
        }

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to TriggerClause[], and then the copy above
        // protects nothing. The same guard ContentCatalog and ModeSpec make.
        _clausesView = Array.AsReadOnly(_clauses);
    }

    /// <summary>The clauses, in the order they were authored.</summary>
    /// <remarks>
    /// Order is meaningful to CC §6.3's plain-language line and to nothing else — the conjunction
    /// is order-independent, so <see cref="IsMet"/> is free to answer on the first clause that
    /// fails.
    /// </remarks>
    public IReadOnlyList<TriggerClause> Clauses => _clausesView;

    /// <summary>
    /// Whether every clause holds against <paramref name="blackboard"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="blackboard"/> is null.</exception>
    /// <remarks>
    /// Allocates nothing — see <see cref="_clauses"/>. Short-circuits on the first clause that
    /// fails, which for the two-clause case is the whole of the optimisation available.
    /// </remarks>
    public bool IsMet(CombatBlackboard blackboard)
    {
        for (int i = 0; i < _clauses.Length; i++)
        {
            if (!_clauses[i].IsMet(blackboard))
            {
                return false;
            }
        }

        return true;
    }
}
