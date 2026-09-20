using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// ADR-0007's first adapter: one JSON file per DTO under a directory, which is
/// <c>Application.persistentDataPath</c> in the container and a temporary folder in a test.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two files, not one.</b> A profile outlives every run and a run snapshot is deleted on death;
/// one file would mean rewriting the player's settings at every stage boundary and losing them to
/// a corrupt run. <see cref="ClearRun"/> never touches the profile.
/// </para>
/// <para>
/// <b>The directory is taken rather than read.</b> Reaching for
/// <c>Application.persistentDataPath</c> in here is what would make this untestable: every row in
/// <c>LocalJsonSaveStoreTests</c> would write to the machine running the tests, and the second run
/// would see the first run's files. <c>BootInstaller</c> passes the real path, once, where the
/// composition is visible.
/// </para>
/// <para>
/// <b>Synchronous inside an async signature, deliberately.</b> <see cref="Task.CompletedTask"/> and
/// <see cref="Task.FromResult{T}"/>, exactly as ADR-0007 describes. A background write would race
/// the next stage boundary's write for the same file, and the thing it would buy — not blocking a
/// frame — is a sub-millisecond local write on a frame that is already swapping an arena. When the
/// store becomes a network one, that adapter brings its own concurrency and this one is untouched.
/// </para>
/// </remarks>
public sealed class LocalJsonSaveStore : ISaveStore
{
    /// <summary>The run snapshot's file name, under the store's directory.</summary>
    public const string RunFileName = "run.json";

    /// <summary>The player profile's file name, under the store's directory.</summary>
    public const string ProfileFileName = "profile.json";

    /// <summary>
    /// What a half-written file is called while it is being written. Appended to the target's own
    /// name so the two always sit in one directory, which is what makes the rename atomic.
    /// </summary>
    private const string TempSuffix = ".tmp";

    private readonly string _directory;
    private readonly string _runPath;
    private readonly string _profilePath;

