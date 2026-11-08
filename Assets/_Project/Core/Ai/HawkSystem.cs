using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

public sealed class HawkSystem
{
    public const float CircleRadius = 3f;
    public const float Height = 4f;
    public const float DiveSeconds = 0.4f;

    private readonly IDomainEvents _events;
    private readonly PlayerStats _stats;
    private float _expiresAt;
    private float _lastDiveAt;
    private float _strikeAt;
    private float _angle;
    private float _damageOfWeapon;
    private float _stunSeconds;
    private int _targetId;
    private Vector3 _from;
    private Vector3 _to;

    public HawkSystem(IDomainEvents events, PlayerStats stats)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public bool IsAloft { get; private set; }
    public Vector3 Position { get; private set; }

    internal bool CanCall => _stats.HawkSeconds.Value > 0f;

    public void Call(float now, float damageOfWeapon, float stunSeconds)
    {
        _expiresAt = now + _stats.HawkSeconds.Value;
        _damageOfWeapon = damageOfWeapon;
        _stunSeconds = stunSeconds;
        if (IsAloft)
        {
            return;
        }

        IsAloft = true;
        _angle = 0f;
        _lastDiveAt = now;
        _events.Publish(new HawkArrived());
    }

    public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat)
    {
        if (!IsAloft)
        {
            return;
        }

        if (_targetId != 0)
        {
            if (now < _strikeAt)
            {
                Position = Vector3.Lerp(_from, _to, (now - _lastDiveAt) / DiveSeconds);
                return;
            }

            Strike(now, enemies, combat);
        }

        if (now >= _expiresAt)
        {
            Clear();
            return;
        }

        _angle += dt;
        Position = player + new Vector3(MathF.Cos(_angle) * CircleRadius, Height, MathF.Sin(_angle) * CircleRadius);
        if (now < _lastDiveAt + _stats.HawkDiveInterval.Value)
        {
            return;
        }

        EnemyAgent target = FindMarked(player, enemies, combat.Weapon.Range.Value, out _);
        if (target is null)
        {
            return;
        }

        _targetId = target.Id;
        _lastDiveAt = now;
        _strikeAt = now + DiveSeconds;
        _from = Position;
        _to = target.Position;
        _events.Publish(new HawkDove(_targetId, _from, _to));
    }

    public void Clear()
    {
        if (!IsAloft)
        {
            return;
        }

        IsAloft = false;
        _targetId = 0;
        Position = default;
        _events.Publish(new HawkLeft());
    }

    internal int CountMarkedEnemies(Vector3 player, EnemySystem enemies, float range)
    {
        FindMarked(player, enemies, range, out int count);
        return count;
    }

    private void Strike(float now, EnemySystem enemies, PlayerCombat combat)
    {
        int targetId = _targetId;
        _targetId = 0;
        if (!enemies.Registry.TryGet(targetId, out EnemyAgent target) || !target.IsAlive)
        {
            return;
        }

        float damage = _damageOfWeapon * combat.Weapon.Damage.Value;
        DamageResult result = enemies.ApplyDamage(targetId, damage, now, combat);
        if (_stunSeconds > 0f && enemies.IsLight(targetId))
        {
            enemies.Stun(targetId, _stunSeconds);
        }

        _events.Publish(new HawkStruck(targetId, result.Applied));
    }

    private static EnemyAgent FindMarked(Vector3 player, EnemySystem enemies, float range, out int count)
    {
        count = 0;
        EnemyAgent nearest = null;
        float rangeSquared = range > 0f && !float.IsInfinity(range) ? range * range : 0f;
        float nearestSquared = rangeSquared;

        foreach (EnemyAgent enemy in enemies.Registry.Alive)
        {
            if (!enemy.IsAlive || !enemy.CrowdControl.IsMarked)
            {
                continue;
            }

            float dx = enemy.Position.X - player.X;
            float dz = enemy.Position.Z - player.Z;
            float distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > rangeSquared)
            {
                continue;
            }

            count++;
            if (nearest is null || distanceSquared < nearestSquared)
            {
                nearest = enemy;
                nearestSquared = distanceSquared;
            }
        }

        return nearest;
    }
}
