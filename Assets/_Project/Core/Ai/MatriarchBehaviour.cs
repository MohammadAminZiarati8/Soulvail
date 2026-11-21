using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Runs the ground combo and brood flight between the boss wrapper's phase beats.</summary>
public sealed class MatriarchBehaviour : IEnemyBehaviour, IBossPhaseListener, IBossAddOwner
{
    private enum State { ShoveReady, Tell, PounceGap, Recovery, BroodRetry, Flight }

    private const float FinalPhaseHealth = 0.33f;
    private const int BroodCap = 2;
    private readonly EnemyAgent _agent;
    private readonly MatriarchStats _stats;
    private readonly ContentId _vultureId;
    private readonly int[] _broodIds = new int[BroodCap];
    private EnemySystem _broodOwner;
    private IDomainEvents _flightEvents;
    private int _broodCount;
    private int _featherCount;
    private float _broodReadyAt;
    private float _featherReadyAt;
    private State _state;
    private int _phase;
    private bool _secondPounce;
    private float _remaining;
    private Vector3 _facing = Vector3.UnitZ;
    private MatriarchArmed _armed;
    private IDomainEvents _tellEvents;

    public MatriarchBehaviour(EnemyAgent agent, MatriarchStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public MatriarchBehaviour(EnemyAgent agent, MatriarchStats stats, ContentId vultureId)
        : this(agent, stats)
    {
        if (vultureId.Value is null)
        {
            throw new ArgumentException("A vulture content ID is required.", nameof(vultureId));
        }

        _vultureId = vultureId;
    }

    internal bool IsAirborne => _flightEvents != null;

    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Intents is null)
        {
            throw new ArgumentException("An initialized tick context is required.", nameof(ctx));
        }

        float dt = MathF.Max(0f, ctx.Dt);
        Vector3 origin = _agent.Position;
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 hero = board.QuarryIsADecoy || board.QuarryIsAPet
            ? ctx.Player.Blackboard.PlayerPosition
            : board.PlayerPosition;
        Vector3 aim = GroundDelta(hero, origin);
        float distanceSquared = aim.LengthSquared();
        bool canAim = float.IsFinite(distanceSquared) && float.IsFinite(hero.Y) && float.IsFinite(origin.Y);
        Vector3 heading = _facing;
        if (canAim && distanceSquared > 0f)
        {
            heading = aim / MathF.Sqrt(distanceSquared);
            if (!IsAirborne)
            {
                float turnRate = Math.Clamp(_stats.TurnDegreesPerSecond.Value, 0f, MatriarchStats.TurnLimit);
                _facing = PlayerMotor.RotateTowards(_facing, heading, turnRate * dt);
            }
        }

