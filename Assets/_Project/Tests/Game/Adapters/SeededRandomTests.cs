using System;
using NUnit.Framework;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Game.Adapters;

[TestFixture]
public sealed class SeededRandomTests
{
    /// <summary>
    /// Enough draws that a distribution fault shows up, few enough that the fixture stays fast.
    /// </summary>
    private const int BulkDraws = 100_000;

    [Test]
    public void SameSeed_SameSequence_EveryStream()
    {
        IRandomStream[] first = StreamsOf(new SeededRandom(42));
        IRandomStream[] second = StreamsOf(new SeededRandom(42));

        for (int i = 0; i < first.Length; i++)
        {
            // Exact equality, deliberately: reproducing a seeded run means bit-for-bit, not close.
            Assert.That(
                Draw(first[i], 100),
                Is.EqualTo(Draw(second[i], 100)),
                $"Stream {i} diverged between two generators built from the same seed.");
        }
    }

    [Test]
    public void DifferentSeed_DifferentSequence()
    {
        float[] fromOne = Draw(new SeededRandom(1).Spawn, 100);
        float[] fromTwo = Draw(new SeededRandom(2).Spawn, 100);

        // Adjacent seeds are the hard case — without the SplitMix64 mix they would correlate.
        Assert.That(fromOne, Is.Not.EqualTo(fromTwo));
    }

    [Test]
    public void Streams_AreIndependent()
    {
        var untouched = new SeededRandom(99);
        var drained = new SeededRandom(99);

        // Stands in for a later mechanic that starts consuming spawn rolls.
        Draw(drained.Spawn, 50);

        Assert.That(
            Draw(drained.Offers, 20),
            Is.EqualTo(Draw(untouched.Offers, 20)),
            "Draining one stream must leave every other stream's sequence untouched.");
    }

    [Test]
    public void NextFloat_InUnitInterval()
    {
        IRandomStream stream = new SeededRandom(5).Spawn;
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (int i = 0; i < BulkDraws; i++)
        {
            float value = stream.NextFloat();
            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
        }

        Assert.That(lowest, Is.GreaterThanOrEqualTo(0f));
        Assert.That(highest, Is.LessThan(1f), "1 is excluded, or Range and NextInt can overshoot.");
    }

    [Test]
    public void NextInt_WithinBounds_AndHitsBothEnds()
    {
        IRandomStream stream = new SeededRandom(6).Spawn;
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        bool sawLow = false;
        bool sawHigh = false;

        for (int i = 0; i < BulkDraws; i++)
        {
            int value = stream.NextInt(3, 6);
            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
            sawLow |= value == 3;
            sawHigh |= value == 5;
        }

        Assert.That(lowest, Is.EqualTo(3));
        Assert.That(highest, Is.EqualTo(5), "maxExclusive is excluded.");
        Assert.That(sawLow, Is.True, "The lowest value must be reachable.");
        Assert.That(sawHigh, Is.True, "The highest value must be reachable.");
    }

