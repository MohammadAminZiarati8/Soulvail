using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>Hits resolve after the projectile pass; obstacle facts wait until the following tick.</summary>
public sealed class BindResolver
{
    private readonly EnemySystem _enemies;
    private readonly PlayerStats _stats;
    private readonly CombatBlackboard _blackboard;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly Pending[] _pending;
    private int _count;

    public BindResolver(EnemySystem enemies, PlayerStats stats, CombatBlackboard blackboard,
        IDomainEvents events, IIntentSink intents, int projectileCapacity)
    {
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        if (projectileCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(projectileCapacity));
        }

        // Two unanswered frames plus this frame's landings. Never grows on a tick.
        _pending = new Pending[checked(projectileCapacity * 3)];
    }

    public void Hit(int targetId, BindingShot shot)
    {
        if (shot is null)
        {
            throw new ArgumentNullException(nameof(shot));
        }

        if (_count == _pending.Length)
        {
            throw new InvalidOperationException("Binding hits exceeded the projectile capacity.");
        }

        _pending[_count++] = new Pending { TargetId = targetId, Shot = shot, Age = -1 };
    }

    /// <summary>Records the first answer to an outstanding probe; never resolves during facts.</summary>
    public void ReportAnchor(int targetId, bool hit, Vector3 point)
    {
        for (int i = 0; i < _count; i++)
        {
            ref Pending pending = ref _pending[i];
            if (pending.TargetId != targetId || pending.Age < 0 || pending.Answered)
            {
                continue;
            }

            if (hit && (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)))
            {
                throw new ArgumentOutOfRangeException(nameof(point));
            }

            pending.Answered = true;
            pending.Hit = hit;
            pending.Point = point;
            return;
        }
    }

    public void Tick()
    {
        int write = 0;
        for (int read = 0; read < _count; read++)
        {
            Pending pending = _pending[read];
            if (!_enemies.Registry.TryGet(pending.TargetId, out EnemyAgent target) || !target.IsAlive)
            {
                continue;
            }

            if (pending.Age < 0)
            {
                Vector3 direction = target.Position - _blackboard.PlayerPosition;
                direction.Y = 0f;
                float length = direction.Length();
                direction = length > 0f ? direction / length : Vector3.Zero;
                EnemyAgent anchor = Pick(target, direction, pending.Shot);
                if (anchor != null)
                {
                    Resolve(pending, BindAnchor.Enemy, anchor.Id, anchor.Position);
                    continue;
                }

                pending.Age = 0;
                // Facts arrive after Tick, once the body has flushed its transforms.
                _pending[write] = pending;
                _intents.BindProbe(new BindProbeIntent(target.Id, target.Position, direction, pending.Shot.Reach));
                write++;
                continue;
            }

            pending.Age++;
            if (pending.Answered || pending.Age >= 2)
            {
                Resolve(pending, pending.Answered && pending.Hit ? BindAnchor.Obstacle : BindAnchor.None,
                    0, pending.Answered && pending.Hit ? pending.Point : target.Position);
                continue;
            }

            _pending[write++] = pending;
        }

        Array.Clear(_pending, write, _count - write);
        _count = write;
    }

    public void Clear()
    {
        Array.Clear(_pending, 0, _count);
        _count = 0;
    }

    private EnemyAgent Pick(EnemyAgent target, Vector3 direction, BindingShot shot)
    {
        EnemyAgent nearest = null;
        float best = shot.Reach * shot.Reach;
        float cosine = MathF.Cos(shot.ConeHalfAngleDeg * (MathF.PI / 180f));
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;
        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent other = agents[i];
            Vector3 offset = other.Position - target.Position;
            offset.Y = 0f;
            float distance = offset.LengthSquared();
            if (other.Id == target.Id || !other.IsAlive || !(distance > 0f && distance <= best)
                || Vector3.Dot(offset, direction) < MathF.Sqrt(distance) * cosine)
            {
                continue;
            }

            if (nearest == null || distance < best || other.Id < nearest.Id)
            {
                nearest = other;
                best = distance;
            }
        }

        return nearest;
    }

    private void Resolve(in Pending pending, BindAnchor anchor, int enemyId, Vector3 point)
    {
        float seconds = anchor == BindAnchor.None ? pending.Shot.AloneSeconds
            : pending.Shot.BoundSeconds + _stats.Resolve(PlayerStat.BindStun).Value;
        _enemies.Stun(pending.TargetId, seconds);
        if (anchor == BindAnchor.Enemy)
        {
            _enemies.Stun(enemyId, seconds);
        }

        // JG-26b rule 1: a rope shows only what is held; a boss is never held (JG-26a).
        bool held = !IsBoss(pending.TargetId) || (anchor == BindAnchor.Enemy && !IsBoss(enemyId));
        if (held)
        {
            _events.Publish(new BindingShotBound(pending.TargetId, anchor, enemyId, point, seconds));
        }
    }

    private bool IsBoss(int enemyId) =>
        _enemies.Registry.TryGet(enemyId, out EnemyAgent agent) && agent.Behaviour is BossBehaviour;

    private struct Pending
    {
        public int TargetId;
        public BindingShot Shot;
        public int Age;
        public bool Answered;
        public bool Hit;
        public Vector3 Point;
    }
}
