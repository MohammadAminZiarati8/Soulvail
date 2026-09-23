using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Content;

// One tree node, as authored data: what taking it does, and — if it is an Active — what firing it
// does. Four types in one file for `ModeSpec.cs`' precedent: a kind, the optional block that kind
// requires, the optional corrupted form any other kind may carry (M6-05a), and the record that
// carries all three.

/// <summary>
/// CH §4's four kinds of node.
/// </summary>
/// <remarks>
/// A closed set, so an enum is the right shape — content is named by <see cref="ContentId"/> and
/// this only says what <em>taking</em> the node does. Unlike <c>EnemyBehaviourKind</c> it is
/// validated by the spec in both directions (see <see cref="SkillSpec"/>'s rule 1 remarks), because
/// there is no staging window here: M3-06's runner exists before the first Active does.
/// </remarks>
public enum SkillKind
{
    /// <summary>Always on. Stat changes and rule changes; ~45 % of a tree (CH §4).</summary>
    Passive,

    /// <summary>Grants a skill with a cooldown, auto-casting by default (CH §4.2).</summary>
    Active,

    /// <summary>Improves a skill already owned. Offered only if the parent is owned.</summary>
    Upgrade,

    /// <summary>Build-defining, end of a branch, three per class.</summary>
    Keystone,
}

/// <summary>
/// What an Active is when it fires: how often, when Auto decides to, and what it does.
/// </summary>
/// <remarks>
/// <para>
/// Present only on a <see cref="SkillKind.Active"/> node, and required on one — see
/// <see cref="SkillSpec"/>. The bargain <see cref="CharacterSpec"/> makes with its
/// <c>ShieldSpec</c>, tightened: a null block says <em>not this one</em>, and here a null block on
/// an Active would be a skill that cannot fire.
/// </para>
/// <para>
/// <b>The authored base and nothing else.</b> <see cref="Cooldown"/> seeds M3-06's <c>Stat</c>, and
/// CH §4.1's <em>"multiplicative and floored at 40 % of base"</em> belongs to the runner — the layer
/// that knows what "too short" means. The same split <c>MovementSkillSpec.Cooldown</c> and
/// <c>ChargeSkill.Cooldown</c> already make.
/// </para>
/// <para>
/// Immutable and shared, like every spec: the cast list is copied on construction.
/// </para>
/// </remarks>
public sealed class ActiveSpec
{
    private readonly ReadOnlyCollection<IEffect> _onCast;

    /// <param name="cooldown">
    /// Seconds between casts, before any reduction. Finite and greater than zero, for
    /// <c>MovementSkillSpec</c>'s reason: a skill with no cooldown is not a skill, and an infinite
    /// one is a node the player takes and never gets to use.
    /// </param>
    /// <param name="trigger">
    /// CC §6.4's authored condition — when Auto decides to fire it. Required: every active ships
    /// with one (CH §4.2), and a null would be an active that auto-casts on nothing while its
    /// toggle still says Auto.
    /// </param>
    /// <param name="onCast">
    /// What firing it does. At least one effect, no null entries. Copied; the caller's list is not
    /// retained.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="trigger"/> or <paramref name="onCast"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="onCast"/> is empty or holds a null entry. An active that casts nothing goes
    /// on cooldown and does nothing, which is the silence M2-06 rule 11 refuses.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cooldown"/> is not a finite number greater than zero.
    /// </exception>
    public ActiveSpec(float cooldown, TriggerSpec trigger, IReadOnlyList<IEffect> onCast)
    {
        // !(value > 0f) rather than value <= 0f, so NaN is refused too (AR §18.3). Infinity is
        // asked about separately because it passes a > 0 test.
        if (!(cooldown > 0f) || float.IsInfinity(cooldown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cooldown),
                cooldown,
                "cooldown must be a finite number greater than zero. A skill with no cooldown is "
                    + "not a skill, and an infinite one is a node that never fires.");
        }

        if (trigger is null)
        {
            throw new ArgumentNullException(nameof(trigger));
        }

