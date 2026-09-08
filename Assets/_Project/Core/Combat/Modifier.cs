using System;

namespace Soulvail.Core.Combat;

/// <summary>
/// How a <see cref="Modifier"/> combines with the rest of a <see cref="Stat"/>'s stack. See
/// AR §11.1 and ADR-0008 for the order and why it is fixed.
/// </summary>
/// <remarks>
/// One of the few enums this project allows, and it qualifies for the reason ADR-0010 gives: a
/// closed set. These three are arithmetic, not content — a modifier kind is never authored,
/// saved or referenced by id, and a fourth would mean the arithmetic itself had changed.
/// </remarks>
public enum ModifierKind
{
    /// <summary>
    /// Added to the base before any percentage applies, in whatever units the stat measures.
    /// </summary>
    Flat,

    /// <summary>
    /// Pooled with every other <see cref="PercentAdd"/> into a single factor: two +50 %
    /// modifiers are ×2.0 together, not ×2.25. The kind almost every node, Pact and affix
    /// wants, because pooled stacking stays linear — the tenth source is worth what the first
    /// was, so nothing exponentiates behind the designer's back.
    /// </summary>
    PercentAdd,

    /// <summary>
    /// Its own multiplicative factor, applied after the pooled one. For the rare, loud
    /// multipliers — Focus at full ramp, a boss phase, the Claiming — because each one scales
    /// everything that came before it, including the others.
    /// </summary>
    PercentMult,
}

/// <summary>
/// One source's contribution to a <see cref="Stat"/>: what kind of change, how much, and who
/// asked for it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Source"/> is the whole reason this is not three floats on the stat. It is what
/// lets a buff be taken back when it ends (<see cref="Stat.RemoveAll"/>) without knowing what
/// else has been added since, and what lets a debug panel answer "why is my damage 47?" — see
/// ADR-0008.
/// </para>
/// <para>
/// A <see langword="readonly"/> struct, so a stat's stack is a flat list of values rather than
/// a list of references to chase, and passing one costs nothing.
/// </para>
/// </remarks>
public readonly struct Modifier
{
    /// <summary>Which stack position this modifier occupies.</summary>
    public readonly ModifierKind Kind;

    /// <summary>
    /// The amount, read according to <see cref="Kind"/>: units for
    /// <see cref="ModifierKind.Flat"/>, a fraction for either percentage kind.
    /// </summary>
    public readonly float Value;

    /// <summary>
    /// Who applied it. Never null on a constructed modifier — but see <see cref="Stat.Add"/>,
    /// because <c>default(Modifier)</c> exists and cannot be stopped from existing.
    /// </summary>
    public readonly object Source;

    /// <param name="kind">Which of the three stack positions this modifier occupies.</param>
    /// <param name="value">
    /// <see cref="ModifierKind.Flat"/>: units. <see cref="ModifierKind.PercentAdd"/>: 0.15 is
    /// +15 %. <see cref="ModifierKind.PercentMult"/>: 0.2 is ×1.2.
    /// </param>
    /// <param name="source">
    /// Who is applying it — a node, a Pact, a status, the tracker that owns a ramp. Identity
    /// only: it is compared by reference and never read, so any object will do, as long as the
    /// same instance is still in hand when the modifier has to come off again.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> is null. A modifier with no source could never be removed, so
    /// it would be a permanent change to the stat with nothing to point at.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of the three, or <paramref name="value"/> is NaN or
    /// infinite — see the remarks on <see cref="Stat.Value"/> for why a non-finite value is
    /// refused at the door rather than left to the caller to clamp.
    /// </exception>
    public Modifier(ModifierKind kind, float value, object source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (kind is not (ModifierKind.Flat or ModifierKind.PercentAdd or ModifierKind.PercentMult))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Modifier kind must be one of Flat, PercentAdd or PercentMult.");
        }

        // Asked as "is it NaN or infinite?" rather than as a range comparison, for the reason
        // MovementSpec documents: every comparison against NaN is false, so the natural
        // spelling of a range check waves it straight through.
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Modifier value must be finite. NaN or infinity spreads to the stat's value and "
                    + "silences its Changed event, which compares two numbers that no longer "
                    + "compare.");
        }

        Kind = kind;
        Value = value;
        Source = source;
    }
}
