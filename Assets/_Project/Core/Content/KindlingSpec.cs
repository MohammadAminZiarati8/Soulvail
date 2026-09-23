using System;

namespace Soulvail.Core.Content;

/// <summary>
/// CH §3.3's Kindling, as authored data: how much one hit is worth, and how many count. Null on a
/// class without a stacking signature, which is every class but the Emberwright.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional on <see cref="CharacterSpec"/> for <see cref="ShieldSpec"/>'s reason, and it is the
/// same argument rather than a second one</b> (M5-02 rule 6): a zeroed block would have to be read
/// against the class id to be understood, and a <see langword="null"/> says <em>this class has no
/// signature of this kind</em> in one word.
/// </para>
/// <para>
/// Immutable and shared, for the reason <see cref="WeaponSpec"/> gives: this is what a designer
/// typed. The live numbers are <c>Kindling.PerStack</c> and <c>Kindling.MaxStacks</c>, two
/// <c>Stat</c>s seeded from these, and every <em>"+1 % a stack"</em> lands there (ADR-0008,
/// M6-07a rule 4).
/// </para>
/// </remarks>
public sealed class KindlingSpec
{
    /// <param name="perStack">
    /// Fractional weapon damage one stack is worth — 0.02 (CH §3.3). Finite and above zero.
    /// </param>
    /// <param name="maxStacks">
    /// How many stacks the ramp holds — 30, which is CH §3.3's +60 % divided by
    /// <paramref name="perStack"/>. Above zero. <b>The cap is authored as a count rather than as a
    /// ceiling fraction</b>, because a count is what the HUD would draw and what a node adds to; the
    /// fraction is the product, and <see cref="MaxBonus"/> is where it is computed once.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="perStack"/> is not a finite number greater than zero, or
    /// <paramref name="maxStacks"/> is below 1.
    /// </exception>
    public KindlingSpec(float perStack, int maxStacks)
    {
        // `!(x > 0f)` so NaN is refused with everything at or below zero, and infinity separately
        // because it passes a `> 0` test — one infinite stack is a weapon that kills anything.
        if (!(perStack > 0f) || float.IsInfinity(perStack))
        {
            throw new ArgumentOutOfRangeException(
                nameof(perStack),
                perStack,
                "perStack must be a finite number greater than zero. A stack worth nothing is a "
                    + "class with no signature, which is better said by passing no KindlingSpec.");
        }

        if (maxStacks < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStacks),
                maxStacks,
                "maxStacks must be at least 1. A ramp that holds nothing never starts, which is "
                    + "better said by passing no KindlingSpec.");
        }

        PerStack = perStack;
        MaxStacks = maxStacks;
        MaxBonus = perStack * maxStacks;
    }

    /// <summary>Fractional weapon damage one stack is worth. 0.02 (CH §3.3).</summary>
    public float PerStack { get; }

    /// <summary>How many stacks the ramp holds. 30 (CH §3.3's +60 %).</summary>
    public int MaxStacks { get; }

    /// <summary>What a full ramp is worth as a fraction — 0.60 for the shipped block.</summary>
    public float MaxBonus { get; }
}