        if (onCast is null)
        {
            throw new ArgumentNullException(nameof(onCast));
        }

        if (onCast.Count == 0)
        {
            throw new ArgumentException(
                "An active must do something when it fires. An empty onCast is a skill that goes "
                    + "on cooldown and produces nothing.",
                nameof(onCast));
        }

        Cooldown = cooldown;
        Trigger = trigger;
        _onCast = SkillSpec.CopyEffects(onCast, nameof(onCast));
    }

    /// <summary>Seconds between casts, before any reduction. Seeds M3-06's <c>Stat</c>.</summary>
    public float Cooldown { get; }

    /// <summary>CC §6.4's authored condition — when Auto fires it.</summary>
    public TriggerSpec Trigger { get; }

    /// <summary>What firing it does, in the order they were authored.</summary>
    public IReadOnlyList<IEffect> OnCast => _onCast;
}

/// <summary>
/// GD §13.2's corrupted form of one node: what it does instead, what it says, and what it costs in
/// Veilrot. Authored beside the clean node rather than derived from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Authored, because GD §13.2's "~1.8×" is a budget rather than an operation</b> (M6-05a).
/// <see cref="IEffect"/> is a marker with no members, and multiplying is backwards for half the
/// shipped primitives — a heal zone pulsing 1.8× as often is a longer interval, a minion count is an
/// <see cref="int"/>, a cooldown reduction is authored negative. GD §13.2's own worked examples
/// carry downsides no multiplication produces, so a designer writes the corrupted node whole.
/// </para>
/// <para>
/// <b>No name of its own.</b> The corrupted node keeps the clean node's name so the player
/// recognises it (M6-05a rule 4); its effects differ, so its description does not.
/// </para>
/// <para>
/// Immutable and shared, like every spec: the effect list is copied on construction through
/// <see cref="SkillSpec.CopyEffects"/>, the one copy-and-null-check in this file (rule 7).
/// </para>
/// </remarks>
public sealed class PactSpec
{
    /// <summary>The least Veilrot a Pact may ask. GD §13.2 and CH §4.4's band.</summary>
    public const float MinVeilrot = 10f;

    /// <summary>The most. A band rather than a number, because GD §13.2 writes one.</summary>
    public const float MaxVeilrot = 20f;

    private readonly ReadOnlyCollection<IEffect> _effects;

    /// <param name="effects">
    /// What taking the corrupted node puts into force — <b>instead of</b> the clean node's, never as
    /// well as (rule 1). At least one, no nulls. Copied.
    /// </param>
    /// <param name="veilrot">What it adds to GD §10's meter. In [10, 20].</param>
    /// <param name="descriptionKey">
    /// What it says. Its own, because its effects differ; the name stays the clean node's.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="effects"/> is empty or holds a null; <paramref name="descriptionKey"/> is a
    /// default.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="veilrot"/> is not a finite number inside the band.
    /// </exception>
    public PactSpec(IReadOnlyList<IEffect> effects, float veilrot, LocKey descriptionKey)
    {
        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        // A Pact that does nothing is a price for nothing. ActiveSpec's empty-onCast refusal, one
        // block over.
        if (effects.Count == 0)
        {
            throw new ArgumentException(
                "A Pact must do something. An empty effect list is a Veilrot price for a node that "
                    + "changes nothing.",
                nameof(effects));
        }

        // Asked as "inside the band" rather than "outside it", so NaN — which fails every
        // comparison — is refused rather than waved through (AR §18.3). Infinity is outside by
        // arithmetic.
        if (!(veilrot >= MinVeilrot && veilrot <= MaxVeilrot))
        {
            throw new ArgumentOutOfRangeException(
                nameof(veilrot),
                veilrot,
                $"A Pact asks between {MinVeilrot} and {MaxVeilrot} Veilrot (GD §13.2, CH §4.4).");
        }

        // SkillSpec's forgotten-field rule: default(LocKey) carries a null past the struct's own
        // constructor, and ADR-0012 says the key exists from the first node.
        if (descriptionKey.Key is null)
        {
            throw new ArgumentException(
                "A Pact has no descriptionKey. Its effects differ from the clean node's, so what "
                    + "it says must too — and a default(LocKey) is a forgotten field.",
                nameof(descriptionKey));
        }

        _effects = SkillSpec.CopyEffects(effects, nameof(effects));
        Veilrot = veilrot;
        DescriptionKey = descriptionKey;
    }

