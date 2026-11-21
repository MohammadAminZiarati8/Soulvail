using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Circles the hero aloft, dives down a locked lane, and lands exposed.</summary>
public sealed class VultureBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private float _phaseEndsAt;
    private float _groundedElapsed;
    private bool _announceClimb = true;
    private Vector3 _landing;
    private Vector3 _facing;

    public VultureBehaviour(EnemyAgent agent) => _agent = agent;

    public VulturePhase State { get; private set; }
    public int DiveCount { get; private set; }
    internal float GroundedHoldSeconds { private get; set; }

    private DiveSpec Spec => _agent.Spec.Dive;

    public void Tick(in EnemyTickContext ctx)
    {
        float step = MathF.Max(0f, ctx.Dt);
        if (_announceClimb)
        {
            _announceClimb = false;
            Enter(ctx, VulturePhase.Climb, Spec.ClimbSeconds);
            _phaseEndsAt = (ctx.Now - step) + Spec.ClimbSeconds;
        }

        SetAloft(State != VulturePhase.Grounded);
        Vector3 velocity = Vector3.Zero;
        Vector3 hero = _agent.Blackboard.PlayerPosition;

        switch (State)
        {
            case VulturePhase.Climb:
                if (ctx.Now >= _phaseEndsAt)
                {
                    int offset = (int)(((long)DiveCount + _agent.Id) % 3);
                    float interval = Spec.MinIntervalSeconds
                        + ((Spec.MaxIntervalSeconds - Spec.MinIntervalSeconds) * (offset * 0.5f));
                    Enter(ctx, VulturePhase.Circle, interval);
                }

                break;

            case VulturePhase.Circle:
                if (ctx.Now < _phaseEndsAt || !TryLock(ctx, hero))
                {
                    velocity = Orbit(_agent.Position, hero, _agent.Id, step, Spec.CircleRadius, Spec.ApproachSpeed,
                        Spec.CircleSpeed, ref _facing);
                }

                break;

            case VulturePhase.Lock:
                // LaneSystem fires on this same run-clock deadline, before behaviours tick.
                if (ctx.Now >= _phaseEndsAt)
                {
                    DiveCount++;
                    Enter(ctx, VulturePhase.Dive, Spec.DiveSeconds);
                }

                break;

            case VulturePhase.Dive:
                if (step > 0f)
                {
                    float travelTime = MathF.Max(step, (_phaseEndsAt - ctx.Now) + step);
                    velocity = (_landing - _agent.Position) / travelTime;
                }

                if (ctx.Now >= _phaseEndsAt)
                {
                    _groundedElapsed = 0f;
                    Enter(ctx, VulturePhase.Grounded, Spec.GroundedSeconds);
                }

                break;

            case VulturePhase.Grounded:
                Vector2 aim = _agent.Blackboard.DirectionToRealPlayer;
                if (aim != Vector2.Zero)
                {
                    _facing = PlayerMotor.RotateTowards(_facing, new Vector3(aim.X, 0f, aim.Y),
                        ShamanBehaviour.TurnRateDegrees * step);
                }

                _groundedElapsed += MathF.Max(0f, step - GroundedHoldSeconds);
                GroundedHoldSeconds = 0f;
                if (_groundedElapsed >= Spec.GroundedSeconds)
                {
                    Enter(ctx, VulturePhase.Climb, Spec.ClimbSeconds);
                }

                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, new Vector2(_facing.X, _facing.Z)));
    }

    public void Reset()
    {
        State = VulturePhase.Climb;
        DiveCount = 0;
        _phaseEndsAt = 0f;
        _groundedElapsed = 0f;
        GroundedHoldSeconds = 0f;
        _announceClimb = true;
        _landing = Vector3.Zero;
        _facing = Vector3.Zero;
        SetAloft(true);
    }

    internal static Vector3 Orbit(Vector3 position, Vector3 hero, int id, float step, float radius,
        float approachSpeed, float orbitSpeed, ref Vector3 facing)
    {
        if (!(step > 0f))
        {
            return Vector3.Zero;
        }

        Vector3 offset = FlatDelta(position, hero);
        float distance = offset.Length();
        Vector3 radial = distance > 0f ? offset / distance : Vector3.UnitZ;
        float approachTime = MathF.Abs(radius - distance) / approachSpeed;
        Vector3 movement;

        if (step < approachTime)
        {
            float radialSpeed = distance < radius ? approachSpeed : -approachSpeed;
            movement = radial * (radialSpeed * step);
        }
        else
        {
            float arc = (step - approachTime) * (orbitSpeed / radius);
            if ((id & 1) != 0)
            {
                arc = -arc;
            }

            float sin = MathF.Sin(arc);
            float cos = MathF.Cos(arc);
            var rotated = new Vector3((radial.X * cos) + (radial.Z * sin), 0f,
                (radial.Z * cos) - (radial.X * sin));
            movement = (rotated * radius) - offset;
        }

        if (movement.LengthSquared() > 0f)
        {
            facing = Vector3.Normalize(movement);
        }

        return movement / step;
    }

    private bool TryLock(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector3 origin = _agent.Position;
        Vector3 towardHero = FlatDelta(hero, origin);
        Vector3 heading = towardHero.LengthSquared() > 0f ? Vector3.Normalize(towardHero) : Vector3.UnitZ;
        int laneId = ctx.Lanes.Open(origin, heading, Spec.LineLength, Spec.LineHalfWidth, Spec.LockSeconds,
            Spec.DiveDamage, ctx.Now);
        if (laneId == 0)
        {
            return false;
        }

        _facing = heading;
        _landing = GravemawBehaviour.FindOpenSpot(ctx.OpenGround, origin + (heading * Spec.LineLength), origin,
            float.PositiveInfinity);
        Enter(ctx, VulturePhase.Lock, Spec.LockSeconds);
        ctx.Events.Publish(new VultureDiveLocked(_agent.Id, laneId, origin, new Vector2(heading.X, heading.Z),
            _landing, Spec.LockSeconds));
        return true;
    }

    private void Enter(in EnemyTickContext ctx, VulturePhase phase, float seconds)
    {
        State = phase;
        _phaseEndsAt = ctx.Now + seconds;
        SetAloft(phase != VulturePhase.Grounded);
        ctx.Events.Publish(new VulturePhaseChanged(_agent.Id, phase, seconds));
    }

    private void SetAloft(bool aloft)
    {
        _agent.IsVulnerable = !aloft;
        _agent.IsHidden = aloft;
        _agent.Health.SetExternalInvulnerable(aloft);
    }

    private static Vector3 FlatDelta(Vector3 to, Vector3 from) => new(to.X - from.X, 0f, to.Z - from.Z);
}
