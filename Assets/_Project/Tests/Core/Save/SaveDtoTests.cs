using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Save;

/// <summary>
/// What a run is, written down. These rows guard the save format's shape — its version floor, the
/// values a reader is allowed to be handed, and the fields it deliberately does not carry — because
/// every one of them becomes a migration the moment a build ships with it.
/// </summary>
[TestFixture]
public sealed class SaveDtoTests
{
    private static readonly ContentId Mode = new ContentId("mode.descent");
    private static readonly ContentId Character = new ContentId("character.oathbound");

    /// <summary>
    /// Two node ids, for the rows about the list. Not resolved against any catalog and not required
    /// to exist — rule 4's point is that this constructor checks the <em>grammar</em> and leaves
    /// "does this build still ship that node" to content validation at <c>RunSession.Start</c>.
    /// </summary>
    private static readonly ContentId Bulwark = new ContentId("skill.oathbound.bulwark");
    private static readonly ContentId Consecrate = new ContentId("skill.oathbound.consecrate");

    private static readonly DateTimeOffset Written =
        new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    [Test]
    public void Snapshot_RecordsEveryField()
    {
        var random = new RandomState(1UL, 2UL, 3UL, 4UL, 5UL);

        // Every argument distinct, so a constructor that assigned two fields from one parameter
        // could not pass — the mistake a ten-argument constructor exists to make.
        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            Mode,
            Character,
            seed: 7,
            stageIndex: 4,
            random,
            playerHp: 61f,
            playerShield: 12f,
            runTime: 138.5f,
            Written,
            level: 7,
            xp: 33.5f,
            pendingLevelUps: 1,
            takenNodeIds: Array.Empty<ContentId>());

