using System;
using Soulvail.Core.Run;

namespace Soulvail.Core.Content;

/// <summary>
/// CH §3's Veilrot column, as authored data: what GD §10's meter does differently for this class.
/// Null on a class the Veil treats ordinarily.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five neutral dials and no kind enum</b>, which is M6-06a rule 2's shape and its argument: three
/// classes turn three different dials, and the version that reads them with a
/// <c>switch (character.Id)</c> is ADR-0009's ban with the word <em>effect</em> removed. A class that
/// leaves a dial neutral costs nothing, which is what <see cref="ScalingSpec"/> and
/// <see cref="OverflowSpec"/> already look like.
/// </para>
/// <para>
/// <b>No dial scales a Pact's effects</b> (M6-07c). CH §3.1's third Oathbound clause — <em>"Pact
/// effects are 25 % weaker for him"</em> — is the multiplication M6-05a refused, and here it would
/// also turn a Pact's authored downside into a discount. Refused in writing; it is a parking-lot line
/// beside GD §13.2's table-versus-examples contradiction, and M7-04's to promote.
/// </para>
/// </remarks>
public sealed class VeilrotSpec
{
    /// <param name="startingVeilrot">
    /// What a fresh run of this class opens at, in [0, <see cref="Veilrot.Max"/>). The Gravecaller's
    /// 15 (CH §3.2). <b>Below <c>Max</c> rather than at or below it</b>: a class that started Claimed
    /// would begin every run on a hundred-second clock, which is a different game.
    /// </param>
    /// <param name="gainMultiplier">
    /// What every <c>Gain</c> is multiplied by. The Gravecaller's 1.5, the Oathbound's 0.6. Finite
    /// and above zero — zero would be a class Pacts are free for, which is the temptation removed
    /// rather than resisted.
    /// </param>
    /// <param name="cleansePriceMultiplier">
    /// What GD §13.3's Cleanse costs, times this. The Oathbound's 0.5 (CH §3.1). Finite and above
    /// zero.
    /// </param>
    /// <param name="damagePerPoint">
    /// Fractional weapon damage per point on the meter. The Gravecaller's 0.01 (CH §3.2). Finite and
    /// not negative.
    /// </param>
    /// <param name="instantCastCost">
    /// What one cast through a cooldown costs, or <b>0 for a class that cannot buy one</b>. The
    /// Emberwright's 5 (CH §3.3). Finite and not negative.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any of the five is outside its range.</exception>
    /// <exception cref="ArgumentException">
    /// All five are neutral, which is a relationship that is no relationship — M6-06a rule 3's
    /// refusal, and the reason a class without one authors <see langword="null"/> instead.
    /// </exception>
    public VeilrotSpec(
        float startingVeilrot = 0f,
        float gainMultiplier = 1f,
        float cleansePriceMultiplier = 1f,
        float damagePerPoint = 0f,
        float instantCastCost = 0f)
    {
        // `!(x >= 0f)` so NaN is refused with the negatives; the upper bound is strict — see the param.
        if (!(startingVeilrot >= 0f) || !(startingVeilrot < Veilrot.Max))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingVeilrot),
                startingVeilrot,
                $"startingVeilrot must be at least 0 and below {Veilrot.Max}. A class that opened "
                    + "Claimed would start every run on a hundred-second clock.");
        }

        RequirePositive(gainMultiplier, nameof(gainMultiplier));
        RequirePositive(cleansePriceMultiplier, nameof(cleansePriceMultiplier));
        RequireNotNegative(damagePerPoint, nameof(damagePerPoint));
        RequireNotNegative(instantCastCost, nameof(instantCastCost));

        if (startingVeilrot == 0f
            && gainMultiplier == 1f
            && cleansePriceMultiplier == 1f
            && damagePerPoint == 0f
            && instantCastCost == 0f)
        {
            throw new ArgumentException(
                "All five dials are neutral, which is a relationship that says nothing. A class the "
                    + "Veil treats ordinarily authors no VeilrotSpec (M6-06a rule 3's refusal).");
        }

        StartingVeilrot = startingVeilrot;
        GainMultiplier = gainMultiplier;
        CleansePriceMultiplier = cleansePriceMultiplier;
        DamagePerPoint = damagePerPoint;
        InstantCastCost = instantCastCost;
    }

    /// <summary>What a fresh run opens at. The Gravecaller's 15.</summary>
    public float StartingVeilrot { get; }

    /// <summary>What every gain is multiplied by. The Oathbound's 0.6, the Gravecaller's 1.5.</summary>
    public float GainMultiplier { get; }

    /// <summary>What the Cleanse's price is multiplied by. The Oathbound's 0.5.</summary>
    public float CleansePriceMultiplier { get; }

    /// <summary>Fractional weapon damage per point on the meter. The Gravecaller's 0.01.</summary>
    public float DamagePerPoint { get; }

    /// <summary>What one cast through a cooldown costs, or 0. The Emberwright's 5.</summary>
    public float InstantCastCost { get; }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number above zero.");
        }
    }

    private static void RequireNotNegative(float value, string name)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite number, zero or more.");
        }
    }
}
