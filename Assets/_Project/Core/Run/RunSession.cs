using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Run;

/// <summary>
/// The brain. Given where everything is, once a frame, it decides what the player does and says
/// so — through an intent for the body and events for everyone else. The only implementation of
/// <see cref="IRunSession"/>, and the one object Unity holds for the length of a run. See AR §3,
/// §4.1 and §7.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny in M0: a run is one character moving. Everything a run will grow — health,
/// targeting, weapons (M1), stages and a director (M2), levels and a tree (M3) — is composed in
/// here as it lands, and each addition is a field ticked from <see cref="Tick"/>, never a second
/// entry point. The port stays <c>Start</c> / <c>Tick</c> / <c>End</c> plus the facts M1 brings
/// (AR §6).
/// </para>
/// <para>
/// No <c>IClock</c>. Simulated time is the sum of each tick's <c>Dt</c>, which rides in on the
/// snapshot, so a clock here would be a second answer to "how much time has passed" — and the
/// wrong one, since it would keep running while the game is paused or backgrounded. AR §7's
/// sketch listed one; M0-10 built the class without it and §7 now says so. Wall-clock arrives
/// with M2-01, for persistence, which is a different question asked by different code.
/// </para>
/// <para>
/// Dependencies come in through the constructor and are never resolved from anywhere: no
/// statics, no locator, no <c>[Inject]</c> attributes in core. <c>RunInstaller</c> (M0-12) builds
/// this and <c>RunScope</c> owns its lifetime.
/// </para>
/// </remarks>
public sealed class RunSession : IRunSession
{
    private readonly ContentCatalog _catalog;
    private readonly IRandom _random;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;

    /// <param name="catalog">Where <c>config.CharacterId</c> is resolved.</param>
    /// <param name="random">The run's generator; its <see cref="IRandom.Seed"/> is recorded in the state.</param>
    /// <param name="events">Where run lifecycle events go.</param>
    /// <param name="intents">Where each tick's <see cref="PlayerMoveIntent"/> is written.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>
    /// Guarded, where <see cref="RunState"/>'s constructor is not, and the difference is the
    /// boundary: this one is public and called from another assembly, so a null arrives from code
    /// core cannot see. Without the guards a missing <paramref name="intents"/> would surface a
    /// frame later as a <see cref="NullReferenceException"/> inside <see cref="Tick"/>, pointing
    /// at the tick rather than at the registration that forgot it.
    /// </remarks>
    public RunSession(ContentCatalog catalog, IRandom random, IDomainEvents events, IIntentSink intents)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public RunState State { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    public void Start(RunConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (IsRunning)
        {
            throw new InvalidOperationException(
                "A run is already running. End it before starting another.");
        }

        // Resolved before anything is assigned, so an unknown id leaves the session exactly as it
        // was: not running, no event published, and whatever State the previous run left still
        // readable. A half-started run would be worse than no run at all.
        CharacterSpec character = _catalog.Character(config.CharacterId);

        // Recorded from the generator rather than chosen here, so the number a bug report quotes
        // is provably the one the streams are drawing from. See RunConfig's remarks.
        int seed = _random.Seed;

        // +Z, because a run begins with the camera behind the character and nothing yet to aim
        // at. The first stick input turns them within a frame or two at 720°/s.
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);

        State = new RunState(config.CharacterId, seed, character, motor);

        // Published before IsRunning flips, so a handler that reads the session from inside this
        // event sees a run that is announced and not yet live. The alternative — flip, then
        // publish — would let a listener tick or end a run whose composition is still mid-flight.
        // The same order holds in End: during a lifecycle event the session still reports the
        // state it is leaving.
        _events.Publish(new RunStarted(config.CharacterId, seed));

        IsRunning = true;
    }

    /// <inheritdoc />
    public void Tick(WorldSnapshot snapshot)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(
                "No run is running. Start one before ticking.");
        }

        // No null guard on the snapshot, unlike Start's config, and the asymmetry is deliberate:
        // this runs 60 times a second against one instance the builder owns for the whole run, so
        // a null could only ever be the first tick after a mis-wired scope — a failure that
        // arrives immediately and unmissably either way.
        State.Time += snapshot.Dt;

        // Written down, not decided: Unity resolves collision and reports where the player ended
        // up. Core never assigns a position to move anyone.
        State.PlayerPosition = snapshot.PlayerPosition;

        // Null face direction: M0 has nothing to aim at, so the character faces the way it moves.
        // M1-08 passes the target's direction here instead.
        State.Motor.Tick(snapshot.Dt, snapshot.MoveInput, null);

        // Exactly one, every tick, including when the stick is centred — the body needs the
        // deceleration velocity just as much as the acceleration one, and a tick that emitted
        // nothing would leave the view applying whatever it last read.
        _intents.PlayerMove(new PlayerMoveIntent(State.Motor.Velocity, State.Motor.Facing));
    }

    /// <inheritdoc />
    public void End()
    {
        // A no-op rather than a throw, so RunScope's disposal can call it without first asking
        // whether a run got as far as starting.
        if (!IsRunning)
        {
            return;
        }

        _events.Publish(new RunEnded(State.Time));

        IsRunning = false;
    }
}
