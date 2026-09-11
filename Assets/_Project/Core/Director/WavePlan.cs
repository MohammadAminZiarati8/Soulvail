using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Director;

/// <summary>
/// One archetype's share of one wave: what, and how many of it.
/// </summary>
/// <remarks>
/// <para>
/// Aggregated rather than enumerated — nine Husks are one entry with a <see cref="Count"/> of 9,
/// not nine entries. The plan says <em>what</em> a wave is made of; spacing them out in time and
/// placing them in the arena is the director's (M2-05).
/// </para>
/// <para>
/// A <see langword="readonly"/> struct for the reason <see cref="RosterEntry"/> is one: a wave is
/// a handful of pairs, and a class per pair would be an allocation per archetype in a type that is
/// refilled every stage.
/// </para>
/// </remarks>
public readonly struct WaveEntry
{
    /// <param name="specId">The archetype's id, e.g. <c>enemy.husk</c>.</param>
    /// <param name="count">How many of it this wave holds. At least 1.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="specId"/> is <c>default(ContentId)</c> — an entry that names no archetype.
    /// Refused here rather than where it is spawned, for the reason <see cref="RosterEntry"/>
    /// gives: left alone it surfaces one layer down as the catalog's "no enemy with id ''",
    /// pointing at content that was never at fault.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is below 1. An entry for nought of something is a wave that reads
    /// as containing an archetype it does not contain — and <see cref="WavePlan"/>'s contract is
    /// that every entry it hands out is a body the director has to spawn.
    /// </exception>
    public WaveEntry(ContentId specId, int count)
    {
        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) has none.",
                nameof(specId));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "count must be at least 1. A zero-count entry is an archetype a wave claims to "
                    + "hold and does not; the composer drops it instead of writing one.");
        }

        SpecId = specId;
        Count = count;
    }

    /// <summary>The archetype, e.g. <c>enemy.husk</c>.</summary>
    public ContentId SpecId { get; }

    /// <summary>How many of it this wave holds. Always at least 1.</summary>
    public int Count { get; }
}

/// <summary>
/// A stage's composition: how many waves, what each one holds, how many bodies may stand in the
/// arena at once, and what the concurrency cap refused to let the stage buy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Allocated once per run and refilled per stage — never per wave.</b> That is the whole reason
/// this is a class with capacity rather than a freshly built list: a stage boundary is a moment
/// the player is standing still and a GC spike there is as visible as one mid-fight.
/// <see cref="WaveComposer.Compose"/> overwrites every field it reads back, so nothing of the
/// previous stage survives into the next one (M2-04 rule 9).
/// </para>
/// <para>
/// <b>It is written through <see langword="internal"/> members and read through public ones.</b>
/// The same bargain <c>RunState</c> makes (AR §18.2): a public setter on
/// <see cref="UnspentThreat"/> or an exposed entry array would let a director, a view or a test
/// rewrite a composition the seed is supposed to determine, with nothing in the compiler to
/// object. <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>, so every assertion in
/// <c>WavePlanTests</c> arrives here through a real <see cref="WaveComposer"/> — which is also the
/// only route M2-05 will have.
/// </para>
/// <para>
/// <b>Out of range throws rather than answering <c>default</c>.</b> A zeroed
/// <see cref="WaveEntry"/> names no archetype, so a silent one would surface one layer down as the
/// catalog's "no enemy with id ''" — the failure pointing at content instead of at the index
/// (M2-04 rule 10).
/// </para>
/// </remarks>
public sealed class WavePlan
{
    /// <summary>Entries for every wave, wave-major: wave <c>w</c> starts at <c>(w−1) · _stride</c>.</summary>
    /// <remarks>
    /// One flat array rather than an array of arrays, because a jagged array is
    /// <c>maxWaves</c> allocations and the same indexing arithmetic written out by the runtime
    /// instead of here.
    /// </remarks>
    private readonly WaveEntry[] _entries;

    /// <summary>How many entries each wave's slice holds — <c>maxEntriesPerWave</c>.</summary>
    private readonly int _stride;

    /// <summary>How many of each wave's slice is in use. Indexed from 0 for wave 1.</summary>
    private readonly int[] _entryCounts;

    /// <summary>Σ <see cref="WaveEntry.Count"/> per wave, kept as it is written.</summary>
    /// <remarks>
    /// Stored rather than summed on demand: the director asks "how many bodies is this wave" every
    /// time it paces one, and the sum cannot change between compositions.
    /// </remarks>
    private readonly int[] _bodyCounts;

