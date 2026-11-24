using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Ground attacks with world-space footprints. The boss wrapper owns the phase beats.</summary>
public sealed class ColossusBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private enum State { Approach, Tell, BetweenSectors, ReturnGap, Recovery }

    // Allow for rounding when converting a float ground position back to a sector angle.
    private const float EdgeTolerance = 1e-7f;
    private readonly EnemyAgent _agent;
    private readonly ColossusStats _stats;
    private State _state;
    private int _phase;
    private Vector2 _facing = Vector2.UnitY;
    private ColossusArmed _armed;
    private float _remaining;
    private float _rayCooldown;
    private int _sector;
    private bool _attemptedHit;
    private Vector2 _shoulderAxis;
    private float _leftPlate = 1f;
    private float _rightPlate = 1f;

    public ColossusBehaviour(EnemyAgent agent, ColossusStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    internal void ObserveShoulders(Vector2 leftToRight) => _shoulderAxis = leftToRight;

    internal void RepairPlates() => _leftPlate = _rightPlate = 1f;

    internal DamageResult ApplyShoulderHit(float amount, float now, Vector3 towardSource, IDomainEvents events)
    {
        Vector2 axis = _shoulderAxis;
        float axisLength = axis.Length();
        if (!(axisLength > 0f) || !float.IsFinite(axisLength))
        {
            axis = new Vector2(_facing.Y, -_facing.X);
            axisLength = 1f;
        }

        var toward = new Vector2(towardSource.X, towardSource.Z);
        float length = toward.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            return _agent.Health.ApplyDamage(amount, now);
        }

        float side = Vector2.Dot(axis / axisLength, toward / length);
        bool right = side >= 0f;
        ref float remaining = ref (right ? ref _rightPlate : ref _leftPlate);
        if (remaining == 0f)
        {
            return _agent.Health.ApplyDamage(amount, now);
        }

        float scale = 1f - Math.Clamp(Tuning(_stats.PlateDamageReduction), 0f, 0.99f);
        DamageResult hit = _agent.Health.ApplyDamage(amount * scale, now);
        if (hit.Applied > 0f)
        {
            // Near the fore/aft axis the plates deflect the blow. A flank can bite into the stone.
            float flank = MathF.Sin(Tuning(_stats.PlateFlankDegrees) * (MathF.PI / 180f));
            if (MathF.Abs(side) >= flank)
            {
                remaining = MathF.Max(0f, remaining - amount / Tuning(_stats.PlateDurability));
            }
            events.Publish(new ColossusPlateHit(_agent.Id, right ? ColossusShoulder.Right : ColossusShoulder.Left, remaining));
        }
        return hit;
    }

    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Intents is null)
        {
            throw new ArgumentException("An initialized tick context is required.", nameof(ctx));
        }

        // Spend time only in the state that was visible at the start of the tick.
        // A coarse frame can finish a timer but cannot skip the next warning or interval.
        float dt = MathF.Max(0f, ctx.Dt);
        _remaining = MathF.Max(0f, _remaining - dt);
        _rayCooldown = MathF.Max(0f, _rayCooldown - dt);
        Vector3 velocity = Vector3.Zero;
        switch (_state)
        {
            case State.Approach:
                velocity = Approach(ctx);
                break;
            case State.Tell:
                PublishProgress(ctx);
                if (_remaining == 0f)
                {
                    Strike(ctx);
                }
                break;
            case State.BetweenSectors when _remaining == 0f:
                Strike(ctx);
                break;
            case State.ReturnGap when _remaining == 0f:
                Arm(ctx, new ColossusArmed(_agent.Id, ColossusMove.ReverseSweep, _armed.Origin,
                    _armed.Facing, _armed.Radius, _armed.SectorDegrees, Tuning(_stats.ReturnTellSeconds)));
                break;
            case State.Recovery when _remaining == 0f:
                _state = State.Approach;
                velocity = Approach(ctx, canStomp: _armed.Move != ColossusMove.Stomp);
                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    private Vector3 Approach(in EnemyTickContext ctx, bool canStomp = false)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector2 offset = GroundOffset(board.PlayerPosition, board.SelfPosition);
        float distance = offset.Length();
        if (!float.IsFinite(distance) || !float.IsFinite(board.SelfPosition.Y))
        {
            return Vector3.Zero;
        }

        if (_rayCooldown == 0f && board.HasLineOfSight && !board.QuarryIsADecoy && !board.QuarryIsAPet
            && distance >= Tuning(_stats.RayMinimumRange) && distance <= Tuning(_stats.RayLength))
        {
            _facing = offset / distance;
            _rayCooldown = Tuning(_stats.RayCooldownSeconds);
            Arm(ctx, new ColossusArmed(_agent.Id, ColossusMove.SundialRay, board.SelfPosition, _facing,
                Tuning(_stats.RayLength), 0f, Tuning(_stats.RayTellSeconds), Tuning(_stats.RayStart),
                Tuning(_stats.RayHalfWidth)));
            return Vector3.Zero;
        }

        bool stomp = canStomp && distance <= Tuning(_stats.StompTriggerRadius);
        float radius = Tuning(stomp ? _stats.StompRadius : _stats.SweepRadius);
        if (stomp || distance <= radius)
        {
            if (distance > 0f)
            {
                _facing = offset / distance;
            }

            float degrees = stomp ? 0f : Tuning(_stats.SectorDegrees);
            float seconds = Tuning(stomp ? _stats.StompTellSeconds : _stats.SweepTellSeconds);
            Arm(ctx, new ColossusArmed(_agent.Id, stomp ? ColossusMove.Stomp : ColossusMove.Sweep,
                board.SelfPosition, _facing, radius, degrees, seconds));
            return Vector3.Zero;
        }

        Vector2 path = board.PathDirectionToPlayer;
        if (path == Vector2.Zero)
        {
            path = board.DirectionToPlayer;
        }
        float length = path.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            return Vector3.Zero;
        }

        _facing = path / length;
        float speed = Tuning(_stats.MoveSpeed);
        return new Vector3(_facing.X * speed, 0f, _facing.Y * speed);
    }

    private void Arm(in EnemyTickContext ctx, ColossusArmed attack)
    {
        _armed = attack;
        _remaining = attack.Seconds;
        _sector = attack.Move switch
        {
            ColossusMove.Stomp or ColossusMove.SundialRay => -1,
            ColossusMove.ReverseSweep => 2,
            _ => 0
        };
        _attemptedHit = false;
        _state = State.Tell;
        ctx.Events.Publish(attack);
        PublishProgress(ctx);
    }

    private void PublishProgress(in EnemyTickContext ctx)
    {
        float fraction = Math.Clamp(1f - _remaining / _armed.Seconds, 0f, 1f);
        ctx.Events.Publish(new ColossusTellProgress(_agent.Id, fraction));
    }

    private void Strike(in EnemyTickContext ctx)
    {
        ColossusMove move = _armed.Move;
        int sector = _sector;
        EnemyBlackboard board = _agent.Blackboard;
        if (!_attemptedHit && !board.QuarryIsADecoy && !board.QuarryIsAPet && ContainsQuarry(board))
        {
            // A defended hit still spends this pass's attempt.
            _attemptedHit = true;
            ctx.Player.ApplyDamage(Tuning(move switch
            {
                ColossusMove.Stomp => _stats.StompDamage,
                ColossusMove.SundialRay => _stats.RayDamage,
                _ => _stats.SweepDamage
            }), ctx.Now);
        }

        bool lastSector = move switch
        {
            ColossusMove.Sweep => sector == 2,
            ColossusMove.ReverseSweep => sector == 0,
            _ => true
        };
        float recovery = 0f;
        if (!lastSector)
        {
            _sector += move == ColossusMove.ReverseSweep ? -1 : 1;
            _remaining = Tuning(_phase == 0 ? _stats.SectorIntervalSeconds : _stats.FastSectorIntervalSeconds);
            _state = State.BetweenSectors;
        }
        else if (move == ColossusMove.Sweep && _phase >= 2)
        {
            _remaining = Tuning(_stats.ReturnGapSeconds);
            _state = State.ReturnGap;
        }
        else
        {
            recovery = Tuning(move switch
            {
                ColossusMove.Stomp => _stats.StompRecoverySeconds,
                ColossusMove.ReverseSweep => _stats.ReturnRecoverySeconds,
                ColossusMove.SundialRay => _stats.RayRecoverySeconds,
                _ => _stats.SweepRecoverySeconds
            });
            _remaining = recovery;
            _state = State.Recovery;
        }

        ctx.Events.Publish(new ColossusImpact(_agent.Id, move, sector, recovery));
    }

    private bool ContainsQuarry(EnemyBlackboard board)
    {
        Vector2 offset = GroundOffset(board.PlayerPosition, _armed.Origin);
        if (_armed.Move == ColossusMove.SundialRay)
        {
            float along = Vector2.Dot(_armed.Facing, offset);
            float across = _armed.Facing.Y * offset.X - _armed.Facing.X * offset.Y;
            return along >= _armed.LaneStart && along <= _armed.Radius && MathF.Abs(across) <= _armed.LaneHalfWidth;
        }
        if (!(offset.Length() <= _armed.Radius))
        {
            return false;
        }
        if (_armed.Move == ColossusMove.Stomp)
        {
            return true;
        }
        if (offset == Vector2.Zero)
        {
            return _sector == 1;
        }

        float angle = MathF.Atan2(_armed.Facing.Y * offset.X - _armed.Facing.X * offset.Y,
            Vector2.Dot(_armed.Facing, offset));
        float halfSector = _armed.SectorDegrees * (MathF.PI / 360f);
        float outerEdge = 3f * halfSector + EdgeTolerance;
        if (angle < -outerEdge || angle > outerEdge)
        {
            return false;
        }

        if (angle >= halfSector - EdgeTolerance)
        {
            return _sector == 2;
        }
        return _sector == (angle >= -halfSector - EdgeTolerance ? 1 : 0);
    }

    private static Vector2 GroundOffset(Vector3 position, Vector3 origin) =>
        new(position.X - origin.X, position.Z - origin.Z);

    private static float Tuning(Stat stat)
    {
        float value = stat.Value;
        if (!(value > 0f) || !float.IsFinite(value))
        {
            throw new InvalidOperationException("Colossus stats must resolve to positive finite values.");
        }
        return value;
    }

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _state = State.Approach;
        _armed = default;
        _remaining = 0f;
        _sector = 0;
        _attemptedHit = false;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _facing = Vector2.UnitY;
        _rayCooldown = 0f;
        _shoulderAxis = Vector2.Zero;
        RepairPlates();
    }
}
