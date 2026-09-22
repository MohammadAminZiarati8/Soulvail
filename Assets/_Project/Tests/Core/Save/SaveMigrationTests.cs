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
/// The version gate, the run chain — three steps as of M6-01b — and, as of M3-09c, a profile chain
/// with a step in it for the first time.
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
/// beside it is <c>Migrate_V1_RunsEveryStepInOrder</c>: the chain row says a v1 save still
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

    /// <summary>
    /// Two more, for v4's lists. A third node and an Ordeal, so no row can pass by putting the same
    /// id in every list.
    /// </summary>
    private static readonly ContentId Reprisal = new ContentId("skill.oathbound.reprisal");
    private static readonly ContentId Thinblood = new ContentId("ordeal.thinblood");

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
    public void Gate_AcceptsOneToFourRefusesFive()
    {
        // The numbers written out, which the four rows around this one deliberately cannot say:
        // they are all phrased against CurrentVersion, so they would keep passing unchanged if the
        // floor were raised to 2 and every v1 save on every device stopped loading. This row is
        // what notices — OldestSupportedRunVersion stays 1 (rule 4). **Renamed at the v4 bump**,
        // because the numbers it names are the subject.
        Assert.That(SaveMigrations.CanReadRun(1), Is.True, "v1 saves are still on devices.");
        Assert.That(SaveMigrations.CanReadRun(2), Is.True, "and so are v2 ones.");
        Assert.That(SaveMigrations.CanReadRun(3), Is.True, "and so are v3 ones.");
        Assert.That(SaveMigrations.CanReadRun(4), Is.True);
        Assert.That(SaveMigrations.CanReadRun(5), Is.False);

        Assert.That(SaveMigrations.OldestSupportedRunVersion, Is.EqualTo(1));
        Assert.That(RunSnapshot.CurrentVersion, Is.EqualTo(4));

        // And a version above the current one is refused at the gate *and* at the migration, which
        // are two different answers a downgraded build has to get right: reading a v5 document as
        // though the fields it does not know were absent would silently delete them on the next
        // write. `ProfileGate_AcceptsOneToThreeRefusesFour`'s pairing, for this format.
        Assert.Throws<NotSupportedException>(
            () => SaveMigrations.MigrateRun(5, SnapshotAt(RunSnapshot.CurrentVersion)));
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
    public void Migrate_V1_RunsEveryStepInOrder()
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
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) },
            economy: new RunEconomy(500, 42.5f, 3, 2),
            banishedNodeIds: new[] { Bulwark },
            pactedNodeIds: new[] { Consecrate },
            ordealIds: new[] { Thinblood });

        RunSnapshot migrated = SaveMigrations.MigrateRun(1, decoded);

        // **v4 from a v1 input: the chain, now three steps** (M6-01b rule 5). Landing at 2 or 3 is
        // what a chain whose later steps read `decoded` instead of `current` would produce, and
        // landing at 4 with an earlier step's fields unwritten is what one whose steps ran out of
        // order would — so this single number is load-bearing three times over.
        Assert.That(migrated.Version, Is.EqualTo(4));

        // A v1 run was unlevelled by construction, so its v2 form is the opening state of a run.
        Assert.That(migrated.Level, Is.EqualTo(1));
        Assert.That(migrated.Xp, Is.EqualTo(0f));
        Assert.That(migrated.PendingLevelUps, Is.EqualTo(0));
        Assert.That(migrated.TakenNodeIds, Is.Empty);

        // And it had no loadout either, so the second step empties what the first left alone.
        Assert.That(migrated.ManualSkillIds, Has.Count.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(migrated.ManualSkillIds, Is.All.EqualTo(default(ContentId)));

        // And no economy, which is the third step's answer over the top of both: a v1 build had no
        // Essence, no Veilrot, no rerolls, no Banish, no Pact and no Ordeal in it at all.
        Assert.That(migrated.Economy.Essence, Is.Zero);
        Assert.That(migrated.Economy.Veilrot, Is.Zero);
        Assert.That(migrated.Economy.RerollsBought, Is.Zero);
        Assert.That(migrated.Economy.RerollsSpent, Is.Zero);
        Assert.That(migrated.BanishedNodeIds, Is.Empty);
        Assert.That(migrated.PactedNodeIds, Is.Empty);
        Assert.That(migrated.OrdealIds, Is.Empty);

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
    public void Migrate_V4_IsIdentity()
    {
        // **Renamed from Migrate_V3_IsIdentity rather than joined by a second row**: identity is a
        // property of the *current* version, so it moves up with every bump and there is only ever
        // one such row. What used to be this row's subject is now Migrate_V3_GetsAnEmptyEconomy,
        // which is a step rather than an identity — and that is exactly the transition a bump makes.
        RunSnapshot original = SnapshotAt(
            RunSnapshot.CurrentVersion,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) },
            economy: new RunEconomy(317, 42.5f, 2, 1),
            banishedNodeIds: new[] { Reprisal },
            pactedNodeIds: new[] { Consecrate },
            ordealIds: new[] { Thinblood });

        RunSnapshot migrated = SaveMigrations.MigrateRun(RunSnapshot.CurrentVersion, original);

        // Every v4 field set away from its default, so a step that ran when it should not have
        // moves one of them and this goes red.
        Assert.That(migrated.Economy.Essence, Is.EqualTo(317));
        Assert.That(migrated.Economy.Veilrot, Is.EqualTo(42.5f));
        Assert.That(migrated.Economy.RerollsBought, Is.EqualTo(2));
        Assert.That(migrated.Economy.RerollsSpent, Is.EqualTo(1));
        Assert.That(migrated.BanishedNodeIds, Is.EqualTo(new[] { Reprisal }));
        Assert.That(migrated.PactedNodeIds, Is.EqualTo(new[] { Consecrate }));
        Assert.That(migrated.OrdealIds, Is.EqualTo(new[] { Thinblood }));

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
        // mirror had no such key. Migrate_V1_RunsEveryStepInOrder's reasoning: the step is the
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

        // **4 rather than 3 as of M6-01b**: a v2 document walks the v2 → v3 step this row is about
        // and then the v3 → v4 one, which is what a chain being a chain means.
        Assert.That(migrated.Version, Is.EqualTo(4));

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
    public void Migrate_V3_GetsAnEmptyEconomy()
    {
        // A v3 DTO that *does* carry an economy, three lists and a hole in its slots, which no real
        // v3 document can — the adapter's mirror had none of those keys. Migrate_V2_GetsEmptySlots'
        // reasoning: the step is the authority and writes its fields regardless of what the mirror
        // held, and an input that was already empty could not tell that apart from a step that
        // passed them through.
        RunSnapshot decoded = SnapshotAt(
            3,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: new[] { Bulwark, Consecrate },
            manualSkillIds: new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) },
            economy: new RunEconomy(500, 42.5f, 3, 2),
            banishedNodeIds: new[] { Reprisal },
            pactedNodeIds: new[] { Consecrate },
            ordealIds: new[] { Thinblood });

        RunSnapshot migrated = SaveMigrations.MigrateRun(3, decoded);

        Assert.That(migrated.Version, Is.EqualTo(4));

        // A v3 run had no economy by construction — none of the mechanics existed — so its v4 form
        // is `default(RunEconomy)` and three empty lists, which is the opening state of a fresh run
        // (M6-01b rule 5). All four scalars, because one guard reading zero says nothing about the
        // other three.
        Assert.That(migrated.Economy.Essence, Is.Zero);
        Assert.That(migrated.Economy.Veilrot, Is.Zero);
        Assert.That(migrated.Economy.RerollsBought, Is.Zero);
        Assert.That(migrated.Economy.RerollsSpent, Is.Zero);
        Assert.That(migrated.BanishedNodeIds, Is.Empty);
        Assert.That(migrated.PactedNodeIds, Is.Empty);
        Assert.That(migrated.OrdealIds, Is.Empty);

        // And **every v3 field survives exactly as it was decoded**. A step that added fields and
        // quietly reset an existing one is the failure this half exists to catch — and here the
        // three lists are what put the two node lists at risk, because they are the same type as
        // the fields being added.
        Assert.That(migrated.Level, Is.EqualTo(7));
        Assert.That(migrated.Xp, Is.EqualTo(33.5f));
        Assert.That(migrated.PendingLevelUps, Is.EqualTo(1));
        Assert.That(migrated.TakenNodeIds, Is.EqualTo(new[] { Bulwark, Consecrate }));
        Assert.That(
            migrated.ManualSkillIds,
            Is.EqualTo(new[] { Bulwark, default(ContentId), Consecrate, default(ContentId) }),
            "The hole included: a step that compacted the slots is the silent re-bind M3-07a refuses.");

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
    /// <para>
    /// <b>M3-09c is the first time this loop ran more than once</b>, and its assertions did not have
    /// to change to do it — which is the whole point of having written it at v1. <b>M4-05b is the
    /// first time it ran over three versions, and they still did not have to change</b>: the one
    /// edit the bump forced on this row is the constructor argument every call site in the project
    /// gained, which is a compiler ripple rather than a claim. The things a loop cannot say are said
    /// beside it, by <see cref="MigrateProfile_V1_RunsBothStepsInOrder"/>,
    /// <see cref="MigrateProfile_V2_GainsNoShards"/> and <see cref="MigrateProfile_V3_IsIdentity"/>.
    /// </para>
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
            var profile = new PlayerProfile(
                version, hapticsEnabled: false, seenFirstActiveHint: false, shards: 0);

            PlayerProfile migrated = SaveMigrations.MigrateProfile(version, profile);

            Assert.That(
                migrated.Version,
                Is.EqualTo(PlayerProfile.CurrentVersion),
                $"Migrating a v{version} profile left it at v{migrated.Version}. Bumping " +
                "PlayerProfile.CurrentVersion needs a step in SaveMigrations, and a fixture " +
                "beside it.");

            Assert.That(migrated.HapticsEnabled, Is.False);
        }

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(
            () => SaveMigrations.MigrateProfile(0, PlayerProfile.Default));

        Assert.That(thrown.Message, Does.Contain("0"));
    }

    [Test]
    public void ProfileGate_AcceptsOneToThreeRefusesFour()
    {
        // The numbers written out, which the row above deliberately cannot say: it is phrased
        // against CurrentVersion throughout, so it would keep passing unchanged if the floor were
        // raised to 2 and every v1 profile on every device stopped loading. This row is what
        // notices — OldestSupportedProfileVersion stays 1 (rule 2). `Gate_AcceptsOneToFourRefusesFive`'s
        // job, for the other format, and **renamed with the v3 bump** for its reason.
        Assert.That(SaveMigrations.CanReadProfile(1), Is.True, "v1 profiles are still on devices.");
        Assert.That(SaveMigrations.CanReadProfile(2), Is.True, "and so are v2 ones.");
        Assert.That(SaveMigrations.CanReadProfile(3), Is.True);
        Assert.That(SaveMigrations.CanReadProfile(4), Is.False);

        Assert.That(SaveMigrations.OldestSupportedProfileVersion, Is.EqualTo(1));
        Assert.That(PlayerProfile.CurrentVersion, Is.EqualTo(3));

        // And the run gate does not answer for the profile: v4 is a run this build reads and a
        // profile it refuses, which is the independence stated as two different answers to one
        // number rather than as prose.
        Assert.That(SaveMigrations.CanReadRun(4), Is.True);

        // And a version above the current one is refused at the gate *and* at the migration, which
        // are two different answers a downgraded build has to get right: reading a v4 document as
        // though the fields it does not know were absent would silently delete them on the next
        // write. A throw rather than a null, because by there the caller has already asked the gate.
        Assert.Throws<NotSupportedException>(
            () => SaveMigrations.MigrateProfile(4, PlayerProfile.Default));

        // And the run format did not move with the profile, which is the independence M2-13b built
        // two methods for. **The two numbers read 4 and 3 as of M6-01b** — they were equal for one
        // milestone, which was the coincidence this row was written to make visible, and the run
        // bumping without the profile is that independence exercised rather than asserted.
        // M6-09a is the profile's own one bump.
        Assert.That(RunSnapshot.CurrentVersion, Is.EqualTo(4));
    }

    [Test]
    public void MigrateProfile_V1_RunsBothStepsInOrder()
    {
        // A v1 DTO that *does* carry the flag and a Shard total, neither of which any real v1
        // document can — v1 had no such key and no build that wrote one had a skill or a payout in
        // it. They are set here precisely so the row can tell "the step wrote it" from "the input
        // happened to be that" (M3-01b rule 3's shape, on the other format).
        var decoded = new PlayerProfile(
            1, hapticsEnabled: false, seenFirstActiveHint: true, shards: 999);

        PlayerProfile migrated = SaveMigrations.MigrateProfile(1, decoded);

        // **v3 from a v1 input: the first time the profile chain runs two steps on one document**
        // (M4-05b rule 4), and `Migrate_V1_RunsEveryStepInOrder`'s shape on this format. Landing at
        // 2 is what a chain whose second step reads `decoded` instead of `current` would produce,
        // and landing at 3 with v2's field unwritten is what one whose steps ran out of order would
        // — so this single number is load-bearing twice over.
        Assert.That(migrated.Version, Is.EqualTo(3));

        // The first step: a v1 profile was written by a build with no skills in it, so there was no
        // first Active to be told about and the callout cannot have been shown.
        Assert.That(
            migrated.SeenFirstActiveHint,
            Is.False,
            "the step is the authority: a v1 document is a v1 document whatever it carries.");

        // The second: a v2 profile was written by a build with no payout in it, so zero is the truth
        // about that player rather than a default standing in for an unknown (rule 3).
        Assert.That(migrated.Shards, Is.Zero);

        // And the one v1 field is kept exactly as it was read, because it *is* something the player
        // chose — which is the difference between it and the two fields written above.
        Assert.That(migrated.HapticsEnabled, Is.False);
    }

    [Test]
    public void MigrateProfile_V2_GainsNoShards()
    {
        // A v2 DTO that *does* carry a total, which no real v2 document can — the adapter's mirror
        // had no such key. `Migrate_V2_GetsEmptySlots`' reasoning on this format: the step is the
        // authority and writes its field regardless of what the mirror held, and an input that was
        // already zero could not tell that apart from a step that passed the field through.
        var decoded = new PlayerProfile(
            2, hapticsEnabled: false, seenFirstActiveHint: true, shards: 999);

        PlayerProfile migrated = SaveMigrations.MigrateProfile(2, decoded);

        Assert.That(migrated.Version, Is.EqualTo(3));
        Assert.That(
            migrated.Shards,
            Is.Zero,
            "a v2 document was written by a build with no payout in it, so zero is the truth.");

        // And both v2 fields survive untouched, set the opposite way round from each other so a
        // step that authored the struct from the one field it knew moves one of them.
        Assert.That(migrated.HapticsEnabled, Is.False);
        Assert.That(migrated.SeenFirstActiveHint, Is.True);
    }

    [Test]
    public void MigrateProfile_V3_IsIdentity()
    {
        var decoded = new PlayerProfile(
            3, hapticsEnabled: false, seenFirstActiveHint: true, shards: 220);

        PlayerProfile migrated = SaveMigrations.MigrateProfile(3, decoded);

        // **Renamed from MigrateProfile_V2_IsIdentity rather than joined by a second row**:
        // identity is a property of the *current* version, so it moves up with every bump and there
        // is only ever one of it — `Migrate_V4_IsIdentity`'s rule, for the other format. What used
        // to be this row's subject is now MigrateProfile_V2_GainsNoShards, which is a step rather
        // than an identity, and that is exactly the transition a bump makes.
        //
        // Every field is set away from its default, so a step that ran when it should not have
        // moves one of them and this goes red.
        Assert.That(migrated.Version, Is.EqualTo(3));
        Assert.That(migrated.HapticsEnabled, Is.False);
        Assert.That(migrated.SeenFirstActiveHint, Is.True);
        Assert.That(migrated.Shards, Is.EqualTo(220));
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
        IReadOnlyList<ContentId> manualSkillIds = null,
        RunEconomy economy = default,
        IReadOnlyList<ContentId> banishedNodeIds = null,
        IReadOnlyList<ContentId> pactedNodeIds = null,
        IReadOnlyList<ContentId> ordealIds = null)
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
            manualSkillIds ?? new ContentId[SkillRunner.MaxManualSlots],
            economy,
            banishedNodeIds ?? Array.Empty<ContentId>(),
            pactedNodeIds ?? Array.Empty<ContentId>(),
            ordealIds ?? Array.Empty<ContentId>());
    }
}
