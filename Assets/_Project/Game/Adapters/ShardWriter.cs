using System;
using Soulvail.Core.Events;
using Soulvail.Core.Save;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The one thing in the app that banks a payout. It hears core say what a dead run was worth, adds
/// it to the lifetime total on the live <see cref="PlayerProfile"/>, and hands the profile back to
/// <see cref="ProfileStore"/> to persist. GD §14.1, AR §10.3, §11.6.
/// </summary>
/// <remarks>
/// <para>
/// <b>It moves one field with <see cref="PlayerProfile.WithShards"/> and never calls the
/// constructor</b> (M3-09c rule 3, M4-05b rule 2). That is the whole reason <see cref="ProfileStore"/>
/// exists: a writer that knows one field and authors the whole DTO resets every field it does not
/// know about, and here that would put a player's haptics back on and show them a hint they had
/// already dismissed — on the frame they died, which is the worst frame in the game to do it on.
/// </para>
/// <para>
/// <b>It adds rather than assigns.</b> <see cref="PlayerProfile.Shards"/> is a lifetime total and
/// <see cref="ShardsAwarded"/> carries one run's worth, so this reads <c>Current.Shards</c>, adds,
/// and saves. That is also why there may only ever be one thing doing it: two writers on this event
/// would each read the same total and the second would overwrite the first's sum with its own.
/// </para>
/// <para>
/// <b>It listens for <see cref="ShardsAwarded"/> and nothing else</b> (M4-05b rule 5). Not
/// <c>RunEnded</c>, which is also published when <c>RunScope</c> is torn down — a payout riding that
/// would pay a player for quitting to the menu and again on every teardown, which is the refusal
/// <c>SaveWriter</c>, <c>HudPresenter</c> and <c>PausePresenter</c> have each already made. Not
/// <c>PlayerDied</c>, which carries no number.
/// </para>
/// <para>
/// <b>It subscribes from its constructor, never from a <c>Start</c> of its own</b> (AR §18.1), which
/// is <c>SaveWriter</c>'s shape and its argument: VContainer orders no two entry points' starts, so
/// being on <c>RunTicker</c>'s dependency chain is what guarantees the object exists and is
/// listening before core can publish anything at all.
/// </para>
/// <para>
/// <b><see cref="ProfileStore"/> comes from <c>BootScope</c>, not from the run scope.</b> A profile
/// outlives a run by definition, and a store registered per run would forget the payout between the
/// death and the menu (M3-09c rule 4, as <c>FirstActiveHint</c> resolves it). A run scope built
/// against a container with no <see cref="ProfileStore"/> does not compose at all, loudly, which is
/// the failure mode this wants.
/// </para>
/// <para>
/// <b>A failed write is logged and swallowed, and this class adds no second policy for it</b>
/// (M4-05b rule 7). <see cref="ProfileStore.Save"/> already observes the fault, logs it, and leaves
/// <see cref="ProfileStore.Current"/> moved. The worst consequence available is a player losing one
/// run's Shards to a full disk, and that must not throw out of an event callback on the frame the
/// player died.
/// </para>
/// </remarks>
public sealed class ShardWriter : IDisposable
{
    private readonly ProfileStore _profiles;

    private readonly IDisposable _awardSubscription;

    private bool _disposed;

    /// <param name="profiles">
    /// The live profile and the only thing that writes one. Resolved from <c>BootScope</c>, so it
    /// outlives the run this writer is scoped to.
    /// </param>
    /// <param name="hub">
    /// The run's event hub. The concrete type rather than <c>IDomainEvents</c>, because this
    /// subscribes: core publishes through the port, the Unity side listens through the hub — the
    /// split <c>RunInstaller</c> registers both names for.
    /// </param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public ShardWriter(ProfileStore profiles, DomainEventHub hub)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        _awardSubscription = hub.Subscribe<ShardsAwarded>(OnShardsAwarded);
    }

    /// <summary>Drops the subscription. A disposed writer banks nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _awardSubscription?.Dispose();
    }

    private void OnShardsAwarded(ShardsAwarded evt)
    {
        PlayerProfile current = _profiles.Current;

        // Read, add, hand back. Never `WithShards(evt.Total)` — that is the assignment rule 5
        // refuses, and it would silently cap every player's lifetime total at whatever their last
        // run happened to be worth.
        _profiles.Save(current.WithShards(current.Shards + evt.Total));
    }
}
