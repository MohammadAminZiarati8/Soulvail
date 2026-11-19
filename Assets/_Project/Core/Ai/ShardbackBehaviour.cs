using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Committed fan, charge and burst casts. Phase beats belong to the boss wrapper.</summary>
public sealed class ShardbackBehaviour : IEnemyBehaviour, IBossPhaseListener, IShardbackProbeListener
{
    private enum State { Approach, Tell, Travel, Recovery }

    private readonly EnemyAgent _agent;
    private readonly ShardbackStats _stats;
    private readonly ShardbackStop[] _stops = new ShardbackStop[4];
    private Guid _lifetime = Guid.NewGuid();
    private ulong _cast;
    private bool _fightStarted;
    private int _phase;
    private int _spent;
    private State _state;
    private ShardbackMove _next;
    private ShardbackArmed _armed;
    private Vector2 _facing = Vector2.UnitY;
    private Vector2 _plus;
    private Vector2 _minus;
    private int _answers;
    private int _expected;
    private bool _clipped;
    private float _remaining;
    private float _damage;
    private float _recovery;
    private float _pillarRecovery;
    private float _speed;
    private float _stallSeconds;
    private float _stalled;
    private float _progress;
    private Vector2 _chargeEnd;
    private bool _chargeAttemptedHit;

