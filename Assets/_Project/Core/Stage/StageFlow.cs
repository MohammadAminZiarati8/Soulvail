using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Stage;

/// <summary>
/// Which part of a stage's life is running. GD §7.1's anatomy, one state per beat.
/// </summary>
/// <remarks>
/// <c>Clear</c> is a state rather than an instant because the Sanctum (GD §7.1, M6-02) lands between
/// it and <c>Gate</c>: when there is an economy to spend, a sixth phase goes in that gap without
/// moving anything either side of it.
/// </remarks>
public enum StagePhase
{
    /// <summary>The barrier seals and the number shows. Nothing spawns (rule 1).</summary>
    Arrival,

    /// <summary>The director runs, until every body of every wave is down.</summary>
    Waves,

    /// <summary>The last body is down; the barrier drops, the door opens, the next arena is named.</summary>
    Clear,

    /// <summary>Waiting for the player to walk into the door. No timeout (rule 8).</summary>
    Gate,

    /// <summary>The screen is covering itself. One beat, then the next <see cref="Arrival"/>.</summary>
    Transition,
}

/// <summary>
/// What makes a stage something a run passes through rather than a number a run was composed at:
/// the arena seals, the waves come, the last body drops, a door opens, and walking through it puts
/// the player one depth further down. GD §7.1 and §7.3; AR §5.
/// </summary>
/// <remarks>
/// <para>
/// <b>It paces and it does not spawn.</b> <c>SpawnDirector</c> owns everything about a wave — when
/// it starts, where a body appears, what the concurrency cap allows — and this object owns only the
/// silence either side of a stage and the crossing between two of them. The one thing it asks the
/// director is <c>IsStageComplete</c>, which M2-05 rule 6 left deliberately unpublished so that "the
/// stage is over" has exactly one publisher: this one.
/// </para>
/// <para>
/// <b>Every beat is measured on core's own simulated clock</b> — the <c>now</c> handed in, which is
/// <c>RunState.Time</c> and never a wall clock. Nothing outside core ever tells this object that a
/// fade finished or that a barrier has dropped: those are pictures of decisions already made here,
/// and a simulation that waited for a view to report back would stall on a dropped frame (rule 9).
/// </para>
/// <para>
/// <b>It allocates nothing per tick.</b> The <c>WavePlan</c> is the one <c>RunSession</c> builds
/// once for the run and this refills at every boundary (M2-04); the state machine is three fields;
/// and the phase test is struct arithmetic. <see cref="Tick"/> on a stage mid-<c>Waves</c> is one
/// property read.
/// </para>
/// </remarks>
public sealed class StageFlow
{
    /// <summary>Seconds of arrival before the first wave may start. GD §7.1's opening beat.</summary>
    public const float ArrivalTime = 2f;

    /// <summary>
    /// Seconds between the last body dropping and the door opening.
    /// </summary>
    /// <remarks>
    /// <b>The one number in this class that no design document gives.</b> GD §7.1 lists the barrier
    /// dropping and the gate materialising as a step of a stage without pricing it. Zero would mean
    /// the door popping open on the frame the last enemy dies, which reads as the game skipping its
    /// own beat; this is a pacing knob to be tuned against the 40–75 s stage of GD §7.3, on a phone,
    /// and it is flagged as invented rather than presented as derived.
    /// </remarks>
    public const float ClearTime = 1.5f;

    /// <summary>Seconds the screen has to cover itself before the world is swapped (rule 9).</summary>
    public const float FadeTime = 0.3f;

    /// <summary>
    /// How close to the door counts as walking into it, in metres — measured on XZ (AR §18.4).
    /// </summary>
    /// <remarks>
    /// A distance rather than a trigger collider, for <c>ProjectileSystem</c>'s reason (M2-07a rule
    /// 1): the answer does not depend on any collider core cannot see, so core decides it outright
    /// and <c>IRunSession</c> gains nothing. The height between a player capsule's centre and a
    /// door's anchor is a rendering detail, which is why Y is not in the comparison.
    /// </remarks>
    public const float GateReachRadius = 1.5f;

