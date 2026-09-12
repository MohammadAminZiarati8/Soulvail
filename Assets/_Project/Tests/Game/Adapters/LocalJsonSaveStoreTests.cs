using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The adapter against a real directory: what it writes, what it reads back, and what it refuses.
/// </summary>
/// <remarks>
/// <para>
/// A temporary folder per test, never <c>Application.persistentDataPath</c>. A fixture that wrote
/// there would fail differently on the machine that ran it second and would leave a file behind
/// for the next run — which is exactly why the adapter takes its directory rather than reading it.
/// </para>
/// <para>
/// <b>The fixture strings are typed by hand and are not a re-serialisation.</b> A fixture produced
/// by the code under test asserts only that the code agrees with itself; these are the literal
/// bytes a v1 save is, and <c>Fixture_V1Run_IsWhatThisBuildWrites</c> is the row that fails the
/// day the format drifts without the fixture moving with it (AR §11.6).
/// </para>
/// </remarks>
[TestFixture]
public sealed class LocalJsonSaveStoreTests
{
    /// <summary>A v1 run, as it is spelled on disk.</summary>
    private const string V1Run =
        "{\"version\":1,\"modeId\":\"mode.descent\",\"characterId\":\"character.oathbound\"," +
        "\"seed\":-20260912,\"stageIndex\":4,\"randomSpawn\":1,\"randomOffers\":2," +
        "\"randomAffixes\":3,\"randomDrops\":4,\"randomMisc\":18446744073709551615," +
        "\"playerHp\":72.5,\"playerShield\":12.25,\"runTime\":137.75," +
        "\"writtenAt\":\"2026-09-12T08:30:00.0000000+00:00\"}";

    /// <summary>A v1 profile, as it is spelled on disk.</summary>
    private const string V1Profile = "{\"version\":1,\"hapticsEnabled\":false}";

    private static readonly ContentId Mode = new ContentId("mode.descent");
    private static readonly ContentId Character = new ContentId("character.oathbound");

