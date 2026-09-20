using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// Which part of its fight the Warden of Ash is in. GD §9.2's hook — <em>"shield-slam shockwaves,
/// ground fissures, summons Husks"</em> — as the five states it takes to do the first two.
/// </summary>
/// <remarks>
/// Flat and five, which is <see cref="SpitterState"/>'s shape and <see cref="ChaserState"/>'s:
/// every attack is <em>enter, telegraph, resolve, recover</em>, and the two attacks differ only in
/// what the resolve is. There is no state for the summoning, because summoning is not this class's
/// — it is the phase machine's, on the <see cref="BossBehaviour"/> this one is delegated to from
/// (M4-02 rule 6).
/// </remarks>
public enum WardenState
{
    /// <summary>Standing where it was spawned, unaware. The player has not come close enough.</summary>
    Idle,

    /// <summary>
    /// Closing, or planted and waiting out a cooldown. One state rather than two, for
    /// <see cref="SpitterState.Approach"/>'s reason: it is one question — <em>is there an attack
    /// I can make from here?</em> — and walking is what it does while the answer is no.
    /// </summary>
    Approach,

    /// <summary>
    /// Planted, shield raised, telegraphing the slam. The ring leaves on the tick this ends, from
    /// the ground it is standing on at that instant.
    /// </summary>
    Slam,

    /// <summary>
    /// The crack is already in the ground and arming under where the player was. The body holds
    /// the gesture for as long as the arm lasts; the fissure's own clock is what fires it.
    /// </summary>
    Fissure,

    /// <summary>Rooted and recovering for <c>EnemySpec.RecoverTime</c> — the window to close in.</summary>
    Recover,
}

