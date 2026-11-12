using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum ShamanState { Reposition, Cast, Flee, Mend }

/// <summary>Hides beyond weapon range, supports living allies and breaks away from pursuit.</summary>
/// <remarks>
/// It looks where it walks and turns to the player once it stands, so a sidestep or a flight never
/// plays the forward gait sideways or backwards; the turn is rate-limited, as Bulwark's is.
/// </remarks>
public sealed class ShamanBehaviour : IEnemyBehaviour
{
    /// <summary>How fast the body turns, in degrees per second: a half-turn in a third of a second.</summary>
    public const float TurnRateDegrees = 540f;

    private const float HealReportInterval = 0.25f;

    private readonly EnemyAgent _agent;
    private Vector2 _facing;
    private float _anchorTimer;
    private float _pulseTimer;
    private float _stateTimer;
    private IDomainEvents _mendEvents;
    private int _mendCasterId;
    private double _mendRemaining;
    private double _mendReadyAt;
    private float _lastHp;
    private double _nextHealReportAt;
    private int _healReportTargetId;
    private float _unreportedHeal;
    public ShamanBehaviour(EnemyAgent agent) { _agent = agent ?? throw new ArgumentNullException(nameof(agent)); Reset(); }
    public ShamanState State { get; private set; }
    public int AnchorId { get; private set; }
    public int MendTargetId { get; private set; }

    /// <summary>Where the body looks on the ground plane; zero until the first tick.</summary>
    public Vector2 Facing => _facing;

    public void Reset()
    {
        LoseMend();
        State = ShamanState.Reposition;
        AnchorId = -1;
        MendTargetId = -1;
        _anchorTimer = 0f;
        _pulseTimer = 2f;
        _stateTimer = 0f;
        _facing = Vector2.Zero;
        _mendReadyAt = double.NegativeInfinity;
        _lastHp = float.NaN;
        _nextHealReportAt = double.NegativeInfinity;
        _unreportedHeal = 0f;
    }

