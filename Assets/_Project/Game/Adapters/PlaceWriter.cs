using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Save;

namespace Soulvail.Game.Adapters;

/// <summary>
/// Writes where a run got to: the best stage of the place it was played in, and, when a finite
/// place was cleared, that it is finished. UI-11d; the profile fields are UI-11a's.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ShardWriter"/>'s shape, and a separate writer for the same reason it is separate
/// from it</b>: it moves two fields with <c>WithBestStage</c> and <c>WithFinished</c> and never
/// calls the DTO's constructor, so it cannot reset a field it does not know. Two writers on one
/// profile are safe because each reads <see cref="ProfileStore.Current"/> at the moment it writes
/// and touches disjoint fields.
/// </para>
/// <para>
/// <b>One save per run end</b> (rule 3). <c>ShardsAwarded</c> and <c>ModeFinished</c> arrive in one
/// tick, so each only notes what it heard and <c>RunEnded</c>, which follows them in that tick,
/// writes. A scope teardown publishes <c>RunEnded</c> with nothing noted and so writes nothing —
/// the refusal <see cref="ShardWriter"/> makes of <c>RunEnded</c> as a payout trigger is kept,
/// because here it is only a flush.
/// </para>
/// <para>
/// <b>Subscribes from its constructor</b> (AR §18.1), and is held by <c>RunTicker</c>'s
/// dependency chain so that it exists before core can publish.
/// </para>
/// </remarks>
public sealed class PlaceWriter : IDisposable
{
    private readonly ProfileStore _profiles;

    private readonly IDisposable _awardSubscription;
    private readonly IDisposable _finishSubscription;
    private readonly IDisposable _endSubscription;

    private ContentId _bestMode;
    private int _bestStage;
    private ContentId _finishedMode;
    private bool _pending;
    private bool _disposed;

    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public PlaceWriter(ProfileStore profiles, DomainEventHub hub)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        _awardSubscription = hub.Subscribe<ShardsAwarded>(OnShardsAwarded);
        _finishSubscription = hub.Subscribe<ModeFinished>(OnModeFinished);
        _endSubscription = hub.Subscribe<RunEnded>(OnRunEnded);
    }

    /// <summary>Drops the subscriptions. A disposed writer writes nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _awardSubscription?.Dispose();
        _finishSubscription?.Dispose();
        _endSubscription?.Dispose();
    }

    private void OnShardsAwarded(ShardsAwarded evt)
    {
        // A publisher that is not a run leaves the mode unstated; it names no place to record.
        if (evt.ModeId.Value is null)
        {
            return;
        }

        _bestMode = evt.ModeId;
        _bestStage = evt.DeepestStage;
        _pending = true;
    }

    private void OnModeFinished(ModeFinished evt)
    {
        _finishedMode = evt.ModeId;

        // The final stage is a best too, even if the payout's own event was never heard.
        if (_bestMode.Value is null || _bestMode == evt.ModeId)
        {
            _bestMode = evt.ModeId;
            _bestStage = Math.Max(_bestStage, evt.FinalStage);
        }

        _pending = true;
    }

    private void OnRunEnded(RunEnded evt)
    {
        if (!_pending)
        {
            return;
        }

        PlayerProfile next = _profiles.Current;

        // A stage below 1 is no stage at all (a run that died on arrival); the row would be refused.
        if (_bestMode.Value is not null && _bestStage >= 1)
        {
            next = next.WithBestStage(_bestMode, _bestStage);
        }

        if (_finishedMode.Value is not null)
        {
            next = next.WithFinished(_finishedMode);
        }

        _pending = false;
        _bestMode = default;
        _bestStage = 0;
        _finishedMode = default;

        _profiles.Save(next);
    }
}
