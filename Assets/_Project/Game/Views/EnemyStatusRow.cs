using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;

namespace Soulvail.Game.Views;

/// <summary>
/// The statuses an enemy's bar knows about, in the order they draw: every debuff before every buff,
/// higher priority first within a side, arrival order breaking a tie. Plain C#; the bar reads it.
/// </summary>
/// <remarks>The countdown is cosmetic. Core ends a status; this only drains the sweep toward it.</remarks>
public sealed class EnemyStatusRow
{
    public const int Tracked = 8;
    public const int EliteShown = 4;
    public const int BasicShown = 1;

    private readonly Entry[] _entries = new Entry[Tracked];
    private int _count;
    private int _arrivals;

    public int Count => _count;

    /// <summary>Adds, updates or removes the event's status by id.</summary>
    public void Apply(in EnemyStatusChanged evt)
    {
        int at = IndexOf(evt.Status.Value);

        if (!(evt.Seconds > 0f))
        {
            if (at >= 0)
            {
                RemoveAt(at);
            }

            return;
        }

        int stacks = evt.Stacks < 1 ? 1 : evt.Stacks;

        if (at >= 0)
        {
            ref Entry known = ref _entries[at];
            known.Duration = evt.Seconds;
            known.Remaining = evt.Seconds;
            known.Stacks = stacks;

            return;
        }

        var fresh = new Entry
        {
            Rule = evt.Rule,
            Duration = evt.Seconds,
            Remaining = evt.Seconds,
            Stacks = stacks,
            Arrival = _arrivals++,
        };

        if (_count == Tracked)
        {
            if (!Outranks(in fresh, in _entries[Tracked - 1]))
            {
                return;
            }

            _count--;
        }

        int place = _count;

        while (place > 0 && Outranks(in fresh, in _entries[place - 1]))
        {
            _entries[place] = _entries[place - 1];
            place--;
        }

        _entries[place] = fresh;
        _count++;
    }

    /// <summary>Drains every sweep by <paramref name="dt"/>; an entry stays until core removes it.</summary>
    public void Step(float dt)
    {
        if (!(dt > 0f))
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            float left = _entries[i].Remaining - dt;
            _entries[i].Remaining = left > 0f ? left : 0f;
        }
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
        _arrivals = 0;
    }

    /// <summary>Rank 0 is drawn first.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is not below <see cref="Count"/>.</exception>
    public StatusRule RuleAt(int rank) => At(rank).Rule;

    /// <summary>Remaining over duration, 0..1.</summary>
    public float FillAt(int rank)
    {
        Entry entry = At(rank);
        float fill = entry.Remaining / entry.Duration;

        return fill > 1f ? 1f : fill > 0f ? fill : 0f;
    }

    public int StacksAt(int rank) => At(rank).Stacks;

    /// <summary>How many icons the bar draws: one on a basic enemy, four on an Elite.</summary>
    public int Shown(bool isElite)
    {
        int limit = isElite ? EliteShown : BasicShown;

        return _count < limit ? _count : limit;
    }

    private Entry At(int rank)
    {
        if ((uint)rank >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "No status at that rank.");
        }

        return _entries[rank];
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].Rule.Id.Value == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveAt(int at)
    {
        for (int i = at; i < _count - 1; i++)
        {
            _entries[i] = _entries[i + 1];
        }

        _count--;
        _entries[_count] = default;
    }

    private static bool Outranks(in Entry a, in Entry b)
    {
        if (a.Rule.Side != b.Rule.Side)
        {
            return a.Rule.Side == StatusSide.Debuff;
        }

        if (a.Rule.Priority != b.Rule.Priority)
        {
            return a.Rule.Priority > b.Rule.Priority;
        }

        return a.Arrival < b.Arrival;
    }

    private struct Entry
    {
        public StatusRule Rule;
        public float Duration;
        public float Remaining;
        public int Stacks;
        public int Arrival;
    }
}
