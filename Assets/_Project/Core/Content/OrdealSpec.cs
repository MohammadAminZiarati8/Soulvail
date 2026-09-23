using System;

namespace Soulvail.Core.Content;

/// <summary>
/// One of GD §13.4's deep-run modifiers, as authored data (ADR-0006): a name, a description, and
/// whichever of five dials it turns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five named fields and no kind enum</b>, which is M6-06a rule 2. A <c>switch (ordeal.Kind)</c>
/// at four call sites is ADR-0009's ban arriving under a different name; five neutral dials mean each
/// consumer reads one number and nothing anywhere dispatches. <see cref="ScalingSpec"/>'s shape.
/// </para>
/// <para>
/// <b>The cost is stated rather than hidden:</b> a shipped Ordeal leaves four dials at their neutral
/// value, which is what <see cref="OverflowSpec"/> already looks like. The gain is that M6-06b is four
/// one-line reads instead of four dispatches.
/// </para>
/// <para>
/// Immutable and shared, like every other spec. What a run has been dealt is <c>Ordeals</c>, in
/// <c>Core/Run/</c>, and never a field here.
/// </para>
/// </remarks>
public sealed class OrdealSpec
{
    /// <param name="id">The Ordeal's stable content id, e.g. <c>ordeal.famine</c>.</param>
    /// <param name="nameKey">Localisation key for its name.</param>
    /// <param name="descriptionKey">Localisation key for what it does, in a sentence.</param>
    /// <param name="essenceMultiplier">What a stage clear pays, times this. 1 is unchanged.</param>
    /// <param name="offerCount">How many nodes a level-up offers, or 0 for unchanged.</param>
    /// <param name="concurrencyBonus">What to add to GD §12.2's concurrency cap. 0 is unchanged.</param>
    /// <param name="veilrotMultiplier">What a Veilrot gain is multiplied by. 1 is unchanged.</param>
    /// <param name="threatCostTarget">Whose threat cost moves, or <c>default</c> for nobody's.</param>
    /// <param name="threatCostMultiplier">And by how much. 1 is unchanged.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/>, <paramref name="nameKey"/> or <paramref name="descriptionKey"/> is a
    /// default; <paramref name="threatCostTarget"/> is named without a multiplier or the reverse; or
    /// every dial is neutral, which is an Ordeal that does nothing (rule 3).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A multiplier is not finite and above zero; <paramref name="offerCount"/> is negative;
    /// <paramref name="concurrencyBonus"/> is negative.
    /// </exception>
    public OrdealSpec(
        ContentId id,
        LocKey nameKey,
        LocKey descriptionKey,
        float essenceMultiplier = 1f,
        int offerCount = 0,
        int concurrencyBonus = 0,
        float veilrotMultiplier = 1f,
        ContentId threatCostTarget = default,
        float threatCostMultiplier = 1f)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) names no Ordeal.",
                nameof(id));
        }

        if (nameKey.Key is null)
        {
            throw new ArgumentException(
                $"'{id}' has no name key. An Ordeal is announced by name the moment it is dealt.",
                nameof(nameKey));
        }

        if (descriptionKey.Key is null)
        {
            throw new ArgumentException(
                $"'{id}' has no description key. A permanent modifier the player cannot read is a "
                    + "rule they learn by dying to it.",
                nameof(descriptionKey));
        }

        EssenceMultiplier = RequireMultiplier(essenceMultiplier, nameof(essenceMultiplier), id);
        VeilrotMultiplier = RequireMultiplier(veilrotMultiplier, nameof(veilrotMultiplier), id);
        ThreatCostMultiplier = RequireMultiplier(threatCostMultiplier, nameof(threatCostMultiplier), id);

        if (offerCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offerCount),
                offerCount,
                $"'{id}'s offerCount must be zero or more. Zero means the offer is unchanged, and a "
                    + "negative count is an offer of nothing.");
        }

        if (concurrencyBonus < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concurrencyBonus),
                concurrencyBonus,
                $"'{id}'s concurrencyBonus must be zero or more. GD §13.4's Ordeals are deep-run "
                    + "pressure, and one that thinned the arena would be a gift wearing the name.");
        }

        // Both halves or neither: a target with a multiplier of 1 moves nobody's cost, and a
        // multiplier with no target has nobody to move — either is an authoring slip that would
        // otherwise pass as a dial turned (rule 3's silence, one field over).
        bool named = threatCostTarget.Value is not null;

        // Exact comparison on purpose: 1 is the authored neutral, not a computed one.
        bool moved = ThreatCostMultiplier != 1f;

        if (named != moved)
        {
            throw new ArgumentException(
                named
                    ? $"'{id}' names '{threatCostTarget}' as its threat-cost target with a multiplier "
                        + "of 1, which moves nothing. Author the multiplier or clear the target."
                    : $"'{id}' has a threat-cost multiplier of {ThreatCostMultiplier} and no target, "
                        + "so there is nobody for it to apply to. Name the archetype.",
                nameof(threatCostTarget));
        }

        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        OfferCount = offerCount;
        ConcurrencyBonus = concurrencyBonus;
        ThreatCostTarget = threatCostTarget;

        if (IsNeutral)
        {
            throw new ArgumentException(
                $"'{id}' turns none of its five dials, so dealing it would change nothing. "
                    + "M2-06 rule 11 refuses a row that does nothing at the authoring door.",
                nameof(id));
        }
    }

    public ContentId Id { get; }

    public LocKey NameKey { get; }

    public LocKey DescriptionKey { get; }

    /// <summary>What a stage clear pays, times this. Famine's 0.6 — GD §13.4.</summary>
    public float EssenceMultiplier { get; }

    /// <summary>How many nodes a level-up offers, or 0 for "unchanged". Vigil's 2.</summary>
    public int OfferCount { get; }

    /// <summary>What to add to GD §12.2's concurrency cap. Swarm's 8.</summary>
    public int ConcurrencyBonus { get; }

    /// <summary>What a Veilrot gain is multiplied by. Hunger's 1.5.</summary>
    public float VeilrotMultiplier { get; }

    /// <summary>Whose threat cost moves, or <c>default</c>. Swarm names the Husk.</summary>
    public ContentId ThreatCostTarget { get; }

    /// <summary>And by how much. Swarm's 0.5.</summary>
    public float ThreatCostMultiplier { get; }

    /// <summary>Every dial at its neutral value. Only ever true inside the constructor's refusal.</summary>
    /// <remarks>
    /// The threat pair is read through its target alone, because the constructor has already made
    /// the two agree: a target is named exactly when the multiplier moves.
    /// </remarks>
    private bool IsNeutral =>
        EssenceMultiplier == 1f
        && OfferCount == 0
        && ConcurrencyBonus == 0
        && VeilrotMultiplier == 1f
        && ThreatCostTarget.Value is null;

    /// <summary>
    /// The one door for all three multipliers, written once: they are the same kind of number and
    /// owe the same sentence — <see cref="OverflowSpec"/>'s <c>Require</c>'s shape.
    /// </summary>
    /// <remarks>
    /// <c>!(value &gt; 0f)</c> so NaN is refused with zero and the negatives (AR §18.3), and infinity
    /// separately because it passes that test. Zero is refused rather than read as "nothing": a
    /// Famine at 0 pays nothing for ever, and a Swarm at 0 makes a Husk free, which the composer
    /// would spend its whole budget on.
    /// </remarks>
    private static float RequireMultiplier(float value, string field, ContentId id)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                field,
                value,
                $"'{id}'s {field} must be a finite number above zero; 1 is unchanged. It multiplies "
                    + "a number the run reads every stage, so a NaN poisons it for the rest of the "
                    + "run and a zero erases it.");
        }

        return value;
    }
}
