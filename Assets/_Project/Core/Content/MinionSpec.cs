using System;

namespace Soulvail.Core.Content;

/// <summary>
/// A class's minions, as authored data: how many, for how long, how often a kill makes one, and
/// what one is. CH §3.2's Rise entire. Converted once at boot from a <c>CharacterDefinition</c> and
/// carried on <see cref="CharacterSpec.Minions"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// <b>Null on a class that has none</b> — <see cref="ShieldSpec"/>'s rule, and
/// <see cref="ProjectileSpec"/>'s argument for it word for word: a zeroed block says nothing at all
/// and has to be read against <see cref="CharacterSpec.Id"/> to be understood, which puts the
/// meaning of the data in a second place. That is why every number here is required to be positive:
/// a class that raises the dead raises them somewhere, for some time, at some rate.
/// </para>
/// <para>
/// Immutable and shared, for the reason <see cref="WeaponSpec"/> gives: this is what a designer
/// typed. The live numbers belong to whatever a Wight turns out to be —
/// <see href="../../../../Docs/plan/tasks/M5-04a-minion-agents-and-registry.md">M5-04a</see> builds
/// the body and M5-04b the Rise that produces one — and every "+1 minion cap" in the game will land
/// on a <c>Stat</c> there rather than here (ADR-0008).
/// </para>
/// <para>
/// <b>It ships read by nothing, on purpose</b> (M5-02 rule 5). A class's numbers are authored
/// together or they are authored twice, and the alternative — a Gravecaller asset that is complete
/// except for the half of the class that is its identity — is a second editing pass over the same
/// file, with the sequencing question ("was the cap ever 3?") answered by a git log.
/// </para>
/// <para>
/// <b>A Wight is not an <see cref="EnemySpec"/> and must never become one.</b> The director composes
/// a stage out of the mode's roster, and an archetype the roster could name is one it could spawn
/// against the player. M5-04a rule 1 is where that is argued; what it means here is that the six
/// combat numbers below are a minion's own rather than a reference to an enemy that behaves like one.
/// </para>
/// </remarks>
public sealed class MinionSpec
{
    /// <summary>The largest legal <see cref="RiseChance"/>: every kill raises one.</summary>
    private const float CertainRise = 1f;

    /// <param name="specId">The minion's stable content id — <c>minion.wight</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="cap">
    /// The most minions alive at once — 3 (CH §3.2's <em>"base cap 3"</em>). Must be positive: a cap
    /// of zero is a class whose signature never appears, which is better said by passing no spec.
    /// </param>
    /// <param name="lifespan">Seconds a raised minion lives — 20 (CH §3.2).</param>
    /// <param name="riseChance">
    /// The fraction of kills that raise one — 0.25 (CH §3.2's <em>"25 % of enemies killed"</em>).
    /// In <c>(0, 1]</c>: zero is the class switched off, and above one is a probability that is not
    /// one — a draw against it would always succeed and the number would read as a multiplier.
    /// </param>
    /// <param name="maxHp">A minion's hit points.</param>
    /// <param name="moveSpeed">How fast a minion moves, in metres per second.</param>
    /// <param name="damage">Damage one of its attacks deals.</param>
    /// <param name="attackInterval">Seconds between its attacks.</param>
    /// <param name="reach">How far it strikes from, in metres.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="specId"/> is <c>default(ContentId)</c> or <paramref name="nameKey"/> is
    /// <c>default(LocKey)</c> — a minion nothing can look up, or one with no name to fail to
    /// display. Refused where the data is built, for <see cref="CharacterSpec"/>'s reason.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="riseChance"/> is outside <c>(0, 1]</c>; <paramref name="cap"/> is not
    /// positive; any other value is not a finite number greater than zero.
    /// </exception>
    public MinionSpec(
        ContentId specId,
        LocKey nameKey,
        int cap,
        float lifespan,
        float riseChance,
        float maxHp,
        float moveSpeed,
        float damage,
        float attackInterval,
        float reach)
    {
        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) names no minion.",
                nameof(specId));
        }

        if (nameKey.Key is null)
        {
            throw new ArgumentException(
                $"'{specId}' has no nameKey. A default(LocKey) is a forgotten field rather than a "
                    + "minion nobody has to name.",
                nameof(nameKey));
        }

        if (cap < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cap),
                cap,
                "cap must be at least 1. A class whose minions can never be alive has no minions, "
                    + "and says so by passing no MinionSpec at all.");
        }

        // `!(x > 0f)` rather than `x <= 0f`, so NaN is refused with everything else — the spelling
        // every spec in this folder uses, for the reason TargetingSpec.Positive gives. The upper
        // bound is asked separately because a chance is the one number here with a ceiling.
        if (!(riseChance > 0f) || riseChance > CertainRise)
        {
            throw new ArgumentOutOfRangeException(
                nameof(riseChance),
                riseChance,
                $"riseChance must be in (0, {CertainRise}]. It is the fraction of kills that raise "
                    + "a minion, so zero is the signature switched off and anything above one is a "
                    + "probability that is not one.");
        }

        SpecId = specId;
        NameKey = nameKey;
        Cap = cap;
        Lifespan = Positive(lifespan, nameof(lifespan));
        MaxHp = Positive(maxHp, nameof(maxHp));
        MoveSpeed = Positive(moveSpeed, nameof(moveSpeed));
        Damage = Positive(damage, nameof(damage));
        AttackInterval = Positive(attackInterval, nameof(attackInterval));
        Reach = Positive(reach, nameof(reach));
        RiseChance = riseChance;
    }

    /// <summary>Stable identity — <c>minion.wight</c>.</summary>
    public ContentId SpecId { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>The most minions alive at once. 3 for the Wight (CH §3.2).</summary>
    public int Cap { get; }

    /// <summary>Seconds a raised minion lives. 20 for the Wight (CH §3.2).</summary>
    public float Lifespan { get; }

    /// <summary>The fraction of kills that raise one, in <c>(0, 1]</c>. 0.25 (CH §3.2).</summary>
    public float RiseChance { get; }

    /// <summary>A minion's hit points.</summary>
    public float MaxHp { get; }

    /// <summary>How fast a minion moves, in metres per second.</summary>
    public float MoveSpeed { get; }

    /// <summary>Damage one of its attacks deals.</summary>
    public float Damage { get; }

    /// <summary>Seconds between its attacks.</summary>
    public float AttackInterval { get; }

    /// <summary>How far it strikes from, in metres.</summary>
    public float Reach { get; }

    /// <remarks>
    /// Infinity is asked about separately because it passes a <c>&gt; 0</c> test: an infinite
    /// <see cref="Lifespan"/> is a permanent army bought with one kill, an infinite
    /// <see cref="MaxHp"/> is a minion nothing can remove, and an infinite
    /// <see cref="AttackInterval"/> is one that winds up and never swings.
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
