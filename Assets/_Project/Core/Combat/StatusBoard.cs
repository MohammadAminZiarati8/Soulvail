using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>
/// The player's buffs and debuffs (MK-05): debuffs first, then buffs, each in posting order. Eight
/// is a design ceiling, so a ninth id is refused rather than grown into.
/// </summary>
public sealed class StatusBoard
{
    public const int Capacity = 8;

    private readonly StatusEntry[] _entries = new StatusEntry[Capacity];
    private int _debuffs;

    public int Count { get; private set; }

    /// <summary>True once a post has been refused for capacity; the view logs it once.</summary>
    public bool HasRefused { get; private set; }

    public StatusEntry this[int i] => (uint)i < (uint)Count
        ? _entries[i]
        : throw new ArgumentOutOfRangeException(nameof(i));

    /// <summary>Adds or refreshes by id. <paramref name="seconds"/> of +∞ means no timer.</summary>
    public void Post(ContentId id, bool isBuff, float seconds, int stacks)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("A status needs a content id.", nameof(id));
        }

        if (!(seconds > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Seconds must be positive or +∞.");
        }

        if (stacks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stacks), stacks, "Stacks must be at least 1.");
        }

        var entry = new StatusEntry(id, isBuff, seconds, seconds, stacks);
        int at = IndexOf(id);
        if (at >= 0 && _entries[at].IsBuff == isBuff)
        {
            _entries[at] = entry;
            return;
        }

        if (at >= 0)
        {
            RemoveAt(at);
        }
        else if (Count == Capacity)
        {
            HasRefused = true;
            return;
        }

        int insert = isBuff ? Count : _debuffs;
        for (int i = Count; i > insert; i--)
        {
            _entries[i] = _entries[i - 1];
        }

        _entries[insert] = entry;
        Count++;
        if (!isBuff)
        {
            _debuffs++;
        }
    }

    public void Clear(ContentId id)
    {
        int at = IndexOf(id);
        if (at >= 0)
        {
            RemoveAt(at);
        }
    }

    /// <summary>Drops every status: a stage boundary and the run's end.</summary>
    public void Reset()
    {
        Array.Clear(_entries, 0, Count);
        Count = 0;
        _debuffs = 0;
    }

    public void Tick(float dt)
    {
        if (!(dt > 0f))
        {
            return;
        }

        for (int i = Count - 1; i >= 0; i--)
        {
            StatusEntry e = _entries[i];
            if (!e.IsTimed)
            {
                continue;
            }

            float remaining = e.Remaining - dt;
            if (remaining > 0f)
            {
                _entries[i] = new StatusEntry(e.Id, e.IsBuff, remaining, e.Duration, e.Stacks);
            }
            else
            {
                RemoveAt(i);
            }
        }
    }

    private int IndexOf(ContentId id)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_entries[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveAt(int at)
    {
        if (!_entries[at].IsBuff)
        {
            _debuffs--;
        }

        Count--;
        for (int i = at; i < Count; i++)
        {
            _entries[i] = _entries[i + 1];
        }

        _entries[Count] = default;
    }
}
