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
    /// <param name="descriptionKey">
    /// Localisation key for the one line about the class that the class-select card draws
    /// (M5-07 rule 5). <b>Required, and guarded, unlike <paramref name="nameKey"/></b>: the
    /// screen this exists for has a sentence-shaped hole in every card, and a
    /// <c>default(LocKey)</c> would fill it with an empty string rather than with a diagnosis.
    /// Third rather than last, so the two keys sit together in <c>SkillSpec</c>'s order — that
    /// type has carried a name and a description since M3-02b and is what the level-up card
    /// draws.
    /// </param>
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
    /// <param name="focus">
    /// The class's Focus ramp (CC §4.3) — how standing still speeds the swing up. Required for the
    /// reason <paramref name="weapon"/> is, one step further out: standing still is not a class
    /// feature but something every character does, so there is no "this class has no Focus" state
    /// to express with a null. A class that should not ramp authors a
    /// <see cref="FocusSpec.MaxMultiplier"/> of 1, which is also how CC §4.3's own "cut it if it
    /// doesn't feel good" is spent — one number in one asset.
    /// <para>
    /// It is required for a second, load-bearing reason: <c>FocusTracker</c> owns the stationary
    /// clock that <c>CombatBlackboard.StationaryTime</c> mirrors, and that clock is a CC §6.4
    /// trigger field in its own right. A class with no tracker would silently stop counting it.
    /// </para>
    /// </param>
    /// <param name="movementSkill">
    /// The class's dash (CC §5) — how far, how long, how often, and what it does to whatever is in
    /// the way. Required for the reason <paramref name="weapon"/> is, and CC §5 says it in its first
    /// line: every class has exactly one, on a permanent button. A <see langword="null"/> would be a
    /// forgotten field rather than a class that cannot dash, and it would produce a character with a
    /// dead button in the one place the design has no fallback for — CC §5's dodge is the whole of
    /// the defensive layer for a class without an Aegis.
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
    /// <param name="minions">
    /// The class's minions, or <see langword="null"/> for a class with none. Optional for the
    /// reason <paramref name="shield"/> is, and it is the same argument rather than a second one:
    /// CH §3.2's Rise is the Gravecaller's signature, so <see langword="null"/> is the honest
    /// default and a block of zeroes would have to be read against <paramref name="id"/> to be
    /// understood (M5-02 rule 6).
    /// </param>
    /// <param name="kindling">
    /// The class's stacking signature, or <see langword="null"/> for a class with none. Optional and
    /// last for <paramref name="minions"/>' counted reason (M6-07a rule 1): 64 <c>new
    /// CharacterSpec(...)</c> sites across 45 files build one, and CH §3.3's Kindling is the
    /// Emberwright's alone, so <see langword="null"/> is the honest default.
    /// </param>
    /// <param name="veilrot">
    /// How the Veil treats this class, or <see langword="null"/> for the way GD §10 describes and no
    /// other (M6-07c rule 1). Optional and last beside <paramref name="kindling"/>, for its counted
    /// reason.
    /// </param>
    /// <param name="unlock">
    /// What this class costs and what proves it (GD §14.2), or <see langword="null"/> for the
    /// starter. Optional and last for the counted reason above, and it makes three blocks appended
    /// in three tasks (M6-09a rule 2): the list is fifteen long, four of them nullable blocks — the
    /// price of CH §3's identity axes being real, and M7's fourth class is the day it wants
    /// restructuring.
    /// </param>
    /// <param name="volley">
    /// The class's volley, or <see langword="null"/> for a class with none — every class but the
    /// Ranger (RS-03b). Optional and last, for the counted reason above.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>. A spec with no id cannot be looked
    /// up, cannot be saved, and would sit in the catalog under a key that
    /// <see cref="ContentCatalog.Character"/> reports as missing — a lie the catalog would tell
    /// forever. Rejected here, where the data is built, rather than where it is read.
    /// <para>
    /// Or <paramref name="descriptionKey"/> is <c>default(LocKey)</c> (M5-07 rule 5).
    /// </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxHp"/> is not greater than zero — a class that starts dead is a content
    /// mistake — or <paramref name="hitIFrames"/> is negative, NaN or infinite.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="movement"/>, <paramref name="targeting"/>, <paramref name="weapon"/>,
    /// <paramref name="focus"/> or <paramref name="movementSkill"/> is null.
    /// </exception>
    public CharacterSpec(
        ContentId id,
        LocKey nameKey,
        LocKey descriptionKey,
        float maxHp,
        MovementSpec movement,
        TargetingSpec targeting,
        WeaponSpec weapon,
        FocusSpec focus,
        MovementSkillSpec movementSkill,
        ShieldSpec shield = null,
        float hitIFrames = 0f,
        MinionSpec minions = null,
        KindlingSpec kindling = null,
        VeilrotSpec veilrot = null,
        UnlockSpec unlock = null,
        VolleySpec volley = null)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("id must be a valid ContentId; default(ContentId) has none.", nameof(id));
        }

        // Guarded where nameKey is not, and the asymmetry is rule 5's whole argument: a name is
        // drawn by screens that already have the id to fall back on, while the description exists
        // only for the class-select card — so a default here is a card with a blank half and
        // nothing anywhere saying which asset forgot it.
        if (descriptionKey.Key is null)
        {
            throw new ArgumentException(
                "descriptionKey must be a valid LocKey; default(LocKey) names no string. Every "
                    + "class needs the one line the class-select card draws (M5-07 rule 5).",
                nameof(descriptionKey));
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
        DescriptionKey = descriptionKey;
        MaxHp = maxHp;
        Movement = movement ?? throw new ArgumentNullException(nameof(movement));
        Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
        Focus = focus ?? throw new ArgumentNullException(nameof(focus));
        MovementSkill = movementSkill ?? throw new ArgumentNullException(nameof(movementSkill));
        Shield = shield;
        HitIFrames = hitIFrames;
        Minions = minions;
        Kindling = kindling;
        Veilrot = veilrot;
        Unlock = unlock;
        Volley = volley;
    }

    /// <summary>Stable identity, e.g. <c>character.oathbound</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>
    /// One line about the class, for the class-select card — never the sentence itself. Never
    /// <c>default(LocKey)</c>: the constructor refuses one (M5-07 rule 5).
    /// </summary>
    /// <remarks>
    /// CH §3's table has nine columns and a phone card has room for three numbers, so the rest of
    /// what separates two classes has to be said in words. <c>"You are not the damage"</c> is a
    /// sentence here rather than a fourth row of figures.
    /// </remarks>
    public LocKey DescriptionKey { get; }

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
    /// The class's Focus ramp — never null. <c>FocusTracker</c> (M1-13) is built from this and
    /// turns it into a modifier on <see cref="Weapon"/>'s fire rate.
    /// </summary>
    /// <remarks>
    /// CC §4.3's Focus, which is standing still. Not CC §3.4's tap-to-focus, which is a target and
    /// is governed by <see cref="Targeting"/>.
    /// </remarks>
    public FocusSpec Focus { get; }

    /// <summary>
    /// The class's dash — never null. <c>ChargeSkill</c> (M1-14) is built from this and seeds its
    /// cooldown <c>Stat</c> from it.
    /// </summary>
    public MovementSkillSpec MovementSkill { get; }

    /// <summary>
    /// The class's regenerating shield, or <see langword="null"/> when it has none. Only the
    /// Oathbound has one in V1 — <c>Health</c> takes it as-is, and null means the shield path
    /// is skipped entirely rather than run against zeroes.
    /// </summary>
    public ShieldSpec Shield { get; }

    /// <summary>Seconds of invulnerability after a hit lands; 0 for none.</summary>
    public float HitIFrames { get; }

    /// <summary>
    /// The class's minions, or <see langword="null"/> when it has none. Only the Gravecaller has
    /// any in V1, and CH §3.2's Wights are <em>raised</em> rather than summoned — see
    /// <see cref="MinionSpec.RiseChance"/>.
    /// </summary>
    /// <remarks>
    /// Read by nothing until M5-04a, which builds the body, and M5-04b, which builds the Rise.
    /// Authored here with the rest of the class because a class's numbers are authored together or
    /// they are authored twice (M5-02 rule 5).
    /// </remarks>
    public MinionSpec Minions { get; }

    /// <summary>
    /// The class's stacking signature, or <see langword="null"/> for a class with none. Only the
    /// Emberwright has one in V1.
    /// </summary>
    /// <remarks>
    /// <c>PlayerCombat</c> builds a <c>Kindling</c> from this and nothing else reads it (M6-07a).
    /// </remarks>
    public KindlingSpec Kindling { get; }

    /// <summary>How the Veil treats this class, or <see langword="null"/> for ordinarily.</summary>
    /// <remarks>
    /// CH §3's third identity axis. <c>RunSession</c> hands it to the meter, the shop and — through
    /// the meter — the runner (M6-07c rule 6).
    /// </remarks>
    public VeilrotSpec Veilrot { get; }

    /// <summary>What this class costs, or <see langword="null"/> for the starter.</summary>
    /// <remarks>
    /// Read by <c>ClassUnlocks</c> and nothing else. <b>Null is what makes a class playable on a
    /// fresh install</b>, not an entry in the profile (M6-09a rule 3).
    /// </remarks>
    public UnlockSpec Unlock { get; }

    /// <summary>The class's volley, or <see langword="null"/> for a class with none.</summary>
    /// <remarks>
    /// <c>PlayerCombat</c> builds a <c>Volley</c> from this and nothing else reads it (RS-03b). Its
    /// count is not here: <c>Volley.Every</c> starts at 0 and a node gives it one.
    /// </remarks>
    public VolleySpec Volley { get; }
}