    /// <summary>The instant <see cref="V1Run"/> records.</summary>
    private static readonly DateTimeOffset FixtureWritten =
        new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);

    private string _directory;
    private LocalJsonSaveStore _store;

    [SetUp]
    public void CreateTemporaryDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "soulvail-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new LocalJsonSaveStore(_directory);
    }

    [TearDown]
    public void DeleteTemporaryDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Constructor_NullDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LocalJsonSaveStore(null));
    }

    [Test]
    public void Constructor_BlankDirectory_Throws()
    {
        // Blank rather than only empty: a path of spaces is a directory nothing can be written to,
        // and it would otherwise fail at the first save instead of at composition.
        Assert.Throws<ArgumentException>(() => new LocalJsonSaveStore("   "));
    }

    [Test]
    public void Store_RoundTripsARun()
    {
        RunSnapshot original = Snapshot();

        Await(_store.SaveRun(original));
        RunSnapshot? loaded = Result(_store.LoadRun());

        Assert.That(loaded.HasValue, Is.True);

        RunSnapshot run = loaded.Value;

        Assert.That(run.Version, Is.EqualTo(original.Version));
        Assert.That(run.ModeId, Is.EqualTo(Mode), "A ContentId is a struct whose Value is a property — the field JsonUtility cannot see.");
        Assert.That(run.CharacterId, Is.EqualTo(Character));
        Assert.That(run.Seed, Is.EqualTo(original.Seed));
        Assert.That(run.StageIndex, Is.EqualTo(original.StageIndex));
        Assert.That(run.Random.Spawn, Is.EqualTo(original.Random.Spawn));
        Assert.That(run.Random.Offers, Is.EqualTo(original.Random.Offers));
        Assert.That(run.Random.Affixes, Is.EqualTo(original.Random.Affixes));
        Assert.That(run.Random.Drops, Is.EqualTo(original.Random.Drops));
        Assert.That(run.Random.Misc, Is.EqualTo(original.Random.Misc));
        Assert.That(run.PlayerHp, Is.EqualTo(original.PlayerHp));
        Assert.That(run.PlayerShield, Is.EqualTo(original.PlayerShield));
        Assert.That(run.RunTime, Is.EqualTo(original.RunTime));
        Assert.That(run.WrittenAt, Is.EqualTo(original.WrittenAt));
    }

    /// <summary>
    /// The five numbers a JSON writer is least likely to honour, asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// A textbook member of the family in [Traps §1](../../../../../Docs/Traps.md): a serialiser
    /// that routed a <c>ulong</c> through a double would accept <c>ulong.MaxValue</c>, read back
    /// something very close to it, and desynchronise every stream in a resumed run — with the
    /// symptom arriving as "the seeded run played differently", a week later.
    /// </remarks>
    [Test]
    public void Store_RoundTripsMaxUlongStreamState()
    {
        var saturated = new RandomState(
            ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);

        Await(_store.SaveRun(Snapshot(random: saturated)));
        RandomState loaded = Result(_store.LoadRun()).Value.Random;

        Assert.That(loaded.Spawn, Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.Offers, Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.Affixes, Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.Drops, Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.Misc, Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public void Store_CompletesSynchronously()
    {
        Task save = _store.SaveRun(Snapshot());

        // Already completed on return, so no continuation is needed to observe the result. This is
        // the property ADR-0007's "async signatures, synchronous first adapter" actually means, and
        // the one a caller at a stage boundary is relying on without saying so.
        Assert.That(save.IsCompleted, Is.True);

        Task<RunSnapshot?> load = _store.LoadRun();

        Assert.That(load.IsCompleted, Is.True);

        Await(save);
        Assert.That(load.Result.HasValue, Is.True);
    }

    [Test]
    public void Store_RoundTripsTheTimestamp()
    {
        // A phone three hours east of UTC. The file records an instant, not an instant plus a
        // place, so what comes back is the same moment spelled in UTC.
        var local = new DateTimeOffset(2026, 9, 12, 11, 30, 0, TimeSpan.FromHours(3));

        Await(_store.SaveRun(Snapshot(writtenAt: local)));
        DateTimeOffset loaded = Result(_store.LoadRun()).Value.WrittenAt;

        Assert.That(loaded, Is.EqualTo(local), "The same instant, whatever offset it was written at.");
        Assert.That(loaded.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Store_RoundTripsAProfile()
    {
        Await(_store.SaveProfile(new PlayerProfile(PlayerProfile.CurrentVersion, hapticsEnabled: false)));

        PlayerProfile? loaded = Result(_store.LoadProfile());

        Assert.That(loaded.HasValue, Is.True);
        Assert.That(loaded.Value.HapticsEnabled, Is.False);
        Assert.That(loaded.Value.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
    }

    [Test]
    public void Store_LoadWithNoFile_IsNull()
    {
        Assert.That(Result(_store.LoadRun()).HasValue, Is.False);
        Assert.That(Result(_store.LoadProfile()).HasValue, Is.False);

        // Nothing is created by asking: a fresh install that has never saved has no files, which
        // is the truth rather than a default written out to look like one.
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public void Store_SaveReplaces()
    {
        Await(_store.SaveRun(Snapshot(stageIndex: 2)));
        Await(_store.SaveRun(Snapshot(stageIndex: 9)));

        Assert.That(Result(_store.LoadRun()).Value.StageIndex, Is.EqualTo(9));

        // One run is kept, never a history: a resume offers the run that was interrupted and there
        // is no second one to choose between.
        Assert.That(Directory.GetFiles(_directory).Length, Is.EqualTo(1));
    }

    [Test]
    public void Store_ClearDeletesTheRunFile()
    {
        Await(_store.SaveRun(Snapshot()));
        Await(_store.SaveProfile(new PlayerProfile(PlayerProfile.CurrentVersion, hapticsEnabled: false)));

        Await(_store.ClearRun());

        Assert.That(File.Exists(Path.Combine(_directory, LocalJsonSaveStore.RunFileName)), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName)), Is.True);

        // The settings survive the death that cleared the run — which is the whole reason the two
        // are separate files.
        Assert.That(Result(_store.LoadProfile()).Value.HapticsEnabled, Is.False);
    }

    [Test]
    public void Store_ClearWithNoFile_DoesNotThrow()
    {
        // Both callers — death, and a completed descent — are unable to know whether a snapshot
        // was ever written, so clearing nothing has to be a success.
        Assert.DoesNotThrow(() => Await(_store.ClearRun()));
    }

    [Test]
    public void Store_WritesThroughATempFile()
    {
        Await(_store.SaveRun(Snapshot()));

        Assert.That(
            Directory.GetFiles(_directory, "*.tmp"),
            Is.Empty,
            "A completed write leaves no temporary file behind.");

        Assert.That(File.Exists(Path.Combine(_directory, LocalJsonSaveStore.RunFileName)), Is.True);
    }

    [Test]
    public void Store_StrayTempFileIsIgnored()
    {
        Await(_store.SaveRun(Snapshot(stageIndex: 6)));

        // What an app kill between the write and the rename leaves behind. No read ever looks at
        // it, and the next write overwrites it.
        File.WriteAllText(
            Path.Combine(_directory, LocalJsonSaveStore.RunFileName + ".tmp"),
            "half a fi");

        Assert.That(Result(_store.LoadRun()).Value.StageIndex, Is.EqualTo(6));
    }

    [Test]
    public void Store_CorruptJson_DeletesAndReturnsNull()
    {
        string path = Path.Combine(_directory, LocalJsonSaveStore.RunFileName);
        File.WriteAllText(path, "{ not json");

        LogAssert.Expect(LogType.Error, new Regex("Discarding the save"));

        Assert.That(Result(_store.LoadRun()).HasValue, Is.False);
        Assert.That(File.Exists(path), Is.False, "An unreadable save is removed, not offered again next launch.");
    }

    [Test]
    public void Store_UnknownVersion_DeletesAndReturnsNull()
    {
        string path = Path.Combine(_directory, LocalJsonSaveStore.RunFileName);

        // Version 0 is what default(RunSnapshot) carries and what `{}` decodes to — the one value
        // no writer can produce and every reader refuses.
        File.WriteAllText(path, V1Run.Replace("\"version\":1", "\"version\":0"));

        LogAssert.Expect(LogType.Error, new Regex("Discarding the save"));

        Assert.That(Result(_store.LoadRun()).HasValue, Is.False);
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void Store_NewerVersion_DeletesAndReturnsNull()
    {
        string path = Path.Combine(_directory, LocalJsonSaveStore.RunFileName);
        File.WriteAllText(path, V1Run.Replace("\"version\":1", "\"version\":99"));

        LogAssert.Expect(LogType.Error, new Regex("Discarding the save"));

        // A downgraded build refuses rather than guesses: reading a v99 file as though the fields
        // it does not know about were absent would resume a run at the wrong place and call it fine.
        Assert.That(Result(_store.LoadRun()).HasValue, Is.False);
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void Store_CorruptProfile_DoesNotTakeTheRunWithIt()
    {
        Await(_store.SaveRun(Snapshot(stageIndex: 3)));
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName), "{ not json");

        LogAssert.Expect(LogType.Error, new Regex("Discarding the save"));

        Assert.That(Result(_store.LoadProfile()).HasValue, Is.False);
        Assert.That(Result(_store.LoadRun()).Value.StageIndex, Is.EqualTo(3));
    }

    /// <summary>
    /// A write the disk refuses faults the returned task, and does not throw out of the call.
    /// </summary>
    /// <remarks>
    /// <b>A file standing where the directory should be</b>, rather than a read-only folder: on
    /// Windows the read-only attribute on a directory does not stop a file being created inside it,
    /// so the obvious spelling of this test would pass against a store that had no error handling
    /// at all. What matters is that the failure arrives at the <c>await</c> — a caller that guarded
    /// only the invocation would otherwise pass here and crash on a device with a full disk
    /// (M2-14a rule 7's case).
    /// </remarks>
    [Test]
    public void Store_SaveIntoAnUnwritableDirectory_FaultsTheTask()
    {
        string blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "this is a file, not a directory");

        var store = new LocalJsonSaveStore(blocked);

        Task save = null;

        Assert.DoesNotThrow(() => save = store.SaveRun(Snapshot()));
        Assert.That(save.IsFaulted, Is.True);

        // Observed, so the exception does not surface later from the finalizer thread.
        Assert.That(save.Exception, Is.Not.Null);
    }

    [Test]
    public void Fixture_V1Run_DecodesToTheExpectedSnapshot()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName), V1Run);

        RunSnapshot run = Result(_store.LoadRun()).Value;

        Assert.That(run.Version, Is.EqualTo(1));
        Assert.That(run.ModeId, Is.EqualTo(Mode));
        Assert.That(run.CharacterId, Is.EqualTo(Character));
        Assert.That(run.Seed, Is.EqualTo(-20260912));
        Assert.That(run.StageIndex, Is.EqualTo(4));
        Assert.That(run.Random.Spawn, Is.EqualTo(1UL));
        Assert.That(run.Random.Offers, Is.EqualTo(2UL));
        Assert.That(run.Random.Affixes, Is.EqualTo(3UL));
        Assert.That(run.Random.Drops, Is.EqualTo(4UL));
        Assert.That(run.Random.Misc, Is.EqualTo(ulong.MaxValue));
        Assert.That(run.PlayerHp, Is.EqualTo(72.5f));
        Assert.That(run.PlayerShield, Is.EqualTo(12.25f));
        Assert.That(run.RunTime, Is.EqualTo(137.75f));
        Assert.That(run.WrittenAt, Is.EqualTo(FixtureWritten));
    }

    [Test]
    public void Fixture_V1Profile_DecodesToTheExpectedProfile()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName), V1Profile);

        PlayerProfile profile = Result(_store.LoadProfile()).Value;

        Assert.That(profile.Version, Is.EqualTo(1));
        Assert.That(profile.HapticsEnabled, Is.False);
    }

    [Test]
    public void Fixture_V1Run_IsWhatThisBuildWrites()
    {
        Await(_store.SaveRun(new RunSnapshot(
            version: 1,
            Mode,
            Character,
            seed: -20260912,
            stageIndex: 4,
            new RandomState(1UL, 2UL, 3UL, 4UL, ulong.MaxValue),
            playerHp: 72.5f,
            playerShield: 12.25f,
            runTime: 137.75f,
            FixtureWritten)));

        string written = File.ReadAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName));

        // Byte for byte. A field renamed, reordered or added changes this text, and a save format
        // that drifts without its fixture moving with it is one that stops loading after a release.
        Assert.That(written, Is.EqualTo(V1Run));
    }

    /// <summary>A snapshot with every field distinct, overridable where a row cares.</summary>
    private static RunSnapshot Snapshot(
        int stageIndex = 4,
        RandomState? random = null,
        DateTimeOffset? writtenAt = null)
    {
        return new RunSnapshot(
            RunSnapshot.CurrentVersion,
            Mode,
            Character,
            seed: -20260912,
            stageIndex,
            random ?? new RandomState(1UL, 2UL, 3UL, 4UL, 5UL),
            playerHp: 61.5f,
            playerShield: 12.25f,
            runTime: 138.5f,
            writtenAt ?? FixtureWritten);
    }

    /// <summary>
    /// Blocks on a task this adapter has already completed, and rethrows its fault unwrapped.
    /// </summary>
    /// <remarks>
    /// Legal only because <c>Store_CompletesSynchronously</c> pins the property it relies on. The
    /// day a store is genuinely asynchronous, these rows want a different helper rather than a
    /// longer timeout.
    /// </remarks>
    private static void Await(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    /// <summary>Blocks on a completed task and returns its result. See <see cref="Await"/>.</summary>
    private static T Result<T>(Task<T> task)
    {
        return task.GetAwaiter().GetResult();
    }
}