/// <summary>
/// The first boss in the game, doing the three things a player can learn: a shield-slam shockwave
/// that expands from where it stood, ground fissures that open under your feet and are a place not
/// to be, and — through the phase machine it hangs off — Husks. GD §9.1 rules 1, 2, 4 and 7,
/// GD §9.2, AR §18.4.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the <c>inner</c> of a <see cref="BossBehaviour"/>, not a behaviour an
/// <see cref="EnemyAgent"/> builds for itself.</b> A boss's archetype is authored
/// <see cref="EnemyBehaviourKind.Boss"/> and <c>EnemyAgent.Initialise</c> deliberately leaves that
/// kind with no behaviour at all (M4-01b), because building one needs the <see cref="BossSpec"/>
/// the archetype does not name. So this is built where both halves are visible —
/// <c>EnemySystem.SpawnBoss</c> — and handed to the phase machine, which delegates every tick it
/// is not sitting out a beat. <b>The split is what makes <em>"the boss does not act during the
/// beat"</em> cost this class nothing:</b> there is no beat check anywhere below, because a
/// behaviour that is not ticked cannot act.
/// </para>
/// <para>
/// <b>Every attack telegraphs, and the number is the body's rather than this class's</b> (rule 1,
/// GD §9.1 rule 1). <see cref="TelegraphSeconds"/> is <c>EnemySpec.WindupTime</c> — the field
/// every other archetype's wind-up already comes from — floored at
/// <see cref="MinTelegraphSeconds"/>. <b>The floor is a clamp rather than a refusal</b>, and that
/// is a decision: GD §9.1 rule 1 is marked non-negotiable, so a boss retuned to 0.3 s must not be
/// playable at 0.3 s — and a hazard system that <em>threw</em> instead would end a live run over a
/// tuning mistake, which is worse than either. What the clamp cannot do is tell anyone the asset
/// disagrees with the fight, so a test over the shipped asset does
/// (<c>ContentValidationTests.Boss_EveryAttackTelegraphsLongEnough</c>).
/// </para>
/// <para>
/// <b>Half of rule 1 is missing and this class says so rather than pretending.</b> GD §9.1 rule 1
/// asks for a distinct visual <em>and</em> audio cue. There is no audio system in this project at
/// all; M7 owns the pass, and what ships here is the visual half plus the events a sound would
/// hang off (<c>EnemyTelegraph</c>, <c>ShockwaveEmitted</c>, <c>FissureArmed</c>).
/// </para>
/// <para>
/// <b>Both attacks have a safe answer that costs positioning</b> (GD §9.1 rule 2). The slam's is
/// to be outside <see cref="ShockwaveMaxRadius"/> when the edge arrives, which the shipped numbers
/// make reachable from melee range and only if the player starts moving on the telegraph: from
/// 2.5 m at the Oathbound's 3 m/s, the telegraph buys 2.7 m and the ring's own travel buys the
/// rest, landing at roughly 8 m against a 7 m ceiling. The fissure's is to step off it, which
/// costs the ground you were fighting from. Neither costs health, and neither is a reflex — both
/// are decisions taken during a window GD §9.1 rule 1 guarantees is at least 0.6 s long.
/// </para>
/// <para>
/// <b>Selection is cooldown and range first, and a draw only where those leave a real choice</b>
/// (rule 7). The stream is the run's <c>IRandom.Misc</c>, handed in rather than reached for, so a
/// boss fight replays identically from a saved run — which is the guarantee M3-08a's lazy draw
/// established for offers and which a boss must not break. It is <em>not</em> the
/// <c>Spawn</c> stream: a boss choosing its attacks out of the same sequence the director composes
/// waves from would make every later stage of a seeded run depend on how long this fight took
/// (ADR-0011).
/// </para>
/// <para>
/// <b>Every number that is not the body's is a constant here, and each is argued where it is
/// declared</b> — <see cref="SpitterBehaviour.RetreatFraction"/>'s and
/// <see cref="BossBehaviour.AddRingRadius"/>'s bargain. <c>EnemySpec</c> gains nothing: a
/// shockwave's speed is not a property an archetype has, and widening the type for one archetype
/// is what ADR-0006 exists to stop. The day a second boss slams (M7's Choirmother does not), the
/// numbers move onto a spec along with the reason.
/// </para>
/// <para>
/// <b>Why the per-tick dependencies are parked in fields</b> — <see cref="SpitterBehaviour"/>'s
/// reason exactly: <see cref="StateMachine{TState}"/> hands its tick handlers a <c>dt</c> and
/// nothing else, and capturing the context in closures would allocate one per tick per boss. They
/// are cleared in a <c>finally</c> so a handler reached from anywhere else cannot read a stale
/// clock.
/// </para>
/// </remarks>
public sealed class WardenBehaviour : IEnemyBehaviour
{
    /// <summary>The shortest telegraph GD §9.1 rule 1 permits, in seconds.</summary>
    /// <remarks>
    /// <b>The rule is marked non-negotiable, so it is a floor in code rather than a note in a
    /// document.</b> <see cref="TelegraphSeconds"/> clamps up to it, which means a Warden retuned
    /// to 0.3 s still gives the player the window the design promises — see the class remarks for
    /// why this is a clamp and not a refusal, and for where the disagreement gets reported.
    /// </remarks>
    public const float MinTelegraphSeconds = 0.6f;

    /// <summary>How fast a slam's ring grows, in metres per second.</summary>
    /// <remarks>
    /// <b>Chosen against the Oathbound's 3 m/s rather than picked.</b> The ring has to be fast
    /// enough that standing still is punished and slow enough that a player who starts moving on
    /// the telegraph gets out: at 8 m/s the edge catches a fleeing player, from 2.5 m and after a
    /// 0.9 s telegraph, at about 8 m — a metre past <see cref="ShockwaveMaxRadius"/>. At 12 it
    /// catches them at 7.2 m and the attack is unavoidable from melee; at 5 it never catches them
    /// at all and the attack is free to ignore.
    /// </remarks>
    public const float ShockwaveSpeed = 8f;

    /// <summary>How far a slam's ring reaches before it is over, in metres.</summary>
    /// <remarks>
    /// Seven, which is the far edge of the band the fight is actually fought in: the Oathbound's
    /// Censer reaches 8 m and acquires at 12, so a ceiling here means the safe ground is <em>just
    /// outside your own weapon range</em> — walking out costs you the attack you were making,
    /// which is precisely GD §9.1 rule 2's <em>"costs positioning, not health"</em>.
    /// </remarks>
    public const float ShockwaveMaxRadius = 7f;

