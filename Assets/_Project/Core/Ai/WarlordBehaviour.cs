using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum WarlordMove { None, Sweep = 1, Charge = 2, WarCry = 3, Lob = 4, Combo = 5, ComboSecond = 6 }

/// <summary>
/// A slow turning frontal guard with a committed sweep, shield charge and war cry; spear lobs from
/// phase 1, and from phase 2 a shattered shield: no guard, faster, and a two-hit combo.
/// </summary>
public sealed class WarlordBehaviour : IEnemyBehaviour, IBossPhaseListener, ILaneStopListener, IBossAddOwner,
    IFrontalGuard
{
    public const float HalfArcDeg = 60f, TurnRateDeg = 40f, WalkConeDeg = 45f;
    public const float AttackGapSeconds = 1.4f, RecoverSeconds = 0.8f;
    public const float SweepWindupSeconds = 0.8f, SweepHalfAngleDeg = 60f, SweepReach = 3.5f,
        SweepDamageScale = 1.5f;
    public const float ChargeTelegraphSeconds = 0.9f, ChargeMinRange = 4f, ChargeLength = 14f,
        ChargeHalfWidth = 1.1f, ChargeSpeed = 16f, ChargeDamageScale = 2f, ChargeCooldownSeconds = 6f,
        ProbeRadius = 0.8f, DazeSeconds = 2f, DazeSlack = 1f, DashGraceSeconds = 0.3f;
    public const float WarCryTelegraphSeconds = 1f, WarCryCooldownSeconds = 14f,
        WarCryFirstAtSeconds = 8f, LockRadius = 3.5f, LockedHalfArcDeg = 90f, EdgeSpacing = 6f;
    public const int MaxGuards = 2;
    public const float LobTelegraphSeconds = 0.8f, LobCooldownSeconds = 5f, LobMinRange = 5f,
        LobSpeed = 9f, LobRadius = 1.4f, LobSpread = 2.5f;
    public const int LobSpears = 3;
    public const float ShatteredSpeedBonus = 0.4f, ShatteredTurnRateDeg = 120f,
        ShatteredRecoverSeconds = 0.5f, ShatteredAttackGapSeconds = 0.9f, ComboSecondWindupSeconds = 0.6f;

    private readonly EnemyAgent _agent;
    private readonly ProjectileSystem _projectiles;
    private bool _firstTick = true;
    private float _publishedArc = -1f;
    private float _remaining = AttackGapSeconds;
    private bool _recovering;
    private Vector2 _aim;
    private Vector3 _origin;
    private IDomainEvents _coneEvents;
    private float _chargeCooldown;
    private bool _dashing;
    private bool _laneOpened;
    private bool _probeAnswered;
    private bool _probeHit;
    private float _probeDistance;
    private float _laneLength;
    private float _dashElapsed;
    private int _probeTicks;
    private readonly ContentId _guardId;
    private readonly int[] _guardIds = new int[MaxGuards];
    private readonly Vector3[] _picks = new Vector3[MaxGuards];
    private float _warCryCooldown = WarCryFirstAtSeconds;
    private float _lobCooldown;
    private bool _shatterPending;

    public WarlordBehaviour(EnemyAgent agent, ProjectileSystem projectiles, IRandomStream random,
        ContentId guardId)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        // The lob's spread is fixed, so a volley spends none of the stream it may draw from.
        _ = random ?? throw new ArgumentNullException(nameof(random));
        _guardId = guardId;
    }

    public WarlordMove Move { get; private set; }
    public float GuardHalfArcDeg { get; private set; } = HalfArcDeg;
    public Vector2 Facing { get; private set; } = Vector2.UnitY;

    /// <summary>BT-09b rule 4: no guard while dazed or shattered (arc 0), else the source inside the arc.</summary>
    bool IFrontalGuard.Covers(Vector2 towardSource) =>
        GuardHalfArcDeg > 0f && InArc(Facing, towardSource, GuardHalfArcDeg);
    public bool IsDazed { get; private set; }
    public int GuardCount { get; private set; }
    public bool IsLocked { get; private set; }
    public int Phase { get; private set; }
    public bool IsShattered { get; private set; }

    private float Gap => IsShattered ? ShatteredAttackGapSeconds : AttackGapSeconds;
    private float Recover => IsShattered ? ShatteredRecoverSeconds : RecoverSeconds;

    public void Tick(in EnemyTickContext ctx)
    {
        // OnPhaseEntered has no context, so the shatter is announced on the next tick the boss runs.
        if (_shatterPending)
        {
            _shatterPending = false;
            ctx.Events.Publish(new WarlordShattered(_agent.Id));
        }

        RollCall(ctx.Enemies.Registry);
        if (_firstTick)
        {
            Facing = Aim();
            _firstTick = false;
        }
        else if (Move == WarlordMove.None && !_recovering && !IsDazed)
        {
            Turn(Path(), ctx.Dt);
        }

        _remaining -= ctx.Dt;
        _chargeCooldown = MathF.Max(0f, _chargeCooldown - ctx.Dt);
        _warCryCooldown = MathF.Max(0f, _warCryCooldown - ctx.Dt);
        _lobCooldown = MathF.Max(0f, _lobCooldown - ctx.Dt);
        if (IsDazed)
        {
            if (_remaining <= 0f)
            {
                IsDazed = false;
                _remaining = Gap;
            }
        }
        else if (Move == WarlordMove.Charge)
        {
            TickCharge(ctx);
        }
        else if (Move != WarlordMove.None)
        {
            if (_remaining <= 0f)
            {
                if (Move == WarlordMove.WarCry)
                {
                    ReleaseWarCry(ctx);
                }
                else if (Move == WarlordMove.Lob)
                {
                    ReleaseLob(ctx);
                }
                else
                {
                    Release(ctx);
                }
            }
        }
        else if (_recovering)
        {
            if (_remaining <= 0f)
            {
                _recovering = false;
                _remaining = Gap;
            }
        }
        else if (_remaining <= 0f)
        {
            if (_warCryCooldown <= 0f && GuardCount < MaxGuards
                && ctx.Enemies.Registry.AliveCount < ctx.Enemies.Registry.Capacity)
            {
                Move = WarlordMove.WarCry;
                _remaining = WarCryTelegraphSeconds;
                ctx.Events.Publish(new EnemyTelegraph(_agent.Id, WarCryTelegraphSeconds, (int)Move));
            }
            else if (Phase >= 1 && _lobCooldown <= 0f && _agent.Blackboard.DistanceToPlayer >= LobMinRange)
            {
                Move = WarlordMove.Lob;
                _remaining = LobTelegraphSeconds;
                Facing = Aim();
                ctx.Events.Publish(new EnemyTelegraph(_agent.Id, LobTelegraphSeconds, (int)Move));
            }
            else if (_agent.Blackboard.DistanceToPlayer <= SweepReach)
            {
                Arm(ctx);
            }
            else if (_agent.Blackboard.DistanceToPlayer >= ChargeMinRange && _chargeCooldown <= 0f)
            {
                ArmCharge(ctx);
            }
        }

        IsLocked = !IsShattered && !IsDazed && AnyGuardNear(ctx.Enemies.Registry);
        GuardHalfArcDeg = IsShattered || IsDazed ? 0f : IsLocked ? LockedHalfArcDeg : HalfArcDeg;
        bool guarded = GuardHalfArcDeg > 0f
            && InArc(Facing, _agent.Blackboard.DirectionToPlayer, GuardHalfArcDeg);
        _agent.Health.SetExternalInvulnerable(guarded);
        _agent.IsVulnerable = !guarded;
        if (_publishedArc != GuardHalfArcDeg)
        {
            ctx.Events.Publish(new WarlordGuardArc(_agent.Id, GuardHalfArcDeg));
            _publishedArc = GuardHalfArcDeg;
        }

        float speed = _dashing ? ChargeSpeed : Move == WarlordMove.None && !_recovering && !IsDazed
            && InArc(Facing, Path(), WalkConeDeg) ? _agent.MoveSpeed.Value : 0f;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id,
            new Vector3(Facing.X * speed, 0f, Facing.Y * speed), Facing));
    }

    private void ArmCharge(in EnemyTickContext ctx)
    {
        Move = WarlordMove.Charge;
        _remaining = ChargeTelegraphSeconds;
        _aim = Aim();
        _origin = _agent.Blackboard.SelfPosition;
        Facing = _aim;
        _dashing = false;
        _laneOpened = false;
        _probeAnswered = false;
        _probeHit = false;
        _probeTicks = 0;
        _laneLength = ChargeLength;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, ChargeTelegraphSeconds, (int)Move));
        ctx.Intents.LaneProbe(new LaneProbeIntent(_agent.Id, _origin,
            new Vector3(_aim.X, 0f, _aim.Y), ChargeLength, ProbeRadius));
    }

    public void OnLaneStop(bool hit, float distance)
    {
        // Once opened, the promised strip cannot be shortened by a late or duplicate answer.
        if (Move != WarlordMove.Charge || _laneOpened || _probeAnswered
            || (hit && (!(distance >= 0f) || float.IsInfinity(distance))))
        {
            return;
        }

        _probeAnswered = true;
        _probeHit = hit;
        _probeDistance = distance;
    }

    private void TickCharge(in EnemyTickContext ctx)
    {
        if (!_dashing)
        {
            _probeTicks++;
            if (!_laneOpened && (_probeAnswered || _probeTicks >= 2 || _remaining <= 0f))
            {
                _laneLength = _probeHit ? MathF.Min(ChargeLength, _probeDistance) : ChargeLength;
                if (_laneLength > 0f)
                {
                    if (ctx.Lanes is null)
                    {
                        throw new InvalidOperationException("A Warlord charge requires a LaneSystem.");
                    }

                    // A coarse tick can cross the deadline before the probe fallback. Epsilon
                    // keeps Open's positive-duration contract and makes it due on this tick.
                    ctx.Lanes.Open(_origin, new Vector3(_aim.X, 0f, _aim.Y), _laneLength,
                        ChargeHalfWidth, MathF.Max(float.Epsilon, _remaining),
                        _agent.ContactDamage.Value * ChargeDamageScale, ctx.Now);
                }
                _laneOpened = true;
            }

            if (_remaining > 0f)
            {
                return;
            }

            _dashing = true;
            _dashElapsed = 0f;
        }
        else
        {
            _dashElapsed += ctx.Dt;
        }

        Vector3 delta = _agent.Blackboard.SelfPosition - _origin;
        float travel = new Vector2(delta.X, delta.Z).Length();
        if (travel >= _laneLength - 0.1f
            || _dashElapsed >= _laneLength / ChargeSpeed + DashGraceSeconds)
        {
            _dashing = false;
            Move = WarlordMove.None;
            _chargeCooldown = ChargeCooldownSeconds;
            IsDazed = _probeHit && travel >= _probeDistance - DazeSlack;
            _recovering = !IsDazed;
            _remaining = IsDazed ? DazeSeconds : Recover;
            if (IsDazed)
            {
                ctx.Events.Publish(new WarlordDazed(_agent.Id, _agent.Blackboard.SelfPosition, DazeSeconds));
            }
        }
    }

    private void Arm(in EnemyTickContext ctx) =>
        ArmCone(ctx, IsShattered ? WarlordMove.Combo : WarlordMove.Sweep, SweepWindupSeconds);

    private void ArmCone(in EnemyTickContext ctx, WarlordMove move, float windup)
    {
        Move = move;
        _remaining = windup;
        _aim = Aim();
        _origin = _agent.Blackboard.SelfPosition;
        Facing = _aim;
        _coneEvents = ctx.Events;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, windup, (int)Move));
        ctx.Events.Publish(new SprayArmed(_agent.Id, _origin, _aim,
            SweepHalfAngleDeg, SweepReach, windup));
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
        if (Move == WarlordMove.Combo)
        {
            // The combo's second hit re-aims at the player and winds its own cone.
            ArmCone(ctx, WarlordMove.ComboSecond, ComboSecondWindupSeconds);
            return;
        }

        Move = WarlordMove.None;
        _recovering = true;
        _remaining = Recover;
        Facing = _aim;
    }

    private void ReleaseLob(in EnemyTickContext ctx)
    {
        Vector3 self = _agent.Blackboard.SelfPosition;
        Vector3 target = _agent.Blackboard.PlayerPosition;
        Vector2 line = Aim();
        var across = new Vector3(-line.Y, 0f, line.X) * LobSpread;
        float damage = _agent.ContactDamage.Value;
        for (int i = 0; i < LobSpears; i++)
        {
            Vector3 offset = i == 0 ? Vector3.Zero : i == 1 ? across : -across;
            _projectiles.Fire(new Projectile(_agent.Spec.Id, _agent.Id, self, target + offset,
                LobSpeed, LobRadius, damage), ctx.Now);
        }

        Move = WarlordMove.None;
        _recovering = true;
        _remaining = Recover;
        _lobCooldown = LobCooldownSeconds;
    }

    private void ReleaseWarCry(in EnemyTickContext ctx)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        int wanted = Math.Min(MaxGuards - GuardCount, registry.Capacity - registry.AliveCount);
        Vector3 self = _agent.Blackboard.SelfPosition;
        Vector3 player = _agent.Blackboard.PlayerPosition;
        int picked = 0;
        while (picked < wanted)
        {
            int best = -1;
            float bestDistance = -1f;
            for (int i = 0; i < ctx.OpenGround.Count; i++)
            {
                Vector3 point = ctx.OpenGround[i];
                if (!(FlatSquared(point, player)
                        >= OpenGroundPicker.PlayerClearance * OpenGroundPicker.PlayerClearance)
                    || !Spaced(point, picked))
                {
                    continue;
                }

                float distance = FlatSquared(point, self);
                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                break;
            }

            _picks[picked++] = ctx.OpenGround[best];
        }

        // Too little open ground: the rest stand on the add ring, on the side away from the player.
        Vector2 away = -Aim();
        int rest = wanted - picked;
        for (int n = 0; n < rest; n++)
        {
            float offset = (n - (rest - 1) * 0.5f) * (MathF.PI / 2f);
            float sin = MathF.Sin(offset);
            float cos = MathF.Cos(offset);
            _picks[picked++] = self + new Vector3(away.X * cos - away.Y * sin, 0f,
                away.X * sin + away.Y * cos) * BossBehaviour.AddRingRadius;
        }

        for (int i = 0; i < picked; i++)
        {
            _guardIds[GuardCount++] = ctx.Enemies.SpawnSummon(_guardId, _picks[i]).Id;
        }

        Move = WarlordMove.None;
        _recovering = true;
        _remaining = Recover;
        _warCryCooldown = WarCryCooldownSeconds;
    }

    private bool Spaced(Vector3 point, int picked)
    {
        for (int i = 0; i < picked; i++)
        {
            if (!(FlatSquared(point, _picks[i]) >= EdgeSpacing * EdgeSpacing))
            {
                return false;
            }
        }

        return true;
    }

    // A dead or despawned guard leaves the count on the tick it goes; registry ids are never reused.
    private void RollCall(EnemyRegistry registry)
    {
        int kept = 0;
        for (int i = 0; i < GuardCount; i++)
        {
            if (registry.TryGet(_guardIds[i], out EnemyAgent guard) && guard.IsAlive)
            {
                _guardIds[kept++] = _guardIds[i];
            }
        }

        GuardCount = kept;
    }

    private bool AnyGuardNear(EnemyRegistry registry)
    {
        for (int i = 0; i < GuardCount; i++)
        {
            if (registry.TryGet(_guardIds[i], out EnemyAgent guard)
                && FlatSquared(guard.Position, _agent.Blackboard.SelfPosition) <= LockRadius * LockRadius)
            {
                return true;
            }
        }

        return false;
    }

    public void ClearAdds(in EnemyTickContext ctx)
    {
        for (int i = 0; i < GuardCount; i++)
        {
            if (ctx.Enemies.Registry.TryGet(_guardIds[i], out EnemyAgent guard) && guard.IsAlive)
            {
                ctx.Enemies.DespawnAtEndOfTick(_guardIds[i]);
            }
        }

        GuardCount = 0;
        IsLocked = false;
        _warCryCooldown = WarCryFirstAtSeconds;
    }

    public void OnPhaseEntered(int phase)
    {
        // The armed move drops first; only then do the phase's changes apply.
        _coneEvents?.Publish(new SprayCleared(_agent.Id));
        _coneEvents = null;
        Move = WarlordMove.None;
        _recovering = false;
        _dashing = false;
        IsDazed = false;
        Phase = phase;
        if (phase >= 2 && !IsShattered)
        {
            IsShattered = true;
            _shatterPending = true;
            _agent.MoveSpeed.Add(new Modifier(ModifierKind.PercentMult, ShatteredSpeedBonus, this));
        }

        GuardHalfArcDeg = IsShattered ? 0f : HalfArcDeg;
        _remaining = Gap;
    }

    public void Reset()
    {
        _agent.MoveSpeed.RemoveAll(this);
        IsShattered = false;
        _shatterPending = false;
        _lobCooldown = 0f;
        OnPhaseEntered(0);
        _agent.Health.SetExternalInvulnerable(false);
        _agent.IsVulnerable = true;
        GuardHalfArcDeg = HalfArcDeg;
        Facing = Vector2.UnitY;
        _firstTick = true;
        _publishedArc = -1f;
        _chargeCooldown = 0f;
        _warCryCooldown = WarCryFirstAtSeconds;
        GuardCount = 0;
        IsLocked = false;
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
        float limit = (IsShattered ? ShatteredTurnRateDeg : TurnRateDeg) * (MathF.PI / 180f) * dt;
        float step = MathF.Max(-limit, MathF.Min(limit, angle));
        float sin = MathF.Sin(step);
        float cos = MathF.Cos(step);
        Facing = Vector2.Normalize(new Vector2(Facing.X * cos - Facing.Y * sin,
            Facing.X * sin + Facing.Y * cos));
    }

    private static float FlatSquared(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;
        return x * x + z * z;
    }

    private bool InArc(Vector2 facing, Vector2 direction, float degrees) => direction == Vector2.Zero
        || Vector2.Dot(facing, Vector2.Normalize(direction)) >= MathF.Cos(degrees * (MathF.PI / 180f));
}
