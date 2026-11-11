using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Tunnels behind the hero, fixes a warning ring, and sits exposed after erupting.</summary>
public sealed class AntlionBehaviour : IEnemyBehaviour
{
    private const float MovingSpeed = 0.5f;

    private readonly EnemyAgent _agent;
    private float _elapsed;
    private bool _announceDig = true;
    private bool _hasSample;
    private float _sampledAt;
    private Vector3 _sample;
    private Vector2 _heroVelocity;
    private Vector3 _ringCentre;
    private Vector2 _facing;

    public AntlionBehaviour(EnemyAgent agent)
    {
        _agent = agent;
    }

    public AntlionPhase State { get; private set; }
    public Vector3 Goal { get; private set; }
    internal float SurfaceHoldSeconds { private get; set; }

    private BurrowSpec Spec => _agent.Spec.Burrow;

    public void Tick(in EnemyTickContext ctx)
    {
        if (_announceDig)
        {
            _announceDig = false;
            Enter(ctx, AntlionPhase.Dig, Spec.DigSeconds);
        }

        Vector3 hero = _agent.Blackboard.PlayerPosition;
        Sample(hero, ctx.Now);
        SetUnderground(State is AntlionPhase.Tunnel or AntlionPhase.Arm);

        Vector3 velocity = Vector3.Zero;
        float step = MathF.Max(0f, ctx.Dt);
        float before = _elapsed;
        _elapsed += State == AntlionPhase.Surfaced ? MathF.Max(0f, step - SurfaceHoldSeconds) : step;
        SurfaceHoldSeconds = 0f;

        switch (State)
        {
            case AntlionPhase.Dig:
                if (_elapsed >= Spec.DigSeconds)
                {
                    UpdateGoal(hero, ctx.PlayerFacing);
                    Enter(ctx, AntlionPhase.Tunnel, Spec.MaxTunnelSeconds);
                }

                break;

            case AntlionPhase.Tunnel:
                UpdateGoal(hero, ctx.PlayerFacing);
                Vector2 toGoal = FlatDelta(Goal, _agent.Position);
                float distance = toGoal.Length();
                if (_elapsed >= Spec.MaxTunnelSeconds
                    || (_elapsed >= Spec.MinTunnelSeconds && distance <= Spec.LockDistance))
                {
                    _ringCentre = GravemawBehaviour.FindOpenSpot(ctx.OpenGround, hero, _agent.Position,
                        float.PositiveInfinity);
                    Enter(ctx, AntlionPhase.Arm, Spec.ArmSeconds);
                    ctx.Events.Publish(new AntlionRingArmed(_agent.Id, _ringCentre, Spec.RingRadius, Spec.ArmSeconds));
                }
                else if (distance > 0f)
                {
                    _facing = toGoal / distance;
                    float speed = step > 0f ? MathF.Min(Spec.TunnelSpeed, distance / step) : Spec.TunnelSpeed;
                    velocity = new Vector3(_facing.X * speed, 0f, _facing.Y * speed);
                }

                break;

            case AntlionPhase.Arm:
                // Aim to arrive on the eruption step, even after a lock at the tunnel timeout.
                Vector2 toRing = FlatDelta(_ringCentre, _agent.Position);
                float travelTime = MathF.Max(step, Spec.ArmSeconds - before);
                velocity = new Vector3(toRing.X / travelTime, 0f, toRing.Y / travelTime);
                if (toRing.LengthSquared() > 0f)
                {
                    _facing = Vector2.Normalize(toRing);
                }

                if (_elapsed >= Spec.ArmSeconds)
                {
                    float radius = Spec.RingRadius;
                    if (FlatDelta(hero, _ringCentre).LengthSquared() <= radius * radius)
                    {
                        ctx.Player.ApplyDamage(Spec.EruptionDamage, ctx.Now);
                    }

                    Enter(ctx, AntlionPhase.Surfaced, Spec.SurfacedSeconds);
                }

                break;

            case AntlionPhase.Surfaced:
                Turn(_agent.Blackboard.DirectionToRealPlayer, step);
                if (_elapsed >= Spec.SurfacedSeconds)
                {
                    Enter(ctx, AntlionPhase.Dig, Spec.DigSeconds);
                }

                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    public void Reset()
    {
        State = AntlionPhase.Dig;
        Goal = Vector3.Zero;
        _elapsed = 0f;
        _announceDig = true;
        _hasSample = false;
        _heroVelocity = Vector2.Zero;
        _ringCentre = Vector3.Zero;
        _facing = Vector2.Zero;
        SurfaceHoldSeconds = 0f;
        SetUnderground(false);
    }

    private void Enter(in EnemyTickContext ctx, AntlionPhase phase, float seconds)
    {
        State = phase;
        _elapsed = 0f;
        SetUnderground(phase is AntlionPhase.Tunnel or AntlionPhase.Arm);
        ctx.Events.Publish(new AntlionPhaseChanged(_agent.Id, phase, seconds));
    }

    private void SetUnderground(bool underground)
    {
        _agent.IsVulnerable = !underground;
        _agent.IsHidden = underground;
        _agent.Health.SetExternalInvulnerable(underground);
    }

    private void Sample(Vector3 hero, float now)
    {
        float seconds = now - _sampledAt;
        if (_hasSample && !(seconds > 0f))
        {
            return;
        }

        _heroVelocity = _hasSample ? FlatDelta(hero, _sample) / seconds : Vector2.Zero;
        _hasSample = true;
        _sample = hero;
        _sampledAt = now;
    }

    private void UpdateGoal(Vector3 hero, Vector2 playerFacing)
    {
        Vector2 direction = _heroVelocity.LengthSquared() >= MovingSpeed * MovingSpeed ? _heroVelocity : playerFacing;
        if (direction.LengthSquared() > 0f)
        {
            direction = Vector2.Normalize(direction);
        }

        Goal = hero - new Vector3(direction.X, 0f, direction.Y) * Spec.BehindDistance;
    }

    private void Turn(Vector2 direction, float dt)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }

        if (_facing == Vector2.Zero)
        {
            _facing = direction;
            return;
        }

        float angle = MathF.Atan2(_facing.X * direction.Y - _facing.Y * direction.X,
            Vector2.Dot(_facing, direction));
        float limit = ShamanBehaviour.TurnRateDegrees * (MathF.PI / 180f) * dt;
        float step = Math.Clamp(angle, -limit, limit);
        float sin = MathF.Sin(step);
        float cos = MathF.Cos(step);
        _facing = Vector2.Normalize(new Vector2(_facing.X * cos - _facing.Y * sin,
            _facing.X * sin + _facing.Y * cos));
    }

    private static Vector2 FlatDelta(Vector3 to, Vector3 from) => new(to.X - from.X, to.Z - from.Z);
}
