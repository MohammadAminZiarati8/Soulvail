using System;
using System.Collections.Generic;
using Soulvail.Core.Ports;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// The <see cref="IDomainEvents"/> core tests publish into: it records instead of dispatching,
/// so a test can assert on the exact sequence a system emitted.
/// </summary>
/// <remarks>
/// Recording boxes each payload. That is deliberate and confined to tests — it buys one list
/// holding every event type in publish order, which is what makes "exactly one <c>LeveledUp</c>,
/// and it came after the kill" expressible at all. Never use this in a build.
/// </remarks>
public sealed class RecordingEvents : IDomainEvents
{
    private readonly List<object> _all = new();

    /// <summary>Every payload published, boxed, in publish order.</summary>
    public IReadOnlyList<object> All => _all;

    /// <summary>Records <paramref name="evt"/>. Never throws, whatever was published.</summary>
    public void Publish<T>(in T evt) where T : struct
    {
        _all.Add(evt);
    }

    /// <summary>Every <typeparamref name="T"/> published, in order. Empty when there were none.</summary>
    public IReadOnlyList<T> Of<T>() where T : struct
    {
        var matches = new List<T>();

        for (int i = 0; i < _all.Count; i++)
        {
            if (_all[i] is T typed)
            {
                matches.Add(typed);
            }
        }

        return matches;
    }

    /// <summary>How many <typeparamref name="T"/> were published.</summary>
    public int Count<T>() where T : struct
    {
        int count = 0;

        for (int i = 0; i < _all.Count; i++)
        {
            if (_all[i] is T)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The one <typeparamref name="T"/> that was published. Throws when there were none or more
    /// than one — usually what a test means by "it emitted the event".
    /// </summary>
    public T Single<T>() where T : struct
    {
        IReadOnlyList<T> matches = Of<T>();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {typeof(T).Name}, but {matches.Count} were published.");
        }

        return matches[0];
    }

    /// <summary>Forgets everything recorded so far, so a test can assert on one phase at a time.</summary>
    public void Clear()
    {
        _all.Clear();
    }
}
