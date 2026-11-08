using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

/// <summary>One glob in flight: who spat it, whom it homes on, and what it carries.</summary>
public struct PetGlob
{
    public int Id;
    public int PetId;
    public int TargetId;
    public Vector3 Position;
    public float Damage;
    public float Slow;
    public float Speed;
}

/// <summary>
/// BT-07a: the boar's globs. A fixed pool; each glob homes on its quarry at its own speed, hits it
/// when within <see cref="HitRadius"/>, and dies on the first cover its step crosses.
/// </summary>
public sealed class PetSpit
{
    public const int Capacity = 8;
    public const float HitRadius = 0.5f;
    public const float SlowSeconds = 1.5f;

    private readonly PetGlob[] _globs = new PetGlob[Capacity];
    private readonly IDomainEvents _events;
    private int _count;
    private int _nextId = 1;

    public PetSpit(IDomainEvents events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public ReadOnlySpan<PetGlob> InFlight => _globs.AsSpan(0, _count);

    /// <summary>Launches a glob; false when the pool is full and nothing flies.</summary>
    internal bool TryLaunch(int petId, Vector3 from, int targetId, float damage, float slow, float speed)
    {
        if (_count == Capacity || !(speed > 0f) || !(damage > 0f))
        {
            return false;
        }

        int id = _nextId++;
        _globs[_count++] = new PetGlob
        {
            Id = id,
            PetId = petId,
            TargetId = targetId,
            Position = from,
            Damage = damage,
            Slow = slow > 0f ? MathF.Min(slow, CrowdControl.MaxSlow) : 0f,
            Speed = speed,
        };
        _events.Publish(new PetSpat(petId, id, from, targetId));
        return true;
    }

    internal void Tick(float dt, float now, EnemySystem enemies, PlayerCombat combat,
        Func<Vector3, Vector3, Vector3?> traceCover)
    {
        int index = 0;
        while (index < _count)
        {
            ref PetGlob glob = ref _globs[index];
            if (!enemies.Registry.TryGet(glob.TargetId, out EnemyAgent target) || !target.IsAlive)
            {
                Land(index, glob.Position, false);
                continue;
            }

            Vector3 to = target.Position;
            Vector3 offset = to - glob.Position;
            float distance = offset.Length();
            float step = dt > 0f ? glob.Speed * dt : 0f;
            bool arrives = distance <= step + HitRadius;
            Vector3 next = arrives || !(distance > 0.0001f) ? to : glob.Position + offset * (step / distance);
            if (traceCover?.Invoke(glob.Position, next) is Vector3 cover)
            {
                Land(index, cover, false);
                continue;
            }

            if (!arrives)
            {
                glob.Position = next;
                index++;
                continue;
            }

            target.Blackboard.LastBitBy = glob.PetId;
            target.Blackboard.LastBitAt = now;
            enemies.ApplyDamage(target.Id, glob.Damage, now, combat);
            if (glob.Slow > 0f)
            {
                enemies.Slow(target.Id, glob.Slow, SlowSeconds);
            }

            Land(index, to, true);
        }
    }

    public void Clear() => _count = 0;

    private void Land(int index, Vector3 position, bool hit)
    {
        int id = _globs[index].Id;
        _count--;
        for (int i = index; i < _count; i++)
        {
            _globs[i] = _globs[i + 1];
        }

        _events.Publish(new GlobLanded(id, position, hit));
    }
}
