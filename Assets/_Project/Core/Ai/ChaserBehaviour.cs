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
/// Which part of its attack a chaser is in. GD §8.1's Husk loop, and the shape every melee
/// archetype after it reuses.
/// </summary>
/// <remarks>
/// Five states, flat, no hierarchy — what <see cref="StateMachine{TState}"/> was built for. The one
/// that looks odd is <see cref="Strike"/>, which lasts exactly one tick: the damage is resolved on
/// the way in, and the state exists so that the moment of the hit is nameable by anything watching
/// — a sound, a hit-stop, a test — rather than being an invisible edge between a windup and a
/// recovery.
/// </remarks>
public enum ChaserState
{
    /// <summary>Spawned and unaware. Nothing moves; the player is too far away to have been noticed.</summary>
    Idle,

    /// <summary>Walking at the player at <c>EnemyAgent.MoveSpeed</c>, depth scaling included.</summary>
    Chase,

    /// <summary>Stopped, facing the player, telegraphing the strike for <c>EnemySpec.WindupTime</c>.</summary>
    Windup,

    /// <summary>The damage frame. One tick.</summary>
    Strike,

    /// <summary>Rooted and vulnerable for <c>EnemySpec.RecoverTime</c> — the window the player is meant to punish.</summary>
    Recover,

    /// <summary>A leaper's tell: stands facing the player for <c>LeapSpec.CrouchSeconds</c>, the landing fixed (JG-21a).</summary>
    Crouch,

    /// <summary>Flying a straight line to the fixed landing in <c>LeapSpec.AirSeconds</c>. Bites nothing.</summary>
    Air,

    /// <summary>The landing's bite. One tick, <see cref="Strike"/>'s shape.</summary>
    Land,
}

/// <summary>
/// The first enemy that fights back: a Husk that walks at the player, plants itself, telegraphs,
/// hits, and stands there long enough to be hit back. Every decision is core's; the body only walks
/// where an <see cref="EnemyMoveIntent"/> tells it to. See AR §3, §9 and GD §8.1, §9.1 rule 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole fight is in the numbers the archetype carries, and none of them is repeated
/// here.</b> Reach, windup and recovery are read off the agent's spec every time they are used;
/// speed and contact damage are read off the agent's own <c>Stat</c>s, whose bases the spec seeds
/// and whose stacks depth scaling (M2-03) and Elite affixes (M7-02) write to. Either way an
/// archetype that wants a slower windup edits its asset and nothing else. Aggro range joined them
/// in M2-06, as <see cref="EnemySpec.AggroRange"/>, once a second behaviour needed to ask the same
/// question. The single constant below is what is left of the numbers that belong to the
/// <em>behaviour</em> rather than to any archetype — how far you have to get for a windup to be
/// abandoned — and it is commented where it is declared.
/// </para>
/// <para>
/// <b>It reads perception and writes working memory, and never the other way round.</b> Everything
/// it knows about the world arrives on <see cref="EnemyBlackboard"/>'s perception half, filled by
/// <c>EnemySystem.Ingest</c> earlier in the same tick; the only field it writes is
/// <see cref="EnemyBlackboard.StateTimer"/>. That is AR §9's discipline, and it is what makes a
/// behaviour testable by handing it a blackboard instead of a world.
/// </para>
/// <para>
/// <b>Why the per-tick dependencies are parked in fields.</b> <see cref="StateMachine{TState}"/>
/// hands its tick handlers an <c>Action&lt;float&gt;</c> — <c>dt</c> and nothing else — so the clock,
/// the player and the two ports are unpacked from the <see cref="EnemyTickContext"/> on the way into
/// <see cref="Tick"/> and read by the handlers. Capturing them in closures instead would allocate a
/// closure per tick per enemy, which at sixty enemies is exactly the per-frame garbage AR §14 bans.
/// They are cleared on the way out so that a handler reached from anywhere else cannot read a stale
/// player — which is also why the context itself is never stored (M2-07b rule 2).
/// </para>
/// <para>
/// <b>One instance per agent, built once and reset.</b> <c>EnemyAgent</c> creates it on the first
/// spawn whose archetype needs one and keeps it for the agent's life, the same bargain
/// <c>Health</c> and <c>EnemyBlackboard</c> make: the registry recycles agents, and a fresh state
/// machine per spawn would allocate three dictionaries and nine delegates on a path a wave walks
/// sixty times.
/// </para>
/// </remarks>
public sealed class ChaserBehaviour : IEnemyBehaviour
{
    /// <summary>
    /// How far past its reach the player must get for a windup to be abandoned, as a multiple of
    /// <see cref="EnemySpec.Reach"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately greater than one, and that gap is the mechanic. At exactly reach the windup
    /// would cancel on the first centimetre of drift — including the enemy's own settling — and a
    /// telegraph that never resolves teaches the player nothing. At 1.5 the player has to genuinely
    /// leave, which is what makes stepping out of a windup a decision rather than an accident.
    /// </para>
    /// <para>
    /// It is not the same test as the strike's. Leaving the cancel band aborts the attack; leaving
    /// only the reach band lets the swing happen and miss (rule 4). Those are two different things
    /// on screen and they are meant to be.
    /// </para>
    /// </remarks>
    public const float WindupCancelReachMultiplier = 1.5f;

