using System;
using System.Numerics;

namespace Soulvail.Core.Ai;

/// <summary>BT-04: where each animal stands out of a fight, and how it gets there.</summary>
/// <remarks>
/// Pure math on the XZ plane (X right, Y forward). The slot frame turns with a lagged copy of his
/// body facing, so a flick of the stick does not swing the pack around.
/// </remarks>
public sealed class PackFormation
{
    public const float DeadZone = 0.5f;
    public const float Spacing = 1.1f;
    public const float TurnDegreesPerSecond = 240f;
    public const int SlotCount = 4;

    private const float ArrivalGain = 2.5f;
    private const float MaxSeparationSpeed = 1.5f;
    private const float TurnRadiansPerSecond = TurnDegreesPerSecond * MathF.PI / 180f;

    public PackFormation() => Facing = Vector2.UnitY;

    /// <summary>The slot frame's forward; lags the body facing at <see cref="TurnDegreesPerSecond"/>.</summary>
    public Vector2 Facing { get; private set; }

    /// <summary>0 right · 1 left · 2 behind · 3 behind-left, in the frame of <see cref="Facing"/>.</summary>
    public static Vector2 SlotOffset(int slot) => slot switch
    {
        0 => new Vector2(1.6f, 0f),
        1 => new Vector2(-1.6f, 0f),
        2 => new Vector2(0f, -1.8f),
        3 => new Vector2(-1.2f, -1.6f),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "The pack has four slots."),
    };

    /// <summary>A slot's offset from him in world XZ, turned by the lagged facing.</summary>
    public Vector2 WorldOffset(int slot)
    {
        Vector2 local = SlotOffset(slot);
        Vector2 forward = Facing;
        var right = new Vector2(forward.Y, -forward.X);
        return right * local.X + forward * local.Y;
    }

    /// <summary>Snaps the frame to a facing, for a fresh spawn. A zero facing keeps the current one.</summary>
    internal void Reset(Vector2 facing)
    {
        if (TryNormalize(facing, out Vector2 unit))
        {
            Facing = unit;
        }
    }

    public void Tick(float dt, Vector2 bodyFacing)
    {
        if (!(dt > 0f) || !TryNormalize(bodyFacing, out Vector2 target))
        {
            return;
        }

        Vector2 current = Facing;
        float angle = MathF.Atan2(Cross(current, target), Vector2.Dot(current, target));
        float step = TurnRadiansPerSecond * dt;
        if (MathF.Abs(angle) <= step)
        {
            Facing = target;
            return;
        }

        float turn = angle < 0f ? -step : step;
        float cos = MathF.Cos(turn);
        float sin = MathF.Sin(turn);
        Facing = Vector2.Normalize(new Vector2(current.X * cos - current.Y * sin, current.X * sin + current.Y * cos));
    }

    /// <summary>Eased speed into a slot: <c>min(max, 2.5 × distance)</c>, never negative.</summary>
    public static float ArrivalSpeed(float distance, float maxSpeed)
    {
        if (!(distance > 0f) || !(maxSpeed > 0f))
        {
            return 0f;
        }

        return MathF.Min(maxSpeed, ArrivalGain * distance);
    }

    /// <summary>
    /// The push (m/s, ≤ 1.5) that moves <paramref name="self"/> off every other position closer than
    /// <see cref="Spacing"/>. The caller passes only standing, grounded animals.
    /// </summary>
    public static Vector2 Separation(ReadOnlySpan<Vector2> positions, int self)
    {
        if ((uint)self >= (uint)positions.Length)
        {
            return Vector2.Zero;
        }

        Vector2 from = positions[self];
        Vector2 push = Vector2.Zero;
        for (int other = 0; other < positions.Length; other++)
        {
            if (other == self)
            {
                continue;
            }

            Vector2 away = from - positions[other];
            float distance = away.Length();
            if (!(distance < Spacing))
            {
                continue;
            }

            // Stacked exactly: split along X by index so the two go opposite ways.
            Vector2 direction = distance > 0.0001f ? away / distance
                : new Vector2(self > other ? 1f : -1f, 0f);
            push += direction * (MaxSeparationSpeed * (Spacing - distance) / Spacing);
        }

        float length = push.Length();
        return length > MaxSeparationSpeed ? push * (MaxSeparationSpeed / length) : push;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool TryNormalize(Vector2 value, out Vector2 unit)
    {
        float length = value.Length();
        if (!(length > 0.0001f) || float.IsInfinity(length))
        {
            unit = default;
            return false;
        }

        unit = value / length;
        return true;
    }
}
