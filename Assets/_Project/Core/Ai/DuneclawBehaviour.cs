using System;
using System.Numerics;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Committed ground geometry; the boss wrapper owns beats and summons.</summary>
public sealed class DuneclawBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private readonly EnemyAgent _agent;
    private readonly DuneclawStats _stats;
    private DuneclawMove _next;
    private DuneclawArmed _armed;
    private float _remaining;
    private Vector2 _facing = Vector2.UnitY;

    public DuneclawBehaviour(EnemyAgent agent, DuneclawStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public int Phase { get; private set; }
    public bool IsArmed { get; private set; }
    public bool IsRecovering { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        if (IsArmed)
        {
            _remaining -= ctx.Dt;
            if (_remaining <= 0f)
            {
                Impact(ctx);
            }
            Stand(ctx);
            return;
        }

        if (IsRecovering)
        {
            _remaining -= ctx.Dt;
            if (_remaining > 0f)
            {
                Stand(ctx);
                return;
            }
            IsRecovering = false;
        }

        Vector3 delta = _agent.Blackboard.PlayerPosition - _agent.Blackboard.SelfPosition;
        var flat = new Vector2(delta.X, delta.Z);
        float range = _next == DuneclawMove.Sweep ? _stats.SweepReach.Value : _stats.SlamRange.Value;
        if (flat.Length() <= range)
        {
            Arm(ctx, flat);
            Stand(ctx);
            return;
        }

        Vector2 path = _agent.Blackboard.PathDirectionToPlayer;
        if (path == Vector2.Zero)
        {
            path = _agent.Blackboard.DirectionToPlayer;
        }
        if (path != Vector2.Zero)
        {
            _facing = Vector2.Normalize(path);
        }
        Vector2 velocity = path == Vector2.Zero ? Vector2.Zero : _facing * _stats.MoveSpeed.Value;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id,
            new Vector3(velocity.X, 0f, velocity.Y), _facing));
    }

    private void Arm(in EnemyTickContext ctx, Vector2 towardPlayer)
    {
        if (towardPlayer != Vector2.Zero)
        {
            _facing = Vector2.Normalize(towardPlayer);
        }
        bool sweep = _next == DuneclawMove.Sweep;
        _remaining = sweep ? _stats.SweepWindupSeconds.Value : _stats.SlamWindupSeconds.Value;
        _armed = new DuneclawArmed(_agent.Id, _next, _agent.Blackboard.SelfPosition,
            _facing, _agent.Blackboard.PlayerPosition,
            sweep ? _stats.SweepReach.Value : _stats.SlamRadius.Value,
            sweep ? _stats.SweepArcDegrees.Value : 0f, _remaining);
        IsArmed = true;
        ctx.Events.Publish(_armed);
    }

    private void Impact(in EnemyTickContext ctx)
    {
        bool sweep = _armed.Move == DuneclawMove.Sweep;
        Vector3 delta = _agent.Blackboard.PlayerPosition - (sweep ? _armed.Origin : _armed.Target);
        var flat = new Vector2(delta.X, delta.Z);
        bool inside = flat.Length() <= _armed.Radius;
        if (sweep && flat != Vector2.Zero)
        {
            inside &= Vector2.Dot(_armed.Facing, Vector2.Normalize(flat))
                >= MathF.Cos(_armed.ArcDegrees * (MathF.PI / 360f));
        }
        if (inside && !_agent.Blackboard.QuarryIsADecoy)
        {
            ctx.Player.ApplyDamage(sweep ? _stats.SweepDamage.Value : _stats.SlamDamage.Value, ctx.Now);
        }

        // In phase 2 the short stationary gap replaces the sweep's ordinary recovery.
        _remaining = sweep ? Phase >= 2 ? _stats.ComboGapSeconds.Value : _stats.SweepRecoverySeconds.Value
            : _stats.SlamRecoverySeconds.Value;
        ctx.Events.Publish(new DuneclawImpact(_agent.Id, _armed.Move, _armed.Target, _remaining));
        _next = sweep ? DuneclawMove.Slam : DuneclawMove.Sweep;
        IsArmed = false;
        IsRecovering = true;
    }

    private void Stand(in EnemyTickContext ctx) =>
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, Vector3.Zero, _facing));

    public void OnPhaseEntered(int phase)
    {
        Phase = phase;
        _next = DuneclawMove.Sweep;
        _armed = default;
        _remaining = 0f;
        IsArmed = false;
        IsRecovering = false;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _facing = Vector2.UnitY;
    }
}
