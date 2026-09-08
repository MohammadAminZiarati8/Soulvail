using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// Every later core test asserts through <see cref="RecordingEvents"/>, so the recorder itself
/// has to be shown to record in order and to filter by type — an unverified fake would make
/// every claim resting on it vacuous.
/// </summary>
[TestFixture]
public sealed class RecordingEventsTests
{
    private readonly struct Alpha
    {
        public Alpha(int x)
        {
            X = x;
        }

        public int X { get; }
    }

    private readonly struct Beta
    {
    }

    [Test]
    public void RecordingEvents_RecordsInOrder_AndFiltersByType()
    {
        var events = new RecordingEvents();

        events.Publish(new Alpha(1));
        events.Publish(default(Beta));
        events.Publish(new Alpha(2));

        IReadOnlyList<Alpha> alphas = events.Of<Alpha>();

        Assert.That(events.All.Count, Is.EqualTo(3));
        Assert.That(alphas.Count, Is.EqualTo(2));
        Assert.That(alphas[0].X, Is.EqualTo(1));
        Assert.That(alphas[1].X, Is.EqualTo(2));
        Assert.That(events.Count<Beta>(), Is.EqualTo(1));
    }

    [Test]
    public void RecordingEvents_Single_ThrowsWhenNotExactlyOne()
    {
        var events = new RecordingEvents();
        events.Publish(new Alpha(1));

        Assert.That(events.Single<Alpha>().X, Is.EqualTo(1), "Sanity: one recorded event reads back.");

        events.Publish(new Alpha(2));

        Assert.Throws<InvalidOperationException>(() => events.Single<Alpha>());
    }
}