        // Finish at most the state that was active at the start of this tick. A late
        // frame never spends its excess time on the newly published warning.
        _remaining = MathF.Max(0f, _remaining - dt);
        Vector3 velocity = Vector3.Zero;
        Vector3? landingPosition = null;
        if (IsAirborne)
        {
            velocity = TickFlight(ctx, origin, hero, canAim, dt, out landingPosition);
        }
        else
        {
            switch (_state)
            {
                case State.BroodRetry or State.ShoveReady or State.Recovery
                    when _remaining == 0f && canAim && _phase > 0 && _vultureId.Value != null
                        && ctx.Now >= _broodReadyAt:
                    CallBrood(ctx, hero);
                    break;
                case State.ShoveReady or State.Recovery when _remaining == 0f && canAim:
                    _secondPounce = false;
                    Arm(ctx, new MatriarchArmed(_agent.Id, MatriarchMove.RuinShove, origin,
                        origin + heading * _stats.ShoveLength.Value, _stats.ShoveWidth.Value, 0f, _stats.ShoveTell.Value));
                    break;
                case State.PounceGap when _remaining == 0f && canAim:
                    Vector3 landing = GravemawBehaviour.FindOpenSpot(ctx.OpenGround, hero, origin, float.PositiveInfinity);
                    Arm(ctx, new MatriarchArmed(_agent.Id, MatriarchMove.CarrionPounce, origin, landing,
                        0f, _stats.PounceRadius.Value, _stats.PounceTell.Value));
                    break;
                case State.Tell when AdvanceTell(ctx):
                    velocity = Impact(ctx, origin, hero, dt);
                    break;
            }
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, new Vector2(_facing.X, _facing.Z),
            landingPosition));
    }

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        if (!IsAirborne)
        {
            ResetGroundAttack();
        }
    }

    private void ResetGroundAttack()
    {
        _state = State.ShoveReady;
        _remaining = 0f;
        _secondPounce = false;
        _armed = default;
        ClearTell();
    }

    public void Reset()
    {
        ClearEncounter();
        _phase = 0;
        _broodReadyAt = 0f;
        _facing = Vector3.UnitZ;
    }

    public void ClearAdds(in EnemyTickContext ctx)
    {
        // The wrapper also calls this at a phase beat. The grounded cooldown survives the beat.
        ClearEncounter();
    }

    internal void ClearEncounter()
    {
        for (int i = 0; i < _broodCount; i++)
        {
            int id = _broodIds[i];
            if (_broodOwner.Registry.TryGet(id, out EnemyAgent add) && add.IsAlive)
            {
                _broodOwner.DespawnAtEndOfTick(id);
            }

            _broodIds[i] = 0;
        }

        _broodCount = 0;
        _broodOwner = null;
        _featherCount = 0;
        _featherReadyAt = 0f;
        ResetGroundAttack();
        Land();
    }

    private void CallBrood(in EnemyTickContext ctx, Vector3 hero)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        while (_broodCount < BroodCap && registry.AliveCount < registry.Capacity
            && TryFindBroodSpot(ctx, hero, out Vector3 spot))
        {
            EnemyAgent add = ctx.Enemies.SpawnSummon(_vultureId, spot);
            if (add.IsAlive)
            {
                _broodIds[_broodCount++] = add.Id;
            }
            else
            {
                break;
            }
        }

        if (_broodCount == 0)
        {
            _broodReadyAt = ctx.Now + _stats.BroodRetry.Value;
            _state = State.BroodRetry;
            return;
        }

        _broodOwner = ctx.Enemies;
        _flightEvents = ctx.Events;
        _state = State.Flight;
        _featherCount = 0;
        ScheduleFeather(ctx.Now);
        _agent.IsHidden = true;
        _agent.IsVulnerable = false;
        _agent.Health.SetExternalInvulnerable(true);
        ctx.Events.Publish(new MatriarchFlightChanged(_agent.Id, true));
    }

    private bool TryFindBroodSpot(in EnemyTickContext ctx, Vector3 hero, out Vector3 spot)
    {
        spot = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < ctx.OpenGround.Count; i++)
        {
            Vector3 candidate = ctx.OpenGround[i];
            float distance = GroundDelta(candidate, _agent.Position).LengthSquared();
            if (distance < nearest && float.IsFinite(candidate.Y)
                && IsBroodSpotClear(ctx.Enemies.Registry, candidate, hero))
            {
                nearest = distance;
                spot = candidate;
            }
        }

        return float.IsFinite(nearest);
    }

    private bool IsBroodSpotClear(EnemyRegistry registry, Vector3 spot, Vector3 hero)
    {
        float playerClearance = OpenGroundPicker.PlayerClearance;
        if (!(GroundDelta(spot, hero).LengthSquared() >= playerClearance * playerClearance))
        {
            return false;
        }

        float enemyClearance = OpenGroundPicker.EnemyClearance;
        foreach (EnemyAgent enemy in registry.Alive)
        {
            if (enemy.IsAlive && !(GroundDelta(spot, enemy.Position).LengthSquared() >= enemyClearance * enemyClearance))
            {
                return false;
            }
        }

        return true;
    }

    private Vector3 TickFlight(in EnemyTickContext ctx, Vector3 origin, Vector3 hero, bool canAim,
        float dt, out Vector3? landingPosition)
    {
        landingPosition = null;
        if (_state == State.Tell && _armed.Move == MatriarchMove.CarrionDive)
        {
            if (AdvanceTell(ctx))
            {
                landingPosition = DiveImpact(ctx, hero);
            }

            return Vector3.Zero;
        }

        if (!HasLivingBrood())
        {
            ArmDive(ctx, origin, hero, canAim);
            return Vector3.Zero;
        }

        if (_state == State.Tell)
        {
            if (_remaining == 0f)
            {
                return Vector3.Zero;
            }

            AdvanceTell(ctx);
        }
        else if (ctx.Now >= _featherReadyAt && canAim)
        {
            Arm(ctx, new MatriarchArmed(_agent.Id, MatriarchMove.FeatherRain, origin, hero,
                0f, _stats.FeatherRadius.Value, _stats.FeatherTell.Value));
            _featherCount++;
            ScheduleFeather(ctx.Now);
        }

        float radius = _stats.OrbitRadius.Value;
        float approachSpeed = _stats.OrbitApproachSpeed.Value;
        return canAim && radius > 0f && approachSpeed > 0f
            ? VultureBehaviour.Orbit(origin, hero, _agent.Id, dt, radius, approachSpeed, _stats.OrbitSpeed.Value,
                ref _facing)
            : Vector3.Zero;
    }

    internal void ResolveRain(in EnemyTickContext ctx, Vector3 hero)
    {
        if (!IsAirborne || _state != State.Tell || _armed.Move != MatriarchMove.FeatherRain)
        {
            return;
        }

        // Hero projectiles and pets can kill the last vulture after enemy movement was decided.
        if (!HasLivingBrood())
        {
            Vector3 origin = _agent.Position;
            bool canAim = float.IsFinite(GroundDelta(hero, origin).LengthSquared())
                && float.IsFinite(hero.Y) && float.IsFinite(origin.Y);
            ArmDive(ctx, origin, hero, canAim);
            return;
        }

        if (_remaining > 0f)
        {
            return;
        }

        AdvanceTell(ctx);
        Vector3 target = _armed.Target;
        _state = State.Flight;
        if (GroundDelta(hero, target).LengthSquared() <= _armed.Radius * _armed.Radius)
        {
            ctx.Player.ApplyDamage(_stats.FeatherDamage.Value, ctx.Now);
        }

        ctx.Events.Publish(new MatriarchImpact(_agent.Id, MatriarchMove.FeatherRain, target));
        ClearTell();
    }

    private void ArmDive(in EnemyTickContext ctx, Vector3 origin, Vector3 hero, bool canAim)
    {
        ClearTell();
        _broodOwner = null;
        Array.Clear(_broodIds, 0, _broodCount);
        _broodCount = 0;
        Vector3 landing = canAim && ctx.OpenGround.Count > 0
            ? GravemawBehaviour.FindOpenSpot(ctx.OpenGround, hero, origin, float.PositiveInfinity)
            : origin;
        Vector3 travel = GroundDelta(landing, origin);
        if (travel.LengthSquared() > 0f)
        {
            _facing = Vector3.Normalize(travel);
        }

        Arm(ctx, new MatriarchArmed(_agent.Id, MatriarchMove.CarrionDive, origin, landing,
            0f, _stats.DiveRadius.Value, _stats.DiveTell.Value));
    }

    private bool HasLivingBrood()
    {
        for (int i = 0; i < _broodCount; i++)
        {
            if (_broodOwner.Registry.TryGet(_broodIds[i], out EnemyAgent add) && add.IsAlive)
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleFeather(float now)
    {
        // The vultures cycle through three ID-offset intervals. Centre the rain's cycle on its base interval.
        int offset = (int)(((long)_featherCount + _agent.Id) % 3) - 1;
        _featherReadyAt = now + _stats.FeatherInterval.Value + offset * _stats.FeatherStagger.Value;
    }

    private bool AdvanceTell(in EnemyTickContext ctx)
    {
        ctx.Events.Publish(new MatriarchTellProgress(_agent.Id, 1f - _remaining / _armed.Seconds));
        return _remaining == 0f;
    }

    private Vector3 DiveImpact(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector3 target = _armed.Target;
        _state = State.Recovery;
        _remaining = _stats.DiveRecovery.Value;
        _broodReadyAt = ctx.Now + _stats.BroodCooldown.Value;
        Land();
        if (GroundDelta(hero, target).LengthSquared() <= _armed.Radius * _armed.Radius)
        {
            ctx.Player.ApplyDamage(_stats.DiveDamage.Value, ctx.Now);
        }

        ctx.Events.Publish(new MatriarchImpact(_agent.Id, MatriarchMove.CarrionDive, target));
        ClearTell();
        return target;
    }

    private void Land()
    {
        IDomainEvents events = _flightEvents;
        if (events is null)
        {
            return;
        }

        _flightEvents = null;
        _agent.IsHidden = false;
        _agent.IsVulnerable = true;
        _agent.Health.SetExternalInvulnerable(false);
        events.Publish(new MatriarchFlightChanged(_agent.Id, false));
    }

    private void Arm(in EnemyTickContext ctx, MatriarchArmed attack)
    {
        if (!(attack.Seconds > 0f) || !float.IsFinite(attack.Seconds))
        {
            throw new InvalidOperationException("Matriarch tell duration must be positive and finite.");
        }

        _armed = attack;
        _remaining = attack.Seconds;
        _state = State.Tell;
        // Phase entry and reset can interrupt the tell without a tick context.
        _tellEvents = ctx.Events;
        ctx.Events.Publish(attack);
        ctx.Events.Publish(new MatriarchTellProgress(_agent.Id, 0f));
    }

    private Vector3 Impact(in EnemyTickContext ctx, Vector3 origin, Vector3 hero, float dt)
    {
        bool shove = _armed.Move == MatriarchMove.RuinShove;
        bool hasFollowUp = shove || (_phase >= 2 && !_secondPounce && _agent.Health.Fraction < FinalPhaseHealth);
        _secondPounce = !shove && hasFollowUp;
        _state = hasFollowUp ? State.PounceGap : State.Recovery;
        _remaining = (hasFollowUp ? _stats.ComboGap : _stats.GroundRecovery).Value;

        Vector3 target = _armed.Target;
        if (shove ? InShoveLane(hero) : GroundDelta(hero, target).LengthSquared() <= _armed.Radius * _armed.Radius)
        {
            ctx.Player.ApplyDamage((shove ? _stats.ShoveDamage : _stats.PounceDamage).Value, ctx.Now);
        }

        ctx.Events.Publish(new MatriarchImpact(_agent.Id, _armed.Move, target));
        ClearTell();
        return shove ? Vector3.Zero : GroundDelta(target, origin) / dt;
    }

    private void ClearTell()
    {
        IDomainEvents events = _tellEvents;
        _tellEvents = null;
        events?.Publish(new MatriarchTellCleared(_agent.Id));
    }

    private bool InShoveLane(Vector3 hero)
    {
        Vector3 lane = GroundDelta(_armed.Target, _armed.Origin);
        float length = lane.Length();
        if (!(length > 0f))
        {
            return false;
        }

        Vector3 heading = lane / length;
        Vector3 offset = GroundDelta(hero, _armed.Origin);
        float along = Vector3.Dot(heading, offset);
        float across = heading.Z * offset.X - heading.X * offset.Z;
        return along >= 0f && along <= length && MathF.Abs(across) <= _armed.Width * 0.5f;
    }

    private static Vector3 GroundDelta(Vector3 to, Vector3 from) => new(to.X - from.X, 0f, to.Z - from.Z);
}
