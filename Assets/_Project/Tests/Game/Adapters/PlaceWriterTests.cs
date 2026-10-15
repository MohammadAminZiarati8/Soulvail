using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// UI-11d rules 1 to 3: the profile hears where a run got to and whether it finished the place,
/// and writes once.
/// </summary>
/// <remarks>
/// The store here is a local recording fake, <c>ShardWriterTests</c>' reason: its answers are
/// already-completed tasks, so the whole write happens before <c>Save</c> returns.
/// </remarks>
[TestFixture]
public sealed class PlaceWriterTests
{
    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Descent = new ContentId("mode.descent");

    private DomainEventHub _hub;
    private RecordingStore _store;
    private ProfileStore _profiles;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _store = new RecordingStore();
        _profiles = new ProfileStore(_store);
        _profiles.Adopt(PlayerProfile.Default);
    }

    [TearDown]
    public void TearDown()
    {
        _hub.Dispose();
    }

    [Test]
    public void ShardsAwarded_RaisesBest()
    {
        using var writer = new PlaceWriter(_profiles, _hub);

        EndRunAt(Jungle, 12);

        Assert.That(_profiles.Current.BestStage(Jungle), Is.EqualTo(12));
        Assert.That(_profiles.Current.FinishedModeIds, Is.Empty, "a death finishes nothing.");
    }

    [Test]
    public void ShardsAwarded_Lower_KeepsBest()
    {
        _profiles.Adopt(PlayerProfile.Default.WithBestStage(Jungle, 20));

        using var writer = new PlaceWriter(_profiles, _hub);

        EndRunAt(Jungle, 7);

        Assert.That(_profiles.Current.BestStage(Jungle), Is.EqualTo(20));
    }

    [Test]
    public void ModeFinished_AddsFinished_AndBest()
    {
        using var writer = new PlaceWriter(_profiles, _hub);

        FinishRun(Jungle, 30);

        Assert.That(_profiles.Current.FinishedModeIds, Is.EqualTo(new[] { Jungle }));
        Assert.That(_profiles.Current.BestStage(Jungle), Is.EqualTo(30));
    }

    [Test]
    public void FinishTick_SavesOnce()
    {
        using var writer = new PlaceWriter(_profiles, _hub);

        FinishRun(Jungle, 30);

        Assert.That(_store.ProfileWrites, Is.EqualTo(1), "two events, one save.");
    }

    [Test]
    public void TeardownRunEnded_WritesNothing()
    {
        using var writer = new PlaceWriter(_profiles, _hub);

        _hub.Publish(new RunEnded(10f));

        Assert.That(_store.ProfileWrites, Is.Zero, "a run left through the menu is not an outcome.");
    }

    [Test]
    public void SecondRunEnded_AfterADeath_WritesNothingMore()
    {
        using var writer = new PlaceWriter(_profiles, _hub);

        EndRunAt(Descent, 5);
        _hub.Publish(new RunEnded(10f));

        Assert.That(_store.ProfileWrites, Is.EqualTo(1), "the scope's own teardown event is not a second outcome.");
    }

    [Test]
    public void Writer_TouchesNothingElse()
    {
        _profiles.Adopt(PlayerProfile.Default.WithShards(77).WithHaptics(false));

        using var writer = new PlaceWriter(_profiles, _hub);

        FinishRun(Jungle, 30);

        Assert.That(_profiles.Current.Shards, Is.EqualTo(77));
        Assert.That(_profiles.Current.HapticsEnabled, Is.False);
    }

    [Test]
    public void Writer_StopsAtDispose()
    {
        var writer = new PlaceWriter(_profiles, _hub);

        writer.Dispose();

        FinishRun(Jungle, 30);

        Assert.That(_store.ProfileWrites, Is.Zero);
    }

    private void EndRunAt(ContentId mode, int stage)
    {
        _hub.Publish(new ShardsAwarded(10, stage, 0, null, mode));
        _hub.Publish(new RunEnded(10f));
    }

    private void FinishRun(ContentId mode, int finalStage)
    {
        _hub.Publish(new ShardsAwarded(10, finalStage, finalStage / 5, null, mode));
        _hub.Publish(new ModeFinished(mode, finalStage));
        _hub.Publish(new RunEnded(10f));
    }

    private sealed class RecordingStore : ISaveStore
    {
        public int ProfileWrites { get; private set; }

        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile)
        {
            ProfileWrites++;

            return Task.CompletedTask;
        }

        public Task<RunSnapshot?> LoadRun() => throw new NotSupportedException();

        public Task SaveRun(RunSnapshot run) => throw new NotSupportedException();

        public Task ClearRun() => throw new NotSupportedException();
    }
}
