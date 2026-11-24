using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;

namespace Soulvail.Core.Director;

/// <summary>Base XP shares in wave, entry, body and child order.</summary>
internal sealed class StageXpAllocation
{
    private readonly ArraySegment<int>[][] _waves;

    internal StageXpAllocation(WavePlan plan, EnemySystem enemies, int budget)
    {
        if (budget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        _waves = new ArraySegment<int>[plan.WaveCount][];
        var weights = new List<BigInteger>();
        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            for (int entryIndex = 0; entryIndex < plan.EntryCount(wave); entryIndex++)
            {
                var entry = plan.Entry(wave, entryIndex);
                EnemySpec spec = enemies.Enemy(entry.SpecId);
                BigInteger parentWeight = ExactWeight(spec.XpValue);
                BroodSpec brood = spec.Brood;
                BigInteger childWeight = brood is null ? BigInteger.Zero : ExactWeight(enemies.Enemy(brood.Child).XpValue);
                for (int body = 0; body < entry.Count; body++)
                {
                    weights.Add(parentWeight);
                    for (int child = 0; child < (brood?.Count ?? 0); child++)
                    {
                        weights.Add(childWeight);
                    }
                }
            }
        }

        int[] shares = Apportion(weights, budget);
        int offset = 0;
        for (int wave = 1; wave <= plan.WaveCount; wave++)
        {
            var bodies = new ArraySegment<int>[plan.BodyCount(wave)];
            _waves[wave - 1] = bodies;
            int bodyIndex = 0;
            for (int entryIndex = 0; entryIndex < plan.EntryCount(wave); entryIndex++)
            {
                var entry = plan.Entry(wave, entryIndex);
                int size = 1 + enemies.BroodCount(entry.SpecId);
                for (int body = 0; body < entry.Count; body++)
                {
                    bodies[bodyIndex++] = new ArraySegment<int>(shares, offset, size);
                    offset += size;
                }
            }
        }
    }

    internal ArraySegment<int> Body(int wave, int index) => _waves[wave - 1][index];

    private int[] Apportion(List<BigInteger> weights, int budget)
    {
        if (weights.Count == 0)
        {
            return Array.Empty<int>();
        }

        var shares = new int[weights.Count];
        BigInteger total = BigInteger.Zero;
        foreach (BigInteger weight in weights)
        {
            total += weight;
        }

        var remainders = new BigInteger[shares.Length];
        var order = new int[shares.Length];
        int left = budget;
        for (int i = 0; i < shares.Length; i++)
        {
            shares[i] = (int)BigInteger.DivRem(budget * weights[i], total, out remainders[i]);
            order[i] = i;
            left -= shares[i];
        }

        Array.Sort(order, (a, b) =>
        {
            int comparison = remainders[b].CompareTo(remainders[a]);
            return comparison == 0 ? a.CompareTo(b) : comparison;
        });
        for (int i = 0; i < left; i++)
        {
            shares[order[i]]++;
        }

        return shares;
    }

    // Every positive finite float is an integer multiple of 2^-149.
    private BigInteger ExactWeight(float xp)
    {
        int bits = BitConverter.SingleToInt32Bits(xp);
        int exponent = bits >> 23;
        int mantissa = bits & 0x7fffff;
        return exponent == 0 ? mantissa : (BigInteger)(mantissa | 0x800000) << (exponent - 1);
    }
}
