using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// M2-14a and M2-14b both assert what a run writes and when, and both say it through this fake —
/// so an unverified one would make every claim resting on it vacuous. The <c>FixedRandomTests</c>
/// and <c>FixedClockTests</c> precedent.
/// </summary>
[TestFixture]
public sealed class InMemorySaveStoreTests
{
    private static readonly DateTimeOffset Written =
        new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);

    private InMemorySaveStore _store;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemorySaveStore();
    }

    [Test]
    public async Task Fake_LoadBeforeSave_IsNull()
    {
        // Null is "no save", and it is an answer rather than a failure: the first launch on a
        // device reaches this path, and so does every launch after a run ends.
        Assert.That(await _store.LoadRun(), Is.Null);
        Assert.That(await _store.LoadProfile(), Is.Null);
    }

    [Test]
    public async Task Fake_RoundTripsARun()
    {
        RunSnapshot saved = Snapshot(stageIndex: 4, playerHp: 61f, runTime: 138.5f);

        await _store.SaveRun(saved);
        RunSnapshot? loaded = await _store.LoadRun();

        Assert.That(loaded.HasValue, Is.True);
        Assert.That(loaded.Value.Version, Is.EqualTo(saved.Version));
        Assert.That(loaded.Value.ModeId, Is.EqualTo(saved.ModeId));
        Assert.That(loaded.Value.CharacterId, Is.EqualTo(saved.CharacterId));
        Assert.That(loaded.Value.Seed, Is.EqualTo(saved.Seed));
        Assert.That(loaded.Value.StageIndex, Is.EqualTo(saved.StageIndex));
        Assert.That(loaded.Value.Random.Spawn, Is.EqualTo(saved.Random.Spawn));
        Assert.That(loaded.Value.Random.Misc, Is.EqualTo(saved.Random.Misc));
        Assert.That(loaded.Value.PlayerHp, Is.EqualTo(saved.PlayerHp));
        Assert.That(loaded.Value.PlayerShield, Is.EqualTo(saved.PlayerShield));
        Assert.That(loaded.Value.RunTime, Is.EqualTo(saved.RunTime));
        Assert.That(loaded.Value.WrittenAt, Is.EqualTo(saved.WrittenAt));
    }

    [Test]
    public async Task Fake_SaveReplaces()
    {
        await _store.SaveRun(Snapshot(stageIndex: 2));
        await _store.SaveRun(Snapshot(stageIndex: 5));

        RunSnapshot? loaded = await _store.LoadRun();

        // One run is kept, never a history: a resume offers the run that was interrupted and
        // there is no second one to choose between.
        Assert.That(loaded.Value.StageIndex, Is.EqualTo(5));
    }

    [Test]
    public async Task Fake_ClearRemovesTheRunNotTheProfile()
    {
        await _store.SaveRun(Snapshot());
        await _store.SaveProfile(new PlayerProfile(PlayerProfile.CurrentVersion, hapticsEnabled: false));

        await _store.ClearRun();

        Assert.That(await _store.LoadRun(), Is.Null);

        PlayerProfile? profile = await _store.LoadProfile();
        Assert.That(profile.HasValue, Is.True, "A cleared run must not take the profile with it.");
        Assert.That(profile.Value.HapticsEnabled, Is.False);
        Assert.That(_store.ClearCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Fake_CountsWrites()
    {
        await _store.SaveRun(Snapshot());
        await _store.SaveRun(Snapshot());
        await _store.SaveRun(Snapshot());
        await _store.SaveProfile(PlayerProfile.Default);

        // The interesting assertion about saving is how many times: a run that wrote a snapshot
        // per frame instead of per stage boundary would round-trip correctly and still be wrong.
        Assert.That(_store.RunWriteCount, Is.EqualTo(3));
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1));
        Assert.That(_store.ClearCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Fake_FailNextWrite_FaultsTheTask()
    {
        _store.FailNextWrite();

        // The call returns normally — this is the line that would throw if the fake got it wrong,
        // and a caller that only wrapped the invocation in a try would then pass while still
        // crashing on a device where the disk is full.
        Task write = _store.SaveRun(Snapshot(stageIndex: 3));

        Assert.That(write.IsFaulted, Is.True);
        Assert.CatchAsync<IOException>(async () => await write);

        // Nothing was stored, and the arming is spent.
        Assert.That(await _store.LoadRun(), Is.Null);

        await _store.SaveRun(Snapshot(stageIndex: 3));
        RunSnapshot? loaded = await _store.LoadRun();

        Assert.That(loaded.Value.StageIndex, Is.EqualTo(3));
        Assert.That(_store.RunWriteCount, Is.EqualTo(2), "A failed write is still a call.");
    }

    private static RunSnapshot Snapshot(
        int stageIndex = 1,
        float playerHp = 100f,
        float runTime = 0f)
    {
        return new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId("mode.descent"),
            new ContentId("character.oathbound"),
            seed: 7,
            stageIndex,
            new RandomState(1UL, 2UL, 3UL, 4UL, 5UL),
            playerHp,
            playerShield: 0f,
            runTime,
            Written);
    }
}
