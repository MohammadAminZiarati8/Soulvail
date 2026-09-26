using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Soulvail.Game.Adapters;
using Soulvail.Tests.PlayMode.Support;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// RS-03g: the shelter that keeps the machine's saves out of the PlayMode suite's way, over a
/// temporary folder, and the row that every fixture in this assembly names it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A temporary folder per row, never <c>persistentDataPath</c></b>, for
/// <c>LocalJsonSaveStoreTests</c>' reason: a row that sheltered the real folder would move the
/// owner's files in the middle of the run that is already sheltering them.
/// </para>
/// <para>
/// <b>The save literals are the bytes of a real v4 pair</b>, the owner's stage-1 Ranger and profile,
/// so the fresh-install row can show the store reading both before <c>Setup</c> and neither after.
/// What the run "writes" is anything else: the shelter never reads a file, it only moves one.
/// </para>
/// </remarks>
[PrebuildSetup(typeof(SaveShelter))]
[PostBuildCleanup(typeof(SaveShelter))]
public sealed class SaveShelterTests
{
    private const string Run =
        "{\"version\":4,\"modeId\":\"mode.descent\",\"characterId\":\"character.ranger\"," +
        "\"seed\":180062718,\"stageIndex\":1,\"randomSpawn\":9242365301571780524," +
        "\"randomOffers\":7318402253708750789,\"randomAffixes\":6408751862959914241," +
        "\"randomDrops\":5469827269080153087,\"randomMisc\":8106522484612819050," +
        "\"playerHp\":90.0,\"playerShield\":0.0,\"runTime\":0.0," +
        "\"writtenAt\":\"2026-09-25T23:14:48.9305271+00:00\",\"level\":1,\"xp\":0.0," +
        "\"pendingLevelUps\":0,\"takenNodeIds\":[],\"manualSkillIds\":[\"\",\"\",\"\",\"\"]," +
        "\"essence\":0,\"veilrot\":0.0,\"rerollsBought\":0,\"rerollsSpent\":0,\"claimed\":false," +
        "\"banishedNodeIds\":[],\"pactedNodeIds\":[],\"ordealIds\":[]}";

    private const string Profile =
        "{\"version\":4,\"hapticsEnabled\":true,\"seenFirstActiveHint\":true,\"shards\":1415," +
        "\"unlockedCharacterIds\":[\"character.gravecaller\",\"character.emberwright\"]," +
        "\"metArchetypeIds\":[\"enemy.husk\",\"enemy.spitter\",\"enemy.bloater\"],\"locale\":\"\"}";

    /// <summary>What a run writes over the saves: any bytes that are not the originals.</summary>
    private const string Written = "{\"written\":\"by the run\"}";

    private string _directory;
    private string _run;
    private string _profile;

