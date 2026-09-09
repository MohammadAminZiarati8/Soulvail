using System;

namespace Soulvail.Core.Content;

/// <summary>
/// A playable class, as authored data: who it is and the numbers it plays with. Converted once
/// at boot from a <c>CharacterDefinition</c> ScriptableObject and registered in the
/// <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and runtime state never lives here: one instance is shared by everything that
/// reads the class's numbers for a whole run, so a mutable field would be a global variable
/// with a nice name. Current HP belongs to <c>Health</c> (M1-02), not to
/// <see cref="MaxHp"/>.
/// </para>
/// <para>
/// <see cref="MaxHp"/> is a raw <see cref="float"/> for the same reason
/// <see cref="MovementSpec.Speed"/> is: M1-01 introduces the modifier stack and the runtime
/// value becomes a <c>Stat</c> seeded from this number. The authored maximum stays a plain
/// number either way — modifiers apply to the live stat, not to the data.
/// </para>
/// </remarks>
public sealed class CharacterSpec
{
    /// <param name="id">The class's stable content id, e.g. <c>character.oathbound</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="maxHp">Starting maximum health.</param>
    /// <param name="movement">How the class moves.</param>
    /// <param name="targeting">
    /// How the class aims itself. Required, unlike <paramref name="shield"/>: auto-aim is the
    /// default control mode for every class (CC §3), so there is no honest <c>null</c> here the
    /// way there is for a class without an Aegis. A default in code would be worse still — CC §7
    /// says every one of these numbers is a designer's knob — so a forgotten spec is a compile
    /// error rather than a silently mis-aimed class.
    /// </param>
    /// <param name="weapon">
    /// The class's basic attack. Required for exactly the reason <paramref name="targeting"/> is:
    /// CC §4 gives every class one, with no ammunition and no cost, so a <c>null</c> would be a
    /// forgotten field rather than a class that does not attack — and it would produce a character
    /// who aims perfectly and never swings.
    /// </param>
    /// <param name="shield">
    /// The class's regenerating shield, or <see langword="null"/> for a class without one.
    /// Optional because most classes are in that case: the Aegis is the Oathbound's signature
    /// (CH §3.1) and the only regeneration in the game, so <see langword="null"/> is the honest
    /// default rather than a convenience.
    /// </param>
    /// <param name="hitIFrames">
    /// Seconds of invulnerability after a hit lands, or 0 for none. Defaults to 0 so that the
    /// two optional parameters together describe "no shield, no mercy" — which is what an
    /// enemy-shaped character would be, and what a class author has to override on purpose.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>. A spec with no id cannot be looked
    /// up, cannot be saved, and would sit in the catalog under a key that
    /// <see cref="ContentCatalog.Character"/> reports as missing — a lie the catalog would tell
    /// forever. Rejected here, where the data is built, rather than where it is read.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxHp"/> is not greater than zero — a class that starts dead is a content
    /// mistake — or <paramref name="hitIFrames"/> is negative, NaN or infinite.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="movement"/>, <paramref name="targeting"/> or <paramref name="weapon"/> is
    /// null.
    /// </exception>
    public CharacterSpec(
        ContentId id,
        LocKey nameKey,
        float maxHp,
        MovementSpec movement,
        TargetingSpec targeting,
        WeaponSpec weapon,
        ShieldSpec shield = null,
        float hitIFrames = 0f)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("id must be a valid ContentId; default(ContentId) has none.", nameof(id));
        }

        // `!(maxHp > 0f)` rather than `maxHp <= 0f`, because every comparison against NaN is
        // false: the natural spelling waves NaN through, and a NaN maximum makes every health
        // fraction NaN — health bars, low-HP effects, death checks — permanently.
        if (!(maxHp > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "maxHp must be greater than zero.");
        }

        // Same spelling, same reason, one rung looser: zero is legal here and means "no
        // i-frames", so the guard is `>= 0` rather than `> 0`. Infinity is asked about
        // separately because it passes a `>= 0` test and means permanent invulnerability.
        if (!(hitIFrames >= 0f) || float.IsInfinity(hitIFrames))
        {
            throw new ArgumentOutOfRangeException(
                nameof(hitIFrames),
                hitIFrames,
                "hitIFrames must be a finite number of seconds, zero or more.");
        }

        Id = id;
        NameKey = nameKey;
        MaxHp = maxHp;
        Movement = movement ?? throw new ArgumentNullException(nameof(movement));
        Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
        Shield = shield;
        HitIFrames = hitIFrames;
    }

    /// <summary>Stable identity, e.g. <c>character.oathbound</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>Starting maximum health.</summary>
    public float MaxHp { get; }

    /// <summary>The class's movement numbers.</summary>
    public MovementSpec Movement { get; }

    /// <summary>
    /// The class's targeting numbers — never null. <c>TargetScorer</c> is built from this, and
    /// M1-04's <c>Targeter</c> reads <see cref="TargetingSpec.Cadence"/> from it.
    /// </summary>
    public TargetingSpec Targeting { get; }

    /// <summary>
    /// The class's basic attack — never null. <c>Weapon</c> (M1-10) is built from this, and seeds
    /// its damage and fire-rate <c>Stat</c>s from it.
    /// </summary>
    public WeaponSpec Weapon { get; }

    /// <summary>
    /// The class's regenerating shield, or <see langword="null"/> when it has none. Only the
    /// Oathbound has one in V1 — <c>Health</c> takes it as-is, and null means the shield path
    /// is skipped entirely rather than run against zeroes.
    /// </summary>
    public ShieldSpec Shield { get; }

    /// <summary>Seconds of invulnerability after a hit lands; 0 for none.</summary>
    public float HitIFrames { get; }
}
