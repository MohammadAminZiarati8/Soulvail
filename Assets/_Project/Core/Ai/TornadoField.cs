using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

public readonly struct TornadoReading
{
    public readonly int Id;
    public readonly int OwnerId;
    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly bool Collapsing;

    internal TornadoReading(int id, int ownerId, Vector3 position, float radius, bool collapsing)
    {
        Id = id;
        OwnerId = ownerId;
        Position = position;
        Radius = radius;
        Collapsing = collapsing;
    }
}

/// <summary>Two arena slots shared by channel reservations and risen tornadoes.</summary>
public sealed class TornadoField
{
    public const int Capacity = 2;
    public const float PulseSeconds = 0.5f;

    private struct Slot
    {
        public int OwnerId;
        public int Id;
        public StormCallSpec Spec;
        public Vector3 Position;
        public Vector3 Heading;
        public float RoseAt;
        public double Life;
        public double PulseTime;
        public bool Collapsing;
        public float CollapseUntil;
    }

    private readonly Slot[] _slots = new Slot[Capacity];
    private readonly IDomainEvents _events;
    private PlayerMotor _motor;
    private int _nextId;

    internal TornadoField(IDomainEvents events) => _events = events;

    internal int Count { get; private set; }

    internal TornadoReading At(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        foreach (Slot slot in _slots)
        {
            if (slot.Id != 0 && index-- == 0)
            {
                return new TornadoReading(slot.Id, slot.OwnerId, slot.Position, slot.Spec.Radius, slot.Collapsing);
            }
        }

        throw new InvalidOperationException("Tornado count is out of sync with the field.");
    }

    internal void AttachMotor(PlayerMotor motor) => _motor = motor;

    internal bool TryReserve(int ownerId)
    {
        if (FindOwner(ownerId) >= 0)
        {
            return false;
        }

        for (int i = 0; i < Capacity; i++)
        {
            if (_slots[i].OwnerId == 0)
            {
                _slots[i].OwnerId = ownerId;
                return true;
            }
        }

        return false;
    }

    internal void Release(int ownerId)
    {
        int index = FindOwner(ownerId);
        if (index >= 0 && _slots[index].Id == 0)
        {
            _slots[index] = default;
        }
    }

    internal void Rise(int ownerId, StormCallSpec spec, Vector3 position, Vector3 heading, float now)
    {
        int index = FindOwner(ownerId);
        if (index < 0 || _slots[index].Id != 0)
        {
            throw new InvalidOperationException("A tornado can only rise from its caller's reservation.");
        }

        int id = ++_nextId;
        _slots[index] = new Slot
        {
            OwnerId = ownerId,
            Id = id,
            Spec = spec,
            Position = position,
            Heading = heading,
            RoseAt = now,
            Life = spec.TornadoSeconds,
        };
        Count++;
        _events.Publish(new TornadoRose(id, ownerId, position, spec.Radius, spec.TornadoSeconds));
    }

    internal void Collapse(int ownerId, float now)
    {
        int index = FindOwner(ownerId);
        if (index < 0)
        {
            return;
        }

        ref Slot slot = ref _slots[index];
        if (slot.Id == 0)
        {
            slot = default;
        }
        else if (!slot.Collapsing)
        {
            slot.Collapsing = true;
            slot.CollapseUntil = now + slot.Spec.CollapseSeconds;
        }
    }

