using System;

namespace Soulvail.Core.Content;

/// <summary>
/// Which movement skill a class carries. Selected by authored data and dispatched once, when
/// something has to decide what kind of dash it is building.
/// </summary>
/// <remarks>
/// A closed set, like <see cref="WeaponKind"/> and for the same reason: nothing is ever inserted
/// into this from content, and a movement skill is named by its owning
/// <see cref="CharacterSpec.Id"/> rather than by an ordinal.
/// <para>
/// One member in V1. <c>Shroudstep</c> arrives with M5-03 and <c>Blink</c> with M6-07; both differ
/// from a Charge in what happens along the path — a corpse decoy, a teleport — rather than in the
/// cooldown, buffer and i-frame window every one of them has, which is why
/// <see cref="MovementSkillSpec"/> holds the shared numbers and the kind selects the behaviour.
/// Deliberately unvalidated here: the loud place for an unrecognised kind is whatever has to build
/// a skill from it, which is the one site that knows the full set.
/// </para>
/// </remarks>
public enum MovementSkillKind
{
    /// <summary>
    /// The Oathbound's Charge (CC §5): a 10 m dash that damages and knocks back everything it
    /// passes through, invulnerable for its whole duration and a little past it.
    /// </summary>
    Charge,
}

/// <summary>
/// A class's movement skill, as authored data: how far it goes, how long it takes, how often it may
/// be used, and what it does to whatever is in the way. CC §5 and CC §7's Charge table entire.
/// Converted once at boot from a <c>CharacterDefinition</c> and carried on
/// <see cref="CharacterSpec.MovementSkill"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and shared, for the reason <see cref="WeaponSpec"/> gives: this is what a designer
/// typed. The live number is <c>ChargeSkill.Cooldown</c>, a <c>Stat</c> seeded from
/// <see cref="Cooldown"/>, and every "−15 % dash cooldown" in the game lands on that rather than
/// here (ADR-0008).
/// </para>
/// <para>
/// <b>Every class has one.</b> Like <see cref="WeaponSpec"/> and <see cref="TargetingSpec"/> and
/// unlike <see cref="ShieldSpec"/>, there is no such thing as a class without a movement skill —
/// CC §5 opens with "every class has exactly one, on a permanent button" — so
/// <see cref="CharacterSpec"/> takes it as a required argument. A missing one is a forgotten field,
/// never a design statement, and it would produce a character with a dead button.
/// </para>
/// <para>
/// <b>Two of these numbers exist only to absorb touch latency</b>, and they are the two most likely
/// to be mistaken for padding: <see cref="IFrameTrail"/> and <see cref="InputBuffer"/>. CC §5 is
/// explicit that without them the dodge feels unreliable, and an unreliable dodge in a game built on
/// dodging is fatal. Neither is a tuning convenience — they are the mechanic surviving a 60-100 ms
/// round trip from glass to simulation.
/// </para>
/// <para>
/// The Oathbound's values are CC §7's: 10 m over 0.22 s (≈45 m/s), i-frames for the duration plus
/// 0.05 s, a 2.5 s cooldown, 20 damage and 5 m of knockback to everything passed through, and a
/// 0.15 s input buffer.
/// </para>
/// </remarks>
public sealed class MovementSkillSpec
{
    /// <param name="kind">Which movement skill this is. <see cref="MovementSkillKind.Charge"/> in V1.</param>
    /// <param name="distance">How far the dash travels, in metres — 10 (CC §5).</param>
    /// <param name="duration">
    /// Seconds the dash takes — 0.22, which is the 10 m at roughly 45 m/s. Must be greater than
    /// zero: this is the window the motor is suspended for and pass-through hits are resolved in
    /// (M1-15), so a duration of zero is a dash that never happens.
    /// </param>
    /// <param name="cooldown">
    /// Seconds before the skill may be used again, measured from the moment it starts — 2.5. Must be
    /// greater than zero, because the cooldown is the whole of CC §5's commitment: a movement skill
    /// with no cooldown is not a skill, it is free movement, and it would make the dash strictly
    /// better than walking. A node that wants it shorter applies a modifier to the <c>Stat</c> this
    /// seeds, under M3-06's 40 % floor.
    /// </param>
    /// <param name="inputBuffer">
    /// Seconds a press stays live while the skill is unavailable — 0.15, so a tap a seventh of a
    /// second before the cooldown ends still fires (CC §5). Zero is legal and means no buffering at
    /// all: the press must arrive on a tick where the dash is already possible, which on a
    /// touchscreen is a dodge that silently eats inputs.
    /// </param>
    /// <param name="damage">
    /// Damage dealt to <em>everything</em> the dash passes through — 20 (CC §5). Zero is legal and
    /// is what a movement skill that only repositions authors; M5-03's Shroudstep is the first.
    /// </param>
    /// <param name="knockback">
    /// How far, in metres, each thing hit is pushed — 5. Zero is legal, for the reason
    /// <paramref name="damage"/> is.
    /// </param>
    /// <param name="iFrameTrail">
    /// Extra seconds of invulnerability after the dash ends — 0.05 (CC §5). Zero is legal and means
    /// i-frames stop exactly when the dash does, which is the version CC §5 says feels unreliable:
    /// the trail is there so that a dodge which visually cleared an attack is not undone by the
    /// frames between the input and the simulation.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="distance"/>, <paramref name="duration"/> or <paramref name="cooldown"/> is
    /// not a finite number greater than zero; or <paramref name="inputBuffer"/>,
    /// <paramref name="damage"/>, <paramref name="knockback"/> or <paramref name="iFrameTrail"/> is
    /// negative, NaN or infinite.
    /// </exception>
    public MovementSkillSpec(
        MovementSkillKind kind,
        float distance,
        float duration,
        float cooldown,
        float inputBuffer,
        float damage,
        float knockback,
        float iFrameTrail)
    {
        Kind = kind;
        Distance = Positive(distance, nameof(distance));
        Duration = Positive(duration, nameof(duration));
        Cooldown = Positive(cooldown, nameof(cooldown));
        InputBuffer = NonNegative(inputBuffer, nameof(inputBuffer));
        Damage = NonNegative(damage, nameof(damage));
        Knockback = NonNegative(knockback, nameof(knockback));
        IFrameTrail = NonNegative(iFrameTrail, nameof(iFrameTrail));
    }

