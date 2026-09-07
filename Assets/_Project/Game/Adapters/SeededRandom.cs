using System;
using Soulvail.Core.Ports;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The adapter behind <see cref="IRandom"/>: five independent PCG32 generators, all derived
/// from one seed. Same seed in, same sequences out, on every stream. See ADR-0011.
/// </summary>
/// <remarks>
/// <para>
/// Each stream's generator is seeded from <c>(seed, streamIndex)</c> run through SplitMix64,
/// not from the raw seed. Seeding several generators with adjacent values correlates their
/// early output; SplitMix64 is an avalanche mix, so one bit of difference in the input scatters
/// the whole 64-bit state and the streams start unrelated.
/// </para>
/// <para>
/// Stream indices are fixed — Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4 — and must never be
/// reordered or renumbered. They are part of what a seed means: changing them changes every
/// stream a seeded run produces, which would invalidate every recorded Daily and bug repro.
/// A new stream takes the next free index.
/// </para>
/// <para>
/// Pure C# despite living in <c>Soulvail.Game</c> — it is an adapter, so it sits on the Unity
/// side of the hexagon by role, not by dependency. It references nothing from the engine, and
/// deliberately not <c>UnityEngine.Random</c>, which is static, global, and unseedable per run.
/// </para>
/// </remarks>
public sealed class SeededRandom : IRandom
{
    private const int SpawnIndex = 0;
    private const int OffersIndex = 1;
    private const int AffixesIndex = 2;
    private const int DropsIndex = 3;
    private const int MiscIndex = 4;

    public SeededRandom(int seed)
    {
        Seed = seed;
        Spawn = CreateStream(seed, SpawnIndex);
        Offers = CreateStream(seed, OffersIndex);
        Affixes = CreateStream(seed, AffixesIndex);
        Drops = CreateStream(seed, DropsIndex);
        Misc = CreateStream(seed, MiscIndex);
    }

    public int Seed { get; }

    public IRandomStream Spawn { get; }

    public IRandomStream Offers { get; }

    public IRandomStream Affixes { get; }

    public IRandomStream Drops { get; }

    public IRandomStream Misc { get; }

    /// <summary>
    /// Builds one stream's generator from the run seed and the stream's fixed index.
    /// </summary>
    /// <remarks>
    /// Two SplitMix64 draws: the first becomes the generator's state, the second its increment.
    /// PCG's increment selects which of 2^63 distinct sequences the generator walks, so deriving
    /// it per stream separates the streams by sequence as well as by position within one.
    /// </remarks>
    private static Pcg32 CreateStream(int seed, int streamIndex)
    {
        // The seed occupies the high half and the index the low half, so no (seed, index) pair
        // can collide with another before the mix ever runs.
        ulong material = ((ulong)(uint)seed << 32) | (uint)streamIndex;

        ulong initialState = SplitMix64(ref material);
        ulong initialSequence = SplitMix64(ref material);

        return new Pcg32(initialState, initialSequence);
    }

    /// <summary>
    /// Advances <paramref name="state"/> and returns its avalanche mix. Used only to derive
    /// generator seeds, never to produce game values.
    /// </summary>
    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// A PCG32 generator (XSH-RR): a 64-bit LCG whose output is permuted down to 32 bits.
    /// Small state, no allocation, and far better distribution than the xorshift it replaces.
    /// </summary>
    private sealed class Pcg32 : IRandomStream
    {
        /// <summary>2^-24. <see cref="NextFloat"/> keeps 24 bits — every one a float can hold exactly.</summary>
        private const float FloatUnit = 1.0f / 16777216.0f;

        private const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Selects which sequence this generator walks. Always odd, or the LCG's period collapses.</summary>
        private readonly ulong _increment;

        private ulong _state;

        public Pcg32(ulong initialState, ulong initialSequence)
        {
            _increment = (initialSequence << 1) | 1UL;

            // The canonical PCG seeding routine: step, absorb the state, step again. Absorbing
            // between two steps stops a low-entropy seed from showing through in the first draw.
            _state = 0UL;
            NextUInt();
            unchecked
            {
                _state += initialState;
            }

            NextUInt();
        }

        public float NextFloat()
        {
            // Top 24 bits, not the bottom: an LCG's low bits are its weakest.
            return (NextUInt() >> 8) * FloatUnit;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    maxExclusive,
                    $"maxExclusive must be greater than minInclusive ({minInclusive}).");
            }

            // long, then uint: the widest range (int.MinValue..int.MaxValue) is 2^32 - 1, which
            // overflows int but fits uint exactly.
            uint range = (uint)((long)maxExclusive - minInclusive);

            // Lemire's multiply-shift: take the high 32 bits of a 64-bit product to land in
            // [0, range) with one multiply and no division, no branch and no rejection loop.
            // Uniform to within one part in 2^32, which no gameplay roll can perceive.
            uint scaled = (uint)(((ulong)NextUInt() * range) >> 32);

            return minInclusive + (int)scaled;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return minInclusive + ((maxInclusive - minInclusive) * NextFloat());
        }

        public bool Chance(float probability)
        {
            // Unconditional draw. Special-casing 0 and 1 would let a tuning change to a
            // certainty shift every value drawn after it.
            return NextFloat() < probability;
        }

        /// <summary>Advances the generator one step and returns the permuted output.</summary>
        private uint NextUInt()
        {
            unchecked
            {
                ulong previous = _state;
                _state = (previous * Multiplier) + _increment;

                // XSH: xorshift the state down to 32 bits, keeping the high, well-mixed end.
                uint xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);

                // RR: rotate by an amount taken from the top bits, which is what breaks the
                // lattice structure a plain LCG would leave behind.
                int rotation = (int)(previous >> 59);

                return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
            }
        }
    }
}
