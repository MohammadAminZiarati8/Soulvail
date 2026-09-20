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
/// <b>Validated by <see cref="WeaponSpec"/> as of M5-01, which is a reversal of what this remark
/// used to say.</b> With one member there was nothing to get wrong and the loud place for an
/// unrecognised kind was whatever had to pick an attack for it. With two, the spec's own validation
/// is kind-conditional — a cone must not carry shot numbers and a shot must carry them — so an
/// undefined kind is one that satisfies neither set of rules and would be constructed with three
/// unchecked numbers on it. It is refused at the door instead.
/// </para>
/// </remarks>
public enum WeaponKind
{
    /// <summary>
    /// A wedge swept along the character's facing — the Censer of CC §4.1. Core asks the body to
    /// resolve the overlap and hits everything inside the arc for full damage.
    /// </summary>
    Cone,

    /// <summary>
    /// A shot that travels — the Gravecaller's Bone Bolt (M5-02). Core computes the arrival itself
    /// and asks the body nothing: the damage frame produces a <c>Projectile</c> aimed by
    /// <c>ProjectileLead</c> at where the target will be (CC §3.7), and everything inside
    /// <see cref="WeaponSpec.ShotRadius"/> of the impact takes full damage.
    /// </summary>
    Projectile,
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

    /// <param name="kind">
    /// What shape the attack takes. Which of the three shot numbers below are required, and which
    /// are refused, follows from this — see the exception list.
    /// </param>
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
    /// <para>
    /// On a <see cref="WeaponKind.Projectile"/> it must be exactly <see cref="MaxConeAngleDeg"/>.
    /// A bolt is not gated by an arc, and 360 is the one value that says so — anything narrower is a
    /// number nothing reads, which is the failure the kind-conditional validation exists to catch.
    /// </para>
    /// </param>
    /// <param name="damageFrame">
    /// How far through the swing the damage lands, as a fraction of the interval — 0.4 (CC §4.2).
    /// The windup that makes a swing readable without making it a commitment.
    /// </param>
    /// <param name="shotSpeed">
    /// Metres per second of flight. Required above zero on a <see cref="WeaponKind.Projectile"/> and
    /// refused on a <see cref="WeaponKind.Cone"/>.
    /// </param>
    /// <param name="shotRadius">
    /// Metres from the impact point that still count as a hit. Required above zero on a
    /// <see cref="WeaponKind.Projectile"/> and refused on a <see cref="WeaponKind.Cone"/>.
    /// </param>
    /// <param name="shotSpread">Reserved and required to be zero, whatever the kind — see <see cref="ShotSpread"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="damage"/>, <paramref name="swingsPerSecond"/> or <paramref name="range"/> is
    /// not a finite number greater than zero; <paramref name="coneAngleDeg"/> is outside
    /// <c>(0, 360]</c>; <paramref name="damageFrame"/> is outside <c>[0, 1)</c>;
    /// <paramref name="shotSpread"/> is not zero; <paramref name="kind"/> is not a defined
    /// <see cref="WeaponKind"/>; or the shot numbers disagree with the kind — see
    /// <paramref name="shotSpeed"/> and <paramref name="coneAngleDeg"/>.
    /// </exception>
    public WeaponSpec(
        WeaponKind kind,
        float damage,
        float swingsPerSecond,
        float range,
        float coneAngleDeg,
        float damageFrame,
        float shotSpeed = 0f,
        float shotRadius = 0f,
        float shotSpread = 0f)
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

