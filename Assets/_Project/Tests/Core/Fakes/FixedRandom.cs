using System;
using Soulvail.Core.Ports;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// The <see cref="IRandom"/> core tests draw from: every value is scripted, so "a 5 % drop
/// happened" is a fact of the test rather than something it waits for.
/// </summary>
/// <remarks>
/// <para>
/// All five streams are the same scripted stream until one is set individually, which is what
/// makes the common case — a test that cares about one roll — a one-liner. Because it is one
/// instance, a draw from <see cref="Spawn"/> also advances <see cref="Offers"/>; override a
/// stream when a test needs them to move independently.
/// </para>
/// <para>
/// Scripted values run out rather than repeat: past the end every stream returns 0.5f forever.
/// A test that under-scripts therefore gets a defined middling value instead of an exception or
/// a wrapped-around replay, so the failure it reports is about the system, not the fake.
/// </para>
/// </remarks>
public sealed class FixedRandom : IRandom
{
    /// <summary>What every stream returns once its scripted values are spent.</summary>
    private const float DefaultValue = 0.5f;

    private readonly ScriptedStream _shared;

    private ScriptedStream _spawn;
    private ScriptedStream _offers;
    private ScriptedStream _affixes;
    private ScriptedStream _drops;
    private ScriptedStream _misc;

    /// <summary>
    /// Scripts every stream with <paramref name="floats"/>, returned in order by
    /// <see cref="IRandomStream.NextFloat"/>, then <see cref="DefaultValue"/> forever.
    /// </summary>
    public FixedRandom(params float[] floats)
    {
        _shared = new ScriptedStream(floats);
    }

    /// <summary>Always 0. A fake has no seed to reproduce; nothing in the suite reads this.</summary>
    public int Seed => 0;

    public IRandomStream Spawn => _spawn ?? _shared;

    public IRandomStream Offers => _offers ?? _shared;

    public IRandomStream Affixes => _affixes ?? _shared;

    public IRandomStream Drops => _drops ?? _shared;

    public IRandomStream Misc => _misc ?? _shared;

    /// <summary>Scripts <see cref="Spawn"/> on its own. Returns this, so setters chain.</summary>
    public FixedRandom SetSpawn(params float[] floats)
    {
        _spawn = new ScriptedStream(floats);
        return this;
    }

    /// <summary>Scripts <see cref="Offers"/> on its own. Returns this, so setters chain.</summary>
    public FixedRandom SetOffers(params float[] floats)
    {
        _offers = new ScriptedStream(floats);
        return this;
    }

    /// <summary>Scripts <see cref="Affixes"/> on its own. Returns this, so setters chain.</summary>
    public FixedRandom SetAffixes(params float[] floats)
    {
        _affixes = new ScriptedStream(floats);
        return this;
    }

    /// <summary>Scripts <see cref="Drops"/> on its own. Returns this, so setters chain.</summary>
    public FixedRandom SetDrops(params float[] floats)
    {
        _drops = new ScriptedStream(floats);
        return this;
    }

    /// <summary>Scripts <see cref="Misc"/> on its own. Returns this, so setters chain.</summary>
    public FixedRandom SetMisc(params float[] floats)
    {
        _misc = new ScriptedStream(floats);
        return this;
    }

    /// <summary>One scripted sequence, replayed once and then exhausted.</summary>
    private sealed class ScriptedStream : IRandomStream
    {
        private readonly float[] _values;

        private int _index;

        public ScriptedStream(float[] values)
        {
            _values = values ?? Array.Empty<float>();
        }

        public float NextFloat()
        {
            return _index < _values.Length ? _values[_index++] : DefaultValue;
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

            int scaled = minInclusive + (int)(NextFloat() * (maxExclusive - minInclusive));

            // Clamped, because a script is hand-written: a stray 1.0f (or a negative) would
            // otherwise hand the system under test an out-of-range index and fail somewhere
            // far from the mistake.
            if (scaled < minInclusive)
            {
                return minInclusive;
            }

            return scaled > maxExclusive - 1 ? maxExclusive - 1 : scaled;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return minInclusive + ((maxInclusive - minInclusive) * NextFloat());
        }

        public bool Chance(float probability)
        {
            // Same rule as the real generator, including the unconditional draw, so swapping
            // SeededRandom for this one never shifts how many values a system consumes.
            return NextFloat() < probability;
        }
    }
}
