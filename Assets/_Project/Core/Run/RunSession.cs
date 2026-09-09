using System;
using System.Numerics;
using Soulvail.Core.Ai;
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
public sealed class RunSession : IRunSession, IPlayerCommands
{
    private readonly ContentCatalog _catalog;
    private readonly IRandom _random;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly int _enemyCapacity;

    /// <summary>
    /// A dash was in flight as of the previous tick. The edge <see cref="Tick"/> needs to know when
    /// to hand movement back to the stick — see the remarks there.
    /// </summary>
    /// <remarks>
    /// Here rather than on <c>PlayerCombat</c> because it is about the motor, and the motor is this
    /// object's to tick. <c>PlayerCombat</c> watches the other edge of the same dash, the one where
    /// the i-frames lapse, and the two are deliberately not the same moment.
    /// </remarks>
    private bool _wasCharging;

    /// <param name="catalog">Where <c>config.CharacterId</c> is resolved.</param>
    /// <param name="random">The run's generator; its <see cref="IRandom.Seed"/> is recorded in the state.</param>
    /// <param name="events">Where run lifecycle events go.</param>
    /// <param name="intents">Where each tick's <see cref="PlayerMoveIntent"/> is written.</param>
    /// <param name="enemyCapacity">
    /// The most enemies a run may hold at once, for the <see cref="EnemySystem"/> each
    /// <see cref="Start"/> builds. It must be the number the <c>WorldSnapshot</c> was built with,
    /// which is why both come from one constant in <c>BootInstaller</c>: an enemy core knows about
    /// but the snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="enemyCapacity"/> is not positive.</exception>
    /// <remarks>
    /// Guarded, where <see cref="RunState"/>'s constructor is not, and the difference is the
    /// boundary: this one is public and called from another assembly, so a null arrives from code
    /// core cannot see. Without the guards a missing <paramref name="intents"/> would surface a
    /// frame later as a <see cref="NullReferenceException"/> inside <see cref="Tick"/>, pointing
    /// at the tick rather than at the registration that forgot it. The capacity is checked here
    /// rather than at the first <see cref="Start"/>, so a mis-wired scope fails while it is being
    /// built rather than one scene later.
    /// </remarks>
    public RunSession(
        ContentCatalog catalog,
        IRandom random,
        IDomainEvents events,
        IIntentSink intents,
        int enemyCapacity)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));

        if (enemyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enemyCapacity),
                enemyCapacity,
                "enemyCapacity must be greater than zero.");
        }

        _enemyCapacity = enemyCapacity;
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

        // The same capacity the registry and the snapshot use, from the same constant, because the
        // candidate buffer it preallocates has to be able to hold every enemy the run may spawn.
        // It gets the intent sink because a damage frame is a question for the body, and the
        // question has to leave on the tick that produced it rather than be relayed through here.
        var combat = new PlayerCombat(character, _events, _intents, _enemyCapacity);

        // One per run, not one per session: End leaves the finished registry readable and a second
        // Start must not inherit the first run's enemies, ids or free list.
        var enemies = new EnemySystem(_catalog, _events, _enemyCapacity);

        State = new RunState(config.CharacterId, seed, character, motor, combat, enemies);

        // With the state, not with the session: a run that ended mid-dash must not make the first
        // tick of the next one think it has a motor to stop.
        _wasCharging = false;

        // Published before IsRunning flips, so a handler that reads the session from inside this
        // event sees a run that is announced and not yet live. The alternative — flip, then
        // publish — would let a listener tick or end a run whose composition is still mid-flight.
        // The same order holds in End: during a lifecycle event the session still reports the
        // state it is leaving.
        _events.Publish(new RunStarted(config.CharacterId, seed));

        IsRunning = true;

        // After RunStarted, and the order is asserted by a test. Subscribers are wired when the
        // scope is built, well before this — so the reason is not "so that anyone is listening",
        // it is that a run has to be announced before the things inside it are: a view handling
        // EnemySpawned may reasonably assume there is a run to put an enemy in. It is also after
        // IsRunning flips, so a handler that ticks or reads the session from inside a spawn event
        // finds a live run rather than one that has not begun.
        enemies.SpawnAll(config.SpawnPlan);
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

        // Ingest first, always. It is what makes every position in core this frame's rather than
        // last frame's, so anything that reads an enemy — perception, targeting, cone hits in
        // M1-11 — has to come after it, and a target chosen from stale positions is the whole bug
        // the snapshot exists to prevent.
        State.Enemies.Ingest(snapshot);

        // Combat between the two enemy passes, which is the order the rest of the frame hangs off.
        // Before the behaviours, so the target is chosen from the same positions the enemies were
        // just seen at rather than from wherever this tick's AI moved them; and before the motor,
        // because the motor needs the facing this decides.
        //
        // Which is also why the facing handed over is the one the motor is *currently* in, from
        // last tick's turn: combat cannot be given a facing that has not been decided yet. A swing
        // landing mid-turn is therefore aimed up to one frame of rotation behind the pose that gets
        // drawn — see PlayerCombat.Tick's bodyFacing, which is where that trade is argued.
        State.Combat.Tick(
            snapshot.Dt,
            State.Time,
            snapshot,
            State.Enemies.Registry.Alive,
            State.Motor.Facing);

        // After combat, and the order decides who wins a trade. The player's swing this tick is
        // resolved against enemies as they were seen, and the enemy's strike lands against a player
        // whose i-frames and Aegis have already been advanced — so a dodge that expired this tick
        // has expired by the time the Husk swings, rather than protecting one frame past its own
        // window. The behaviours get the intent sink because a strike is decided here and the walk
        // it interrupts is a question for the body, which has to leave on the tick that produced it.
        State.Enemies.Tick(snapshot.Dt, State.Time, State.Combat, _intents);

        TickBody(snapshot);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A forward, and the shortest method here on purpose: everything a report has to be sceptical
    /// about — whether a swing is owed an answer at all, duplicate ids, ids that have since died —
    /// belongs to the object that asked the question, and putting any of it here would split one
    /// rule across two classes. This one adds only what it alone knows: that there is a run, and
    /// what time it is.
    /// </para>
    /// <para>
    /// Damage lands at <see cref="RunState.Time"/> — the simulated clock as of the last
    /// <see cref="Tick"/> — rather than at some interpolated moment between ticks, for the reason
    /// <see cref="FocusTarget"/> gives: a fact arrives between frames, and core has exactly one
    /// clock. The lag is at most one frame and it is the same lag the i-frames and the Aegis
    /// recharge are already measured against, which matters more than being right to the
    /// millisecond about a swing nobody can see the timing of.
    /// </para>
    /// </remarks>
    public void ReportConeHits(ReadOnlySpan<int> enemyIds)
    {
        RequireRunning(nameof(ReportConeHits));

        State.Combat.ResolveConeHits(enemyIds, State.Time, State.Enemies);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A forward, for the reason <see cref="ReportConeHits"/> is one: every rule about which reports
    /// count — the window, the per-dash dedupe, whether an id is still breathing — belongs to the
    /// object that started the dash. This one adds the clock, and the clock is the same one a swing
    /// lands on: <see cref="RunState.Time"/> as of the last <see cref="Tick"/>, at most a frame
    /// behind the sweep that produced the report and measured against the same simulated seconds the
    /// dash's own window was.
    /// </para>
    /// <para>
    /// Unlike <see cref="ReportConeHits"/>, several of these per dash is the normal case rather than
    /// a mistake — see the port.
    /// </para>
    /// </remarks>
    public void ReportChargeHits(ReadOnlySpan<int> enemyIds)
    {
        RequireRunning(nameof(ReportChargeHits));

        State.Combat.ResolveChargeHits(enemyIds, State.Time, State.Enemies);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Resolved against the enemies as of the last <see cref="Tick"/>, because a command lands
    /// before the frame it belongs to is built (<c>RunTicker</c>'s command phase). Those positions
    /// are therefore up to one frame old — at most a couple of centimetres of walking against a 3 m
    /// tap radius, which no thumb can tell apart from exact. The alternative, running commands
    /// after the ingest, would put the tap *after* the tick it should have influenced and cost a
    /// whole frame of latency on the one input the player expects to be instant.
    /// </para>
    /// <para>
    /// Throws rather than no-ops when nothing is running, unlike <see cref="End"/>: ending a run
    /// that never started is scope teardown being tidy, while commanding one is an input adapter
    /// that is listening when it should not be.
    /// </para>
    /// </remarks>
    public void FocusTarget(Vector3 worldPoint)
    {
        RequireRunning(nameof(FocusTarget));

        State.Combat.FocusAt(worldPoint, State.Enemies.Registry.Alive);
    }

    /// <inheritdoc />
    public void ClearFocus()
    {
        RequireRunning(nameof(ClearFocus));

        State.Combat.ClearFocus();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A press is <em>recorded</em>, never acted on. Nothing about the dash happens here: whether it
    /// fires at all, which way it goes and whether the press is still worth honouring are all
    /// decided on the next <see cref="Tick"/>, by the one object holding CC §5's clock. The reason
    /// is <c>Targeter.Focus</c>'s: a thumb lands between ticks, and a command that resolved itself
    /// where it arrived would put a gameplay decision at whatever point in the frame the input
    /// system happened to read the screen.
    /// </para>
    /// <para>
    /// Stamped with <see cref="RunState.Time"/>, which is the clock the input buffer is measured
    /// against — so a press is at most one frame older than it says it is, and the 0.15 s it stays
    /// live is 0.15 s of simulated time rather than of a wall clock that keeps running while the
    /// game is paused.
    /// </para>
    /// </remarks>
    public void MovementSkill()
    {
        RequireRunning(nameof(MovementSkill));

        State.Combat.Charge.Request(State.Time);
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

        // Cleared without announcing a despawn each, and after RunEnded rather than before: the
        // scope is going away and with it every subscriber a despawn could reach, so 64 farewell
        // events would be noise — while a listener handling RunEnded can still read the census
        // that was live when the run finished.
        State.Enemies.Clear();
    }

    /// <summary>
    /// Moves the player — or deliberately does not, while a dash is doing it instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two instructions about where the character goes, and never both at once.</b> A
    /// <see cref="ChargeIntent"/> has already told the body to travel 10 m in a fixed direction over
    /// 0.22 s; a <see cref="PlayerMoveIntent"/> alongside it would be a second opinion arriving
    /// sixty times a second, and the body would have to invent a rule about which of them core
    /// meant. So for the length of the dash core says nothing about movement at all, and the motor
    /// is not ticked either — it would be integrating a stick nobody is being moved by.
    /// </para>
    /// <para>
    /// <b>The tick the dash ends on stops the motor and moves nowhere.</b> A suspended motor still
    /// holds whatever velocity it had when the dash began, and letting that out would fling the
    /// character on for another frame at the speed they were running before they dodged. So
    /// <c>Stop</c> first, and the intent that goes out carries the zero — one frame at rest, which
    /// is 16 ms nobody can feel, against a carry-over everybody can. Acceleration resumes from rest
    /// on the next tick, which is what CC §2.4's 0.06 s ramp is for.
    /// </para>
    /// <para>
    /// The facing survives all of it. <c>PlayerMotor.Stop</c> leaves it alone on purpose, and
    /// nothing here turns the character round: a dash that ended with the body snapping back to
    /// where it was looking before would undo the one thing the player just committed to.
    /// </para>
    /// <para>
    /// Exactly one <see cref="PlayerMoveIntent"/> per tick otherwise, including when the stick is
    /// centred — the body needs the deceleration velocity just as much as the acceleration one, and
    /// a tick that emitted nothing would leave the view applying whatever it last read.
    /// </para>
    /// </remarks>
    private void TickBody(WorldSnapshot snapshot)
    {
        if (State.Combat.Charge.IsActive)
        {
            _wasCharging = true;
            return;
        }

        if (_wasCharging)
        {
            _wasCharging = false;

            State.Motor.Stop();
        }
        else
        {
            // The character now looks at what it is aiming at. Null when there is nothing to aim
            // at, which the motor reads as "face the way you are moving" — M0's behaviour, still
            // correct for an empty arena.
            State.Motor.Tick(snapshot.Dt, snapshot.MoveInput, State.Combat.FaceDirection);
        }

        _intents.PlayerMove(new PlayerMoveIntent(State.Motor.Velocity, State.Motor.Facing));
    }

    /// <summary>
    /// Refuses a command or a fact that arrived outside a run, naming what arrived.
    /// </summary>
    /// <remarks>
    /// Named rather than generic, because these reach here from different adapters and
    /// "FocusTarget was called with no run running" points straight at whichever one is listening
    /// when it should not be.
    /// </remarks>
    private void RequireRunning(string member)
    {
        if (IsRunning)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{member} was called with no run running. Player commands and physical facts both " +
            "belong to a live run — the input map is disabled outside one and the ticker stops " +
            "reporting — so this is a wiring mistake rather than something the player or the " +
            "world did.");
    }
}
