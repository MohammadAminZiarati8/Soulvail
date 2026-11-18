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
    private readonly Vector3[] _slabs = new Vector3[2];
    private readonly bool[] _intact = new bool[2];
    private bool _initialised;
    private bool _placed;
    private Vector3 _spawn;
    private Vector3 _initialPlayer;
    private Vector2 _initialFacing;
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
        PlaceSlabs(ctx);
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
        if (!sweep)
        {
            for (int slot = 0; slot < _slabs.Length; slot++)
            {
                float tolerance = _stats.SlabAimRadius.Value;
                if (_intact[slot] && MathF.Sqrt(DistanceSquared(_armed.Target, _slabs[slot])) <= tolerance)
                {
                    _intact[slot] = false;
                    _remaining = _stats.SlabRecoverySeconds.Value;
                    ctx.Events.Publish(new DuneclawSlabShattered(_agent.Id, slot));
                    break;
                }
            }
        }
        ctx.Events.Publish(new DuneclawImpact(_agent.Id, _armed.Move, _armed.Target, _remaining));
        _next = sweep ? DuneclawMove.Slam : DuneclawMove.Sweep;
        IsArmed = false;
        IsRecovering = true;
    }

    private void PlaceSlabs(in EnemyTickContext ctx)
    {
        if (!_initialised)
        {
            _initialised = true;
            _spawn = _agent.Blackboard.SelfPosition;
            _initialPlayer = _agent.Blackboard.PlayerPosition;
            Vector3 delta = _initialPlayer - _spawn;
            var facing = new Vector2(delta.X, delta.Z);
            _initialFacing = facing == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(facing);
        }
        // An empty report cannot distinguish an unmeasured arena from one without floor.
        if (_placed || ctx.OpenGround.Count == 0)
        {
            return;
        }
        _placed = true;
        var side = new Vector3(_initialFacing.Y, 0f, -_initialFacing.X);
        float spacing = _stats.SlabMinimumSpacing.Value;
        float spacingSquared = spacing * spacing;
        for (int slot = 0; slot < _slabs.Length; slot++)
        {
            Vector3 desired = _spawn + side * (_stats.SlabOffset.Value * (slot == 0 ? -1f : 1f));
            float tolerance = _stats.SlabPlacementTolerance.Value;
            float best = float.PositiveInfinity;
            Vector3 chosen = default;
            for (int i = 0; i < ctx.OpenGround.Count; i++)
            {
                Vector3 point = ctx.OpenGround[i];
                float distance = DistanceSquared(point, desired);
                if (!float.IsFinite(point.Y) || !(distance <= tolerance * tolerance) || !(distance < best)
                    || !(DistanceSquared(point, _spawn) >= spacingSquared)
                    || !(DistanceSquared(point, _initialPlayer) >= spacingSquared)
                    || (slot > 0 && _intact[0] && !(DistanceSquared(point, _slabs[0]) >= spacingSquared)))
                {
                    continue;
                }
                best = distance;
                chosen = point;
            }
            if (float.IsPositiveInfinity(best))
            {
                continue;
            }
            _slabs[slot] = chosen;
            _intact[slot] = true;
            ctx.Events.Publish(new DuneclawSlabPlaced(_agent.Id, slot, chosen));
        }
    }

    private float DistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;
        return x * x + z * z;
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
        _initialised = _placed = false;
        Array.Clear(_slabs, 0, _slabs.Length);
        Array.Clear(_intact, 0, _intact.Length);
    }
}
