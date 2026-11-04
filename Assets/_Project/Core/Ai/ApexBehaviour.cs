using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>What the Apex is doing; Pounce and Feint are JG-30b's.</summary>
public enum ApexMove { None, Dash = 1, Rake = 2, Pounce = 3, Feint = 4 }

/// <summary>
/// Stage 30's boss (JG-30a): always in sight, she runs the player down, slashes along a warned lane
/// that stops at a stone, and rakes twice at anyone who stays close.
/// </summary>
/// <remarks>
/// The rake is decided here against the player's position, as the Warlord's sweep is: a
/// <c>ConeHitIntent</c> asks the body which <em>enemies</em> a player's swing reached, so it cannot
/// carry an enemy's swipe (AR §18.2: core decides; Unity answers only what needs colliders).
/// </remarks>
public sealed class ApexBehaviour : IEnemyBehaviour, IBossPhaseListener, ILaneStopListener
{
    private const float LaneEndSlack = 0.1f;

    private readonly EnemyAgent _agent;
    private readonly ApexStats _stats;
    private readonly LaneSystem _lanes;
    private float _remaining;
    private float _dashCooldown;
    private Vector2 _facing = Vector2.UnitY;
    private Vector2 _aim;
    private Vector3 _origin;
    private int _swipes;
    private bool _dashing;
    private bool _laneOpened;
    private bool _probeAnswered;
    private bool _probeHit;
    private float _probeDistance;
    private float _laneLength;
    private float _dashElapsed;
    private int _probeTicks;

