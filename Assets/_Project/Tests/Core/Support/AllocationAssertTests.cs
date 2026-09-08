using System;
using NUnit.Framework;

namespace Soulvail.Tests.Core.Support;

/// <summary>
/// Self-test of the measuring instrument. Every later "allocates nothing" claim in the suite
/// rests on <see cref="AllocationAssert"/>, so it has to be shown to fail when it should —
/// whichever strategy it picked on this runtime.
/// </summary>
[TestFixture]
public sealed class AllocationAssertTests
{
    /// <summary>Written by the probed body and read back, so the allocation cannot be optimised away.</summary>
    private object _sink;

    [Test]
    public void AllocationAssert_DetectsAllocation()
    {
        Assert.Throws<AssertionException>(() => AllocationAssert.None(() => _sink = new object()));
        Assert.That(_sink, Is.Not.Null, "Sanity: the allocating body actually ran.");
    }

    [Test]
    public void AllocationAssert_PassesForPureBody()
    {
        Assert.DoesNotThrow(() => AllocationAssert.None(() => Math.Sqrt(2)));
    }
}