    /// <summary>How wide a slam's band is, in metres.</summary>
    /// <remarks>
    /// The band's front edge is half of this ahead of the nominal radius and the edge is what
    /// bites (<see cref="ShockwaveSystem"/>), so this is a <em>drawing</em> width rather than a
    /// hit tolerance — 1.2 m reads as a ring rather than a line on a 6-inch screen, which is
    /// GD §9.1 rule 7's question and the one no Editor can answer.
    /// </remarks>
    public const float ShockwaveThickness = 1.2f;

    /// <summary>How close the player must be for a slam to be worth making, in metres.</summary>
    /// <remarks>
    /// Inside <see cref="ShockwaveMaxRadius"/> and not equal to it: a slam thrown at a player
    /// standing on the ceiling is one they escape by not moving, which teaches the wrong lesson.
    /// Five metres is comfortably inside the Censer's 8 m reach, so the attack answers the range
    /// the player actually chooses to fight at.
    /// </remarks>
    public const float SlamRange = 5f;

    /// <summary>Simulated seconds between two slams.</summary>
    /// <remarks>
    /// Six, so the attack is an event rather than a rhythm, and so a single ring is ever expanding
    /// at once — which is what <see cref="ShockwaveSystem.Capacity"/>'s four is headroom over.
    /// </remarks>
    public const float SlamCooldown = 6f;

    /// <summary>How far a fissure reaches, in metres.</summary>
    /// <remarks>
    /// 2.5 m, which is about a stride and a half: large enough to be worth drawing and to punish a
    /// player who only shuffles, small enough that one step off it is a complete answer.
    /// </remarks>
    public const float FissureRadius = 2.5f;

    /// <summary>How long a crack is drawn after it has gone off, in simulated seconds.</summary>
    /// <remarks>
    /// The window is cosmetic in V1 — a fissure bites once, at the end of its arm
    /// (<see cref="FissureSystem"/>) — and 0.6 s is long enough for the player to see where they
    /// were standing and short enough that the floor is not permanently cracked by minute three.
    /// </remarks>
    public const float FissureOpenSeconds = 0.6f;

    /// <summary>Simulated seconds between two fissures.</summary>
    /// <remarks>
    /// 3.5 s, shorter than <see cref="SlamCooldown"/> because the fissure is the answer to a
    /// player standing off: it is what stops the fight being won from 10 m with no risk, and it
    /// has to come round often enough to be a reason to move.
    /// </remarks>
    public const float FissureCooldown = 3.5f;

    /// <summary>
    /// The odds a slam is chosen when both attacks are available from where the boss is standing.
    /// </summary>
    /// <remarks>
    /// <b>The one draw this behaviour makes, and it is made only when the choice is real</b> (rule
    /// 7). Cooldowns and range decide every other tick; this decides the tick on which both would
    /// have been legal, and an even coin is what stops the fight having a fixed opening the player
    /// memorises.
    /// </remarks>
    public const float SlamOdds = 0.5f;

    private readonly EnemyAgent _agent;

    private readonly ShockwaveSystem _shockwaves;

    private readonly FissureSystem _fissures;

    private readonly IRandomStream _random;

    private readonly StateMachine<WardenState> _machine;

    /// <summary>Simulated run time as of the tick in progress. See the class remarks.</summary>
    private float _now;

    private IIntentSink _intents;

    private IDomainEvents _events;

    /// <summary>
    /// The simulated seconds each attack is next legal at — absolute, never counted down.
    /// </summary>
    /// <remarks>
    /// <c>Weapon</c>'s and <see cref="ZoneSystem"/>'s discipline (rule 5): a deadline compared
    /// against the clock cannot drift, where a timer decremented by <c>dt</c> sixty times a second
    /// does — and a tick that swallows a whole cooldown leaves the attack legal rather than
    /// skipping it.
    /// </remarks>
    private float _nextSlamAt;

    private float _nextFissureAt;

