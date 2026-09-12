using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Save;

/// <summary>
/// The version gate, and the chain that is empty at v1.
/// </summary>
/// <remarks>
/// <c>Chain_IsUnbrokenFromOldestToCurrent</c> is why this fixture exists now, while there is
/// nothing to migrate: it is the row that fails the day a <c>CurrentVersion</c> is bumped without
/// a step being written, which is the only mechanism in the project that makes AR §11.6's promise
/// self-enforcing rather than remembered.
/// </remarks>
[TestFixture]
public sealed class SaveMigrationTests
{
    private static readonly ContentId Mode = new ContentId("mode.descent");
    private static readonly ContentId Character = new ContentId("character.oathbound");

    private static readonly DateTimeOffset Written =
        new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    [Test]
    public void Gate_AcceptsCurrent()
    {
        Assert.That(SaveMigrations.CanReadRun(RunSnapshot.CurrentVersion), Is.True);
    }

    [Test]
    public void Gate_RefusesZero()
    {
        // Zero is what default(RunSnapshot) carries and no writer can produce, so a file claiming
        // it was never written by this game.
        Assert.That(SaveMigrations.CanReadRun(0), Is.False);
    }

    [Test]
    public void Gate_RefusesOlderThanSupported()
    {
        Assert.That(
            SaveMigrations.CanReadRun(SaveMigrations.OldestSupportedRunVersion - 1),
            Is.False);
    }

    [Test]
    public void Gate_RefusesNewerThanCurrent()
    {
        // A downgraded build must refuse a format it has never seen rather than read it as though
        // the fields it does not know about were simply absent.
        Assert.That(SaveMigrations.CanReadRun(RunSnapshot.CurrentVersion + 1), Is.False);
    }

    [Test]
    public void Migrations_HoldsNoState()
    {
        FieldInfo[] fields = typeof(SaveMigrations).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            Assert.That(
                field.IsLiteral || field.IsInitOnly,
                Is.True,
                $"SaveMigrations.{field.Name} is mutable state. Migrations are pure functions " +
                "(ADR-0007), and a static with a setter is one nothing resets under a disabled " +
                "domain reload.");
        }
    }

    [Test]
    public void Chain_IsUnbrokenFromOldestToCurrent()
    {
        for (int version = SaveMigrations.OldestSupportedRunVersion;
            version <= RunSnapshot.CurrentVersion;
            version++)
        {
            Assert.That(
                SaveMigrations.CanReadRun(version),
                Is.True,
                $"Version {version} is inside the supported range but the gate refuses it.");

            RunSnapshot migrated = SaveMigrations.MigrateRun(version, SnapshotAt(version));

            Assert.That(
                migrated.Version,
                Is.EqualTo(RunSnapshot.CurrentVersion),
                $"Migrating a v{version} run left it at v{migrated.Version}. Bumping " +
                "CurrentVersion needs a step in SaveMigrations, and a fixture beside it.");
        }
    }

    [Test]
    public void Migrate_AtCurrent_IsIdentity()
    {
        RunSnapshot original = SnapshotAt(RunSnapshot.CurrentVersion);

        RunSnapshot migrated = SaveMigrations.MigrateRun(RunSnapshot.CurrentVersion, original);

        Assert.That(migrated.Version, Is.EqualTo(original.Version));
        Assert.That(migrated.ModeId, Is.EqualTo(original.ModeId));
        Assert.That(migrated.CharacterId, Is.EqualTo(original.CharacterId));
        Assert.That(migrated.Seed, Is.EqualTo(original.Seed));
        Assert.That(migrated.StageIndex, Is.EqualTo(original.StageIndex));
        Assert.That(migrated.Random.Spawn, Is.EqualTo(original.Random.Spawn));
        Assert.That(migrated.Random.Offers, Is.EqualTo(original.Random.Offers));
        Assert.That(migrated.Random.Affixes, Is.EqualTo(original.Random.Affixes));
        Assert.That(migrated.Random.Drops, Is.EqualTo(original.Random.Drops));
        Assert.That(migrated.Random.Misc, Is.EqualTo(original.Random.Misc));
        Assert.That(migrated.PlayerHp, Is.EqualTo(original.PlayerHp));
        Assert.That(migrated.PlayerShield, Is.EqualTo(original.PlayerShield));
        Assert.That(migrated.RunTime, Is.EqualTo(original.RunTime));
        Assert.That(migrated.WrittenAt, Is.EqualTo(original.WrittenAt));
    }

    [Test]
    public void Migrate_RefusedVersion_Throws()
    {
        RunSnapshot snapshot = SnapshotAt(RunSnapshot.CurrentVersion);

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(
            () => SaveMigrations.MigrateRun(0, snapshot));

        // Naming the version is the difference between a bug report that can be acted on and one
        // that says the save did not load.
        Assert.That(thrown.Message, Does.Contain("0"));
    }

    /// <summary>
    /// The four gate rows and the two chain rows again, for the other DTO.
    /// </summary>
    /// <remarks>
    /// One row rather than six copies: the profile's gate is the run's gate with two constants
    /// swapped, and what is worth asserting is that it stayed that way — the day the two formats
    /// version independently, this is what notices if only one of them grew a step.
    /// </remarks>
    [Test]
    public void Profile_ChainAndGateMirrorTheRun()
    {
        Assert.That(SaveMigrations.CanReadProfile(PlayerProfile.CurrentVersion), Is.True);
        Assert.That(SaveMigrations.CanReadProfile(0), Is.False);
        Assert.That(
            SaveMigrations.CanReadProfile(SaveMigrations.OldestSupportedProfileVersion - 1),
            Is.False);
        Assert.That(SaveMigrations.CanReadProfile(PlayerProfile.CurrentVersion + 1), Is.False);

        for (int version = SaveMigrations.OldestSupportedProfileVersion;
            version <= PlayerProfile.CurrentVersion;
            version++)
        {
            var profile = new PlayerProfile(version, hapticsEnabled: false);

            PlayerProfile migrated = SaveMigrations.MigrateProfile(version, profile);

            Assert.That(migrated.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
            Assert.That(migrated.HapticsEnabled, Is.False);
        }

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(
            () => SaveMigrations.MigrateProfile(0, PlayerProfile.Default));

        Assert.That(thrown.Message, Does.Contain("0"));
    }

    /// <summary>A snapshot in <paramref name="version"/>'s format, with every field distinct.</summary>
    private static RunSnapshot SnapshotAt(int version)
    {
        return new RunSnapshot(
            version,
            Mode,
            Character,
            seed: 7,
            stageIndex: 4,
            new RandomState(1UL, 2UL, 3UL, 4UL, 5UL),
            playerHp: 61f,
            playerShield: 12f,
            runTime: 138.5f,
            Written);
    }
}
