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
/// Three members as of M6-07a, which authors the class that carries the third. Each differs from a Charge in what happens along the path — a corpse decoy, a
/// teleport — rather than in the cooldown, buffer and i-frame window every one of them has, which is
/// why <see cref="MovementSkillSpec"/> holds the shared numbers and the kind selects the behaviour.
/// Deliberately unvalidated here: the loud place for an unrecognised kind is whatever has to build
/// a skill from it, which is the one site that knows the full set.
/// </para>
/// <para>
/// <b>A member may land a task before its behaviour does</b>, and <see cref="Shroudstep"/> is the
/// first that has: the kind is <em>content identity</em>, so it belongs to the asset that names it,
/// while what it does is a system. It landed at M5-02 and means something as of M5-03 — which is
/// also why the Gravecaller's numbers author 0 damage and 0 knockback rather than leaving CC §5's
/// defaults to be dealt by a blink (M5-02 rule 7). <b>Still no second skill class:</b>
/// <c>PlayerCombat</c> builds a <c>ChargeSkill</c> from the spec whatever the kind says, and the
/// kind selects a <em>payload</em> on the start edge (M5-03 rule 1). <see cref="Blink"/> was the
/// second member to land before its behaviour, at M6-07a, and its payload arrived at M6-07b.
/// </para>
/// </remarks>
public enum MovementSkillKind
{
    /// <summary>
    /// The Oathbound's Charge (CC §5): a 10 m dash that damages and knocks back everything it
    /// passes through, invulnerable for its whole duration and a little past it.
    /// </summary>
    Charge,

    /// <summary>
    /// The Gravecaller's Shroudstep (CH §3.2): a 6 m blink leaving a corpse decoy that taunts for
    /// <see cref="MovementSkillSpec.DecoyDuration"/> seconds. The clock, the buffer and the i-frame
    /// window are the Charge's; the corpse is the difference (M5-03).
    /// </summary>
    Shroudstep,

    /// <summary>
    /// The Emberwright's Blink (CH §3.3): an instant 10 m teleport leaving a fire pool. The clock, the
    /// buffer and the i-frame window are the Charge's; the pool is the difference (M6-07b), dropped
    /// where the blink left and sized by <see cref="MovementSkillSpec.PoolRadius"/> and its two
    /// siblings.
    /// </summary>
    Blink,
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
    /// <param name="decoyDuration">
    /// Seconds a <see cref="MovementSkillKind.Shroudstep"/>'s corpse decoy stands — 3 (CH §3.2).
    /// <b>Validated against <paramref name="kind"/> rather than on its own</b> (M5-03 rule 5), the
    /// way <see cref="WeaponSpec"/>'s three shot numbers are: a Shroudstep must carry a finite
    /// number greater than zero, and every other kind must carry exactly zero. A
    /// <see cref="MovementSkillKind.Charge"/> with a decoy duration is a forgotten field rather
    /// than a design statement, and a Shroudstep without one is a blink that leaves nothing —
    /// which is the whole of the skill.
    /// <para>
    /// Defaulted, so every <c>new MovementSkillSpec(...)</c> written before M5-03 keeps meaning
    /// what it meant and no shipped asset is rewritten (M4-01a rule 4's trade).
    /// </para>
    /// </param>
    /// <param name="poolRadius">
    /// How far a <see cref="MovementSkillKind.Blink"/>'s fire pool reaches, in metres — 3 (M6-07b
    /// rule 5). <b>Validated against <paramref name="kind"/></b>, <paramref name="decoyDuration"/>'s
    /// treatment one field over (rule 4): on a Blink all three pool numbers must be finite and
    /// greater than zero, and on every other kind all three must be exactly zero. Defaulted and last
    /// for the reason <paramref name="decoyDuration"/> is.
    /// </param>
    /// <param name="poolDuration">How long the pool burns, in simulated seconds — 3.</param>
    /// <param name="poolDamagePerPulse">What one pulse of it takes off — 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="distance"/>, <paramref name="duration"/> or <paramref name="cooldown"/> is
    /// not a finite number greater than zero; <paramref name="inputBuffer"/>,
    /// <paramref name="damage"/>, <paramref name="knockback"/> or <paramref name="iFrameTrail"/> is
    /// negative, NaN or infinite; or <paramref name="decoyDuration"/> or one of the three pool
    /// numbers disagrees with <paramref name="kind"/>.
    /// </exception>
    public MovementSkillSpec(
        MovementSkillKind kind,
        float distance,
        float duration,
        float cooldown,
        float inputBuffer,
        float damage,
        float knockback,
        float iFrameTrail,
        float decoyDuration = 0f,
        float poolRadius = 0f,
        float poolDuration = 0f,
        float poolDamagePerPulse = 0f)
    {
        Kind = kind;
        Distance = Positive(distance, nameof(distance));
        Duration = Positive(duration, nameof(duration));
        Cooldown = Positive(cooldown, nameof(cooldown));
        InputBuffer = NonNegative(inputBuffer, nameof(inputBuffer));
        Damage = NonNegative(damage, nameof(damage));
        Knockback = NonNegative(knockback, nameof(knockback));
        IFrameTrail = NonNegative(iFrameTrail, nameof(iFrameTrail));
        DecoyDuration = Decoy(kind, decoyDuration);
        PoolRadius = Pool(kind, poolRadius, nameof(poolRadius));
        PoolDuration = Pool(kind, poolDuration, nameof(poolDuration));
        PoolDamagePerPulse = Pool(kind, poolDamagePerPulse, nameof(poolDamagePerPulse));
    }

