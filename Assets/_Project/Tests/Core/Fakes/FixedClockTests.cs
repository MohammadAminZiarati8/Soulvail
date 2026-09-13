using System;
using NUnit.Framework;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// Every later test that says <em>"this was written three days ago"</em> says it through
/// <see cref="FixedClock"/>, so the fake itself has to be shown to sit still and to move exactly
/// as far as it is told — an unverified fake would make every claim resting on it vacuous. The
/// <c>FixedRandomTests</c> precedent.
/// </summary>
[TestFixture]
public sealed class FixedClockTests
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void FixedClock_StartsWhereTold()
    {
        var clock = new FixedClock(Start);

        Assert.That(clock.UtcNow, Is.EqualTo(Start));

        // Read twice, because "moves only when told" is the whole point: a fake that quietly
        // ticked would make a migration fixture flaky in a way nothing would attribute to it.
        Assert.That(clock.UtcNow, Is.EqualTo(Start));
    }

    [Test]
    public void FixedClock_AdvanceMoves()
    {
        var clock = new FixedClock(Start);

        clock.Advance(TimeSpan.FromDays(3));

        Assert.That(clock.UtcNow, Is.EqualTo(Start.AddDays(3)));
    }

    [Test]
    public void FixedClock_AdvanceBackwards()
    {
        var clock = new FixedClock(Start);

        // Legal, and the reason it is legal is rule 4: IClock promises nothing about
        // monotonicity, so the backwards-clock case has to be reachable from a test. Whoever
        // subtracts two timestamps (M2-13, M2-14) owes the negative-difference branch, and this
        // is what lets them write it against something rather than imagine it.
        Assert.That(() => clock.Advance(TimeSpan.FromHours(-1)), Throws.Nothing);

        Assert.That(clock.UtcNow, Is.EqualTo(Start.AddHours(-1)));
    }

    [Test]
    public void FixedClock_SetReplaces()
    {
        var clock = new FixedClock(Start);
        clock.Advance(TimeSpan.FromDays(3));

        DateTimeOffset instant = Start.AddYears(1);
        clock.Set(instant);

        // Replaces, never accumulates: Set after an Advance lands on exactly what it was given.
        Assert.That(clock.UtcNow, Is.EqualTo(instant));
    }
}
