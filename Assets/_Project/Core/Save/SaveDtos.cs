using System;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Save;

// The two things that survive an app kill, grouped in one file like `EnemyEvents.cs` and for the
// same reason: they are the save format's whole vocabulary, and reading it in one place is worth
// more than one type per file. See AR §10.3, §11.6 and
// <../../../../Docs/adr/0007-save-store-async-local-first-versioned.md>.
//
// Both are `readonly struct`s, and that is what makes AR §10.3's `Task<PlayerProfile?>` and
// `Task<RunSnapshot?>` compile as written: the project enables nullable reference types nowhere,
// so those return types are `Nullable<T>` — a value that is there or is not — rather than nullable
// references, which would need the language feature switched on for one file on the strength of
// two signatures.
//
// Both carry `int Version`, and `default` carries 0. `CurrentVersion` starts at 1, so the zeroed
// form a struct can always be built is the one value no writer can produce and every reader
// already has to refuse — which is AR §18.3's "a struct with an invariant needs the check at both
// ends" answered without a second concept to maintain.

/// <summary>
/// A run, written at a stage boundary and read back after an app kill (GD §7.3). Complete enough
/// to rebuild the run, including where every random stream had got to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>WorldSnapshot</c>.</b> That is the per-frame block of facts Unity hands core, it
/// lives in <c>Core/Run</c>, and it has nothing to do with this. The names are AR §10.3's and are
/// kept rather than disambiguated, because the save format's names are the ones that end up in a
/// file on a player's device.
/// </para>
/// <para>
/// <b>What is deliberately not in it:</b> live enemies, waves and projectiles. The write point has
/// none by construction — M2-10 clears both systems at the stage boundary — and the ruling is that
/// a resume restarts the stage whole rather than mid-fight. Essence, Shards and unlocks are absent
/// because the mechanics that own them are not built; AR §6's rule that a port grows a member when
/// its mechanic lands reads the same way for a save format, and a field written at v1 that nothing
/// reads is a field every later migration carries for ever.
/// </para>
/// <para>
/// <b>v2 is what a levelled run is made of</b>, and it is the first bump this format has ever
/// taken: <see cref="Level"/>, <see cref="Xp"/>, <see cref="PendingLevelUps"/> and
/// <see cref="TakenNodeIds"/> in one step (ledger row 2, ruled at M3-00a). The fourth has no writer
/// until M3-03 and is written empty until then, which is the one place the rule above is knowingly
/// traded against: the reader is two tasks away in the same milestone, and the alternative is a
/// second step in the chain, for ever, for a format nobody has shipped. <b>M3-03 fills the field
/// and does not bump the version</b>; <c>Fixture_V3Run_IsWhatThisBuildWrites</c> is the row that
/// objects if it does.
/// </para>
/// <para>
/// <b>v3 is the loadout, and it is one field</b> (M3-07b): <see cref="ManualSkillIds"/>, CC §6.2's
/// four thumb positions exactly as the runner holds them. It carries slot <em>positions</em> rather
/// than a set of Manual ids, because which skills are Manual is recoverable from the list and
/// <em>where each one sits under the thumb</em> is recoverable from nothing else — a set would come
/// back compacted into S1…Sn, which is the silent re-bind M3-07a rule 3 refuses to do during a run
/// and has no business doing across a restart either.
/// </para>
/// <para>
/// <b>What v3 still does not carry is cooldowns</b>, and the reason is not the one M3-00b wrote
/// down. That spec said saving them would need <c>RunState.Time</c> to mean something across a
/// process death <em>"which it deliberately does not"</em> — but it does, and always has:
/// <see cref="RunTime"/> is a v1 field and <c>RunSession.Start</c> restores <c>State.Time</c> from
/// it exactly. The true reason is cost and review size. <c>SkillRunner._readyAt</c> is keyed by the
/// runner's registration order, which is <em>derived</em> from <see cref="TakenNodeIds"/>, so the
/// only safe spelling on disk is a keyed pair of lists with its own length, duplicate and
/// non-finite guards — not one field — and it does not belong in the PR that ships this format's
/// first two-step chain. <b>The later bump is cheap and that is a fact about the code rather than a
/// hope</b>: <c>_readyAt</c> is absolute against a clock that is itself restored exactly, so a v4
/// step is add-only, which is the case <c>MigrateRun</c>'s signature already handles. <b>The known
/// gap meanwhile</b>: quitting at a boundary and resuming returns every skill ready, so a
/// <c>Continue</c> is a free cooldown reset — bounded by the longest cooldown and by a stage's
/// opening seconds, recorded here rather than left to be discovered.
/// </para>
/// </remarks>
public readonly struct RunSnapshot
{
    /// <summary>The format this build writes. Bumped by the migration that changes the shape.</summary>
    public const int CurrentVersion = 3;

    /// <summary>
    /// The four empty slots, shared: what <see cref="ManualSkillIds"/> answers for
    /// <c>default(RunSnapshot)</c>, and what <see cref="CopySlots"/> returns for a run with nothing
    /// on Manual.
    /// </summary>
    /// <remarks>
    /// <b>Static readonly and immutable, which is not what AR §13 bans</b> — that is static
    /// <em>mutable</em> state. This is <see cref="Array.Empty{T}"/>'s trick with a length: four
    /// <c>default(ContentId)</c>s are indistinguishable from any other four, and a read-only
    /// wrapper over them cannot be written through, so one instance answers every caller that has
    /// no loadout. That is what keeps <c>RunRecorder.Take</c>
    /// allocation-free for a run with no Manual skills — see <see cref="ManualSkillIds"/>.
    /// </remarks>
    private static readonly IReadOnlyList<ContentId> EmptySlots =
        Array.AsReadOnly(new ContentId[SkillRunner.MaxManualSlots]);

    /// <summary>
    /// The nodes, copied and wrapped. Null only for <c>default(RunSnapshot)</c>, which
    /// <see cref="TakenNodeIds"/> answers as an empty list — a struct always has a zeroed form, and
    /// this is the field that would otherwise hand a reader a null (AR §18.3).
    /// </summary>
    private readonly IReadOnlyList<ContentId> _takenNodeIds;

    /// <summary>
    /// The four slots, copied and wrapped. Null only for <c>default(RunSnapshot)</c>, which
    /// <see cref="ManualSkillIds"/> answers as <see cref="EmptySlots"/>.
    /// </summary>
    private readonly IReadOnlyList<ContentId> _manualSkillIds;

    /// <param name="version">
    /// The format the snapshot is written in. <see cref="CurrentVersion"/> for anything this build
    /// produces; an older number is what a migration is handed.
    /// </param>
    /// <param name="modeId">The mode the run is playing, e.g. <c>mode.descent</c>.</param>
    /// <param name="characterId">The class being played, e.g. <c>character.oathbound</c>.</param>
    /// <param name="seed">
    /// What the run's generator was seeded with. Any <see cref="int"/> is a legal seed, including
    /// zero and negatives, so there is nothing to validate.
    /// </param>
    /// <param name="stageIndex">
    /// The stage this run resumes <em>at</em> — the one the player had not started yet.
    /// </param>
    /// <param name="random">Where every stream stood at the write. See <see cref="RandomState"/>.</param>
    /// <param name="playerHp">Hit points remaining. Zero is legal: a snapshot is never written dead, but a reader must not be the thing that decides that.</param>
    /// <param name="playerShield">Shield remaining, zero when there is none.</param>
    /// <param name="runTime">Simulated seconds elapsed — <c>RunState.Time</c>.</param>
    /// <param name="writtenAt">Wall-clock at the write, from <see cref="IClock.UtcNow"/>.</param>
    /// <param name="level">The player's level, from 1. A run that has never levelled carries 1.</param>
    /// <param name="xp">
    /// Experience <em>into</em> the current level, never the run's cumulative total — the number
    /// <c>LevelTracker.Xp</c> holds. Absolute rather than a fraction, for
    /// <paramref name="playerShield"/>'s reason: a fraction cannot be restored without the maximum
    /// that produced it, and here that maximum is <c>XpToNext</c>, which moves with the level and
    /// with whatever curve the mode ships next.
    /// </param>
    /// <param name="pendingLevelUps">Picks the player has earned and not yet been given.</param>
    /// <param name="takenNodeIds">
    /// The tree nodes taken, in the order they were taken. Copied; the caller's list is not
    /// retained. Empty until M3-03 has a tree to write down.
    /// </param>
    /// <param name="manualSkillIds">
    /// CC §6.2's four thumb positions, in order, with <c>default(ContentId)</c> for an empty one.
    /// Exactly <see cref="SkillRunner.MaxManualSlots"/> long, always. Copied; the caller's list is
    /// not retained — and here that is a correctness requirement rather than a convention, because
    /// the list this is handed is <c>SkillRunner.Slots</c>, a <em>live</em> view over the runner's
    /// own table that <c>SetAutoCast</c> writes through.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below 1, <paramref name="stageIndex"/> is below 1,
    /// <paramref name="level"/> is below 1, <paramref name="pendingLevelUps"/> is negative, or any
    /// of <paramref name="playerHp"/>, <paramref name="playerShield"/>, <paramref name="runTime"/>
    /// and <paramref name="xp"/> is negative or non-finite.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="takenNodeIds"/> or <paramref name="manualSkillIds"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// An entry of <paramref name="takenNodeIds"/> is <c>default(ContentId)</c> and so names no
    /// node. Entries are <em>not</em> resolved against the catalog here: a node id this build no
    /// longer ships is content validation's answer at <c>RunSession.Start</c> (M3-03), with the
    /// diagnostic that names it, exactly as <paramref name="modeId"/> is treated below. Or
    /// <paramref name="manualSkillIds"/> is not exactly <see cref="SkillRunner.MaxManualSlots"/>
    /// long, or names one skill twice — <b>but an entry of <c>default(ContentId)</c> there is
    /// legal, and that is the exact opposite of the rule above.</b> See
    /// <see cref="ManualSkillIds"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This constructor guards, where <c>RunState</c>'s deliberately does not</b> (AR §18.2).
    /// The difference is who can reach it: <c>RunState</c> is built only from inside core, so its
    /// guards would be checks against our own mistakes, while these values arrive from a file
    /// written by an older build or edited by hand. This is a real boundary, and the guard is what
    /// stops a hand-edited stage of 0 reaching the depth scaling that would quietly compute a
    /// stage-zero curve from it.
    /// </para>
    /// <para>
    /// <b><c>default(ContentId)</c> is not refused</b>, unlike in <c>RunConfig</c>. A mode id that
    /// resolves to nothing is content validation's answer to give, at <c>RunSession.Start</c>,
    /// with the diagnostic that already names the real problem — and unlike a composition mistake,
    /// a save naming content this build no longer ships is a migration's business, not a
    /// constructor's.
    /// </para>
    /// </remarks>
    public RunSnapshot(
        int version,
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        RandomState random,
        float playerHp,
        float playerShield,
        float runTime,
        DateTimeOffset writtenAt,
        int level,
        float xp,
        int pendingLevelUps,
        IReadOnlyList<ContentId> takenNodeIds,
        IReadOnlyList<ContentId> manualSkillIds)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "version must be at least 1. Zero is the value default(RunSnapshot) carries and no " +
                "writer can produce, so it means the snapshot was never written.");
        }

        if (stageIndex < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIndex),
                stageIndex,
                "stageIndex must be at least 1. Stages are numbered from 1, not from zero.");
        }

        // `!(v >= 0f)` rather than `v < 0f`: every comparison against NaN is false, so the natural
        // spelling admits NaN, and a NaN hit point total restored into a run is permanent (AR §18.3).
        if (!(playerHp >= 0f) || float.IsInfinity(playerHp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerHp),
                playerHp,
                "playerHp must be a finite value of at least 0.");
        }

        if (!(playerShield >= 0f) || float.IsInfinity(playerShield))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerShield),
                playerShield,
                "playerShield must be a finite value of at least 0.");
        }

        if (!(runTime >= 0f) || float.IsInfinity(runTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(runTime),
                runTime,
                "runTime must be a finite value of at least 0. It is simulated seconds elapsed, " +
                "which only ever grows.");
        }

        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "level must be at least 1. Every run starts at 1 and levelling only ever goes up, " +
                "so a zero is a file that was hand-edited or written by a build that counted from " +
                "an array index.");
        }

        // `!(v >= 0f)` rather than `v < 0f`, for playerHp's reason: NaN passes the natural spelling
        // and a NaN restored into a LevelTracker makes `while (Xp >= XpToNext)` false for ever, so
        // the player silently stops levelling (AR §18.3).
        if (!(xp >= 0f) || float.IsInfinity(xp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(xp),
                xp,
                "xp must be a finite value of at least 0. It is experience into the current level, " +
                "not the run's cumulative total.");
        }

        if (pendingLevelUps < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pendingLevelUps),
                pendingLevelUps,
                "pendingLevelUps must be 0 or more. A negative count is a run that owes the player " +
                "less than nothing, and LevelTracker.SpendLevelUp would refuse every pick.");
        }

        if (takenNodeIds is null)
        {
            throw new ArgumentNullException(
                nameof(takenNodeIds),
                "takenNodeIds must be a list, empty for a run that has taken no nodes. Null and " +
                "empty are not two ways of saying the same thing here — a reader must never have " +
                "to ask.");
        }

        if (manualSkillIds is null)
        {
            throw new ArgumentNullException(
                nameof(manualSkillIds),
                $"manualSkillIds must be a list of exactly {SkillRunner.MaxManualSlots}, all " +
                "default(ContentId) for a run with nothing on Manual. Null and four empties are " +
                "not two ways of saying the same thing here — a reader must never have to ask.");
        }

        Version = version;
        ModeId = modeId;
        CharacterId = characterId;
        Seed = seed;
        StageIndex = stageIndex;
        Random = random;
        PlayerHp = playerHp;
        PlayerShield = playerShield;
        RunTime = runTime;
        WrittenAt = writtenAt;
        Level = level;
        Xp = xp;
        PendingLevelUps = pendingLevelUps;
        _takenNodeIds = CopyNodes(takenNodeIds);
        _manualSkillIds = CopySlots(manualSkillIds);
    }

    /// <summary>
    /// The format this snapshot was written in. 0 for <c>default(RunSnapshot)</c>, which is the
    /// value no writer produces and every reader refuses.
    /// </summary>
    public int Version { get; }

    /// <summary>The mode being played. Resolved against the <c>ContentCatalog</c> at <c>Start</c>.</summary>
    public ContentId ModeId { get; }

    /// <summary>The class being played. Resolved against the <c>ContentCatalog</c> at <c>Start</c>.</summary>
    public ContentId CharacterId { get; }

    /// <summary>
    /// What the run's generator was seeded with — the identity half of a restore, of which
    /// <see cref="Random"/> is the position half.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// The stage this run resumes <em>at</em>: the one the player had not started yet, never the
    /// one they had just cleared.
    /// </summary>
    public int StageIndex { get; }

    /// <summary>
    /// Where every random stream stood at the write. With <see cref="Seed"/>, a complete restore;
    /// without it, a position on an unknown sequence.
    /// </summary>
    public RandomState Random { get; }

    /// <summary>Hit points remaining at the write.</summary>
    public float PlayerHp { get; }

    /// <summary>Shield remaining at the write. Zero when there is none.</summary>
    public float PlayerShield { get; }

    /// <summary>
    /// Simulated seconds — <c>RunState.Time</c>, the sum of every tick's <c>Dt</c>. Never
    /// wall-clock; see <see cref="WrittenAt"/> for that.
    /// </summary>
    public float RunTime { get; }

    /// <summary>
    /// <see cref="IClock.UtcNow"/> at the write, and the only wall-clock number a run carries.
    /// </summary>
    /// <remarks>
    /// Never compared with <see cref="RunTime"/> and never derived from it. A device clock moves
    /// backwards on a date change, on DST and on an NTP correction, and simulated time does not
    /// move at all while the app is backgrounded — two different numbers measuring two different
    /// things. Carrying both under names that cannot be confused is this type's whole contribution
    /// to keeping M2-01's rule true.
    /// </remarks>
    public DateTimeOffset WrittenAt { get; }

    /// <summary>The player's level at the write, from 1.</summary>
    public int Level { get; }

    /// <summary>
    /// Experience into the current level — <c>LevelTracker.Xp</c>, never <c>XpFraction</c>.
    /// </summary>
    /// <remarks>
    /// Absolute for the reason <see cref="PlayerShield"/> is (M2-14a rule 4), and the argument is
    /// sharper here: a fraction is over <c>XpToNext</c>, which moves with the level <em>and</em>
    /// with the mode's curve — and CH §5.2's exponent is flagged for a retune at M3-15. A run saved
    /// as "40 % of the way there" and resumed under a retuned curve would come back at a different
    /// number of points, silently, in the player's favour or against it depending on which way the
    /// curve went.
    /// </remarks>
    public float Xp { get; }

    /// <summary>Picks the player has earned and not yet been given.</summary>
    public int PendingLevelUps { get; }

    /// <summary>
    /// The tree nodes taken, in take order. Never null — empty for <c>default(RunSnapshot)</c>, and
    /// empty for every run this build writes until M3-03 has a tree to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Copied on the way in and wrapped, and that allocates.</b> <c>ContentCatalog.Index</c>'s
    /// reasoning — an array handed out as an <see cref="IReadOnlyList{T}"/> casts straight back to
    /// <c>ContentId[]</c>, and then the copy protects nothing — and the allocation is named here
    /// rather than hidden, because it is against M2-14a rule 7's <em>allocates nothing</em>.
    /// <b>Borrowing the caller's buffer is what cannot be done:</b> <c>SaveWriter</c>
    /// <em>enqueues</em> the write (<c>SaveWriter.cs:118</c>), so a snapshot is held across frames
    /// and a borrowed buffer would be rewritten under a save that had not happened yet. Twenty-seven
    /// ids twice a minute is the whole cost.
    /// </para>
    /// <para>
    /// <b>An empty list costs nothing</b>, which is what keeps <c>RunRecorder.Take</c> allocation-free
    /// for as long as it passes one: there is no copy to make, so the shared zero-length array
    /// answers. It is handed out unwrapped and that is safe for exactly one reason — a zero-length
    /// array has nothing to write through. M3-03 is where the paragraph above starts costing
    /// something.
    /// </para>
    /// <para>
    /// <b><c>default(ContentId)</c> is refused here and is legal in <see cref="ManualSkillIds"/>,
    /// and the contrast is deliberate</b> (M3-07b rule 3). In this list an entry that names nothing
    /// can only be a forgotten field, because a node the player took has an id; in that one it is
    /// the only way to spell <em>"this slot is empty"</em>, and refusing it would make an empty S2
    /// unsaveable. Two lists of <c>ContentId</c> on one struct with opposite rules is exactly what a
    /// later reader gets wrong, so it is written at both ends rather than only at the surprising one.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContentId> TakenNodeIds => _takenNodeIds ?? Array.Empty<ContentId>();

    /// <summary>
    /// CC §6.2's four thumb positions in order, <c>default(ContentId)</c> for an empty one — always
    /// exactly <see cref="SkillRunner.MaxManualSlots"/> long, and never null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed length rather than compacted, because that is what makes a <em>hole</em>
    /// expressible</b> (M3-07a rule 3). A player with skills in S1 and S3 has two buttons, not two
    /// adjacent ones, and a list that dropped the gap would restore them under the wrong thumbs.
    /// Four empties for <c>default(RunSnapshot)</c>, and four empties for every run until a player
    /// switches something to Manual — which is also CC §6.1's default, so a migrated v2 run is
    /// indistinguishable from a fresh one on this axis.
    /// </para>
    /// <para>
    /// <b><c>default(ContentId)</c> means an empty slot here, and the opposite in
    /// <see cref="TakenNodeIds"/>.</b> See that property; the rule is written at both ends on purpose.
    /// <b>What is refused instead is a wrong length and a repeated id</b> — the first because the
    /// positions <em>are</em> the state, the second because one skill cannot sit under two thumbs, so
    /// a list naming it twice is a corrupt file rather than a stale one. A slot naming a skill this
    /// run does not own is neither: that is ordinary staleness and <c>SkillRunner.Restore</c> drops
    /// it silently (M3-07b rule 6).
    /// </para>
    /// <para>
    /// <b>Copied and wrapped, and unlike <see cref="TakenNodeIds"/> that is required for
    /// correctness rather than convention</b>: what a recorder passes is <c>SkillRunner.Slots</c>, a
    /// live view over the runner's own table, and <c>SaveWriter</c> enqueues the write — so a
    /// borrowed one would be rewritten by the next <c>SetAutoCast</c>, under a save that had not
    /// happened yet. <b>A run with nothing on Manual still costs nothing</b>, by
    /// <see cref="EmptySlots"/>: four empties are indistinguishable, so there is one shared
    /// instance and no copy to make. The first skill set to Manual is the first boundary write to
    /// ask for heap on this field, which is the same trade <see cref="TakenNodeIds"/> names for the
    /// first node taken.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContentId> ManualSkillIds => _manualSkillIds ?? EmptySlots;

    /// <summary>
    /// <paramref name="nodes"/> as a list of this snapshot's own, refusing an entry that names
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Indexed rather than <c>foreach</c>ed, so the guard and the copy are one pass over an
    /// <see cref="IReadOnlyList{T}"/> without an enumerator — the shape
    /// <c>ContentCatalog.Index</c> uses, for the same reason.
    /// </remarks>
    private static IReadOnlyList<ContentId> CopyNodes(IReadOnlyList<ContentId> takenNodeIds)
    {
        if (takenNodeIds.Count == 0)
        {
            return Array.Empty<ContentId>();
        }

        var copy = new ContentId[takenNodeIds.Count];

        for (int i = 0; i < takenNodeIds.Count; i++)
        {
            ContentId node = takenNodeIds[i];

            if (node.Value is null)
            {
                throw new ArgumentException(
                    $"takenNodeIds[{i}] is default(ContentId) and names no node. An id this build " +
                    "no longer ships is content validation's answer to give at RunSession.Start; " +
                    "an id that is not an id at all is a file that cannot be read.",
                    nameof(takenNodeIds));
            }

            copy[i] = node;
        }

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// <paramref name="manualSkillIds"/> as a list of this snapshot's own, refusing a wrong length
    /// and a repeated id — and accepting <c>default(ContentId)</c>, which is an empty slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A run with nothing on Manual returns the shared <see cref="EmptySlots"/> and allocates
    /// nothing</b>, which is <see cref="CopyNodes"/>' zero-length shortcut with a length: there is
    /// nothing in four empties for a later write to change, so one instance is safe to hand to
    /// everybody. <b>That is why the occupancy scan comes first and the array is allocated after
    /// it</b> — allocating the copy up front and returning the shared one at the end would leave a
    /// dead four-element array behind on the very path <c>Take_AllocatesNothing</c> measures, and
    /// the row would go red for a reason that looked like the guards.
    /// </para>
    /// <para>
    /// The duplicate check is the inner loop over what has already been read, which is at most six
    /// comparisons — a nested walk rather than a set, because allocating a <c>HashSet</c> to
    /// deduplicate four entries would cost more than the walk it replaced.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ContentId> CopySlots(IReadOnlyList<ContentId> manualSkillIds)
    {
        if (manualSkillIds.Count != SkillRunner.MaxManualSlots)
        {
            throw new ArgumentException(
                $"manualSkillIds must name exactly {SkillRunner.MaxManualSlots} slots and names " +
                $"{manualSkillIds.Count}. CC §6.2 draws four fixed thumb positions, so the length " +
                "is the format: an empty slot is default(ContentId) in place, never an entry left " +
                "out. A shorter list cannot say which of the four are empty.",
                nameof(manualSkillIds));
        }

        bool anyOccupied = false;

        for (int i = 0; i < SkillRunner.MaxManualSlots; i++)
        {
            // `default(ContentId)` is legal, and this is the one list in this struct where it is —
            // see ManualSkillIds. A hole is an ordinary state and is never compacted away.
            if (manualSkillIds[i].Value is null)
            {
                continue;
            }

            anyOccupied = true;

            for (int earlier = 0; earlier < i; earlier++)
            {
                if (manualSkillIds[earlier] == manualSkillIds[i])
                {
                    throw new ArgumentException(
                        $"manualSkillIds names '{manualSkillIds[i]}' in both slot {earlier} and " +
                        $"slot {i}. One skill sits under one thumb, so a list naming it twice is a " +
                        "corrupt file rather than a stale one — unlike a slot naming a skill this " +
                        "build no longer ships, which SkillRunner.Restore drops in silence.",
                        nameof(manualSkillIds));
                }
            }
        }

        if (!anyOccupied)
        {
            return EmptySlots;
        }

        var copy = new ContentId[SkillRunner.MaxManualSlots];

        for (int i = 0; i < SkillRunner.MaxManualSlots; i++)
        {
            copy[i] = manualSkillIds[i];
        }

        return Array.AsReadOnly(copy);
    }
}

