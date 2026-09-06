using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using UnityIs = UnityEngine.TestTools.Constraints.Is;

namespace Soulvail.Tests.Core.Support;

/// <summary>
/// The one way every spec measures "allocates nothing". Use it instead of hand-rolling
/// a GC probe, so every allocation test in the suite agrees on what the claim means.
/// </summary>
/// <remarks>
/// Measured with Unity's GC recorder (<c>AllocatingGCMemoryConstraint</c>) rather than the
/// BCL counters. On this runtime <c>GC.GetAllocatedBytesForCurrentThread()</c> is stubbed to
/// always return 0, and <c>GC.CollectionCount(0)</c> does not move across 100 000 object
/// allocations — both would report "no allocation" for code that allocates freely, which is
/// worse than no test at all. See PROGRESS, M0-02.
/// </remarks>
public static class AllocationAssert
{
    /// <summary>
    /// Runs <paramref name="body"/> once as warm-up, then <paramref name="iterations"/> times
    /// under measurement, and fails if anything was allocated.
    /// </summary>
    /// <remarks>
    /// The warm-up absorbs one-time costs — JIT, static initialisers, a lazily grown buffer —
    /// so only steady-state allocation is reported. Repeating the body catches amortised
    /// allocation that a single call would hide, such as a list doubling its capacity.
    /// </remarks>
    public static void None(Action body, int iterations = 10_000)
    {
        body();

        // Built before measurement starts, so the closure itself is never counted.
        TestDelegate measured = () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                body();
            }
        };

        Assert.That(
            measured,
            UnityIs.Not.AllocatingGCMemory(),
            $"Expected no allocation across {iterations} iterations.");
    }
}