        Assert.That(snapshot.Version, Is.EqualTo(RunSnapshot.CurrentVersion));
        Assert.That(snapshot.ModeId, Is.EqualTo(Mode));
        Assert.That(snapshot.CharacterId, Is.EqualTo(Character));
        Assert.That(snapshot.Seed, Is.EqualTo(7));
        Assert.That(snapshot.StageIndex, Is.EqualTo(4));
        Assert.That(snapshot.Random.Spawn, Is.EqualTo(1UL));
        Assert.That(snapshot.Random.Misc, Is.EqualTo(5UL));
        Assert.That(snapshot.PlayerHp, Is.EqualTo(61f));
        Assert.That(snapshot.PlayerShield, Is.EqualTo(12f));
        Assert.That(snapshot.RunTime, Is.EqualTo(138.5f));
        Assert.That(snapshot.WrittenAt, Is.EqualTo(Written));
        Assert.That(snapshot.Level, Is.EqualTo(7));
        Assert.That(snapshot.Xp, Is.EqualTo(33.5f));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(1));
    }

    [Test]
    public void Snapshot_RecordsTheFourNewFields()
    {
        var taken = new[] { Bulwark, Consecrate };

        RunSnapshot snapshot = Snapshot(level: 7, xp: 33.5f, pendingLevelUps: 1, takenNodeIds: taken);

        // Every value distinct and none of them a zero, so a constructor that assigned two fields
        // from one parameter — the mistake a fourteen-argument constructor exists to make — could
        // not pass.
        Assert.That(snapshot.Level, Is.EqualTo(7));
        Assert.That(snapshot.Xp, Is.EqualTo(33.5f));
        Assert.That(snapshot.PendingLevelUps, Is.EqualTo(1));

        Assert.That(snapshot.TakenNodeIds, Is.EqualTo(taken), "In take order, which is what the list means.");

        // Equal to the input and not the same object: the copy is what rule 5 is about.
        Assert.That(snapshot.TakenNodeIds, Is.Not.SameAs(taken));
    }

    [Test]
    public void Snapshot_LevelBelowOne_Throws()
    {
        // Every run starts at 1 and levelling only goes up, so a zero is a hand-edited file or a
        // build that counted from an array index.
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(level: 0));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(level: -1));
    }

    [Test]
    public void Snapshot_NegativeOrNonFiniteXp_Throws()
    {
        // All three spellings, for playerHp's reason: `value < 0f` would admit NaN — every
        // comparison against it is false — and `!(value >= 0f)` alone would admit +infinity
        // (AR §18.3). A NaN here is the sharper failure of the two: it makes LevelTracker's
        // `while (Xp >= XpToNext)` false for ever, so the player silently stops levelling.
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(xp: -1f));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(xp: float.NaN));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(xp: float.PositiveInfinity));
    }

    [Test]
    public void Snapshot_NegativePending_Throws()
    {
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(pendingLevelUps: -1));
    }

    [Test]
    public void Snapshot_NullNodes_Throws()
    {
        // Built directly rather than through the helper, which substitutes the empty list for a
        // parameter that was not supplied and so cannot tell "not supplied" from "supplied as
        // null" — the same reason Snapshot_DefaultContentIdIsAllowed builds its own.
        //
        // Null and empty are not two ways of saying "no nodes". A reader must never have to ask,
        // which is also why default(RunSnapshot) answers an empty list rather than a null.
        Assert.Catch<ArgumentNullException>(() => new RunSnapshot(
            RunSnapshot.CurrentVersion,
            Mode,
            Character,
            seed: 7,
            stageIndex: 1,
            default,
            playerHp: 100f,
            playerShield: 0f,
            runTime: 0f,
            Written,
            level: 1,
            xp: 0f,
            pendingLevelUps: 0,
            takenNodeIds: null));
    }

    [Test]
    public void Snapshot_DefaultNodeId_Throws()
    {
        // The valid entry first, so the row cannot pass by refusing the list wholesale.
        Assert.Catch<ArgumentException>(() => Snapshot(takenNodeIds: new[] { Bulwark, default }));
    }

    [Test]
    public void Snapshot_NodesAreCopied()
    {
        var taken = new List<ContentId> { Bulwark };

        RunSnapshot snapshot = Snapshot(takenNodeIds: taken);

        taken.Add(Consecrate);

        // A borrowed list is what cannot be used here: SaveWriter *enqueues* the write, so a
        // snapshot is held across frames and the caller's buffer would be rewritten underneath a
        // save that had not happened yet (rule 5).
        Assert.That(snapshot.TakenNodeIds.Count, Is.EqualTo(1));
        Assert.That(snapshot.TakenNodeIds[0], Is.EqualTo(Bulwark));
    }

    [Test]
    public void Snapshot_VersionBelowOne_Throws()
    {
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(version: 0));
    }

    [Test]
    public void Snapshot_StageBelowOne_Throws()
    {
        // Stages are numbered from 1, so a zero is a file that was hand-edited or written by a
        // build that counted from an array index.
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(stageIndex: 0));
    }

    [Test]
    public void Snapshot_NegativeHp_Throws()
    {
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerHp: -1f));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerShield: -1f));
    }

    [Test]
    public void Snapshot_NonFiniteHp_Throws()
    {
        // Both forms, because `value < 0f` would admit NaN — every comparison against it is
        // false — and `!(value >= 0f)` alone would admit +infinity (AR §18.3).
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerHp: float.NaN));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerHp: float.PositiveInfinity));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerShield: float.NaN));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(playerShield: float.PositiveInfinity));
    }

    [Test]
    public void Snapshot_NegativeRunTime_Throws()
    {
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(runTime: -1f));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(runTime: float.NaN));
        Assert.Catch<ArgumentOutOfRangeException>(() => Snapshot(runTime: float.PositiveInfinity));
    }

    [Test]
    public void Snapshot_DefaultHasEmptyNodesAndVersionZero()
    {
        RunSnapshot zeroed = default;

        // The whole of the both-ends problem, closed by arithmetic instead of by an IsValid
        // member: CurrentVersion starts at 1, so the form no constructor can prevent is the one
        // value no writer can produce, and every reader already refuses it.
        Assert.That(zeroed.Version, Is.EqualTo(0));
        Assert.That(RunSnapshot.CurrentVersion, Is.GreaterThan(0));

        // The one v2 field a zeroed struct could hand out as a null. It answers an empty list
        // instead, so no reader has to ask — including the readers that will not exist until M3-03.
        Assert.That(zeroed.TakenNodeIds, Is.Not.Null);
        Assert.That(zeroed.TakenNodeIds, Is.Empty);
    }

    [Test]
    public void Snapshot_DefaultContentIdIsAllowed()
    {
        // Built directly rather than through the helper, whose optional ContentId? parameters
        // cannot tell "not supplied" from "supplied as default(ContentId)".
        //
        // Not refused here, unlike in RunConfig. A save naming content this build no longer ships
        // is a migration's problem and RunSession.Start's diagnostic, not a constructor's.
        Assert.DoesNotThrow(() => new RunSnapshot(
            RunSnapshot.CurrentVersion,
            default,
            default,
            seed: 7,
            stageIndex: 1,
            default,
            playerHp: 100f,
            playerShield: 0f,
            runTime: 0f,
            Written,
            level: 1,
            xp: 0f,
            pendingLevelUps: 0,
            takenNodeIds: Array.Empty<ContentId>()));
    }

    [Test]
    public void Snapshot_RunTimeAndWrittenAtAreIndependent()
    {
        DateTimeOffset threeDaysOn = Written.AddDays(3);

        RunSnapshot snapshot = Snapshot(runTime: 5f, writtenAt: threeDaysOn);

        // Simulated seconds and wall-clock are two different numbers measuring two different
        // things, and neither is ever derived from the other: five seconds of play can sit under
        // a timestamp three days later, because a backgrounded app stops ticking and the device
        // clock does not.
        Assert.That(snapshot.RunTime, Is.EqualTo(5f));
        Assert.That(snapshot.WrittenAt, Is.EqualTo(threeDaysOn));
    }

    [Test]
    public void Profile_Default_IsCurrentVersionAndHapticsOn()
    {
        PlayerProfile profile = PlayerProfile.Default;

        Assert.That(profile.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
        Assert.That(profile.HapticsEnabled, Is.True, "GD §16.3: haptics are on until turned off.");
    }

    [Test]
    public void Profile_VersionBelowOne_Throws()
    {
        Assert.Catch<ArgumentOutOfRangeException>(() => new PlayerProfile(0, hapticsEnabled: true));
    }

    [Test]
    public void Profile_HasNoShards()
    {
        string[] properties = typeof(PlayerProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Pinned rather than remembered. ADR-0007 names Shards and unlocks, and they arrive with
        // the mechanics that own them — a field written at v1 that nothing reads is a field every
        // later migration carries for ever.
        Assert.That(properties, Is.EqualTo(new[] { "HapticsEnabled", "Version" }));
    }

    [Test]
    public void RandomState_RecordsFiveStreams()
    {
        var state = new RandomState(10UL, 20UL, 30UL, 40UL, 50UL);

        // In stream-index order — Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4 — because that
        // order is part of what a seed means and a transposed pair here would resume a run on the
        // wrong sequences without failing anything (AR §18.3).
        Assert.That(state.Spawn, Is.EqualTo(10UL));
        Assert.That(state.Offers, Is.EqualTo(20UL));
        Assert.That(state.Affixes, Is.EqualTo(30UL));
        Assert.That(state.Drops, Is.EqualTo(40UL));
        Assert.That(state.Misc, Is.EqualTo(50UL));
    }

    [Test]
    public void Store_EveryMemberIsAsync()
    {
        foreach (MethodInfo method in typeof(ISaveStore).GetMethods())
        {
            // ADR-0007: async from day one, even though the first adapter is a synchronous file
            // write. The day SyncingSaveStore wraps the local one, not one signature moves.
            Assert.That(
                typeof(Task).IsAssignableFrom(method.ReturnType),
                Is.True,
                $"ISaveStore.{method.Name} returns {method.ReturnType.Name}, not a Task.");
        }
    }

    [Test]
    public void Store_HasNoByRefParameter()
    {
        foreach (MethodInfo method in typeof(ISaveStore).GetMethods())
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                // An async method cannot have a by-ref parameter. `SaveRun(in RunSnapshot)` would
                // compile today only because the first adapter is synchronous, and would refuse
                // the first implementation that reached for async — which is the one ADR-0007
                // says is coming.
                Assert.That(
                    parameter.ParameterType.IsByRef,
                    Is.False,
                    $"ISaveStore.{method.Name}'s '{parameter.Name}' is by-ref, which no async " +
                    "implementation of this port could satisfy.");

                Assert.That(parameter.ParameterType.IsByRefLike, Is.False);
            }
        }
    }

    [Test]
    public void Dtos_AreValueTypes()
    {
        // This is what makes AR §10.3's `Task<RunSnapshot?>` legal as written: both DTOs are
        // structs, so the `?` is Nullable<T> and needs no nullable-reference switch thrown for
        // one file on the strength of two return types.
        Assert.That(typeof(RunSnapshot).IsValueType, Is.True);
        Assert.That(typeof(PlayerProfile).IsValueType, Is.True);

        Assert.That(
            typeof(ISaveStore).GetMethod(nameof(ISaveStore.LoadRun)).ReturnType,
            Is.EqualTo(typeof(Task<RunSnapshot?>)));

        Assert.That(
            typeof(ISaveStore).GetMethod(nameof(ISaveStore.LoadProfile)).ReturnType,
            Is.EqualTo(typeof(Task<PlayerProfile?>)));
    }

    [Test]
    public void Random_StreamHasNoSettableState()
    {
        MemberInfo[] members = typeof(IRandomStream).GetMembers();

        // Four draw methods and nothing else. A settable position here would be reachable from
        // every core system that holds a stream, so any behaviour could rewind the sequence it
        // draws from — the position lives on IRandom, which composition holds.
        Assert.That(
            members.Select(member => member.Name).OrderBy(name => name, StringComparer.Ordinal),
            Is.EqualTo(new[] { "Chance", "NextFloat", "NextInt", "Range" }));

        Assert.That(typeof(IRandomStream).GetProperty("State"), Is.Null);
    }

    /// <summary>
    /// A valid snapshot with one value swapped, so each guard row says only what it is about.
    /// </summary>
    /// <remarks>
    /// <paramref name="takenNodeIds"/> defaults to <c>null</c> meaning <em>not supplied</em>, and
    /// the empty list is substituted below — so <c>Snapshot_NullNodes_Throws</c> cannot go through
    /// this helper and does not: it passes the null to the constructor itself, the way
    /// <c>Snapshot_DefaultContentIdIsAllowed</c> already has to for its own <c>ContentId?</c>
    /// reason.
    /// </remarks>
    private static RunSnapshot Snapshot(
        int version = RunSnapshot.CurrentVersion,
        int seed = 7,
        int stageIndex = 1,
        float playerHp = 100f,
        float playerShield = 0f,
        float runTime = 0f,
        DateTimeOffset? writtenAt = null,
        int level = 1,
        float xp = 0f,
        int pendingLevelUps = 0,
        IReadOnlyList<ContentId> takenNodeIds = null)
    {
        return new RunSnapshot(
            version,
            Mode,
            Character,
            seed,
            stageIndex,
            default,
            playerHp,
            playerShield,
            runTime,
            writtenAt ?? Written,
            level,
            xp,
            pendingLevelUps,
            takenNodeIds ?? Array.Empty<ContentId>());
    }
}
