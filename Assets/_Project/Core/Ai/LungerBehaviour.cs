using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Where a <see cref="LungerBehaviour"/> is in its cycle.</summary>
public enum LungerState
{
    /// <summary>Walking the path to the player until it is in range and off cooldown.</summary>
    Stalk,

    /// <summary>Standing, its lane armed, committed to <c>EnemyBlackboard.LungeDirection</c>.</summary>
    Windup,

    /// <summary>Crossing the lane it warned of, at <c>LungeSpec.Speed</c>.</summary>
    Dash,

    /// <summary>Standing after the dash, open to punishment.</summary>
    Recover,
}

/// <summary>
/// GD §8.1's Lunger: stalks, stops, warns with a lane down its line, and dashes the lane's length.
/// JG-07a.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lane is the hit.</b> The wind-up opens one <see cref="LaneSystem"/> lane, armed for exactly
/// the wind-up, and the lane bites whoever stands in it on the tick it ends — the tick the dash leaves.
/// The behaviour applies no damage of its own: one rule for one danger, the read Old Croaker's lash
/// already taught (JG-04e). The body then crosses the strip it warned of.
/// </para>
/// <para>
/// <b>It commits to where the player was.</b> The direction is caught on entering the wind-up, in
/// <see cref="EnemyBlackboard.LungeDirection"/>, and every intent until the next stalk faces it.
/// </para>
/// <para>
/// <b>A lane outlives its Lunger.</b> One killed or reset mid-wind-up leaves its strip on the floor,
/// and the strip bites, as a fissure does: the warning was honest when it was drawn.
/// </para>
/// <para>
/// The context is unpacked into fields on the way in and cleared in a <c>finally</c> on the way out,
/// as <see cref="ChaserBehaviour"/> does it and for its reason.
/// </para>
/// </remarks>
public sealed class LungerBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private readonly StateMachine<LungerState> _machine;

    private float _now;

    private IIntentSink _intents;
    private IDomainEvents _events;
    private LaneSystem _lanes;

    /// <summary>Metres of the current dash already covered, so the last tick stops on its length.</summary>
    private float _dashed;

    /// <summary>The way it last faced while stalking: the dash's fallback when the player is on top of it.</summary>
    private Vector2 _facing = new Vector2(0f, 1f);

    /// <param name="agent">The enemy this drives. Its spec must carry a <see cref="LungeSpec"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public LungerBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        _machine = new StateMachine<LungerState>(LungerState.Stalk);

        _machine.OnTick(LungerState.Stalk, TickStalk);

        _machine.OnEnter(LungerState.Windup, EnterWindup);
        _machine.OnTick(LungerState.Windup, TickWindup);

        _machine.OnEnter(LungerState.Dash, EnterDash);
        _machine.OnTick(LungerState.Dash, TickDash);

        _machine.OnEnter(LungerState.Recover, EnterRecover);
        _machine.OnTick(LungerState.Recover, TickRecover);

        _machine.Start();
    }

    /// <summary>Where it is in its cycle.</summary>
    public LungerState State => _machine.Current;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <paramref name="ctx"/> carries no <see cref="EnemyTickContext.Lanes"/> (rule 9): a Lunger that
    /// cannot warn must not dash, and nothing would report it if it simply did.
    /// </exception>
    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Lanes is null)
        {
            throw new InvalidOperationException(
                "A Lunger was ticked with no LaneSystem on its EnemyTickContext. RunSession passes "
                    + "one; a context built anywhere else for a Lunger must too.");
        }

        _now = ctx.Now;
        _intents = ctx.Intents;
        _events = ctx.Events;
        _lanes = ctx.Lanes;

        try
        {
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            _intents = null;
            _events = null;
            _lanes = null;
        }
    }

    /// <summary>A fresh Lunger: stalking, off cooldown. A lane already open stays open (rule 10).</summary>
    public void Reset()
    {
        _machine.Transition(LungerState.Stalk);

        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;
        blackboard.NextAttackAt = 0f;
        blackboard.LungeDirection = Vector2.Zero;

        _dashed = 0f;
        _facing = new Vector2(0f, 1f);
    }

    private LungeSpec Lunge => _agent.Spec.Lunge;

    /// <summary>Rule 1: the chaser's walk, path first, until in range and off cooldown.</summary>
    private void TickStalk(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        if (blackboard.DistanceToPlayer <= Lunge.Range && _now >= blackboard.NextAttackAt)
        {
            // Deferred by the machine until this handler returns, so EnterWindup runs after the
            // stop below: this tick's one intent is the halt, and the wind-up starts from it.
            _machine.Transition(LungerState.Windup);

            Stand(blackboard.DirectionToPlayer != Vector2.Zero ? blackboard.DirectionToPlayer : _facing);

            return;
        }

        Vector2 direction = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        if (direction != Vector2.Zero)
        {
            _facing = direction;
        }

        // The agent's stat, not the spec's float, so depth scaling reaches the walk (M2-03 rule 7).
        float speed = _agent.MoveSpeed.Value;

        Emit(new Vector3(direction.X * speed, 0f, direction.Y * speed), _facing);
    }

    /// <summary>Rule 2: commit to the player's line, telegraph, and open the lane.</summary>
    private void EnterWindup()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        Vector2 aim = blackboard.DirectionToPlayer != Vector2.Zero ? blackboard.DirectionToPlayer : _facing;

        blackboard.LungeDirection = Vector2.Normalize(aim);

        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));

        // A dropped lane (a full table) costs the warning, not the move (rule 8). The agent's stat,
        // so a deep Lunger's lane bites as hard as its depth says.
        _lanes.Open(
            _agent.Position,
            new Vector3(blackboard.LungeDirection.X, 0f, blackboard.LungeDirection.Y),
            Lunge.Length,
            Lunge.HalfWidth,
            _agent.Spec.WindupTime,
            _agent.ContactDamage.Value,
            _now);
    }

    /// <summary>Rule 3: stand facing the committed line until the lane is due.</summary>
    private void TickWindup(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.LungeDirection);

        if (blackboard.StateTimer >= _agent.Spec.WindupTime)
        {
            _machine.Transition(LungerState.Dash);
        }
    }

    private void EnterDash()
    {
        _agent.Blackboard.StateTimer = 0f;

        _dashed = 0f;
    }

    /// <summary>
    /// Rule 4: along the committed line at <see cref="LungeSpec.Speed"/>. The last tick is cut to the
    /// distance left, so the dash covers the lane's length and not a tick past it.
    /// </summary>
    private void TickDash(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        float remaining = Lunge.Length - _dashed;

        if (!(remaining > 0f))
        {
            _machine.Transition(LungerState.Recover);

            Stand(blackboard.LungeDirection);

            return;
        }

        float speed = dt > 0f ? MathF.Min(Lunge.Speed, remaining / dt) : Lunge.Speed;

        _dashed += speed * dt;

        Vector2 line = blackboard.LungeDirection;

        Emit(new Vector3(line.X * speed, 0f, line.Y * speed), line);
    }

    /// <summary>Rule 5: the cooldown counts from the dash's end.</summary>
    private void EnterRecover()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;
        blackboard.NextAttackAt = _now + Lunge.Cooldown;
    }

    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.LungeDirection);

        if (blackboard.StateTimer >= _agent.Spec.RecoverTime)
        {
            _facing = blackboard.LungeDirection;

            _machine.Transition(LungerState.Stalk);
        }
    }

    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
