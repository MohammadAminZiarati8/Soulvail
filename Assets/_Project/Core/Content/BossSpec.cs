using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

// GD §9.1's boss, as data: the phases it passes through, the health fraction each begins at, how
// long the beat between them lasts, and what each phase may call in. Three types in one file for
// `ModeSpec.cs`' and `TriggerSpec.cs`' reason — a module's vocabulary read in one place is worth
// more than one type per file, and none of these three says anything without the other two.

/// <summary>
/// One summon a phase may make: an archetype and how many of it — GD §9.1 rule 4's <em>"summons
/// adds"</em>.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="readonly"/> struct rather than a class, for <see cref="RosterEntry"/>'s reason:
/// a phase's summons are a list of pairs, and a class per pair would be an allocation per row in a
/// type read at every phase change.
/// </para>
/// <para>
/// <b>It names an ordinary <see cref="EnemySpec"/> and nothing else.</b> A summoned Husk goes
/// through <c>EnemySystem.Spawn</c> like every other body, so it is scaled to the stage's depth,
/// registered in the same pool, targeted by the same scorer and worth the same experience — which
/// is the whole of why a boss is an <c>EnemyAgent</c> rather than a parallel system (M4-01b rule 2).
/// </para>
/// </remarks>
public readonly struct AddWave
{
    /// <param name="specId">The archetype to summon, e.g. <c>enemy.husk</c>.</param>
    /// <param name="count">How many. At least 1.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="specId"/> is <c>default(ContentId)</c> — a row that names no archetype.
    /// Refused where the summon is authored rather than where it is spawned, for the reason
    /// <see cref="RosterEntry"/> gives: left alone it would surface one layer down as the catalog's
    /// <em>"no enemy with id ''"</em>, pointing at content that was never at fault.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is below 1. A summon of nothing is not a quiet phase — it is a row
    /// a designer typed and a boss that silently calls in no help, which is the silence M2-06 rule
    /// 11 refuses. A phase that summons nothing authors no rows at all.
    /// </exception>
    public AddWave(ContentId specId, int count)
    {
        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) names no archetype.",
                nameof(specId));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"count for '{specId}' must be at least 1. A phase that summons nothing authors no "
                    + "AddWave at all, rather than one of zero.");
        }

        SpecId = specId;
        Count = count;
    }

    /// <summary>The archetype, resolved against the <see cref="ContentCatalog"/>.</summary>
    public ContentId SpecId { get; }

    /// <summary>How many of it to summon. At least 1.</summary>
    public int Count { get; }
}

/// <summary>
/// One phase of a boss fight: the health fraction it begins at, and what entering it calls in.
/// </summary>
/// <remarks>
/// <para>
/// <b>A phase is entered and never left</b> (M4-01b rule 3). <see cref="EntersBelow"/> is a
/// threshold rather than a band, and <c>BossPhases</c> latches it — healing a boss back over 66 %
/// does not put it back in phase 1, because the beat clears the arena and a boss oscillating
/// across a threshold would do that every few seconds.
/// </para>
/// <para>
/// <b>What a phase <em>does</em> is not here.</b> The Warden's shield-slam and its fissures are
/// M4-02's, on the behaviour the boss delegates to; this type carries only the two things the phase
/// machine itself needs to know. A phase that fights differently is a behaviour question, and one
/// that calls in help is this.
/// </para>
/// </remarks>
public sealed class BossPhaseSpec
{
    private readonly AddWave[] _summons;

    private readonly ReadOnlyCollection<AddWave> _summonsView;

    /// <param name="entersBelow">
    /// Health fraction at or under which this phase begins, in <c>(0, 1]</c>. 1.0 for the first
    /// phase — a boss is in its opening phase from full health — then 0.66 and 0.33 for the
    /// Warden (GD §9.1 rule 3).
    /// </param>
    /// <param name="summons">
    /// What entering this phase calls in, or null for a phase that calls in nothing. Copied; the
    /// caller's list is not retained.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="entersBelow"/> is not a finite number in <c>(0, 1]</c>. Zero is refused
    /// because a phase entered at no health is one entered on the frame the boss dies; above 1 is a
    /// phase entered before the fight starts; and NaN is refused explicitly, because every
    /// comparison against it is false — a NaN threshold is a phase that is silently never entered
    /// (AR §18.3).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A summon row is <c>default(AddWave)</c> and so names no archetype. A struct always has a
    /// zeroed form, so the check the constructor makes is repeated here (AR §18.3).
    /// </exception>
    public BossPhaseSpec(float entersBelow, IReadOnlyList<AddWave> summons = null)
    {
        // Asked as "is it outside the range or NaN?" rather than as a pair of comparisons, for the
        // reason AR §18.3 gives: every comparison against NaN is false, so `> 0 && <= 1` waves it
        // straight through.
        if (!(entersBelow > 0f) || !(entersBelow <= 1f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(entersBelow),
                entersBelow,
                "entersBelow must be a finite health fraction in (0, 1]. 1 is the opening phase; "
                    + "0.66 and 0.33 are GD §9.1 rule 3's other two.");
        }

        EntersBelow = entersBelow;

        _summons = CopySummons(summons, entersBelow);

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to AddWave[], and then the copy protects nothing.
        // The same guard ContentCatalog, ModeSpec and TriggerSpec make.
        _summonsView = Array.AsReadOnly(_summons);
    }

