using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

public enum AxePass { Out, Back }

public readonly struct Axe
{
    public int Id { get; }
    public AxePass Pass { get; }
    public Vector3 Position { get; }
    public Vector3 TurnPoint { get; }
    public float Damage { get; }

    internal Axe(int id, AxePass pass, Vector3 position, Vector3 turnPoint, float damage)
    {
        Id = id;
        Pass = pass;
        Position = position;
        TurnPoint = turnPoint;
        Damage = damage;
    }
}

public sealed class AxeSystem
{
    public const int Capacity = 8;
    public const float CatchRadius = 0.3f;

    private readonly IDomainEvents _events;
    private readonly WeaponSpec _weapon;
    private readonly Stat _range;
    private readonly Stat _returnDamage;
    private readonly Stat _carry;
    private readonly Axe[] _axes = new Axe[Capacity];
    private readonly int[][] _hitIds = new int[Capacity][];
    private readonly int[] _hitCounts = new int[Capacity];
    private int _count;
    private int _nextId;
    private int _carryLimit;

    public AxeSystem(IDomainEvents events, PlayerStats stats, WeaponSpec weapon)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        if (weapon.Kind != WeaponKind.ReturningAxe)
        {
            throw new ArgumentException("An axe system needs a returning axe weapon.", nameof(weapon));
        }

        _weapon = weapon;
        _range = stats.Resolve(PlayerStat.WeaponRange);
        _returnDamage = stats.AxeReturnDamage;
        _carry = stats.AxeCarry;
        for (int i = 0; i < Capacity; i++)
        {
            _hitIds[i] = new int[weapon.AxeHitsPerPass];
        }

        Clear();
        _carry.Changed += OnCarryChanged;
    }

    public int InHand { get; private set; }
    public ReadOnlySpan<Axe> InFlight => _axes.AsSpan(0, _count);

    public bool TryThrow(Vector3 hand, Vector3 toward, float damage, float now)
    {
        if (InHand == 0 || _count == Capacity)
        {
            return false;
        }

        Vector3 direction = toward - hand;
        direction.Y = 0f;
        float distance = direction.Length();
        direction = distance > 0f ? direction / distance : Vector3.UnitZ;
        Vector3 turnPoint = hand + (direction * MathF.Max(0f, _range.Value));
        int id = ++_nextId;
        _axes[_count] = new Axe(id, AxePass.Out, hand, turnPoint, damage);
        _hitCounts[_count] = 0;
        _count++;
        InHand--;
        _events.Publish(new AxeThrown(id, hand, turnPoint));
        return true;
    }

    public void Tick(float dt, float now, Vector3 hand, PlayerCombat player, EnemySystem enemies)
    {
        if (!(dt > 0f))
        {
            return;
        }

        float step = _weapon.AxeSpeed * dt;
        for (int i = 0; i < _count;)
        {
            Axe axe = _axes[i];
            bool outbound = axe.Pass == AxePass.Out;
            Vector3 goal = outbound ? axe.TurnPoint : hand;
            Vector3 offset = goal - axe.Position;
            float distance = offset.Length();
            Vector3 end = distance <= step ? goal : axe.Position + (offset * (step / distance));
            float damage = outbound ? axe.Damage : axe.Damage * _returnDamage.Value;

            Sweep(i, axe, end, damage, now, player, enemies);

            if (!outbound && Vector3.DistanceSquared(end, hand) <= CatchRadius * CatchRadius)
            {
                Remove(i);
                InHand = Math.Min(_carryLimit, InHand + 1);
                _events.Publish(new AxeCaught(axe.Id));
                continue;
            }

            if (outbound && distance <= step)
            {
                _axes[i] = new Axe(axe.Id, AxePass.Back, end, axe.TurnPoint, axe.Damage);
                _hitCounts[i] = 0;
                _events.Publish(new AxeTurned(axe.Id, end));
            }
            else
            {
                _axes[i] = new Axe(axe.Id, axe.Pass, end, axe.TurnPoint, axe.Damage);
            }

            i++;
        }
    }

    public void Clear()
    {
        _count = 0;
        _carryLimit = CarryLimit(_carry.Value);
        InHand = _carryLimit;
    }

    private void Sweep(int slot, Axe axe, Vector3 end, float damage, float now, PlayerCombat player, EnemySystem enemies)
    {
        if (!(damage > 0f))
        {
            return;
        }

        var segment = new Vector2(end.X - axe.Position.X, end.Z - axe.Position.Z);
        float lengthSquared = segment.LengthSquared();
        int[] hitIds = _hitIds[slot];

        while (_hitCounts[slot] < hitIds.Length)
        {
            int bestId = 0;
            float bestAlong = float.PositiveInfinity;
            foreach (EnemyAgent enemy in enemies.Registry.Alive)
            {
                if (!enemy.IsAlive || Array.IndexOf(hitIds, enemy.Id, 0, _hitCounts[slot]) >= 0)
                {
                    continue;
                }

                var offset = new Vector2(enemy.Position.X - axe.Position.X, enemy.Position.Z - axe.Position.Z);
                float along = lengthSquared > 0f
                    ? Math.Clamp(Vector2.Dot(offset, segment) / lengthSquared, 0f, 1f)
                    : 0f;
                float reach = _weapon.ShotRadius + enemy.Spec.BodyRadius;
                if (along < bestAlong && Vector2.DistanceSquared(offset, segment * along) <= reach * reach)
                {
                    bestId = enemy.Id;
                    bestAlong = along;
                }
            }

            if (bestId == 0)
            {
                break;
            }

            hitIds[_hitCounts[slot]++] = bestId;
            DamageResult result = enemies.ApplyDamage(bestId, damage, now, player);
            if (_weapon.MarkSeconds > 0f)
            {
                enemies.Mark(bestId, _weapon.MarkSeconds);
            }

            _events.Publish(new AxeHit(axe.Id, bestId, axe.Pass, result.Applied));
        }
    }

    private void Remove(int slot)
    {
        int[] freed = _hitIds[slot];
        _count--;
        for (int i = slot; i < _count; i++)
        {
            _axes[i] = _axes[i + 1];
            _hitIds[i] = _hitIds[i + 1];
            _hitCounts[i] = _hitCounts[i + 1];
        }

        _hitIds[_count] = freed;
    }

    private void OnCarryChanged(Stat stat)
    {
        int carry = CarryLimit(stat.Value);
        InHand = Math.Max(0, InHand + carry - _carryLimit);
        _carryLimit = carry;
    }

    private static int CarryLimit(float carry) => (int)Math.Clamp(carry, 1f, 5f);
}
