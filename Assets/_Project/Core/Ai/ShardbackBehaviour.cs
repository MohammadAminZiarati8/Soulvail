using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Committed strips and disc; phase beats belong to the boss wrapper.</summary>
public sealed class ShardbackBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private readonly EnemyAgent _agent;
    private readonly ShardbackStats _stats;
    private ShardbackMove _next;
    private ShardbackArmed _armed;
    private Vector2 _facing = Vector2.UnitY;
    private float _remaining;
    private int _phase;
    private bool _casting;
    private bool _recovering;

    public ShardbackBehaviour(EnemyAgent agent, ShardbackStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public void Tick(in EnemyTickContext ctx)
    {
        float dt = float.IsFinite(ctx.Dt) && ctx.Dt > 0f ? ctx.Dt : 0f;
        Vector3 velocity = Vector3.Zero;
        if (_casting)
        {
            _remaining -= dt;
            if (_remaining <= 0f)
                Impact(ctx);
        }
        else
        {
            if (_recovering)
            {
                _remaining -= dt;
                _recovering = _remaining > 0f;
            }
            if (!_recovering)
            {
                EnemyBlackboard board = _agent.Blackboard;
                Vector3 delta = board.PlayerPosition - board.SelfPosition;
                var flat = new Vector2(delta.X, delta.Z);
                if (float.IsFinite(flat.LengthSquared()) && float.IsFinite(board.SelfPosition.Y))
                {
                    if (flat.Length() <= _stats.CastRange.Value)
                    {
                        if (flat != Vector2.Zero)
                            _facing = Vector2.Normalize(flat);
                        Arm(ctx);
                    }
                    else
                    {
                        Vector2 path = board.PathDirectionToPlayer;
                        if (path == Vector2.Zero)
                            path = board.DirectionToPlayer;
                        if (path.LengthSquared() > 0f && float.IsFinite(path.LengthSquared()))
                        {
                            _facing = Vector2.Normalize(path);
                            Vector2 move = _facing * _stats.MoveSpeed.Value;
                            velocity = new Vector3(move.X, 0f, move.Y);
                        }
                    }
                }
            }
        }
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    private void Arm(in EnemyTickContext ctx)
    {
        bool fan = _next == ShardbackMove.Fan;
        _remaining = fan ? _stats.FanWindupSeconds.Value : _stats.BurstWindupSeconds.Value;
        _armed = new ShardbackArmed(_agent.Id, _next, _agent.Blackboard.SelfPosition, _facing,
            fan ? _stats.FanLength.Value : _stats.BurstRadius.Value,
            fan ? _stats.FanHalfWidth.Value : 0f, fan ? _stats.FanAngleDegrees.Value : 0f, _remaining);
        _casting = true;
        ctx.Events.Publish(_armed);
    }

    private void Impact(in EnemyTickContext ctx)
    {
        Vector3 delta = _agent.Blackboard.PlayerPosition - _armed.Origin;
        var flat = new Vector2(delta.X, delta.Z);
        bool fan = _armed.Move == ShardbackMove.Fan;
        bool hit = fan ? InStrip(flat, _armed.Facing)
            || InStrip(flat, Rotate(_armed.AngleDegrees)) || InStrip(flat, Rotate(-_armed.AngleDegrees))
            : flat.Length() <= _armed.Reach;
        if (hit && !_agent.Blackboard.QuarryIsADecoy)
            ctx.Player.ApplyDamage(fan ? _stats.FanDamage.Value : _stats.BurstDamage.Value, ctx.Now);
        _remaining = fan ? _stats.FanRecoverySeconds.Value : _stats.BurstRecoverySeconds.Value;
        ctx.Events.Publish(new ShardbackImpact(_agent.Id, _armed.Move, _armed.Origin, _remaining));
        _next = _phase > 0 && fan ? ShardbackMove.Burst : ShardbackMove.Fan;
        _casting = false;
        _recovering = true;
    }

    private bool InStrip(Vector2 point, Vector2 direction)
    {
        float along = Vector2.Dot(point, direction);
        float across = point.X * direction.Y - point.Y * direction.X;
        return along >= 0f && along <= _armed.Reach && MathF.Abs(across) <= _armed.HalfWidth;
    }

    private Vector2 Rotate(float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector2(_armed.Facing.X * cos - _armed.Facing.Y * sin,
            _armed.Facing.X * sin + _armed.Facing.Y * cos);
    }

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _next = ShardbackMove.Fan;
        _armed = default;
        _remaining = 0f;
        _casting = _recovering = false;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _facing = Vector2.UnitY;
    }
}