    /// <summary>Health fraction at or under which this phase begins. 1 for the first.</summary>
    public float EntersBelow { get; }

    /// <summary>What entering this phase summons, in the order authored. May be empty.</summary>
    public IReadOnlyList<AddWave> Summons => _summonsView;

    /// <summary>How many bodies this phase's summons ask for in total.</summary>
    /// <remarks>
    /// Read once, when a <c>BossBehaviour</c> sizes the table it tracks its adds in — so the whole
    /// of a fight's summoning is allocated at spawn rather than at a phase change, which is a
    /// moment the arena is already busy.
    /// </remarks>
    public int SummonedBodyCount
    {
        get
        {
            int total = 0;

            for (int i = 0; i < _summons.Length; i++)
            {
                total += _summons[i].Count;
            }

            return total;
        }
    }

    /// <summary>Copies the summon rows, refusing one that names nothing.</summary>
    /// <remarks>
    /// A duplicated archetype is deliberately <em>not</em> refused, unlike <see cref="ModeSpec"/>'s
    /// roster: two rows of <c>enemy.husk</c> are five Husks and three Husks, which is a legible
    /// thing for a designer to author, where two roster rows for one archetype could only disagree
    /// about which stage introduces it.
    /// </remarks>
    private static AddWave[] CopySummons(IReadOnlyList<AddWave> summons, float entersBelow)
    {
        if (summons is null || summons.Count == 0)
        {
            return Array.Empty<AddWave>();
        }

        var copy = new AddWave[summons.Count];

        for (int i = 0; i < summons.Count; i++)
        {
            AddWave wave = summons[i];

            if (wave.SpecId.Value is null)
            {
                throw new ArgumentException(
                    $"summons[{i}] of the phase entered below {entersBelow} names no archetype. A "
                        + "default(AddWave) has no spec id.",
                    nameof(summons));
            }

            copy[i] = wave;
        }

        return copy;
    }
}

/// <summary>
/// One boss, as authored data: the body it wears, the phases it passes through, and how long the
/// invulnerable beat between them lasts. Registered in the <see cref="ContentCatalog"/> beside
/// every other kind and named by a mode's boss roster. See AR §10.1, ADR-0006 and GD §9.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>It names an <see cref="EnemySpecId"/> rather than restating a body</b> (M4-01b rule 2). A
/// boss's hit points, contact damage, reach, telegraph times and experience value are authored on
/// an ordinary <see cref="EnemySpec"/> like every other creature in the game (ADR-0006), and this
/// type adds only what is true of a <em>boss</em>: that it has phases at all. The alternative — a
/// parallel boss body — would mean two spawn paths and two damage paths, which is the duplication
/// M2 spent three tasks removing.
/// </para>
/// <para>
/// <b>Which stages hold a boss is not here either.</b> That is the mode's statement, on
/// <see cref="BossRosterEntry"/>, for the reason the enemy roster is the mode's: a Boss Rush and a
/// Descent disagree about how often a boss appears and agree completely about what the Warden is
/// (GD §4.5).
/// </para>
/// <para>
/// Immutable and shared, like every other spec: the phase list is copied on construction, so a
/// builder that keeps filling its own list afterwards cannot change what the boss holds.
/// </para>
/// </remarks>
public sealed class BossSpec
{
    /// <summary>The health fraction the first phase must begin at — full.</summary>
    /// <remarks>
    /// A boss is in its opening phase from the moment it stands up, so the first threshold is the
    /// top of the range rather than a number a designer chooses. Authoring 0.9 there would give a
    /// boss a tenth of a fight in no phase at all.
    /// </remarks>
    public const float FirstPhaseEntersBelow = 1f;

    private readonly BossPhaseSpec[] _phases;

    private readonly ReadOnlyCollection<BossPhaseSpec> _phasesView;

