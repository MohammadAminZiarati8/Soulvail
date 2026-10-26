using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// Which part of its fight Old Croaker, the Jungle's first boss, is in.
/// </summary>
/// <remarks>
/// <see cref="WardenState"/>'s shape: every attack is <em>enter, telegraph, resolve, recover</em>.
/// A chain of hops is one <see cref="Slam"/> rather than a state per hop, because the hops differ
/// only in where they land.
/// </remarks>
public enum CroakerState
{
    /// <summary>Sitting where it was spawned, unaware. The player has not come close enough.</summary>
    Idle,

    /// <summary>Closing, or sitting and waiting out the slam's cooldown inside its range.</summary>
    Approach,

    /// <summary>
    /// Hopping: each hop telegraphs, arms a fissure where the player stood, carries the body there
    /// and lands in a shockwave. A later phase chains more hops, with a short gap between landings.
    /// </summary>
    Slam,

    /// <summary>
    /// The tongue lash (JG-04e): rooted, it telegraphs, and a lane armed at the wind-up's start
    /// fires down the player's line as the telegraph ends.
    /// </summary>
    Lash,

    /// <summary>The call on entering a phase, once the beat is over. Rooted.</summary>
    Croak,

    /// <summary>Rooted after the last landing — the window to close in from any side.</summary>
    Recover,
}

/// <summary>
/// Old Croaker's fighting: it approaches and hop-slams onto where the player stood, the landing
/// opening a fissure and sending a shockwave out of it; while the slam cools it lashes its tongue
/// down a lane at a player in mid-range; and it croaks on entering each phase. JG-04b and JG-04e;
/// GD §9.1 rules 1, 2 and 7.
/// </summary>
/// <remarks>
/// <para>
/// <b>The <c>inner</c> of a <see cref="BossBehaviour"/>, like <see cref="WardenBehaviour"/></b>, so
/// the beat costs this class nothing: a behaviour that is not ticked cannot act. The phase is read
/// off the agent's own <see cref="EnemyAgent.Behaviour"/> — the boss this one is the inner of —
/// rather than handed in, because the boss-behaviour factory builds the inner before the
/// <see cref="BossBehaviour"/> exists. Driven bare, with no boss above it, it is phase 0: one hop
/// and no croak.
/// </para>
/// <para>
/// <b>The hop is built from hazards that already exist.</b> The landing spot is a
/// <see cref="FissureSystem"/> circle armed at take-off for exactly the telegraph, so it fires on
/// its own clock on the tick the body lands; the impact is a <see cref="ShockwaveSystem"/> ring
/// from the fissure's centre. Core cannot place a body — a position is only ever what the snapshot
/// reports (AR §3) — so the move intent carries it there instead, at the one velocity that covers
/// take-off to landing in the telegraph.
/// </para>
/// <para>
/// <b>The telegraphs are constants matched to <c>Croaker.fbx</c>, not <c>EnemySpec.WindupTime</c></b>:
/// Slam's impact is frame 27, Lash's furthest reach frame 24 and Croak's call frame 30 at 30 fps, so
/// <see cref="SlamTelegraphSeconds"/>, <see cref="LashTelegraphSeconds"/> and
/// <see cref="CroakTelegraphSeconds"/> line up with the clips at speed 1. Each
/// <see cref="EnemyTelegraph"/> names its move so the body plays the right clip.
/// </para>
/// <para>
/// <b>No prediction</b>: a fissure goes where the player stands <em>now</em>. Leading the target
/// is the Strangler's lesson, not this boss's. The safe answer is to step out of the circle, then
/// out of the ring's path (GD §9.1 rule 2).
/// </para>
/// <para>
/// <b>No draw is made</b>: the slam wins whenever it is ready and in range, and the lash only fills
/// its cooldown, so range and the two cooldowns decide everything. The run's <c>Misc</c> stream is
/// still taken (rule 7), so a later random choice replays from a seed without a signature change.
/// </para>
/// <para>
/// Per-tick dependencies are parked in fields and cleared in a <c>finally</c>, for
/// <see cref="WardenBehaviour"/>'s reason: a closure would allocate per tick.
/// </para>
/// </remarks>
public sealed class CroakerBehaviour : IEnemyBehaviour
{
    /// <summary><see cref="EnemyTelegraph.Move"/> for a hop-slam.</summary>
    public const int SlamMove = 1;