    /// <summary>Which movement skill this is.</summary>
    public MovementSkillKind Kind { get; }

    /// <summary>How far the dash travels, in metres. 10 for the Charge.</summary>
    public float Distance { get; }

    /// <summary>Seconds the dash takes. 0.22 for the Charge.</summary>
    public float Duration { get; }

    /// <summary>Seconds before the skill may be used again, from the moment it starts. 2.5.</summary>
    public float Cooldown { get; }

    /// <summary>Seconds a press stays live while the skill is unavailable. 0.15.</summary>
    public float InputBuffer { get; }

    /// <summary>Damage dealt to everything the dash passes through. 20 for the Charge.</summary>
    public float Damage { get; }

    /// <summary>How far each thing hit is pushed, in metres. 5 for the Charge.</summary>
    public float Knockback { get; }

    /// <summary>Extra seconds of invulnerability after the dash ends. 0.05.</summary>
    public float IFrameTrail { get; }

    /// <remarks>
    /// `!(x &gt; 0f)` rather than `x &lt;= 0f`, so NaN is refused with everything else — the
    /// spelling every spec in this folder uses, for the reason <c>TargetingSpec.Positive</c> gives.
    /// Infinity is asked about separately because it passes a <c>&gt; 0</c> test: an infinite
    /// <see cref="Distance"/> is a dash across the arena, an infinite <see cref="Duration"/> is one
    /// that never ends — leaving the character invulnerable and the motor suspended for the rest of
    /// the run — and an infinite <see cref="Cooldown"/> is a button that works once.
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

    /// <remarks>
    /// The same spelling one rung looser, for the four numbers where zero is a design statement
    /// rather than a mistake. Infinity is still refused: an infinite <see cref="IFrameTrail"/> is
    /// permanent invulnerability bought with one dash, and an infinite <see cref="InputBuffer"/> is
    /// a press that fires whenever the cooldown next ends, however long ago it was made.
    /// </remarks>
    private static float NonNegative(float value, string paramName)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number, zero or more.");
        }

        return value;
    }
}
