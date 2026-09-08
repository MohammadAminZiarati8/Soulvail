using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Soulvail.Core.Ports;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The run-scoped adapter behind <see cref="IDomainEvents"/>: one subscriber list per event
/// type, typed fan-out, nothing static. Registered in <c>RunScope</c> and disposed with it, so
/// a run's subscriptions cannot outlive the run. See ADR-0004.
/// </summary>
/// <remarks>
/// <para>
/// Two rules pull against each other here: a publish in flight must see the subscriber list as
/// it was when it started, and a publish must not allocate. Copying the list at the top of every
/// publish satisfies the first and breaks the second. So the list is walked live and *mutations*
/// are deferred instead — a subscribe or unsubscribe requested from inside a handler is queued
/// and applied when the publish unwinds. The queue is only touched when someone actually mutates
/// mid-publish, which is rare, so the hot path pays nothing. It also makes re-entrancy free: a
/// handler that republishes the same type walks the same untouched list.
/// </para>
/// <para>
/// Channels are created by <see cref="Subscribe{T}"/>, never by <see cref="Publish{T}"/>, so
/// publishing an event nobody listens for allocates nothing at all rather than merely nothing
/// after the first time.
/// </para>
/// </remarks>
public sealed class DomainEventHub : IDomainEvents, IDisposable
{
    /// <summary>Event type → its <see cref="Channel{T}"/>. Keyed by <c>typeof(T)</c>, which never allocates.</summary>
    private readonly Dictionary<Type, object> _channels = new();

    private bool _disposed;

    /// <summary>
    /// Registers <paramref name="handler"/> for <typeparamref name="T"/>. Dispose the returned
    /// handle to unsubscribe — from <c>OnDisable</c> in a view, or with the scope in a presenter.
    /// </summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : struct
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DomainEventHub));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        Channel<T> channel = GetOrCreateChannel<T>();
        channel.Add(handler);
        return new Subscription<T>(channel, handler);
    }

    /// <summary>
    /// How many handlers <typeparamref name="T"/> currently has. Read from inside a publish it
    /// reports the list being walked; a mid-publish subscribe or unsubscribe shows up once that
    /// publish has finished.
    /// </summary>
    public int SubscriberCount<T>() where T : struct
    {
        return TryGetChannel(out Channel<T> channel) ? channel.Count : 0;
    }

    public void Publish<T>(in T evt) where T : struct
    {
        if (_disposed || !TryGetChannel(out Channel<T> channel))
        {
            return;
        }

        channel.Publish(in evt);
    }

    /// <summary>Drops every subscription. Publishing afterwards is silent; subscribing throws.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dropping the channels drops every handler with them, and leaves any subscription handle
        // a view still holds pointing at an orphan — which is what makes disposing it later a no-op
        // rather than a crash. A publish already in flight finishes against its own channel,
        // consistent with the frozen-list rule.
        _channels.Clear();
    }

    private Channel<T> GetOrCreateChannel<T>() where T : struct
    {
        if (_channels.TryGetValue(typeof(T), out object existing))
        {
            return (Channel<T>)existing;
        }

        var created = new Channel<T>();
        _channels.Add(typeof(T), created);
        return created;
    }

    private bool TryGetChannel<T>(out Channel<T> channel) where T : struct
    {
        if (_channels.TryGetValue(typeof(T), out object existing))
        {
            channel = (Channel<T>)existing;
            return true;
        }

        channel = null;
        return false;
    }

    /// <summary>One event type's subscriber list, plus the mutations deferred while it publishes.</summary>
    private sealed class Channel<T> where T : struct
    {
        private readonly List<Action<T>> _handlers = new();

        /// <summary>Subscribe/unsubscribe requested from inside a handler; drained when publishing unwinds.</summary>
        private readonly List<PendingChange> _pending = new();

        /// <summary>Nesting depth of <see cref="Publish"/>; above zero, the handler list is frozen.</summary>
        private int _publishDepth;

        public int Count => _handlers.Count;

        public void Add(Action<T> handler)
        {
            if (_publishDepth > 0)
            {
                _pending.Add(new PendingChange(handler, isAdd: true));
                return;
            }

            _handlers.Add(handler);
        }

        public void Remove(Action<T> handler)
        {
            if (_publishDepth > 0)
            {
                _pending.Add(new PendingChange(handler, isAdd: false));
                return;
            }

            _handlers.Remove(handler);
        }

        public void Publish(in T evt)
        {
            // Locals, not fields: nested publishes each need their own, and neither allocates
            // until a handler actually throws.
            Exception first = null;
            List<Exception> all = null;

            _publishDepth++;
            try
            {
                // Indexed, and no mutation can land while depth is above zero, so every handler
                // present when this publish began runs exactly once.
                for (int i = 0; i < _handlers.Count; i++)
                {
                    try
                    {
                        _handlers[i](evt);
                    }
                    catch (Exception ex)
                    {
                        // One broken listener must not silence the rest. Report after the fan-out.
                        if (first is null)
                        {
                            first = ex;
                        }
                        else
                        {
                            all ??= new List<Exception> { first };
                            all.Add(ex);
                        }
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPending();
                }
            }

            if (all is not null)
            {
                throw new AggregateException(all);
            }

            if (first is not null)
            {
                // Not `throw first` — that would overwrite the stack trace of the handler that failed.
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }

        private void ApplyPending()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                PendingChange change = _pending[i];
                if (change.IsAdd)
                {
                    _handlers.Add(change.Handler);
                }
                else
                {
                    _handlers.Remove(change.Handler);
                }
            }

            _pending.Clear();
        }

        private readonly struct PendingChange
        {
            public PendingChange(Action<T> handler, bool isAdd)
            {
                Handler = handler;
                IsAdd = isAdd;
            }

            public Action<T> Handler { get; }

            public bool IsAdd { get; }
        }
    }

    /// <summary>
    /// The handle <see cref="Subscribe{T}"/> returns. Disposing removes the one handler it was
    /// issued for; the flag is what keeps a second dispose from removing an identical handler
    /// that some other handle owns.
    /// </summary>
    private sealed class Subscription<T> : IDisposable where T : struct
    {
        private readonly Channel<T> _channel;
        private readonly Action<T> _handler;

        private bool _disposed;

        public Subscription(Channel<T> channel, Action<T> handler)
        {
            _channel = channel;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _channel.Remove(_handler);
        }
    }
}