        // Reserved and refused, whatever the kind — and refused before the kind is even looked at,
        // because a spread authored on a cone is the same forgotten field as one authored on a bolt.
        if (shotSpread != 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shotSpread),
                shotSpread,
                "shotSpread must be zero. The field is reserved: a spread is a change to what a "
                    + "seed means (ADR-0011), so the day one is wanted is a validation change here "
                    + "rather than a widening of this spec — and until then it must not become a "
                    + "number nobody draws for.");
        }

        ValidateShotNumbers(kind, coneAngleDeg, shotSpeed, shotRadius);

        Kind = kind;
        Damage = Positive(damage, nameof(damage));
        SwingsPerSecond = Positive(swingsPerSecond, nameof(swingsPerSecond));
        Range = Positive(range, nameof(range));
        ConeAngleDeg = coneAngleDeg;
        DamageFrame = damageFrame;
        ShotSpeed = shotSpeed;
        ShotRadius = shotRadius;
        ShotSpread = shotSpread;
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

    /// <summary>
    /// Metres per second of flight. Zero on a <see cref="WeaponKind.Cone"/>.
    /// </summary>
    /// <remarks>
    /// Not a <c>Stat</c> and not seeded into one, unlike <see cref="Damage"/> and
    /// <see cref="SwingsPerSecond"/>: with the range, this is the dodge window the shot gives
    /// whatever it is aimed at, and nothing in the design asks a node to move it. The day one does,
    /// it becomes a <c>Stat</c> on <c>Weapon</c> the way <see cref="Range"/> did at M3-12a.
    /// </remarks>
    public float ShotSpeed { get; }

    /// <summary>Metres from the impact point that still count. Zero on a cone.</summary>
    /// <remarks>
    /// A bolt is a small blast, which is what <c>ProjectileSystem.LandOnEnemies</c> makes of this
    /// number and what keeps one damage figure honest against an arc that hits a crowd.
    /// </remarks>
    public float ShotRadius { get; }

    /// <summary>Reserved and required to be zero.</summary>
    /// <remarks>
    /// <b>A decision about seeds rather than about aim.</b> <c>ProjectileSystem</c>'s own remarks
    /// say a spread "is a change to what a seed means (ADR-0011)" — a shot that scatters has to draw
    /// from a named stream, and which stream, in what order, is a reproducibility question rather
    /// than a feel one. The field exists so that the day a spread is wanted it is a change to the
    /// validation above and one call site, rather than a widening of this spec and its forty-odd
    /// callers; it is refused today so that it cannot first become a number nobody draws for.
    /// </remarks>
    public float ShotSpread { get; }

    /// <summary>
    /// Rule 2: which of the three shot numbers this kind requires, and which it refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both directions are validated, and the refusing direction is the one worth having.</b> A
    /// bolt with no speed never arrives and a bolt with no radius catches a mathematical point, so
    /// those are obvious. A <em>cone</em> carrying a shot speed is the interesting case: it is a
    /// weapon somebody authored as a projectile and left as a cone, or the reverse, and silently
    /// ignoring the number would ship a weapon that is not the one in the asset. The same argument
    /// covers a bolt with a 60° arc — <c>PlayerCombat</c> never reads an angle on a projectile
    /// weapon, so 60 there is a designer's intention going nowhere.
    /// </para>
    /// <para>
    /// 360 is required rather than merely allowed because there has to be exactly one spelling of
    /// "the angle does not gate this weapon". It is also the value <c>PlayerCombat.ConeAngle</c>
    /// already clamps a live angle down to, so the number a projectile weapon carries is the widest
    /// one the game has a meaning for rather than a sentinel invented here.
    /// </para>
    /// </remarks>
    private static void ValidateShotNumbers(
        WeaponKind kind,
        float coneAngleDeg,
        float shotSpeed,
        float shotRadius)
    {
        switch (kind)
        {
            case WeaponKind.Cone:
                RequireZero(shotSpeed, nameof(shotSpeed), kind);
                RequireZero(shotRadius, nameof(shotRadius), kind);
                return;

            case WeaponKind.Projectile:
                RequirePositive(shotSpeed, nameof(shotSpeed), kind);
                RequirePositive(shotRadius, nameof(shotRadius), kind);

                if (coneAngleDeg != MaxConeAngleDeg)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(coneAngleDeg),
                        coneAngleDeg,
                        $"coneAngleDeg must be exactly {MaxConeAngleDeg} on a {kind} weapon. An arc "
                            + "does not gate a shot, and a narrower angle here is a number nothing "
                            + "reads.");
                }

                return;

            default:
                // Unreachable from authored content, which selects from the enum — and reachable
                // from a cast integer, which is why the two branches above are a switch rather than
                // an if/else that would let one through with three unchecked numbers on it.
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "kind must be a defined WeaponKind. An undefined one satisfies neither set of "
                        + "shot-number rules, so nothing would have checked them.");
        }
    }

    /// <remarks>
    /// <see cref="Positive"/>'s test with <see cref="Positive"/>'s reasoning, and a message that
    /// names the kind as well as the field: an authored number that is wrong <em>for this kind</em>
    /// is the mistake worth diagnosing, and "shotSpeed must be greater than zero" on its own leaves a
    /// designer looking at a cone wondering why it is being asked.
    /// </remarks>
    private static void RequirePositive(float value, string paramName, WeaponKind kind)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero on a {kind} weapon. A shot "
                    + "with no speed never arrives, and one with no extent can only catch a "
                    + "mathematical point.");
        }
    }

    /// <remarks>
    /// Exactly zero, not "at most zero" and not "small": the field is absent rather than switched
    /// off, and a negative speed on a cone is as much a forgotten field as a positive one. NaN fails
    /// the equality and is refused with the rest.
    /// </remarks>
    private static void RequireZero(float value, string paramName, WeaponKind kind)
    {
        if (value != 0f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be zero on a {kind} weapon. A cone that carries one is a weapon "
                    + "somebody meant to author as a projectile, and ignoring the number would ship "
                    + "a weapon that is not the one in the asset.");
        }
    }

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