    /// <summary><see cref="EnemyTelegraph.Move"/> for the tongue lash (JG-04e).</summary>
    public const int LashMove = 2;

    /// <summary><see cref="EnemyTelegraph.Move"/> for the croak.</summary>
    public const int CroakMove = 3;

    /// <summary>How close the player must be for a slam, in metres.</summary>
    public const float SlamRange = 8f;

    /// <summary>Simulated seconds from one slam's first take-off to the next's.</summary>
    public const float SlamCooldown = 8f;

    /// <summary>
    /// Simulated seconds of rest after any attack ends — the recover after a slam, the end of a
    /// lash — before the next may start. What keeps the lash from following the slam at once
    /// (owner's playtest, 2026-10-26: the attacks came too fast).
    /// </summary>
    public const float AttackGapSeconds = 2.5f;

    /// <summary>
    /// How long each hop telegraphs, in seconds: <c>Croaker.fbx</c>'s Slam impact, frame 27 at
    /// 30 fps. Above GD §9.1 rule 1's 0.6 s floor (<see cref="WardenBehaviour.MinTelegraphSeconds"/>).
    /// </summary>
    public const float SlamTelegraphSeconds = 0.9f;

    /// <summary>
    /// How long the lash telegraphs, in seconds: <c>Croaker.fbx</c>'s Lash at its furthest reach,
    /// frame 24 at 30 fps. The lane is armed for exactly this long, so it bites as the tongue lands.
    /// </summary>
    public const float LashTelegraphSeconds = 0.8f;

    /// <summary>The nearest the player may be for a lash, in metres. Closer is the slam's.</summary>
    public const float LashMinRange = 3f;

    /// <summary>The farthest the player may be for a lash, in metres — the lane's own length.</summary>
    public const float LashRange = 9f;

    /// <summary>Simulated seconds from one lash's wind-up to the next's.</summary>
    public const float LashCooldown = 8f;

    /// <summary>How far the lane runs from the mouth, in metres.</summary>
    public const float LashLength = 9f;

    /// <summary>Half the lane's width, in metres: one sidestep clears it.</summary>
    public const float LashHalfWidth = 0.8f;

    /// <summary>
    /// How long the croak telegraphs, in seconds: <c>Croaker.fbx</c>'s Croak call, frame 30 at 30 fps.
    /// </summary>
    public const float CroakTelegraphSeconds = 1.0f;

    /// <summary>Seconds between one landing and the next hop's take-off, in a chained slam.</summary>
    public const float HopGapSeconds = 0.4f;

    /// <summary>Seconds rooted after the last landing.</summary>
    public const float RecoverSeconds = 1.2f;

    /// <summary>
    /// The most hops one slam chains: phase 3's. A boss authored with more phases does not hop
    /// more, which also keeps three rings inside <see cref="ShockwaveSystem.Capacity"/>'s four.
    /// </summary>
    public const int MaxHops = 3;

    /// <summary>The landing circle's radius, in metres.</summary>
    public const float FissureRadius = 2.5f;

    /// <summary>How long a fired fissure is drawn, in seconds — the Warden's number.</summary>
    public const float FissureOpenSeconds = WardenBehaviour.FissureOpenSeconds;

    /// <summary>How fast a landing's ring grows, in metres per second.</summary>
    public const float ShockwaveSpeed = 7f;

    /// <summary>How far a landing's ring reaches, in metres.</summary>
    public const float ShockwaveMaxRadius = 6f;

