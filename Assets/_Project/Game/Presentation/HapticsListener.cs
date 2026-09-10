using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer.Unity;

namespace Soulvail.Game.Presentation;

/// <summary>
/// Turns four things that happen into four things a thumb feels: a light tick when you hit, a firm
/// thump when you are hit, a shorter one when something dies, a medium one when you Charge. GD
/// §16.3's cheapest game-feel win, with CC §5.6's off switch.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure subscriber, and gameplay never knows it exists.</b> It holds no game state, is asked
/// no questions, and writes no intents — it reads events and calls a vibrator. Deleting this class
/// changes nothing about how the game plays, which is the property that lets it be registered as an
/// entry point and forgotten about.
/// </para>
/// <para>
/// <b>The rate limit is the feature, not a guard.</b> A swing through six Husks publishes six
/// <c>EnemyDamaged</c> events on one frame, and six pulses is not six ticks — it is a phone buzzing
/// like it is face-down on a table. So events are gathered into a window of
/// <see cref="WindowSeconds"/> and exactly one pulse comes out of it: the strongest, on the grounds
/// that amplitude is how this mapping spells importance. The heavy "you were hit" thump can
/// therefore never be swallowed by a light tick that happened to land 20 ms earlier, which is the
/// case worth protecting — being hit is the one haptic in the game that carries information.
/// </para>
/// <para>
/// <b>The cost of that is latency, and it is deliberate but unproven.</b> Nothing can know which
/// event in a window is strongest until the window closes, so every pulse arrives up to
/// <see cref="WindowSeconds"/> after the thing it describes — including an isolated swing, which
/// has nothing to be strongest against. The alternative is to pulse immediately and let a stronger
/// event re-pulse over the top, which is punchier and gives up the property above. Which of the two
/// is right is a question only a thumb can answer, and there is no phone yet: the first hardware
/// session decides, and it is a small change either way.
/// </para>
/// <para>
/// Flushing needs a heartbeat, which is why this is an <see cref="ITickable"/> as well as a
/// listener. The tick does nothing at all while no window is open — an arena in which nothing is
/// happening costs one comparison a frame.
/// </para>
/// </remarks>
public sealed class HapticsListener : IStartable, ITickable, IDisposable
{
    /// <summary>
    /// How long events are gathered before the strongest of them plays. Also the shortest possible
    /// gap between two pulses, since a new window can only open once the last one has closed.
    /// </summary>
    public const float WindowSeconds = 0.1f;

    /// <summary>A hit that landed on the player: the longest and hardest pulse in the game.</summary>
    private const int PlayerDamagedMs = 60;
    private const float PlayerDamagedAmplitude = 1f;

    /// <summary>A hit the player landed. Deliberately the faintest thing here — it happens most.</summary>
    private const int EnemyDamagedMs = 15;
    private const float EnemyDamagedAmplitude = 0.4f;

    /// <summary>A kill. Above a hit, below being hit.</summary>
    private const int EnemyDiedMs = 30;
    private const float EnemyDiedAmplitude = 0.7f;

    /// <summary>The dash leaving the ground.</summary>
    private const int ChargeStartedMs = 30;
    private const float ChargeStartedAmplitude = 0.6f;

    private readonly DomainEventHub _hub;
    private readonly IVibrator _vibrator;
    private readonly HapticsSettings _settings;
    private readonly Func<float> _clock;

    private IDisposable _playerDamagedSubscription;
    private IDisposable _enemyDamagedSubscription;
    private IDisposable _enemyDiedSubscription;
    private IDisposable _chargeStartedSubscription;

    /// <summary>Something is waiting to be played and the window it is in has not closed yet.</summary>
    private bool _windowOpen;

    /// <summary>When the open window began, on the injected clock.</summary>
    private float _windowStart;

    /// <summary>The strongest pulse offered in the open window — the one that will play.</summary>
    private int _pendingMilliseconds;
    private float _pendingAmplitude;

