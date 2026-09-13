using System;
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
/// a resume restarts the stage whole rather than mid-fight. Level, XP, the skill tree, Essence,
/// Shards and unlocks are absent because the mechanics that own them are not built; AR §6's rule
/// that a port grows a member when its mechanic lands reads the same way for a save format, and a
/// field written at v1 that nothing reads is a field every later migration carries for ever.
/// </para>
/// </remarks>
public readonly struct RunSnapshot
{
    /// <summary>The format this build writes. Bumped by the migration that changes the shape.</summary>
    public const int CurrentVersion = 1;

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
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below 1, <paramref name="stageIndex"/> is below 1, or any of
    /// <paramref name="playerHp"/>, <paramref name="playerShield"/> and <paramref name="runTime"/>
    /// is negative or non-finite.
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
        DateTimeOffset writtenAt)
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
