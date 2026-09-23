using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Combat;
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
/// bytes a save is, and <c>Fixture_V3Run_IsWhatThisBuildWrites</c> is the row that fails the day
/// the format drifts without the fixture moving with it (AR §11.6).
/// </para>
/// <para>
/// <b>Four run literals as of M6-01b, and they play two parts.</b> <c>V4Run</c> is what this build
/// writes and reads. <c>V3Run</c>, <c>V2Run</c> and <c>V1Run</c> are all <em>migration inputs</em> —
/// documents a player already has on their device — decoded through the real adapter, which is the
/// only place a step and the code that calls it are tested together. <b>Not one of the three older
/// literals changed by one character at this bump</b>, and that is what earns them the right to
/// prove anything: a fixture regenerated alongside the format it is meant to pin stops being
/// evidence. <c>V1Run</c> now walks through <em>three</em> steps in one decode.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LocalJsonSaveStoreTests
{
    /// <summary>
    /// A v1 run, as it is spelled on disk — and as of M3-01b, the migration's input rather than
    /// anything this build writes.
    /// </summary>
    /// <remarks>
    /// <b>Not one character of it changed at either bump, deliberately.</b> It is a real v1
    /// document with no <c>level</c>, <c>xp</c>, <c>pendingLevelUps</c>, <c>takenNodeIds</c> or
    /// <c>manualSkillIds</c> key, which is what makes
    /// <c>Fixture_V1Run_DecodesToTheExpectedSnapshot</c> a test of the steps through the real
    /// adapter instead of a test of a step in isolation. <b>As of M3-07b it walks two of them</b>,
    /// v1 → v2 → v3, which is the first time this fixture has proved a chain rather than a step.
    /// </remarks>
    private const string V1Run =
        "{\"version\":1,\"modeId\":\"mode.descent\",\"characterId\":\"character.oathbound\"," +
        "\"seed\":-20260912,\"stageIndex\":4,\"randomSpawn\":1,\"randomOffers\":2," +
        "\"randomAffixes\":3,\"randomDrops\":4,\"randomMisc\":18446744073709551615," +
        "\"playerHp\":72.5,\"playerShield\":12.25,\"runTime\":137.75," +
        "\"writtenAt\":\"2026-09-12T08:30:00.0000000+00:00\"}";

    /// <summary>
    /// A v2 run, as it is spelled on disk — and as of M3-07b, the v2 → v3 step's input rather than
    /// anything this build writes.
    /// </summary>
    /// <remarks>
    /// The four v2 keys follow <c>writtenAt</c>, because field order in <c>RunMirror</c> is key
    /// order on disk and v2 only appends (rule 9). Typed by hand like its predecessor: a fixture
    /// produced by the code under test asserts only that the code agrees with itself.
    /// <b>Unchanged at the v3 bump and that is what makes it evidence</b> — it has no
    /// <c>manualSkillIds</c> key, which is precisely the document a player who stopped playing
    /// after M3-01b has, and the only thing that proves the new step runs against the real mirror's
    /// field initialiser rather than against a list the test handed it.
    /// </remarks>
    private const string V2Run =
        "{\"version\":2,\"modeId\":\"mode.descent\",\"characterId\":\"character.oathbound\"," +
        "\"seed\":-20260912,\"stageIndex\":4,\"randomSpawn\":1,\"randomOffers\":2," +
        "\"randomAffixes\":3,\"randomDrops\":4,\"randomMisc\":18446744073709551615," +
        "\"playerHp\":72.5,\"playerShield\":12.25,\"runTime\":137.75," +
        "\"writtenAt\":\"2026-09-12T08:30:00.0000000+00:00\"," +
        "\"level\":7,\"xp\":33.5,\"pendingLevelUps\":1," +
        "\"takenNodeIds\":[\"skill.oathbound.bulwark\",\"skill.oathbound.consecrate\"]}";

    /// <summary>
    /// A v3 run, as it is spelled on disk — and as of M6-01b, the v3 → v4 step's input rather than
    /// anything this build writes.
    /// </summary>
    /// <remarks>
    /// <b>One new key at v3, <c>manualSkillIds</c>, following <c>takenNodeIds</c></b>, because field
    /// order in <c>RunMirror</c> is key order on disk and each bump only appends (rule 9). Typed by
    /// hand like both of its predecessors. <b>It has S1 and S3 filled and S2 and S4 empty</b>, which
    /// is the whole reason the field is four slots rather than a set of ids: an empty slot is
    /// <c>""</c> in place, the hole survives the round trip, and a format that compacted it would
    /// write two entries here and hand the player back two adjacent buttons. <b>Not one character of
    /// it changed at the v4 bump</b>, for <see cref="V1Run"/>'s reason — it is a real v3 document
    /// with no economy keys at all, which is precisely what a player who stopped playing after
    /// M3-07b has.
    /// </remarks>
    private const string V3Run =
        "{\"version\":3,\"modeId\":\"mode.descent\",\"characterId\":\"character.oathbound\"," +
        "\"seed\":-20260912,\"stageIndex\":4,\"randomSpawn\":1,\"randomOffers\":2," +
        "\"randomAffixes\":3,\"randomDrops\":4,\"randomMisc\":18446744073709551615," +
        "\"playerHp\":72.5,\"playerShield\":12.25,\"runTime\":137.75," +
        "\"writtenAt\":\"2026-09-12T08:30:00.0000000+00:00\"," +
        "\"level\":7,\"xp\":33.5,\"pendingLevelUps\":1," +
        "\"takenNodeIds\":[\"skill.oathbound.bulwark\",\"skill.oathbound.consecrate\"]," +
        "\"manualSkillIds\":[\"skill.oathbound.bulwark\",\"\",\"skill.oathbound.consecrate\",\"\"]}";

    /// <summary>A v4 run, as it is spelled on disk — what this build writes.</summary>
    /// <remarks>
    /// <para>
    /// <b>Seven new keys, following <c>manualSkillIds</c></b>, for the key-order reason every bump
    /// before it had: field order in <c>RunMirror</c> is key order on disk and v4 only appends.
    /// Typed by hand like all three of its predecessors.
    /// </para>
    /// <para>
    /// <b>The economy is four flat keys rather than a nested object</b> (M6-01b rule 6), which is
    /// what <c>randomSpawn</c>…<c>randomMisc</c> already are and for the same reason: this file is
    /// read by a human in a bug report. <b>Every one of the seven carries a value no default could
    /// produce</b> — 317, 42.5, 2, 1 and three one-entry arrays, each naming a different id — so a
    /// mirror that dropped a key, wrote one field into two, or nested the economy cannot round-trip
    /// green. <b>Only <c>essence</c> has a writer</b>; the other six are the shape M6-02b, M6-04,
    /// M6-05a and M6-06a fill without touching the version, and this row is what objects if one of
    /// them bumps it.
    /// </para>
    /// </remarks>
    private const string V4Run =
        "{\"version\":4,\"modeId\":\"mode.descent\",\"characterId\":\"character.oathbound\"," +
        "\"seed\":-20260912,\"stageIndex\":4,\"randomSpawn\":1,\"randomOffers\":2," +
        "\"randomAffixes\":3,\"randomDrops\":4,\"randomMisc\":18446744073709551615," +
        "\"playerHp\":72.5,\"playerShield\":12.25,\"runTime\":137.75," +
        "\"writtenAt\":\"2026-09-12T08:30:00.0000000+00:00\"," +
        "\"level\":7,\"xp\":33.5,\"pendingLevelUps\":1," +
        "\"takenNodeIds\":[\"skill.oathbound.bulwark\",\"skill.oathbound.consecrate\"]," +
        "\"manualSkillIds\":[\"skill.oathbound.bulwark\",\"\",\"skill.oathbound.consecrate\",\"\"]," +
        "\"essence\":317,\"veilrot\":42.5,\"rerollsBought\":2,\"rerollsSpent\":1," +
        "\"banishedNodeIds\":[\"skill.oathbound.reprisal\"]," +
        "\"pactedNodeIds\":[\"skill.oathbound.consecrate\"]," +
        "\"ordealIds\":[\"ordeal.thinblood\"]}";

    /// <summary>
    /// A v1 profile, as it is spelled on disk — and as of M3-09c, the profile migration's input
    /// rather than anything this build writes.
    /// </summary>
    /// <remarks>
    /// <b>Not one character of it changed at either bump, deliberately.</b> It is a real v1 document
    /// with no <c>seenFirstActiveHint</c> key and no <c>shards</c> key, which is what makes
    /// <c>Fixture_V1Profile_DecodesToTheExpectedProfile</c> a test of the whole chain through the
    /// real adapter instead of a test of a step in isolation — and a fixture rewritten alongside the
    /// format it pins stops being evidence.
    /// </remarks>
    private const string V1Profile = "{\"version\":1,\"hapticsEnabled\":false}";

    /// <summary>
    /// A v2 profile, as it is spelled on disk — and as of M4-05b, the v2 → v3 step's input rather
    /// than anything this build writes.
    /// </summary>
    /// <remarks>
    /// <b>One new key at v2, <c>seenFirstActiveHint</c>, following <c>hapticsEnabled</c></b>, because
    /// field order in <c>ProfileMirror</c> is key order on disk and each bump only appends. Typed by
    /// hand like its predecessor: a fixture produced by the code under test asserts only that the
    /// code agrees with itself. <b>Not one character of it changed at the v3 bump</b>, for
    /// <see cref="V1Profile"/>'s reason — it is a real v2 document with no <c>shards</c> key, and a
    /// fixture rewritten alongside the format it pins stops being evidence.
    /// </remarks>
    private const string V2Profile =
        "{\"version\":2,\"hapticsEnabled\":false,\"seenFirstActiveHint\":true}";

    /// <summary>
    /// A v3 profile, as it is spelled on disk — and as of M6-09a, the v3 → v4 step's input rather
    /// than anything this build writes.
    /// </summary>
    /// <remarks>
    /// <b>One new key at v3, <c>shards</c>, following <c>seenFirstActiveHint</c></b>, for the key
    /// order reason above. <b>Not one character of it changed at the v4 bump</b>, for
    /// <see cref="V1Profile"/>'s reason: it is the document every install since the <c>m4</c> tag
    /// holds, and the one the grandfathering exists for.
    /// </remarks>
    private const string V3Profile =
        "{\"version\":3,\"hapticsEnabled\":false,\"seenFirstActiveHint\":true,\"shards\":220}";

    /// <summary>A v4 profile, as it is spelled on disk — what this build writes.</summary>
    /// <remarks>
    /// <b>Three new keys, flat, following <c>shards</c></b> (M6-09a rule 7): two string arrays and a
    /// string, so the document stays one a human can read in a bug report.
    /// </remarks>
    private const string V4Profile =
        "{\"version\":4,\"hapticsEnabled\":false,\"seenFirstActiveHint\":true,\"shards\":220," +
        "\"unlockedCharacterIds\":[\"character.emberwright\"],\"metArchetypeIds\":[\"enemy.husk\"]," +
        "\"locale\":\"fr\"}";

    private static readonly ContentId Mode = new ContentId("mode.descent");
    private static readonly ContentId Character = new ContentId("character.oathbound");

    /// <summary>The two nodes <see cref="V2Run"/> names.</summary>
    private static readonly ContentId Bulwark = new ContentId("skill.oathbound.bulwark");
    private static readonly ContentId Consecrate = new ContentId("skill.oathbound.consecrate");

    /// <summary>The banished node and the Ordeal <see cref="V4Run"/> names.</summary>
    private static readonly ContentId Reprisal = new ContentId("skill.oathbound.reprisal");
    private static readonly ContentId Thinblood = new ContentId("ordeal.thinblood");

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
        Assert.That(run.Level, Is.EqualTo(original.Level));
        Assert.That(run.Xp, Is.EqualTo(original.Xp));
        Assert.That(run.PendingLevelUps, Is.EqualTo(original.PendingLevelUps));

        // The fields that are not scalars, so the ones a mirror could plausibly lose: JsonUtility
        // sees fields, and ContentId's Value is a property.
        Assert.That(run.TakenNodeIds, Is.EqualTo(original.TakenNodeIds));
        Assert.That(run.ManualSkillIds, Is.EqualTo(original.ManualSkillIds));

        // v4's seven, flattened on the way out and rebuilt on the way in. The economy is the one a
        // nested [Serializable] class would also have round-tripped, which is why rule 6 is about
        // the file a human reads rather than about correctness.
        Assert.That(run.Economy.Essence, Is.EqualTo(original.Economy.Essence));
        Assert.That(run.Economy.Veilrot, Is.EqualTo(original.Economy.Veilrot));
        Assert.That(run.Economy.RerollsBought, Is.EqualTo(original.Economy.RerollsBought));
        Assert.That(run.Economy.RerollsSpent, Is.EqualTo(original.Economy.RerollsSpent));
        Assert.That(run.BanishedNodeIds, Is.EqualTo(original.BanishedNodeIds));
        Assert.That(run.PactedNodeIds, Is.EqualTo(original.PactedNodeIds));
        Assert.That(run.OrdealIds, Is.EqualTo(original.OrdealIds));
    }

    [Test]
    public void Roundtrip_PreservesTheHole()
    {
        // **The whole reason the field is four slots and not a set of ids** (rule 1). A run with
        // skills in S1 and S3 must come back with skills in S1 and S3 — not S1 and S2. This is the
        // one row that walks the entire path a player's loadout takes: the runner's table, the
        // recorder's read, the DTO's copy, the mirror's array, the file's bytes, and back through
        // all five into a live run's slots.
        var slots = new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) };

        Await(_store.SaveRun(Snapshot()));

        RunSnapshot run = Result(_store.LoadRun()).Value;

        Assert.That(run.ManualSkillIds, Is.EqualTo(slots));

        // Said the other way round too, because `Is.EqualTo` on the whole list is the assertion a
        // reader skims: the second slot is *empty*, and the skill that a compacting format would
        // have moved into it is still in the third.
        Assert.That(run.ManualSkillIds[1], Is.EqualTo(default(ContentId)), "S2 is a hole.");
        Assert.That(run.ManualSkillIds[2], Is.EqualTo(Consecrate), "and S3 did not slide into it.");
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
        Await(_store.SaveProfile(new PlayerProfile(
            PlayerProfile.CurrentVersion,
            hapticsEnabled: false,
            seenFirstActiveHint: true,
            shards: 220, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "")));

        PlayerProfile? loaded = Result(_store.LoadProfile());

        Assert.That(loaded.HasValue, Is.True);
        Assert.That(loaded.Value.HapticsEnabled, Is.False);
        Assert.That(loaded.Value.Version, Is.EqualTo(PlayerProfile.CurrentVersion));

        // Set the opposite way from `hapticsEnabled` above, so a mirror that wrote one field into
        // both keys — or dropped the second — cannot round-trip green.
        Assert.That(loaded.Value.SeenFirstActiveHint, Is.True);

        // And v3's number, which is the one field here a wrong answer would cost the player
        // something they had earned rather than a setting they can flip back.
        Assert.That(loaded.Value.Shards, Is.EqualTo(220));
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
        Await(_store.SaveProfile(new PlayerProfile(
            PlayerProfile.CurrentVersion,
            hapticsEnabled: false,
            seenFirstActiveHint: false,
            shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "")));

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

        // **v4, not v1 — and this row is the whole chain running through the real adapter.** A v1
        // document on a device goes through LoadRun and comes back having walked *three* steps in
        // order. Asserting it here rather than only on SaveMigrations is the difference between "the
        // steps are correct" and "the steps are wired up", and ledger row 2's trap is precisely that
        // the second can be false while the first is true.
        Assert.That(run.Version, Is.EqualTo(4));

        // A v1 run was unlevelled by construction (rule 3). The mirror's `level = 1` initialiser is
        // what lets the document reach the constructor at all — a v1 file has no `level` key, and
        // RunSnapshot refuses a level below 1 — and the step is what gives it v1's meaning.
        Assert.That(run.Level, Is.EqualTo(1));
        Assert.That(run.Xp, Is.EqualTo(0f));
        Assert.That(run.PendingLevelUps, Is.EqualTo(0));
        Assert.That(run.TakenNodeIds, Is.Empty);

        // And it had no loadout either (rule 5). The mirror's four-entry `manualSkillIds`
        // initialiser is what lets a document with no such key reach the constructor at all — it
        // refuses a list that is not exactly four — and the v2 → v3 step is what gives it v2's
        // meaning: every skill on Auto, which is also CC §6.1's default.
        Assert.That(run.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(run.ManualSkillIds, Is.All.EqualTo(default(ContentId)));

        // And no economy (M6-01b rule 5). A v1 build had no Essence, no Veilrot, no rerolls and
        // none of the three lists in it at all, so `default(RunEconomy)` and three empty lists are
        // the truth about that run rather than defaults standing in for an unknown — and the
        // mirror's `Array.Empty<string>()` initialisers are what let a document with none of the
        // keys reach the constructor's null guards at all.
        AssertNoEconomy(run);

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

        // **v4, because this literal is now the input to a three-step chain.** The document is
        // unchanged from M2-13b; what changed is first that loading it became a migration rather
        // than a read (M3-09c), and now that the migration is three steps deep — and this is the
        // only place the steps and the code that calls them are tested together.
        Assert.That(profile.Version, Is.EqualTo(4));
        Assert.That(profile.UnlockedCharacterIds, Is.EqualTo(new[] { Gravecaller }), "grandfathered.");

        // Haptics as before: the one v1 field is something the player chose and survives both steps.
        Assert.That(profile.HapticsEnabled, Is.False);

        // And the hint is unseen, which is true rather than a default: a v1 profile was written by
        // a build with no skills in it, so there was no first Active to be told about.
        Assert.That(profile.SeenFirstActiveHint, Is.False);

        // And nothing is banked, for the same kind of reason one step further on: a v1 build had no
        // payout in it either, so zero is the truth about that player rather than a placeholder.
        Assert.That(profile.Shards, Is.Zero);
    }

    [Test]
    public void Fixture_V2Profile_DecodesToTheExpectedProfile()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName), V2Profile);

        PlayerProfile profile = Result(_store.LoadProfile()).Value;

        // **Renamed from Fixture_V2Profile_IsWhatThisBuildWrites rather than joined by a second
        // row**: "what this build writes" is a property of the current version and moves up with
        // every bump, and what used to be this row's subject is now a migration input — which is
        // exactly the transition a bump makes. `Fixture_V2Run_DecodesToTheExpectedSnapshot` made
        // the same move on the other format at M3-07b.
        Assert.That(profile.Version, Is.EqualTo(4));

        // The document has no `shards` key at all, so this is the v2 → v3 step's answer read
        // through the real adapter rather than through the mirror's field initialiser: JsonUtility
        // leaves the field at zero and the step writes zero over it, and both being zero is the
        // point — a v2 document is a v2 document, and there is no number here to disagree about.
        Assert.That(profile.Shards, Is.Zero);

        // And both v2 fields survive, set the opposite way round from each other.
        Assert.That(profile.HapticsEnabled, Is.False);
        Assert.That(profile.SeenFirstActiveHint, Is.True);
    }

    [Test]
    public void Store_ReadsTheV3ProfileLiteral()
    {
        // Was Fixture_V3Profile_IsWhatThisBuildWrites: "what this build writes" moved up with the
        // bump, and the literal became the v3 → v4 step's input through the real adapter (rule 10).
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName), V3Profile);

        PlayerProfile profile = Result(_store.LoadProfile()).Value;

        Assert.That(profile.Version, Is.EqualTo(4));
        Assert.That(profile.Shards, Is.EqualTo(220), "the banked total survives the step.");
        Assert.That(profile.UnlockedCharacterIds, Is.EqualTo(new[] { Gravecaller }), "and the Gravecaller is unlocked.");
        Assert.That(profile.MetArchetypeIds, Is.Empty);
        Assert.That(profile.Locale, Is.EqualTo(string.Empty));
        Assert.That(profile.HapticsEnabled, Is.False);
        Assert.That(profile.SeenFirstActiveHint, Is.True);
    }

    [Test]
    public void Store_WritesAV4ProfileLiteral()
    {
        Await(_store.SaveProfile(new PlayerProfile(
            version: 4, hapticsEnabled: false, seenFirstActiveHint: true, shards: 220,
            new[] { new ContentId("character.emberwright") }, new[] { new ContentId("enemy.husk") }, locale: "fr")));

        string written = File.ReadAllText(
            Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName));

        // Byte for byte, and the three new keys flat on the end (rule 7). A field renamed,
        // reordered or added changes this text.
        Assert.That(written, Is.EqualTo(V4Profile));

        // And it reads back as written: the round trip of every one of the seven.
        PlayerProfile loaded = Result(_store.LoadProfile()).Value;

        Assert.That(loaded.UnlockedCharacterIds, Is.EqualTo(new[] { new ContentId("character.emberwright") }));
        Assert.That(loaded.MetArchetypeIds, Is.EqualTo(new[] { new ContentId("enemy.husk") }));
        Assert.That(loaded.Locale, Is.EqualTo("fr"));
    }

    [Test]
    public void Store_ADocumentWithoutTheNewFieldsDecodes()
    {
        // A v4 document with all three keys absent: the mirror's initialisers are what stand
        // between that and PlayerProfile's null guards (rule 7).
        File.WriteAllText(
            Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName),
            "{\"version\":4,\"hapticsEnabled\":true,\"seenFirstActiveHint\":false,\"shards\":5}");

        PlayerProfile profile = Result(_store.LoadProfile()).Value;

        Assert.That(profile.UnlockedCharacterIds, Is.Not.Null.And.Empty);
        Assert.That(profile.MetArchetypeIds, Is.Not.Null.And.Empty);
        Assert.That(profile.Locale, Is.EqualTo(string.Empty));
        Assert.That(profile.Shards, Is.EqualTo(5));
    }

    [Test]
    public void Store_AnUnparseableIdIsDroppedNotFatal()
    {
        // A refused profile is replaced by the default at boot and the next write destroys every
        // Shard the install banked. So an entry that is not an id costs that entry, not the file.
        File.WriteAllText(
            Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName),
            "{\"version\":4,\"hapticsEnabled\":true,\"seenFirstActiveHint\":false,\"shards\":900," +
            "\"unlockedCharacterIds\":[\"Not An Id\",\"character.gravecaller\"],\"metArchetypeIds\":[\"\"],\"locale\":\"\"}");

        PlayerProfile profile = Result(_store.LoadProfile()).Value;

        Assert.That(profile.Shards, Is.EqualTo(900));
        Assert.That(profile.UnlockedCharacterIds, Is.EqualTo(new[] { Gravecaller }));
        Assert.That(profile.MetArchetypeIds, Is.Empty);
    }

    private static readonly ContentId Gravecaller = new ContentId("character.gravecaller");

    [Test]
    public void Fixture_V2Run_DecodesToTheExpectedSnapshot()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName), V2Run);

        RunSnapshot run = Result(_store.LoadRun()).Value;

        // **v4, because this literal is the input to a two-step walk as of M6-01b.** The document is
        // unchanged from M3-01b; what changed is first that loading it became a migration rather
        // than a read, and now that the migration runs a second step over the top.
        Assert.That(run.Version, Is.EqualTo(4));
        Assert.That(run.Level, Is.EqualTo(7));
        Assert.That(run.Xp, Is.EqualTo(33.5f));
        Assert.That(run.PendingLevelUps, Is.EqualTo(1));

        // In take order, which is what the list means — not a set.
        Assert.That(run.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));

        // **Four empty slots, and every v2 field above survived the step that added them** (rule
        // 5). A v2 run had no loadout by construction, so its v3 form is every skill on Auto — the
        // same state a fresh run is in, which is what lets the step need no special case anywhere
        // above the DTO. This is the new step through the real adapter rather than in isolation.
        Assert.That(run.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(run.ManualSkillIds, Is.All.EqualTo(default(ContentId)));

        // And no economy, which is the third step's answer over the top of the second.
        AssertNoEconomy(run);

        // And the v1 half of the document is still read the same way, which is the half a bump is
        // most likely to break by shifting a field.
        Assert.That(run.ModeId, Is.EqualTo(Mode));
        Assert.That(run.Seed, Is.EqualTo(-20260912));
        Assert.That(run.StageIndex, Is.EqualTo(4));
        Assert.That(run.Random.Misc, Is.EqualTo(ulong.MaxValue));
        Assert.That(run.PlayerHp, Is.EqualTo(72.5f));
        Assert.That(run.WrittenAt, Is.EqualTo(FixtureWritten));
    }

    [Test]
    public void Fixture_V3Run_DecodesToTheExpectedSnapshot()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName), V3Run);

        RunSnapshot run = Result(_store.LoadRun()).Value;

        // **v4, because this literal is now the v3 → v4 step's input.** The document is unchanged
        // from M3-07b; what changed is that loading it is a migration rather than a read.
        Assert.That(run.Version, Is.EqualTo(4));

        // A v3 run had no economy by construction — none of GD §15's or §13's mechanics existed —
        // so its v4 form is a fresh economy and three empty lists. The document has none of the
        // seven keys, so this is the step's answer read through the real adapter rather than through
        // the mirror's field initialisers.
        AssertNoEconomy(run);

        // **The hole survives, in place.** S1 and S3 are filled and S2 and S4 are empty, which is
        // the state a set of ids could not express — it would come back as S1 and S2 and silently
        // move a button out from under the thumb that had learned it (rule 1).
        Assert.That(
            run.ManualSkillIds,
            Is.EqualTo(new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) }));

        // And the v1 and v2 halves of the document are still read the same way, which is the part a
        // bump is most likely to break by shifting a field.
        Assert.That(run.Level, Is.EqualTo(7));
        Assert.That(run.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));
        Assert.That(run.Seed, Is.EqualTo(-20260912));
        Assert.That(run.Random.Misc, Is.EqualTo(ulong.MaxValue));
        Assert.That(run.PlayerHp, Is.EqualTo(72.5f));
        Assert.That(run.WrittenAt, Is.EqualTo(FixtureWritten));
    }

    [Test]
    public void Fixture_V3Run_CarryingAnEconomy_StillGetsV3sMeaning()
    {
        // A v3 document with an `essence` key, which no real v3 document can have — the mirror had
        // no such field. Written this way on purpose (M6-01b rule 5): **the step is written from the
        // shape rather than from what was decoded**, so a v3 document is a v3 document whatever it
        // carries, and a fixture whose input was already empty could not tell that apart from a step
        // that passed the field through. `V3Run` has one `}` and it is the last character.
        File.WriteAllText(
            Path.Combine(_directory, LocalJsonSaveStore.RunFileName),
            V3Run.Replace("}", ",\"essence\":500}"));

        RunSnapshot run = Result(_store.LoadRun()).Value;

        Assert.That(run.Version, Is.EqualTo(4));
        Assert.That(
            run.Economy.Essence,
            Is.Zero,
            "The v3 → v4 step is the authority: 500 was decoded and then overwritten by v3's meaning.");
    }

    [Test]
    public void Fixture_V4Run_DecodesToTheExpectedSnapshot()
    {
        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName), V4Run);

        RunSnapshot run = Result(_store.LoadRun()).Value;

        // No migration ran: this is what the build writes, read back.
        Assert.That(run.Version, Is.EqualTo(4));

        // **The four scalars and the three lists, each a value no step could have produced.** The
        // step writes zeroes and empties, so every one of these assertions fails the day the mirror
        // stops carrying a key and the migration quietly answers for it instead.
        Assert.That(run.Economy.Essence, Is.EqualTo(317));
        Assert.That(run.Economy.Veilrot, Is.EqualTo(42.5f));
        Assert.That(run.Economy.RerollsBought, Is.EqualTo(2));
        Assert.That(run.Economy.RerollsSpent, Is.EqualTo(1));
        Assert.That(run.BanishedNodeIds, Is.EqualTo(new[] { Reprisal }));
        Assert.That(run.PactedNodeIds, Is.EqualTo(new[] { Consecrate }));
        Assert.That(run.OrdealIds, Is.EqualTo(new[] { Thinblood }));

        // And the v3 half of the document is still read the same way, hole included — the part a
        // bump is most likely to break by shifting a field.
        Assert.That(
            run.ManualSkillIds,
            Is.EqualTo(new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) }));

        Assert.That(run.Level, Is.EqualTo(7));
        Assert.That(run.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));
        Assert.That(run.Seed, Is.EqualTo(-20260912));
        Assert.That(run.Random.Misc, Is.EqualTo(ulong.MaxValue));
        Assert.That(run.PlayerHp, Is.EqualTo(72.5f));
        Assert.That(run.WrittenAt, Is.EqualTo(FixtureWritten));
    }

    [Test]
    public void Fixture_V4Run_WithoutTheNewArrays_Decodes()
    {
        // A v4 document with all three arrays missing — which is what a hand-edited file looks like,
        // and what the mirror's `Array.Empty<string>()` initialisers exist for (rule 6). A null
        // would reach RunSnapshot's null guard and turn the save into "Discarding the save", so this
        // is the row that says the initialisers are load-bearing rather than tidy.
        string stripped = V4Run
            .Replace(",\"banishedNodeIds\":[\"skill.oathbound.reprisal\"]", string.Empty)
            .Replace(",\"pactedNodeIds\":[\"skill.oathbound.consecrate\"]", string.Empty)
            .Replace(",\"ordealIds\":[\"ordeal.thinblood\"]", string.Empty);

        Assert.That(stripped, Does.Not.Contain("ordealIds"), "The fixture really removed them.");

        File.WriteAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName), stripped);

        RunSnapshot run = Result(_store.LoadRun()).Value;

        Assert.That(run.BanishedNodeIds, Is.Not.Null);
        Assert.That(run.BanishedNodeIds, Is.Empty);
        Assert.That(run.PactedNodeIds, Is.Not.Null);
        Assert.That(run.PactedNodeIds, Is.Empty);
        Assert.That(run.OrdealIds, Is.Not.Null);
        Assert.That(run.OrdealIds, Is.Empty);

        // No migration ran, so the four scalars are still the document's own — which is what makes
        // this a row about the arrays rather than about the step.
        Assert.That(run.Version, Is.EqualTo(4));
        Assert.That(run.Economy.Essence, Is.EqualTo(317));
    }

    [Test]
    public void Fixture_V4Run_IsWhatThisBuildWrites()
    {
        Await(_store.SaveRun(new RunSnapshot(
            version: 4,
            Mode,
            Character,
            seed: -20260912,
            stageIndex: 4,
            new RandomState(1UL, 2UL, 3UL, 4UL, ulong.MaxValue),
            playerHp: 72.5f,
            playerShield: 12.25f,
            runTime: 137.75f,
            FixtureWritten,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) },
            economy: new RunEconomy(317, 42.5f, 2, 1),
            banishedNodeIds: new[] { Reprisal },
            pactedNodeIds: new[] { Consecrate },
            ordealIds: new[] { Thinblood })));

        string written = File.ReadAllText(Path.Combine(_directory, LocalJsonSaveStore.RunFileName));

        // Byte for byte. A field renamed, reordered or added changes this text, and a save format
        // that drifts without its fixture moving with it is one that stops loading after a release.
        //
        // **Renamed from the v3 row, whose literal is now the v3 → v4 step's input.** That literal
        // was not regenerated, which is what earns it the right to prove anything: a fixture
        // rewritten alongside the format it pins stops being evidence. **This is also the row that
        // objects if a later task bumps the version to write a field v4 already reserves** — M6-02b,
        // M6-04, M6-05a and M6-06a each fill exactly one argument at `RunRecorder.Take`, and filling
        // a field does not change the shape of the document, so the text here stays true and only
        // its contents move.
        Assert.That(written, Is.EqualTo(V4Run));
    }

    /// <summary>
    /// A run that came back with no economy at all: <c>default(RunEconomy)</c> and three empty
    /// lists, which is every migration step's answer and every pre-v4 document's truth.
    /// </summary>
    /// <remarks>
    /// One helper rather than seven assertions in each of three rows. All four scalars, because one
    /// of them reading zero says nothing about the other three, and <c>Is.Not.Null</c> beside each
    /// list because empty and null are different answers and only one of them is legal.
    /// </remarks>
    private static void AssertNoEconomy(RunSnapshot run)
    {
        Assert.That(run.Economy.Essence, Is.Zero);
        Assert.That(run.Economy.Veilrot, Is.Zero);
        Assert.That(run.Economy.RerollsBought, Is.Zero);
        Assert.That(run.Economy.RerollsSpent, Is.Zero);

        Assert.That(run.BanishedNodeIds, Is.Not.Null);
        Assert.That(run.BanishedNodeIds, Is.Empty);
        Assert.That(run.PactedNodeIds, Is.Not.Null);
        Assert.That(run.PactedNodeIds, Is.Empty);
        Assert.That(run.OrdealIds, Is.Not.Null);
        Assert.That(run.OrdealIds, Is.Empty);
    }

    /// <summary>A snapshot with every field distinct, overridable where a row cares.</summary>
    /// <remarks>
    /// The v2 fields carry non-default values here — a levelled run with two nodes — because the
    /// round-trip rows are the ones that would otherwise pass against a mirror that dropped them:
    /// zero, one and an empty array all survive being lost. <b>v3's field carries a hole for the
    /// same reason and one more</b>: four empties would round-trip through a mirror that dropped
    /// the key entirely, because the step would put four empties back. <b>v4's seven carry
    /// non-defaults for exactly that second reason</b> — the v3 → v4 step writes zeroes and empties,
    /// so a mirror that dropped any of them would round-trip green against a fresh economy.
    /// </remarks>
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
            writtenAt ?? FixtureWritten,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) },
            economy: new RunEconomy(317, 42.5f, 2, 1),
            banishedNodeIds: new[] { Reprisal },
            pactedNodeIds: new[] { Consecrate },
            ordealIds: new[] { Thinblood });
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
