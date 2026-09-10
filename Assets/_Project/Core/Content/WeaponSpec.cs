using System;

namespace Soulvail.Core.Content;

/// <summary>
/// What shape a weapon's attack takes. Selected by authored data and dispatched once, when the
/// weapon decides how to ask the body for hits.
/// </summary>
/// <remarks>
/// A closed set, like <see cref="EnemyBehaviourKind"/> and for the same reason: nothing is ever
/// inserted into this from content, and a weapon is named by its owning
/// <see cref="CharacterSpec.Id"/> rather than by an ordinal.
/// <para>
/// One member in V1. <c>Projectile</c> arrives with M5-01's Gravecaller, which needs a travel time
/// and a leading seam (CC §3.7) that a cone has no use for. Deliberately unvalidated by
/// <see cref="WeaponSpec"/>: the loud place for an unrecognised kind is whatever has to pick an
/// attack for it, which is the one site that knows the full set.
/// </para>
/// </remarks>
public enum WeaponKind
{
    /// <summary>
    /// A wedge swept along the character's facing — the Censer of CC §4.1. Core asks the body to
    /// resolve the overlap and hits everything inside the arc for full damage.
    /// </summary>
    Cone,
}

/// <summary>
/// A class's basic attack, as authored data: what it hits, how hard, how often, and how far
/// through the swing the damage lands. CC §4.1 and CC §7's Attack table entire. Converted once at
/// boot from a <c>CharacterDefinition</c> and carried on <see cref="CharacterSpec.Weapon"/>. See
/// AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and shared, for the reason <see cref="TargetingSpec"/> gives: this is what a designer
/// typed. The live numbers are <c>Weapon.Damage</c> and <c>Weapon.FireRate</c>, two
/// <c>Stat</c>s seeded from <see cref="Damage"/> and <see cref="SwingsPerSecond"/>, and every
/// "+15 % damage" in the game lands on those rather than here (ADR-0008).
/// </para>
/// <para>
/// <b>Every class has one.</b> Like <see cref="TargetingSpec"/> and unlike <see cref="ShieldSpec"/>,
/// there is no such thing as a class without a basic attack — CC §4 gives all three one, with no
/// ammunition, no reload and no cost — so <see cref="CharacterSpec"/> takes it as a required
/// argument. A missing weapon is a forgotten field, never a design statement.
/// </para>
/// <para>
/// The Oathbound's values are CC §7's: 13 damage, 3.0 swings per second, 8 m, 60°, damage at 0.4
/// of the swing. 13 × 3 = 39 damage per second against a 36 HP Husk, which is the third damage
/// frame — the TTK invariant of GD §6.2, and the number to check first when any of these move.
/// </para>
/// </remarks>
public sealed class WeaponSpec
{
    /// <summary>A full circle. <see cref="ConeAngleDeg"/> is the whole arc, not the half-angle.</summary>
    private const float MaxConeAngleDeg = 360f;

    /// <param name="kind">What shape the attack takes. <see cref="WeaponKind.Cone"/> in V1.</param>
    /// <param name="damage">
    /// Damage one swing deals to <em>every</em> enemy in the arc — 13 (CC §4.1). Not divided
    /// between them: arc coverage is what the Oathbound's low single-target number buys.
    /// </param>
    /// <param name="swingsPerSecond">Swings per second at rest — 3.0, a 0.333 s interval.</param>
    /// <param name="range">How far the arc reaches, in metres — 8.</param>
    /// <param name="coneAngleDeg">
    /// The full opening angle of the arc, in degrees — 60, so ±30° either side of facing. Written
    /// as the full angle because that is the number CC §4.1 publishes and the one a designer
    /// pictures; the halving belongs to whatever tests a direction against it.
    /// </param>
    /// <param name="damageFrame">
    /// How far through the swing the damage lands, as a fraction of the interval — 0.4 (CC §4.2).
    /// The windup that makes a swing readable without making it a commitment.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="damage"/>, <paramref name="swingsPerSecond"/> or <paramref name="range"/> is
    /// not a finite number greater than zero; <paramref name="coneAngleDeg"/> is outside
    /// <c>(0, 360]</c>; or <paramref name="damageFrame"/> is outside <c>[0, 1)</c>.
    /// </exception>
    public WeaponSpec(
        WeaponKind kind,
        float damage,
        float swingsPerSecond,
        float range,
        float coneAngleDeg,
        float damageFrame)
    {
        // `!(x > 0f)` throughout rather than `x <= 0f`, so NaN is refused with everything else —
        // the spelling every spec in this folder uses, for the reason TargetingSpec.Positive gives.
        if (!(coneAngleDeg > 0f) || coneAngleDeg > MaxConeAngleDeg)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coneAngleDeg),
                coneAngleDeg,
                $"coneAngleDeg must be greater than zero and at most {MaxConeAngleDeg} — it is the "
                    + "full opening angle. A zero arc hits nothing however well the swing is aimed, "
                    + "and anything past a full circle is a second lap that cannot hit twice.");
        }

        // Zero is legal: a weapon that lands its damage on the first frame of the swing is one with
        // no windup at all. One is not, and the exclusion is what keeps the frame *inside* the
        // swing — at exactly one it would land on the tick the swing ends, so a fire-rate modifier
        // could reorder a hit and the swing that replaces it.
        if (!(damageFrame >= 0f) || damageFrame >= 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(damageFrame),
                damageFrame,
                "damageFrame must be a fraction of the swing in [0, 1). It is where in the swing "
                    + "the damage lands, so it has to land before the swing is over.");
        }

        Kind = kind;
        Damage = Positive(damage, nameof(damage));
        SwingsPerSecond = Positive(swingsPerSecond, nameof(swingsPerSecond));
        Range = Positive(range, nameof(range));
        ConeAngleDeg = coneAngleDeg;
        DamageFrame = damageFrame;
    }

    /// <summary>What shape the attack takes.</summary>
    public WeaponKind Kind { get; }

    /// <summary>Damage one swing deals to every enemy in the arc. 13 for the Censer.</summary>
    public float Damage { get; }

    /// <summary>Swings per second at rest. 3.0 for the Censer.</summary>
    public float SwingsPerSecond { get; }

    /// <summary>How far the arc reaches, in metres. 8 for the Censer.</summary>
    public float Range { get; }

    /// <summary>The full opening angle of the arc, in degrees. 60 for the Censer.</summary>
    public float ConeAngleDeg { get; }

    /// <summary>Where in the swing the damage lands, as a fraction of the interval. 0.4.</summary>
    public float DamageFrame { get; }

    /// <remarks>
    /// Infinity is asked about separately because it passes a <c>&gt; 0</c> test: an infinite
    /// <see cref="Damage"/> kills everything the arc touches whatever its health, an infinite
    /// <see cref="SwingsPerSecond"/> is a swing interval of zero, and an infinite
    /// <see cref="Range"/> is a melee weapon that reaches across the arena.
    /// </remarks>
    private static float Positive(float value, string paramName)
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
}
