using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>What the Apex is doing.</summary>
public enum ApexMove { None, Dash = 1, Rake = 2, Pounce = 3, Feint = 4 }

/// <summary>
/// Stage 30's boss: hunts, dashes a warned lane, rakes twice, and pounces onto a fixed ring.
/// A feint crouches without a ring, then arms a dash.
/// </summary>
/// <remarks>
/// The rake is decided here against the player's position, as the Warlord's sweep is: a
/// <c>ConeHitIntent</c> asks the body which <em>enemies</em> a player's swing reached, so it cannot
/// carry an enemy's swipe (AR §18.2: core decides; Unity answers only what needs colliders).
/// </remarks>
public sealed class ApexBehaviour : IEnemyBehaviour, IBossPhaseListener, ILaneStopListener
{
    private const float LaneEndSlack = 0.1f;
    private const float ArrivedEpsilon = 1e-4f;
    private const float MinimumTelegraphSeconds = 0.6f;

    private readonly EnemyAgent _agent;
    private readonly ApexStats _stats;
    private readonly LaneSystem _lanes;
    private readonly IRandomStream _random;
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
    private float _pounceReadyAt;
    private Vector3 _landing;
    private Vector2 _flightDirection;
    private float _flightSpeed;
    private float _flightRemaining;
    private float _airEndsAt;
    private bool _hasPounced;
    private bool _lastWasFeint;

    public ApexBehaviour(EnemyAgent agent, ApexStats stats, IRandomStream random, LaneSystem lanes)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        _remaining = _stats.MoveGapSeconds.Value;
    }

    public ApexMove Move { get; private set; }

    public bool IsRecovering { get; private set; }

    /// <summary>The landing's stationary punish window; no move may be picked.</summary>
    public bool InWindow { get; private set; }

    /// <summary>An already launched pounce finishes even during a boss phase beat.</summary>
    public bool IsAirborne { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        _remaining -= ctx.Dt;
        _dashCooldown = MathF.Max(0f, _dashCooldown - ctx.Dt);
        float speed = 0f;
        if (Move == ApexMove.Pounce)
        {
            speed = TickPounce(ctx);
        }
        else if (Move == ApexMove.Feint)
        {
            if (_remaining <= 0f)
            {
                _pounceReadyAt = ctx.Now + _stats.PounceCooldownSeconds.Value;
                ArmDash(ctx);
            }
        }
        else if (Move == ApexMove.Dash)
        {
            speed = TickDash(ctx);
        }
        else if (Move == ApexMove.Rake)
        {
            TickRake(ctx);
        }
        else if (InWindow || IsRecovering)
        {
            if (_remaining <= 0f)
            {
                IsRecovering = false;
                InWindow = false;
                _remaining = _stats.MoveGapSeconds.Value;
            }
        }
        else if (_remaining <= 0f && _agent.Blackboard.DistanceToPlayer <= _stats.RakeRange.Value)
        {
            ArmRake(ctx);
        }
        else if (_remaining <= 0f && ctx.Now >= _pounceReadyAt
            && _agent.Blackboard.DistanceToPlayer >= _stats.PounceMinRange.Value
            && _agent.Blackboard.DistanceToPlayer <= _stats.PounceMaxRange.Value)
        {
            ArmPounce(ctx);
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

        Vector2 direction = IsAirborne ? _flightDirection : _facing;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id,
            new Vector3(direction.X * speed, 0f, direction.Y * speed), _facing));
    }

    private void ArmPounce(in EnemyTickContext ctx)
    {
        // Exactly one draw per pick, including forced real pounces, on the injected boss stream.
        bool rolledFeint = _random.Chance(_stats.FeintChance.Value);
        bool feint = _hasPounced && !_lastWasFeint && rolledFeint;
        _hasPounced = true;
        _lastWasFeint = feint;
        Move = feint ? ApexMove.Feint : ApexMove.Pounce;
        _remaining = MathF.Max(MinimumTelegraphSeconds,
            feint ? _stats.FeintCrouchSeconds.Value : _stats.PounceCrouchSeconds.Value);
        _facing = Aim();
        IsAirborne = false;
        ctx.Events.Publish(new ApexCrouched(_agent.Id, feint, _remaining));
        if (!feint)
        {
            _landing = _agent.Blackboard.PlayerPosition;
            ctx.Events.Publish(new EnemyLeapAimed(_agent.Id, _agent.Blackboard.SelfPosition,
                _landing, _stats.PounceRadius.Value, _remaining, _stats.PounceAirSeconds.Value));
        }

        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, _remaining, (int)Move));
    }

    private float TickPounce(in EnemyTickContext ctx)
    {
        if (!IsAirborne)
        {
            if (_remaining > 0f)
            {
                return 0f;
            }

            Vector3 delta = _landing - _agent.Blackboard.SelfPosition;
            var flat = new Vector2(delta.X, delta.Z);
            _flightRemaining = flat.Length();
            _flightDirection = _flightRemaining > ArrivedEpsilon ? flat / _flightRemaining : Vector2.Zero;
            _flightSpeed = _flightRemaining / _stats.PounceAirSeconds.Value;
            _airEndsAt = ctx.Now + _stats.PounceAirSeconds.Value;
            IsAirborne = true;
        }
        else if (_flightRemaining <= ArrivedEpsilon || ctx.Now >= _airEndsAt)
        {
            Land(ctx);
            return 0f;
        }

        // The body integrates the intent. Trim the last step so it cannot overshoot the fixed ring.
        float speed = ctx.Dt > 0f ? MathF.Min(_flightSpeed, _flightRemaining / ctx.Dt) : _flightSpeed;
        _flightRemaining -= speed * ctx.Dt;
        return speed;
    }

    private void Land(in EnemyTickContext ctx)
    {
        IsAirborne = false;
        Move = ApexMove.None;
        InWindow = true;
        _remaining = _stats.PounceWindowSeconds.Value;
        _pounceReadyAt = ctx.Now + _stats.PounceCooldownSeconds.Value;
        Vector3 delta = _agent.Blackboard.PlayerPosition - _landing;
        float radius = _stats.PounceRadius.Value;
        if (!_agent.Blackboard.QuarryIsADecoy && delta.X * delta.X + delta.Z * delta.Z <= radius * radius)
        {
            ctx.Player.ApplyDamage(_agent.ContactDamage.Value * _stats.PounceDamageScale.Value, ctx.Now);
        }

        ctx.Events.Publish(new ApexLanded(_agent.Id, _landing, _remaining));
    }

    private void ArmDash(in EnemyTickContext ctx)
    {
        Move = ApexMove.Dash;
        float telegraph = MathF.Max(MinimumTelegraphSeconds, _stats.DashTelegraphSeconds.Value);
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
        float windup = MathF.Max(MinimumTelegraphSeconds, _stats.RakeWindupSeconds.Value);
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
        if (IsAirborne)
        {
            return;
        }

        // An opened lane stays in the LaneSystem to fire; she just doesn't dash it.
        Move = ApexMove.None;
        IsRecovering = false;
        InWindow = false;
        _dashing = false;
        _swipes = 0;
        _remaining = _stats.MoveGapSeconds.Value;
    }

    public void Reset()
    {
        IsAirborne = false;
        OnPhaseEntered(0);
        _dashCooldown = 0f;
        _pounceReadyAt = 0f;
        _hasPounced = false;
        _lastWasFeint = false;
        _flightRemaining = 0f;
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
