using System;
using Soulvail.Core.Ports;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// The <see cref="IClock"/> core tests read: it starts where it is told and moves only when told,
/// so a fixture can say <em>"written three days ago"</em> without sleeping.
/// </summary>
/// <remarks>
/// <para>
/// The <c>FixedRandom</c> trade, for the same reason: a test that depends on wall-clock time has
/// to own it, or the thing it asserts is a property of the machine running it. A migration
/// fixture stamped with a real <c>DateTimeOffset.UtcNow</c> is a test that ages.
/// </para>
/// <para>
/// <see cref="Advance"/> accepts a negative span on purpose. <see cref="IClock"/> promises nothing
/// about monotonicity — a date change, DST, or an NTP correction all move a device clock
/// backwards — so the code that will eventually subtract two timestamps needs that case to be
/// reachable rather than hypothetical.
/// </para>
/// </remarks>
public sealed class FixedClock : IClock
{
    /// <param name="start">The instant this clock reads until it is told otherwise.</param>
    public FixedClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    /// <inheritdoc />
    /// <remarks>Whatever it was last set to or advanced to. Nothing moves it on its own.</remarks>
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Jumps to <paramref name="instant"/>, wherever the clock was before.</summary>
    public void Set(DateTimeOffset instant)
    {
        UtcNow = instant;
    }

    /// <summary>
    /// Moves the clock by <paramref name="by"/>. A negative span is legal and does not throw.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        UtcNow = UtcNow.Add(by);
    }
}