/// <summary>
/// What survives every run: the player's own settings, and — when the mechanics that own them
/// exist — their Shards and unlocks.
/// </summary>
/// <remarks>
/// <b>One setting at v1, and Shards are not in it.</b> ADR-0007 names Shards and unlocks, and they
/// arrive with M4-06 and M6-08; reserving fields for them now would put two numbers nothing reads
/// into the first format every later migration has to carry. <see cref="HapticsEnabled"/> is here
/// because it has a consumer today: <c>HapticsSettings</c> currently persists through
/// <c>PlayerPrefs</c> as an explicit stopgap, and M2-13b is what moves it onto <c>ISaveStore</c>.
/// </remarks>
public readonly struct PlayerProfile
{
    /// <summary>The format this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <param name="version">The format the profile is written in.</param>
    /// <param name="hapticsEnabled">Whether the device is allowed to buzz (GD §16.3).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below 1 — the value <c>default(PlayerProfile)</c> carries.
    /// </exception>
    public PlayerProfile(int version, bool hapticsEnabled)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "version must be at least 1. Zero is the value default(PlayerProfile) carries and " +
                "no writer can produce, so it means the profile was never written.");
        }

        Version = version;
        HapticsEnabled = hapticsEnabled;
    }

    /// <summary>The format this profile was written in. 0 for <c>default(PlayerProfile)</c>.</summary>
    public int Version { get; }

    /// <summary>Whether haptics are on. Defaults to on (GD §16.3); the player may turn them off.</summary>
    public bool HapticsEnabled { get; }

    /// <summary>
    /// A profile for a player who has never had one: the current format, GD §16.3's defaults.
    /// </summary>
    /// <remarks>
    /// A property rather than <c>default</c>, because <c>default</c> is deliberately the
    /// <em>invalid</em> form — version 0 — and "there was no file" and "the file says nothing is
    /// on" have to be different answers. This is what a loader substitutes for the first;
    /// <see cref="ISaveStore.LoadProfile"/> returning null is how it learns which it has.
    /// </remarks>
    public static PlayerProfile Default => new PlayerProfile(CurrentVersion, hapticsEnabled: true);
}