    /// <param name="agent">
    /// The body this drives — the boss's own. Its spec supplies the telegraph, the recovery and
    /// the damage; its blackboard supplies every fact.
    /// </param>
    /// <param name="shockwaves">Where a slam's ring is sent.</param>
    /// <param name="fissures">Where a crack is opened.</param>
    /// <param name="random">
    /// The run's <c>Misc</c> stream — the one draw selection makes, and the reason a seeded run
    /// replays a boss fight (rule 7). Handed in rather than reached for, because nothing in core
    /// holds a generator it was not given (ADR-0011).
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// Guarded, unlike <see cref="Tick"/>, for <see cref="SpitterBehaviour"/>'s reason: the type is
    /// public and reachable from <c>Soulvail.Tests.Core</c>, and the four references are checked
    /// once per boss rather than once per frame.
    /// </remarks>
    public WardenBehaviour(
        EnemyAgent agent,
        ShockwaveSystem shockwaves,
        FissureSystem fissures,
        IRandomStream random)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _shockwaves = shockwaves ?? throw new ArgumentNullException(nameof(shockwaves));
        _fissures = fissures ?? throw new ArgumentNullException(nameof(fissures));
        _random = random ?? throw new ArgumentNullException(nameof(random));

        _machine = new StateMachine<WardenState>(WardenState.Idle);

        _machine.OnTick(WardenState.Idle, TickIdle);

        _machine.OnTick(WardenState.Approach, TickApproach);

        _machine.OnEnter(WardenState.Slam, EnterSlam);
        _machine.OnTick(WardenState.Slam, TickSlam);

        _machine.OnEnter(WardenState.Fissure, EnterFissure);
        _machine.OnTick(WardenState.Fissure, TickFissure);

        _machine.OnEnter(WardenState.Recover, EnterRecover);
        _machine.OnTick(WardenState.Recover, TickRecover);

