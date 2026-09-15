using System;
using System.Threading;
using System.Threading.Tasks;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The live <see cref="PlayerProfile"/>, and the only thing in the app that writes one. Built in
/// <c>BootScope</c>, so it outlives every run — which is what a profile means (AR §11.6, ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class exists because there was no single writer, and a second field is what made that
/// cost something.</b> Until M3-09c <c>HapticsSettings</c> persisted with
/// <c>new PlayerProfile(CurrentVersion, value)</c> — it authored the <em>whole struct</em> from the
/// one field it knew about. That is correct for a record with one field in it and silently
/// destructive the moment there are two: toggling haptics would have reset
/// <see cref="PlayerProfile.SeenFirstActiveHint"/> to whatever the constructor defaulted it to, and
/// a one-time hint would have come back for a player who had already dismissed it. So the live
/// profile is held here, both features copy-with (<c>WithHaptics</c>,
/// <c>WithSeenFirstActiveHint</c>) and hand it back, and <see cref="Save"/> is the only call in
/// <c>Soulvail.Game</c> that reaches <see cref="ISaveStore.SaveProfile"/> (M3-09c rule 3).
/// </para>
/// <para>
/// <b>The general rule, written where the next writer will read it: a writer that knows one field
/// must never author the whole DTO.</b> M4-07's Shards and M6-09's unlocks are profile fields too,
/// and rule 3 is why this store is built now rather than left for four writers to discover each
/// other.
/// </para>
/// <para>
/// <b><c>BootScope</c>, not <c>RunScope</c>.</b> A profile outlives a run by definition, and the
/// hint is spent <em>during</em> one — a store rebuilt per run would forget the write between the
/// level-up that spent it and the boundary that saved it (M3-09c rule 4). It is <c>Game</c>-side
/// rather than core for the reason <c>HapticsSettings</c> is: nothing in a simulation asks whether
/// a hint has been seen.
/// </para>
/// <para>
/// <b>It does not read the disk, ever.</b> The profile is loaded once, at boot, by <c>BootFlow</c> —
/// the one place in the app with a legitimate reason to await I/O — and handed over through
/// <see cref="Adopt"/>. A store that loaded here would have to block, or return before the value it
/// promised had arrived. That is <c>HapticsSettings.FromStore</c>'s own argument, inherited.
/// </para>
/// </remarks>
public sealed class ProfileStore
{
    private readonly ISaveStore _store;

    private PlayerProfile _current = PlayerProfile.Default;

    /// <param name="store">Where <see cref="Save"/> writes. Never read from here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public ProfileStore(ISaveStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// The profile as it currently stands — <see cref="PlayerProfile.Default"/> until a load lands.
    /// </summary>
    /// <remarks>
    /// The default rather than <c>default</c>, so a reader that arrives before <c>BootFlow</c>'s
    /// continuation gets GD §16.3's answers rather than version 0's invalid one. On a fresh install
    /// it is also the final answer, because a missing file is not written back (rule 4).
    /// </remarks>
    public PlayerProfile Current => _current;

    /// <summary>
    /// Takes the profile that arrived from disk, <b>without writing it back</b>. What
    /// <c>BootFlow</c>'s load calls.
    /// </summary>
    /// <remarks>
    /// The write is skipped rather than skipped-if-equal, and that is <c>HapticsSettings.Apply</c>'s
    /// old reason: a load that immediately re-saves is a load that can corrupt what it just read —
    /// and on a fresh install, where the caller substitutes <see cref="PlayerProfile.Default"/> for
    /// a file that does not exist, it would put a profile on disk for a player who has never changed
    /// a setting.
    /// </remarks>
    public void Adopt(in PlayerProfile profile)
    {
        _current = profile;
    }

    /// <summary>
    /// Replaces the profile and persists it — the only write of a profile in the app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fire and forget, with the fault observed and logged</b> (rule 5), which is
    /// <c>HapticsSettings</c>' existing shape and <c>SaveWriter</c>'s argument: a profile write that
    /// fails must not take down a run or throw out of a UI callback. The smallest possible
    /// consequence in the project is a hint shown twice because a disk was full.
    /// </para>
    /// <para>
    /// <b><see cref="Current"/> moves before the task is awaited and stays moved if it faults.</b>
    /// The toggle the player flipped has already moved on screen, and the hint they have already
    /// seen has already been seen; a store that rolled back to match the disk would disagree with
    /// the player about what happened in front of them.
    /// </para>
    /// <para>
    /// The continuation runs synchronously, so with today's synchronous store the error is logged
    /// before this method returns.
    /// </para>
    /// </remarks>
    public void Save(in PlayerProfile profile)
    {
        _current = profile;

        _store.SaveProfile(profile).ContinueWith(
            static task => Debug.LogError(
                "Could not save the player profile: " +
                task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
