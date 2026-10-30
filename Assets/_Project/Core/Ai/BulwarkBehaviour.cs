using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>The Bulwark's committed melee cycle.</summary>
public enum BulwarkState
{
    Approach,
    Windup,
    Bash,
    Recover,
}

/// <summary>A turning frontal shield; its bash and recovery keep the front it warned with.</summary>
public sealed class BulwarkBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private readonly StateMachine<BulwarkState> _machine;
    private Vector2 _facing = Vector2.UnitY;
    private bool _firstTick = true;
    private float _now;
    private PlayerCombat _player;
    private IIntentSink _intents;
    private IDomainEvents _events;

    public BulwarkBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _machine = new StateMachine<BulwarkState>(BulwarkState.Approach);
        _machine.OnTick(BulwarkState.Approach, TickApproach);
        _machine.OnEnter(BulwarkState.Windup, EnterWindup);
        _machine.OnTick(BulwarkState.Windup, TickWindup);
        _machine.OnEnter(BulwarkState.Bash, EnterBash);
        _machine.OnTick(BulwarkState.Bash, TickRecover);
        _machine.OnTick(BulwarkState.Recover, TickRecover);
        _machine.Start();
    }

    public BulwarkState State => _machine.Current;

    private BulwarkSpec Shield => _agent.Spec.Bulwark;

    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _player = ctx.Player;
        _intents = ctx.Intents;
        _events = ctx.Events;
        try
        {
            if (_firstTick)
            {
                Vector2 player = _agent.Blackboard.DirectionToPlayer;
                _facing = player != Vector2.Zero ? Vector2.Normalize(player) : Vector2.UnitY;
                _firstTick = false;
            }
            else if (State == BulwarkState.Approach)
            {
                Turn(Path(), MathF.Max(0f, ctx.Dt));
            }

            bool shielded = InArc(_agent.Blackboard.DirectionToPlayer, Shield.ShieldHalfArcDegrees);
            _agent.Health.SetExternalInvulnerable(shielded);
            _agent.IsVulnerable = !shielded;
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            _player = null;
            _intents = null;
            _events = null;
        }
    }

    public void Reset()
    {
        _machine.Transition(BulwarkState.Approach);
        _agent.Blackboard.StateTimer = 0f;
        _agent.Blackboard.NextAttackAt = 0f;
        _agent.Health.SetExternalInvulnerable(false);
        _agent.IsVulnerable = true;
        _facing = Vector2.UnitY;
        _firstTick = true;
    }

    private void TickApproach(float dt)
    {
        EnemyBlackboard board = _agent.Blackboard;
        if (board.DistanceToPlayer <= _agent.Spec.Reach && !_agent.IsVulnerable
            && _now >= board.NextAttackAt)
        {
            Stand();
            _machine.Transition(BulwarkState.Windup);
            return;
        }

        float speed = InArc(Path(), Shield.WalkConeDegrees) ? _agent.MoveSpeed.Value : 0f;
        Emit(new Vector3(_facing.X * speed, 0f, _facing.Y * speed));
    }

    private void EnterWindup()
    {
        _agent.Blackboard.StateTimer = 0f;
        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));
    }

    private void TickWindup(float dt)
    {
        _agent.Blackboard.StateTimer += dt;
        Stand();
        if (_agent.Blackboard.StateTimer >= _agent.Spec.WindupTime)
        {
            _machine.Transition(BulwarkState.Bash);
        }
    }

    private void EnterBash()
    {
        EnemyBlackboard board = _agent.Blackboard;
        board.StateTimer = 0f;
        board.NextAttackAt = _now + _agent.Spec.RecoverTime;
        if (!board.QuarryIsADecoy && board.DistanceToPlayer <= _agent.Spec.Reach
            && InArc(board.DirectionToPlayer, Shield.ShieldHalfArcDegrees))
        {
            _player.ApplyDamage(_agent.ContactDamage.Value, _now);
        }
    }

    private void TickRecover(float dt)
    {
        _agent.Blackboard.StateTimer += dt;
        Stand();
        _machine.Transition(_now >= _agent.Blackboard.NextAttackAt
            ? BulwarkState.Approach : BulwarkState.Recover);
    }

    private Vector2 Path() => _agent.Blackboard.PathDirectionToPlayer != Vector2.Zero
        ? _agent.Blackboard.PathDirectionToPlayer : _agent.Blackboard.DirectionToPlayer;

    private void Turn(Vector2 direction, float dt)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }

        float angle = MathF.Atan2(_facing.X * direction.Y - _facing.Y * direction.X,
            Vector2.Dot(_facing, direction));
        float limit = Shield.TurnRateDegrees * (MathF.PI / 180f) * dt;
        float step = MathF.Max(-limit, MathF.Min(limit, angle));
        float sin = MathF.Sin(step);
        float cos = MathF.Cos(step);
        _facing = Vector2.Normalize(new Vector2(_facing.X * cos - _facing.Y * sin,
            _facing.X * sin + _facing.Y * cos));
    }

    private bool InArc(Vector2 direction, float degrees) => direction == Vector2.Zero
        || Vector2.Dot(_facing, Vector2.Normalize(direction)) >= MathF.Cos(degrees * (MathF.PI / 180f));

    private void Stand() => Emit(Vector3.Zero);

    private void Emit(Vector3 velocity) =>
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
}
