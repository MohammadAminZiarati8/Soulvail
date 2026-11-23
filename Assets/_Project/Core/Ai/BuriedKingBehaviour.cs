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
    private enum State
    {
        Ready, SweepTell, CastTell, Outbound, Embedded, RecallTell, Returning, FractureTell, SlamTell,
        LungeTell, Lunging, LungeRecovery, Recovery, Dead
    }
    private enum ReturnPhase { Recall, Reclaim, Shattered }

    private readonly EnemyAgent _agent;
    private readonly Stat _sweepReach = new(4f);
    private readonly Stat _sweepDegrees = new(120f);
    private readonly Stat _sweepTell = new(0.85f);
    private readonly Stat _sweepRecovery = new(1.4f);
    private readonly Stat _laneWidth = new(1.6f);
    private readonly Stat _landingRadius = new(2f);
    private readonly Stat _laneTell = new(1f);
    private readonly Stat _flightSpeed = new(18f);
    private readonly Stat _embeddedTime = new(1.6f);
    private readonly Stat _catchRecovery = new(2.1f);
    private readonly Stat _reclaimThreshold = new(0.66f);
    private readonly Stat _shatteredThreshold = new(0.33f);
    private readonly Stat _fractureTell = new(1f);
    private readonly Stat _slamTell = new(0.9f);
    private readonly Stat _slamRadius = new(3.5f);
    private readonly Stat _slamRecovery = new(2.4f);
    private readonly Stat _shatteredRecovery = new(2.7f);
    private readonly Stat _minimumCastLength = new(4f);
    private readonly Stat _landingSnap = new(1.5f);
    private readonly Stat _backOffTime = new(1.6f);
    private readonly Stat _sweepDamage = new(24f);
    private readonly Stat _outboundDamage = new(16f);
    private readonly Stat _returnDamage = new(20f);
    private readonly Stat _slamDamage = new(28f);
    private readonly Stat _lungeWindup = new(1f);
    private readonly Stat _lungeDistance = new(5f);
    private readonly Stat _lungeTravel = new(0.55f);
    private readonly Stat _lungeCut = new(0.15f);
    private readonly Stat _lungeRecovery = new(0.9f);
    private readonly Stat _lungeSlashStart = new(4.1f);
    private readonly Stat _lungeSlashEnd = new(7.4f);
    private readonly Stat _lungeWidth = new(2.5f);
    private readonly Stat _lungeDamage = new(26f);
    private readonly Stat _lungeDeceleration = new(0.25f);
    private readonly Stat _lungePositionTolerance = new(0.08f);
    private State _state;
    private Vector2 _facing = Vector2.UnitY;
    private Vector3 _start;
    private Vector3 _end;
    private float _remaining;
    private float _distanceTravelled;
    private float _backOffElapsed;
    private int _sweeps;
    private bool _hit;
    private ReturnPhase _unlockedPhase;
    private ReturnPhase _castPhase;
    private float _lungeClearance;
    private Vector3 _lungeExpectedPosition;
    private Vector3 _slashStart;
    private Vector3 _slashEnd;

    public BuriedKingBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    public static ContentId PrototypeBodyId => new("enemy.buried_king_prototype");

    public void Tick(in EnemyTickContext ctx)
    {
        float hp = _agent.Health.Fraction;
        if (_unlockedPhase < ReturnPhase.Shattered && hp <= _shatteredThreshold.Value)
        {
            _unlockedPhase = ReturnPhase.Shattered;
        }
        else if (_unlockedPhase == ReturnPhase.Recall && hp <= _reclaimThreshold.Value)
        {
            _unlockedPhase = ReturnPhase.Reclaim;
        }

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
            case State.Embedded:
                if (Elapsed(ctx.Dt))
                {
                    Lock(ctx, State.RecallTell, BuriedKingPose.RecallTell, _end, board.SelfPosition, _laneTell.Value);
                }
                break;
            case State.FractureTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveFracture(ctx, hero);
                }
                else
                {
                    Publish(ctx.Events, BuriedKingPose.FractureTell, fractureProgress: 1f - _remaining / _fractureTell.Value);
                }
                break;
            case State.SlamTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveSlam(ctx, hero);
                }
                break;
            case State.LungeTell:
                if (Elapsed(ctx.Dt))
                {
                    _state = State.Lunging;
                    _remaining = _lungeTravel.Value;
                    _lungeExpectedPosition = _start;
                    PublishLunge(ctx.Events, BuriedKingPose.LungeTravel, _remaining);
                }
                break;
            case State.Lunging:
                velocity = AdvanceLunge(ctx, board.SelfPosition, hero);
                break;
            case State.LungeRecovery:
            case State.Recovery:
                if (Elapsed(ctx.Dt))
                {
                    if (_state == State.LungeRecovery)
                    {
                        Publish(ctx.Events, BuriedKingPose.Held);
                    }

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
        _unlockedPhase = _castPhase = ReturnPhase.Recall;
        _lungeClearance = 0f;
    }

    internal void ObserveLungeClearance(float distance) => _lungeClearance = distance;

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
        if (_unlockedPhase != ReturnPhase.Recall && board.HasLineOfSight && !close
            && distance >= _lungeSlashStart.Value && distance <= _lungeSlashEnd.Value
            && _lungeClearance >= _lungeDistance.Value)
        {
            _sweeps = 0;
            _backOffElapsed = 0f;
            _start = board.SelfPosition;
            Vector3 axis = new(_facing.X, 0f, _facing.Y);
            _end = _start + axis * _lungeDistance.Value;
            _slashStart = _start + axis * _lungeSlashStart.Value;
            _slashEnd = _start + axis * _lungeSlashEnd.Value;
            _state = State.LungeTell;
            _remaining = _lungeWindup.Value;
            PublishLunge(ctx.Events, BuriedKingPose.LungeTell, _remaining);
        }
        else if (board.HasLineOfSight && !close
            && TryLanding(ctx, board.SelfPosition, hero, out Vector3 landing))
        {
            _castPhase = _unlockedPhase;
            _sweeps = 0;
            _backOffElapsed = 0f;
            Face(Ground(landing - board.SelfPosition));
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

    private Vector2 AdvanceLunge(in EnemyTickContext ctx, Vector3 position, Vector3 hero)
    {
        float tolerance = _lungePositionTolerance.Value;
        bool arrived = Ground(position - _lungeExpectedPosition).LengthSquared() <= tolerance * tolerance;
        if (!arrived || _remaining <= 0f)
        {
            if (arrived && IsInRectangle(hero, _slashStart, _facing, 0f,
                    _lungeSlashEnd.Value - _lungeSlashStart.Value, _lungeWidth.Value))
            {
                ctx.Player.ApplyDamage(_lungeDamage.Value, ctx.Now);
            }

            _state = State.LungeRecovery;
            _remaining = _lungeRecovery.Value;
            PublishLunge(ctx.Events, BuriedKingPose.LungeRecovery, _remaining, interrupted: !arrived);
            return Vector2.Zero;
        }

        if (!(ctx.Dt > 0f))
        {
            return Vector2.Zero;
        }

        _remaining = MathF.Max(0f, _remaining - ctx.Dt);
        float progress = 1f - _remaining / _lungeTravel.Value;
        float glide = progress + _lungeDeceleration.Value * progress * (1f - progress);
        Vector3 next = Vector3.Lerp(_start, _end, glide);
        Vector2 velocity = Ground(next - _lungeExpectedPosition) / ctx.Dt;
        _lungeExpectedPosition = next;
        return velocity;
    }

    private void PublishLunge(IDomainEvents events, BuriedKingPose pose, float duration, bool interrupted = false) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, _start, _end, _lungeWidth.Value,
            0f, default, 0f, 0f, duration, 0f, _slashStart, _slashEnd, interrupted ? 0f : _lungeCut.Value));

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
        if (!_hit && next > _distanceTravelled && IsInLane(hero, lane / length, _distanceTravelled, next))
        {
            _hit = true;
            ctx.Player.ApplyDamage(returning ? _returnDamage.Value : _outboundDamage.Value, ctx.Now);
        }

        _distanceTravelled = next;
        if (next < length)
        {
            Publish(ctx.Events, returning ? BuriedKingPose.Returning : BuriedKingPose.Outbound,
                Vector3.Lerp(_start, _end, next / length));
        }
        else if (returning)
        {
            switch (_castPhase)
            {
                case ReturnPhase.Shattered:
                    _state = State.FractureTell;
                    _remaining = _fractureTell.Value;
                    Publish(ctx.Events, BuriedKingPose.FractureTell, warningSeconds: _remaining);
                    break;
                case ReturnPhase.Reclaim:
                    StartSlam(ctx, _agent.Blackboard.SelfPosition);
                    break;
                default:
                    Recover(ctx, _catchRecovery.Value);
                    break;
            }
        }
        else
        {
            float radius = _landingRadius.Value;
            if (!_hit && Ground(hero - _end).LengthSquared() <= radius * radius)
            {
                ctx.Player.ApplyDamage(_outboundDamage.Value, ctx.Now);
            }

            _state = State.Embedded;
            _remaining = _embeddedTime.Value;
            Publish(ctx.Events, BuriedKingPose.Embedded, _end);
        }
    }

    private bool IsInLane(Vector3 hero, Vector2 axis, float from, float to) =>
        IsInRectangle(hero, _start, axis, from, to, _laneWidth.Value);

    private static bool IsInRectangle(Vector3 hero, Vector3 origin, Vector2 axis, float from, float to, float width)
    {
        Vector2 offset = Ground(hero - origin);
        float along = Vector2.Dot(offset, axis);
        float across = MathF.Abs(offset.X * axis.Y - offset.Y * axis.X);
        return along >= from && along <= to && across <= width * 0.5f;
    }

    private void ResolveFracture(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 lane = Ground(_end - _start);
        float length = lane.Length();
        if (length > 0f && IsInLane(hero, lane / length, 0f, length))
        {
            ctx.Player.ApplyDamage(_returnDamage.Value, ctx.Now);
        }

        StartSlam(ctx, _end);
    }

    private void StartSlam(in EnemyTickContext ctx, Vector3 center) =>
        Lock(ctx, State.SlamTell, BuriedKingPose.SlamTell, center, center, _slamTell.Value);

    private void ResolveSlam(in EnemyTickContext ctx, Vector3 hero)
    {
        float radius = _slamRadius.Value;
        if (Ground(hero - _start).LengthSquared() <= radius * radius)
        {
            ctx.Player.ApplyDamage(_slamDamage.Value, ctx.Now);
        }

        Recover(ctx, _castPhase == ReturnPhase.Shattered ? _shatteredRecovery.Value : _slamRecovery.Value);
    }

    private void Lock(in EnemyTickContext ctx, State state, BuriedKingPose pose, Vector3 start, Vector3 end, float duration)
    {
        _state = state;
        _start = start;
        _end = end;
        _remaining = duration;
        Publish(ctx.Events, pose, start, warningSeconds: duration);
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

    private void Publish(IDomainEvents events, BuriedKingPose pose, Vector3 weapon = default,
        float fractureProgress = 0f, float warningSeconds = 0f) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, _start, _end, _laneWidth.Value, _sweepDegrees.Value,
            weapon, pose == BuriedKingPose.SlamTell ? _slamRadius.Value : 0f, fractureProgress, warningSeconds,
            pose is BuriedKingPose.CastTell or BuriedKingPose.Embedded ? _landingRadius.Value : 0f));

    private static Vector2 Ground(Vector3 vector) => new(vector.X, vector.Z);
}