    /// <summary>Seconds from a chase's start until a leaper may first crouch (JG-21a rule 1).</summary>
    public const float FirstLeapDelaySeconds = 1f;

    /// <summary>Metres of flight left that count as arrived, so float drift never adds a tick.</summary>
    private const float ArrivedEpsilon = 1e-4f;

    private readonly EnemyAgent _agent;
    private readonly StateMachine<ChaserState> _machine;

    /// <summary>Simulated run time as of the tick in progress. See the class remarks.</summary>
    private float _now;

    private PlayerCombat _player;
    private PetSystem _pets;
    private int _attackPetId;
    private IIntentSink _intents;
    private IDomainEvents _events;

    /// <summary>Where the leap lands, fixed when the crouch begins.</summary>
    private Vector3 _leapTarget;

    /// <summary>The flight's XZ direction, unit or zero.</summary>
    private Vector2 _leapDirection;

    /// <summary>The flight's speed: distance over <c>AirSeconds</c>.</summary>
    private float _leapSpeed;

    /// <summary>Metres of flight still asked for, so the last tick is cut short rather than overshooting.</summary>
    private float _leapRemaining;

    /// <summary>
    /// Run time the flight ends at, whatever happened to it. Absolute, so a stun (behaviour not
    /// ticked) or a root (moves zeroed) leaves the body short and it lands where it stopped (rule 6)
    /// without the behaviour knowing crowd control exists (AR §18.4).
    /// </summary>
    private float _airEndsAt;

    /// <param name="agent">The enemy this drives. Its spec supplies every number; its blackboard, every fact.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    /// <remarks>
    /// Guarded even though <c>EnemyAgent</c> is the only caller and passes <c>this</c>, because
    /// unlike the registry's internal constructors this type is public and reachable from
    /// <c>Soulvail.Tests.Core</c> — which is exactly how <c>ChaserBehaviourTests</c> builds one.
    /// </remarks>
    public ChaserBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        _machine = new StateMachine<ChaserState>(ChaserState.Idle);

        _machine.OnTick(ChaserState.Idle, TickIdle);

        _machine.OnEnter(ChaserState.Chase, EnterChase);
        _machine.OnTick(ChaserState.Chase, TickChase);

        _machine.OnEnter(ChaserState.Windup, EnterWindup);
        _machine.OnTick(ChaserState.Windup, TickWindup);

        _machine.OnEnter(ChaserState.Strike, EnterStrike);
        _machine.OnTick(ChaserState.Strike, TickStrike);