    /// <param name="id">The boss's stable content id, e.g. <c>boss.warden</c>.</param>
    /// <param name="enemySpecId">
    /// The <see cref="EnemySpec"/> its body, health and contact damage come from (rule 2).
    /// </param>
    /// <param name="phases">
    /// Its phases, outermost first: the first enters at <see cref="FirstPhaseEntersBelow"/> and
    /// each one after it at a strictly lower fraction. Copied; the caller's list is not retained.
    /// </param>
    /// <param name="beatSeconds">
    /// How long the invulnerable beat between two phases lasts — GD §9.1 rule 3. Finite and
    /// greater than zero.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> or <paramref name="enemySpecId"/> is <c>default(ContentId)</c>;
    /// <paramref name="phases"/> is empty; a phase is null; the first phase does not enter at
    /// <see cref="FirstPhaseEntersBelow"/>; or two phases are out of order. The ordering is
    /// refused rather than sorted, because a list a designer typed out of order is a list they
    /// meant something else by, and silently reordering it would make the asset and the fight
    /// disagree.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="phases"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="beatSeconds"/> is not a finite number greater than zero. Infinity is called
    /// out because it looks like a number and means a boss that is invulnerable for the rest of the
    /// run; NaN because every comparison against it is false, so a beat measured against one never
    /// ends either (AR §18.3).
    /// </exception>
    public BossSpec(
        ContentId id,
        ContentId enemySpecId,
        IReadOnlyList<BossPhaseSpec> phases,
        float beatSeconds)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) names no boss.",
                nameof(id));
        }

        if (enemySpecId.Value is null)
        {
            throw new ArgumentException(
                $"'{id}' names no enemy spec. A boss wears an ordinary body (ADR-0006) and cannot "
                    + "be spawned without one.",
                nameof(enemySpecId));
        }

        if (phases is null)
        {
            throw new ArgumentNullException(nameof(phases));
        }

        // `!(beatSeconds > 0f)` rather than `<= 0f`, so NaN is refused too, and infinity asked
        // about separately because it passes a `> 0` test — EnemySpec.Positive's spelling.
        if (!(beatSeconds > 0f) || float.IsInfinity(beatSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beatSeconds),
                beatSeconds,
                $"beatSeconds for '{id}' must be a finite number greater than zero. The beat is "
                    + "what GD §9.1 rule 3 makes a phase change readable with; a boss without one "
                    + "changes phase between two frames and clears the arena with no warning.");
        }

        Id = id;
        EnemySpecId = enemySpecId;
        BeatSeconds = beatSeconds;

        _phases = CopyPhases(phases, id);

        _phasesView = Array.AsReadOnly(_phases);
    }

    /// <summary>Stable identity, e.g. <c>boss.warden</c>.</summary>
    public ContentId Id { get; }

    /// <summary>
    /// The <see cref="EnemySpec"/> this boss's body, health and contact damage come from.
    /// </summary>
    public ContentId EnemySpecId { get; }

    /// <summary>Its phases, outermost first. Never empty; the first enters at 1.</summary>
    public IReadOnlyList<BossPhaseSpec> Phases => _phasesView;

    /// <summary>
    /// Seconds the invulnerable beat between two phases lasts (GD §9.1 rule 3).
    /// </summary>
    /// <remarks>
    /// One number for every transition rather than one per phase: the beat is a property of the
    /// <em>change</em> — the arena clearing and the boss telegraphing what it is about to become —
    /// and nothing in GD §9.1 makes a later change longer than an earlier one. The day a boss wants
    /// two lengths, the number moves onto <see cref="BossPhaseSpec"/> along with the reason.
    /// </remarks>
    public float BeatSeconds { get; }

    /// <summary>The most bodies any one of this boss's phases summons.</summary>
    /// <remarks>
    /// What a <c>BossBehaviour</c> sizes its add table from, once, at spawn — see
    /// <see cref="BossPhaseSpec.SummonedBodyCount"/>.
    /// </remarks>
    public int MaxSummonedBodies
    {
        get
        {
            int most = 0;

            for (int i = 0; i < _phases.Length; i++)
            {
                int bodies = _phases[i].SummonedBodyCount;

                if (bodies > most)
                {
                    most = bodies;
                }
            }

            return most;
        }
    }

    /// <summary>
    /// Copies the phases, refusing an empty list, a null row, a first phase that does not begin at
    /// full health, and a pair that is out of order.
    /// </summary>
    private static BossPhaseSpec[] CopyPhases(IReadOnlyList<BossPhaseSpec> phases, ContentId id)
    {
        if (phases.Count == 0)
        {
            throw new ArgumentException(
                $"'{id}' authors no phases. A boss has at least the one it starts in — a fight "
                    + "with no phases is an ordinary enemy, and is spelled by not authoring a "
                    + "BossSpec at all.",
                nameof(phases));
        }

        var copy = new BossPhaseSpec[phases.Count];

        for (int i = 0; i < phases.Count; i++)
        {
            BossPhaseSpec phase = phases[i];

            if (phase is null)
            {
                throw new ArgumentException($"phases[{i}] of '{id}' is null.", nameof(phases));
            }

            if (i == 0)
            {
                if (phase.EntersBelow != FirstPhaseEntersBelow)
                {
                    throw new ArgumentException(
                        $"'{id}'s first phase enters below {phase.EntersBelow} rather than "
                            + $"{FirstPhaseEntersBelow}. A boss is in its opening phase from full "
                            + "health, so anything lower leaves the top of the fight in no phase.",
                        nameof(phases));
                }
            }
            else if (!(phase.EntersBelow < copy[i - 1].EntersBelow))
            {
                throw new ArgumentException(
                    $"'{id}'s phases are out of order: phases[{i}] enters below "
                        + $"{phase.EntersBelow}, which is not under phases[{i - 1}]'s "
                        + $"{copy[i - 1].EntersBelow}. Phases are authored outermost first and are "
                        + "entered one way only, so a flat or rising threshold is a phase that can "
                        + "never be reached.",
                    nameof(phases));
            }

            copy[i] = phase;
        }

        return copy;
    }
}
