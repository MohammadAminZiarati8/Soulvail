namespace Soulvail.Core.Ports;

/// <summary>
/// Where every stream's generator had got to at one instant: position only, five words, in
/// stream-index order. Written into a <c>RunSnapshot</c> so a resumed run carries on drawing
/// where it left off instead of restarting every stream at draw 0.
/// </summary>
/// <remarks>
/// <para>
/// <b>Position, not identity.</b> The run's seed is what selects which of 2^63 sequences each
/// stream walks — <c>SeededRandom</c> derives each generator's increment from
/// <c>(seed, streamIndex)</c> — so this says <em>how far along</em> and the seed says
/// <em>along what</em>. A resume needs both, and neither is sufficient alone: restoring these
/// five words onto a generator built from the same seed is a complete restore, and restoring
/// them onto a differently-seeded one lands in a legal position on the wrong sequence. The
/// <c>RunSnapshot</c> carries both and M2-14b is where their agreement is checked.
/// </para>
/// <para>
/// <b>Five named words rather than an array</b>, because core allocates nothing and a
/// <c>ulong[5]</c> would allocate on every capture. The consequence is real and is the save
/// format's version field's job: a sixth stream — AR §18.3, <em>a new stream takes the next free
/// index</em> — is a format change with a migration, which is what ADR-0007 exists to make
/// survivable.
/// </para>
/// </remarks>
public readonly struct RandomState
{
    /// <param name="spawn">Stream 0's position.</param>
    /// <param name="offers">Stream 1's position.</param>
    /// <param name="affixes">Stream 2's position.</param>
    /// <param name="drops">Stream 3's position.</param>
    /// <param name="misc">Stream 4's position.</param>
    /// <remarks>
    /// Nothing is validated: every 64-bit value is a legal generator position, so there is no
    /// wrong one to refuse. See <see cref="IRandom.Restore"/>.
    /// </remarks>
    public RandomState(ulong spawn, ulong offers, ulong affixes, ulong drops, ulong misc)
    {
        Spawn = spawn;
        Offers = offers;
        Affixes = affixes;
        Drops = drops;
        Misc = misc;
    }

    /// <summary>Where <see cref="IRandom.Spawn"/> stood. Stream index 0.</summary>
    public ulong Spawn { get; }

    /// <summary>Where <see cref="IRandom.Offers"/> stood. Stream index 1.</summary>
    public ulong Offers { get; }

    /// <summary>Where <see cref="IRandom.Affixes"/> stood. Stream index 2.</summary>
    public ulong Affixes { get; }

    /// <summary>Where <see cref="IRandom.Drops"/> stood. Stream index 3.</summary>
    public ulong Drops { get; }

    /// <summary>Where <see cref="IRandom.Misc"/> stood. Stream index 4.</summary>
    public ulong Misc { get; }
}

/// <summary>
/// One independent sequence of random values. Draws advance only this stream.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. Weighted picks and shuffles land here when the director needs them
/// (M2) — adding them speculatively would fix their semantics before there is a caller to
/// judge them against.
/// </para>
/// <para>
/// <b>A stream cannot say where it is, and cannot be moved.</b> Its position is read and written
/// through <see cref="IRandom.Capture"/> and <see cref="IRandom.Restore"/> instead, deliberately:
/// a settable position here would be reachable from every core system that holds a stream, so any
/// behaviour could rewind the sequence it draws from, and the determinism bug that caused would
/// read as a content bug for a week. Composition holds the <see cref="IRandom"/>; nothing else
/// needs the door (M2-13a).
/// </para>
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

    /// <summary>
    /// Where every stream stands right now, so a run can be written down and carried on rather
    /// than restarted. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// Here rather than on <see cref="IRandomStream"/> — one door instead of five, held by the
    /// composition root, and no core system's vocabulary changes at all. See
    /// <see cref="IRandomStream"/>'s remarks for why the five-door version was refused.
    /// </remarks>
    RandomState Capture();

    /// <summary>
    /// Puts every stream back where <paramref name="state"/> says it was. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seed is not restored</b>, and cannot be: it is a constructor argument that selects
    /// each stream's sequence, so a generator built from the run's seed already walks the right
    /// one and only its position is missing. That is what makes this a complete restore.
    /// </para>
    /// <para>
    /// <b>Nothing is refused.</b> A state captured under a different seed is indistinguishable
    /// from a legitimate one — every value is a legal position — so a guard here would be a check
    /// that cannot fail correctly. The agreement between a snapshot's seed and its state is
    /// checkable exactly where both are visible, which is <c>RunSession.Start</c>, where M2-02
    /// already throws on a seed the generator disagrees with.
    /// </para>
    /// <para>
    /// <c>in</c> rather than by value, unlike every parameter on <c>ISaveStore</c>: this is a core
    /// call with no async implementation imaginable, so the by-ref parameter that would one day
    /// block an <c>async</c> adapter costs nothing here.
    /// </para>
    /// </remarks>
    void Restore(in RandomState state);
}
