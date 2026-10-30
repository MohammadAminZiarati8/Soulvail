using System;
using System.Numerics;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

/// <summary>Chooses reported ground in the boss's band, preferring clear spots, with one draw.</summary>
public sealed class OpenGroundPicker
{
    public const float MinDistance = 4f, MaxDistance = 8f, EnemyClearance = 1.5f, PlayerClearance = 3f;

    private readonly IRandomStream _random;

    public OpenGroundPicker(IRandomStream random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public bool TryPick(in EnemyTickContext ctx, Vector3 centre, Vector3 player,
        ReadOnlySpan<Vector3> reserved, out Vector3 spot)
    {
        int band = 0;
        int clear = 0;
        for (int i = 0; i < ctx.OpenGround.Count; i++)
        {
            Vector3 candidate = ctx.OpenGround[i];
            if (!InBand(candidate, centre))
            {
                continue;
            }

            band++;
            if (IsClear(ctx, candidate, player, reserved))
            {
                clear++;
            }
        }

        spot = default;
        if (band == 0)
        {
            return false;
        }

        int index = (int)(_random.NextFloat() * (clear > 0 ? clear : band));
        for (int i = 0; i < ctx.OpenGround.Count; i++)
        {
            Vector3 candidate = ctx.OpenGround[i];
            if (InBand(candidate, centre)
                && (clear == 0 || IsClear(ctx, candidate, player, reserved)))
            {
                if (index-- == 0)
                {
                    spot = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private bool InBand(Vector3 spot, Vector3 centre)
    {
        float distance = DistanceSquared(spot, centre);
        return distance >= MinDistance * MinDistance && distance <= MaxDistance * MaxDistance;
    }

    private bool IsClear(in EnemyTickContext ctx, Vector3 spot, Vector3 player,
        ReadOnlySpan<Vector3> reserved)
    {
        if (!(DistanceSquared(spot, player) >= PlayerClearance * PlayerClearance))
        {
            return false;
        }

        foreach (EnemyAgent enemy in ctx.Enemies.Registry.Alive)
        {
            if (enemy.IsAlive && !(DistanceSquared(spot, enemy.Position) >= EnemyClearance * EnemyClearance))
            {
                return false;
            }
        }

        for (int i = 0; i < reserved.Length; i++)
        {
            if (!(DistanceSquared(spot, reserved[i]) >= EnemyClearance * EnemyClearance))
            {
                return false;
            }
        }

        return true;
    }

    private float DistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;
        return x * x + z * z;
    }
}