    /// <summary>How wide a landing's band is drawn, in metres — the Warden's number.</summary>
    public const float ShockwaveThickness = WardenBehaviour.ShockwaveThickness;

    private readonly EnemyAgent _agent;

    private readonly ShockwaveSystem _shockwaves;

    private readonly FissureSystem _fissures;

    private readonly LaneSystem _lanes;

    private readonly StateMachine<CroakerState> _machine;

    private float _now;

    private IIntentSink _intents;

    private IDomainEvents _events;

    /// <summary>The simulated second the next slam is legal at — absolute, never counted down.</summary>
    private float _nextSlamAt;

    /// <summary>The simulated second the next lash is legal at — absolute, never counted down.</summary>
    private float _nextLashAt;

    /// <summary>The simulated second any attack is legal at: the last one's end plus the gap.</summary>
    private float _nextAttackAt;

    /// <summary>The boss phase this behaviour last acted in; a change is a croak.</summary>
    private int _seenPhase;

    /// <summary>How many hops this slam makes, fixed at its first take-off.</summary>
    private int _hopCount;

    /// <summary>How many hops of this slam have landed.</summary>
    private int _hopsLanded;

    /// <summary>In the air (a hop's telegraph) rather than sitting in the gap between two hops.</summary>
    private bool _airborne;

    /// <summary>Where the hop in progress lands: the fissure's centre.</summary>
    private Vector3 _landing;

    /// <summary>The ground velocity that carries the body from take-off to the landing in the telegraph.</summary>
    private Vector3 _hopVelocity;

