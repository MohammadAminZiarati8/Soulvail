using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Holds its range, closes when ready, rears over a locked cone and spits poison.</summary>
public sealed class CobraBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private float _elapsed;
    private bool _announce = true;
    private Vector3 _apex;
    private Vector2 _direction;
    private Vector2 _facing = Vector2.UnitY;

    public CobraBehaviour(EnemyAgent agent)
    {
        _agent = agent;
    }

    public CobraPhase State { get; private set; }

    /// <summary>Seconds until the next spit may be prepared; starts at the spit.</summary>
    public float Cooldown { get; private set; }

    private SpitConeSpec Spec => _agent.Spec.SpitCone;

    public void Tick(in EnemyTickContext ctx)
    {
        if (_announce)
        {
            _announce = false;
            Enter(ctx, CobraPhase.Hold, 0f);
        }

        EnemyBlackboard board = _agent.Blackboard;
        float dt = MathF.Max(0f, ctx.Dt);
        _elapsed += dt;
        Cooldown = MathF.Max(0f, Cooldown - dt);

        bool ready = !(Cooldown > 0f) && board.HasLineOfSight;
        if (State == CobraPhase.Hold && ready)
        {
            Enter(ctx, CobraPhase.Close, 0f);
        }
        else if (State == CobraPhase.Close && !ready)
        {
            Enter(ctx, CobraPhase.Hold, 0f);
        }

        Vector3 velocity = Vector3.Zero;
        switch (State)
        {
            case CobraPhase.Hold:
                velocity = HoldBand(board);
                break;

            case CobraPhase.Close:
                if (board.DistanceToPlayer <= Spec.CloseDistance)
                {
                    Rear(ctx, board);
                }
                else
                {
                    velocity = Slither(board, board.DistanceToPlayer - Spec.CloseDistance, dt);
                }

                break;

            case CobraPhase.Rear:
                _facing = _direction;
                if (_elapsed >= Spec.RearSeconds)
                {
                    Spit(ctx, board);
                    Cooldown = Spec.CooldownSeconds;
                    Enter(ctx, CobraPhase.Recover, Spec.RecoverSeconds);
                }

                break;

            case CobraPhase.Recover:
                if (_elapsed >= Spec.RecoverSeconds)
                {
                    Enter(ctx, CobraPhase.Hold, 0f);
                }

                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    public void Reset()
    {
        State = CobraPhase.Hold;
        Cooldown = 0f;
        _elapsed = 0f;
        _announce = true;
        _apex = Vector3.Zero;
        _direction = Vector2.Zero;
        _facing = Vector2.UnitY;
    }

    private Vector3 HoldBand(EnemyBlackboard board)
    {
        float distance = board.DistanceToPlayer;
        if (distance > Spec.HoldDistance)
        {
            return Slither(board, float.PositiveInfinity, 0f);
        }

        Face(board.DirectionToPlayer);
        if (distance < Spec.HoldDistance * SpitterBehaviour.RetreatFraction)
        {
            Vector2 back = -board.DirectionToPlayer;
            float speed = _agent.MoveSpeed.Value;
            return new Vector3(back.X * speed, 0f, back.Y * speed);
        }

        return Vector3.Zero;
    }

    private Vector3 Slither(EnemyBlackboard board, float room, float dt)
    {
        Vector2 inbound = board.PathDirectionToPlayer != Vector2.Zero
            ? board.PathDirectionToPlayer
            : board.DirectionToPlayer;
        Face(inbound);
        float speed = _agent.MoveSpeed.Value;
        if (dt > 0f)
        {
            speed = MathF.Min(speed, room / dt);
        }

        return new Vector3(inbound.X * speed, 0f, inbound.Y * speed);
    }

    private void Face(Vector2 direction)
    {
        if (direction != Vector2.Zero)
        {
            _facing = direction;
        }
    }

    private void Rear(in EnemyTickContext ctx, EnemyBlackboard board)
    {
        Face(board.DirectionToPlayer);
        _apex = board.SelfPosition;
        _direction = _facing;
        Enter(ctx, CobraPhase.Rear, Spec.RearSeconds);
        ctx.Events.Publish(new CobraConeArmed(_agent.Id, _apex, _direction, Spec.HalfAngleDegrees,
            Spec.Length, Spec.RearSeconds));
    }

    private void Spit(in EnemyTickContext ctx, EnemyBlackboard board)
    {
        Vector3 delta = board.PlayerPosition - _apex;
        var flat = new Vector2(delta.X, delta.Z);
        float distance = flat.Length();
        bool hit = board.HasLineOfSight && distance <= Spec.Length
            && (distance == 0f || Vector2.Dot(flat / distance, _direction)
                >= MathF.Cos(Spec.HalfAngleDegrees * (MathF.PI / 180f)));
        if (hit)
        {
            DamageResult result = ctx.Player.ApplyDamage(Spec.SpitDamage, ctx.Now);
            if (!result.Blocked)
            {
                ctx.Player.ApplyPoison(Spec.PoisonFractionPerSecond, Spec.PoisonSeconds, Spec.PoisonMaxStacks);
            }
        }

        ctx.Events.Publish(new CobraSpat(_agent.Id, hit));
    }

    private void Enter(in EnemyTickContext ctx, CobraPhase phase, float seconds)
    {
        State = phase;
        _elapsed = 0f;
        ctx.Events.Publish(new CobraPhaseChanged(_agent.Id, phase, seconds));
    }
}
