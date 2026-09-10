using System;
using System.Threading;
using NUnit.Framework;
using Soulvail.Game.Adapters;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The adapter forwards the system clock and caches nothing. Three rows, because there is only
/// one thing it can get wrong — and that one thing, a value read once and reused, would give two
/// saves written in one session the same timestamp.
/// </summary>
[TestFixture]
public sealed class UnityClockTests
{
    [Test]
    public void UtcNow_IsUtc()
    {
        var clock = new UnityClock();

        // Zero offset, not "the local offset happens to be zero on this machine": a stamp carrying
        // a local offset would read as a different instant on a phone that had travelled.
        Assert.That(clock.UtcNow.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void UtcNow_AgreesWithSystemClock()
    {
        var clock = new UnityClock();

        DateTimeOffset reference = DateTimeOffset.UtcNow;
        DateTimeOffset read = clock.UtcNow;

        // Five seconds is slack for a loaded test machine, not precision: the row exists to catch
        // a clock that is off by an epoch — a wrong zero, a local time, a Unix stamp — not one
        // that is off by a millisecond.
        Assert.That(
            (read - reference).Duration(),
            Is.LessThan(TimeSpan.FromSeconds(5)),
            "UnityClock must read the system clock, not some other origin.");
    }

    [Test]
    public void UtcNow_IsNotCached()
    {
        var clock = new UnityClock();

        DateTimeOffset first = clock.UtcNow;

        // Well above the ~16 ms the Windows system clock ticks at, so the assertion is about the
        // adapter rather than about timer resolution.
        Thread.Sleep(50);

        Assert.That(
            clock.UtcNow,
            Is.GreaterThan(first),
            "A cached instant would stamp every save in a session with the same moment.");
    }
}