    /// <summary>What taking the corrupted node puts into force, in the order they were authored.</summary>
    public IReadOnlyList<IEffect> Effects => _effects;

    /// <summary>What it adds to GD §10's meter, in [<see cref="MinVeilrot"/>, <see cref="MaxVeilrot"/>].</summary>
    public float Veilrot { get; }

    /// <summary>Localisation key for what the corrupted node says.</summary>
    public LocKey DescriptionKey { get; }
}

/// <summary>
/// One tree node, as authored data: its identity, its text, its kind, and the effects taking it
/// puts into force. Converted once at boot from a <c>SkillDefinition</c> ScriptableObject (M3-02b)
/// and registered in the <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// <b>A kind requires its block, and a block requires its kind — both directions, unlike
/// <see cref="EnemySpec"/>.</b> An <see cref="SkillKind.Active"/> without an
/// <see cref="ActiveSpec"/> is a skill that cannot fire; an <see cref="ActiveSpec"/> on a
/// <see cref="SkillKind.Passive"/> is a cooldown nothing will ever run. M2-06 let a block precede
/// its kind so a Spitter's numbers could be authored before the code that threw them existed; here
/// the runner (M3-06) lands before the first active (M3-11), and M3-11 ships Consecrate's block and
/// its kind in one PR — so there is nothing to stage and the looser rule would only admit mistakes.
/// <see cref="ParentId"/> is checked the same way in both directions.
/// </para>
/// <para>
/// <b>A node that does nothing is refused.</b> GD §13.1's <em>"every node must change how you
/// play"</em> has a weaker cousin a constructor can enforce, which is that every node must at least
/// do <em>something</em>. An Active is the one kind that may take with no <see cref="Effects"/>,
/// because its power is on cast — and then <see cref="ActiveSpec.OnCast"/> carries the refusal
/// instead.
/// </para>
/// <para>
/// <b>A Pact is authored beside the node, optional and last</b> (M6-05a rules 2 and 3): a
/// <see cref="PactSpec"/> is the node's corrupted form, refused on an
/// <see cref="SkillKind.Active"/> because an Active's power is on cast and a corrupted one would be
/// a second <see cref="ActiveSpec"/> the runner has no door for. <see cref="HasPact"/> is the read
/// every caller uses.
/// </para>
/// <para>
/// <b>What this type deliberately does not carry.</b> No tier and no branch: where a
/// node sits is the <see cref="SkillTreeSpec"/>'s, so one node cannot disagree with the tree that
/// holds it. No <c>TagSet</c>, for <see cref="EnemySpec"/>'s reason — until affixes need one.
/// </para>
/// <para>
/// Immutable and shared across every run (AR §10.1): one instance describes the node and is handed
/// to whoever is playing, which is why the effects it carries hold an <em>address</em> rather than
/// a live object. It is also the <c>source</c> M3-03 passes to <c>EffectRegistry.Apply</c> — a
/// reference stable for the run's life and never confused with another node's.
/// </para>
/// </remarks>
public sealed class SkillSpec
{
    private readonly ReadOnlyCollection<IEffect> _effects;

