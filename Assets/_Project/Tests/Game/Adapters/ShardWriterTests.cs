using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The one thing in the app that banks a payout: what it adds, what it refuses to hear, what it
/// must not erase on the way past, and what it does when the disk says no (M4-05b rules 2, 5, 6, 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>The store here answers with already-completed tasks</b>, which is what makes these rows
/// synchronous: <c>ProfileStore.Save</c> attaches its fault continuation with
/// <c>ExecuteSynchronously</c> on the default scheduler, so with a synchronous store the whole
/// write — and its error log — happens before <c>Save</c> returns. An EditMode test never pumps
/// Unity's synchronisation context, so a store that deferred would make every row here a lie.
/// </para>
/// <para>
/// <b>The fake is local rather than borrowed from <c>Soulvail.Tests.Core</c>'s shelf</b>, which is
/// <c>SaveWriterTests</c>' reason: this assembly does not reference that one, deliberately, and the
/// store these rows need — one that faults the <em>next</em> profile write and counts the rest — is
/// a shape <c>InMemorySaveStore</c> does not have on this side of the line.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ShardWriterTests
{
    private DomainEventHub _hub;
    private RecordingStore _store;
    private ProfileStore _profiles;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _store = new RecordingStore();
        _profiles = new ProfileStore(_store);
    }

    [TearDown]
    public void TearDown()
    {
        _hub.Dispose();
    }

    [Test]
    public void Construct_NullProfiles_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ShardWriter(null, _hub));
    }

    [Test]
    public void Construct_NullHub_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ShardWriter(_profiles, null));
    }

    [Test]
    public void Writer_BanksThePayout()
    {
        Adopt(shards: 100);

        using var writer = new ShardWriter(_profiles, _hub);

        _hub.Publish(new ShardsAwarded(total: 220, deepestStage: 12, bossesKilled: 2));

        // Added to the lifetime total, not written over it (rule 5): the profile holds what every
        // run this install ever finished was worth, and the event carries one run's.
        Assert.That(_profiles.Current.Shards, Is.EqualTo(320));

        // And it reached the disk. A writer that moved `Current` and never saved would look right
        // for the rest of the session and lose the payout to the next app kill — which is the one
        // thing this whole task exists to stop.
        Assert.That(_store.ProfileWrites, Is.EqualTo(1), "one call, not one per term.");
        Assert.That(Written().Shards, Is.EqualTo(320));
    }

    [Test]
    public void Writer_AddsRatherThanAssigns()
    {
        Adopt(shards: 100);

        using var writer = new ShardWriter(_profiles, _hub);

        _hub.Publish(new ShardsAwarded(total: 40, deepestStage: 4, bossesKilled: 0));
        _hub.Publish(new ShardsAwarded(total: 40, deepestStage: 4, bossesKilled: 0));

        // **Two identical awards, which is the pair an assignment cannot be told apart from by one
        // of them.** `WithShards(evt.Total)` would leave 40 here and would leave 40 after the first
        // award too — so a single-award row would pass against the bug and this one cannot.
        Assert.That(_profiles.Current.Shards, Is.EqualTo(180));
        Assert.That(Written().Shards, Is.EqualTo(180));
        Assert.That(_store.ProfileWrites, Is.EqualTo(2));
    }

    [Test]
    public void Writer_IgnoresRunEnded()
    {
        Adopt(shards: 100);

        using var writer = new ShardWriter(_profiles, _hub);

        // Rule 5's refusal, and `SaveWriter.Writer_IgnoresRunEnded`'s shape. RunEnded is also
        // published when RunScope is torn down, which is every ordinary exit from the Run scene —
        // so a payout riding it would pay a player for quitting to the menu, and again on every
        // teardown after that. PlayerDied is refused for a different reason: it carries no number.
        _hub.Publish(new RunEnded(120f));
        _hub.Publish(new PlayerDied(120f));

        Assert.That(_profiles.Current.Shards, Is.EqualTo(100), "a run that ends any other way pays nothing.");
        Assert.That(_store.ProfileWrites, Is.Zero, "and nothing was written at all.");
    }

    [Test]
    public void Writer_KeepsTheOtherFields()
    {
        // Haptics off and the hint seen, both away from the values a fresh profile carries — so a
        // writer that authored the whole DTO from the one field it knows about moves both, and this
        // row names which (rule 2). It is the bug M3-09c existed to stop, checked from the new door.
        _profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion,
            hapticsEnabled: false,
            seenFirstActiveHint: true,
            shards: 0));

        using var writer = new ShardWriter(_profiles, _hub);

        _hub.Publish(new ShardsAwarded(total: 10, deepestStage: 1, bossesKilled: 0));

        PlayerProfile written = Written();

        Assert.That(written.Shards, Is.EqualTo(10));
        Assert.That(written.HapticsEnabled, Is.False, "the player's choice survived the payout.");
        Assert.That(written.SeenFirstActiveHint, Is.True, "and so did what the game had noticed.");
    }

    [Test]
    public void Writer_SurvivesAFailedWrite()
    {
        Adopt(shards: 100);

        _store.FailNextProfileWrite();

        // Rule 7: the fault is logged by ProfileStore.Save and swallowed there, and this class adds
        // no second policy. The worst consequence available is a player losing one run's Shards to
        // a full disk, which must not throw out of an event callback on the frame they died.
        LogAssert.Expect(LogType.Error, new Regex("Could not save the player profile"));

        using var writer = new ShardWriter(_profiles, _hub);

        Assert.DoesNotThrow(
            () => _hub.Publish(new ShardsAwarded(total: 220, deepestStage: 12, bossesKilled: 2)));

        // And `Current` stays moved, for the reason ProfileStore gives: the payout the player has
        // already been shown has already been earned, and a store that rolled back to match the
        // disk would disagree with them about what happened in front of them.
        Assert.That(_profiles.Current.Shards, Is.EqualTo(320));
    }

    [Test]
    public void Writer_StopsAtDispose()
    {
        Adopt(shards: 100);

        var writer = new ShardWriter(_profiles, _hub);

        writer.Dispose();

        _hub.Publish(new ShardsAwarded(total: 220, deepestStage: 12, bossesKilled: 2));

        // Rule 6: the writer is scoped to the run, so disposing the scope takes the subscription
        // with it. A subscription that outlived its scope would bank the next run's payout twice —
        // once through the old writer and once through the new one.
        Assert.That(_profiles.Current.Shards, Is.EqualTo(100));
        Assert.That(_store.ProfileWrites, Is.Zero);

        // And disposing twice is a no-op, which is what a scope teardown after an explicit dispose
        // needs it to be.
        Assert.DoesNotThrow(writer.Dispose);
    }

    /// <summary>A profile at <paramref name="shards"/>, adopted without being written back.</summary>
    private void Adopt(int shards)
    {
        _profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion,
            hapticsEnabled: true,
            seenFirstActiveHint: false,
            shards: shards));
    }

    /// <summary>The profile as it stands on the fake's "disk".</summary>
    private PlayerProfile Written()
    {
        Assert.That(_store.Saved.HasValue, Is.True, "Nothing was ever written.");

        return _store.Saved.Value;
    }

    /// <summary>
    /// An <see cref="ISaveStore"/> that remembers the last profile it was handed, counts the
    /// handings, and can be told to fault the next one.
    /// </summary>
    /// <remarks>
    /// The run half throws rather than answering: nothing in this fixture touches a run, and a
    /// silent no-op would let a future row here pass while testing nothing.
    /// </remarks>
    private sealed class RecordingStore : ISaveStore
    {
        private bool _failNextProfileWrite;

        public PlayerProfile? Saved { get; private set; }

        public int ProfileWrites { get; private set; }

        public void FailNextProfileWrite()
        {
            _failNextProfileWrite = true;
        }

        public Task<PlayerProfile?> LoadProfile()
        {
            return Task.FromResult(Saved);
        }

        public Task SaveProfile(PlayerProfile profile)
        {
            ProfileWrites++;

            if (_failNextProfileWrite)
            {
                _failNextProfileWrite = false;

                return Task.FromException(new IOException("There is not enough space on the disk."));
            }

            Saved = profile;

            return Task.CompletedTask;
        }

        public Task<RunSnapshot?> LoadRun() => throw new NotSupportedException();

        public Task SaveRun(RunSnapshot run) => throw new NotSupportedException();

        public Task ClearRun() => throw new NotSupportedException();
    }
}
