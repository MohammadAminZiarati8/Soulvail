using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// The whole of what can be checked without a hand. Every row here is about <em>which</em> pulse is
/// played and <em>how often</em> — the two things a mapping can get wrong — against a vibrator that
/// only writes down what it was asked for.
/// </summary>
/// <remarks>
/// <para>
/// Time is a field, not a clock: the listener takes a <c>Func&lt;float&gt;</c> and these tests hand
/// it one that reads <see cref="_now"/>, so a hundred-millisecond window is exercised in no time at
/// all and never flakes on a slow machine.
/// </para>
/// <para>
/// Nothing here can say whether any of it <em>feels</em> right. There is no phone (M0-20a), the
/// Editor resolves <c>NullVibrator</c>, and every row of the spec's device checklist is deferred to
/// the first hardware session. What these tests do guarantee is that when a device is finally
/// attached, the pulses reaching it are the ones the spec asks for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HapticsListenerTests
{
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    private DomainEventHub _hub;
    private FakeVibrator _vibrator;
    private HapticsSettings _settings;
    private HapticsListener _listener;

    /// <summary>The clock the listener reads, in seconds. Moved by <see cref="Advance"/>.</summary>
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _now = 0f;
        _hub = new DomainEventHub();
        _vibrator = new FakeVibrator();

        // In memory rather than PlayerPrefs-backed: a test that flips this must not leave the
        // developer's own haptics turned off on the machine it ran on.
        _settings = HapticsSettings.InMemory(true);

        _listener = new HapticsListener(_hub, _vibrator, _settings, () => _now);
        _listener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        _listener.Dispose();
        _hub.Dispose();
    }

    /// <summary>Rule 1: a hit that landed on the player is the heaviest pulse in the game.</summary>
    [Test]
    public void PlayerDamaged_HeavyPulse()
    {
        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));
        AssertPulse(_vibrator.Calls[0], 60, 1f);
    }

    /// <summary>
    /// Rule 1: a blocked hit is published like any other and must not be felt. The HUD flashes for
    /// it (M1-17); the hand stays still, or "that hurt" would also fire on the moments it did not.
    /// </summary>
    [Test]
    public void Blocked_NoPulse()
    {
        Publish(new PlayerDamaged(0f, 0f, 1f, 1f, blocked: true));
        Flush();

        Assert.That(_vibrator.Calls, Is.Empty);
    }

    /// <summary>Rule 1: a hit the player landed is the faintest thing in the mapping.</summary>
    [Test]
    public void EnemyDamaged_LightPulse()
    {
        Publish(new EnemyDamaged(1, 12f, 0.5f, killed: false));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));
        AssertPulse(_vibrator.Calls[0], 15, 0.4f);
    }

    /// <summary>Rule 1: a kill sits above a hit and below being hit.</summary>
    [Test]
    public void EnemyDied_MediumPulse()
    {
        Publish(new EnemyDied(1, HuskId, Vector3.Zero));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));
        AssertPulse(_vibrator.Calls[0], 30, 0.7f);
    }

    /// <summary>Rule 1: the dash leaving the ground.</summary>
    [Test]
    public void ChargeStarted_MediumPulse()
    {
        Publish(new ChargeStarted(new Vector2(0f, 1f)));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));
        AssertPulse(_vibrator.Calls[0], 30, 0.6f);
    }

    /// <summary>
    /// Rule 2, and the reason the whole window exists: two events inside one window are one pulse,
    /// and it is the stronger of them — even though the weaker arrived first.
    /// </summary>
    /// <remarks>
    /// This is the case the design is for. Being hit mid-swing publishes an <c>EnemyDamaged</c> and
    /// a <c>PlayerDamaged</c> a few frames apart, and the hit that matters is the one that landed on
    /// the player. An immediate pulse would have spent the window on the tick and swallowed it.
    /// </remarks>
    [Test]
    public void RateLimit_StrongestWins()
    {
        Publish(new EnemyDamaged(1, 12f, 0.5f, killed: false));

        Advance(0.05f);
        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));

        // Mid-window: nothing may have played yet, because nothing yet knows what the strongest
        // event in this window is going to be.
        _listener.Tick();
        Assert.That(_vibrator.Calls, Is.Empty, "The window is still open, so nothing can be sure it is the strongest.");

        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1), "Six hits in a window are one tick, not six.");
        AssertPulse(_vibrator.Calls[0], 60, 1f);
    }

    /// <summary>Rule 2, from the other side: a weaker event arriving mid-window changes nothing.</summary>
    [Test]
    public void RateLimit_WeakerInWindow_Dropped()
    {
        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));

        Advance(0.05f);
        Publish(new EnemyDamaged(1, 12f, 0.5f, killed: false));

        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));
        AssertPulse(_vibrator.Calls[0], 60, 1f);
    }

    /// <summary>Rule 2: once a window has closed, the next event opens its own.</summary>
    [Test]
    public void RateLimit_ResetsAfterWindow()
    {
        Publish(new EnemyDamaged(1, 12f, 0.5f, killed: false));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(1));

        // A hair past the first window, which is where a second pulse becomes allowed.
        Advance(0.01f);
        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));
        Flush();

        Assert.That(_vibrator.Calls, Has.Count.EqualTo(2), "The limit is a window, not a lockout.");
        AssertPulse(_vibrator.Calls[1], 60, 1f);
    }

    /// <summary>
    /// Rule 3: with the toggle off, the vibrator is not called at all — including for a pulse that
    /// was already waiting when it was flipped.
    /// </summary>
    [Test]
    public void Disabled_NoPulses()
    {
        _settings.Enabled = false;

        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));
        Publish(new EnemyDamaged(1, 12f, 0.5f, killed: false));
        Publish(new ChargeStarted(new Vector2(0f, 1f)));
        Flush();

        Assert.That(_vibrator.Calls, Is.Empty);
    }

    /// <summary>
    /// Rule 3 again, at the boundary that only exists because pulses are deferred: turning haptics
    /// off while one is queued must silence it rather than let one last buzz through.
    /// </summary>
    [Test]
    public void DisabledMidWindow_NoPulse()
    {
        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));

        Advance(0.05f);
        _settings.Enabled = false;

        Flush();

        Assert.That(_vibrator.Calls, Is.Empty);
    }

    /// <summary>
    /// Rule 5: disposal releases every subscription, so a run that has ended cannot buzz over the
    /// menu that replaced it.
    /// </summary>
    [Test]
    public void Dispose_Unsubscribes()
    {
        _listener.Dispose();

        Assert.That(_hub.SubscriberCount<PlayerDamaged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemyDamaged>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<EnemyDied>(), Is.Zero);
        Assert.That(_hub.SubscriberCount<ChargeStarted>(), Is.Zero);

        Publish(new PlayerDamaged(0f, 8f, 0.8f, 0f, blocked: false));
        Flush();

        Assert.That(_vibrator.Calls, Is.Empty);

        // TearDown disposes again on purpose: a second dispose is a no-op, which is what lets the
        // scope tear this down after a test has already done it by hand.
    }

    private void Publish<T>(in T evt) where T : struct
    {
        _hub.Publish(in evt);
    }

    /// <summary>Moves the clock the listener reads, in seconds.</summary>
    private void Advance(float seconds)
    {
        _now += seconds;
    }

    /// <summary>Closes the open window and plays whatever it decided on.</summary>
    private void Flush()
    {
        Advance(HapticsListener.WindowSeconds);
        _listener.Tick();
    }

    private static void AssertPulse(FakeVibrator.Call call, int milliseconds, float amplitude)
    {
        Assert.That(call.Milliseconds, Is.EqualTo(milliseconds), "Duration is half of what a pulse means.");
        Assert.That(call.Amplitude, Is.EqualTo(amplitude).Within(0.0001f), "Amplitude is the other half.");
    }

    /// <summary>A vibrator that buzzes nothing and remembers everything.</summary>
    private sealed class FakeVibrator : IVibrator
    {
        private readonly List<Call> _calls = new List<Call>();

        /// <summary>Every pulse asked for, in order. Named for the call rather than for the pulse
        /// because a type called <c>Pulse</c> cannot live beside the method of the same name.</summary>
        public IReadOnlyList<Call> Calls => _calls;

        public void Pulse(int milliseconds, float amplitude01)
        {
            _calls.Add(new Call(milliseconds, amplitude01));
        }

        public readonly struct Call
        {
            public Call(int milliseconds, float amplitude)
            {
                Milliseconds = milliseconds;
                Amplitude = amplitude;
            }

            public int Milliseconds { get; }

            public float Amplitude { get; }
        }
    }
}
