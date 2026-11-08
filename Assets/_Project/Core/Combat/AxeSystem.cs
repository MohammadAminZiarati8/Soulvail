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

    /// <summary>The most axes he carries. A carry modifier past this does nothing (BT-01 rule 1).</summary>
    public const int MaxCarry = 2;

    /// <summary>Each axe of a pair leaves this far either side of the aim.</summary>
    public const float PairSpreadDegrees = 8f;

    /// <summary>The slowest a slowed attack speed can make the flight, as a fraction of the base.</summary>
    public const float MinPaceFactor = 0.1f;
    public const float CatchRadius = 0.3f;
    public const float ReturnSlowSeconds = 1.5f;

    private readonly IDomainEvents _events;
    private readonly WeaponSpec _weapon;
    private readonly Stat _range;
    private readonly Stat _returnDamage;
    private readonly Stat _returnSlow;
    private readonly Stat _carry;
    private readonly Stat _fireRate;
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
        _returnSlow = stats.AxeReturnSlow;
        _carry = stats.AxeCarry;
        _fireRate = stats.Resolve(PlayerStat.FireRate);
        for (int i = 0; i < Capacity; i++)
        {
            _hitIds[i] = new int[weapon.AxeHitsPerPass];
        }

        Clear();
        _carry.Changed += OnCarryChanged;
    }

    public int InHand { get; private set; }
    public ReadOnlySpan<Axe> InFlight => _axes.AsSpan(0, _count);

    /// <summary>
    /// Attack speed over the weapon's base swings per second: what the flight is multiplied by, so
    /// a faster cadence brings the axes home just as much sooner (BT-01 rule 5). Floored at
    /// <see cref="MinPaceFactor"/>, so a slowed or silenced weapon never strands an axe in the air.
    /// </summary>
    public float PaceFactor
    {
        get
        {
            float factor = _fireRate.Value / _weapon.SwingsPerSecond;
            return factor > MinPaceFactor && !float.IsInfinity(factor) ? factor : MinPaceFactor;
        }
    }

    public bool TryThrow(Vector3 hand, Vector3 toward, float damage, float now)
    {
        if (InHand == 0 || _count == Capacity)
        {
            return false;
        }

        Launch(hand, Flat(toward - hand), damage);
        return true;
    }

    /// <summary>
    /// Throws every axe in hand at once, fanned ±<see cref="PairSpreadDegrees"/> about
    /// <paramref name="toward"/>; a lone axe flies straight at it. Each turns at the full
    /// weapon range along its own line, whatever the target's distance.
    /// </summary>
    /// <returns>How many axes left the hand.</returns>
    public int TryThrowPair(Vector3 hand, Vector3 toward, float damage, float now)
    {
        int throwing = Math.Min(InHand, Capacity - _count);
        if (throwing <= 0)
        {
            return 0;
        }

        Vector3 aim = Flat(toward - hand);
        if (throwing == 1)
        {
            Launch(hand, aim, damage);
            return 1;
        }

        float spread = PairSpreadDegrees * (MathF.PI / 180f);
        for (int i = 0; i < throwing; i++)
        {
            float angle = -spread + (2f * spread * i / (throwing - 1));
            Launch(hand, Turn(aim, angle), damage);
        }

        return throwing;
    }

    /// <summary>
    /// Flies every axe one step. An out step is traced against cover with
    /// <paramref name="traceCover"/> and turns where it meets it; the way back is never traced,
    /// so an axe always comes home (BT-02). A null trace means the arena has no cover.
    /// </summary>
    public void Tick(float dt, float now, Vector3 hand, PlayerCombat player, EnemySystem enemies,
        Func<Vector3, Vector3, Vector3?> traceCover)
    {
        if (!(dt > 0f))
        {
            return;
        }

        float step = _weapon.AxeSpeed * PaceFactor * dt;
        for (int i = 0; i < _count;)
        {
            Axe axe = _axes[i];
            bool outbound = axe.Pass == AxePass.Out;
            Vector3 goal = outbound ? axe.TurnPoint : hand;
            Vector3 offset = goal - axe.Position;
            float distance = offset.Length();
            Vector3 end = distance <= step ? goal : axe.Position + (offset * (step / distance));
            bool turns = outbound && distance <= step;
            Vector3 turnPoint = axe.TurnPoint;
            if (outbound && traceCover?.Invoke(axe.Position, end) is Vector3 cover)
            {
                end = cover;
                turnPoint = cover;
                turns = true;
            }

            float damage = outbound ? axe.Damage : axe.Damage * _returnDamage.Value;

            Sweep(i, axe, end, damage, now, player, enemies);

            if (!outbound && Vector3.DistanceSquared(end, hand) <= CatchRadius * CatchRadius)
            {
                Remove(i);
                InHand = Math.Min(_carryLimit, InHand + 1);
                _events.Publish(new AxeCaught(axe.Id));
                continue;
            }

            if (turns)
            {
                _axes[i] = new Axe(axe.Id, AxePass.Back, end, turnPoint, axe.Damage);
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

            if (axe.Pass == AxePass.Back && _returnSlow.Value > 0f)
            {
                enemies.Slow(bestId, MathF.Min(_returnSlow.Value, CrowdControl.MaxSlow), ReturnSlowSeconds);
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

    private void Launch(Vector3 hand, Vector3 direction, float damage)
    {
        Vector3 turnPoint = hand + (direction * MathF.Max(0f, _range.Value));
        int id = ++_nextId;
        _axes[_count] = new Axe(id, AxePass.Out, hand, turnPoint, damage);
        _hitCounts[_count] = 0;
        _count++;
        InHand--;
        _events.Publish(new AxeThrown(id, hand, turnPoint));
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.Y = 0f;
        float distance = direction.Length();
        return distance > 0f ? direction / distance : Vector3.UnitZ;
    }

    private static Vector3 Turn(Vector3 direction, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector3((direction.X * cos) + (direction.Z * sin), 0f, (direction.Z * cos) - (direction.X * sin));
    }

    private static int CarryLimit(float carry) => (int)Math.Clamp(carry, 1f, MaxCarry);
}
