using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>Fixed ground snares, in placement order. Expiry wins at the lifetime boundary.</summary>
public sealed class TrapSystem
{
    public const int Capacity = 2;

    private readonly EnemySystem _enemies;
    private readonly PlayerCombat _player;
    private readonly IDomainEvents _events;
    private readonly Entry[] _entries = new Entry[Capacity];
    private int _nextId = 1;

    public TrapSystem(EnemySystem enemies, PlayerCombat player, IDomainEvents events)
    {
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public int Count { get; private set; }

    /// <summary>Places a standard 0.8 m snare that arms in 0.3 s.</summary>
    public int Place(Vector3 at, float rootSeconds, float damage, float lifetime, float now) =>
        Place(at, rootSeconds, damage, lifetime, now, 0.8f, 0.3f);

    /// <summary>Snapshots the cast's numbers and the authored PlaceSnare geometry.</summary>
    public int Place(Vector3 at, float rootSeconds, float damage, float lifetime, float now,
        float radius, float armDelay)
    {
        Positive(rootSeconds, nameof(rootSeconds));
        Positive(damage, nameof(damage));
        Positive(lifetime, nameof(lifetime));
        Positive(radius, nameof(radius));
        Positive(armDelay, nameof(armDelay));
        if (!(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z)))
        {
            throw new ArgumentOutOfRangeException(nameof(at));
        }

        if (!(float.IsFinite(now) && float.IsFinite(now + lifetime) && float.IsFinite(now + armDelay)))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        if (Count == Capacity)
        {
            Expire(0);
        }

        int id = _nextId++;
        _entries[Count++] = new Entry
        {
            Id = id,
            At = at,
            Radius = radius,
            RootSeconds = rootSeconds,
            Damage = damage,
            ArmsAt = now + armDelay,
            ExpiresAt = now + lifetime,
        };
        _events.Publish(new TrapPlaced(id, at));
        return id;
    }

    public void Tick(float now)
    {
        if (!float.IsFinite(now))
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;
        int index = 0;
        while (index < Count)
        {
            Entry trap = _entries[index];
            if (now >= trap.ExpiresAt)
            {
                Expire(index);
                continue;
            }

            bool sprung = false;
            if (now >= trap.ArmsAt)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    EnemyAgent agent = agents[i];
                    if (!agent.IsAlive)
                    {
                        continue;
                    }

                    float x = agent.Position.X - trap.At.X;
                    float z = agent.Position.Z - trap.At.Z;
                    float reach = trap.Radius + agent.Spec.BodyRadius;
                    if (!(x * x + z * z <= reach * reach))
                    {
                        continue;
                    }

                    RemoveAt(index);
                    // EnemySystem is the single door for boss scaling, damage and death effects.
                    _enemies.Root(agent.Id, trap.RootSeconds);
                    _enemies.ApplyDamage(agent.Id, trap.Damage, now, _player);
                    _events.Publish(new TrapSprung(trap.Id, agent.Id));
                    sprung = true;
                    break;
                }
            }

            if (!sprung)
            {
                index++;
            }
        }
    }

    /// <summary>Stage/run teardown is silent; ids remain unique across stage boundaries.</summary>
    public void Clear()
    {
        Array.Clear(_entries, 0, Count);
        Count = 0;
    }

    private void Expire(int index)
    {
        int id = _entries[index].Id;
        RemoveAt(index);
        _events.Publish(new TrapExpired(id));
    }

    private void RemoveAt(int index)
    {
        for (int i = index + 1; i < Count; i++)
        {
            _entries[i - 1] = _entries[i];
        }

        _entries[--Count] = default;
    }

    private static void Positive(float value, string name)
    {
        if (!(value > 0f) || !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private struct Entry
    {
        public int Id;
        public Vector3 At;
        public float Radius;
        public float RootSeconds;
        public float Damage;
        public float ArmsAt;
        public float ExpiresAt;
    }
}