    public void Tick(in EnemyTickContext ctx)
    {
        ShamanSpec spec = _agent.Spec.Shaman;
        MendSpec mend = _agent.Spec.Mend;
        EnemyBlackboard bb = _agent.Blackboard;
        if (mend is not null)
        {
            if (_agent.Health.Current < _lastHp)
            {
                OnHpLost(ctx.Now);
            }
            _lastHp = _agent.Health.Current;
        }

        Vector2 away = -bb.DirectionToPlayer;
        if (away.LengthSquared() < 1e-6f)
        { away = Vector2.UnitY; }
        else
        { away = Vector2.Normalize(away); }
        Vector3 velocity = Vector3.Zero;
        Vector2 facing = -away;
        if (mend is null)
        {
            _pulseTimer = MathF.Max(0f, _pulseTimer - ctx.Dt);
        }
        _anchorTimer -= ctx.Dt;
        EnemyRegistry registry = ctx.Enemies.Registry;
        if (State != ShamanState.Mend &&
            (_anchorTimer <= 0f || !registry.TryGet(AnchorId, out EnemyAgent currentAnchor) || !currentAnchor.IsAlive))
        {
            PickAnchor(registry);
            _anchorTimer = 0.5f;
        }

        if (State != ShamanState.Flee && bb.DistanceToPlayer < spec.FleeDistance)
        {
            if (State == ShamanState.Mend)
            {
                FinishMend(ctx.Now, MendEndReason.Lost);
            }
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
        else if (State == ShamanState.Mend)
        {
            facing = TickMend(ctx, mend, facing);
        }
        else if (mend is not null && ctx.Now >= _mendReadyAt && PickMendTarget(registry, mend) is EnemyAgent target)
        {
            BeginMend(ctx.Events, target.Id, mend.ChannelSeconds);
            facing = DirectionTo(target, facing);
        }
        else if (mend is null && _pulseTimer <= 0f && HasAlly(registry, spec.AuraRadius))
        {
            State = ShamanState.Cast;
            _stateTimer = spec.CastSeconds;
            ctx.Events.Publish(new ShamanCast(_agent.Id, spec.CastSeconds));
        }
        else
        {
            Vector3 player = bb.PlayerPosition;
            Vector3 goal = player + new Vector3(away.X, 0f, away.Y) * (spec.HoldDistance + 1f);
            if (registry.TryGet(AnchorId, out EnemyAgent anchor) && anchor.IsAlive)
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
        Vector2 heading = new Vector2(velocity.X, velocity.Z);
        Turn(heading.LengthSquared() > 1e-4f ? Vector2.Normalize(heading) : facing, MathF.Max(0f, ctx.Dt));
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
        if (mend is not null)
        {
            ReportMendHeal(ctx);
        }
    }

    internal void OnHpLost(float now)
    {
        _lastHp = _agent.Health.Current;
        if (!_agent.IsAlive)
        {
            LoseMend();
            return;
        }

        MendSpec mend = _agent.Spec.Mend;
        _mendReadyAt = (double)now + mend.BrokenSeconds + mend.Cooldown;
        EndMend(MendEndReason.Broken);
    }

    internal void LoseMend() => EndMend(MendEndReason.Lost);

    private EnemyAgent PickMendTarget(EnemyRegistry registry, MendSpec mend)
    {
        EnemyAgent target = null;
        float lowest = mend.HurtBelow;
        float nearest = float.PositiveInfinity;
        float rangeSquared = mend.Range * mend.Range;
        foreach (EnemyAgent ally in registry.Alive)
        {
            if (!Ally(ally))
            {
                continue;
            }

            float distance = DistanceSquared(ally.Position, _agent.Position);
            float fraction = ally.Health.Fraction;
            if (distance <= rangeSquared &&
                (fraction < lowest || (target is not null && fraction == lowest && distance < nearest)))
            {
                target = ally;
                lowest = fraction;
                nearest = distance;
            }
        }

        return target;
    }

    private Vector2 DirectionTo(EnemyAgent ally, Vector2 fallback)
    {
        Vector3 delta = ally.Position - _agent.Position;
        Vector2 direction = new Vector2(delta.X, delta.Z);
        return direction.LengthSquared() > 1e-6f ? Vector2.Normalize(direction) : fallback;
    }

    private void BeginMend(IDomainEvents events, int allyId, float seconds)
    {
        State = ShamanState.Mend;
        AnchorId = MendTargetId = allyId;
        _mendRemaining = seconds;
        _mendEvents = events;
        _mendCasterId = _agent.Id;
        events.Publish(new PriestMendStarted(_mendCasterId, allyId, seconds));
    }

    private Vector2 TickMend(in EnemyTickContext ctx, MendSpec mend, Vector2 fallback)
    {
        if (!ctx.Enemies.Registry.TryGet(MendTargetId, out EnemyAgent ally) || !ally.IsAlive ||
            DistanceSquared(ally.Position, _agent.Position) > mend.BreakRange * mend.BreakRange)
        {
            FinishMend(ctx.Now, MendEndReason.Lost);
            return fallback;
        }

        double step = Math.Min(_mendRemaining, MathF.Max(0f, ctx.Dt));
        _mendRemaining -= step;
        float healed = ally.Health.Heal((mend.HealPerSecond * ally.Health.MaxHp.Value) * (float)step);
        if (healed > 0f)
        {
            if (_healReportTargetId != ally.Id)
            {
                _unreportedHeal = 0f;
                _healReportTargetId = ally.Id;
            }
            _unreportedHeal += healed;
        }

        if (ally.Health.Fraction >= 1f)
        {
            FinishMend(ctx.Now, MendEndReason.Healed);
        }
        else if (_mendRemaining <= 0d)
        {
            FinishMend(ctx.Now, MendEndReason.Done);
        }

        return DirectionTo(ally, fallback);
    }

    private void ReportMendHeal(in EnemyTickContext ctx)
    {
        if (_unreportedHeal <= 0f || ctx.Now < _nextHealReportAt)
        {
            return;
        }

        float healed = _unreportedHeal;
        _unreportedHeal = 0f;
        if (ctx.Enemies.Registry.TryGet(_healReportTargetId, out EnemyAgent ally) && ally.IsAlive)
        {
            _nextHealReportAt = (double)ctx.Now + HealReportInterval;
            ctx.Events.Publish(new EnemyHealed(ally.Id, healed, ally.Health.Fraction));
        }
    }

    private void FinishMend(float now, MendEndReason reason)
    {
        _mendReadyAt = (double)now + _agent.Spec.Mend.Cooldown;
        EndMend(reason);
    }

    private void EndMend(MendEndReason reason)
    {
        if (_mendEvents is null)
        {
            return;
        }

        IDomainEvents events = _mendEvents;
        int allyId = MendTargetId;
        _mendEvents = null;
        MendTargetId = AnchorId = -1;
        _anchorTimer = 0f;
        State = ShamanState.Reposition;
        events.Publish(new PriestMendEnded(_mendCasterId, allyId, reason));
    }

    private void Turn(Vector2 direction, float dt)
    {
        if (_facing == Vector2.Zero)
        {
            _facing = direction;
            return;
        }

        float angle = MathF.Atan2(_facing.X * direction.Y - _facing.Y * direction.X,
            Vector2.Dot(_facing, direction));
        float limit = TurnRateDegrees * (MathF.PI / 180f) * dt;
        float step = MathF.Max(-limit, MathF.Min(limit, angle));
        float sin = MathF.Sin(step);
        float cos = MathF.Cos(step);
        _facing = Vector2.Normalize(new Vector2(_facing.X * cos - _facing.Y * sin,
            _facing.X * sin + _facing.Y * cos));
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
