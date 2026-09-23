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
/// The four scalars a run accumulates and spends — GD §15's economy table, minus the one that
/// outlives the run. One argument rather than four, for <see cref="RandomState"/>'s reason: a
/// constructor that already takes fourteen things does not get better by taking eighteen.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>default</c> is a legal fresh run</b> — nothing earned, nothing corrupted, nothing bought —
/// which is what makes the v3 → v4 step in <c>SaveMigrations</c> one token. AR §18.3's "a struct
/// with an invariant needs the check at both ends" is met because the zeroed form is the
/// <em>valid</em> opening state rather than an invalid one; the check that matters is on the way in.
/// </para>
/// <para>
/// <b>Four numbers behind one parameter, and the adjacent pair is why</b> (M6-01b rule 1).
/// <see cref="RerollsBought"/> and <see cref="RerollsSpent"/> are two <see cref="int"/>s that mean
/// opposite things, and eighteen positional arguments on <see cref="RunSnapshot"/>'s constructor
/// would leave them one transposition away from a bug no compiler could see. Behind a struct they
/// are named at the call site.
/// </para>
/// <para>
/// <b>One real value and three that nothing writes yet</b> (M6-01b). <see cref="Essence"/> is
/// M6-01a's and is captured by <c>RunRecorder.Take</c>; <see cref="Veilrot"/> is M6-04's and the two
/// counters are M6-02b's, and each of those tasks replaces exactly one argument at the recorder
/// <em>without</em> touching <see cref="RunSnapshot.CurrentVersion"/>. The format pays the ripple
/// once rather than three times.
/// </para>
/// </remarks>
public readonly struct RunEconomy
{
    /// <summary>
    /// The top of <see cref="Veilrot"/>'s range. GD §10.2's Claiming fires here, which is what makes
    /// it a ceiling rather than a convention.
    /// </summary>
    private const float MaxVeilrot = 100f;

    /// <param name="essence">What the wallet held at the write. Never negative.</param>
    /// <param name="veilrot">GD §10's meter, in [0, <see cref="MaxVeilrot"/>].</param>
    /// <param name="rerollsBought">How many rerolls this run has bought. Never negative.</param>
    /// <param name="rerollsSpent">How many of them have been used. Never more than were bought.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="essence"/>, <paramref name="rerollsBought"/> or
    /// <paramref name="rerollsSpent"/> is negative; <paramref name="veilrot"/> is non-finite or
    /// outside [0, 100]; or more rerolls are spent than were bought.
    /// </exception>
    public RunEconomy(int essence, float veilrot, int rerollsBought, int rerollsSpent)
    {
        if (essence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(essence),
                essence,
                "essence must be 0 or more. EssenceWallet.Spend refuses to overdraw, so a negative " +
                "balance is a hand-edited file rather than anything a writer can produce.");
        }

        if (rerollsBought < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rerollsBought),
                rerollsBought,
                "rerollsBought must be 0 or more. It is a count that only ever grows.");
        }

        if (rerollsSpent < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rerollsSpent),
                rerollsSpent,
                "rerollsSpent must be 0 or more. It is a count that only ever grows.");
        }

        // Guarded at both ends, and that is not symmetry for its own sake (rule 2). Negative is a
        // file somebody edited; above 100 is a file somebody edited *usefully*, because GD §10.2's
        // Claiming fires at 100 and a saved 10 000 would arrive Claimed with headroom nothing can
        // ever cleanse. `!(v >= 0f)` rather than `v < 0f`, so NaN is refused with the negatives —
        // playerHp's spelling, and here a NaN makes every threshold comparison false for the rest of
        // the run (AR §18.3).
        if (!(veilrot >= 0f) || veilrot > MaxVeilrot)
        {
            throw new ArgumentOutOfRangeException(
                nameof(veilrot),
                veilrot,
                $"veilrot must be a finite value in [0, {MaxVeilrot}]. GD §10.2's Claiming fires at " +
                "the top of that range, so a larger number is a meter with headroom nothing can " +
                "cleanse rather than a player in more trouble.");
        }

        // **The one relational guard this format has** (rule 3). Every other check on a snapshot is
        // about a single value; this pair is a stock — Bought − Spent is what the player may still
        // use — so `spent: 99, bought: 0` is a negative stock that M6-02b's counter would carry for
        // the rest of the run, where `spent: 0, bought: 99` is merely a rich file.
        if (rerollsSpent > rerollsBought)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rerollsSpent),
                rerollsSpent,
                $"rerollsSpent is {rerollsSpent} against {rerollsBought} bought. The pair is a " +
                "stock rather than two tallies: what is left to use is bought minus spent, and a " +
                "negative stock is a file nothing in the game can produce.");
        }

        Essence = essence;
        Veilrot = veilrot;
        RerollsBought = rerollsBought;
        RerollsSpent = rerollsSpent;
    }

    /// <summary>What the wallet held at the write (M6-01a).</summary>
    public int Essence { get; }

    /// <summary>GD §10's meter, in [0, 100]. Zero until M6-04 writes it.</summary>
    public float Veilrot { get; }

    /// <summary>How many rerolls this run has bought — GD §13.3's doubling price. M6-02's.</summary>
    public int RerollsBought { get; }

    /// <summary>How many of them have been used. Never more than were bought. M6-02's.</summary>
    public int RerollsSpent { get; }
}

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
/// <b>v4 is everything M6 will ask a run to remember, and four of its six fields have no writer
/// yet</b> (M6-01b): <see cref="Economy"/>'s four scalars, <see cref="BanishedNodeIds"/>,
/// <see cref="PactedNodeIds"/> and <see cref="OrdealIds"/>. This is v2's trade above, counted rather
/// than argued — <c>new RunSnapshot(...)</c> has 36 call sites across 30 files, so bumping once per
/// mechanic (Essence here, Veilrot at M6-04, the shop's counters at M6-02b, Pacts at M6-05a, Ordeals
/// at M6-06a) is five steps, five fixture sets and 180 edited call sites for a format no player has
/// ever seen. <b>Only <see cref="RunEconomy.Essence"/> has a writer today</b>; each of the other
/// four arrives with its own task and <b>none of them bumps the version</b>, which
/// <c>Fixture_V4Run_IsWhatThisBuildWrites</c> is the row that objects to. The risk is stated rather
/// than waved at: if one of those specs wants a different shape, v4 is re-cut before <c>m6</c> is
/// tagged, because it has never left the machine it was written on — and the opposite mistake,
/// a field omitted, is a run's Veilrot silently destroyed by the first <c>Continue</c>.
/// </para>
/// <para>
/// <b>What v4 still does not carry is cooldowns</b>, and the reason is not the one M3-00b wrote
/// down. That spec said saving them would need <c>RunState.Time</c> to mean something across a
/// process death <em>"which it deliberately does not"</em> — but it does, and always has:
/// <see cref="RunTime"/> is a v1 field and <c>RunSession.Start</c> restores <c>State.Time</c> from
/// it exactly. The true reason is cost and review size. <c>SkillRunner._readyAt</c> is keyed by the
/// runner's registration order, which is <em>derived</em> from <see cref="TakenNodeIds"/>, so the
/// only safe spelling on disk is a keyed pair of lists with its own length, duplicate and
/// non-finite guards — not one field — and it does not belong in the PR that ships this format's
/// first two-step chain. <b>The later bump is cheap and that is a fact about the code rather than a
/// hope</b>: <c>_readyAt</c> is absolute against a clock that is itself restored exactly, so a
/// later step is add-only, which is the case <c>MigrateRun</c>'s signature already handles.
/// <b>The question was asked again at v4 and answered the same way</b> (M6-01b): the only safe
/// spelling is still a keyed pair of lists with its own length, duplicate and non-finite guards,
/// which is a task rather than a field and has no owner — and it stays cheap at v5 for exactly the
/// reason it was cheap at v4. <b>The known gap meanwhile</b>: quitting at a boundary and resuming
/// returns every skill ready, so a <c>Continue</c> is a free cooldown reset — bounded by the longest
/// cooldown and by a stage's opening seconds, recorded here rather than left to be discovered.
/// </para>
/// </remarks>
public readonly struct RunSnapshot
{
    /// <summary>The format this build writes. Bumped by the migration that changes the shape.</summary>
    public const int CurrentVersion = 4;

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

    /// <summary>
    /// v4's banished nodes, copied and wrapped. Null only for <c>default(RunSnapshot)</c>, which
    /// <see cref="BanishedNodeIds"/> answers as an empty list — <see cref="_takenNodeIds"/>' rule.
    /// </summary>
    private readonly IReadOnlyList<ContentId> _banishedNodeIds;

    /// <summary>
    /// v4's pacted nodes, copied and wrapped. Null only for <c>default(RunSnapshot)</c>, which
    /// <see cref="PactedNodeIds"/> answers as an empty list.
    /// </summary>
    private readonly IReadOnlyList<ContentId> _pactedNodeIds;

    /// <summary>
    /// v4's Ordeals, copied and wrapped. Null only for <c>default(RunSnapshot)</c>, which
    /// <see cref="OrdealIds"/> answers as an empty list.
    /// </summary>
    private readonly IReadOnlyList<ContentId> _ordealIds;

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
    /// <param name="economy">
    /// GD §15's four scalars at the write. <c>default</c> is a legal fresh run — see
    /// <see cref="RunEconomy"/>, which guards its own contents.
    /// </param>
    /// <param name="banishedNodeIds">
    /// The nodes GD §13.3's Banish took out of this run's pool. Copied; empty until M6-02b.
    /// </param>
    /// <param name="pactedNodeIds">
    /// Which of <paramref name="takenNodeIds"/> were taken corrupted. Copied; empty until M6-05a.
    /// The subset relation is deliberately not checked here — see <see cref="PactedNodeIds"/>.
    /// </param>
    /// <param name="ordealIds">
    /// GD §13.4's Ordeals, in the order they were drawn. Copied; written since M6-06a.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below 1, <paramref name="stageIndex"/> is below 1,
    /// <paramref name="level"/> is below 1, <paramref name="pendingLevelUps"/> is negative, or any
    /// of <paramref name="playerHp"/>, <paramref name="playerShield"/>, <paramref name="runTime"/>
    /// and <paramref name="xp"/> is negative or non-finite.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="takenNodeIds"/>, <paramref name="manualSkillIds"/>,
    /// <paramref name="banishedNodeIds"/>, <paramref name="pactedNodeIds"/> or
    /// <paramref name="ordealIds"/> is null.
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
        IReadOnlyList<ContentId> manualSkillIds,
        RunEconomy economy,
        IReadOnlyList<ContentId> banishedNodeIds,
        IReadOnlyList<ContentId> pactedNodeIds,
        IReadOnlyList<ContentId> ordealIds)
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

        // The three v4 lists share one rule and one message shape, because they are the same kind
        // of thing: a list of ids, empty for a run that has none, never null (rule 4).
        RequireList(banishedNodeIds, nameof(banishedNodeIds), "banished no nodes");
        RequireList(pactedNodeIds, nameof(pactedNodeIds), "taken no Pacts");
        RequireList(ordealIds, nameof(ordealIds), "drawn no Ordeals");

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
        _takenNodeIds = CopyIds(takenNodeIds, nameof(takenNodeIds));
        _manualSkillIds = CopySlots(manualSkillIds);
        Economy = economy;
        _banishedNodeIds = CopyIds(banishedNodeIds, nameof(banishedNodeIds));
        _pactedNodeIds = CopyIds(pactedNodeIds, nameof(pactedNodeIds));
        _ordealIds = CopyIds(ordealIds, nameof(ordealIds));
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
    /// GD §15's four scalars at the write — Essence, Veilrot and the two reroll counters. v4.
    /// </summary>
    /// <remarks>
    /// <b><c>default</c> for <c>default(RunSnapshot)</c>, and that is a legal fresh run</b> rather
    /// than the invalid form <see cref="Version"/> is: nothing earned, nothing corrupted, nothing
    /// bought. See <see cref="RunEconomy"/>, which owns the guards.
    /// </remarks>
    public RunEconomy Economy { get; }

    /// <summary>
    /// Nodes GD §13.3's Banish took out of this run's pool. Never null; empty is ordinary. v4.
    /// </summary>
    /// <remarks>
    /// Empty for every run this build writes until M6-02b has a shop to banish from — which is
    /// <see cref="TakenNodeIds"/>' own position between M3-01b and M3-03, and the same trade: the
    /// reader is a milestone's width away and the alternative is a second step in the chain for
    /// ever. An empty list costs nothing, so <c>RunRecorder.Take</c> stays allocation-free.
    /// </remarks>
    public IReadOnlyList<ContentId> BanishedNodeIds => _banishedNodeIds ?? Array.Empty<ContentId>();

    /// <summary>
    /// Which of <see cref="TakenNodeIds"/> were taken in GD §13.2's corrupted form — a <b>subset</b>
    /// of that list, and the only thing that tells a resumed run which effects it paid Veilrot for
    /// (M6-05a rule 5). Never null. v4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A parallel list rather than a second id per node</b>, which is the alternative M6-05a
    /// weighed: giving a Pact its own <see cref="ContentId"/> would make <see cref="TakenNodeIds"/>
    /// stop being "ids of this tree", and <c>IsTaken</c>, <c>IsBanished</c>, <c>TreeViewPresenter</c>
    /// and <c>RunRecorder</c> would each need to know that two ids mean one node.
    /// </para>
    /// <para>
    /// <b>Its subset relation is deliberately <em>not</em> checked here</b>, unlike
    /// <see cref="RunEconomy"/>'s one relational guard: the two lists are independent arguments at
    /// this layer and the tree is what can answer, so the refusal is <c>SkillTree.Restore</c>'s —
    /// <see cref="TakenNodeIds"/>' rule about unshipped ids, one list over.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContentId> PactedNodeIds => _pactedNodeIds ?? Array.Empty<ContentId>();

    /// <summary>
    /// GD §13.4's Ordeals, in the order they were drawn. Never null; written since M6-06a. v4.
    /// </summary>
    public IReadOnlyList<ContentId> OrdealIds => _ordealIds ?? Array.Empty<ContentId>();

    /// <summary>
    /// Refuses a null list, naming it and saying what an empty one would have meant.
    /// </summary>
    /// <remarks>
    /// One helper rather than three copies, because the three v4 lists say the same thing with a
    /// different noun — and <paramref name="whatEmptyMeans"/> is what keeps the message worth
    /// reading. <see cref="TakenNodeIds"/>' and <see cref="ManualSkillIds"/>' guards stay written
    /// out above: theirs disagree about <c>default(ContentId)</c>, and a shared helper would be the
    /// first place that contrast could be forgotten (M3-07b rule 3).
    /// </remarks>
    private static void RequireList(
        IReadOnlyList<ContentId> ids, string name, string whatEmptyMeans)
    {
        if (ids is null)
        {
            throw new ArgumentNullException(
                name,
                $"{name} must be a list, empty for a run that has {whatEmptyMeans}. Null and empty " +
                "are not two ways of saying the same thing here — a reader must never have to ask.");
        }
    }

    /// <summary>
    /// <paramref name="ids"/> as a list of this snapshot's own, refusing an entry that names
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Indexed rather than <c>foreach</c>ed, so the guard and the copy are one pass over an
    /// <see cref="IReadOnlyList{T}"/> without an enumerator — the shape
    /// <c>ContentCatalog.Index</c> uses, for the same reason.
    /// </para>
    /// <para>
    /// <b>Shared by all four id lists that refuse a defaulted entry</b> — the takes and v4's three
    /// — because they agree about every rule they have. <see cref="CopySlots"/> is the one that
    /// cannot join them: an empty slot <em>is</em> a defaulted id there.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ContentId> CopyIds(IReadOnlyList<ContentId> ids, string name)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<ContentId>();
        }

        var copy = new ContentId[ids.Count];

        for (int i = 0; i < ids.Count; i++)
        {
            ContentId id = ids[i];

            if (id.Value is null)
            {
                throw new ArgumentException(
                    $"{name}[{i}] is default(ContentId) and names nothing. An id this build no " +
                    "longer ships is content validation's answer to give at RunSession.Start; an " +
                    "id that is not an id at all is a file that cannot be read.",
                    name);
            }

            copy[i] = id;
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
    /// nothing</b>, which is <see cref="CopyIds"/>' zero-length shortcut with a length: there is
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
/// What survives every run: the player's own settings, what the game has already shown them, their
/// Shards, the classes they own, the archetypes they have met, and the language they read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seven fields at v4, and the last three arrived together on purpose</b> (M6-09a). A bump is
/// every <c>new PlayerProfile(...)</c> site whether it carries one field or three, so GD §14.2's
/// unlocks, GD §14.1's archetype set and M6-10's locale share one step rather than three —
/// <see cref="Locale"/> ships a milestone-internal two tasks before its reader, which is the one
/// exception to the rule below and is named here for that reason.
/// </para>
/// <para>
/// <b>At v3, Shards arrived alone.</b> ADR-0007 names Shards and unlocks;
/// <see cref="Shards"/> arrived at M4-05b, and unlocks waited for M6-09a — reserving a field
/// for those then would have put a number nothing read into a format every later migration had to carry.
/// <see cref="HapticsEnabled"/> shipped at v1 because it had a consumer that day;
/// <see cref="SeenFirstActiveHint"/> shipped at v2 for the same reason; <see cref="Shards"/> ships
/// at v3 with two — <c>ShardWriter</c>, which banks a dead run's payout, and M4-06's screen, which
/// draws it. <b>No field ships before a consumer</b>, and the rule is that rather than <em>no field
/// before a spender</em>: GD §14.2's unlocks are M6-09's and the Sanctum is M6-02's, so this build
/// banks a number no player can spend. That trade cuts the other way from an unread
/// <c>Palette</c> colour, and the difference is that this one is on a save format — an unread colour
/// costs nothing to add later, while <b>a Shard total not written is data destroyed</b>: the runs
/// that earned it are gone, and a v4 at M6 could not pay anybody back for deaths they already spent
/// (M4-05b rule 8).
/// </para>
/// <para>
/// <b>The three fields are different kinds of fact and belong in the same file anyway.</b>
/// <see cref="HapticsEnabled"/> is something the player <em>chose</em>;
/// <see cref="SeenFirstActiveHint"/> is something the game <em>noticed</em>; <see cref="Shards"/> is
/// something the player <em>earned</em>. What they have in common is the only thing this format is
/// about: they outlive a run. CC §6.3's <em>"Once. Never again."</em> is a claim about an install
/// rather than about a run, and that is the whole reason the hint's flag is here rather than on a
/// presenter (M3-09c rule 11). GD §14.1's <em>"dying must always pay something"</em> is the same
/// kind of claim about a number, and is why the payout is banked here rather than shown and
/// forgotten.
/// </para>
/// <para>
/// <b>A writer that knows one field must never author the whole struct.</b> That is what the two
/// <c>With</c> helpers are for, and it is the rule this format grew teeth for at v2: until then
/// <c>HapticsSettings</c> persisted with <c>new PlayerProfile(CurrentVersion, value)</c>, which is
/// correct for a record with one field in it and silently destructive the moment there are two. The
/// live profile now has exactly one holder and one writer — <c>ProfileStore</c> — and these helpers
/// are what make the right thing the easy thing (M3-09c rule 3). <b>v3 is the first version to test
/// that claim from a second feature</b>: <c>ShardWriter</c> knows one field and reaches for
/// <see cref="WithShards"/>, never the constructor, and
/// <c>SaveDtoTests.Profile_HasNoConstructorThatOmitsAField</c> is the door that keeps it that way.
/// </para>
/// </remarks>
public readonly struct PlayerProfile
{
    /// <summary>The format this build writes.</summary>
    public const int CurrentVersion = 4;

    /// <param name="version">The format the profile is written in.</param>
    /// <param name="hapticsEnabled">Whether the device is allowed to buzz (GD §16.3).</param>
    /// <param name="seenFirstActiveHint">
    /// Whether CC §6.3's one-time callout has already been shown. v2.
    /// </param>
    /// <param name="shards">
    /// Soul Shards banked across every run this install has ever finished. v3, and never negative.
    /// </param>
    /// <param name="unlockedCharacterIds">
    /// The classes this install has earned, in the order it earned them. v4. Never null, never
    /// holding <c>default(ContentId)</c>; copied.
    /// </param>
    /// <param name="metArchetypeIds">
    /// Every enemy archetype this install has met, in first-meeting order. v4. Never null, never
    /// holding <c>default(ContentId)</c>; copied.
    /// </param>
    /// <param name="locale">
    /// The language table to read, or empty for the device's. v4. Never null.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below 1 — the value <c>default(PlayerProfile)</c> carries — or
    /// <paramref name="shards"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentNullException">Either list, or <paramref name="locale"/>, is null.</exception>
    /// <exception cref="ArgumentException">Either list holds <c>default(ContentId)</c>.</exception>
    /// <remarks>
    /// <b>There is deliberately no overload that omits a field.</b> One would compile at every
    /// existing call site on the day a fourth field lands and quietly reset it, which is exactly the
    /// bug v2 exists to have fixed rather than repeated — and v3 is the bump that would have
    /// repeated it, because the writer that lands with it knows one field out of three. v4 is the
    /// same argument at seven (M6-09a rule 1).
    /// </remarks>
    public PlayerProfile(
        int version,
        bool hapticsEnabled,
        bool seenFirstActiveHint,
        int shards,
        IReadOnlyList<ContentId> unlockedCharacterIds,
        IReadOnlyList<ContentId> metArchetypeIds,
        string locale)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "version must be at least 1. Zero is the value default(PlayerProfile) carries and " +
                "no writer can produce, so it means the profile was never written.");
        }

        // Guarded where RunState's deliberately is not (AR §18.2), and for RunSnapshot's reason:
        // these values arrive from a file written by an older build or edited by hand. A negative
        // lifetime total is nothing any writer can produce — ShardsAwarded.Total is a sum of
        // non-negative terms and ShardWriter only ever adds — so it is exactly the hand-edited
        // input this boundary exists to refuse rather than to carry into a Sanctum at M6-02.
        if (shards < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shards),
                shards,
                "shards must be 0 or more. It is a lifetime total that only ever grows, so a " +
                "negative is a hand-edited file rather than anything a writer can produce.");
        }

        // Null and empty differ here for the reason the run's lists already taught (M6-01b rule 4):
        // empty is a real answer — nothing earned, nothing met, the device's language — while null
        // is nobody having set the field at all.
        if (locale is null)
        {
            throw new ArgumentNullException(
                nameof(locale),
                "locale must not be null. Empty means \"the device's language\"; null means nobody " +
                "set the field, and those are different answers.");
        }

        Version = version;
        HapticsEnabled = hapticsEnabled;
        SeenFirstActiveHint = seenFirstActiveHint;
        Shards = shards;
        UnlockedCharacterIds = CopyIds(unlockedCharacterIds, nameof(unlockedCharacterIds));
        MetArchetypeIds = CopyIds(metArchetypeIds, nameof(metArchetypeIds));
        Locale = locale;
    }

    /// <summary>The format this profile was written in. 0 for <c>default(PlayerProfile)</c>.</summary>
    public int Version { get; }

    /// <summary>Whether haptics are on. Defaults to on (GD §16.3); the player may turn them off.</summary>
    public bool HapticsEnabled { get; }

    /// <summary>
    /// Whether the player has already been told that skills can be set to Manual — CC §6.3's
    /// <em>"Once. Never again."</em> v2.
    /// </summary>
    /// <remarks>
    /// Spent when the callout is <em>shown</em> rather than when it is dismissed (M3-09c rule 10):
    /// a player who saw it and died two seconds later has seen it, and an app killed mid-callout
    /// must not show it again.
    /// </remarks>
    public bool SeenFirstActiveHint { get; }

    /// <summary>
    /// Soul Shards banked across every run this install has ever finished (GD §14.1). v3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A lifetime total, never one run's worth.</b> <c>ShardsAwarded</c> carries what a single
    /// death paid; this is the sum of every one of them, which is why <c>ShardWriter</c> reads this
    /// field, adds, and hands the profile back rather than assigning (M4-05b rule 5). It is also why
    /// there may only ever be one thing doing that.
    /// </para>
    /// <para>
    /// <b>One <c>int</c>.</b> GD §14.1's third term — a bonus for each archetype the run met —
    /// needs a <em>set</em> of <c>ContentId</c>s, which is <see cref="MetArchetypeIds"/> at v4
    /// rather than a second number here. The largest cost GD §14.2 names is 3 500, so an
    /// <c>int</c> is not close to tight.
    /// </para>
    /// </remarks>
    public int Shards { get; }

    /// <summary>
    /// Which classes this install may pick, in the order they were earned (GD §14.2). v4. <b>The
    /// starter is not in this list and does not need to be</b> — a class that authors no
    /// <c>UnlockSpec</c> is playable without one (M6-09a rule 3).
    /// </summary>
    /// <remarks>
    /// <b>An id this build no longer ships is not refused</b>, for <c>RunSnapshot.TakenNodeIds</c>'
    /// reason: a class deleted from the catalog is content validation's answer, and a profile that
    /// refuses to load because a designer renamed an asset is the worse failure (M6-09a rule 1).
    /// </remarks>
    public IReadOnlyList<ContentId> UnlockedCharacterIds { get; }

    /// <summary>
    /// Every enemy archetype this install has ever met, in first-meeting order — GD §14.1's third
    /// term, which is a <em>lifetime</em> fact and is why it is here rather than on a run. v4.
    /// </summary>
    /// <remarks>
    /// Read by one thing in core, <c>ShardPayout</c>, and it moves no number a run plays with —
    /// GD §14.3's <em>"no permanent power progression"</em>, which
    /// <c>Profile_CarriesNoNumberThatAffectsARun</c> asserts rather than remembers.
    /// </remarks>
    public IReadOnlyList<ContentId> MetArchetypeIds { get; }

    /// <summary>
    /// Which language table to read, or empty for the device's — M6-10's. v4, and written by
    /// nothing until then.
    /// </summary>
    /// <remarks>
    /// <b>A <see langword="string"/> rather than a <c>LocKey</c> or an enum</b>, and each was
    /// weighed. A <c>LocKey</c> is a key into a table and a locale names the table. An enum is a
    /// closed set, and ADR-0012's whole promise is that <em>"adding a language is adding a
    /// table"</em> — a member per language would make it adding a table and an enum member and a
    /// migration. A BCP-47 tag is what Unity's own <c>Application.systemLanguage</c> maps to and
    /// what a file name can be.
    /// </remarks>
    public string Locale { get; }

    /// <summary>This profile with <see cref="HapticsEnabled"/> moved and nothing else touched.</summary>
    /// <remarks>The shape a multi-field record needs — see the type's remarks.</remarks>
    public PlayerProfile WithHaptics(bool value)
    {
        return new PlayerProfile(
            Version, value, SeenFirstActiveHint, Shards, UnlockedCharacterIds, MetArchetypeIds, Locale);
    }

    /// <summary>
    /// This profile with <see cref="SeenFirstActiveHint"/> moved and nothing else touched.
    /// </summary>
    /// <remarks><see cref="WithHaptics"/>'s mirror, and the reason it is a pair rather than one.</remarks>
    public PlayerProfile WithSeenFirstActiveHint(bool value)
    {
        return new PlayerProfile(
            Version, HapticsEnabled, value, Shards, UnlockedCharacterIds, MetArchetypeIds, Locale);
    }

    /// <summary>This profile with <see cref="Shards"/> moved and nothing else touched.</summary>
    /// <remarks>
    /// The third of the set, and the one that made the pattern worth having: <c>ShardWriter</c>
    /// knows nothing about haptics or the hint, and a writer that authored the whole struct from the
    /// one field it knew would reset both on the frame the player died (M4-05b rule 2).
    /// </remarks>
    public PlayerProfile WithShards(int value)
    {
        return new PlayerProfile(
            Version, HapticsEnabled, SeenFirstActiveHint, value, UnlockedCharacterIds, MetArchetypeIds, Locale);
    }

    /// <summary>
    /// This profile with <see cref="UnlockedCharacterIds"/> replaced and nothing else touched.
    /// </summary>
    /// <remarks>
    /// Replaced rather than appended to, for <see cref="WithShards"/>' reason in the other
    /// direction: the caller holds the whole list and says what it now is. <c>ShardWriter</c> and
    /// <c>ProfileStore.Unlock</c> are the two writers, and each builds the list from
    /// <see cref="UnlockedCharacterIds"/> before handing it in.
    /// </remarks>
    public PlayerProfile WithUnlocked(IReadOnlyList<ContentId> value)
    {
        return new PlayerProfile(
            Version, HapticsEnabled, SeenFirstActiveHint, Shards, value, MetArchetypeIds, Locale);
    }

    /// <summary>
    /// This profile with <see cref="MetArchetypeIds"/> replaced and nothing else touched.
    /// </summary>
    public PlayerProfile WithMetArchetypes(IReadOnlyList<ContentId> value)
    {
        return new PlayerProfile(
            Version, HapticsEnabled, SeenFirstActiveHint, Shards, UnlockedCharacterIds, value, Locale);
    }

    /// <summary>This profile with <see cref="Locale"/> moved and nothing else touched.</summary>
    public PlayerProfile WithLocale(string value)
    {
        return new PlayerProfile(
            Version, HapticsEnabled, SeenFirstActiveHint, Shards, UnlockedCharacterIds, MetArchetypeIds, value);
    }

    /// <summary>
    /// A profile for a player who has never had one: the current format, GD §16.3's defaults,
    /// nothing seen, nothing banked, <b>nothing unlocked, nothing met, and no locale</b> — an empty
    /// unlock list is a playable game, because the starter needs no entry (M6-09a rule 3).
    /// </summary>
    /// <remarks>
    /// A property rather than <c>default</c>, because <c>default</c> is deliberately the
    /// <em>invalid</em> form — version 0 — and "there was no file" and "the file says nothing is
    /// on" have to be different answers. This is what a loader substitutes for the first;
    /// <see cref="ISaveStore.LoadProfile"/> returning null is how it learns which it has.
    /// </remarks>
    public static PlayerProfile Default =>
        new PlayerProfile(
            CurrentVersion,
            hapticsEnabled: true,
            seenFirstActiveHint: false,
            shards: 0,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            locale: string.Empty);

    /// <summary>
    /// <paramref name="ids"/> as a list of this profile's own, refusing null and an entry that
    /// names nothing.
    /// </summary>
    /// <remarks>
    /// The profile's own copy of <c>RunSnapshot</c>'s helper rather than a shared one: the two
    /// formats version independently, and the messages name different consequences.
    /// </remarks>
    private static IReadOnlyList<ContentId> CopyIds(IReadOnlyList<ContentId> ids, string name)
    {
        if (ids is null)
        {
            throw new ArgumentNullException(
                name,
                $"{name} must be a list, empty for an install that has none yet. Null and empty " +
                "are not two ways of saying the same thing here — a reader must never have to ask.");
        }

        if (ids.Count == 0)
        {
            return Array.Empty<ContentId>();
        }

        var copy = new ContentId[ids.Count];

        for (int i = 0; i < ids.Count; i++)
        {
            ContentId id = ids[i];

            if (id.Value is null)
            {
                throw new ArgumentException(
                    $"{name}[{i}] is default(ContentId) and names nothing. An id this build no " +
                    "longer ships is kept (M6-09a rule 1); an id that is not an id at all is a " +
                    "file that cannot be read.",
                    name);
            }

            copy[i] = id;
        }

        return Array.AsReadOnly(copy);
    }
}