    /// <param name="maxWaves">
    /// The most waves any stage may be composed into — GD §12.2's wave curve maxes at 5, so that
    /// is what a run allocates. A plan asked to hold more throws rather than truncating.
    /// </param>
    /// <param name="maxEntriesPerWave">
    /// The most distinct archetypes any one wave may hold: the mode's roster length, since
    /// entries are aggregated per archetype and a wave cannot contain one that is not eligible.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either argument is below 1. A plan with no room is one every composition throws against,
    /// and the throw would name the composer rather than the capacity that is wrong.
    /// </exception>
    public WavePlan(int maxWaves, int maxEntriesPerWave)
    {
        if (maxWaves < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWaves),
                maxWaves,
                "maxWaves must be at least 1. GD §12.2's curve never asks for fewer than 2.");
        }

        if (maxEntriesPerWave < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEntriesPerWave),
                maxEntriesPerWave,
                "maxEntriesPerWave must be at least 1. It is the mode's roster length — a mode "
                    + "with an empty roster has nothing to compose and is not composed.");
        }

        _stride = maxEntriesPerWave;
        _entries = new WaveEntry[maxWaves * maxEntriesPerWave];
        _entryCounts = new int[maxWaves];
        _bodyCounts = new int[maxWaves];
        MaxWaves = maxWaves;
    }

    /// <summary>The depth this composition is for. 0 before anything has been composed into it.</summary>
    public int Stage { get; private set; }

    /// <summary>How many waves the stage is delivered in — GD §12.2's W(n). 0 before composition.</summary>
    public int WaveCount { get; private set; }

    /// <summary>
    /// The most bodies that may be alive at once during this stage — GD §12.2's C(n), already
    /// capped at the device tier.
    /// </summary>
    /// <remarks>
    /// A property of the stage rather than of a wave: the director holds the whole arena to it
    /// while a wave is being spawned, and the composer holds each wave's body count to it.
    /// </remarks>
    public int Concurrency { get; private set; }

    /// <summary>
    /// What the stage's budget could not be spent on — the threat the concurrency cap refused.
    /// </summary>
    /// <remarks>
    /// <b>Reported rather than discarded, and it is not waste.</b> GD §11.2's rule is that a phone
    /// that cannot draw more bodies spends its surplus on better ones, which is what the composer's
    /// upgrade pass does; what is left when nothing more expensive is affordable is this. M7-02's
    /// Elites are what eventually spend it — 2.5× the cost for one body (GD §8.3) is upgrading by
    /// another name. Never negative: the one body a stage is allowed to overspend on
    /// (<see cref="WaveComposer"/> rule 8) is reported as zero surplus rather than as a debt.
    /// </remarks>
    public float UnspentThreat { get; private set; }

    /// <summary>The most waves this plan can hold — what it was allocated for.</summary>
    internal int MaxWaves { get; }

    /// <summary>The most distinct archetypes any one of its waves can hold.</summary>
    internal int MaxEntriesPerWave => _stride;

    /// <summary>How many distinct archetypes wave <paramref name="wave"/> holds.</summary>
    /// <param name="wave">The wave, numbered from 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="wave"/> is outside <c>1..WaveCount</c>.
    /// </exception>
    public int EntryCount(int wave) => _entryCounts[Index(wave)];

    /// <summary>One archetype's share of wave <paramref name="wave"/>.</summary>
    /// <param name="wave">The wave, numbered from 1.</param>
    /// <param name="index">
    /// Which entry, from 0. Entries are in the mode's roster order, which is a statement about
    /// composition and not about spawn order — the director decides that.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="wave"/> is outside <c>1..WaveCount</c>, or <paramref name="index"/> is
    /// outside that wave's entries.
    /// </exception>
    public WaveEntry Entry(int wave, int index)
    {
        int waveIndex = Index(wave);
        int count = _entryCounts[waveIndex];

        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Wave {wave} of stage {Stage} holds {count} entries, so the index must be "
                    + $"between 0 and {count - 1}.");
        }

        return _entries[(waveIndex * _stride) + index];
    }

    /// <summary>How many bodies wave <paramref name="wave"/> holds — Σ every entry's count.</summary>
    /// <param name="wave">The wave, numbered from 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="wave"/> is outside <c>1..WaveCount</c>.
    /// </exception>
    public int BodyCount(int wave) => _bodyCounts[Index(wave)];

    /// <summary>
    /// Restates the stage, the wave count and the concurrency, and empties every wave.
    /// </summary>
    /// <remarks>
    /// <b>Every field a reader can see is written here or in <see cref="Complete"/></b>, which is
    /// what makes reuse safe: a plan that kept the previous stage's
    /// <see cref="UnspentThreat"/> or its fourth wave would be a pooled object remembering its
    /// last life, the failure AR §18.4 names for <c>EnemyView</c> and the one
    /// <c>Compose_ReusesPlan</c> exists to catch.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="waveCount"/> is outside <c>1..MaxWaves</c>, or <paramref name="stage"/> or
    /// <paramref name="concurrency"/> is below 1.
    /// </exception>
    internal void Begin(int stage, int waveCount, int concurrency)
    {
        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage, "Stages are numbered from 1.");
        }

        if (waveCount < 1 || waveCount > MaxWaves)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waveCount),
                waveCount,
                $"This plan holds {MaxWaves} waves and stage {stage} asks for {waveCount}. Size "
                    + "the plan to the wave curve's maximum once per run, not per stage.");
        }

        if (concurrency < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concurrency),
                concurrency,
                "concurrency must be at least 1. A stage that may hold no enemies is one nothing "
                    + "can be composed for.");
        }

        Stage = stage;
        WaveCount = waveCount;
        Concurrency = concurrency;
        UnspentThreat = 0f;

        // Only the wave slices this composition will use need clearing, and SetWave rewrites each
        // of those outright — but the counts are what every reader bounds itself by, so they are
        // zeroed for the whole array. A wave past waveCount then reads as empty rather than as
        // whatever the last, longer stage left there.
        Array.Clear(_entryCounts, 0, _entryCounts.Length);
        Array.Clear(_bodyCounts, 0, _bodyCounts.Length);
    }

    /// <summary>
    /// Writes wave <paramref name="wave"/>: one entry per archetype with a non-zero count, in
    /// roster order.
    /// </summary>
    /// <param name="wave">The wave, numbered from 1.</param>
    /// <param name="roster">The eligible archetypes, roster order.</param>
    /// <param name="counts">How many of each was bought, parallel to <paramref name="roster"/>.</param>
    /// <param name="length">How much of both spans is in use.</param>
    /// <remarks>
    /// Takes the composer's working arrays rather than a list of entries, so composing a wave
    /// allocates nothing: the <see cref="WaveEntry"/> values are built in place in the plan's own
    /// storage. Zero counts are skipped, which is what keeps <see cref="Entry"/>'s promise that
    /// every entry it hands out is a body to spawn — an archetype the draws never picked, or one
    /// the upgrade pass emptied, is simply not in the wave.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="wave"/> is outside <c>1..WaveCount</c>, or more than
    /// <see cref="MaxEntriesPerWave"/> archetypes have a non-zero count.
    /// </exception>
    internal void SetWave(int wave, ReadOnlySpan<RosterEntry> roster, ReadOnlySpan<int> counts, int length)
    {
        int waveIndex = Index(wave);
        int written = 0;
        int bodies = 0;

        for (int i = 0; i < length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            if (written == _stride)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    length,
                    $"Wave {wave} holds more than the {_stride} archetypes this plan was sized "
                        + "for. Size maxEntriesPerWave to the mode's roster length.");
            }

            _entries[(waveIndex * _stride) + written] = new WaveEntry(roster[i].SpecId, counts[i]);
            written++;
            bodies += counts[i];
        }

        _entryCounts[waveIndex] = written;
        _bodyCounts[waveIndex] = bodies;
    }

    /// <summary>Closes the composition with what the stage could not spend.</summary>
    /// <remarks>
    /// Clamped at zero rather than guarded, because a negative surplus is a real outcome of one
    /// real rule: a stage whose budget cannot afford the cheapest thing in its roster still opens
    /// with one body (<see cref="WaveComposer"/> rule 8), and that overspend is a deliberate
    /// decision rather than an arithmetic fault worth throwing over.
    /// </remarks>
    internal void Complete(float unspentThreat)
    {
        UnspentThreat = unspentThreat > 0f ? unspentThreat : 0f;
    }

    /// <summary>Turns a 1-based wave number into an array index, refusing anything outside the plan.</summary>
    /// <remarks>
    /// Bounded by <see cref="WaveCount"/> and not by the allocated length, deliberately: a plan
    /// sized for five waves holding a three-wave stage must refuse wave 4, or a reader sees the
    /// empty slice as a wave that exists and is empty.
    /// </remarks>
    private int Index(int wave)
    {
        if (wave < 1 || wave > WaveCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wave),
                wave,
                WaveCount == 0
                    ? "This plan holds no composition yet — nothing has been composed into it, so "
                        + "it has no waves. Call WaveComposer.Compose first."
                    : $"Stage {Stage} has {WaveCount} waves, numbered from 1.");
        }

        return wave - 1;
    }
}