    private readonly ModeSpec _mode;
    private readonly WaveComposer _composer;
    private readonly SpawnDirector _director;
    private readonly EnemySystem _enemies;
    private readonly ProjectileSystem _projectiles;
    private readonly PlayerCombat _player;
    private readonly IDomainEvents _events;
    private readonly WavePlan _plan;

    /// <summary>
    /// The run's seed, for <see cref="ArenaFor"/> and nothing else. Held rather than drawn from,
    /// which is the whole of rule 6 — see that method.
    /// </summary>
    private readonly int _seed;

    /// <summary>The simulated second the current phase was entered at.</summary>
    private float _phaseEnteredAt;

    /// <summary>The last <c>now</c> seen, so <see cref="PhaseElapsed"/> can be read between ticks.</summary>
    private float _now;

    private bool _begun;

    /// <param name="mode">Whose curves the stages are composed from and whose <c>HasStage</c> ends a finite run.</param>
    /// <param name="composer">Turns a depth into waves, into the one plan below.</param>
    /// <param name="director">Given that plan when <c>Arrival</c> ends, and asked when the stage is over.</param>
    /// <param name="enemies">Re-depthed and emptied at a boundary, in that order (rule 10).</param>
    /// <param name="projectiles">Emptied with them, so a bolt does not follow the player through the door (rule 11).</param>
    /// <param name="player">
    /// For its <c>Targeter</c> alone. Deliberately the whole object rather than the targeter, so that
    /// the narrowness of what is reset is visible at the call site rather than hidden in a
    /// constructor argument — see <see cref="Advance"/>, rule 12.
    /// </param>
    /// <param name="events">Where the three stage events go.</param>
    /// <param name="plan">
    /// The run's single <c>WavePlan</c>, built once by <c>RunSession.Start</c> at the wave curve's
    /// ceiling and refilled here at every boundary. A transition is the worst moment in a run to
    /// allocate one.
    /// </param>
    /// <param name="seed">The run's seed, for <see cref="ArenaFor"/>.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public StageFlow(
        ModeSpec mode,
        WaveComposer composer,
        SpawnDirector director,
        EnemySystem enemies,
        ProjectileSystem projectiles,
        PlayerCombat player,
        IDomainEvents events,
        WavePlan plan,
        int seed)
    {
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));

        // Every int is a legal seed — it is a bit pattern, not a quantity — so there is nothing here
        // for a guard to reject.
        _seed = seed;
    }

    /// <summary>Which beat of the stage is running.</summary>
    public StagePhase Phase { get; private set; }

    /// <summary>The depth being played. Zero before <see cref="Begin"/>.</summary>
    public int Stage { get; private set; }

    /// <summary>Simulated seconds since the current phase was entered.</summary>
    public float PhaseElapsed => _now - _phaseEnteredAt;

    /// <summary>
    /// True once a finite mode has cleared its final stage — the run is over and the caller ends it.
    /// </summary>
    /// <remarks>
    /// Read by <c>RunSession.Tick</c>, which publishes <c>RunEnded</c>. Nothing here does: ending a
    /// run is the session's word, and a second publisher of it would be a second place that has to
    /// remember to clear the arena first (rule 14).
    /// </remarks>
    public bool IsModeComplete { get; private set; }

    /// <summary>
    /// Opens <paramref name="stage"/> at <paramref name="now"/>: adopts the composition already made
    /// for it and enters <see cref="StagePhase.Arrival"/>.
    /// </summary>
    /// <param name="stage">The run's opening depth — the config's, never assumed to be 1 (GD §4.5).</param>
    /// <param name="now">Simulated run seconds. Zero for a fresh run.</param>
    /// <remarks>
    /// <para>
    /// <b>It adopts rather than composes, and that is a deviation from the spec worth stating.</b>
    /// The spec has <c>Begin</c> compose the opening stage and take the <c>Spawn</c> stream to do it
    /// with. Either way of arranging that costs something M2-02 already paid for: composing here and
    /// calling this before <c>RunStarted</c> would publish <see cref="StageArrived"/> into a run that
    /// has not been announced, and calling it after would let an ineligible mode throw with the run
    /// announced and half an arena standing — which is ledger row 3, the exact bug M2-02's ordering
    /// exists to prevent. Composing here <em>as well</em> would draw the Spawn stream twice for the
    /// opening stage, for a composition the run then discards. So <c>RunSession.Start</c> keeps its
    /// one pre-<c>RunStarted</c> composition as the validation it already is, and this adopts it.
    /// <see cref="Tick"/> takes the stream, because a <em>boundary</em> composes and that is where
    /// the draw belongs.
    /// </para>
    /// <para>
    /// The director is not begun here. It is begun when arrival ends (rule 2), which is what makes
    /// GD §7.1's two seconds a pause with a cause rather than the one M2-05 rule 1 refused to insert.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A stage is already open.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stage"/> is below 1, or <paramref name="now"/> is not finite.
    /// </exception>
    public void Begin(int stage, float now)
    {
        if (_begun)
        {
            throw new InvalidOperationException(
                "This flow has already been begun. A stage boundary advances it; a second Begin "
                    + "would re-open a run that is already being played.");
        }

        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Stages are numbered from 1 (GD §8.2).");
        }

        RequireFinite(now, nameof(now));

        _begun = true;
        _now = now;

        Stage = stage;

        EnterArrival(now);
    }

    /// <summary>
    /// One frame of the stage's own life: the beat that is running either continues or ends.
    /// </summary>
    /// <param name="now">Simulated run seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="snapshot">
    /// This frame's world, for the two facts a stage needs: where the player is and where the door
    /// is. Both are read rather than remembered, so an arena that moves its gate is answered the
    /// frame it moves it.
    /// </param>
    /// <param name="spawn">
    /// The <c>Spawn</c> stream, and only ever that one (ADR-0011). A parameter rather than a field
    /// read, so the one draw a boundary makes is visible in the signature — the shape
    /// <c>SpawnDirector.Tick</c> already uses.
    /// </param>
    /// <remarks>
    /// Never called before <see cref="Begin"/>, and it says so rather than no-opping: a flow ticked
    /// without a stage is a composition mistake, and the symptom of letting it through is a run
    /// that never leaves stage zero with nothing in the log.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No stage has been begun.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="spawn"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now, WorldSnapshot snapshot, IRandomStream spawn)
    {
        if (!_begun)
        {
            throw new InvalidOperationException(
                "No stage has been begun. RunSession.Start opens the run's first stage; ticking "
                    + "before that is a wiring mistake rather than something the player did.");
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (spawn is null)
        {
            throw new ArgumentNullException(nameof(spawn));
        }

        // Guarded on a per-frame path, for SpawnDirector.Tick's reason: every comparison against a
        // NaN is false, so a NaN clock does not crash the flow — it parks it in whatever phase it
        // was in, for ever, with nothing in the log (AR §18.3).
        RequireFinite(now, nameof(now));

        _now = now;

        switch (Phase)
        {
            case StagePhase.Arrival:
                if (PhaseElapsed >= ArrivalTime)
                {
                    EnterWaves(now, snapshot);
                }

                break;

            case StagePhase.Waves:
                if (_director.IsStageComplete)
                {
                    EnterClear(now, snapshot);
                }

                break;

            case StagePhase.Clear:
                // A mode that has run out of stages stays here. There is no door to open onto a
                // stage the mode says does not exist, and the session ends the run on the flag.
                if (!IsModeComplete && PhaseElapsed >= ClearTime)
                {
                    EnterGate(now);
                }

                break;

            case StagePhase.Gate:
                if (IsInsideTheDoor(snapshot))
                {
                    EnterTransition(now);
                }

                break;

            case StagePhase.Transition:
                if (PhaseElapsed >= FadeTime)
                {
                    Advance(now, spawn);
                }

                break;

            default:
                throw new InvalidOperationException($"Unhandled stage phase '{Phase}'.");
        }
    }

    /// <summary>
    /// Seals the arena and announces it. Nothing spawns for <see cref="ArrivalTime"/> (rule 1).
    /// </summary>
    /// <remarks>
    /// <see cref="StageArrived"/> goes out here for <em>every</em> stage, the first one included, so
    /// that whatever dresses an arena has one event to listen to and one code path to do it in
    /// (rule 4).
    /// </remarks>
    private void EnterArrival(float now)
    {
        Enter(StagePhase.Arrival, now);

        _events.Publish(new StageArrived(Stage, ArenaFor(Stage)));
    }

    /// <summary>
    /// Hands the director this stage's plan and lets it run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Clear</c> then <c>Begin</c>, in that order, so a stage can never inherit the previous
    /// one's claims or a telegraph it left pending. <c>Begin</c> resets most of the same state on
    /// its own; the explicit <c>Clear</c> is the belt, and it is what makes the ordering a stated
    /// rule rather than a property of the director's current internals.
    /// </para>
    /// <para>
    /// <b>The arena's spawn points are handed over here, and this is the only place they are read.</b>
    /// They come off the frame's snapshot rather than out of the run's plan (M2-11a rule 6): a run
    /// has one room per stage, so where a body may go is a fact about whichever arena is standing
    /// now — reported by whatever raised it, the same way the door is. Two seconds of arrival have
    /// passed by the time this runs, so the room the points belong to is unambiguously the one the
    /// player is in.
    /// </para>
    /// <para>
    /// The director's first <em>tick</em> lands on the next frame, because <c>RunSession</c> ticks
    /// it before this object (rule 13). One frame of delay against a two-second arrival, named here
    /// rather than discovered later.
    /// </para>
    /// </remarks>
    private void EnterWaves(float now, WorldSnapshot snapshot)
    {
        Enter(StagePhase.Waves, now);

        _director.Clear();
        _director.Begin(_plan, snapshot.SpawnPoints, now);
    }

    /// <summary>
    /// The arena is empty. The barrier drops, the door opens, and the next arena is named.
    /// </summary>
    /// <remarks>
    /// The gate position is read off this frame's snapshot rather than held, and it is carried on the
    /// event so that whatever draws a door does not have to ask core where one is.
    /// </remarks>
    private void EnterClear(float now, WorldSnapshot snapshot)
    {
        Enter(StagePhase.Clear, now);

        // Asked of the mode, because whether there is a stage after this one is the mode's question
        // (M2-02 rule 3). Descent is endless, so this is inert in V1 — and it is written anyway,
        // because the alternative is a run that walks through a door into a stage its own mode says
        // it does not have (rule 14).
        bool hasNext = _mode.HasStage(Stage + 1);

        IsModeComplete = !hasNext;

        Vector3 gate = snapshot.HasGate ? snapshot.GatePosition : Vector3.Zero;

        _events.Publish(new StageCleared(Stage, gate, hasNext ? ArenaFor(Stage + 1) : default));
    }

    /// <summary>Waiting for the player, for as long as they like (rule 8).</summary>
    private void EnterGate(float now)
    {
        Enter(StagePhase.Gate, now);
    }

    /// <summary>
    /// The player is in the door. The screen has <see cref="FadeTime"/> to cover itself.
    /// </summary>
    private void EnterTransition(float now)
    {
        Enter(StagePhase.Transition, now);

        _events.Publish(new StageTransitionStarted(Stage, FadeTime));
    }

    /// <summary>
    /// Crosses the boundary: one depth further down, a cleared arena, and a freshly composed stage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the contract.</b> Depth first, so that anything reacting to the clears cannot
    /// spawn a body priced at the stage that has just ended (M2-03 rule 12). Then the enemies, then
    /// the shots, then the target, then the composition, and only then the next arrival.
    /// </para>
    /// <para>
    /// <b><c>Targeter.Reset</c>, and deliberately not <c>PlayerCombat.Reset</c>.</b> The wide one
    /// calls <c>Health.Reset</c>, and a free refill at every boundary would delete the attrition GD
    /// §12.5's death horizon is made of and pre-empt the Sanctum's heal (GD §13.3) before the Sanctum
    /// exists. What must go is the target — an id belonging to an arena that has just been torn down
    /// — so the narrow reset is the right one and the wide one is a trap worth naming (rule 12).
    /// </para>
    /// <para>
    /// <b><c>ProjectileSystem.Clear</c> is not tidiness.</b> A shot fired at the old arena's floor,
    /// landing after the swap, would damage the player at coordinates that no longer mean anything.
    /// <c>EnemySystem.Clear</c> beside it is belt and braces — the stage is complete, so nothing is
    /// alive — except for a corpse still waiting for its despawn frame, which is exactly the kind of
    /// thing that survives a boundary and stands in the next arena (rule 11).
    /// </para>
    /// </remarks>
    private void Advance(float now, IRandomStream spawn)
    {
        int next = Stage + 1;

        Stage = next;

        _enemies.Depth = next;
        _enemies.Clear();
        _projectiles.Clear();
        _player.Targeter.Reset();

        // **Before the composition, and this line is load-bearing rather than tidy.** The run owns
        // one WavePlan and every stage is composed into it (M2-04), so recomposing it is a mutation
        // of an object the director may still be holding — and the director is ticked every frame
        // by RunSession, including through the arrival that follows this. It sizes its per-wave
        // bookkeeping from the plan's dimensions at Begin, so a plan that grows a wave underneath
        // it walks `w < _plan.WaveCount` straight off the end of arrays it sized for the stage
        // before: GD §12.2's W(n) goes from two waves to three at stage 5, and the crossing out of
        // stage 4 is where that first bites. Cleared here, the director holds no plan for the whole
        // of Arrival and its Tick is a no-op, which is also what makes rule 1 true by construction
        // rather than by this object happening not to have begun it yet.
        _director.Clear();

        // Into the same plan object the run has held since Start. A boundary is the worst moment in
        // a run to allocate, and WavePlan.Begin is written to be refilled (M2-04).
        _composer.Compose(next, _mode, _plan, spawn);

        EnterArrival(now);
    }

    /// <summary>
    /// Whether the player is standing in the door, on XZ.
    /// </summary>
    /// <remarks>
    /// <b>An arena with no door parks here rather than throwing</b>, which is M2-05 rule 12's bargain
    /// for its reason: a scene dressed without a gate is the M0 grey box and every core fixture that
    /// never intends to leave stage 1, and neither of those is a fault. The cost — a run that
    /// silently cannot advance — is bought back in the Editor, where <c>DebugOverlay</c> reads
    /// <c>gate: —</c> (rule 15).
    /// </remarks>
    private static bool IsInsideTheDoor(WorldSnapshot snapshot)
    {
        if (!snapshot.HasGate)
        {
            return false;
        }

        // XZ, and no square root: AR §18.4. The height between a player capsule's centre and a
        // door's anchor is a rendering detail, and counting it would make a door on a plinth
        // unreachable from the floor in front of it.
        float dx = snapshot.PlayerPosition.X - snapshot.GatePosition.X;
        float dz = snapshot.PlayerPosition.Z - snapshot.GatePosition.Z;

        return (dx * dx) + (dz * dz) <= GateReachRadius * GateReachRadius;
    }

    /// <summary>
    /// Which arena a depth is fought in — a pure function of the run's seed and the stage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived, never drawn, and that is the point of it.</b> A run resumed at stage 7 lands in
    /// the arena stage 7 always had, with no saved stream position and no dependence on ledger row 1.
    /// Rejected: a <c>spawn.NextInt</c> draw, which makes arena identity a function of how many spawn
    /// positions the previous six stages happened to reject — and re-rolls the arena on every resume
    /// until row 1 is fixed (rule 6).
    /// </para>
    /// <para>
    /// <b>It answers <c>default</c> for a mode with no arena roster</b>, which is every core
    /// fixture and every scene dressed with its own grey box. Whatever raises arenas reads that as
    /// "leave the room standing", so an M0-shaped run is unaffected by any of this (M2-11a rule 3).
    /// </para>
    /// </remarks>
    private ContentId ArenaFor(int stage) => _mode.ArenaFor(stage, _seed);

    /// <summary>Moves to <paramref name="phase"/> and restarts the phase clock.</summary>
    private void Enter(StagePhase phase, float now)
    {
        Phase = phase;
        _phaseEnteredAt = now;
        _now = now;
    }

    private static void RequireFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be finite.");
        }
    }
}
