using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Save;

/// <summary>
/// The version gate, and the run chain — one step long as of M3-01b.
/// </summary>
/// <remarks>
/// <para>
/// <c>Chain_IsUnbrokenFromOldestToCurrent</c> is why this fixture existed at v1, while there was
/// nothing to migrate: it is the row that fails the day a <c>CurrentVersion</c> is bumped without a
/// step being written, which is the only mechanism in the project that makes AR §11.6's promise
/// self-enforcing rather than remembered.
/// </para>
/// <para>
/// <b>M3-01b is the first time it looped over more than one version</b>, and its text did not have
/// to change to do it — which is the whole point of having written it at v1. What the bump added
/// beside it is <c>Migrate_V1_GetsUnlevelledDefaults</c>: the chain row says a v1 save still
/// <em>loads</em>, and only a fixture row can say what it loads <em>as</em>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SaveMigrationTests
{
    private static readonly ContentId Mode = new ContentId("mode.descent");
    private static readonly ContentId Character = new ContentId("character.oathbound");

    /// <summary>Two node ids, for the rows that need the v2 list to be non-empty.</summary>
    private static readonly ContentId Bulwark = new ContentId("skill.oathbound.bulwark");
    private static readonly ContentId Consecrate = new ContentId("skill.oathbound.consecrate");

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
    public void Gate_AcceptsOneToThreeRefusesFour()
    {
        // The numbers written out, which the four rows around this one deliberately cannot say:
        // they are all phrased against CurrentVersion, so they would keep passing unchanged if the
        // floor were raised to 2 and every v1 save on every device stopped loading. This row is
        // what notices — OldestSupportedRunVersion stays 1 (rule 4).
        Assert.That(SaveMigrations.CanReadRun(1), Is.True, "v1 saves are still on devices.");
        Assert.That(SaveMigrations.CanReadRun(2), Is.True, "and so are v2 ones.");
        Assert.That(SaveMigrations.CanReadRun(3), Is.True);
        Assert.That(SaveMigrations.CanReadRun(4), Is.False);

        Assert.That(SaveMigrations.OldestSupportedRunVersion, Is.EqualTo(1));
        Assert.That(RunSnapshot.CurrentVersion, Is.EqualTo(3));
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
    public void Migrate_V1_RunsBothStepsInOrder()
    {
        // A v1 DTO that *does* carry levelling and a loadout, which no real v1 document can — the
        // adapter's mirror would have nowhere to read either from. Written this way on purpose:
        // rule 3 says each step is the authority and writes its own fields regardless of what the
        // mirror held, and a fixture whose input was already empty could not tell that apart from a
        // step that simply passed the fields through.
        RunSnapshot decoded = SnapshotAt(
            1,
            level: 5,
            xp: 99f,
            pendingLevelUps: 2,
            takenNodeIds: new[] { Bulwark },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) });

        RunSnapshot migrated = SaveMigrations.MigrateRun(1, decoded);

        // **v3 from a v1 input: the first two-step migration this project has ever run** (rule 4).
        // Landing at 2 is what a chain whose second step reads `decoded` instead of `current` would
        // produce, and landing at 3 with v2's fields unwritten is what one whose steps ran out of
        // order would — so this single number is load-bearing twice over.
        Assert.That(migrated.Version, Is.EqualTo(3));

        // A v1 run was unlevelled by construction, so its v2 form is the opening state of a run.
        Assert.That(migrated.Level, Is.EqualTo(1));
        Assert.That(migrated.Xp, Is.EqualTo(0f));
        Assert.That(migrated.PendingLevelUps, Is.EqualTo(0));
        Assert.That(migrated.TakenNodeIds, Is.Empty);

        // And it had no loadout either, so the second step empties what the first left alone.
        Assert.That(migrated.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(migrated.ManualSkillIds, Is.All.EqualTo(default(ContentId)));

        // And every v1 field survives exactly as it was decoded. A migration that added fields and
        // quietly moved an existing one is the failure this half exists to catch.
        Assert.That(migrated.ModeId, Is.EqualTo(decoded.ModeId));
        Assert.That(migrated.CharacterId, Is.EqualTo(decoded.CharacterId));
        Assert.That(migrated.Seed, Is.EqualTo(decoded.Seed));
        Assert.That(migrated.StageIndex, Is.EqualTo(decoded.StageIndex));
        Assert.That(migrated.Random.Spawn, Is.EqualTo(decoded.Random.Spawn));
        Assert.That(migrated.Random.Offers, Is.EqualTo(decoded.Random.Offers));
        Assert.That(migrated.Random.Affixes, Is.EqualTo(decoded.Random.Affixes));
        Assert.That(migrated.Random.Drops, Is.EqualTo(decoded.Random.Drops));
        Assert.That(migrated.Random.Misc, Is.EqualTo(decoded.Random.Misc));
        Assert.That(migrated.PlayerHp, Is.EqualTo(decoded.PlayerHp));
        Assert.That(migrated.PlayerShield, Is.EqualTo(decoded.PlayerShield));
        Assert.That(migrated.RunTime, Is.EqualTo(decoded.RunTime));
        Assert.That(migrated.WrittenAt, Is.EqualTo(decoded.WrittenAt));
    }

    [Test]
    public void Migrate_V3_IsIdentity()
    {
        // **Renamed from Migrate_V2_IsIdentity rather than joined by a second row**: identity is a
        // property of the *current* version, so it moves up with every bump and there is only ever
        // one such row. What used to be this row's subject is now Migrate_V2_GetsEmptySlots, which
        // is a step rather than an identity — and that is exactly the transition a bump makes.
        RunSnapshot original = SnapshotAt(
            RunSnapshot.CurrentVersion,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) });

        RunSnapshot migrated = SaveMigrations.MigrateRun(RunSnapshot.CurrentVersion, original);

        Assert.That(migrated.Level, Is.EqualTo(7));
        Assert.That(migrated.Xp, Is.EqualTo(33.5f));
        Assert.That(migrated.PendingLevelUps, Is.EqualTo(1));
        Assert.That(migrated.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));

        // The hole included: an identity that compacted the slots would be the silent re-bind rule
        // 1 exists to refuse, and it would be invisible in a fixture whose slots were contiguous.
        Assert.That(
            migrated.ManualSkillIds,
            Is.EqualTo(new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) }));

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
    public void Migrate_V2_GetsEmptySlots()
    {
        // A v2 DTO that *does* carry a loadout, which no real v2 document can — the adapter's
        // mirror had no such key. Migrate_V1_RunsBothStepsInOrder's reasoning: the step is the
        // authority and writes its field regardless of what the mirror held, and an input that was
        // already empty could not tell that apart from a step that passed the field through.
        RunSnapshot decoded = SnapshotAt(
            2,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, Consecrate, default(ContentId), default(ContentId) });

        RunSnapshot migrated = SaveMigrations.MigrateRun(2, decoded);

        Assert.That(migrated.Version, Is.EqualTo(3));

        // A v2 run had no loadout by construction, so its v3 form is four empty slots — every skill
        // on Auto, which is also CC §6.1's default (rule 5).
        Assert.That(migrated.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(migrated.ManualSkillIds, Is.All.EqualTo(default(ContentId)));

        // And every v2 field survives exactly as it was decoded. A step that added a field and
        // quietly reset an existing one is the failure this half exists to catch — and here the
        // node list is the one at risk, because it is the same type as the field being added.
        Assert.That(migrated.Level, Is.EqualTo(7));
        Assert.That(migrated.Xp, Is.EqualTo(33.5f));
        Assert.That(migrated.PendingLevelUps, Is.EqualTo(1));
        Assert.That(migrated.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));
        Assert.That(migrated.ModeId, Is.EqualTo(decoded.ModeId));
        Assert.That(migrated.CharacterId, Is.EqualTo(decoded.CharacterId));
        Assert.That(migrated.Seed, Is.EqualTo(decoded.Seed));
        Assert.That(migrated.StageIndex, Is.EqualTo(decoded.StageIndex));
        Assert.That(migrated.Random.Misc, Is.EqualTo(decoded.Random.Misc));
        Assert.That(migrated.PlayerHp, Is.EqualTo(decoded.PlayerHp));
        Assert.That(migrated.PlayerShield, Is.EqualTo(decoded.PlayerShield));
        Assert.That(migrated.RunTime, Is.EqualTo(decoded.RunTime));
        Assert.That(migrated.WrittenAt, Is.EqualTo(decoded.WrittenAt));
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
    /// <remarks>
    /// The v2 fields default to an unlevelled run, so <c>Chain_IsUnbrokenFromOldestToCurrent</c>
    /// can go on saying nothing about them: it asserts that every supported version migrates up to
    /// the current one, and it is the row whose <em>text does not change</em> when a format is
    /// bumped. That is the whole point of having written it at v1 (rule 2).
    /// </remarks>
    private static RunSnapshot SnapshotAt(
        int version,
        int level = 1,
        float xp = 0f,
        int pendingLevelUps = 0,
        IReadOnlyList<ContentId> takenNodeIds = null,
        IReadOnlyList<ContentId> manualSkillIds = null)
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
            Written,
            level,
            xp,
            pendingLevelUps,
            takenNodeIds ?? Array.Empty<ContentId>(),
            manualSkillIds ?? new ContentId[SkillRunner.MaxManualSlots]);
    }
}
