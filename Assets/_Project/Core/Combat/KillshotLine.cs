using System;
using System.Numerics;

namespace Soulvail.Core.Combat;

/// <summary>Forward XZ corridors use body centres, never colliders or cover senses.</summary>
public static class KillshotLine
{
    public static bool TryPick(ReadOnlySpan<EnemyPoint> enemies, Vector3 player, float halfWidth,
        float length, out Vector3 direction)
    {
        direction = default;
        if (!(halfWidth > 0f) || !float.IsFinite(halfWidth) || !(length > 0f) || !float.IsFinite(length))
        {
            return false;
        }

        int bestCount = 0;
        float bestFirst = float.PositiveInfinity;
        int bestId = int.MaxValue;
        int bestAimId = int.MaxValue;
        for (int i = 0; i < enemies.Length; i++)
        {
            Vector3 delta = enemies[i].Position - player;
            delta.Y = 0f;
            float distance = delta.Length();
            if (!(distance >= 0f && distance <= length))
            {
                continue;
            }

            // A body exactly at the origin supplies no bearing; +Z is a deterministic fallback.
            Vector3 candidate = distance > 0f ? delta / distance : Vector3.UnitZ;
            int count = 0;
            float first = float.PositiveInfinity;
            int firstId = int.MaxValue;
            for (int j = 0; j < enemies.Length; j++)
            {
                if (!Contains(enemies[j].Position, player, candidate, halfWidth, length, out _))
                {
                    continue;
                }

                count++;
                Vector3 offset = enemies[j].Position - player;
                float near = offset.X * offset.X + offset.Z * offset.Z;
                if (near < first || (near == first && enemies[j].Id < firstId))
                {
                    first = near;
                    firstId = enemies[j].Id;
                }
            }

            if (count > bestCount || (count == bestCount && (first < bestFirst
                || (first == bestFirst && (firstId < bestId
                    || (firstId == bestId && enemies[i].Id < bestAimId))))))
            {
                bestCount = count;
                bestFirst = first;
                bestId = firstId;
                bestAimId = enemies[i].Id;
                direction = candidate;
            }
        }

        return bestCount > 0;
    }

    internal static bool Contains(Vector3 point, Vector3 origin, Vector3 direction,
        float halfWidth, float length, out float along)
    {
        float x = point.X - origin.X;
        float z = point.Z - origin.Z;
        along = x * direction.X + z * direction.Z;
        float across = x * direction.Z - z * direction.X;
        return along >= 0f && along <= length && MathF.Abs(across) <= halfWidth;
    }

    /// <summary>Insertion sort into run-owned scratch; equal distances use the enemy id.</summary>
    internal static int Walk(ReadOnlySpan<EnemyPoint> enemies, Vector3 origin, Vector3 direction,
        float halfWidth, float length, Span<EnemyPoint> ordered)
    {
        int count = 0;
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyPoint point = enemies[i];
            if (!Contains(point.Position, origin, direction, halfWidth, length, out float along))
            {
                continue;
            }

            int at = count;
            while (at > 0)
            {
                EnemyPoint previous = ordered[at - 1];
                Contains(previous.Position, origin, direction, halfWidth, length, out float prior);
                if (prior < along || (prior == along && previous.Id < point.Id))
                {
                    break;
                }

                ordered[at] = previous;
                at--;
            }

            ordered[at] = point;
            count++;
        }

        return count;
    }
}