    public ApexBehaviour(EnemyAgent agent, ApexStats stats, IRandomStream random, LaneSystem lanes)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        // Nothing in the hunt, dash or rake draws; JG-30b's feint is the first that may.
        _ = random ?? throw new ArgumentNullException(nameof(random));
        _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        _remaining = _stats.MoveGapSeconds.Value;
    }

    public ApexMove Move { get; private set; }

    public bool IsRecovering { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        _remaining -= ctx.Dt;
        _dashCooldown = MathF.Max(0f, _dashCooldown - ctx.Dt);
        float speed = 0f;
        if (Move == ApexMove.Dash)
        {
            speed = TickDash(ctx);
        }
        else if (Move == ApexMove.Rake)
        {
            TickRake(ctx);
        }
        else if (IsRecovering)
        {
            if (_remaining <= 0f)
            {
                IsRecovering = false;
                _remaining = _stats.MoveGapSeconds.Value;
            }
        }
        else if (_remaining <= 0f && _agent.Blackboard.DistanceToPlayer <= _stats.RakeRange.Value)
        {
            ArmRake(ctx);
        }
        else if (_remaining <= 0f && _agent.Blackboard.DistanceToPlayer >= _stats.DashMinRange.Value
            && _dashCooldown <= 0f)
        {
            ArmDash(ctx);
        }
        else
        {
            Vector2 path = Path();
            if (path != Vector2.Zero)
            {
                _facing = Vector2.Normalize(path);
            }

            speed = _stats.RunSpeed.Value;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id,
            new Vector3(_facing.X * speed, 0f, _facing.Y * speed), _facing));
    }

    private void ArmDash(in EnemyTickContext ctx)
    {
        Move = ApexMove.Dash;
        float telegraph = _stats.DashTelegraphSeconds.Value;
        float length = _stats.DashLength.Value;
        _remaining = telegraph;
        _aim = Aim();
        _origin = _agent.Blackboard.SelfPosition;
        _facing = _aim;
        _dashing = false;
        _laneOpened = false;
        _probeAnswered = false;
        _probeHit = false;
        _probeTicks = 0;
        _laneLength = length;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, telegraph, (int)Move));
        ctx.Intents.LaneProbe(new LaneProbeIntent(_agent.Id, _origin,
            new Vector3(_aim.X, 0f, _aim.Y), length, _stats.DashHalfWidth.Value));
    }

    public void OnLaneStop(bool hit, float distance)
    {
        // Once opened, the promised strip cannot be shortened by a late or duplicate answer.
        if (Move != ApexMove.Dash || _laneOpened || _probeAnswered
            || (hit && (!(distance >= 0f) || float.IsInfinity(distance))))
        {
            return;
        }

        _probeAnswered = true;
        _probeHit = hit;
        _probeDistance = distance;
    }

    private float TickDash(in EnemyTickContext ctx)
    {
        float dashSpeed = _stats.DashSpeed.Value;
        if (!_dashing)
        {
            _probeTicks++;
            if (!_laneOpened && (_probeAnswered || _probeTicks >= 2 || _remaining <= 0f))
            {
                float length = _stats.DashLength.Value;
                _laneLength = _probeHit ? MathF.Min(length, _probeDistance) : length;
                if (_laneLength > 0f)
                {
                    // A coarse tick can cross the deadline before the probe fallback. Epsilon keeps
                    // Open's positive-duration contract and makes the lane due on this tick.
                    _lanes.Open(_origin, new Vector3(_aim.X, 0f, _aim.Y), _laneLength,
                        _stats.DashHalfWidth.Value, MathF.Max(float.Epsilon, _remaining),
                        _agent.ContactDamage.Value * _stats.DashDamageScale.Value, ctx.Now);
                }

                _laneOpened = true;
            }

            if (_remaining > 0f)
            {
                return 0f;
            }

            _dashing = true;
            _dashElapsed = 0f;
        }
        else
        {
            _dashElapsed += ctx.Dt;
        }

        // A stone shortened the lane, so the dash ends at it: no daze, an ordinary recovery (rule 4).
        Vector3 delta = _agent.Blackboard.SelfPosition - _origin;
        float travel = new Vector2(delta.X, delta.Z).Length();
        if (travel >= _laneLength - LaneEndSlack
            || _dashElapsed >= _laneLength / dashSpeed + _stats.DashGraceSeconds.Value)
        {
            _dashing = false;
            Move = ApexMove.None;
            _dashCooldown = _stats.DashCooldownSeconds.Value;
            IsRecovering = true;
            _remaining = _stats.DashRecoverSeconds.Value;
            return 0f;
        }

        return dashSpeed;
    }

    private void ArmRake(in EnemyTickContext ctx)
    {
        Move = ApexMove.Rake;
        float windup = _stats.RakeWindupSeconds.Value;
        _remaining = windup;
        _swipes = 0;
        _aim = Aim();
        _origin = _agent.Blackboard.SelfPosition;
        _facing = _aim;
        // One telegraph covers both swipes.
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, windup + _stats.RakeSecondSeconds.Value, (int)Move));
    }

    private void TickRake(in EnemyTickContext ctx)
    {
        // A coarse tick that crosses both deadlines lands both swipes.
        while (Move == ApexMove.Rake && _remaining <= 0f)
        {
            Swipe(ctx);
            _swipes++;
            if (_swipes == 1)
            {
                _remaining += _stats.RakeSecondSeconds.Value;
            }
            else
            {
                Move = ApexMove.None;
                IsRecovering = true;
                _remaining = _stats.RakeRecoverSeconds.Value;
            }
        }
    }

    private void Swipe(in EnemyTickContext ctx)
    {
        Vector3 delta = _agent.Blackboard.PlayerPosition - _origin;
        var flat = new Vector2(delta.X, delta.Z);
        float reach = _stats.RakeReach.Value;
        if (!_agent.Blackboard.QuarryIsADecoy && flat.LengthSquared() <= reach * reach
            && InArc(_aim, flat, _stats.RakeHalfAngleDeg.Value))
        {
            ctx.Player.ApplyDamage(_agent.ContactDamage.Value * _stats.RakeDamageScale.Value, ctx.Now);
        }
    }

    public void OnPhaseEntered(int phase)
    {
        // An opened lane stays in the LaneSystem to fire; she just doesn't dash it.
        Move = ApexMove.None;
        IsRecovering = false;
        _dashing = false;
        _swipes = 0;
        _remaining = _stats.MoveGapSeconds.Value;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _dashCooldown = 0f;
        _facing = Vector2.UnitY;
        _laneOpened = false;
        _probeAnswered = false;
        _probeHit = false;
        _probeTicks = 0;
        _dashElapsed = 0f;
    }

    private Vector2 Aim() => _agent.Blackboard.DirectionToPlayer != Vector2.Zero
        ? Vector2.Normalize(_agent.Blackboard.DirectionToPlayer) : _facing;

    private Vector2 Path() => _agent.Blackboard.PathDirectionToPlayer != Vector2.Zero
        ? _agent.Blackboard.PathDirectionToPlayer : _agent.Blackboard.DirectionToPlayer;

    private static bool InArc(Vector2 facing, Vector2 direction, float degrees) => direction == Vector2.Zero
        || Vector2.Dot(facing, Vector2.Normalize(direction)) >= MathF.Cos(degrees * (MathF.PI / 180f));
}