    public ShardbackBehaviour(EnemyAgent agent, ShardbackStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public void Tick(in EnemyTickContext ctx)
    {
        if (!_fightStarted)
        {
            _fightStarted = true;
            ctx.Events.Publish(new ShardbackFightStarted(_agent.Id, _lifetime));
        }

        Vector3 velocity = _state switch
        {
            State.Tell => Tell(ctx),
            State.Travel => Travel(ctx),
            State.Recovery => Recover(ctx),
            _ => Approach(ctx)
        };
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    private Vector3 Recover(in EnemyTickContext ctx)
    {
        _remaining -= MathF.Max(0f, ctx.Dt);
        if (_remaining > 0f)
            return Vector3.Zero;

        _state = State.Approach;
        return Approach(ctx);
    }

    private Vector3 Approach(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector2 flat = Flat(board.PlayerPosition - board.SelfPosition);
        if (!float.IsFinite(flat.LengthSquared()) || !float.IsFinite(board.SelfPosition.Y))
            return Vector3.Zero;

        if (flat.Length() <= _stats.CastRange.Value)
        {
            if (flat.LengthSquared() > 0f)
                _facing = Vector2.Normalize(flat);
            Arm(ctx);
            return Vector3.Zero;
        }

        Vector2 path = board.PathDirectionToPlayer;
        if (path == Vector2.Zero)
            path = board.DirectionToPlayer;
        if (!float.IsFinite(path.LengthSquared()) || !(path.LengthSquared() > 0f))
            return Vector3.Zero;

        _facing = Vector2.Normalize(path);
        return Ground(_facing * _stats.MoveSpeed.Value);
    }

    private void Arm(in EnemyTickContext ctx)
    {
        bool fan = _next == ShardbackMove.Fan;
        bool charge = _next == ShardbackMove.Charge;
        (float reach, float width, float windup, float damage, float recovery) = _next switch
        {
            ShardbackMove.Fan => (_stats.FanLength.Value, _stats.FanHalfWidth.Value,
                _stats.FanWindupSeconds.Value, _stats.FanDamage.Value, _stats.FanRecoverySeconds.Value),
            ShardbackMove.Charge => (_stats.ChargeLength.Value, _stats.ChargeHalfWidth.Value,
                _stats.ChargeWindupSeconds.Value, _stats.ChargeDamage.Value, _stats.ChargeRecoverySeconds.Value),
            _ => (_stats.BurstRadius.Value, 0f, _stats.BurstWindupSeconds.Value,
                _stats.BurstDamage.Value, _stats.BurstRecoverySeconds.Value)
        };
        if ((fan || charge) && (!(reach > 0f) || !float.IsFinite(reach)
            || !(width > 0f) || !float.IsFinite(width)))
            return;

        float angle = fan ? _stats.FanAngleDegrees.Value : 0f;
        if (!float.IsFinite(angle))
            return;
        _armed = new ShardbackArmed(_agent.Id, _next, _agent.Blackboard.SelfPosition, _facing,
            reach, width, angle, windup, new ShardbackProbeKey(_lifetime, ++_cast));
        _remaining = windup;
        _damage = damage;
        _recovery = recovery;
        _expected = fan ? 0b1110 : charge ? 0b0001 : 0;
        _answers = 0;
        _clipped = false;
        if (charge)
        {
            _speed = MathF.Max(0f, _stats.ChargeSpeed.Value);
            _pillarRecovery = _stats.PillarRecoverySeconds.Value;
            _stallSeconds = _stats.ChargeStallSeconds.Value;
        }
        _state = State.Tell;
        ctx.Events.Publish(_armed);

        if (fan)
        {
            _plus = Rotate(angle);
            _minus = Rotate(-angle);
            Probe(ctx, 1, _facing);
            Probe(ctx, 2, _plus);
            Probe(ctx, 3, _minus);
        }
        else if (charge)
        {
            Probe(ctx, 0, _facing);
        }
    }

    private void Probe(in EnemyTickContext ctx, int lane, Vector2 facing) =>
        ctx.Intents.LaneProbe(new LaneProbeIntent(_agent.Id, _armed.Origin, Ground(facing),
            _armed.Reach, _armed.HalfWidth, _armed.Key, lane));

    public void OnShardbackStop(in ShardbackStop report)
    {
        if (_state != State.Tell || !report.IsValid || report.BossId != _agent.Id
            || report.Key.Lifetime != _armed.Key.Lifetime || report.Key.Cast != _armed.Key.Cast
            || report.Distance > _armed.Reach
            || (report.Kind == ShardbackStopKind.None && report.Distance != _armed.Reach)
            || (report.Kind == ShardbackStopKind.Pillar && (_spent & (1 << report.PillarSlot)) != 0))
            return;

        int lane = 1 << report.Lane;
        if ((_expected & lane) == 0 || (_answers & lane) != 0)
            return;
        _stops[report.Lane] = report;
        _answers |= lane;
    }

    private Vector3 Tell(in EnemyTickContext ctx)
    {
        _remaining -= MathF.Max(0f, ctx.Dt);
        if (_answers != _expected)
            return Vector3.Zero;

        bool charge = _armed.Move == ShardbackMove.Charge;
        if (!_clipped && _expected != 0)
        {
            _clipped = true;
            ctx.Events.Publish(charge
                ? new ShardbackClipped(_agent.Id, _armed.Key, _stops[0].Distance, 0f, 0f)
                : new ShardbackClipped(_agent.Id, _armed.Key, _stops[1].Distance, _stops[2].Distance, _stops[3].Distance));
        }
        if (_remaining > 0f)
            return Vector3.Zero;

        if (charge)
        {
            if (_stops[0].Distance == 0f)
            {
                FinishCharge(ctx, reachedStop: true);
                return Vector3.Zero;
            }
            _chargeEnd = Flat(_armed.Origin) + _armed.Facing * _stops[0].Distance;
            _progress = ObserveProgress(0f);
            _stalled = 0f;
            _chargeAttemptedHit = false;
            _state = State.Travel;
            ctx.Events.Publish(new ShardbackChargeStarted(_agent.Id, _armed.Key));
            return ChargeVelocity(ctx.Dt);
        }

        Vector2 point = Flat(_agent.Blackboard.PlayerPosition - _armed.Origin);
        bool fan = _armed.Move == ShardbackMove.Fan;
        bool hit = fan ? InStrip(point, _armed.Facing, _stops[1])
            || InStrip(point, _plus, _stops[2]) || InStrip(point, _minus, _stops[3])
            : point.Length() <= _armed.Reach;
        if (hit && !_agent.Blackboard.QuarryIsADecoy)
            ctx.Player.ApplyDamage(_damage, ctx.Now);
        Finish(ctx, fan && _phase >= 2 ? 0f : _recovery);
        return Vector3.Zero;
    }

    private Vector3 Travel(in EnemyTickContext ctx)
    {
        ShardbackStop stop = _stops[0];
        float observed = ObserveProgress(_progress);
        // A world-space position can round the commanded endpoint to just short in local space.
        bool reachedStop = observed >= stop.Distance || (observed > 0f
            && Vector2.Dot(Flat(_agent.Blackboard.SelfPosition) - _chargeEnd, _armed.Facing) >= 0f);
        if (reachedStop)
            observed = stop.Distance;

        if (observed > _progress)
        {
            Vector2 point = Flat(_agent.Blackboard.PlayerPosition - _armed.Origin);
            if (!_chargeAttemptedHit && !_agent.Blackboard.QuarryIsADecoy
                && InStrip(point, _armed.Facing, stop, _progress, observed))
            {
                _chargeAttemptedHit = true;
                ctx.Player.ApplyDamage(_damage, ctx.Now);
            }
            _stalled = 0f;
        }
        else
        {
            _stalled += MathF.Max(0f, ctx.Dt);
        }

        _progress = observed;
        if (reachedStop || _stalled >= _stallSeconds)
        {
            FinishCharge(ctx, reachedStop);
            return Vector3.Zero;
        }
        return ChargeVelocity(ctx.Dt);
    }

    private float ObserveProgress(float fallback)
    {
        float along = Vector2.Dot(Flat(_agent.Blackboard.SelfPosition - _armed.Origin), _armed.Facing);
        return float.IsFinite(along) ? Math.Clamp(along, 0f, _stops[0].Distance) : fallback;
    }

    private Vector3 ChargeVelocity(float dt)
    {
        float remaining = _stops[0].Distance - _progress;
        return dt > 0f ? Ground(_armed.Facing * MathF.Min(_speed, remaining / dt)) : Vector3.Zero;
    }

    private void FinishCharge(in EnemyTickContext ctx, bool reachedStop)
    {
        ShardbackStop stop = _stops[0];
        bool breaks = reachedStop && stop.Kind == ShardbackStopKind.Pillar;
        if (breaks)
            _spent |= 1 << stop.PillarSlot;
        Finish(ctx, breaks ? _pillarRecovery : _recovery);
        if (breaks)
            ctx.Events.Publish(new ShardbackPillarBroken(_agent.Id, stop.Key.Lifetime, stop.PillarSlot));
    }

    private void Finish(in EnemyTickContext ctx, float recovery)
    {
        _remaining = recovery;
        _state = State.Recovery;
        _next = _armed.Move switch
        {
            ShardbackMove.Fan => ShardbackMove.Charge,
            ShardbackMove.Charge when _phase > 0 => ShardbackMove.Burst,
            _ => ShardbackMove.Fan
        };
        ctx.Events.Publish(new ShardbackImpact(_agent.Id, _armed.Move, _armed.Origin, recovery, _armed.Key));
    }

    private bool InStrip(Vector2 point, Vector2 direction, in ShardbackStop stop) =>
        InStrip(point, direction, stop, 0f, stop.Distance);

    private bool InStrip(Vector2 point, Vector2 direction, in ShardbackStop stop, float from, float to)
    {
        float along = Vector2.Dot(point, direction);
        float across = point.X * direction.Y - point.Y * direction.X;
        return along >= from && along <= to
            && (stop.Kind == ShardbackStopKind.None || along < stop.Distance)
            && MathF.Abs(across) <= _armed.HalfWidth;
    }

    private Vector2 Rotate(float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector2(_facing.X * cos - _facing.Y * sin, _facing.X * sin + _facing.Y * cos);
    }

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Z);
    private static Vector3 Ground(Vector2 point) => new(point.X, 0f, point.Y);

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _next = ShardbackMove.Fan;
        _armed = default;
        _state = State.Approach;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _facing = Vector2.UnitY;
        _lifetime = Guid.NewGuid();
        _fightStarted = false;
        _cast = 0;
        _spent = 0;
    }
}
