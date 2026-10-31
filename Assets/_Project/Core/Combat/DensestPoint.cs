using System;
using System.Numerics;

namespace Soulvail.Core.Combat;

/// <summary>Chooses an enemy centre using XZ geometry. The input contains living enemies only.</summary>
public static class DensestPoint
{
    public static bool TryPick(
        ReadOnlySpan<EnemyPoint> enemies, Vector3 player, float range, float radius, out Vector3 centre)
    {
        centre = default;
        if (!(range > 0f) || float.IsInfinity(range) || !(radius > 0f) || float.IsInfinity(radius))
        {
            return false;
        }

        int best = -1;
        int bestCount = -1;
        float bestDistance = float.PositiveInfinity;
        float rangeSquared = range * range;
        float radiusSquared = radius * radius;
        for (int i = 0; i < enemies.Length; i++)
        {
            float distance = DistanceSquared(enemies[i].Position, player);
            if (!(distance <= rangeSquared))
            {
                continue;
            }

            int count = 0;
            for (int j = 0; j < enemies.Length; j++)
            {
                if (i != j && DistanceSquared(enemies[i].Position, enemies[j].Position) <= radiusSquared)
                {
                    count++;
                }
            }

            if (count > bestCount || (count == bestCount
                && (distance < bestDistance || (distance == bestDistance && enemies[i].Id < enemies[best].Id))))
            {
                best = i;
                bestCount = count;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return false;
        }

        centre = enemies[best].Position;
        return true;
    }

    private static float DistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}
