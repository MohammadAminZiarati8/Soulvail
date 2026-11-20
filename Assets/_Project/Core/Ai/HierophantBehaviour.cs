using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Locked ground attacks and the Hierophant's recurring Jackal escort.</summary>
public sealed class HierophantBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private readonly EnemyAgent _agent;
    private readonly HierophantStats _stats;
    private readonly ContentId _priestSpec = new("enemy.jackal-priest");
    private readonly ContentId _slingerSpec = new("enemy.jackal");
    private int _openingAttacksLeft = 2;
    private bool _openingComplete;
    private int _priestId;
    private int _slingerId;
    private float _replacementAt;
    private float _wardReadyAt;
    private bool _wardBroken;
    private bool _healAttempted;
    private float _healEndsAt;
    private float _healDuration;
    private float _now;
    private bool _wardActive;
    private Vector3 _wardPosition;
    private bool _telling;
    private int _phase;
    private HierophantMove _next = HierophantMove.SunSeal;
    private int _firstSealSlot = -1;
    private float _sealHalfWidth;
    private Vector2 _facing = Vector2.UnitY;
    private HierophantArmed _armed;
    private float _remaining;
    private float _damage;
    private float _recovery;
    private float _pairGap;
    private float _chaseSeconds;
    private float _fireDuration;
    private float _fireDepth;
    private float _fireElapsed;
    private bool _fireActive;
    private bool _fireHit;

    public HierophantBehaviour(EnemyAgent agent, HierophantStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        if (_sealHalfWidth == 0f)
            PlaceSeals(ctx);

        Vector3 velocity = Vector3.Zero;
        float dt = MathF.Max(0f, ctx.Dt);
        if (_fireActive)
            AdvanceFire(ctx, dt);
        float available = MathF.Max(0f, dt - _remaining);
        _remaining = MathF.Max(0f, _remaining - dt);
        if (_fireActive && _fireElapsed >= _fireDuration && _remaining == 0f)
            EndFire(ctx.Events);
        if (_telling)
        {
            ctx.Events.Publish(new HierophantTellProgress(_agent.Id,
                Math.Clamp(1f - _remaining / _armed.Seconds, 0f, 1f)));
            // An impact starts a fresh clock. This tick's excess time belongs to the tell.
            if (_remaining == 0f)
                Impact(ctx);
        }
        else if (_remaining == 0f && !_fireActive)
        {
            _openingComplete = _openingAttacksLeft == 0;
            velocity = Prepare(ctx, dt, available);
        }

        TickEscort(ctx);
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

    internal bool IsWardPriest(int id) => _wardActive && _agent.IsAlive && id == _priestId;
    internal bool IsHealingPriest(int id) => _healEndsAt > 0f && _agent.IsAlive && id == _priestId;
    internal float LastTickTime => _now;

    internal float WardDamageScale(EnemyRegistry registry) =>
        _wardActive && IsLiving(registry, _priestId) ? _stats.WardDamageScale.Value : 1f;

    internal void SuspendWard(float now, IDomainEvents events)
    {
        EndHeal(events);
        if (!_wardActive)
            return;

        _wardActive = false;
        _wardBroken = true;
        _wardReadyAt = now + _stats.WardBreakSeconds.Value;
        events.Publish(new HierophantWard(_agent.Id, _priestId, false, default));
    }

    internal void OnEscortHit(int id, bool killed, float now, EnemyRegistry registry, IDomainEvents events)
    {
        if (id == _priestId)
            SuspendWard(now, events);
        if (killed)
            OnEscortLost(id, now, registry, events);
    }

    internal void OnEscortDespawn(int id, EnemyRegistry registry, IDomainEvents events) =>
        OnEscortLost(id, _now, registry, events);

    private void OnEscortLost(int id, float now, EnemyRegistry registry, IDomainEvents events)
    {
        if (id == _priestId)
        {
            SuspendWard(now, events);
            _priestId = 0;
        }
        else if (id == _slingerId)
        {
            _slingerId = 0;
        }
        else
        {
            return;
        }

        if (!IsLiving(registry, _priestId) && !IsLiving(registry, _slingerId))
            _replacementAt = now + _stats.AddReplacementSeconds.Value;
    }

    private void TickEscort(in EnemyTickContext ctx)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        if (_openingComplete && ctx.Now >= _replacementAt
            && !IsLiving(registry, _priestId) && !IsLiving(registry, _slingerId)
            && registry.Capacity - registry.AliveCount >= 2)
        {
            Vector3 offset = new(_facing.Y * BossBehaviour.AddRingRadius, 0f,
                -_facing.X * BossBehaviour.AddRingRadius);
            _priestId = ctx.Enemies.SpawnEscort(_priestSpec, EscortPosition(_agent.Position + offset),
                this, _stats.AddBonus.Value, 0f).Id;
            _slingerId = ctx.Enemies.SpawnEscort(_slingerSpec, EscortPosition(_agent.Position - offset),
                this, _stats.AddBonus.Value, _stats.AddBonus.Value).Id;
            _wardBroken = _healAttempted = false;
        }

        if (!registry.TryGet(_priestId, out EnemyAgent priest) || !priest.IsAlive || priest.CrowdControl.IsStunned)
        {
            SuspendWard(ctx.Now, ctx.Events);
            return;
        }

        if (_wardActive)
        {
            if (_wardPosition == priest.Position)
                return;
        }
        else
        {
            if (ctx.Now < _wardReadyAt)
                return;

            if (TickHeal(ctx, priest))
                return;

            (priest.Behaviour as ShamanBehaviour)?.LoseMend();
            _wardActive = true;
        }

        _wardPosition = priest.Position;
        ctx.Events.Publish(new HierophantWard(_agent.Id, _priestId, true, _wardPosition));
    }

    private bool TickHeal(in EnemyTickContext ctx, EnemyAgent priest)
    {
        if (_healEndsAt > 0f)
        {
            if (ctx.Now < _healEndsAt)
            {
                float fraction = Math.Clamp(1f - (_healEndsAt - ctx.Now) / _healDuration, 0f, 1f);
                ctx.Events.Publish(new HierophantHealChannel(_agent.Id, priest.Position, fraction, true));
                return true;
            }

            EndHeal(ctx.Events);
            float healed = _agent.Health.Heal(_agent.Health.MaxHp.Value * _stats.HealFraction.Value);
            if (healed > 0f)
                ctx.Events.Publish(new EnemyHealed(_agent.Id, healed, _agent.Health.Fraction));
        }
        else if (_wardBroken && !_healAttempted && _agent.Health.Fraction < 1f)
        {
            _healDuration = _stats.HealChannelSeconds.Value;
            if (!(_healDuration > 0f) || !float.IsFinite(_healDuration))
                throw new ArgumentOutOfRangeException(nameof(_stats.HealChannelSeconds));

            _healAttempted = true;
            _healEndsAt = ctx.Now + _healDuration;
            (priest.Behaviour as ShamanBehaviour)?.LoseMend();
            ctx.Events.Publish(new HierophantHealChannel(_agent.Id, priest.Position, 0f, true));
            return true;
        }

        return false;
    }

    private void EndHeal(IDomainEvents events)
    {
        if (_healEndsAt == 0f)
            return;

        _healEndsAt = 0f;
        events.Publish(new HierophantHealChannel(_agent.Id, default, 0f, false));
    }

    private Vector3 EscortPosition(Vector3 position)
    {
        float edge = _stats.DunesHalfExtent.Value - 1f;
        return new Vector3(Math.Clamp(position.X, -edge, edge), position.Y, Math.Clamp(position.Z, -edge, edge));
    }

    private static bool IsLiving(EnemyRegistry registry, int id) =>
        registry.TryGet(id, out EnemyAgent agent) && agent.IsAlive;

    private void PlaceSeals(in EnemyTickContext ctx)
    {
        float halfWidth = _stats.SealHalfWidth.Value;
        if (!(halfWidth > 0f) || !float.IsFinite(2f * halfWidth))
            throw new ArgumentOutOfRangeException(nameof(_stats.SealHalfWidth));

        _sealHalfWidth = halfWidth;
        var east = new Vector2(2f * halfWidth, 0f);
        ctx.Events.Publish(new HierophantSealsPlaced(_agent.Id, -east, Vector2.Zero, east));
    }

    private Vector3 Prepare(in EnemyTickContext ctx, float dt, float available)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 hero = board.PlayerPosition;
        Vector3 origin = board.SelfPosition;
        var towardHero = new Vector2(hero.X - origin.X, hero.Z - origin.Z);
        float distance = towardHero.Length();
        if (!float.IsFinite(distance) || !float.IsFinite(origin.Y))
            return Vector3.Zero;

        Vector2 direction = distance > 0f ? towardHero / distance : _facing;
        if (_next == HierophantMove.SunSeal)
        {
            ArmSeal(ctx, origin, direction, distance, hero.X);
            return Vector3.Zero;
        }

        float range = _stats.FireReach.Value;
        if (!(range > 0f) || !float.IsFinite(range))
            throw new ArgumentOutOfRangeException(nameof(_stats.FireReach));

        if (distance <= range)
        {
            _chaseSeconds = 0f;
            if (distance == 0f || TurnTowards(direction, dt))
            {
                Arm(ctx, new HierophantArmed(_agent.Id, _next, -1, origin, _facing,
                    range, _stats.FireHalfWidth.Value, _stats.FireTellSeconds.Value),
                    _stats.FireDamage.Value, _stats.FireRecoverySeconds.Value);
            }
            return Vector3.Zero;
        }

        float chaseLimit = _stats.FireChaseSeconds.Value;
        if (!(chaseLimit > 0f) || !float.IsFinite(chaseLimit))
            throw new ArgumentOutOfRangeException(nameof(_stats.FireChaseSeconds));
        _chaseSeconds += available;
        if (_chaseSeconds >= chaseLimit)
        {
            ArmSeal(ctx, origin, direction, distance, hero.X);
            return Vector3.Zero;
        }

        TurnTowards(direction, dt);
        // The path steers movement around cover while the body turns toward the hero.
        Vector2 path = board.PathDirectionToPlayer;
        if (path == Vector2.Zero)
            path = direction;
        float pathLength = path.Length();
        float speed = _stats.MoveSpeed.Value;
        return pathLength > 0f && float.IsFinite(pathLength) && speed > 0f && float.IsFinite(speed)
            ? Ground(path / pathLength * speed)
            : Vector3.Zero;
    }

    private void ArmSeal(in EnemyTickContext ctx, Vector3 origin, Vector2 facing, float distance, float targetX)
    {
        float edge = _stats.DunesHalfExtent.Value;
        if (!(edge > 0f) || !float.IsFinite(edge))
            throw new ArgumentOutOfRangeException(nameof(_stats.DunesHalfExtent));

        // The Dunes' playable square is centred at world zero. End at the first boundary
        // in the cast direction, including the target if its sample is just outside the edge.
        float eastWest = DistanceToEdge(origin.X, facing.X, edge);
        float northSouth = DistanceToEdge(origin.Z, facing.Y, edge);
        float reach = MathF.Max(distance, MathF.Min(eastWest, northSouth));
        if (!(reach > 0f))
            return;

        _facing = facing;
        _chaseSeconds = 0f;
        Arm(ctx, new HierophantArmed(_agent.Id, HierophantMove.SunSeal, SelectSeal(targetX),
            new Vector3(origin.X, 0f, origin.Z), facing, reach, _sealHalfWidth, _stats.SealTellSeconds.Value),
            _stats.SealDamage.Value, _stats.SealRecoverySeconds.Value);
    }

    private static float DistanceToEdge(float origin, float direction, float edge) =>
        direction == 0f ? float.PositiveInfinity : ((direction > 0f ? edge : -edge) - origin) / direction;

    private bool TurnTowards(Vector2 target, float dt)
    {
        float turnSpeed = _stats.TurnDegreesPerSecond.Value;
        if (!float.IsFinite(turnSpeed))
            throw new ArgumentOutOfRangeException(nameof(_stats.TurnDegreesPerSecond));

        float step = MathF.Min(180f, MathF.Max(0f, turnSpeed) * dt);
        Vector3 heading = PlayerMotor.RotateTowards(Ground(_facing), Ground(target), step);
        _facing = new Vector2(heading.X, heading.Z);
        return _facing == target;
    }

    private int SelectSeal(float x)
    {
        // Compare against midpoints between the eligible centres. Exact ties go west.
        return _firstSealSlot switch
        {
            0 => x <= _sealHalfWidth ? 1 : 2,
            1 => x <= 0f ? 0 : 2,
            2 => x <= -_sealHalfWidth ? 0 : 1,
            _ => x <= -_sealHalfWidth ? 0 : x <= _sealHalfWidth ? 1 : 2
        };
    }

    private void Arm(in EnemyTickContext ctx, in HierophantArmed armed, float damage, float recovery)
    {
        if (!(damage >= 0f) || !float.IsFinite(damage))
            throw new ArgumentOutOfRangeException(nameof(damage));
        if (!(recovery >= 0f) || !float.IsFinite(recovery))
            throw new ArgumentOutOfRangeException(nameof(recovery));

        if (armed.Move == HierophantMove.FireThrow)
        {
            _fireDuration = _stats.FireTravelSeconds.Value;
            _fireDepth = _stats.FireDepth.Value;
            if (!(_fireDuration > 0f) || !float.IsFinite(_fireDuration))
                throw new ArgumentOutOfRangeException(nameof(_stats.FireTravelSeconds));
            if (!(_fireDepth > 0f) || !float.IsFinite(_fireDepth))
                throw new ArgumentOutOfRangeException(nameof(_stats.FireDepth));
        }
        else if (_phase == 2 && _firstSealSlot < 0)
        {
            float gap = _stats.SealPairGapSeconds.Value;
            if (!(gap >= 0f) || !float.IsFinite(gap))
                throw new ArgumentOutOfRangeException(nameof(_stats.SealPairGapSeconds));

            _pairGap = gap;
        }

        _armed = armed;
        _remaining = armed.Seconds;
        _damage = damage;
        _recovery = recovery;
        _telling = true;
        ctx.Events.Publish(armed);
    }

    private void Impact(in EnemyTickContext ctx)
    {
        if (_openingAttacksLeft > 0)
            _openingAttacksLeft--;
        bool seal = _armed.Move == HierophantMove.SunSeal;
        if (seal)
            TryHitStrip(ctx, 0f, _armed.Reach);

        bool firstOfPair = seal && _phase == 2 && _firstSealSlot < 0;
        if (firstOfPair)
        {
            _firstSealSlot = _armed.SealSlot;
            _next = HierophantMove.SunSeal;
            _remaining = _pairGap;
        }
        else
        {
            _firstSealSlot = -1;
            _next = seal ? HierophantMove.FireThrow : HierophantMove.SunSeal;
            _remaining = _recovery;
        }
        _telling = false;
        ctx.Events.Publish(new HierophantImpact(_agent.Id, _armed.Move, _armed.SealSlot,
            firstOfPair ? 0f : _recovery));
        if (!seal)
        {
            _fireElapsed = 0f;
            _fireHit = false;
            _fireActive = true;
            ctx.Events.Publish(new HierophantFireWave(_agent.Id, 0f, 0f, 0f, true));
        }
    }

    private void AdvanceFire(in EnemyTickContext ctx, float dt)
    {
        if (_fireElapsed >= _fireDuration)
        {
            EndFire(ctx.Events);
            return;
        }

        float previousFront = _armed.Reach * (_fireElapsed / _fireDuration);
        _fireElapsed = MathF.Min(_fireDuration, _fireElapsed + dt);
        float fraction = _fireElapsed / _fireDuration;
        float front = _armed.Reach * fraction;
        float rear = MathF.Max(0f, front - _fireDepth);
        if (!_fireHit && front > previousFront)
            _fireHit = TryHitStrip(ctx, MathF.Max(0f, previousFront - _fireDepth), front);

        ctx.Events.Publish(new HierophantFireWave(_agent.Id, rear, front, fraction, true));
    }

    private void EndFire(IDomainEvents events)
    {
        _fireActive = false;
        events.Publish(new HierophantFireWave(_agent.Id, 0f, 0f, 1f, false));
    }

    private bool TryHitStrip(in EnemyTickContext ctx, float rear, float front)
    {
        EnemyBlackboard board = _agent.Blackboard;
        if (board.QuarryIsADecoy || board.QuarryIsAPet)
            return false;

        var offset = new Vector2(board.PlayerPosition.X - _armed.Origin.X, board.PlayerPosition.Z - _armed.Origin.Z);
        float along = Vector2.Dot(_armed.Facing, offset);
        float across = _armed.Facing.Y * offset.X - _armed.Facing.X * offset.Y;
        if (!(along >= rear && along <= front && MathF.Abs(across) <= _armed.HalfWidth))
            return false;

        ctx.Player.ApplyDamage(_damage, ctx.Now);
        return true;
    }

    private Vector3 Ground(Vector2 flat) => new(flat.X, 0f, flat.Y);

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _telling = false;
        _fireActive = false;
        _next = HierophantMove.SunSeal;
        _firstSealSlot = -1;
        _remaining = 0f;
        _chaseSeconds = 0f;
        _armed = default;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _openingAttacksLeft = 2;
        _openingComplete = false;
        _priestId = _slingerId = 0;
        _replacementAt = _wardReadyAt = _healEndsAt = _now = 0f;
        _wardActive = _wardBroken = _healAttempted = false;
        _sealHalfWidth = 0f;
        _facing = Vector2.UnitY;
    }
}