    internal void Tick(in EnemyTickContext ctx, Vector3 hero)
    {
        Span<Vector2> pullOffsets = stackalloc Vector2[Capacity];
        Vector2 displacement = Vector2.Zero;
        int pulls = 0;
        for (int i = 0; i < Capacity; i++)
        {
            ref Slot slot = ref _slots[i];
            if (slot.OwnerId == 0)
            {
                continue;
            }

            // Also covers scripted despawns and deaths caused by reducing MaxHp to zero.
            if (!ctx.Enemies.Registry.TryGet(slot.OwnerId, out EnemyAgent owner) || !owner.IsAlive)
            {
                Collapse(slot.OwnerId, ctx.Now);
            }

            if (slot.Id == 0)
            {
                continue;
            }

            if (slot.Collapsing)
            {
                if (ctx.Now >= slot.CollapseUntil)
                {
                    End(ref slot, owner, ctx.Now);
                }
            }
            else if (ctx.Now > slot.RoseAt && ctx.Dt > 0f)
            {
                Vector2 pull = Advance(ref slot, ctx, hero, out Vector2 offset);
                if (pull != Vector2.Zero)
                {
                    displacement += pull;
                    pullOffsets[pulls++] = offset;
                }

                if (slot.Life <= 0d)
                {
                    End(ref slot, owner, ctx.Now);
                }
            }
        }

        if (pulls > 0 && _motor is not null)
        {
            // Overlapping pulls can add up to more than either centre allows on its own.
            float scale = 1f;
            foreach (Vector2 offset in pullOffsets[..pulls])
            {
                float toward = Vector2.Dot(displacement, offset);
                float limit = offset.LengthSquared();
                if (toward > limit)
                {
                    scale = MathF.Min(scale, limit / toward);
                }
            }

            _motor.AddDrift(displacement * (scale / ctx.Dt));
        }
    }

    internal void Clear()
    {
        Array.Clear(_slots, 0, Capacity);
        Count = 0;
        _motor?.ClearDrift();
    }

    private static Vector2 Advance(ref Slot slot, in EnemyTickContext ctx, Vector3 hero, out Vector2 pullOffset)
    {
        StormCallSpec spec = slot.Spec;
        double activeSeconds = Math.Min(slot.Life, ctx.Dt);
        float step = (float)activeSeconds;
        Vector3 offset = hero - slot.Position;
        offset.Y = 0f;
        if (offset.LengthSquared() > 0f)
        {
            slot.Heading = PlayerMotor.RotateTowards(slot.Heading, Vector3.Normalize(offset),
                spec.TurnDegreesPerSecond * step);
        }
        Vector3 velocity = slot.Heading * spec.DriftSpeed;
        Vector3 from = slot.Position;
        slot.Position += velocity * step;
        slot.Life -= activeSeconds;

        bool spaceMove = ctx.Player.Charge.IsActive;
        double pulseAt = PulseSeconds - slot.PulseTime;
        slot.PulseTime += activeSeconds;
        while (slot.PulseTime >= PulseSeconds)
        {
            slot.PulseTime -= PulseSeconds;
            // A long step can carry the funnel into or out of the ring between pulses.
            Vector3 pulseOffset = from + velocity * (float)pulseAt - hero;
            pulseOffset.Y = 0f;
            if (!spaceMove && pulseOffset.LengthSquared() <= spec.Radius * spec.Radius)
            {
                ctx.Player.ApplyPulse(spec.DamagePerSecond * PulseSeconds, ctx.Now);
            }

            pulseAt += PulseSeconds;
        }

        pullOffset = new Vector2(slot.Position.X - hero.X, slot.Position.Z - hero.Z);
        float distance = pullOffset.Length();
        if (!spaceMove && distance > 0f && distance <= spec.PullRadius)
        {
            return pullOffset * (MathF.Min(spec.PullSpeed * step, distance) / distance);
        }

        return Vector2.Zero;
    }

    private void End(ref Slot slot, EnemyAgent owner, float now)
    {
        var ended = new TornadoEnded(slot.Id, slot.OwnerId, slot.Collapsing);
        if (owner is not null && owner.Behaviour is StormCallerBehaviour caller)
        {
            caller.TornadoEnded(now);
        }

        slot = default;
        Count--;
        _events.Publish(ended);
    }

    private int FindOwner(int ownerId)
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (_slots[i].OwnerId == ownerId)
            {
                return i;
            }
        }

        return -1;
    }
}
