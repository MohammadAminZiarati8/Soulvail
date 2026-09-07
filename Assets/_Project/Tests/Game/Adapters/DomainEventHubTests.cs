using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Game.Adapters;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Game.Adapters;

[TestFixture]
public sealed class DomainEventHubTests
{
    private DomainEventHub _hub;

    /// <summary>A payload with a field, so "the handler got the value" is checkable.</summary>
    private readonly struct Alpha
    {
        public Alpha(int x)
        {
            X = x;
        }

        public int X { get; }
    }

    /// <summary>A second, unrelated payload type. Exists only to prove channels do not bleed.</summary>
    private readonly struct Beta
    {
    }

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        // Mirrors RunScope tearing the hub down. Safe even for the test that disposed it already.
        _hub.Dispose();
    }

    [Test]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _hub.Publish(new Alpha(1)));
    }

    [Test]
    public void Subscribe_ThenPublish_HandlerReceivesPayload()
    {
        int seen = 0;
        _hub.Subscribe<Alpha>(evt => seen = evt.X);

        _hub.Publish(new Alpha(3));

        Assert.That(seen, Is.EqualTo(3));
    }

    [Test]
    public void Publish_InvokesSubscribersInOrder()
    {
        var log = new List<string>();
        _hub.Subscribe<Alpha>(_ => log.Add("h1"));
        _hub.Subscribe<Alpha>(_ => log.Add("h2"));

        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h1", "h2" }));
    }

    [Test]
    public void Publish_OtherType_NotInvoked()
    {
        bool called = false;
        _hub.Subscribe<Alpha>(_ => called = true);

        _hub.Publish(default(Beta));

        Assert.That(called, Is.False);
    }

    [Test]
    public void DisposeHandle_RemovesOnlyThatHandler()
    {
        var log = new List<string>();
        IDisposable handleOne = _hub.Subscribe<Alpha>(_ => log.Add("h1"));
        _hub.Subscribe<Alpha>(_ => log.Add("h2"));

        handleOne.Dispose();
        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h2" }));
        Assert.That(_hub.SubscriberCount<Alpha>(), Is.EqualTo(1));
    }

    [Test]
    public void DisposeHandle_Twice_IsSafe()
    {
        IDisposable handle = _hub.Subscribe<Alpha>(_ => { });
        handle.Dispose();

        Assert.DoesNotThrow(() => handle.Dispose());
        Assert.That(_hub.SubscriberCount<Alpha>(), Is.Zero);
    }

    [Test]
    public void SubscribeDuringPublish_TakesEffectAfter()
    {
        var log = new List<string>();
        Action<Alpha> second = _ => log.Add("h2");
        bool added = false;

        _hub.Subscribe<Alpha>(_ =>
        {
            log.Add("h1");
            if (!added)
            {
                added = true;
                _hub.Subscribe(second);
            }
        });

        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h1" }), "The in-flight publish must not see the new subscriber.");

        log.Clear();
        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h1", "h2" }));
    }

    [Test]
    public void UnsubscribeDuringPublish_StillReceivesCurrent()
    {
        var log = new List<string>();
        Action<Alpha> second = _ => log.Add("h2");
        IDisposable handleTwo = null;
        bool dropped = false;

        _hub.Subscribe<Alpha>(_ =>
        {
            log.Add("h1");
            if (!dropped)
            {
                dropped = true;
                handleTwo.Dispose();
            }
        });
        handleTwo = _hub.Subscribe(second);

        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h1", "h2" }), "The list was frozen when this publish began.");

        log.Clear();
        _hub.Publish(default(Alpha));

        Assert.That(log, Is.EqualTo(new[] { "h1" }));
    }

    [Test]
    public void HandlerThrows_LaterHandlersStillRun_ExceptionRethrown()
    {
        bool laterRan = false;
        _hub.Subscribe<Alpha>(_ => throw new InvalidOperationException("boom"));
        _hub.Subscribe<Alpha>(_ => laterRan = true);

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => _hub.Publish(default(Alpha)));

        Assert.That(laterRan, Is.True, "A broken listener must not silence the ones after it.");
        Assert.That(thrown.Message, Is.EqualTo("boom"));
    }

    [Test]
    public void HubDispose_ClearsAll_PublishNoOp_SubscribeThrows()
    {
        bool called = false;
        _hub.Subscribe<Alpha>(_ => called = true);

        _hub.Dispose();

        Assert.That(_hub.SubscriberCount<Alpha>(), Is.Zero);
        Assert.DoesNotThrow(() => _hub.Publish(default(Alpha)));
        Assert.That(called, Is.False);
        Assert.Throws<ObjectDisposedException>(() => _hub.Subscribe<Alpha>(_ => { }));
    }

    [Test]
    public void Publish_AfterWarmup_AllocatesNothing()
    {
        int received = 0;
        _hub.Subscribe<Alpha>(_ => received++);
        var evt = new Alpha(7);

        // AllocationAssert runs the body once as warm-up before it starts measuring, so the
        // channel and the delegate call site are both already hot. Any allocation left is the
        // publish itself — a boxed payload above all.
        AllocationAssert.None(() => _hub.Publish(evt));

        Assert.That(received, Is.GreaterThan(0), "Sanity: the measured body actually published.");
    }
}
