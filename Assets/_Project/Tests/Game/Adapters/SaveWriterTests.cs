using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The one thing in the app that awaits <see cref="ISaveStore"/> for a run: what it writes, what it
/// deletes, and — the half that matters on a device — what it does when the disk says no.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every store here answers with an already-completed task, and that is what makes these rows
/// synchronous.</b> <c>SaveWriter</c> chains with <c>ExecuteSynchronously</c> on the default
/// scheduler, so a link whose antecedent is already finished runs inline rather than being posted
/// to Unity's synchronisation context — which in an EditMode test would never be pumped. The one
/// row that needs a write to hang says so by holding a <see cref="TaskCompletionSource{TResult}"/>
/// open, and completing it from the test thread runs the continuation on the test thread.
/// </para>
/// <para>
/// The fakes are local rather than borrowed from <c>Soulvail.Tests.Core</c>'s shelf: this assembly
/// does not reference that one, deliberately, and the stores these rows need — one that hangs, one
/// that faults a clear — are shapes <c>InMemorySaveStore</c> does not have.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SaveWriterTests
{
    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 12, 21, 30, 0, TimeSpan.Zero);

    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        _hub.Dispose();
    }

    [Test]
    public void Writer_SavesWhatCoreAnnounced()
    {
        var store = new RecordingStore();

        using var writer = new SaveWriter(store, _hub);

        RunSnapshot snapshot = Snapshot(stage: 4, hp: 84f);

        _hub.Publish(new RunSnapshotTaken(snapshot));

        Assert.That(store.Saved, Has.Count.EqualTo(1));

        // The argument, not merely a snapshot: an adapter that wrote the last one it happened to be
        // holding would round-trip perfectly and still lose a stage.
        Assert.That(store.Saved[0].StageIndex, Is.EqualTo(4));
        Assert.That(store.Saved[0].PlayerHp, Is.EqualTo(84f));
        Assert.That(store.Saved[0].Seed, Is.EqualTo(snapshot.Seed));
        Assert.That(store.Saved[0].WrittenAt, Is.EqualTo(snapshot.WrittenAt));

        Assert.That(store.Cleared, Is.Zero);
        Assert.That(writer.IsWriting, Is.False, "A completed write is not in flight.");
    }

    [Test]
    public void Writer_OpeningWriteReplacesAStaleRun()
    {
        // Rule 2, and the whole reason a run writes itself down on its first frame. Without the
        // opening write, the file on disk goes on describing an abandoned stage-12 run for the
        // forty to seventy-five seconds it takes the new run to reach its own first boundary — and
        // a player who loses the phone in that window resumes into somebody else's run.
        var store = new RecordingStore();

        store.Preload(Snapshot(stage: 12, hp: 40f));

        using var writer = new SaveWriter(store, _hub);

        _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 1, hp: 100f)));

        RunSnapshot? loaded = store.LoadRun().Result;

        Assert.That(loaded.HasValue, Is.True);
        Assert.That(
            loaded.Value.StageIndex,
            Is.EqualTo(1),
            "The file must describe the run in progress at all times, not the one before it.");
    }

    [Test]
    public void Writer_ClearsOnPlayerDied()
    {
        var store = new RecordingStore();

        store.Preload(Snapshot(stage: 5, hp: 10f));

        using var writer = new SaveWriter(store, _hub);

        _hub.Publish(new PlayerDied(120f));

        Assert.That(store.Cleared, Is.EqualTo(1));
        Assert.That(store.Saved, Is.Empty, "A death writes nothing; it forgets.");
        Assert.That(store.LoadRun().Result.HasValue, Is.False);
    }

    [Test]
    public void Writer_IgnoresRunEnded()
    {
        // Rule 9's whole point, pinned. RunEnded is also published when RunScope is disposed, which
        // is every ordinary exit from the Run scene — so a writer listening for it would start
        // deleting saved runs the day M8-02 adds a "quit to menu" button, and the symptom would be
        // a resume that quietly stopped working.
        var store = new RecordingStore();

        store.Preload(Snapshot(stage: 5, hp: 10f));

        using var writer = new SaveWriter(store, _hub);

        _hub.Publish(new RunEnded(120f));

        Assert.That(store.Cleared, Is.Zero);
        Assert.That(store.LoadRun().Result.HasValue, Is.True, "The run is still resumable.");
    }

    [Test]
    public void Writer_FailedSaveDoesNotThrow()
    {
        LogAssert.Expect(LogType.Error, new Regex("Could not save the run"));

        var store = new RecordingStore();

        store.FailNextWrite();

        using var writer = new SaveWriter(store, _hub);

        // A full disk, a revoked permission, a volume that vanished. None of them may end a run
        // that is otherwise perfectly playable (rule 8).
        Assert.That(() => _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 2, hp: 50f))), Throws.Nothing);

        // And the writer is still a writer afterwards: one failure, not a broken object.
        _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 3, hp: 50f)));

        Assert.That(store.Saved, Has.Count.EqualTo(1), "The second write went through.");
        Assert.That(store.Saved[0].StageIndex, Is.EqualTo(3));
    }

    [Test]
    public void Writer_FailedClearDoesNotThrow()
    {
        LogAssert.Expect(LogType.Error, new Regex("Could not clear the run"));

        var store = new RecordingStore();

        store.FailNextWrite();

        using var writer = new SaveWriter(store, _hub);

        Assert.That(() => _hub.Publish(new PlayerDied(30f)), Throws.Nothing);
    }

    [Test]
    public void Writer_ThrowingStoreDoesNotThrow()
    {
        // The other half of rule 8. A store may fail at the call rather than at the await — a guard
        // that throws immediately does — and a writer that only handled faulted tasks would let
        // that one straight into the frame.
        LogAssert.Expect(LogType.Error, new Regex("Could not save the run"));

        using var writer = new SaveWriter(new ThrowingStore(), _hub);

        Assert.That(() => _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 2, hp: 50f))), Throws.Nothing);
    }

    [Test]
    public void Writer_ObservesEveryFault()
    {
        // Rule 11. A task that faults with nobody awaiting it surfaces later as an
        // unobserved-exception log with no stack that points anywhere useful — the worst possible
        // shape for a bug that only reproduces on a device.
        LogAssert.Expect(LogType.Error, new Regex("Could not save the run"));

        var unobserved = new List<Exception>();

        void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs args) =>
            unobserved.Add(args.Exception);

        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            var store = new RecordingStore();
            store.FailNextWrite();

            using (var writer = new SaveWriter(store, _hub))
            {
                _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 2, hp: 50f)));
            }

            // Twice: the first pass finalises the faulted tasks, the second collects what the
            // finalisers resurrected, which is when the event would be raised.
            for (int i = 0; i < 2; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.That(unobserved, Is.Empty, "A fault reached the finaliser instead of the log.");
    }

    [Test]
    public void Writer_SerialisesTwoSnapshots()
    {
        var store = new HangingStore();

        using var writer = new SaveWriter(store, _hub);

        _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 2, hp: 90f)));
        _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 3, hp: 80f)));

        Assert.That(store.Started, Has.Count.EqualTo(1), "The second write must wait for the first.");
        Assert.That(store.Started[0], Is.EqualTo(2));
        Assert.That(writer.IsWriting, Is.True);

        store.CompleteFirst();

        Assert.That(store.Started, Has.Count.EqualTo(2), "And then it goes, in order.");
        Assert.That(store.Started[1], Is.EqualTo(3));
    }

    [Test]
    public void Writer_DisposeDropsSubscriptions()
    {
        var store = new RecordingStore();

        store.Preload(Snapshot(stage: 5, hp: 10f));

        var writer = new SaveWriter(store, _hub);

        writer.Dispose();

        Assert.That(() => _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 6, hp: 10f))), Throws.Nothing);
        Assert.That(() => _hub.Publish(new PlayerDied(30f)), Throws.Nothing);

        Assert.That(store.Saved, Is.Empty);
        Assert.That(store.Cleared, Is.Zero);

        // Twice, because a scope can be torn down more than once and the second one must be silent.
        Assert.That(() => writer.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Writer_DisposeDoesNotAwait()
    {
        var store = new HangingStore();

        var writer = new SaveWriter(store, _hub);

        _hub.Publish(new RunSnapshotTaken(Snapshot(stage: 2, hp: 90f)));

        Assert.That(writer.IsWriting, Is.True, "The fixture failed to leave a write in flight.");

        // An abandoned write either completed or did not, and M2-13b's atomic move means the file
        // is never half of either — so there is nothing a wait could protect, and waiting would
        // block a scene change on I/O.
        Assert.That(() => writer.Dispose(), Throws.Nothing);

        store.CompleteFirst();
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SaveWriter(null, _hub));
        Assert.Throws<ArgumentNullException>(() => new SaveWriter(new RecordingStore(), null));
    }

    private static RunSnapshot Snapshot(int stage, float hp) => new RunSnapshot(
        RunSnapshot.CurrentVersion,
        new ContentId("mode.descent"),
        new ContentId("character.oathbound"),
        seed: 4_242,
        stageIndex: stage,
        random: new RandomState(1, 2, 3, 4, 5),
        playerHp: hp,
        playerShield: 12f,
        runTime: 190.5f,
        writtenAt: Instant);

    /// <summary>A store that remembers what it was asked to do, and can be told to fail once.</summary>
    private sealed class RecordingStore : ISaveStore
    {
        private readonly List<RunSnapshot> _saved = new();

        private RunSnapshot? _run;
        private bool _failNextWrite;

        public IReadOnlyList<RunSnapshot> Saved => _saved;

        public int Cleared { get; private set; }

        /// <summary>
        /// Faults the next write's <em>task</em>, the way real I/O fails — never throwing from the
        /// call, which a caller could pass with a <c>try</c> and still crash on a device.
        /// </summary>
        public void FailNextWrite() => _failNextWrite = true;

        /// <summary>Puts a run in the store without it having been written through this object.</summary>
        public void Preload(RunSnapshot run) => _run = run;

        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun() => Task.FromResult(_run);

        public Task SaveRun(RunSnapshot run)
        {
            if (TakeArmedFailure(out Task faulted))
            {
                return faulted;
            }

            _saved.Add(run);
            _run = run;

            return Task.CompletedTask;
        }

        public Task ClearRun()
        {
            if (TakeArmedFailure(out Task faulted))
            {
                return faulted;
            }

            Cleared++;
            _run = null;

            return Task.CompletedTask;
        }

        private bool TakeArmedFailure(out Task faulted)
        {
            if (!_failNextWrite)
            {
                faulted = null;
                return false;
            }

            _failNextWrite = false;
            faulted = Task.FromException(new IOException("The fixture was told to fail this write."));

            return true;
        }
    }

    /// <summary>A store whose first write never finishes until the fixture says so.</summary>
    private sealed class HangingStore : ISaveStore
    {
        private readonly List<int> _started = new();
        private readonly TaskCompletionSource<bool> _first = new();

        /// <summary>The stage index of every write that has actually begun, in order.</summary>
        public IReadOnlyList<int> Started => _started;

        public void CompleteFirst() => _first.SetResult(true);

        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun() => Task.FromResult<RunSnapshot?>(null);

        public Task SaveRun(RunSnapshot run)
        {
            _started.Add(run.StageIndex);

            return _started.Count == 1 ? _first.Task : Task.CompletedTask;
        }

        public Task ClearRun() => Task.CompletedTask;
    }

    /// <summary>A store that throws from the call rather than faulting the task it returns.</summary>
    private sealed class ThrowingStore : ISaveStore
    {
        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun() => Task.FromResult<RunSnapshot?>(null);

        public Task SaveRun(RunSnapshot run) =>
            throw new InvalidOperationException("This store refuses at the call.");

        public Task ClearRun() => Task.CompletedTask;
    }
}
