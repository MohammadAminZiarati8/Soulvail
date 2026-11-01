using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum WarlordMove { None, Sweep = 1, Charge = 2, WarCry = 3, Lob = 4, Combo = 5, ComboSecond = 6 }

/// <summary>A slow turning frontal guard with a committed glaive sweep.</summary>
public sealed class WarlordBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public const float HalfArcDeg = 60f, TurnRateDeg = 40f, WalkConeDeg = 45f;
    public const float AttackGapSeconds = 1.4f, RecoverSeconds = 0.8f;
    public const float SweepWindupSeconds = 0.8f, SweepHalfAngleDeg = 60f, SweepReach = 3.5f,
        SweepDamageScale = 1.5f;

    private readonly EnemyAgent _agent;
    private bool _firstTick = true;
    private float _publishedArc = -1f;
    private float _remaining = AttackGapSeconds;
    private bool _recovering;
    private Vector2 _aim;
    private Vector3 _origin;
    private IDomainEvents _coneEvents;

    public WarlordBehaviour(EnemyAgent agent, ProjectileSystem projectiles, IRandomStream random,
        ContentId guardId)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        // These dependencies are part of the boss's API for its later moves.
        _ = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _ = random ?? throw new ArgumentNullException(nameof(random));
    }

    public WarlordMove Move { get; private set; }
    public float GuardHalfArcDeg { get; private set; } = HalfArcDeg;
    public Vector2 Facing { get; private set; } = Vector2.UnitY;

    public void Tick(in EnemyTickContext ctx)
    {
        if (_firstTick)
        {
            Facing = Aim();
            _firstTick = false;
        }
        else if (Move == WarlordMove.None && !_recovering)
        {
            Turn(Path(), ctx.Dt);
        }

        _remaining -= ctx.Dt;
        if (Move != WarlordMove.None)
        {
            if (_remaining <= 0f)
            {
                Release(ctx);
            }
        }
        else if (_recovering)
        {
            if (_remaining <= 0f)
            {
                _recovering = false;
                _remaining = AttackGapSeconds;
            }
        }
        else if (_remaining <= 0f && _agent.Blackboard.DistanceToPlayer <= SweepReach)
        {
            Arm(ctx);
        }

        bool guarded = GuardHalfArcDeg > 0f
            && InArc(Facing, _agent.Blackboard.DirectionToPlayer, GuardHalfArcDeg);
        _agent.Health.SetExternalInvulnerable(guarded);
        _agent.IsVulnerable = !guarded;
        if (_publishedArc != GuardHalfArcDeg)
        {
            ctx.Events.Publish(new WarlordGuardArc(_agent.Id, GuardHalfArcDeg));
            _publishedArc = GuardHalfArcDeg;
        }

        float speed = Move == WarlordMove.None && !_recovering
            && InArc(Facing, Path(), WalkConeDeg) ? _agent.MoveSpeed.Value : 0f;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id,
            new Vector3(Facing.X * speed, 0f, Facing.Y * speed), Facing));
    }

    private void Arm(in EnemyTickContext ctx)
    {
        Move = WarlordMove.Sweep;
        _remaining = SweepWindupSeconds;
        _aim = Aim();
        _origin = _agent.Blackboard.SelfPosition;
        Facing = _aim;
        _coneEvents = ctx.Events;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, SweepWindupSeconds, (int)Move));
        ctx.Events.Publish(new SprayArmed(_agent.Id, _origin, _aim,
            SweepHalfAngleDeg, SweepReach, SweepWindupSeconds));
    }

    private void Release(in EnemyTickContext ctx)
    {
        Vector3 delta = _agent.Blackboard.PlayerPosition - _origin;
        var flat = new Vector2(delta.X, delta.Z);
        bool hit = !_agent.Blackboard.QuarryIsADecoy && flat.LengthSquared() <= SweepReach * SweepReach
            && InArc(_aim, flat, SweepHalfAngleDeg);
        if (hit)
        {
            ctx.Player.ApplyDamage(_agent.ContactDamage.Value * SweepDamageScale, ctx.Now);
        }

        _coneEvents = null;
        ctx.Events.Publish(new SprayFired(_agent.Id, hit));
        Move = WarlordMove.None;
        _recovering = true;
        _remaining = RecoverSeconds;
        Facing = _aim;
    }

    public void OnPhaseEntered(int phase)
    {
        _coneEvents?.Publish(new SprayCleared(_agent.Id));
        _coneEvents = null;
        Move = WarlordMove.None;
        _recovering = false;
        _remaining = AttackGapSeconds;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _agent.Health.SetExternalInvulnerable(false);
        _agent.IsVulnerable = true;
        GuardHalfArcDeg = HalfArcDeg;
        Facing = Vector2.UnitY;
        _firstTick = true;
        _publishedArc = -1f;
    }

    private Vector2 Aim() => _agent.Blackboard.DirectionToPlayer != Vector2.Zero
        ? Vector2.Normalize(_agent.Blackboard.DirectionToPlayer) : Vector2.UnitY;

    private Vector2 Path() => _agent.Blackboard.PathDirectionToPlayer != Vector2.Zero
        ? _agent.Blackboard.PathDirectionToPlayer : _agent.Blackboard.DirectionToPlayer;

    private void Turn(Vector2 direction, float dt)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }

        float angle = MathF.Atan2(Facing.X * direction.Y - Facing.Y * direction.X,
            Vector2.Dot(Facing, direction));
        float limit = TurnRateDeg * (MathF.PI / 180f) * dt;
        float step = MathF.Max(-limit, MathF.Min(limit, angle));
        float sin = MathF.Sin(step);
        float cos = MathF.Cos(step);
        Facing = Vector2.Normalize(new Vector2(Facing.X * cos - Facing.Y * sin,
            Facing.X * sin + Facing.Y * cos));
    }

    private bool InArc(Vector2 facing, Vector2 direction, float degrees) => direction == Vector2.Zero
        || Vector2.Dot(facing, Vector2.Normalize(direction)) >= MathF.Cos(degrees * (MathF.PI / 180f));
}