    [SetUp]
    public void CreateTemporaryDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "soulvail-shelter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _run = Path.Combine(_directory, LocalJsonSaveStore.RunFileName);
        _profile = Path.Combine(_directory, LocalJsonSaveStore.ProfileFileName);
    }

    [TearDown]
    public void DeleteTemporaryDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Rule 1. Every fixture, not only the two that start a run: a row run alone must meet the same
    /// empty folder, and the next fixture must not be able to forget.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="CustomAttributeData"/> because the attributes keep the type they name
    /// <c>internal</c>. A fixture is any type declaring a method NUnit builds a test from — <c>[Test]</c>,
    /// <c>[UnityTest]</c> or <c>[TestCase]</c>.
    /// </remarks>
    [Test]
    public void EveryFixture_SheltersTheSaves()
    {
        Type[] fixtures = typeof(SaveShelterTests).Assembly.GetTypes().Where(DeclaresATest).ToArray();

        // The control: the scan finds the two fixtures that start a run through boot's store.
        Assert.That(fixtures, Has.Member(typeof(BootSmokeTests)));
        Assert.That(fixtures, Has.Member(typeof(RunBodyTests)));

        foreach (Type fixture in fixtures)
        {
            Assert.That(
                Names(fixture, typeof(PrebuildSetupAttribute)),
                Is.True,
                $"{fixture.Name} does not carry [PrebuildSetup(typeof(SaveShelter))], so run alone it " +
                "reads and writes the machine's real saves.");

            Assert.That(
                Names(fixture, typeof(PostBuildCleanupAttribute)),
                Is.True,
                $"{fixture.Name} does not carry [PostBuildCleanup(typeof(SaveShelter))], so run alone " +
                "it would leave the saves in the shelter.");
        }
    }

    [Test]
    public void Setup_LeavesAFreshInstall()
    {
        WriteBothSaves();
        var store = new LocalJsonSaveStore(_directory);

        Assert.That(store.LoadProfile().Result.HasValue, Is.True, "The control: the store reads the profile.");
        Assert.That(store.LoadRun().Result.HasValue, Is.True, "The control: the store reads the run.");

        new SaveShelter(_directory).Setup();

        Assert.That(File.Exists(_run), Is.False, "run.json stayed where the suite would read it.");
        Assert.That(File.Exists(_profile), Is.False, "profile.json stayed where the suite would read it.");
        Assert.That(store.LoadProfile().Result.HasValue, Is.False, "The suite would play the machine's profile.");
        Assert.That(store.LoadRun().Result.HasValue, Is.False, "The suite would be offered the machine's run.");
    }

    [Test]
    public void Cleanup_PutsTheSavesBackByteForByte()
    {
        WriteBothSaves();
        byte[] run = File.ReadAllBytes(_run);
        byte[] profile = File.ReadAllBytes(_profile);
        var shelter = new SaveShelter(_directory);

        shelter.Setup();
        WriteOverBoth();
        shelter.Cleanup();

        Assert.That(File.ReadAllBytes(_run), Is.EqualTo(run), "run.json is not the one that was there.");
        Assert.That(File.ReadAllBytes(_profile), Is.EqualTo(profile), "profile.json is not the one that was there.");
        Assert.That(Directory.Exists(ShelterPath), Is.False, "The shelter outlived the run.");
    }

    [Test]
    public void Cleanup_RemovesARunWrittenWhereThereWasNone()
    {
        File.WriteAllText(_profile, Profile);
        byte[] profile = File.ReadAllBytes(_profile);
        var shelter = new SaveShelter(_directory);

        shelter.Setup();
        WriteOverBoth();
        shelter.Cleanup();

        Assert.That(File.Exists(_run), Is.False, "The run's run.json was left behind, so the Menu offers Continue.");
        Assert.That(File.ReadAllBytes(_profile), Is.EqualTo(profile), "profile.json is not the one that was there.");
        Assert.That(Directory.Exists(ShelterPath), Is.False, "The shelter outlived the run.");
    }

    [Test]
    public void Cleanup_WithoutAShelter_TouchesNothing()
    {
        WriteBothSaves();

        new SaveShelter(_directory).Cleanup();

        Assert.That(File.ReadAllText(_run), Is.EqualTo(Run));
        Assert.That(File.ReadAllText(_profile), Is.EqualTo(Profile));
    }

    /// <summary>
    /// Rule 4: a <c>Setup</c> killed between its two moves. The run is in the shelter, and the
    /// profile, with neither a copy nor a marker there, never left.
    /// </summary>
    [Test]
    public void Cleanup_LeavesAFileSetupNeverReached()
    {
        Directory.CreateDirectory(ShelterPath);
        File.WriteAllText(Path.Combine(ShelterPath, LocalJsonSaveStore.RunFileName), Run);
        File.WriteAllText(_profile, Profile);

        new SaveShelter(_directory).Cleanup();

        Assert.That(File.ReadAllText(_run), Is.EqualTo(Run), "The sheltered run did not come back.");
        Assert.That(File.ReadAllText(_profile), Is.EqualTo(Profile), "A profile the shelter never held was touched.");
        Assert.That(Directory.Exists(ShelterPath), Is.False);
    }

    /// <summary>Rule 5: an Editor killed mid-pass, then the next pass.</summary>
    [Test]
    public void Setup_RestoresAShelterLeftBehind()
    {
        WriteBothSaves();
        byte[] run = File.ReadAllBytes(_run);
        byte[] profile = File.ReadAllBytes(_profile);

        new SaveShelter(_directory).Setup();
        WriteOverBoth();

        LogAssert.Expect(LogType.Warning, new Regex("left the saves sheltered"));

        var next = new SaveShelter(_directory);

        next.Setup();

        Assert.That(File.Exists(_run), Is.False, "The next run did not start from a fresh install.");
        Assert.That(File.Exists(_profile), Is.False, "The next run did not start from a fresh install.");

        next.Cleanup();

        Assert.That(File.ReadAllBytes(_run), Is.EqualTo(run), "The killed run's run.json replaced the original.");
        Assert.That(File.ReadAllBytes(_profile), Is.EqualTo(profile), "The killed run's profile replaced the original.");
        Assert.That(Directory.Exists(ShelterPath), Is.False);
    }

    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_NullOrBlankDirectory_Throws(string directory)
    {
        Assert.Throws<ArgumentException>(() => new SaveShelter(directory));
    }

    private string ShelterPath => Path.Combine(_directory, SaveShelter.FolderName);

    private void WriteBothSaves()
    {
        File.WriteAllText(_run, Run);
        File.WriteAllText(_profile, Profile);
    }

    private void WriteOverBoth()
    {
        File.WriteAllText(_run, Written);
        File.WriteAllText(_profile, Written);
    }

    private static bool DeclaresATest(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Any(method => method.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute is ISimpleTestBuilder or ITestBuilder));

    private static bool Names(Type fixture, Type attribute) =>
        fixture.GetCustomAttributesData().Any(data =>
            data.AttributeType == attribute
            && data.ConstructorArguments.Count == 1
            && (data.ConstructorArguments[0].Value as Type) == typeof(SaveShelter));
}