    /// <param name="agent">The boss's own body.</param>
    /// <param name="shockwaves">Where a landing's ring is sent.</param>
    /// <param name="fissures">Where a landing circle is armed.</param>
    /// <param name="lanes">Where a lash's lane is armed.</param>
    /// <param name="random">The run's <c>Misc</c> stream (rule 7).</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public CroakerBehaviour(
        EnemyAgent agent,
        ShockwaveSystem shockwaves,
        FissureSystem fissures,
        LaneSystem lanes,
        IRandomStream random)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _shockwaves = shockwaves ?? throw new ArgumentNullException(nameof(shockwaves));
        _fissures = fissures ?? throw new ArgumentNullException(nameof(fissures));
        _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));

        // Guarded and not yet held: nothing draws (see the class remarks), and a field that is
        // never read is a warning rather than a promise.
        if (random is null)
        {
            throw new ArgumentNullException(nameof(random));
        }

        _machine = new StateMachine<CroakerState>(CroakerState.Idle);

        _machine.OnTick(CroakerState.Idle, TickIdle);

        _machine.OnTick(CroakerState.Approach, TickApproach);

        _machine.OnEnter(CroakerState.Slam, EnterSlam);
        _machine.OnTick(CroakerState.Slam, TickSlam);

        _machine.OnEnter(CroakerState.Lash, EnterLash);
        _machine.OnTick(CroakerState.Lash, TickLash);

        _machine.OnEnter(CroakerState.Croak, EnterCroak);
        _machine.OnTick(CroakerState.Croak, TickCroak);

        _machine.OnEnter(CroakerState.Recover, EnterRecover);
        _machine.OnTick(CroakerState.Recover, TickRecover);

        _machine.Start();
    }

    /// <summary>Which part of the fight this boss is in.</summary>
    public CroakerState State => _machine.Current;

    /// <summary>The simulated second the next slam is legal at. Zero before the first one.</summary>
    public float NextSlamAt => _nextSlamAt;

    /// <summary>The simulated second the next lash is legal at. Zero before the first one.</summary>
    public float NextLashAt => _nextLashAt;

    /// <summary>The simulated second the next attack of either kind is legal at. Zero before the first.</summary>
    public float NextAttackAt => _nextAttackAt;

    /// <summary>How many hops the slam in progress makes; 0 before the first.</summary>
    public int HopCount => _hopCount;

    /// <summary>How many hops of the slam in progress have landed.</summary>
    public int HopsLanded => _hopsLanded;

    /// <inheritdoc />
    /// <remarks>
    /// <b>The phase check comes before the machine ticks</b>, so the first tick after a beat — the
    /// first this behaviour gets — is the croak's first, whatever it was doing when the beat began.
    /// A hop interrupted by a phase change is abandoned; the fissure it armed fires anyway, on its
    /// own clock.
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _intents = ctx.Intents;
        _events = ctx.Events;

        try
        {
            int phase = CurrentPhase();

            if (phase != _seenPhase)
            {
                _seenPhase = phase;

                if (_machine.Current == CroakerState.Croak)
                {
                    // Already croaking: a second crossing restarts the call rather than being lost.
                    EnterCroak();
                }
                else
                {
                    _machine.Transition(CroakerState.Croak);
                }
            }

            _machine.Tick(ctx.Dt);
        }
        finally
        {
            _intents = null;
            _events = null;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _machine.Transition(CroakerState.Idle);

        _nextSlamAt = 0f;
        _nextLashAt = 0f;
        _nextAttackAt = 0f;
        _seenPhase = 0;
        _hopCount = 0;
        _hopsLanded = 0;
        _airborne = false;

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>The phase of the boss this is the inner of, or 0 when driven bare.</summary>
    private int CurrentPhase() => _agent.Behaviour is BossBehaviour boss ? boss.Phase : 0;

    private void TickIdle(float dt)
    {
        Stand();

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.AggroRange)
        {
            _machine.Transition(CroakerState.Approach);
        }
    }

    /// <summary>
    /// Rule 1: close until the player is inside <see cref="SlamRange"/>, slam when it is off
    /// cooldown — and, while it cools, lash a player between <see cref="LashMinRange"/> and
    /// <see cref="LashRange"/> (JG-04e rule 4).
    /// </summary>
    private void TickApproach(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        float distance = blackboard.DistanceToPlayer;

        // Resting after the last attack: it still closes, but nothing starts.
        bool rested = _now >= _nextAttackAt;

        // The slam is cooling, so the lash cannot steal a slam's turn: it only fills the gap.
        if (rested
            && _now < _nextSlamAt
            && _now >= _nextLashAt
            && distance >= LashMinRange
            && distance <= LashRange
            && blackboard.DirectionToPlayer != Vector2.Zero)
        {
            Stand();

            _machine.Transition(CroakerState.Lash);

            return;
        }

        if (distance <= SlamRange)
        {
            Stand();

            if (rested && _now >= _nextSlamAt)
            {
                _machine.Transition(CroakerState.Slam);
            }

            return;
        }

        Vector2 inbound = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        // The agent's stat, so depth scaling and affixes are already in it.
        float speed = _agent.MoveSpeed.Value;

        Emit(new Vector3(inbound.X * speed, 0f, inbound.Y * speed), inbound);
    }

    /// <summary>Rule 4: the chain's length is fixed by the phase at the first take-off.</summary>
    private void EnterSlam()
    {
        _hopCount = Math.Min(_seenPhase + 1, MaxHops);
        _hopsLanded = 0;

        // The cooldown runs from the first take-off, so a longer chain does not buy a longer gap.
        _nextSlamAt = _now + SlamCooldown;

        TakeOff();
    }

    /// <summary>
    /// Rule 2: one hop's telegraph, and a fissure armed for exactly that long where the player
    /// stands now.
    /// </summary>
    private void TakeOff()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        _airborne = true;
        _landing = blackboard.PlayerPosition;

        Vector3 from = _agent.Position;

        _hopVelocity = new Vector3(
            (_landing.X - from.X) / SlamTelegraphSeconds,
            0f,
            (_landing.Z - from.Z) / SlamTelegraphSeconds);

        _events.Publish(new EnemyTelegraph(_agent.Id, SlamTelegraphSeconds, SlamMove));

        // A dropped fissure (a full table) is not a special case — WardenBehaviour's rule 8.
        _fissures.Open(
            _landing,
            FissureRadius,
            SlamTelegraphSeconds,
            FissureOpenSeconds,
            _agent.ContactDamage.Value,
            _now);
    }

    /// <summary>Rules 3 and 4: fly the hop, land in a ring, and either hop again or recover.</summary>
    /// <remarks>
    /// The hop velocity goes out on every airborne tick up to and including the landing one, so a
    /// body that follows its intents covers the whole of take-off to landing in the telegraph.
    /// </remarks>
    private void TickSlam(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        if (!_airborne)
        {
            Stand();

            blackboard.StateTimer += dt;

            if (blackboard.StateTimer >= HopGapSeconds)
            {
                TakeOff();
            }

            return;
        }

        Emit(_hopVelocity, Facing(_hopVelocity));

        blackboard.StateTimer += dt;

        if (blackboard.StateTimer < SlamTelegraphSeconds)
        {
            return;
        }

        // The fissure fires on its own clock this tick; the ring leaves from its centre.
        _shockwaves.Emit(
            _landing,
            ShockwaveSpeed,
            ShockwaveMaxRadius,
            _agent.ContactDamage.Value,
            ShockwaveThickness,
            _now);

        _hopsLanded++;
        _airborne = false;
        blackboard.StateTimer = 0f;

        if (_hopsLanded >= _hopCount)
        {
            _machine.Transition(CroakerState.Recover);
        }
    }

    /// <summary>
    /// JG-04e rule 4: the wind-up's telegraph, and a lane armed for exactly that long down the
    /// player's line as it is now. Never revised afterwards — the sidestep is the answer.
    /// </summary>
    private void EnterLash()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        // From the wind-up, like the slam's from its first take-off.
        _nextLashAt = _now + LashCooldown;

        Vector2 aim = blackboard.DirectionToPlayer;

        _events.Publish(new EnemyTelegraph(_agent.Id, LashTelegraphSeconds, LashMove));

        // A dropped lane (a full table) is not a special case — the fissure's rule 8.
        _lanes.Open(
            _agent.Position,
            new Vector3(aim.X, 0f, aim.Y),
            LashLength,
            LashHalfWidth,
            LashTelegraphSeconds,
            _agent.ContactDamage.Value,
            _now);
    }

    /// <summary>Rooted until the tongue lands, then back to approaching.</summary>
    private void TickLash(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer >= LashTelegraphSeconds)
        {
            _nextAttackAt = _now + AttackGapSeconds;

            _machine.Transition(CroakerState.Approach);
        }
    }

    /// <summary>Rule 5: the call, telegraphed for its clip.</summary>
    private void EnterCroak()
    {
        _agent.Blackboard.StateTimer = 0f;

        _airborne = false;

        _events.Publish(new EnemyTelegraph(_agent.Id, CroakTelegraphSeconds, CroakMove));
    }

    private void TickCroak(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer >= CroakTelegraphSeconds)
        {
            _machine.Transition(CroakerState.Approach);
        }
    }

    private void EnterRecover()
    {
        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 6: rooted for <see cref="RecoverSeconds"/>, then back to approaching, never to idle.</summary>
    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand();

        if (blackboard.StateTimer >= RecoverSeconds)
        {
            _nextAttackAt = _now + AttackGapSeconds;

            _machine.Transition(CroakerState.Approach);
        }
    }

    /// <summary>The direction of a ground velocity, or the player's when there is none.</summary>
    private Vector2 Facing(Vector3 velocity)
    {
        var direction = new Vector2(velocity.X, velocity.Z);

        return direction == Vector2.Zero
            ? _agent.Blackboard.DirectionToPlayer
            : Vector2.Normalize(direction);
    }

    private void Stand() => Emit(Vector3.Zero, _agent.Blackboard.DirectionToPlayer);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