    /// <summary>Simulated seconds between a pool's pulses. Half a second, and a constant.</summary>
    /// <remarks>
    /// <b>Not authored, and that is a decision about events rather than about feel</b> (M6-07b rule
    /// 5). It is the one number that decides how many <c>ZoneBurned</c>s a second reach the hub, and a
    /// pool authored at a millisecond is <c>ZoneSystem.MaxPulses</c>' hang, caught only after somebody
    /// typed it. It is Consecrate's interval, so the game's two zones pulse on one beat.
    /// </remarks>
    public const float PoolPulseInterval = 0.5f;

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

    /// <summary>
    /// Seconds a Shroudstep's decoy stands. Zero on a movement skill that leaves none.
    /// </summary>
    /// <remarks>
    /// 3 for the Gravecaller (CH §3.2) against a 2.5 s cooldown, which is why
    /// <c>LureSystem.Capacity</c> is two rather than one: the overlap is half a second wide and it
    /// is legitimate. Read once per blink by <c>PlayerCombat.TickCharge</c>, which is the only
    /// caller — the decoy is dropped where the blink left, not where it arrived (M5-03 rule 6).
    /// </remarks>
    public float DecoyDuration { get; }

    /// <summary>How far a <see cref="MovementSkillKind.Blink"/>'s fire pool reaches. 0 otherwise.</summary>
    /// <remarks>
    /// Like the other two pool numbers, the live value is a <c>Stat</c> on <c>ChargeSkill</c> seeded
    /// from this one (M6-07b rule 11), and the pool is dropped where the blink left.
    /// </remarks>
    public float PoolRadius { get; }

    /// <summary>How long it burns, in simulated seconds. 0 otherwise.</summary>
    public float PoolDuration { get; }

    /// <summary>What one pulse of it takes off. 0 otherwise.</summary>
    /// <remarks>
    /// The interval is <see cref="PoolPulseInterval"/> and is not authored. Three fields rather than
    /// four, because the one number a designer never has an opinion about is the one that decides
    /// how many events a second the pool publishes.
    /// </remarks>
    public float PoolDamagePerPulse { get; }

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

    /// <summary>
    /// The one number on this spec whose legal range depends on <see cref="Kind"/> — M5-03 rule 5,
    /// and <c>WeaponSpec</c>'s kind-conditional shot numbers one folder over.
    /// </summary>
    /// <remarks>
    /// Written as "the kind that leaves something behind, and everything else", rather than as a
    /// <c>switch</c> over every member: the third kind is <see cref="MovementSkillKind.Blink"/>
    /// (M6-07a), which leaves a fire pool and not a decoy, so it stays on the zero side of this line
    /// — M6-07b kept it there, and gave the pool its own three numbers in <see cref="Pool"/>. A kind
    /// added without a thought about this field therefore refuses a duration rather than silently
    /// accepting one nothing reads.
    /// </remarks>
    private static float Decoy(MovementSkillKind kind, float decoyDuration)
    {
        if (kind == MovementSkillKind.Shroudstep)
        {
            // `!(value > 0f)` so NaN is refused with everything at or below zero, and infinity
            // separately because it passes a `> 0` test — a decoy that never rots is a permanent
            // taunt bought with one blink.
            if (!(decoyDuration > 0f) || float.IsInfinity(decoyDuration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(decoyDuration),
                    decoyDuration,
                    "A Shroudstep's decoyDuration must be a finite number greater than zero. The "
                        + "corpse is the skill (CH §3.2); a blink that leaves nothing is a Charge "
                        + "with a shorter distance.");
            }

            return decoyDuration;
        }

        if (decoyDuration != 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decoyDuration),
                decoyDuration,
                $"A {kind} leaves no decoy, so its decoyDuration must be exactly zero. A non-zero "
                    + "one is a forgotten field rather than a design statement — nothing would "
                    + "ever read it.");
        }

        return 0f;
    }

    /// <summary>
    /// One of the three pool numbers, validated against <see cref="Kind"/> — M6-07b rule 4, and
    /// <see cref="Decoy"/>'s line with <see cref="MovementSkillKind.Blink"/> on the other side of it.
    /// </summary>
    /// <remarks>
    /// A Charge with a pool radius is a forgotten field, and a Blink without one is a teleport, which
    /// CH §3.3 says it is not. All three are asked the same question, so the one that is wrong is
    /// named by the exception rather than inferred.
    /// </remarks>
    private static float Pool(MovementSkillKind kind, float value, string paramName)
    {
        if (kind == MovementSkillKind.Blink)
        {
            // The same spelling as Decoy's: NaN fails `> 0`, infinity is asked separately — an
            // infinite radius burns the arena, an infinite duration never retires.
            if (!(value > 0f) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    value,
                    $"A Blink's {paramName} must be a finite number greater than zero. The fire pool "
                        + "is the skill (CH §3.3); a blink that leaves nothing is a teleport.");
            }

            return value;
        }

        if (value != 0f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"A {kind} leaves no fire pool, so its {paramName} must be exactly zero. A non-zero "
                    + "one is a forgotten field rather than a design statement — nothing would ever "
                    + "read it.");
        }

        return 0f;
    }
}
