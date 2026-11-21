using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public sealed class BuriedKingBehaviour : IEnemyBehaviour
{
    private enum State { Ready, SweepTell, CastTell, Outbound, Advance, RecallTell, Returning, Recovery, Dead }

    private readonly EnemyAgent _agent;
    private readonly Stat _sweepReach = new(4f);
    private readonly Stat _sweepDegrees = new(120f);
    private readonly Stat _sweepTell = new(0.85f);
    private readonly Stat _sweepRecovery = new(1.4f);
    private readonly Stat _laneWidth = new(1.6f);
    private readonly Stat _laneTell = new(1f);
    private readonly Stat _flightSpeed = new(18f);
    private readonly Stat _advanceTime = new(1.6f);
    private readonly Stat _catchRecovery = new(2.1f);
    private readonly Stat _minimumCastLength = new(4f);
    private readonly Stat _landingSnap = new(1.5f);
    private readonly Stat _weaponClearance = new(2f);
    private readonly Stat _backOffTime = new(1.6f);
    private readonly Stat _sweepDamage = new(24f);
    private readonly Stat _outboundDamage = new(16f);
    private readonly Stat _returnDamage = new(20f);
    private State _state;
    private Vector2 _facing = Vector2.UnitY;
    private Vector3 _start;
    private Vector3 _end;
    private float _remaining;
    private float _distanceTravelled;
    private float _backOffElapsed;
    private int _sweeps;
    private bool _hit;

    public BuriedKingBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    public static ContentId PrototypeBodyId => new("enemy.buried_king_prototype");

    public void Tick(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 hero = ctx.Player.Blackboard.PlayerPosition;
        Vector2 velocity = Vector2.Zero;
        switch (_state)
        {
            case State.Ready:
                velocity = Select(ctx, board, hero);
                break;
            case State.SweepTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveSweep(ctx, hero);
                }
                break;
            case State.CastTell:
            case State.RecallTell:
                if (Elapsed(ctx.Dt))
                {
                    StartFlight(ctx, returning: _state == State.RecallTell);
                }
                break;
            case State.Outbound:
            case State.Returning:
                Fly(ctx, hero);
                break;
            case State.Advance:
                velocity = Advance(ctx, board);
                break;
            case State.Recovery:
                if (Elapsed(ctx.Dt))
                {
                    _state = State.Ready;
                    velocity = Select(ctx, board, hero);
                }
                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, new Vector3(velocity.X, 0f, velocity.Y), _facing));
    }

    public void Reset()
    {
        _state = State.Ready;
        _facing = Vector2.UnitY;
        _start = _end = Vector3.Zero;
        _remaining = _distanceTravelled = _backOffElapsed = 0f;
        _sweeps = 0;
        _hit = false;
    }

    internal void ClearEncounter(IDomainEvents events)
    {
        if (_state == State.Dead)
        {
            return;
        }

        Reset();
        _state = State.Dead;
        Publish(events, BuriedKingPose.Cleared);
    }

    private Vector2 Select(in EnemyTickContext ctx, EnemyBlackboard board, Vector3 hero)
    {
        Vector2 toHero = Ground(hero - board.SelfPosition);
        float distance = toHero.Length();
        Face(toHero);
        bool close = distance <= _sweepReach.Value;
        if (board.HasLineOfSight && !close
            && TryLanding(ctx, board.SelfPosition, hero, out Vector3 landing))
        {
            _sweeps = 0;
            _backOffElapsed = 0f;
            Lock(ctx, State.CastTell, BuriedKingPose.CastTell, board.SelfPosition, landing, _laneTell.Value);
        }
        else if (close && _sweeps >= 2 && _backOffElapsed < _backOffTime.Value)
        {
            _backOffElapsed += ctx.Dt;
            return -_facing * _agent.MoveSpeed.Value;
        }
        else if (close && board.HasLineOfSight)
        {
            if (_sweeps >= 2)
            {
                _sweeps = 0;
                _backOffElapsed = 0f;
            }

            _sweeps++;
            Vector3 tip = board.SelfPosition + new Vector3(_facing.X, 0f, _facing.Y) * _sweepReach.Value;
            Lock(ctx, State.SweepTell, BuriedKingPose.SweepTell, board.SelfPosition, tip, _sweepTell.Value);
        }
        else
        {
            return Approach(board);
        }

        return Vector2.Zero;
    }

    private bool TryLanding(in EnemyTickContext ctx, Vector3 origin, Vector3 hero, out Vector3 landing)
    {
        landing = default;
        float best = _landingSnap.Value * _landingSnap.Value;
        float minLengthSquared = _minimumCastLength.Value * _minimumCastLength.Value;
        bool found = false;
        for (int i = 0; i < ctx.OpenGround.Count; i++)
        {
            Vector3 point = ctx.OpenGround[i];
            float offset = Ground(point - hero).LengthSquared();
            if (offset <= best && Ground(point - origin).LengthSquared() >= minLengthSquared)
            {
                landing = point;
                best = offset;
                found = true;
            }
        }

        return found;
    }

    private void ResolveSweep(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 offset = Ground(hero - _start);
        float distance = offset.Length();
        float edge = MathF.Cos(_sweepDegrees.Value * (MathF.PI / 360f));
        if (distance <= _sweepReach.Value && Vector2.Dot(offset, _facing) >= distance * edge)
        {
            ctx.Player.ApplyDamage(_sweepDamage.Value, ctx.Now);
        }

        Recover(ctx, _sweepRecovery.Value);
    }

    private Vector2 Advance(in EnemyTickContext ctx, EnemyBlackboard board)
    {
        if (_remaining <= 0f)
        {
            Vector3 scepter = _end;
            Face(Ground(scepter - board.SelfPosition));
            Lock(ctx, State.RecallTell, BuriedKingPose.RecallTell, scepter, board.SelfPosition, _laneTell.Value);
            return Vector2.Zero;
        }

        // Keep enough space for a readable return even if the player hugs the embedded weapon.
        float clearance = MathF.Max(0f, Ground(_end - board.SelfPosition).Length() - _weaponClearance.Value);
        float step = MathF.Min(ctx.Dt, _remaining);
        _remaining -= step;
        Vector2 velocity = Approach(board);
        float speed = velocity.Length();
        float travel = speed * ctx.Dt;
        return travel > 0f ? velocity * (MathF.Min(speed * step, clearance) / travel) : Vector2.Zero;
    }

    private void StartFlight(in EnemyTickContext ctx, bool returning)
    {
        _state = returning ? State.Returning : State.Outbound;
        _distanceTravelled = 0f;
        _hit = false;
        Publish(ctx.Events, returning ? BuriedKingPose.Returning : BuriedKingPose.Outbound, _start);
    }

    private void Fly(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 lane = Ground(_end - _start);
        float length = lane.Length();
        bool returning = _state == State.Returning;
        float next = MathF.Min(length, _distanceTravelled + _flightSpeed.Value * ctx.Dt);
        if (!_hit && next > _distanceTravelled)
        {
            Vector2 axis = lane / length;
            Vector2 offset = Ground(hero - _start);
            float along = Vector2.Dot(offset, axis);
            float across = MathF.Abs(offset.X * axis.Y - offset.Y * axis.X);
            if (along >= _distanceTravelled && along <= next && across <= _laneWidth.Value * 0.5f)
            {
                _hit = true;
                ctx.Player.ApplyDamage(returning ? _returnDamage.Value : _outboundDamage.Value, ctx.Now);
            }
        }

        _distanceTravelled = next;
        if (next < length)
        {
            Publish(ctx.Events, returning ? BuriedKingPose.Returning : BuriedKingPose.Outbound,
                Vector3.Lerp(_start, _end, next / length));
        }
        else if (returning)
        {
            Recover(ctx, _catchRecovery.Value);
        }
        else
        {
            _state = State.Advance;
            _remaining = _advanceTime.Value;
            Publish(ctx.Events, BuriedKingPose.Embedded, _end);
        }
    }

    private void Lock(in EnemyTickContext ctx, State state, BuriedKingPose pose, Vector3 start, Vector3 end, float duration)
    {
        _state = state;
        _start = start;
        _end = end;
        _remaining = duration;
        Publish(ctx.Events, pose, start);
    }

    private void Recover(in EnemyTickContext ctx, float duration)
    {
        _state = State.Recovery;
        _remaining = duration;
        Publish(ctx.Events, BuriedKingPose.Held);
    }

    private bool Elapsed(float dt)
    {
        _remaining -= dt;
        return _remaining <= 0f;
    }

    private Vector2 Approach(EnemyBlackboard board)
    {
        Vector2 direction = board.PathDirectionToPlayer == Vector2.Zero ? board.DirectionToPlayer : board.PathDirectionToPlayer;
        Face(direction);
        return direction * _agent.MoveSpeed.Value;
    }

    private void Face(Vector2 direction)
    {
        if (direction != Vector2.Zero)
        {
            _facing = Vector2.Normalize(direction);
        }
    }

    private void Publish(IDomainEvents events, BuriedKingPose pose, Vector3 weapon = default) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, _start, _end, _laneWidth.Value, _sweepDegrees.Value, weapon));

    private static Vector2 Ground(Vector3 vector) => new(vector.X, vector.Z);
}
