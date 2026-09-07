namespace Soulvail.Core.Ports;

/// <summary>
/// One independent sequence of random values. Draws advance only this stream.
/// </summary>
/// <remarks>
/// Deliberately small. Weighted picks and shuffles land here when the director needs them
/// (M2) — adding them speculatively would fix their semantics before there is a caller to
/// judge them against.
/// </remarks>
public interface IRandomStream
{
    /// <summary>A value in [0, 1). Never returns 1.</summary>
    float NextFloat();

    /// <summary>
    /// A value in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="maxExclusive"/> is not greater than <paramref name="minInclusive"/>.
    /// An empty range has no value to return, so it is a caller bug rather than a result.
    /// </exception>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>A value in [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>].</summary>
    float Range(float minInclusive, float maxInclusive);

    /// <summary>
    /// True with probability <paramref name="probability"/>: <c>NextFloat() &lt; probability</c>,
    /// so 0 or less is never true and 1 or more is always true. Consumes one draw either way,
    /// which is what keeps a sequence stable when a probability is tuned to a certainty.
    /// </summary>
    bool Chance(float probability);
}

/// <summary>
/// The outbound port core draws randomness through: one seed, several independent streams.
/// <c>UnityEngine.Random</c> is never used in core.
/// </summary>
/// <remarks>
/// Streams exist so that adding a mechanic that consumes randomness in one concern cannot
/// shift the sequence of another. With a single shared generator, one new drop roll would
/// silently change every seeded run's spawns. Draw from the stream that matches the concern.
/// See <see href="../../../../Docs/adr/0011-random-streams.md">ADR-0011</see>.
/// </remarks>
public interface IRandom
{
    /// <summary>The seed every stream was derived from. One source of truth for a run's identity.</summary>
    int Seed { get; }

    /// <summary>Enemy spawning: what appears, where, and when.</summary>
    IRandomStream Spawn { get; }

    /// <summary>Skill-tree offers and rerolls.</summary>
    IRandomStream Offers { get; }

    /// <summary>Elite and enemy affix rolls.</summary>
    IRandomStream Affixes { get; }

    /// <summary>Loot and essence drops.</summary>
    IRandomStream Drops { get; }

    /// <summary>Everything with no stream of its own — cosmetic variation, barks, jitter.</summary>
    IRandomStream Misc { get; }
}
