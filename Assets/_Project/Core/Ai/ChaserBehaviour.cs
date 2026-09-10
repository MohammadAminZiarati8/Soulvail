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
/// archetype that wants a slower windup edits its asset and nothing else. The two constants below
/// are the ones that belong to the <em>behaviour</em> rather than to any archetype — how far it can
/// notice you, and how far you have to get for a windup to be abandoned — and both are commented
/// where they are declared.
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
/// the player and the two ports are stored on the way into <see cref="Tick"/> and read by the
/// handlers. Capturing them in closures instead would allocate a closure per tick per enemy, which
/// at sixty enemies is exactly the per-frame garbage AR §14 bans. They are cleared on the way out so
/// that a handler reached from anywhere else cannot read a stale player.
/// </para>
/// <para>
/// <b>One instance per agent, built once and reset.</b> <c>EnemyAgent</c> creates it on the first
/// spawn whose archetype needs one and keeps it for the agent's life, the same bargain
/// <c>Health</c> and <c>EnemyBlackboard</c> make: the registry recycles agents, and a fresh state
/// machine per spawn would allocate three dictionaries and nine delegates on a path a wave walks
/// sixty times.
/// </para>
/// </remarks>
public sealed class ChaserBehaviour
{
    /// <summary>
    /// Metres within which a chaser notices the player and starts walking.
    /// </summary>
    /// <remarks>
    /// The behaviour's number, not the archetype's, which is why it is here rather than on
    /// <see cref="EnemySpec"/>: 30 m is comfortably beyond anything the camera shows, so in practice
    /// every enemy in the arena is already coming for you and the state exists for the spawner's
    /// sake (M2-05) rather than as a stealth mechanic. The day an archetype wants to be genuinely
    /// unaware until approached, this moves onto the spec with it.
    /// </remarks>
    public const float AggroRange = 30f;

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

    private readonly EnemyAgent _agent;
    private readonly StateMachine<ChaserState> _machine;

    /// <summary>Simulated run time as of the tick in progress. See the class remarks.</summary>
    private float _now;

    private PlayerCombat _player;
    private IIntentSink _intents;
    private IDomainEvents _events;

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

        _machine.OnTick(ChaserState.Chase, TickChase);

        _machine.OnEnter(ChaserState.Windup, EnterWindup);
        _machine.OnTick(ChaserState.Windup, TickWindup);

        _machine.OnEnter(ChaserState.Strike, EnterStrike);
        _machine.OnTick(ChaserState.Strike, TickStrike);

        _machine.OnEnter(ChaserState.Recover, EnterRecover);
        _machine.OnTick(ChaserState.Recover, TickRecover);

        // Started here rather than on the first Tick, so Current is meaningful the moment the agent
        // exists and Reset never has to ask whether the machine has run yet.
        _machine.Start();
    }

    /// <summary>Which part of the loop this enemy is in.</summary>
    public ChaserState State => _machine.Current;

    /// <summary>
    /// Advances this enemy by one tick: decide, then say so through an intent, an event, or damage.
    /// </summary>
    /// <param name="dt">Seconds since the last tick, from the snapshot.</param>
    /// <param name="now">
    /// Simulated run time — the same seconds <c>Health</c>, <c>Targeter</c> and every i-frame window
    /// are measured in, never a wall clock.
    /// </param>
    /// <param name="player">
    /// Who to hit. Reached directly rather than through a damage intent because the player's health
    /// is core's own state: routing a strike out to the body and back would put a gameplay decision
    /// on a round trip through Unity for no gain (AR §3).
    /// </param>
    /// <param name="intents">Where this tick's <see cref="EnemyMoveIntent"/> goes.</param>
    /// <param name="events">Where <see cref="EnemyTelegraph"/> goes.</param>
    /// <remarks>
    /// <para>
    /// <b>Exactly one <see cref="EnemyMoveIntent"/> per tick, in every state including
    /// <see cref="ChaserState.Idle"/>.</b> The M1-18 spec only asks for one in Chase and Windup;
    /// emitting in all five is a deliberate widening, and the reason is the body: <c>EnemyView</c>
    /// applies gravity inside the same <c>CharacterController.Move</c> that carries the walk, so a
    /// tick with no intent is a tick an enemy is not pinned to the floor by. Making the cadence
    /// unconditional also makes it the same promise <c>RunSession.TickBody</c> makes for the player
    /// — one instruction per tick, never none — which is the rule that stops a body reapplying stale
    /// velocity for ever.
    /// </para>
    /// <para>
    /// Unguarded, unlike a constructor: this runs sixty times a second for every living enemy, and a
    /// null here could only be the first tick after a mis-wired system — a failure that arrives
    /// immediately and unmissably either way. The same asymmetry <c>RunSession.Tick</c> draws with
    /// its snapshot.
    /// </para>
    /// <para>
    /// Allocates nothing: the machine's <c>Tick</c> is allocation-free by construction, the handlers
    /// are delegates built once, and everything below is struct arithmetic over fields.
    /// </para>
    /// </remarks>
    public void Tick(float dt, float now, PlayerCombat player, IIntentSink intents, IDomainEvents events)
    {
        _now = now;
        _player = player;
        _intents = intents;
        _events = events;

        try
        {
            _machine.Tick(dt);
        }
        finally
        {
            // Cleared even if a handler threw, so a half-finished tick cannot leave this holding a
            // player and two ports that the next caller did not supply.
            _player = null;
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
    /// No intent of its own beyond the standing-still one every state emits — an idle Husk is not
    /// facing anywhere in particular, so the facing goes out as zero and the body keeps whatever
    /// rotation it was spawned with.
    /// </remarks>
    private void TickIdle(float dt)
    {
        Stand(Vector2.Zero);

        if (_agent.Blackboard.DistanceToPlayer <= AggroRange)
        {
            _machine.Transition(ChaserState.Chase);
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
        float speed = _agent.MoveSpeed.Value;

        Emit(new Vector3(direction.X * speed, 0f, direction.Y * speed), direction);

        if (blackboard.DistanceToPlayer <= _agent.Spec.Reach)
        {
            _machine.Transition(ChaserState.Windup);
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
    /// </remarks>
    private void EnterStrike()
    {
        _agent.Blackboard.StateTimer = 0f;

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.Reach)
        {
            // The agent's stat rather than the spec's float, for the reason TickChase's speed is
            // one: a stage-40 Husk hits for 8 × d(40), and the 8 is the base.
            _player.ApplyDamage(_agent.ContactDamage.Value, _now);
        }
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

    /// <summary>Emits a tick's worth of standing still, looking <paramref name="facingXZ"/>.</summary>
    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
