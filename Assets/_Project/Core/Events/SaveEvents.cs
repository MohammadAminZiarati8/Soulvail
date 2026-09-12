using Soulvail.Core.Save;

namespace Soulvail.Core.Events;

// The save module's domain events — one, at M2-14a. Grouped in a file of their own on
// `RunEvents.cs`' precedent rather than folded into it: a run's lifecycle and a run's persistence
// are different vocabularies, and the module that grows here next is a profile being written, not
// a run beginning. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.

/// <summary>
/// A run has been written down: core has composed a <see cref="RunSnapshot"/> describing the stage
/// the player is about to play, and whoever owns a medium may now put it somewhere. GD §7.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries the whole DTO by value.</b> <see cref="RunSnapshot"/> is a <c>readonly struct</c>
/// and this crosses <c>IDomainEvents</c> by <c>in</c>, so a snapshot costs nothing to announce —
/// which is what lets the write point be a stage boundary that is also swapping an arena.
/// </para>
/// <para>
/// <b>Core does not save; it says that there is something to save.</b> Persistence is asynchronous
/// and owns a file, so it lives on the Unity side behind <c>ISaveStore</c> — <c>SaveWriter</c> is
/// the one listener, and a failed write never reaches the frame that caused it (M2-14a rule 8).
/// </para>
/// </remarks>
public readonly struct RunSnapshotTaken
{
    /// <summary>The run as it stood, complete enough to rebuild it.</summary>
    public readonly RunSnapshot Snapshot;

    public RunSnapshotTaken(RunSnapshot snapshot)
    {
        Snapshot = snapshot;
    }
}