    [Test]
    public void NextInt_InvalidRange_Throws()
    {
        IRandomStream stream = new SeededRandom(7).Spawn;

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(5, 5));
    }

    [Test]
    public void Range_WithinBounds()
    {
        IRandomStream stream = new SeededRandom(8).Spawn;
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (int i = 0; i < 10_000; i++)
        {
            float value = stream.Range(-2f, 2f);
            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
        }

        Assert.That(lowest, Is.GreaterThanOrEqualTo(-2f));
        Assert.That(highest, Is.LessThanOrEqualTo(2f));
    }

    [Test]
    public void Chance_ZeroNeverOneAlways()
    {
        IRandomStream never = new SeededRandom(9).Spawn;
        IRandomStream always = new SeededRandom(9).Offers;
        bool anyTrue = false;
        bool anyFalse = false;

        for (int i = 0; i < 1_000; i++)
        {
            anyTrue |= never.Chance(0f);
            anyFalse |= !always.Chance(1f);
        }

        Assert.That(anyTrue, Is.False, "NextFloat is never negative, so p = 0 cannot succeed.");
        Assert.That(anyFalse, Is.False, "NextFloat is always below 1, so p = 1 cannot fail.");
    }

    [Test]
    public void Chance_ConsumesOneDraw()
    {
        IRandomStream reference = new SeededRandom(10).Spawn;
        reference.NextFloat();
        float secondValue = reference.NextFloat();

        IRandomStream subject = new SeededRandom(10).Spawn;
        subject.Chance(0.5f);

        // A Chance that skipped the draw when the outcome was certain would desync every
        // seeded run the moment a probability was tuned to 0 or 1.
        Assert.That(subject.NextFloat(), Is.EqualTo(secondValue));
    }

    [Test]
    public void Seed_Roundtrips()
    {
        Assert.That(new SeededRandom(7).Seed, Is.EqualTo(7));
    }

    [Test]
    public void Draw_AllocatesNothing()
    {
        IRandomStream stream = new SeededRandom(11).Spawn;
        float floatSink = 0f;
        int intSink = 0;
        float rangeSink = 0f;
        int chanceSink = 0;

        // Every draw, not only NextFloat: the rule is that no draw allocates, and NextInt is
        // where a boxed comparer or a widening conversion would most easily creep in. The
        // delegates are built before AllocationAssert starts measuring, so they cost nothing here.
        AllocationAssert.None(() => floatSink += stream.NextFloat());
        AllocationAssert.None(() => intSink += stream.NextInt(0, 10));
        AllocationAssert.None(() => rangeSink += stream.Range(-1f, 1f));
        AllocationAssert.None(() => chanceSink += stream.Chance(0.5f) ? 1 : 0);

        Assert.That(floatSink, Is.GreaterThan(0f), "Sanity: the measured bodies actually drew.");
        Assert.That(intSink, Is.GreaterThan(0));
        Assert.That(chanceSink, Is.GreaterThan(0));
        Assert.That(rangeSink, Is.Not.EqualTo(float.NaN));
    }

    [Test]
    public void Capture_Restore_ReplaysTheSameDraws()
    {
        var random = new SeededRandom(7);
        Draw(random.Spawn, 100);

        RandomState captured = random.Capture();
        float[] recorded = Draw(random.Spawn, 100);

        random.Restore(captured);

        // The whole point of the pair, in one row: a run put back where it was draws what it was
        // going to draw. Exact equality, because resuming a seeded run means bit-for-bit.
        Assert.That(Draw(random.Spawn, 100), Is.EqualTo(recorded));
    }

    [Test]
    public void Capture_CoversEveryStream()
    {
        var random = new SeededRandom(7);
        IRandomStream[] streams = StreamsOf(random);

        foreach (IRandomStream stream in streams)
        {
            stream.NextFloat();
        }

        RandomState captured = random.Capture();
        var expected = new float[streams.Length];

        for (int i = 0; i < streams.Length; i++)
        {
            expected[i] = streams[i].NextFloat();
        }

        // Advance all five past the capture, so a Restore that put back only one — or that
        // transposed two — cannot pass by accident.
        foreach (IRandomStream stream in streams)
        {
            Draw(stream, 10);
        }

        random.Restore(captured);

        for (int i = 0; i < streams.Length; i++)
        {
            Assert.That(
                streams[i].NextFloat(),
                Is.EqualTo(expected[i]),
                $"Stream {i} did not come back to where it was captured.");
        }
    }

    [Test]
    public void Restore_IsCompleteAcrossAFreshGenerator()
    {
        var original = new SeededRandom(7);
        Draw(original.Spawn, 500);

        RandomState captured = original.Capture();
        float next = original.Spawn.NextFloat();

        // A different object, built the way a resumed run builds one: the seed from the snapshot
        // through the constructor, the position through Restore. The increment — which selects
        // which of 2^63 sequences each stream walks — is derived from the seed and is never
        // captured, so this is the row that shows position alone is a *complete* restore.
        var resumed = new SeededRandom(7);
        resumed.Restore(captured);

        Assert.That(resumed.Spawn.NextFloat(), Is.EqualTo(next));
    }

    [Test]
    public void Restore_UnderADifferentSeed_DoesNotThrow()
    {
        var seven = new SeededRandom(7);
        Draw(seven.Spawn, 50);
        RandomState fromSeven = seven.Capture();

        var eight = new SeededRandom(8);
        float beforeRestore = new SeededRandom(8).Spawn.NextFloat();

        // Refuses nothing, deliberately: every 64-bit word is a legal generator position, so a
        // guard here would be a check that cannot fail correctly. The agreement between a seed
        // and a state is checkable only where both are visible, which is RunSession.Start.
        Assert.DoesNotThrow(() => eight.Restore(fromSeven));

        // And it did land somewhere else on seed 8's own sequence, so the restore was not a no-op.
        Assert.That(eight.Spawn.NextFloat(), Is.Not.EqualTo(beforeRestore));
    }

    [Test]
    public void Capture_AllocatesNothing()
    {
        var random = new SeededRandom(11);
        ulong sink = 0UL;

        AllocationAssert.None(() => sink += random.Capture().Spawn);

        Assert.That(sink, Is.Not.EqualTo(0UL), "Sanity: the measured body actually captured.");
    }

    [Test]
    public void Restore_AllocatesNothing()
    {
        var random = new SeededRandom(11);
        Draw(random.Spawn, 10);
        RandomState captured = random.Capture();

        // Built outside the measured body: `in` takes it by reference, so nothing is copied and
        // nothing is boxed on the way in.
        AllocationAssert.None(() => random.Restore(captured));

        Assert.That(random.Capture().Spawn, Is.EqualTo(captured.Spawn));
    }

    /// <summary>The five streams in their fixed index order, so a test can sweep all of them.</summary>
    private static IRandomStream[] StreamsOf(IRandom random)
    {
        return new[] { random.Spawn, random.Offers, random.Affixes, random.Drops, random.Misc };
    }

    private static float[] Draw(IRandomStream stream, int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = stream.NextFloat();
        }

        return values;
    }
}
