using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Keeps its distance and reserves arena space while calling a tornado.</summary>
public sealed class StormCallerBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private TornadoField _reservation;
    private Vector2 _facing = Vector2.UnitY;
    private double _remaining;
    private float _readyAt = float.NegativeInfinity;

    public StormCallerBehaviour(EnemyAgent agent) => _agent = agent;

    public StormCallerPhase State { get; private set; }

    private StormCallSpec Spec => _agent.Spec.StormCall;

    public void Tick(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector2 toward = board.DirectionToPlayer;
        float distance = board.DistanceToPlayer;
        float dt = MathF.Max(0f, ctx.Dt);
        Vector2 velocity = Vector2.Zero;
        if (toward != Vector2.Zero)
        {
            _facing = toward;
        }

        if (State != StormCallerPhase.Flee && distance < Spec.FleeDistance)
        {
            BreakChannel(ctx.Now, ctx.Events);
            Enter(StormCallerPhase.Flee, Spec.FleeSeconds, ctx.Events);
        }

        switch (State)
        {
            case StormCallerPhase.Flee:
                float fleeSpeed = _agent.MoveSpeed.Value * Spec.FleeSpeedMultiplier;
                if (dt > 0f)
                {
                    fleeSpeed *= (float)Math.Min(1d, _remaining / dt);
                }

                velocity = -_facing * fleeSpeed;
                _remaining -= dt;
                if (_remaining <= 0d)
                {
                    Enter(StormCallerPhase.Hold, 0f, ctx.Events);
                }

                break;

            case StormCallerPhase.Channel:
                _remaining -= dt;
                if (_remaining <= 0d)
                {
                    TornadoField field = _reservation;
                    _reservation = null;
                    var heading = new Vector3(_facing.X, 0f, _facing.Y);
                    Enter(StormCallerPhase.Hold, 0f, ctx.Events);
                    field.Rise(_agent.Id, Spec, _agent.Position + heading * Spec.RiseAhead, heading, ctx.Now);
                }

                break;

            case StormCallerPhase.Hold:
                if (ctx.Now >= _readyAt && distance <= Spec.ChannelReach &&
                    ctx.Enemies.Tornadoes.TryReserve(_agent.Id))
                {
                    _reservation = ctx.Enemies.Tornadoes;
                    Enter(StormCallerPhase.Channel, Spec.ChannelSeconds, ctx.Events);
                }
                else
                {
                    velocity = HoldVelocity(board, dt);
                }

                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, new Vector3(velocity.X, 0f, velocity.Y), _facing));
    }

    public void Reset()
    {
        _reservation?.Release(_agent.Id);
        _reservation = null;
        _facing = Vector2.UnitY;
        _remaining = 0d;
        _readyAt = float.NegativeInfinity;
        State = StormCallerPhase.Hold;
    }

    internal void BreakChannel(float now, IDomainEvents events)
    {
        if (State != StormCallerPhase.Channel)
        {
            return;
        }

        _reservation.Release(_agent.Id);
        _reservation = null;
        _readyAt = now + Spec.BrokenSeconds;
        Enter(StormCallerPhase.Hold, 0f, events);
        events.Publish(new StormChannelBroken(_agent.Id));
    }

    internal void TornadoEnded(float now) => _readyAt = now + Spec.RechannelSeconds;

    private Vector2 HoldVelocity(EnemyBlackboard board, float dt)
    {
        float room = board.DistanceToPlayer - Spec.HoldDistance;
        if (room == 0f)
        {
            return Vector2.Zero;
        }

        Vector2 direction = room < 0f ? -board.DirectionToPlayer
            : board.PathDirectionToPlayer != Vector2.Zero ? board.PathDirectionToPlayer : board.DirectionToPlayer;
        float speed = _agent.MoveSpeed.Value;
        if (dt > 0f)
        {
            speed = MathF.Min(speed, MathF.Abs(room) / dt);
        }

        return direction * speed;
    }

    private void Enter(StormCallerPhase phase, float seconds, IDomainEvents events)
    {
        State = phase;
        _remaining = seconds;
        events.Publish(new StormCallerPhaseChanged(_agent.Id, phase, seconds));
    }
}
