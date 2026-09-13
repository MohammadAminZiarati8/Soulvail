using System;
using System.Threading;
using System.Threading.Tasks;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The one thing in the app that awaits <see cref="ISaveStore"/> for a run. It hears core say that
/// a snapshot was taken and writes it; it hears that the player died and forgets it; and a write
/// that fails never reaches the frame that caused it. AR §10.3, GD §7.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failed write is logged and swallowed, deliberately.</b> A full disk, a revoked permission
/// or a storage volume that vanished mid-write must not end the player's run — the run is still
/// perfectly playable, it just will not survive being killed. Rejected: surfacing it to the player,
/// which needs a dialog and a <c>LocKey</c>, and M6-10 owns the first of those. The
/// <c>Debug.LogError</c> is what tells a bug report apart from <em>"the game forgot my run"</em>.
/// </para>
/// <para>
/// <b>It subscribes from its constructor, never from a <c>Start</c> of its own</b> (AR §18.1).
/// The opening snapshot is published from inside <c>RunSession.Start</c>, which <c>RunTicker</c>
/// calls from <em>its</em> <c>Start</c> — and VContainer orders no two entry points' starts. Being
/// on <c>RunTicker</c>'s dependency chain is what guarantees this object is listening before the
/// first snapshot is taken, exactly as it guarantees it for <c>EnemyViews</c> and
/// <c>ProjectileViews</c>.
/// </para>
/// <para>
/// <b>Writes are serialised onto one chain.</b> A local file write is sub-millisecond and the two
/// write points are a stage apart, so today they cannot overlap; they are chained regardless,
/// because ADR-0007's <c>SyncingSaveStore</c> is a network write over this same port, and the day
/// two of them overlap is the day one stage's snapshot lands after the next one's.
/// </para>
/// </remarks>
public sealed class SaveWriter : IDisposable
{
    private readonly ISaveStore _store;

    private readonly IDisposable _snapshotSubscription;
    private readonly IDisposable _diedSubscription;

    /// <summary>
    /// The tail of the serialised chain. Never faulted: every link catches its own operation's
    /// failure and completes normally, which is what rule 11 rests on — a task that faults with
    /// nobody awaiting it surfaces later as an unobserved-exception log with no stack that points
    /// anywhere useful, and that is the worst possible shape for a bug that only reproduces on a
    /// device.
    /// </summary>
    private Task _chain = Task.CompletedTask;

    /// <summary>Operations queued and not yet finished. See <see cref="IsWriting"/>.</summary>
    private int _pending;

    private bool _disposed;

    /// <param name="store">Where a run is written, replaced and forgotten.</param>
    /// <param name="hub">
    /// The run's event hub. The concrete type rather than <c>IDomainEvents</c>, because this
    /// subscribes: core publishes through the port, the Unity side listens through the hub — the
    /// split <c>RunInstaller</c> registers both names for.
    /// </param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public SaveWriter(ISaveStore store, DomainEventHub hub)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        _snapshotSubscription = hub.Subscribe<RunSnapshotTaken>(OnSnapshotTaken);

        // PlayerDied and not RunEnded, and the difference matters more than it looks. RunEnded is
        // also published when RunScope is disposed, which is every ordinary exit from the Run
        // scene — so the first "quit to menu" button (M8-02) would silently start deleting saved
        // runs the day it landed, and the symptom would be a resume that stopped working for
        // reasons nobody would connect to a button.
        _diedSubscription = hub.Subscribe<PlayerDied>(OnPlayerDied);
    }

    /// <summary>
    /// Whether a write or a clear is queued or in flight. For tests and the debug overlay; nothing
    /// gates on it, and nothing may — a run that waited on its own save would stutter at exactly
    /// the boundary this class exists to be invisible at.
    /// </summary>
    public bool IsWriting => Volatile.Read(ref _pending) > 0;

    /// <summary>
    /// Drops both subscriptions and returns. <b>It does not await.</b>
    /// </summary>
    /// <remarks>
    /// An abandoned write either completed or did not, and M2-13b's atomic move means the file on
    /// disk is never half of either — so there is nothing a wait could protect. Waiting, on the
    /// other hand, would block scope teardown on I/O during a scene change, which is the one moment
    /// a frame cannot be spared.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _snapshotSubscription?.Dispose();
        _diedSubscription?.Dispose();
    }

    private void OnSnapshotTaken(RunSnapshotTaken evt)
    {
        RunSnapshot snapshot = evt.Snapshot;

        Enqueue(() => _store.SaveRun(snapshot), $"save the run at stage {snapshot.StageIndex}");
    }

    /// <remarks>
    /// AR §10.3's <em>"deleted on death"</em>. A dead run is not resumable, and a file that
    /// outlived it would offer the player a run they have already lost.
    /// </remarks>
    private void OnPlayerDied(PlayerDied evt)
    {
        Enqueue(() => _store.ClearRun(), "clear the run after a death");
    }

    /// <summary>
    /// Puts one store operation on the end of the chain, to start when whatever is ahead of it has
    /// finished.
    /// </summary>
    /// <remarks>
    /// <see cref="TaskContinuationOptions.ExecuteSynchronously"/> with
    /// <see cref="TaskScheduler.Default"/> rather than the ambient scheduler: a link whose
    /// antecedent is already complete runs inline instead of being posted to Unity's
    /// synchronisation context and waiting for the next player-loop update. With the local adapter
    /// — whose tasks are all already completed — that makes the whole chain synchronous, which is
    /// both the fastest thing it could be and the reason this class is testable without pumping a
    /// frame.
    /// </remarks>
    private void Enqueue(Func<Task> operation, string description)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Increment(ref _pending);

        _chain = _chain.ContinueWith(
                _ => Run(operation, description),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            .Unwrap();
    }

    /// <summary>
    /// Runs one operation and returns a task that completes when it has, however it ended.
    /// </summary>
    /// <remarks>
    /// The <c>try</c> covers a store that throws <em>from the call</em> rather than faulting the
    /// task it returns. Both are real: a null-argument guard throws immediately, while a disk that
    /// is full arrives at the await. Neither may reach the frame.
    /// </remarks>
    private Task Run(Func<Task> operation, string description)
    {
        Task task;

        try
        {
            task = operation() ?? Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Finish(description, exception);

            return Task.CompletedTask;
        }

        return task.ContinueWith(
            completed => Finish(description, completed.Exception),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Closes one operation out: the fault is read — which is what observes it — and logged once,
    /// naming what was being attempted.
    /// </summary>
    /// <remarks>
    /// Reading <c>completed.Exception</c> at the call site is the observation; doing it in one
    /// place is what makes rule 11 checkable rather than a habit. This method never throws, so the
    /// chain it is on never faults and the link behind it has nothing to observe in turn.
    /// </remarks>
    private void Finish(string description, Exception error)
    {
        Interlocked.Decrement(ref _pending);

        if (error is null)
        {
            return;
        }

        // The inner exception where there is exactly one, because an AggregateException's message
        // is a wrapper's message and says nothing about the disk.
        Exception reported = error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerExceptions[0]
            : error;

        Debug.LogError(
            $"Could not {description}: {reported.Message}. The run is unaffected and still "
            + "playable; it will not survive being killed.");
    }
}
