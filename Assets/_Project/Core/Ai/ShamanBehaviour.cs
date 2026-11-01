using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum ShamanState { Reposition, Cast, Flee }

/// <summary>Hides beyond weapon range, supports living allies and breaks away from pursuit.</summary>
public sealed class ShamanBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private float _anchorTimer;
    private float _pulseTimer;
    private float _stateTimer;
    public ShamanBehaviour(EnemyAgent agent) { _agent = agent ?? throw new ArgumentNullException(nameof(agent)); Reset(); }
    public ShamanState State { get; private set; }
    public int AnchorId { get; private set; }

    public void Reset()
    {
        State = ShamanState.Reposition;
        AnchorId = -1;
        _anchorTimer = 0f;
        _pulseTimer = 2f;
        _stateTimer = 0f;
    }

    public void Tick(in EnemyTickContext ctx)
    {
        ShamanSpec spec = _agent.Spec.Shaman;
        EnemyBlackboard bb = _agent.Blackboard;
        Vector2 away = -bb.DirectionToPlayer;
        if (away.LengthSquared() < 1e-6f)
        { away = Vector2.UnitY; }
        else
        { away = Vector2.Normalize(away); }
        Vector3 velocity = Vector3.Zero;
        _pulseTimer = MathF.Max(0f, _pulseTimer - ctx.Dt);
        _anchorTimer -= ctx.Dt;
        if (_anchorTimer <= 0f || !ctx.Enemies.Registry.TryGet(AnchorId, out EnemyAgent anchor) || !anchor.IsAlive)
        {
            PickAnchor(ctx.Enemies.Registry);
            _anchorTimer = 0.5f;
        }

        if (State != ShamanState.Flee && bb.DistanceToPlayer < spec.FleeDistance)
        {
            State = ShamanState.Flee;
            _stateTimer = spec.FleeSeconds;
        }

        if (State == ShamanState.Flee)
        {
            velocity = new Vector3(away.X, 0f, away.Y) * (_agent.MoveSpeed.Value * spec.FleeSpeedMultiplier);
            _stateTimer -= ctx.Dt;
            if (_stateTimer <= 0f)
            { State = ShamanState.Reposition; }
        }
        else if (State == ShamanState.Cast)
        {
            _stateTimer -= ctx.Dt;
            if (_stateTimer <= 0f)
            {
                Pulse(ctx, spec);
                State = ShamanState.Reposition;
                _pulseTimer = spec.PulseInterval;
            }
        }
        else if (_pulseTimer <= 0f && HasAlly(ctx.Enemies.Registry, spec.AuraRadius))
        {
            State = ShamanState.Cast;
            _stateTimer = spec.CastSeconds;
            ctx.Events.Publish(new ShamanCast(_agent.Id, spec.CastSeconds));
        }
        else
        {
            Vector3 player = bb.PlayerPosition;
            Vector3 goal = player + new Vector3(away.X, 0f, away.Y) * (spec.HoldDistance + 1f);
            if (ctx.Enemies.Registry.TryGet(AnchorId, out anchor) && anchor.IsAlive)
            {
                Vector3 radial = anchor.Position - player;
                radial.Y = 0f;
                if (radial.LengthSquared() < 1e-6f)
                { radial = new Vector3(away.X, 0f, away.Y); }
                else
                { radial = Vector3.Normalize(radial); }
                goal = anchor.Position + radial * spec.BehindDistance;
                Vector3 offset = goal - player;
                offset.Y = 0f;
                if (offset.Length() < spec.HoldDistance)
                { goal = player + radial * spec.HoldDistance; }
            }
            Vector3 delta = goal - _agent.Position;
            delta.Y = 0f;
            float distance = delta.Length();
            if (distance > 0.4f)
            {
                float speed = _agent.MoveSpeed.Value;
                if (ctx.Dt > 0f)
                { speed = MathF.Min(speed, distance / ctx.Dt); }
                velocity = delta / distance * speed;
            }
        }
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, -away));
    }

    private bool Ally(EnemyAgent other) => other.Id != _agent.Id && other.IsAlive &&
        other.Spec.Behaviour != EnemyBehaviourKind.Shaman && other.Spec.Behaviour != EnemyBehaviourKind.Boss;

    private static float DistanceSquared(Vector3 a, Vector3 b)
    { float x = a.X - b.X; float z = a.Z - b.Z; return x * x + z * z; }

    private void PickAnchor(EnemyRegistry registry)
    {
        AnchorId = -1;
        float nearest = 144.00001f;
        bool shield = false;
        foreach (EnemyAgent ally in registry.Alive)
        {
            if (!Ally(ally))
            { continue; }
            float distance = DistanceSquared(ally.Position, _agent.Position);
            bool bulwark = ally.Spec.Behaviour == EnemyBehaviourKind.Bulwark;
            if (distance > 144f || (shield && !bulwark))
            { continue; }
            if ((bulwark && !shield) || distance < nearest)
            { AnchorId = ally.Id; nearest = distance; shield = bulwark; }
        }
    }

    private bool HasAlly(EnemyRegistry registry, float radius)
    {
        foreach (EnemyAgent ally in registry.Alive)
        { if (Ally(ally) && DistanceSquared(ally.Position, _agent.Position) <= radius * radius) { return true; } }
        return false;
    }

    private void Pulse(in EnemyTickContext ctx, ShamanSpec spec)
    {
        int touched = 0;
        foreach (EnemyAgent ally in ctx.Enemies.Registry.Alive)
        {
            if (!Ally(ally) || DistanceSquared(ally.Position, _agent.Position) > spec.AuraRadius * spec.AuraRadius)
            { continue; }
            float healed = ally.Health.Heal(ally.Health.MaxHp.Value * spec.HealFraction);
            if (healed > 0f)
            { ctx.Events.Publish(new EnemyHealed(ally.Id, healed, ally.Health.Fraction)); }
            ctx.Enemies.Haste(ally.Id, spec.HasteFraction, spec.HasteSeconds);
            touched++;
        }
        ctx.Events.Publish(new ShamanPulsed(_agent.Id, _agent.Position, spec.AuraRadius, touched));
    }
}