    /// <param name="hub">The run's event hub. Subscribed in <see cref="Start"/>, released in <see cref="Dispose"/>.</param>
    /// <param name="vibrator">Where a pulse goes. <c>NullVibrator</c> anywhere but an Android player build.</param>
    /// <param name="settings">The off switch, shared with whatever eventually flips it.</param>
    /// <param name="clock">
    /// Seconds from some fixed point, monotonic — <c>Time.realtimeSinceStartup</c> in a run, and
    /// whatever a test says otherwise. Real time rather than the run's own clock on purpose: this
    /// measures how a hand feels a sequence of buzzes, which does not slow down with a hit-stop or
    /// stop with a pause. Null falls back to the Unity clock, and <c>RunScope</c> passes one anyway
    /// — VContainer resolves every parameter and never falls back to a C# default.
    /// </param>
    /// <exception cref="ArgumentNullException">The hub, the vibrator or the settings are null.</exception>
    public HapticsListener(
        DomainEventHub hub,
        IVibrator vibrator,
        HapticsSettings settings,
        Func<float> clock = null)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _vibrator = vibrator ?? throw new ArgumentNullException(nameof(vibrator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? (() => Time.realtimeSinceStartup);
    }

    /// <summary>Takes the four subscriptions. Called by VContainer when the run scope is built.</summary>
    public void Start()
    {
        _playerDamagedSubscription = _hub.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        _enemyDamagedSubscription = _hub.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        _enemyDiedSubscription = _hub.Subscribe<EnemyDied>(OnEnemyDied);
        _chargeStartedSubscription = _hub.Subscribe<ChargeStarted>(OnChargeStarted);
    }

    /// <summary>Plays the open window's strongest pulse once that window has closed.</summary>
    public void Tick()
    {
        if (!_windowOpen || _clock() - _windowStart < WindowSeconds)
        {
            return;
        }

        _windowOpen = false;

        // Read again rather than trusted from when the event arrived: the toggle can be flipped
        // while a window is open, and the player who just turned haptics off is owed silence
        // rather than one last buzz.
        if (!_settings.Enabled)
        {
            return;
        }

        _vibrator.Pulse(_pendingMilliseconds, _pendingAmplitude);
    }

    /// <summary>
    /// Drops the subscriptions and abandons any pulse still waiting. Called with the run scope.
    /// </summary>
    public void Dispose()
    {
        _playerDamagedSubscription?.Dispose();
        _enemyDamagedSubscription?.Dispose();
        _enemyDiedSubscription?.Dispose();
        _chargeStartedSubscription?.Dispose();

        _playerDamagedSubscription = null;
        _enemyDamagedSubscription = null;
        _enemyDiedSubscription = null;
        _chargeStartedSubscription = null;

        // A run that ended half a window ago must not buzz over the menu.
        _windowOpen = false;
    }

    /// <remarks>
    /// A blocked hit is published exactly like a landed one and means the opposite: nothing was
    /// applied, because i-frames or a Charge turned it away (CC §7). The HUD flashes for it; the
    /// hand must not, or the one signal in the game that says "that hurt" would also fire on the
    /// moments the player got away with it.
    /// </remarks>
    private void OnPlayerDamaged(PlayerDamaged evt)
    {
        if (evt.Blocked)
        {
            return;
        }

        Offer(PlayerDamagedMs, PlayerDamagedAmplitude);
    }

    /// <remarks>
    /// The killing blow publishes this and then <c>EnemyDied</c> immediately, so both land in the
    /// same window and the death's 0.7 wins on amplitude. That is the intended reading: a kill
    /// feels like a kill rather than like a hit followed by a kill.
    /// </remarks>
    private void OnEnemyDamaged(EnemyDamaged evt)
    {
        Offer(EnemyDamagedMs, EnemyDamagedAmplitude);
    }

    private void OnEnemyDied(EnemyDied evt)
    {
        Offer(EnemyDiedMs, EnemyDiedAmplitude);
    }

    private void OnChargeStarted(ChargeStarted evt)
    {
        Offer(ChargeStartedMs, ChargeStartedAmplitude);
    }

    /// <summary>
    /// Puts one pulse forward. It plays when the window closes if nothing stronger is offered
    /// first, and is dropped if something is.
    /// </summary>
    /// <remarks>
    /// Checked here as well as at the flush so that a disabled toggle costs nothing at all — no
    /// window opens, no clock is read, and the vibrator is never so much as looked at, which is
    /// what rule 3 asks for.
    /// </remarks>
    private void Offer(int milliseconds, float amplitude)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        if (!_windowOpen)
        {
            _windowOpen = true;
            _windowStart = _clock();
            _pendingMilliseconds = milliseconds;
            _pendingAmplitude = amplitude;
            return;
        }

        if (amplitude <= _pendingAmplitude)
        {
            return;
        }

        // The window keeps its original start: a stream of events must not be able to hold the
        // pulse off forever by continually raising the bar.
        _pendingMilliseconds = milliseconds;
        _pendingAmplitude = amplitude;
    }
}