    /// <param name="id">The node's stable content id, e.g. <c>skill.oathbound.consecrate</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="descriptionKey">Localisation key for the description the tree and the level-up
    /// screen show.</param>
    /// <param name="kind">Which of CH §4's four kinds this is.</param>
    /// <param name="effects">
    /// What taking it puts into force, applied once when it is taken. At least one for a
    /// <see cref="SkillKind.Passive"/>, an <see cref="SkillKind.Upgrade"/> or a
    /// <see cref="SkillKind.Keystone"/>; may be empty for an <see cref="SkillKind.Active"/>.
    /// Copied; the caller's list is not retained.
    /// </param>
    /// <param name="active">
    /// The cooldown, the trigger and the cast effects — required on an
    /// <see cref="SkillKind.Active"/> and refused on every other kind.
    /// </param>
    /// <param name="parentId">
    /// The skill this improves — required on an <see cref="SkillKind.Upgrade"/> and refused on
    /// every other kind. That the parent <em>exists</em> is M3-03's check, for
    /// <see cref="SkillTreeSpec"/>'s reason: a spec constructor has no catalog.
    /// </param>
    /// <param name="pact">
    /// GD §13.2's corrupted form of this node, or <see langword="null"/> for a node nobody wrote one
    /// for. Refused on an <see cref="SkillKind.Active"/> (M6-05a rule 3). Optional and last because
    /// seventy call sites build a <see cref="SkillSpec"/> (rule 2).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/>, <paramref name="nameKey"/> or <paramref name="descriptionKey"/> is a
    /// default value; <paramref name="kind"/> and <paramref name="active"/> disagree;
    /// <paramref name="kind"/> and <paramref name="parentId"/> disagree; <paramref name="effects"/>
    /// holds a null entry; a non-Active was given no effects; or an Active was given a
    /// <paramref name="pact"/>.
    /// </exception>
    public SkillSpec(
        ContentId id,
        LocKey nameKey,
        LocKey descriptionKey,
        SkillKind kind,
        IReadOnlyList<IEffect> effects,
        ActiveSpec active = null,
        ContentId parentId = default,
        PactSpec pact = null)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) names no skill.",
                nameof(id));
        }

        // Both keys are required, and neither is checked against a table — "does this key resolve"
        // is M3-14's question about a language. What is refused here is a *forgotten field*:
        // default(LocKey) carries a null past the struct's own constructor (AR §18.3, M0-08), and
        // ADR-0012 says the key exists from the first node even though the text does not.
        if (nameKey.Key is null)
        {
            throw new ArgumentException(
                $"'{id}' has no nameKey. A default(LocKey) is a forgotten field rather than a "
                    + "node that chose to be nameless.",
                nameof(nameKey));
        }

        if (descriptionKey.Key is null)
        {
            throw new ArgumentException(
                $"'{id}' has no descriptionKey. GD §13.1's two-second rule is about the text, but "
                    + "the key exists from the first node (ADR-0012).",
                nameof(descriptionKey));
        }

        if (kind == SkillKind.Active && active is null)
        {
            throw new ArgumentException(
                $"'{id}' is an Active with no ActiveSpec, which is a skill that cannot fire. "
                    + "Unlike EnemySpec, the kind and its block ship together.",
                nameof(active));
        }

        if (kind != SkillKind.Active && active is not null)
        {
            throw new ArgumentException(
                $"'{id}' is a {kind} carrying an ActiveSpec, which is a cooldown nothing will ever "
                    + "run. Only an Active has one.",
                nameof(active));
        }

        if (kind == SkillKind.Upgrade && parentId.Value is null)
        {
            throw new ArgumentException(
                $"'{id}' is an Upgrade with no parentId. An Upgrade improves a skill you already "
                    + "own (CH §4), and one that names nothing could never be gated on anything.",
                nameof(parentId));
        }

        if (kind != SkillKind.Upgrade && parentId.Value is not null)
        {
            throw new ArgumentException(
                $"'{id}' is a {kind} naming parent '{parentId}'. Only an Upgrade has a parent; on "
                    + "any other kind the field would be read by nothing.",
                nameof(parentId));
        }

        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        // An Active is the one kind that may take with no effects, because its power is on cast —
        // and ActiveSpec has already refused an empty OnCast, so the node still does something.
        if (kind != SkillKind.Active && effects.Count == 0)
        {
            throw new ArgumentException(
                $"'{id}' is a {kind} with no effects, so taking it would change nothing. Only an "
                    + "Active may take with none, because its power is on cast.",
                nameof(effects));
        }

        // M6-05a rule 3. A corrupted Active is a second ActiveSpec — a second cooldown Stat, a
        // second trigger, a second auto-cast slot — and SkillRunner.Add takes a SkillSpec, which a
        // PactSpec is not. Refused in writing rather than half-built.
        if (kind == SkillKind.Active && pact is not null)
        {
            throw new ArgumentException(
                $"'{id}' is an Active carrying a Pact. An Active's power is on cast, so its "
                    + "corrupted form would be a second ActiveSpec the runner has no door for; "
                    + "Exhume, Bulwark and Consecrate wait for M7-04, which builds that door.",
                nameof(pact));
        }

        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        Kind = kind;
        Active = active;
        ParentId = parentId;
        Pact = pact;
        _effects = CopyEffects(effects, nameof(effects));
    }

    /// <summary>Stable identity, e.g. <c>skill.oathbound.consecrate</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>Localisation key for the description the tree and the level-up screen show.</summary>
    public LocKey DescriptionKey { get; }

    /// <summary>Which of CH §4's four kinds this is.</summary>
    public SkillKind Kind { get; }

    /// <summary>
    /// What taking it puts into force, in the order they were authored. Applied once, on take.
    /// </summary>
    /// <remarks>
    /// Applied by M3-03 through <c>EffectRegistry.Apply</c>, with this spec as the source. Empty
    /// only on an <see cref="SkillKind.Active"/>. <b>Whether each effect's address resolves</b> —
    /// whether the <c>PlayerStat</c> a <c>ModifyStat</c> names exists this run — is not asked here
    /// and not asked by <c>EffectRegistry.CanApply</c> either, which answers only "is there a
    /// handler for this type". That seam is M3-02b's or M3-14b's to close.
    /// </remarks>
    public IReadOnlyList<IEffect> Effects => _effects;

    /// <summary>
    /// The cooldown, trigger and cast effects, or <see langword="null"/> on any kind but
    /// <see cref="SkillKind.Active"/>.
    /// </summary>
    public ActiveSpec Active { get; }

    /// <summary>
    /// The skill this improves, or <c>default(ContentId)</c> on any kind but
    /// <see cref="SkillKind.Upgrade"/>.
    /// </summary>
    public ContentId ParentId { get; }

    /// <summary>
    /// The corrupted form of this node, or <see langword="null"/> for a node nobody wrote one for.
    /// </summary>
    public PactSpec Pact { get; }

    /// <summary>Whether this node can ever be offered as a Pact — M6-05a rule 2.</summary>
    public bool HasPact => Pact is not null;

    /// <summary>
    /// Copies an effect list, refusing a null entry, and wraps it so the copy cannot be written
    /// through.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="ActiveSpec"/> and <see cref="PactSpec"/> rather than written three
    /// times: the lists are the same
    /// kind of thing — effects a node carries — and a second copy of the loop would be a second
    /// place for the null rule to drift. <c>internal</c> because nothing outside this file has a
    /// list of effects to copy.
    /// </remarks>
    internal static ReadOnlyCollection<IEffect> CopyEffects(
        IReadOnlyList<IEffect> effects,
        string paramName)
    {
        // Array.Empty rather than new IEffect[0], which is M3-01b's lesson: a copy-on-construction
        // list stays free until there is something to copy. The wrapper itself is one object per
        // spec at boot, and nothing here is a static — AR §7 (and Palette is the only one).
        if (effects.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<IEffect>());
        }

        var copy = new IEffect[effects.Count];

        for (int i = 0; i < effects.Count; i++)
        {
            copy[i] = effects[i]
                ?? throw new ArgumentException($"{paramName}[{i}] is null.", paramName);
        }

        return Array.AsReadOnly(copy);
    }
}