        _machine.OnEnter(ChaserState.Recover, EnterRecover);
        _machine.OnTick(ChaserState.Recover, TickRecover);

        _machine.OnEnter(ChaserState.Crouch, EnterCrouch);
        _machine.OnTick(ChaserState.Crouch, TickCrouch);

        _machine.OnTick(ChaserState.Air, TickAir);

        _machine.OnEnter(ChaserState.Land, EnterLand);
        _machine.OnTick(ChaserState.Land, TickLand);

        // Started here rather than on the first Tick, so Current is meaningful the moment the agent
        // exists and Reset never has to ask whether the machine has run yet.
        _machine.Start();
    }

    /// <summary>Which part of the loop this enemy is in.</summary>
    public ChaserState State => _machine.Current;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Exactly one <see cref="EnemyMoveIntent"/> per tick, in every state including
    /// <see cref="ChaserState.Idle"/>.</b> The M1-18 spec only asks for one in Chase and Windup;
    /// emitting in all five is a deliberate widening, and the reason is the body: <c>EnemyView</c>
    /// applies gravity inside the same <c>CharacterController.Move</c> that carries the walk, so a
    /// tick with no intent is a tick an enemy is not pinned to the floor by. Making the cadence
    /// unconditional also makes it the same promise <c>RunSession.TickBody</c> makes for the player
    /// — one instruction per tick, never none — which is the rule that stops a body reapplying stale
    /// velocity for ever. As of M2-07b it is the seam's rule rather than this class's.
    /// </para>
    /// <para>
    /// Unguarded, unlike a constructor: this runs sixty times a second for every living enemy, and
    /// everything a null here could mean was refused once for the whole arena when the
    /// <see cref="EnemyTickContext"/> was built. The same asymmetry <c>RunSession.Tick</c> draws
    /// with its snapshot.
    /// </para>
    /// <para>
    /// <see cref="EnemyTickContext.Projectiles"/> is deliberately ignored: a Husk throws nothing.
    /// The context is what a behaviour <em>may</em> reach, not what it must.
    /// </para>
    /// <para>
    /// Allocates nothing: the machine's <c>Tick</c> is allocation-free by construction, the handlers
    /// are delegates built once, and everything below is struct arithmetic over fields.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _player = ctx.Player;
        _pets = ctx.Pets;
        _intents = ctx.Intents;
        _events = ctx.Events;

        try
        {
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            // Cleared even if a handler threw, so a half-finished tick cannot leave this holding a
            // player and two ports that the next caller did not supply.
            _player = null;
            _pets = null;
            _intents = null;
            _events = null;
        }
    }

    /// <summary>
    /// Back to an unaware enemy standing still: <see cref="ChaserState.Idle"/> with a blank timer.
    /// </summary>
    /// <remarks>
    /// What a recycled agent gets instead of a new behaviour (<c>EnemyAgent.Initialise</c>). The
    /// transition runs the current state's exit handlers, which is correct and cheap — there are
    /// none — and is skipped outright when the machine is already idle.
    /// </remarks>
    public void Reset()
    {
        _machine.Transition(ChaserState.Idle);

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 1: notice the player and start walking.</summary>
    /// <remarks>
    /// <para>
    /// No intent of its own beyond the standing-still one every state emits — an idle Husk is not
    /// facing anywhere in particular, so the facing goes out as zero and the body keeps whatever
    /// rotation it was spawned with.
    /// </para>
    /// <para>
    /// The range is the archetype's as of M2-06, off <see cref="EnemySpec.AggroRange"/>, where it
    /// used to be a <c>const</c> on this class. Nothing changed on screen — all three authored
    /// archetypes carry the 30 this constant held — and the move is what lets the Spitter of M2-07b
    /// ask the same question without depending on the chaser.
    /// </para>
    /// </remarks>
    private void TickIdle(float dt)
    {
        Stand(Vector2.Zero);

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.AggroRange)
        {
            _machine.Transition(ChaserState.Chase);
        }
    }

    /// <summary>
    /// Every chase starts at the top of a hop, for an archetype that hops; a leaper's next leap is
    /// at least <see cref="FirstLeapDelaySeconds"/> away, and never sooner than its cooldown allows.
    /// </summary>
    private void EnterChase()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        if (_agent.Spec.Leap != null)
        {
            blackboard.NextAttackAt = MathF.Max(blackboard.NextAttackAt, _now + FirstLeapDelaySeconds);
        }
    }

    /// <summary>Rule 2: walk at the player, and plant when close enough to swing.</summary>
    /// <remarks>
    /// <para>
    /// The path direction wins when there is one, because it is the only one that goes round a
    /// pillar; the straight line is the fallback, which is what a chaser has until M1-19 bakes a
    /// NavMesh. Both are unit vectors or zero — <c>EnemySystem</c> refuses to normalise a separation
    /// too small to have a direction — so a zero here is "nowhere to go", not "north".
    /// </para>
    /// <para>
    /// The reach test comes after the move, so the tick that arrives in range still spends its
    /// intent walking. One frame of walk against a 0.4 s windup is invisible, and the alternative
    /// — testing first — makes an enemy that stops one tick early at a distance it then never
    /// closes.
    /// </para>
    /// </remarks>
    private void TickChase(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        Vector2 direction = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        // The agent's stat, not the spec's float: depth scaling and M7-02's affixes live on the
        // stack behind it, and the spec's number is only its base (M2-03 rule 7).
        // A hopper moves only in the air (HopGait). The timer counts from the chase's start, so the
        // first hop leaves on the first tick; a walker gets the speed unchanged.
        float speed = HopGait.Speed(
            _agent.MoveSpeed.Value,
            _agent.Spec.HopTime,
            _agent.Spec.HopRestTime,
            ref blackboard.StateTimer,
            dt);

        Emit(new Vector3(direction.X * speed, 0f, direction.Y * speed), direction);

        if (blackboard.DistanceToPlayer <= _agent.Spec.Reach)
        {
            _machine.Transition(ChaserState.Windup);

            return;
        }

        // JG-21a rule 1: a leaper in its band, off cooldown, crouches. No LeapSpec, no change (rule 7).
        LeapSpec leap = _agent.Spec.Leap;

        if (leap != null
            && _now >= blackboard.NextAttackAt
            && blackboard.DistanceToPlayer >= leap.MinRange
            && blackboard.DistanceToPlayer <= leap.MaxRange)
        {
            _machine.Transition(ChaserState.Crouch);
        }
    }

    /// <summary>Rule 3, the tell: announce the windup so the body can show it.</summary>
    /// <remarks>
    /// GD §9.1 rule 1 — every attack is telegraphed. The event carries the duration rather than
    /// leaving the view to look it up, because the view has no catalog and no spec: it has an id and
    /// a number of seconds to fill, which is all a pulse or a ring needs.
    /// </remarks>
    private void EnterWindup()
    {
        _attackPetId = _agent.Blackboard.QuarryPetId;
        _agent.Blackboard.StateTimer = 0f;

        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));
    }

    /// <summary>Rule 3: stand, stare, and either swing or give up on it.</summary>
    /// <remarks>
    /// <para>
    /// The cancel is tested before the timer, so a player who leaves on the same tick the windup
    /// completes has beaten it. That ordering is the one the player can feel: the alternative would
    /// let a swing they had already escaped resolve — and then miss on rule 4's reach test anyway,
    /// which is the same outcome reached by a route that looks like a bug.
    /// </para>
    /// <para>
    /// <see cref="EnemyBlackboard.StateTimer"/> rather than
    /// <see cref="StateMachine{TState}.TimeInState"/>, which tracks the same seconds. AR §9 puts an
    /// enemy's working memory on its blackboard, where anything inspecting the enemy can read it,
    /// and the machine's own counter is bookkeeping that only the machine can see.
    /// </para>
    /// </remarks>
    private void TickWindup(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        // Face, do not move. The stare is the tell, so the facing is live even though the velocity
        // is zero — see EnemyMoveIntent on why the two are separate fields.
        Stand(blackboard.DirectionToPlayer);

        if (blackboard.DistanceToPlayer > _agent.Spec.Reach * WindupCancelReachMultiplier)
        {
            _machine.Transition(ChaserState.Chase);

            return;
        }

        if (blackboard.StateTimer >= _agent.Spec.WindupTime)
        {
            _machine.Transition(ChaserState.Strike);
        }
    }

    /// <summary>Rule 4: the damage frame.</summary>
    /// <remarks>
    /// <para>
    /// Resolved on entry rather than on the strike's own tick, so the hit lands on the tick the
    /// windup completed — the frame the player was watching — instead of one frame later.
    /// </para>
    /// <para>
    /// <b>The reach is re-tested here and that is not a duplicate of the cancel.</b> A player who
    /// backed off to somewhere between the reach and the cancel band gets swung at and missed, which
    /// is the honest picture of what happened. i-frames are not consulted at all:
    /// <c>PlayerCombat.ApplyDamage</c> owns that question and publishes a blocked
    /// <c>PlayerDamaged</c> either way, so a strike that arrives during a dodge is still a strike
    /// that arrived.
    /// </para>
    /// <para>
    /// <b>And a strike at a corpse hurts nobody</b> (M5-03 rule 7). While a Shroudstep's decoy
    /// stands, <c>DistanceToPlayer</c> is the distance to <em>it</em> — so a Husk that walked to a
    /// decoy six metres from the player is inside its reach of a thing with no hit points, and
    /// swinging at it must not reach across the arena. This is the only place in the game that
    /// needed telling: every other way an enemy hurts the player resolves against the real player
    /// position and misses by construction — a Spitter's bolt is landed by <c>ProjectileSystem</c>
    /// and a Bloater's blast by <c>EnemySystem.Explode</c>, both against <c>RunState.PlayerPosition</c>.
    /// The windup, the cancel and the recovery are deliberately left alone: the Husk should be seen
    /// to commit to the corpse, which is what makes the three seconds legible.
    /// </para>
    /// </remarks>
    private void EnterStrike()
    {
        _agent.Blackboard.StateTimer = 0f;

        HitQuarry(_agent.ContactDamage.Value, _agent.Spec.Reach);
    }

    /// <summary>Rule 4: one tick, then the punish window opens.</summary>
    private void TickStrike(float dt)
    {
        Stand(_agent.Blackboard.DirectionToPlayer);

        _machine.Transition(ChaserState.Recover);
    }

    private void EnterRecover()
    {
        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 5: rooted, facing the player, for as long as the archetype says.</summary>
    /// <remarks>
    /// It goes back to <see cref="ChaserState.Chase"/> rather than to <see cref="ChaserState.Idle"/>
    /// whatever the distance: an enemy that has hit you knows where you are, and re-deciding aggro
    /// after every swing would let a player walk 30 m and switch it off. Chase's own reach test
    /// sends it straight back into a windup if the player is still standing there, which is the
    /// intended pressure.
    /// </remarks>
    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.DirectionToPlayer);

        if (blackboard.StateTimer >= _agent.Spec.RecoverTime)
        {
            _machine.Transition(ChaserState.Chase);
        }
    }

    /// <summary>JG-21a rule 2: fix the landing where the player stands now, and announce it.</summary>
    private void EnterCrouch()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;
        LeapSpec leap = _agent.Spec.Leap;

        blackboard.StateTimer = 0f;

        // The quarry's position, as every other attack aims: a decoy draws the leap too.
        _attackPetId = blackboard.QuarryPetId;
        _leapTarget = new Vector3(blackboard.PlayerPosition.X, _agent.Position.Y, blackboard.PlayerPosition.Z);

        _events.Publish(new EnemyLeapAimed(
            _agent.Id, _agent.Position, _leapTarget, leap.Radius, leap.CrouchSeconds, leap.AirSeconds));
        _events.Publish(new EnemyTelegraph(_agent.Id, leap.CrouchSeconds, move: 1));
    }

    /// <summary>Rule 2: stand facing the player; never cancelled — the dodge is leaving the ring.</summary>
    /// <remarks>
    /// The tick that completes the crouch already flies, so the flight gets exactly
    /// <c>AirSeconds</c> of moves and still one intent per tick. The flight is set up here rather
    /// than on entering <see cref="ChaserState.Air"/>: a transition asked for inside a handler runs
    /// its enter after the handler returns, too late for this tick's move.
    /// </remarks>
    private void TickCrouch(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        if (blackboard.StateTimer >= _agent.Spec.Leap.CrouchSeconds)
        {
            BeginFlight();

            _machine.Transition(ChaserState.Air);

            Fly(dt);

            return;
        }

        Stand(blackboard.DirectionToPlayer);
    }

    /// <summary>Rule 3: velocity is the distance over the air time, set once.</summary>
    private void BeginFlight()
    {
        LeapSpec leap = _agent.Spec.Leap;

        var offset = new Vector2(_leapTarget.X - _agent.Position.X, _leapTarget.Z - _agent.Position.Z);
        float distance = offset.Length();

        _leapDirection = distance > ArrivedEpsilon ? offset / distance : Vector2.Zero;
        _leapRemaining = distance;
        _leapSpeed = distance / leap.AirSeconds;
        _airEndsAt = _now + leap.AirSeconds;

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rules 3 and 6: fly until the distance is covered or the clock runs out, then land.</summary>
    private void TickAir(float dt)
    {
        if (!(_leapRemaining > ArrivedEpsilon) || _now >= _airEndsAt)
        {
            _machine.Transition(ChaserState.Land);

            Stand(_leapDirection);

            return;
        }

        Fly(dt);
    }

    /// <summary>One tick of flight, the last one cut to what is left.</summary>
    private void Fly(float dt)
    {
        float speed = dt > 0f ? MathF.Min(_leapSpeed, _leapRemaining / dt) : _leapSpeed;

        _leapRemaining -= speed * dt;

        Emit(new Vector3(_leapDirection.X * speed, 0f, _leapDirection.Y * speed), _leapDirection);
    }

    /// <summary>
    /// Rules 4–6: one bite, where the body actually is — the blackboard's distance is measured from
    /// it — so a player who left the ring, or a leap that stopped short, bites only what is there.
    /// </summary>
    private void EnterLand()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;
        LeapSpec leap = _agent.Spec.Leap;

        blackboard.StateTimer = 0f;

        // The cooldown counts from the landing (rule 1).
        blackboard.NextAttackAt = _now + leap.Cooldown;

        HitQuarry(_agent.ContactDamage.Value * leap.DamageMultiplier, leap.Radius);
    }

    private void HitQuarry(float damage, float playerReach)
    {
        EnemyBlackboard board = _agent.Blackboard;
        if (_attackPetId != board.QuarryPetId || board.QuarryIsADecoy)
        {
            return;
        }

        if (board.QuarryIsAPet)
        {
            if (board.DistanceToPlayer <= _agent.Spec.Reach)
            {
                _pets?.ApplyDamage(board.QuarryPetId, damage, _now, _agent.Id);
            }
        }
        else if (board.DistanceToPlayer <= playerReach)
        {
            _player.ApplyDamage(damage, _now);
        }
    }

    /// <summary>Rule 4: one tick, then <see cref="ChaserState.Recover"/>.</summary>
    private void TickLand(float dt)
    {
        Stand(_agent.Blackboard.DirectionToPlayer);

        _machine.Transition(ChaserState.Recover);
    }

    /// <summary>Emits a tick's worth of standing still, looking <paramref name="facingXZ"/>.</summary>
    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