    /// <param name="directory">
    /// Where the two files live. Created on the first write rather than here: a fresh install has
    /// no save directory, and a constructor that made one would put a folder on disk for a player
    /// who has never saved anything.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty or blank.</exception>
    public LocalJsonSaveStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "A save store needs a directory to write into — Application.persistentDataPath in " +
                "the container, a temporary folder in a test.",
                nameof(directory));
        }

        _directory = directory;
        _runPath = Path.Combine(directory, RunFileName);
        _profilePath = Path.Combine(directory, ProfileFileName);
    }

    /// <inheritdoc />
    public Task<PlayerProfile?> LoadProfile()
    {
        return Task.FromResult(Read<PlayerProfile>(_profilePath, DecodeProfile));
    }

    /// <inheritdoc />
    public Task SaveProfile(PlayerProfile profile)
    {
        var mirror = new ProfileMirror
        {
            version = profile.Version,
            hapticsEnabled = profile.HapticsEnabled,
            seenFirstActiveHint = profile.SeenFirstActiveHint,
            shards = profile.Shards,
        };

        return Write(_profilePath, JsonUtility.ToJson(mirror));
    }

    /// <inheritdoc />
    public Task<RunSnapshot?> LoadRun()
    {
        return Task.FromResult(Read<RunSnapshot>(_runPath, DecodeRun));
    }

    /// <inheritdoc />
    public Task SaveRun(RunSnapshot run)
    {
        var mirror = new RunMirror
        {
            version = run.Version,
            modeId = run.ModeId.Value ?? string.Empty,
            characterId = run.CharacterId.Value ?? string.Empty,
            seed = run.Seed,
            stageIndex = run.StageIndex,
            randomSpawn = run.Random.Spawn,
            randomOffers = run.Random.Offers,
            randomAffixes = run.Random.Affixes,
            randomDrops = run.Random.Drops,
            randomMisc = run.Random.Misc,
            playerHp = run.PlayerHp,
            playerShield = run.PlayerShield,
            runTime = run.RunTime,

            // Normalised to UTC on the way out, so the file records an instant rather than an
            // instant plus wherever the phone happened to be standing. Round-trip format, invariant
            // culture: a save written under a Turkish locale must be readable under an English one.
            writtenAt = run.WrittenAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),

            level = run.Level,
            xp = run.Xp,
            pendingLevelUps = run.PendingLevelUps,
            takenNodeIds = ToStrings(run.TakenNodeIds),
            manualSkillIds = ToStrings(run.ManualSkillIds),
        };

        return Write(_runPath, JsonUtility.ToJson(mirror));
    }

    /// <inheritdoc />
    public Task ClearRun()
    {
        try
        {
            // Succeeds when there was nothing to forget: File.Delete on a missing path is a no-op,
            // and neither caller — death, or a completed descent — can know whether a snapshot was
            // ever written.
            File.Delete(_runPath);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return Task.FromException(exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes <paramref name="json"/> to <paramref name="path"/> so that the file on disk is
    /// either entirely the old content or entirely the new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Temp file first, then a rename.</b> Android kills backgrounded apps mid-anything
    /// (GD §7.3), and a half-written <c>run.json</c> is worse than no <c>run.json</c> — it is a run
    /// the player watched being saved and then lost. A rename within one directory is the one file
    /// operation the OS makes atomic.
    /// </para>
    /// <para>
    /// <b>A stray <c>.tmp</c> is harmless.</b> A kill landing between the write and the rename
    /// leaves one behind; the next write overwrites it and no read ever looks at it.
    /// </para>
    /// <para>
    /// <b>The task faults, the call does not throw.</b> That is the port's rule read literally:
    /// real I/O fails at the <c>await</c>, so a caller that guards only the invocation has to fail
    /// here too rather than on a device with a full disk.
    /// </para>
    /// </remarks>
    private Task Write(string path, string json)
    {
        string temporary = path + TempSuffix;

        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temporary, json);
            Rename(temporary, path);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return Task.FromException(exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads <paramref name="path"/> and decodes it, or answers null when there is no save there
    /// to speak of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three reasons, one behaviour, and the behaviour is "there is no save".</b> A malformed
    /// document, a version this build does not understand, and a decode that threw all end the
    /// same way: the file is deleted and null comes back. A player whose save is unreadable has
    /// lost the run either way, and the only difference between this design and an exception into
    /// the boot path is whether the app also refuses to start.
    /// </para>
    /// <para>
    /// <b>A read that fails for I/O reasons is not a corrupt save, and the file is kept.</b> A
    /// locked or unreadable file may be perfectly good the next time it is asked for, and deleting
    /// it on the strength of one failed read would turn a transient fault into a lost run.
    /// </para>
    /// </remarks>
    private T? Read<T>(string path, Func<string, T> decode)
        where T : struct
    {
        string json;

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            Debug.LogError($"Could not read the save '{path}': {exception.Message}. Reading it as no save.");
            return null;
        }

        try
        {
            return decode(json);
        }
        catch (Exception exception) when (IsUnreadableSave(exception))
        {
            Discard(path, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Deletes an unreadable save and says so once, naming the file and the reason.
    /// </summary>
    /// <remarks>
    /// <c>LogError</c> rather than a warning, and unconditionally rather than only in the Editor.
    /// In the Editor an unreadable save is nearly always a bug in the format and wants to be loud;
    /// on a device it is the one line that tells a bug report apart from "the game forgot my run",
    /// and silencing it would leave the only path that can lose a run untraceable.
    /// </remarks>
    private static void Discard(string path, string reason)
    {
        Debug.LogError($"Discarding the save '{path}': {reason}");

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // Nothing further to do. The file is already unreadable, and throwing from a load would
            // turn a bad save into a failed launch, which is the whole thing this design avoids.
            Debug.LogError($"Could not delete the unreadable save '{path}': {exception.Message}");
        }
    }

    /// <summary>
    /// Moves <paramref name="temporary"/> onto <paramref name="path"/>, replacing what is there.
    /// </summary>
    /// <remarks>
    /// Two branches rather than one call because <c>File.Move(source, destination, overwrite)</c>
    /// is not available at this project's API compatibility level, and <c>File.Replace</c> — which
    /// is — throws <see cref="FileNotFoundException"/> when the destination does not exist yet.
    /// Both branches are a rename within one directory, which is the property that matters.
    /// </remarks>
    private static void Rename(string temporary, string path)
    {
        if (File.Exists(path))
        {
            File.Replace(temporary, path, destinationBackupFileName: null);
            return;
        }

        File.Move(temporary, path);
    }

    /// <summary>Decodes a run, refusing a version this build cannot read.</summary>
    /// <exception cref="ArgumentException">The document is malformed, or a field is out of range.</exception>
    /// <exception cref="FormatException">The timestamp is not a round-trip one.</exception>
    /// <exception cref="NotSupportedException">The format version is outside this build's range.</exception>
    private static RunSnapshot DecodeRun(string json)
    {
        RunMirror mirror = JsonUtility.FromJson<RunMirror>(json);

        if (mirror is null)
        {
            throw new FormatException("it decoded to nothing at all.");
        }

        // A document of `{}` decodes to every field at its default, so version 0 — the value no
        // writer can produce — is what "this was never a save" looks like after a successful parse.
        if (!SaveMigrations.CanReadRun(mirror.version))
        {
            throw new NotSupportedException(
                $"format version {mirror.version} is outside the range this build reads " +
                $"({SaveMigrations.OldestSupportedRunVersion}–{RunSnapshot.CurrentVersion}).");
        }

        var decoded = new RunSnapshot(
            mirror.version,
            ToContentId(mirror.modeId),
            ToContentId(mirror.characterId),
            mirror.seed,
            mirror.stageIndex,
            new RandomState(
                mirror.randomSpawn,
                mirror.randomOffers,
                mirror.randomAffixes,
                mirror.randomDrops,
                mirror.randomMisc),
            mirror.playerHp,
            mirror.playerShield,
            mirror.runTime,
            ToTimestamp(mirror.writtenAt),
            mirror.level,
            mirror.xp,
            mirror.pendingLevelUps,
            ToContentIds(mirror.takenNodeIds),
            ToSlots(mirror.manualSkillIds));

        return SaveMigrations.MigrateRun(mirror.version, decoded);
    }

    /// <summary>Decodes a profile, refusing a version this build cannot read.</summary>
    /// <exception cref="ArgumentException">The document is malformed.</exception>
    /// <exception cref="FormatException">The document decoded to nothing.</exception>
    /// <exception cref="NotSupportedException">The format version is outside this build's range.</exception>
    private static PlayerProfile DecodeProfile(string json)
    {
        ProfileMirror mirror = JsonUtility.FromJson<ProfileMirror>(json);

        if (mirror is null)
        {
            throw new FormatException("it decoded to nothing at all.");
        }

        if (!SaveMigrations.CanReadProfile(mirror.version))
        {
            throw new NotSupportedException(
                $"format version {mirror.version} is outside the range this build reads " +
                $"({SaveMigrations.OldestSupportedProfileVersion}–{PlayerProfile.CurrentVersion}).");
        }

        var decoded = new PlayerProfile(
            mirror.version, mirror.hapticsEnabled, mirror.seenFirstActiveHint, mirror.shards);

        return SaveMigrations.MigrateProfile(mirror.version, decoded);
    }

    /// <summary>
    /// The id a file names, or <c>default</c> when it names nothing this build can parse.
    /// </summary>
    /// <remarks>
    /// Accepted rather than thrown on, because <c>RunSnapshot</c>'s own constructor deliberately
    /// admits <c>default(ContentId)</c>: a save naming content that no longer exists is content
    /// validation's answer to give at <c>RunSession.Start</c>, with the diagnostic that already
    /// names the real problem — not a decode failure that would read as a corrupt file.
    /// </remarks>
    private static ContentId ToContentId(string value)
    {
        return ContentId.TryParse(value, out ContentId id) ? id : default;
    }

    /// <summary>The node ids a file names, as core spells them.</summary>
    /// <remarks>
    /// <para>
    /// <b>An unparseable entry becomes <c>default(ContentId)</c> and the constructor refuses it</b>,
    /// which discards the save. That is deliberately *not* how <see cref="ToContentId"/> treats the
    /// mode and the character, and the two questions are different: an id that parses but names
    /// content this build no longer ships is content validation's answer to give at
    /// <c>RunSession.Start</c>, while an entry that is not an id at all is a document nothing can
    /// read. Passing it through as a default would put a node named <c>""</c> into a resumed tree.
    /// </para>
    /// <para>
    /// Null-tolerant, for <see cref="RunMirror.takenNodeIds"/>'s initialiser to be a belt rather
    /// than the only brace: a hand-written <c>"takenNodeIds":null</c> decodes to null, and the
    /// answer to that is an empty list, not a <see cref="NullReferenceException"/> out of a load.
    /// </para>
    /// </remarks>
    private static ContentId[] ToContentIds(string[] values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<ContentId>();
        }

        var ids = new ContentId[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            ids[i] = ToContentId(values[i]);
        }

        return ids;
    }

    /// <summary>The four thumb positions a file names, as core spells them.</summary>
    /// <remarks>
    /// <para>
    /// <b>Its own converter rather than <see cref="ToContentIds"/>, because the two lists disagree
    /// about <c>default(ContentId)</c></b> (M3-07b rule 3). There an unparseable entry becomes a
    /// default and the constructor refuses it, which discards the save; here a default <em>is</em>
    /// the format's way of writing an empty slot, so <c>""</c> round-trips to a hole and an entry
    /// that is not an id at all becomes one too. That is the same answer <c>SkillRunner.Restore</c>
    /// gives a slot naming content this build no longer ships, and for the same reason: a lost
    /// button costs one visit to CC §6.3's screen, where a lost node would cost the player power.
    /// </para>
    /// <para>
    /// <b>The length is passed through rather than corrected</b>, so a hand-edited document naming
    /// three slots reaches <c>RunSnapshot</c>'s guard and is discarded as the unreadable save it is.
    /// Null becomes four empties — the belt to <see cref="RunMirror.manualSkillIds"/>'s brace, for
    /// <see cref="ToContentIds"/>' reason: a hand-written <c>"manualSkillIds":null</c> is a file to
    /// read, not a <see cref="NullReferenceException"/> out of a load.
    /// </para>
    /// </remarks>
    private static ContentId[] ToSlots(string[] values)
    {
        if (values is null)
        {
            return new ContentId[SkillRunner.MaxManualSlots];
        }

        var ids = new ContentId[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            ids[i] = ToContentId(values[i]);
        }

        return ids;
    }

    /// <summary>The node ids as a file spells them. The inverse of <see cref="ToContentIds"/>.</summary>
    private static string[] ToStrings(IReadOnlyList<ContentId> ids)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<string>();
        }

        var values = new string[ids.Count];

        for (int i = 0; i < ids.Count; i++)
        {
            values[i] = ids[i].Value ?? string.Empty;
        }

        return values;
    }

    /// <summary>The instant a file records, in UTC.</summary>
    /// <exception cref="FormatException">
    /// The text is not a round-trip timestamp. A decode failure like any other — the file is
    /// discarded rather than silently stamped with a zero date, which would make a resumed run
    /// claim to have been written in year one.
    /// </exception>
    private static DateTimeOffset ToTimestamp(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            throw new FormatException($"'{value}' is not a round-trip timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is the disk saying no, rather than a bug in here.
    /// </summary>
    /// <remarks>
    /// Named rather than a bare <c>catch</c>, so a <see cref="NullReferenceException"/> in this
    /// file surfaces as the bug it is instead of reaching a caller as "the save failed".
    /// </remarks>
    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException
            || exception is UnauthorizedAccessException
            || exception is System.Security.SecurityException;
    }

    /// <summary>
    /// Whether <paramref name="exception"/> means the bytes on disk are not a save this build can
    /// read — rule 5's three reasons, which share one behaviour.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentException"/> is both what <c>JsonUtility</c> throws at malformed JSON and
    /// what the DTO constructors throw at a hand-edited stage of 0 or a negative hit point total —
    /// the guards M2-13a put there precisely because these values arrive from a file.
    /// </remarks>
    private static bool IsUnreadableSave(Exception exception)
    {
        return exception is ArgumentException
            || exception is FormatException
            || exception is NotSupportedException;
    }

    /// <summary>
    /// <see cref="RunSnapshot"/> as it is spelled on disk, and the on-disk format itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A mirror, because core's DTOs cannot be serialised directly.</b> They are
    /// <c>readonly struct</c>s with properties and <c>JsonUtility</c> sees only fields — and
    /// <c>ContentId</c> is a struct whose <c>Value</c> is a property, so a direct write would
    /// silently produce <c>{}</c>. The mapping between this and the DTO is the whole of what "the
    /// adapter owns the medium" means (AR §10.3).
    /// </para>
    /// <para>
    /// <b>Public fields, against the project's convention, because here they are the format.</b>
    /// <c>JsonUtility</c> takes a field's name as its JSON key, so a private one would put a
    /// leading underscore into every key of every file a player will ever own. Nested and private
    /// so nothing outside this adapter can hold one; the precedent is <c>SeededRandom.Pcg32</c>.
    /// </para>
    /// <para>
    /// <b>The five stream positions are flattened rather than nested</b>, which keeps the count of
    /// mirrors at two. The field order here is the key order on disk, and that is what the fixture
    /// rows in <c>LocalJsonSaveStoreTests</c> pin.
    /// </para>
    /// <para>
    /// <b>Rejected: <c>com.unity.nuget.newtonsoft-json</c>.</b> It is the obvious answer and it is
    /// a package, which CLAUDE.md's <em>Ask before</em> covers; a mirror per DTO is thirty lines
    /// and adds no dependency to a shipped save format.
    /// </para>
    /// </remarks>
    [Serializable]
    private sealed class RunMirror
    {
        public int version;
        public string modeId;
        public string characterId;
        public int seed;
        public int stageIndex;
        public ulong randomSpawn;
        public ulong randomOffers;
        public ulong randomAffixes;
        public ulong randomDrops;
        public ulong randomMisc;
        public float playerHp;
        public float playerShield;
        public float runTime;
        public string writtenAt;

        /// <summary>
        /// v2's four, appended rather than interleaved: field order is key order on disk, and the
        /// fixture rows pin it.
        /// </summary>
        /// <remarks>
        /// <b><c>level</c> is initialised to 1 and that is load-bearing</b> (M3-01b rule 3). A v1
        /// document has no <c>level</c> key, so the field keeps whatever the default constructor
        /// left — and <c>RunSnapshot</c> refuses a level below 1, which would turn every v1 save on
        /// every player's device into "Discarding the save" before the migration ever ran.
        /// <c>takenNodeIds</c> is initialised for the same reason, one step milder: a null would
        /// reach the constructor's null guard. The migration is still the authority and overwrites
        /// all four for a v1 document, whatever these held.
        /// </remarks>
        public int level = 1;
        public float xp;
        public int pendingLevelUps;
        public string[] takenNodeIds = Array.Empty<string>();

        /// <summary>
        /// v3's one, appended after <see cref="takenNodeIds"/>: field order is key order on disk,
        /// and the fixture rows pin it.
        /// </summary>
        /// <remarks>
        /// <b>Initialised to four entries and that is load-bearing</b>, harder than
        /// <see cref="level"/>'s. A v1 or v2 document has no <c>manualSkillIds</c> key, so the field
        /// keeps what the default constructor left — and <c>RunSnapshot</c> refuses a list that is
        /// not exactly four, so a zero-length default would turn every pre-v3 save on every player's
        /// device into "Discarding the save" <em>before</em> the step that fills it ever ran. The
        /// step is still the authority and overwrites it regardless: a v2 document that somehow
        /// carried slots is still a v2 document (M3-01b rule 3's reason).
        /// </remarks>
        public string[] manualSkillIds = new string[SkillRunner.MaxManualSlots];
    }

    /// <summary><see cref="PlayerProfile"/> as it is spelled on disk. See <see cref="RunMirror"/>.</summary>
    [Serializable]
    private sealed class ProfileMirror
    {
        public int version;
        public bool hapticsEnabled;

        /// <summary>
        /// v2's one, appended after <see cref="hapticsEnabled"/>: field order is key order on disk,
        /// and the two profile fixture rows pin it.
        /// </summary>
        /// <remarks>
        /// <b>Left at <c>false</c> rather than initialised</b>, unlike <c>RunMirror.level</c> and
        /// <c>RunMirror.manualSkillIds</c>. A v1 document has no <c>seenFirstActiveHint</c> key, so
        /// the field keeps the default constructor's <c>false</c> — which is also what the v1 → v2
        /// step writes, so there is no value here a wrong initialiser could hide. The step is still
        /// the authority and overwrites it regardless.
        /// </remarks>
        public bool seenFirstActiveHint;

        /// <summary>
        /// v3's one, appended after <see cref="seenFirstActiveHint"/> for its reason: field order is
        /// key order on disk, and the three profile fixture rows pin it.
        /// </summary>
        /// <remarks>
        /// <b>Left at <c>0</c> rather than initialised</b>, for the field above's reason and with
        /// the same conclusion: a v1 or v2 document has no <c>shards</c> key, so this keeps the
        /// default constructor's zero — which is also exactly what the v2 → v3 step writes
        /// unconditionally (M4-05b rule 3). The step is the authority either way; this field is only
        /// ever the authority for a v3 document, which is the one case where the key is really there.
        /// </remarks>
        public int shards;
    }
}
