using System;

namespace Soulvail.Core.Content;

/// <summary>
/// What every level gained is worth in this mode: the fraction of base maximum hit points and of
/// base weapon damage it adds (LV-01a). Pooled with every other percentage (ADR-0008), so level 30
/// at 0.03 / 0.02 is ×1.87 / ×1.58 rather than a compound.
/// </summary>
/// <remarks>
/// <b><c>default(LevelGrowthSpec)</c> is legal and means a level grows nothing</b> — the same
/// bargain as <see cref="EssenceSpec"/>: the invariant is "finite and not negative", and zero is
/// both, so AR §18.3's both-ends rule does not bite.
/// </remarks>
public readonly struct LevelGrowthSpec
{
    /// <param name="maxHp">Fraction of base max HP one level adds; 0.03 is +3 %. Zero is legal.</param>
    /// <param name="damage">Fraction of base weapon damage one level adds. Zero is legal.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either value is NaN, infinite or negative.</exception>
    public LevelGrowthSpec(float maxHp, float damage)
    {
        MaxHp = Require(maxHp, nameof(maxHp));
        Damage = Require(damage, nameof(damage));
    }

    /// <summary>The fraction one level adds to maximum hit points. 0.03 in the Jungle.</summary>
    public float MaxHp { get; }

    /// <summary>The fraction one level adds to weapon damage. 0.02 in the Jungle.</summary>
    public float Damage { get; }

    /// <summary>Whether a level grows anything at all — what the level-up toast asks.</summary>
    public bool Grows => MaxHp > 0f || Damage > 0f;

    private static float Require(float value, string field)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                field,
                value,
                $"A mode's level growth {field} must be a finite fraction, zero or more. It is "
                    + "added to a player stat as a PercentAdd modifier on every level gained, so a "
                    + "NaN poisons that stat for the run and a negative one punishes levelling.");
        }

        return value;
    }
}
