using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Stage;

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
    private readonly int _deviceEnemyCap;
    private readonly int _projectileCapacity;

    /// <summary>
    /// What turns the plan into enemies in an arena. Never null while a run is running: an arena
    /// with nowhere to put anything gets an inert one rather than none (M2-05 rule 12), so nothing
    /// downstream has to ask whether this run has a director.
    /// </summary>
    private SpawnDirector _director;

    /// <summary>
    /// The stage the run is in the middle of, and what ends it. Null for a mode whose content comes
    /// entirely from its spawn plan — there is nothing to compose, so there are no waves to pace.
    /// </summary>
    /// <remarks>
    /// Nullable where <see cref="_director"/> is not, and the asymmetry is deliberate: an inert
    /// director is still a director, because "no spawn points" is a property of the <em>arena</em>
    /// and every arena has one. An empty roster is a property of the <em>mode</em>, and a mode with
    /// no waves has no stage to pace through — <c>StageFlow</c> would be holding a plan nothing ever
    /// composed into.
    /// </remarks>
    private StageFlow _flow;

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

    /// <param name="catalog">Where the config's mode, class and every archetype it names are resolved.</param>
    /// <param name="random">
    /// The run's generator. Its <see cref="IRandom.Seed"/> is no longer what the state records —
    /// the config states that — but <see cref="Start"/> refuses a config that disagrees with it.
    /// </param>
    /// <param name="events">Where run lifecycle events go.</param>
    /// <param name="intents">Where each tick's <see cref="PlayerMoveIntent"/> is written.</param>
    /// <param name="enemyCapacity">
    /// The most enemies a run may hold at once, for the <see cref="EnemySystem"/> each
    /// <see cref="Start"/> builds. It must be the number the <c>WorldSnapshot</c> was built with,
    /// which is why both come from one constant in <c>BootInstaller</c>: an enemy core knows about
    /// but the snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <param name="deviceEnemyCap">
    /// The most enemies this device may have standing at once — GD §11.1's tier, not a difficulty
    /// number. It bounds GD §12.2's concurrency curve, so it decides how many bodies a stage is
    /// delivered in and therefore how much of its budget is spent on <em>quality</em> instead
    /// (GD §11.2, <see cref="WaveComposer"/>). A separate argument from
    /// <paramref name="enemyCapacity"/> and always smaller: that one is how many enemies the
    /// snapshot can carry, this one is how many the phone can draw.
    /// </param>
    /// <param name="projectileCapacity">
    /// The most shots that may be in the air at once, for the <see cref="ProjectileSystem"/> each
    /// <see cref="Start"/> builds. From <c>BootInstaller</c> beside the enemy cap, because it is the
    /// same kind of number — what this device is allowed to have happening at once — and
    /// deliberately not derived from the enemy cap: a shot outlives its shooter, so the two counts
    /// are not the same question.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="enemyCapacity"/>, <paramref name="deviceEnemyCap"/> or
    /// <paramref name="projectileCapacity"/> is not positive, or the cap exceeds the capacity — an
    /// arena allowed to hold more bodies than the snapshot can carry is an arena core would be blind
    /// to part of.
    /// </exception>
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
        int enemyCapacity,
        int deviceEnemyCap,
        int projectileCapacity)
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

        if (deviceEnemyCap <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceEnemyCap),
                deviceEnemyCap,
                "deviceEnemyCap must be greater than zero. A device allowed no enemies is one "
                    + "every stage composes an empty arena for.");
        }

        if (deviceEnemyCap > enemyCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceEnemyCap),
                deviceEnemyCap,
                $"deviceEnemyCap is {deviceEnemyCap} and enemyCapacity is {enemyCapacity}. A "
                    + "stage may not be allowed more bodies than the snapshot can carry back — "
                    + "the surplus would exist in core and be invisible to it.");
        }

        if (projectileCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectileCapacity),
                projectileCapacity,
                "projectileCapacity must be greater than zero. A run allowed no shots in the air "
                    + "is one whose Spitters fire in silence — Fire refuses every one of them and "
                    + "says nothing, which is exactly the failure a playtest cannot see.");
        }

        _enemyCapacity = enemyCapacity;
        _deviceEnemyCap = deviceEnemyCap;
        _projectileCapacity = projectileCapacity;
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public RunState State { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A run is already running.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The catalog holds no mode or class with the config's ids, or the plan or the mode's roster
    /// names an archetype nobody authored.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The config's seed disagrees with the generator this session was built with.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The mode has no stage <c>config.StageIndex</c>.
    /// </exception>
    /// <remarks>
    /// The order is the contract, and the first half of it is new in M2-02: resolve the mode,
    /// resolve the class, check the seed, check the stage, resolve every archetype the run could
    /// possibly need — and only then build the state, publish <c>RunStarted</c>, flip
    /// <see cref="IsRunning"/> and spawn. Everything from <c>RunStarted</c> onwards is unchanged
    /// and is asserted by a test (AR §18.1).
    /// </remarks>
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

        // Everything below this line and above `new RunState` is validation, and the whole block
        // runs before a single thing is assigned or announced (ledger row 3). An unauthored mode,
        // class or archetype therefore leaves the session exactly as it was: not running, no
        // event published, nothing standing, and whatever State the previous run left still
        // readable. Until M2-02 the plan was spawned *after* RunStarted and after IsRunning
        // flipped, so a stranger mid-plan threw with the run announced and half an arena alive —
        // inert while only RunScope authored a plan, and mode data is the first thing that can.
        ModeSpec mode = _catalog.Mode(config.ModeId);

        CharacterSpec character = _catalog.Character(config.CharacterId);

        // One truth, checked at the one place both are visible. The config states the seed and the
        // generator was built from one, so the only way they can differ is a composition mistake —
        // and the symptom of letting it through would be a run whose recorded seed does not replay
        // it, which is the one number a bug report is worth having. Core does not reseed the
        // generator to make them agree: that would be an IRandom.Reseed, widening a port ahead of
        // its caller (AR §6), and it is half of what restoring stream state rides on — M2-13a
        // weighs it with ledger row 1.
        if (config.Seed != _random.Seed)
        {
            throw new ArgumentException(
                $"config.Seed is {config.Seed} but the run's generator was seeded with "
                    + $"{_random.Seed}. A run has one seed; the composition root builds the "
                    + "generator and states the same number here.",
                nameof(config));
        }

        // Asked of the mode, because whether stage 6 exists is the mode's question — an endless
        // Descent has every stage from 1, a finite mode does not. RunConfig already refused a
        // stage below 1 without needing to see a mode.
        if (!mode.HasStage(config.StageIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.StageIndex,
                $"'{mode.Id}' has no stage {config.StageIndex}. It runs from "
                    + $"{mode.StartingStage} to "
                    + (mode.IsEndless ? "endless." : $"{mode.FinalStage}."));
        }

        RequireAuthored(config.SpawnPlan, mode);

        // The last of the validation, and it is here for ledger row 3's reason rather than for
        // tidiness: WaveComposer refuses a mode that introduces nothing at or before this stage,
        // and composing after RunStarted would strand exactly the announcement that row exists to
        // stop being stranded. Held in locals until the run is built, so a throw below still
        // leaves this session's own fields as the previous run left them.
        //
        // A composition that succeeds and a Start that then fails would leave the Spawn stream
        // advanced. That is accepted: the run it was drawn for does not exist, and the alternative
        // — duplicating the composer's eligibility rule here so the real call can happen later —
        // is two copies of one rule, which is how they come to disagree.
        WavePlan plan = null;
        WaveComposer composer = null;

        // An empty roster is legal (M2-02): a mode whose content comes entirely from its spawn
        // plan has nothing to schedule, so it is not composed at all rather than composed into
        // nothing — which is what WaveComposer refuses, loudly and correctly.
        if (mode.Roster.Count > 0)
        {
            // Sized once for the whole run, at the wave curve's ceiling by the mode's roster
            // length, because M2-10 recomposes into this same object at every stage boundary and a
            // transition is the worst moment in a run to allocate (WavePlan's own reasoning).
            plan = new WavePlan(mode.Scaling.Waves.Max, mode.Roster.Count);

            composer = new WaveComposer(_catalog, new ThreatBudget(mode.Scaling, _deviceEnemyCap));

            // The first thing in a run to consume the Spawn stream, and it draws from that one and
            // no other (ADR-0011).
            composer.Compose(config.StageIndex, mode, plan, _random.Spawn);
        }

        int seed = config.Seed;

        // +Z, because a run begins with the camera behind the character and nothing yet to aim
        // at. The first stick input turns them within a frame or two at 720°/s.
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);

        // The same capacity the registry and the snapshot use, from the same constant, because the
        // candidate buffer it preallocates has to be able to hold every enemy the run may spawn.
        // It gets the intent sink because a damage frame is a question for the body, and the
        // question has to leave on the tick that produced it rather than be relayed through here.
        var combat = new PlayerCombat(character, _events, _intents, _enemyCapacity);

        // One per run, not one per session: End leaves the finished registry readable and a second
        // Start must not inherit the first run's enemies, ids or free list. It takes the run's
        // generator because respawning draws a position (M1-19) — from the Spawn stream and no
        // other, which is the system's own rule to keep rather than this class's to enforce.
        //
        // The scaling is built here and handed straight in, and nothing else in the run holds one:
        // the curves belong to the mode, the mode is resolved above, and the only thing that ever
        // applies them is a spawn. A run's depth starts at the config's stage — a fresh run reads
        // it from the mode's StartingStage, a resumed one from the save (M2-14b) — and M2-10 moves
        // it at each boundary.
        var enemies = new EnemySystem(
            _catalog,
            _events,
            _random,
            new DepthScaling(mode.Scaling),
            _enemyCapacity)
        {
            Depth = config.StageIndex,
        };

        // One per run, like the registry above and for the same reason: a second Start must not
        // inherit the first run's shots or its ids. It takes no generator, because a shot goes
        // exactly where it was aimed and spread would be a change to what a seed means (ADR-0011).
        var projectiles = new ProjectileSystem(_events, _projectileCapacity);

        State = new RunState(
            config.ModeId,
            config.CharacterId,
            seed,
            config.StageIndex,
            character,
            motor,
            combat,
            enemies,
            projectiles);

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
        //
        // Every id in the plan was resolved above, so this line can no longer fail on content and
        // the announcement above can no longer be stranded by it (ledger row 3).
        enemies.SpawnAll(config.SpawnPlan);

        // Built after SpawnAll (M2-05 rule 13), so the arena's dressed-in enemies are standing
        // before wave 1 arrives — the ids follow the order the arena reads in, and the director's
        // concurrency check counts them, which it could not do if it had begun first.
        //
        // Built for every run, including one with nowhere to spawn and one whose mode has nothing
        // to compose: an inert director is a director, so nothing downstream has to ask which kind
        // of run it is in (rule 12).
        // Where a body may be put no longer comes in here: an arena's spawn points are a fact about
        // whichever room is standing, so they arrive on the snapshot and reach the director at
        // Begin, one stage at a time (M2-11a rule 6).
        _director = new SpawnDirector(enemies, _events);

        // A mode with an empty roster composed nothing above, so there is no stage to pace: the
        // arena is whatever the spawn plan dressed into it and it stays that way. The director is
        // built anyway, one line up, for rule 12's reason — an inert director is a director.
        if (plan is null)
        {
            _flow = null;

            return;
        }

        _flow = new StageFlow(
            mode,
            composer,
            _director,
            enemies,
            projectiles,
            combat,
            _events,
            plan,
            seed);

        // Last, and after RunStarted and SpawnAll for the reason SpawnAll itself is after them: this
        // publishes StageArrived, and a handler dressing an arena from it may reasonably assume
        // there is a run to dress one for. It does not begin the director — GD §7.1's two seconds
        // of arrival come first, and it is the end of those that hands the plan over (M2-10 rule 2).
        _flow.Begin(config.StageIndex, State.Time);
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
        //
        // The context is built here and once (M2-07b rule 2), never per agent: every enemy in the
        // arena therefore decides against one reading of the clock, and widening what a behaviour
        // may reach is a change to one struct rather than to every implementer, every dispatch and
        // every test that calls one. M2-08 is the first time that promise was called in: the census
        // is the sixth member, added in one line so that a Bloater's fuse can end its own life
        // through the door damage reaches an enemy through. It is a struct; it allocates nothing.
        var enemies = new EnemyTickContext(
            snapshot.Dt,
            State.Time,
            State.Combat,
            _intents,
            _events,
            State.Projectiles,
            State.Enemies);

        State.Enemies.Tick(enemies);

        // After the behaviours and before the death check (M2-07a rule 10, AR §18.1).
        //
        // After, because a shot fired this tick starts flying now and must not be able to arrive on
        // the tick it left: the behaviours are where a Spitter releases (M2-07b), and landing in the
        // same step would erase the flight the whole archetype exists to make the player walk out
        // of. Before, because a bolt that kills has to end the run on the tick it landed, exactly as
        // a Husk's strike does — put after the check and the player would keep playing for one
        // frame with no hit points.
        //
        // Core decides the arrival and calls PlayerCombat.ApplyDamage itself; nothing is asked of
        // the body (ledger row 7, settled at M2-07a rule 1).
        State.Projectiles.Tick(State.Time, State.PlayerPosition, State.Combat);

        // The first thing that ends a run from inside one (M1-17). Asked here rather than
        // subscribed to, because core has no business listening to its own events: PlayerCombat
        // publishes PlayerDied for everyone with something to say about a death, and this class
        // reads the state it already owns.
        //
        // After both of the passes that can hurt the player — a Husk's strike and a bolt's arrival —
        // because that is where the death was announced, so RunEnded follows PlayerDied on the same
        // tick, in that order, whichever of the two killed them.
        // And before TickBody, because a corpse is not steered: the intent it would write is a
        // velocity for a run that is over, and RunTicker would apply it to a body nobody is
        // driving any more.
        if (State.Combat.IsDead)
        {
            End();
            return;
        }

        // After the death check and before the motor (M2-05 rule 14, AR §18.1). After, because a
        // run that ended this tick must spawn nothing — a wave arriving on the frame the player
        // died would be telegraphed into an arena nobody is playing in. Before the motor, because
        // the director is part of the world the player is moving through rather than part of the
        // move: it sees the same simulated `now` and the same player position everything else this
        // tick did.
        //
        // The Spawn stream and no other, read here rather than held by the director, so that the
        // one thing in a run that consumes spawn randomness does so through the run's own
        // generator (ADR-0011).
        _director.Tick(State.Time, State.PlayerPosition, _random.Spawn);

        // After the director and before the motor (M2-10 rule 13, AR §18.1). After, because the
        // flow ends a stage by reading IsStageComplete, which the director has just this instant
        // finished deciding — asked one step earlier it would be answering about last frame's
        // arena. Before the motor, for the director's own reason: the stage is part of the world
        // the player is moving through rather than part of the move.
        //
        // Null for a mode with nothing to compose, which is a run with no waves rather than a run
        // with no stage — the depth is still the config's and everything priced against it still is.
        if (_flow is not null)
        {
            _flow.Tick(State.Time, snapshot, _random.Spawn);

            // Copied rather than owned, because the two numbers answer different questions: the
            // flow's is what the stage machine is running, and this is what the run reports and
            // saves (M2-13). They agree because this is the one line that moves the second one.
            State.StageIndex = _flow.Stage;

            // A finite mode that has run out of stages. The flow sets the flag and stays in Clear;
            // ending the run is this class's word and nobody else's (rule 14). Inert for Descent,
            // which is endless — and written anyway, because ModeSpec.FinalStage exists and the
            // alternative is a run walking through a door into a stage its mode does not have.
            if (_flow.IsModeComplete)
            {
                End();

                return;
            }
        }

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

        // The same moment and the same silence, for the same reason. It also drops whatever rings
        // were in flight: a telegraph is a promise to the player, and there is no longer a player
        // to keep it to.
        _director.Clear();

        // And the shots that were still in the air, silently again. A bolt that landed on an ended
        // run would hurt a corpse and publish an impact into a scope that is being torn down.
        State.Projectiles.Clear();
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
    /// Refuses a run whose plan or roster names an archetype nobody authored, before anything about
    /// the run has been announced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The roster is walked too, not only the plan</b>, and that is the half worth arguing for.
    /// The plan fails on the first frame either way, because <c>SpawnAll</c> would hit it
    /// immediately; the roster is what M2-05's director spawns from, so an unauthored archetype
    /// there would otherwise surface forty seconds into a run as a wave that threw — at a moment
    /// that looks like a director bug and points at nothing. A mode's schedule is a statement of
    /// intent (M2-02 rule 10: Descent ships with Husk alone until M2-06 authors the other two),
    /// and this is what makes the gap between intent and content loud instead of mysterious.
    /// </para>
    /// <para>
    /// <c>TryGetEnemy</c> rather than <c>Enemy</c>, so the message can say <em>where</em> the
    /// stranger was named. The catalog's own "no enemy with id 'x'" is true and unhelpful when
    /// three different lists could have held it.
    /// </para>
    /// </remarks>
    private void RequireAuthored(SpawnPlan plan, ModeSpec mode)
    {
        for (int i = 0; i < plan.Initial.Count; i++)
        {
            RequireArchetype(plan.Initial[i].SpecId, $"the spawn plan's entry {i}");
        }

        for (int i = 0; i < mode.Roster.Count; i++)
        {
            RequireArchetype(mode.Roster[i].SpecId, $"'{mode.Id}'s roster");
        }
    }

    /// <summary>Refuses one archetype id the catalog does not hold, naming who asked for it.</summary>
    private void RequireArchetype(ContentId specId, string source)
    {
        if (_catalog.TryGetEnemy(specId, out _))
        {
            return;
        }

        throw new KeyNotFoundException(
            $"No enemy with id '{specId}' in the catalog, and {source} names it. Nothing about "
                + "this run has been announced; add an EnemyDefinition to BootScope's enemy list "
                + "or correct the id.");
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