        // Started here rather than on the first Tick, so Current is meaningful the moment the boss
        // exists and Reset never has to ask whether the machine has run yet — SpitterBehaviour's
        // bargain.
        _machine.Start();
    }

    /// <summary>Which part of the fight this boss is in.</summary>
    public WardenState State => _machine.Current;

    /// <summary>
    /// How long every one of this boss's attacks telegraphs for, in simulated seconds.
    /// </summary>
    /// <remarks>
    /// <b>The body's authored <c>WindupTime</c>, floored at <see cref="MinTelegraphSeconds"/></b>
    /// (rule 1). One number for both attacks rather than two, because <c>EnemySpec</c> gains
    /// nothing in this task and a second authored telegraph is a field on a type this task has no
    /// business widening — the Warden's slam and its fissure wind up for the same length, which is
    /// also what makes the two read as the same creature doing two things.
    /// </remarks>
    public float TelegraphSeconds =>
        MathF.Max(_agent.Spec.WindupTime, MinTelegraphSeconds);

    /// <summary>
    /// The simulated second the next slam is legal at. Zero before the first one.
    /// </summary>
    public float NextSlamAt => _nextSlamAt;

    /// <summary>The simulated second the next fissure is legal at. Zero before the first one.</summary>
    public float NextFissureAt => _nextFissureAt;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Unguarded, unlike the constructor and for its reason: this runs sixty times a second for a
    /// living boss, and everything a null here could mean was refused once for the whole arena when
    /// <see cref="EnemyTickContext"/> was built.
    /// </para>
    /// <para>
    /// Allocates nothing: the machine's <c>Tick</c> is allocation-free by construction, the
    /// handlers are delegates built once, and everything below is struct arithmetic over fields.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _intents = ctx.Intents;
        _events = ctx.Events;

        try
        {
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            // Cleared even if a handler threw, so a half-finished tick cannot leave this holding
            // two ports the next caller did not supply.
            _intents = null;
            _events = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>The cooldowns go back to zero with the state</b>, which makes a recycled body a boss that
    /// can attack the moment it has a target — correct, because the first thing it meets is an
    /// arena it has just been spawned into at <c>SpawnDirector.MinPlayerDistance</c>, well beyond
    /// <see cref="SlamRange"/>. Anything the hazards themselves left behind is
    /// <c>RunSession</c>'s to clear, not this class's: a ring outlives the body that sent it, which
    /// is the whole of rule 2.
    /// </remarks>
    public void Reset()
    {
        _machine.Transition(WardenState.Idle);

        _nextSlamAt = 0f;
        _nextFissureAt = 0f;

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Notice the player and start closing — the Chaser's rule and the Chaser's range.</summary>
    private void TickIdle(float dt)
    {
        Stand();

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.AggroRange)
        {
            _machine.Transition(WardenState.Approach);
        }
    }

    /// <summary>
    /// Rule 7: pick an attack if one is legal from here, and walk if none is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The intent goes out before the transition</b>, whatever is decided —
    /// <c>SpitterBehaviour.TickApproach</c>'s rule and <see cref="IEnemyBehaviour.Tick"/>'s
    /// contract: one instruction per tick, in every state, because <c>EnemyView</c> folds gravity
    /// into the same <c>Move</c> that carries the walk.
    /// </para>
    /// <para>
    /// <b>Walking in follows the NavMesh path when there is one</b>, like every other archetype.
    /// There is no retreat: a Warden that backed off would be kiting, which is a Spitter's mind,
    /// and GD §9.2's hook asks the player to get <em>behind</em> this thing rather than to chase
    /// it.
    /// </para>
    /// <para>
    /// <b>It stops at <see cref="SlamRange"/> rather than at its own reach</b>, and does not close
    /// further while a cooldown runs. Standing on the player is neither an attack — this boss has
    /// no melee, by design: its three things are the slam, the crack and its Husks — nor readable,
    /// and a boss the size of a building parked on top of you is the one arrangement in which
    /// nothing at all can be seen.
    /// </para>
    /// </remarks>
    private void TickApproach(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        float distance = blackboard.DistanceToPlayer;

        if (TryChoose(distance, out WardenState attack))
        {
            Stand();

            _machine.Transition(attack);

            return;
        }

        if (distance <= SlamRange)
        {
            Stand();

            return;
        }

        Vector2 inbound = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        // The agent's stat, not the spec's float: depth scaling and M7-02's affixes live on the
        // stack behind it, so a stage-40 Warden closes at the scaled speed.
        Emit(Ground(inbound, _agent.MoveSpeed.Value), inbound);
    }

    /// <summary>Rule 1, the tell: announce the windup so the body can raise the shield.</summary>
    /// <remarks>
    /// The same <c>EnemyTelegraph</c> a Husk publishes, so M1-12's swell already draws it with
    /// nothing added — and it carries <see cref="TelegraphSeconds"/> rather than the raw authored
    /// number, so what is drawn is the window the player actually gets.
    /// </remarks>
    private void EnterSlam()
    {
        _agent.Blackboard.StateTimer = 0f;

        _events.Publish(new EnemyTelegraph(_agent.Id, TelegraphSeconds));
    }

    /// <summary>
    /// Rule 2: hold the telegraph, then send a ring out of the ground it is standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ring leaves on the tick the telegraph completed</b> rather than one later —
    /// <c>SpitterState.Release</c>'s care without a state to name it, because nothing outside this
    /// class needs the instant named: <c>ShockwaveEmitted</c> is what a view hangs off.
    /// </para>
    /// <para>
    /// <b>The origin is <c>_agent.Position</c> read at that instant</b>, which is where the body
    /// was standing when it slammed and not where it walks to afterwards (rule 2). It is the
    /// position the last snapshot reported, so it is a place in the world the player could see the
    /// boss standing on rather than one core invented.
    /// </para>
    /// <para>
    /// <b>A dropped ring is not a special case</b> (rule 8). <see cref="ShockwaveSystem.Emit"/>
    /// answers 0 when the table is full and this does not look: the Warden believes it slammed,
    /// which is the only reading that does not require a boss to know a hazard system's capacity —
    /// <c>ProjectileSystem.Fire</c>'s bargain, made again. The cooldown is spent either way, so a
    /// full table costs the boss the attack rather than making it spam one.
    /// </para>
    /// </remarks>
    private void TickSlam(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer < TelegraphSeconds)
        {
            return;
        }

        _shockwaves.Emit(
            _agent.Position,
            ShockwaveSpeed,
            ShockwaveMaxRadius,
            // The agent's stat, so GD §12.3's d(n) and M7-02's affixes are already in it.
            _agent.ContactDamage.Value,
            ShockwaveThickness,
            _now);

        _nextSlamAt = _now + SlamCooldown;

        _machine.Transition(WardenState.Recover);
    }

    /// <summary>
    /// Rule 3: the crack opens under the player's feet at this instant, and arms from there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opened on entry rather than at the end of a windup, and the arm <em>is</em> the
    /// telegraph</b> (rule 1). A boss that wound up and then placed a crack would telegraph twice
    /// for one attack, and the second telegraph — the crack itself, visible on the floor — is the
    /// one the player can actually act on, because it says <em>where</em> as well as
    /// <em>when</em>.
    /// </para>
    /// <para>
    /// <b>The position is the blackboard's, taken once</b> — this frame's, filled by
    /// <c>EnemySystem.Ingest</c> above the behaviour step — and the crack does not track (rule 3).
    /// A tracking fissure has no safe answer at all, which is exactly what GD §9.1 rule 2 forbids.
    /// </para>
    /// <para>
    /// The <c>EnemyTelegraph</c> goes out beside it so the body has a gesture to play for the same
    /// window, and a dropped fissure is not a special case, for <see cref="TickSlam"/>'s reason.
    /// </para>
    /// </remarks>
    private void EnterFissure()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        _fissures.Open(
            blackboard.PlayerPosition,
            FissureRadius,
            TelegraphSeconds,
            FissureOpenSeconds,
            _agent.ContactDamage.Value,
            _now);

        _nextFissureAt = _now + FissureCooldown;

        _events.Publish(new EnemyTelegraph(_agent.Id, TelegraphSeconds));
    }

    /// <summary>Rule 3: hold the gesture for as long as the crack is arming.</summary>
    /// <remarks>
    /// The boss is rooted for the arm and the crack is on its own clock, so the two end together
    /// without either reading the other — which is what lets a fissure outlive the body that
    /// opened it, exactly as a ring does.
    /// </remarks>
    private void TickFissure(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer >= TelegraphSeconds)
        {
            _machine.Transition(WardenState.Recover);
        }
    }

    private void EnterRecover()
    {
        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>The window GD §9.2's hook is about: rooted, and open from behind.</summary>
    /// <remarks>
    /// It goes back to <see cref="WardenState.Approach"/> rather than to
    /// <see cref="WardenState.Idle"/> whatever the distance — <c>SpitterBehaviour.TickRecover</c>'s
    /// rule and the same one: a boss that has attacked you knows where you are, and re-deciding
    /// aggro after every attack would let a player walk 40 m and switch the fight off.
    /// </remarks>
    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer >= _agent.Spec.RecoverTime)
        {
            _machine.Transition(WardenState.Approach);
        }
    }

    /// <summary>
    /// Rule 7: which attack, if any, is legal from <paramref name="distance"/> right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cooldown and range decide, and the draw happens only where both would have been
    /// legal.</b> That is what makes the fight deterministic in the sense that matters: the same
    /// seed and the same state give the same attack, and the sequence does not shift because an
    /// unrelated system drew from another stream.
    /// </para>
    /// <para>
    /// <b>A fissure has no range condition</b>, deliberately. It is the answer to a player who has
    /// walked out of slam range, so gating it on distance would leave a boss with nothing to do at
    /// 10 m — and a Warden that cannot reach a standing-off player is a fight that is won by
    /// standing off.
    /// </para>
    /// </remarks>
    private bool TryChoose(float distance, out WardenState attack)
    {
        bool slam = _now >= _nextSlamAt && distance <= SlamRange;
        bool fissure = _now >= _nextFissureAt;

        if (slam && fissure)
        {
            attack = _random.Chance(SlamOdds) ? WardenState.Slam : WardenState.Fissure;

            return true;
        }

        if (slam)
        {
            attack = WardenState.Slam;

            return true;
        }

        if (fissure)
        {
            attack = WardenState.Fissure;

            return true;
        }

        attack = WardenState.Idle;

        return false;
    }

    /// <summary>A ground-plane velocity: <paramref name="directionXZ"/> at <paramref name="speed"/>.</summary>
    private static Vector3 Ground(Vector2 directionXZ, float speed) =>
        new Vector3(directionXZ.X * speed, 0f, directionXZ.Y * speed);

    /// <summary>
    /// Emits a tick's worth of standing still, looking at the player.
    /// </summary>
    /// <remarks>
    /// The facing is live even where the velocity is zero — see <c>EnemyMoveIntent</c> on why the
    /// two are separate fields. A boss that kept staring through a telegraph is a boss whose
    /// attack the player can see coming, which is the whole of GD §9.1 rule 1.
    /// </remarks>
    private void Stand() => Emit(Vector3.Zero, _agent.Blackboard.DirectionToPlayer);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
