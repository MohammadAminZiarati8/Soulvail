using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public sealed class BuriedKingBehaviour : IEnemyBehaviour
{
    private enum State
    {
        Ready, SweepTell, CastTell, Outbound, Embedded, RecallTell, Returning, FractureTell, SlamTell,
        LungeApproach, LungeTell, Lunging, LungeRecovery, CutRecovery, Recovery, Dead,
        SpinTell, SpinActive, SpinRecovery
    }
    private readonly EnemyAgent _agent;
    private readonly Stat _sweepReach = new(4f);
    private readonly Stat _sweepDegrees = new(120f);
    private readonly Stat _sweepTell = new(0.85f);
    private readonly Stat _sweepCut = new(0.16f);
    private readonly Stat _sweepRecovery = new(1.4f);
    private readonly Stat _laneWidth = new(1.6f);
    private readonly Stat _landingRadius = new(2f);
    private readonly Stat _laneTell = new(1f);
    private readonly Stat _flightSpeed = new(18f);
    private readonly Stat _embeddedTime = new(1.6f);
    private readonly Stat _catchRecovery = new(2.1f);
    private readonly Stat _spinThreshold = new(0.66f);
    private readonly Stat _shatteredThreshold = new(0.33f);
    private readonly Stat _fractureTell = new(1f);
    private readonly Stat _slamTell = new(0.9f);
    private readonly Stat _slamCut = new(0.22f);
    private readonly Stat _slamRadius = new(3.5f);
    private readonly Stat _slamRecovery = new(2.4f);
    private readonly Stat _shatteredRecovery = new(2.7f);
    private readonly Stat _minimumCastLength = new(4f);
    private readonly Stat _landingSnap = new(1.5f);
    private readonly Stat _backOffTime = new(1.6f);
    private readonly Stat _sweepDamage = new(24f);
    private readonly Stat _outboundDamage = new(16f);
    private readonly Stat _returnDamage = new(20f);
    private readonly Stat _slamDamage = new(28f);
    private readonly Stat _lungeWindup = new(1f);
    private readonly BuriedKingLeapStats _leap = new();
    private readonly Stat _lungeRunSpeedMultiplier = new(1.6f);
    private readonly Stat _lungeTravel = new(0.55f);
    private readonly Stat _lungeCut = new(0.15f);
    private readonly Stat _lungeRecovery = new(0.9f);
    private readonly Stat _minimumLungeRange = new(4.1f);
    private readonly Stat _spentLungeCastRange = new(7.4f);
    private readonly Stat _lungeDamage = new(26f);
    private readonly Stat _lungeDeceleration = new(0.25f);
    private readonly Stat _lungePositionTolerance = new(0.08f);
    private readonly Stat _spinTell = new(1.25f);
    private readonly Stat _spinDuration = new(5f);
    private readonly Stat _spinRecovery = new(1.6f);
    private readonly Stat _spinSpeed = new(2f);
    private readonly Stat _spinTurnSpeed = new(180f);
    private readonly Stat _spinRadius = new(3.5f);
    private readonly Stat _spinDamage = new(10f);
    private readonly Stat _spinFirstPulse = new(0.75f);
    private readonly Stat _spinPulseInterval = new(1f);
    private readonly Stat _spinRepeatRange = new(5f);
    private const int AttacksBetweenSpins = 2;
    private State _state;
    private Vector2 _facing = Vector2.UnitY;
    private Vector3 _start;
    private Vector3 _end;
    private float _remaining;
    private float _distanceTravelled;
    private float _backOffElapsed;
    private int _sweeps;
    private bool _hit;
    private bool _fractureUnlocked;
    private bool _fractureOnReturn;
    private float _lungeClearance;
    private bool _lungeReady = true;
    private Vector3 _lungeExpectedPosition;
    private bool _spinUnlocked;
    private bool _spinDebuted;
    private double _spinElapsed;
    private float _nextSpinPulse;
    private int _attacksSinceSpin;

    public BuriedKingBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    public static ContentId PrototypeBodyId => new("enemy.buried_king_prototype");

    public void Tick(in EnemyTickContext ctx)
    {
        float hp = _agent.Health.Fraction;
        _spinUnlocked |= hp <= _spinThreshold.Value;
        _fractureUnlocked |= hp <= _shatteredThreshold.Value;

        EnemyBlackboard board = _agent.Blackboard;
        Vector3 hero = ctx.Player.Blackboard.PlayerPosition;
        Vector2 velocity = Vector2.Zero;
        switch (_state)
        {
            case State.Ready:
            case State.LungeApproach:
                velocity = Select(ctx, board, hero);
                break;
            case State.SweepTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveSweep(ctx, hero);
                }
                break;
            case State.CastTell:
            case State.RecallTell:
                if (Elapsed(ctx.Dt))
                {
                    StartFlight(ctx, returning: _state == State.RecallTell);
                }
                break;
            case State.Outbound:
            case State.Returning:
                Fly(ctx, hero);
                break;
            case State.Embedded:
                if (Elapsed(ctx.Dt))
                {
                    Lock(ctx, State.RecallTell, BuriedKingPose.RecallTell, _end, board.SelfPosition, _laneTell.Value);
                }
                break;
            case State.FractureTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveFracture(ctx, hero);
                }
                else
                {
                    Publish(ctx.Events, BuriedKingPose.FractureTell, fractureProgress: 1f - _remaining / _fractureTell.Value);
                }
                break;
            case State.SlamTell:
                if (Elapsed(ctx.Dt))
                {
                    ResolveSlam(ctx, hero);
                }
                break;
            case State.LungeTell:
                if (!AtLungePosition(board.SelfPosition, _start))
                {
                    RecoverInterruptedLunge(ctx);
                }
                else if (Elapsed(ctx.Dt))
                {
                    _state = State.Lunging;
                    _remaining = _lungeTravel.Value;
                    _lungeExpectedPosition = _start;
                    PublishLunge(ctx.Events, BuriedKingPose.LungeTravel, _remaining);
                }
                break;
            case State.Lunging:
                velocity = AdvanceLunge(ctx, board.SelfPosition, hero);
                break;
            case State.SpinTell:
                if (Elapsed(ctx.Dt))
                {
                    _state = State.SpinActive;
                    _spinElapsed = 0d;
                    _nextSpinPulse = _spinFirstPulse.Value;
                    PublishSpin(ctx.Events, BuriedKingPose.SpinActive, board.SelfPosition, _spinDuration.Value);
                }
                break;
            case State.SpinActive:
                velocity = AdvanceSpin(ctx, board, hero);
                break;
            case State.LungeRecovery:
            case State.CutRecovery:
            case State.SpinRecovery:
            case State.Recovery:
                if (Elapsed(ctx.Dt))
                {
                    if (_state != State.Recovery)
                    {
                        Publish(ctx.Events, BuriedKingPose.Held);
                    }

                    _state = State.Ready;
                    velocity = Select(ctx, board, hero);
                }
                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, new Vector3(velocity.X, 0f, velocity.Y), _facing));
    }

    public void Reset()
    {
        _state = State.Ready;
        _facing = Vector2.UnitY;
        _start = _end = Vector3.Zero;
        _remaining = _distanceTravelled = _backOffElapsed = 0f;
        _sweeps = 0;
        _hit = false;
        _fractureUnlocked = _fractureOnReturn = false;
        _lungeClearance = 0f;
        _lungeReady = true;
        _spinUnlocked = _spinDebuted = false;
        _spinElapsed = 0d;
        _nextSpinPulse = 0f;
        _attacksSinceSpin = 0;
    }

    internal void ObserveLungeClearance(float distance) =>
        _lungeClearance = float.IsFinite(distance) ? MathF.Min(distance, _leap.Length) : 0f;

    internal void ClearEncounter(IDomainEvents events)
    {
        if (_state == State.Dead)
        {
            return;
        }

        Reset();
        _state = State.Dead;
        Publish(events, BuriedKingPose.Cleared);
    }

    private Vector2 Select(in EnemyTickContext ctx, EnemyBlackboard board, Vector3 hero)
    {
        Vector2 toHero = Ground(hero - board.SelfPosition);
        float distance = toHero.Length();
        Face(toHero);
        bool close = distance <= _sweepReach.Value;
        if (_state == State.Ready && TryStartSpin(ctx, board, distance))
        {
            return Vector2.Zero;
        }

        if (_lungeReady && board.HasLineOfSight && distance >= _minimumLungeRange.Value
            && _lungeClearance >= _leap.Length)
        {
            float untilInRange = distance - _leap.Length;
            if (untilInRange > 0.00001f)
            {
                if (_state != State.LungeApproach)
                {
                    _state = State.LungeApproach;
                    Publish(ctx.Events, BuriedKingPose.LungeApproach);
                }

                float runSpeed = _agent.MoveSpeed.Value * _lungeRunSpeedMultiplier.Value;
                if (ctx.Dt > 0f)
                {
                    runSpeed = MathF.Min(runSpeed, untilInRange / ctx.Dt);
                }

                return Approach(board, runSpeed);
            }

            _lungeReady = false;
            _sweeps = 0;
            _backOffElapsed = 0f;
            _start = board.SelfPosition;
            Vector3 axis = new(_facing.X, 0f, _facing.Y);
            _end = _start + axis * _leap.Length;
            _state = State.LungeTell;
            _remaining = _lungeWindup.Value;
            PublishLunge(ctx.Events, BuriedKingPose.LungeTell, _remaining);
            return Vector2.Zero;
        }

        if (_state == State.LungeApproach)
        {
            _state = State.Ready;
            Publish(ctx.Events, BuriedKingPose.Held);
            if (TryStartSpin(ctx, board, distance))
            {
                return Vector2.Zero;
            }
        }

        // After a lunge, walk into sweep reach or cast if the player opens more distance.
        if (board.HasLineOfSight && !close && (_lungeReady || distance > _spentLungeCastRange.Value)
            && TryLanding(ctx, board.SelfPosition, hero, out Vector3 landing))
        {
            _lungeReady = true;
            _fractureOnReturn = _fractureUnlocked;
            _sweeps = 0;
            _backOffElapsed = 0f;
            Face(Ground(landing - board.SelfPosition));
            Lock(ctx, State.CastTell, BuriedKingPose.CastTell, board.SelfPosition, landing, _laneTell.Value);
        }
        else if (close && _sweeps >= 2 && _backOffElapsed < _backOffTime.Value)
        {
            _backOffElapsed += ctx.Dt;
            return -_facing * _agent.MoveSpeed.Value;
        }
        else if (close && board.HasLineOfSight)
        {
            if (_sweeps >= 2)
            {
                _sweeps = 0;
                _backOffElapsed = 0f;
            }

            _sweeps++;
            _lungeReady = true;
            Vector3 tip = board.SelfPosition + new Vector3(_facing.X, 0f, _facing.Y) * _sweepReach.Value;
            Lock(ctx, State.SweepTell, BuriedKingPose.SweepTell, board.SelfPosition, tip, _sweepTell.Value);
        }
        else
        {
            return Approach(board, _agent.MoveSpeed.Value);
        }

        return Vector2.Zero;
    }

    private bool TryStartSpin(in EnemyTickContext ctx, EnemyBlackboard board, float distance)
    {
        if (!_spinUnlocked || (_spinDebuted && (_attacksSinceSpin < AttacksBetweenSpins
            || !board.HasLineOfSight || distance > _spinRepeatRange.Value)))
        {
            return false;
        }

        _spinDebuted = true;
        _attacksSinceSpin = 0;
        _state = State.SpinTell;
        _remaining = _spinTell.Value;
        PublishSpin(ctx.Events, BuriedKingPose.SpinTell, board.SelfPosition, _remaining);
        return true;
    }

    private Vector2 AdvanceSpin(in EnemyTickContext ctx, EnemyBlackboard board, Vector3 hero)
    {
        _spinElapsed = Math.Min(_spinDuration.Value, _spinElapsed + ctx.Dt);
        if (_spinElapsed >= _nextSpinPulse && _nextSpinPulse < _spinDuration.Value)
        {
            // Skip every elapsed beat and sample once from this frame's observed centers.
            do
            {
                _nextSpinPulse += _spinPulseInterval.Value;
            }
            while (_nextSpinPulse <= _spinElapsed);

            float radius = _spinRadius.Value;
            if (Ground(hero - board.SelfPosition).LengthSquared() <= radius * radius)
            {
                ctx.Player.ApplyDamage(_spinDamage.Value, ctx.Now);
            }

            PublishSpin(ctx.Events, BuriedKingPose.SpinPulse, board.SelfPosition, 0f);
        }

        if (_spinElapsed >= _spinDuration.Value)
        {
            _state = State.SpinRecovery;
            _remaining = _spinRecovery.Value;
            PublishSpin(ctx.Events, BuriedKingPose.SpinRecovery, board.SelfPosition, _remaining);
            return Vector2.Zero;
        }

        Vector2 direction = board.PathDirectionToPlayer == Vector2.Zero ? board.DirectionToPlayer : board.PathDirectionToPlayer;
        if (direction == Vector2.Zero)
        {
            return Vector2.Zero;
        }

        _facing = Ground(PlayerMotor.RotateTowards(new Vector3(_facing.X, 0f, _facing.Y),
            Vector3.Normalize(new Vector3(direction.X, 0f, direction.Y)), _spinTurnSpeed.Value * ctx.Dt));
        return _facing * _spinSpeed.Value;
    }

    private void PublishSpin(IDomainEvents events, BuriedKingPose pose, Vector3 center, float duration) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, center, center, 0f, 0f,
            default, _spinRadius.Value, 0f, duration, 0f));

    private Vector2 AdvanceLunge(in EnemyTickContext ctx, Vector3 position, Vector3 hero)
    {
        if (!AtLungePosition(position, _lungeExpectedPosition))
        {
            RecoverInterruptedLunge(ctx);
            return Vector2.Zero;
        }

        if (_remaining <= 0f)
        {
            if (IsInLungeLane(hero))
            {
                ctx.Player.ApplyDamage(_lungeDamage.Value, ctx.Now);
            }

            StartSlam(ctx, position);
            return Vector2.Zero;
        }

        if (!(ctx.Dt > 0f))
        {
            return Vector2.Zero;
        }

        _remaining = MathF.Max(0f, _remaining - ctx.Dt);
        float progress = 1f - _remaining / _lungeTravel.Value;
        float glide = progress + _lungeDeceleration.Value * progress * (1f - progress);
        Vector3 next = Vector3.Lerp(_start, _end, glide);
        Vector2 velocity = Ground(next - position) / ctx.Dt;
        _lungeExpectedPosition = next;
        return velocity;
    }

    private bool AtLungePosition(Vector3 observed, Vector3 expected)
    {
        float tolerance = _lungePositionTolerance.Value;
        return Ground(observed - expected).LengthSquared() <= tolerance * tolerance;
    }

    private bool IsInLungeLane(Vector3 hero)
    {
        Vector2 axis = Vector2.Normalize(Ground(_end - _start));
        Vector2 fromStart = Ground(hero - _start);
        Vector2 fromEnd = Ground(hero - _end);
        float across = MathF.Abs(fromStart.X * axis.Y - fromStart.Y * axis.X);
        const float rounding = 0.00001f;
        return Vector2.Dot(fromStart, axis) >= -rounding && Vector2.Dot(fromEnd, axis) <= rounding
            && across <= _leap.Width * 0.5f + rounding;
    }

    private void RecoverInterruptedLunge(in EnemyTickContext ctx)
    {
        _state = State.LungeRecovery;
        _remaining = _lungeRecovery.Value;
        PublishLunge(ctx.Events, BuriedKingPose.LungeRecovery, _remaining);
    }

    private void PublishLunge(IDomainEvents events, BuriedKingPose pose, float duration) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, _start, _end, _leap.Width,
            0f, default, 0f, 0f, duration, 0f, pose == BuriedKingPose.LungeRecovery ? 0f : _lungeCut.Value));

    private bool TryLanding(in EnemyTickContext ctx, Vector3 origin, Vector3 hero, out Vector3 landing)
    {
        landing = default;
        float best = _landingSnap.Value * _landingSnap.Value;
        float minLengthSquared = _minimumCastLength.Value * _minimumCastLength.Value;
        bool found = false;
        for (int i = 0; i < ctx.OpenGround.Count; i++)
        {
            Vector3 point = ctx.OpenGround[i];
            float offset = Ground(point - hero).LengthSquared();
            if (offset <= best && Ground(point - origin).LengthSquared() >= minLengthSquared)
            {
                landing = point;
                best = offset;
                found = true;
            }
        }

        return found;
    }

    private void ResolveSweep(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 offset = Ground(hero - _start);
        float distance = offset.Length();
        float edge = MathF.Cos(_sweepDegrees.Value * (MathF.PI / 360f));
        if (distance <= _sweepReach.Value && Vector2.Dot(offset, _facing) >= distance * edge)
        {
            ctx.Player.ApplyDamage(_sweepDamage.Value, ctx.Now);
        }

        Recover(ctx, _sweepRecovery.Value, BuriedKingPose.SweepImpact);
    }

    private void StartFlight(in EnemyTickContext ctx, bool returning)
    {
        _state = returning ? State.Returning : State.Outbound;
        _distanceTravelled = 0f;
        _hit = false;
        Publish(ctx.Events, returning ? BuriedKingPose.Returning : BuriedKingPose.Outbound, _start);
    }

    private void Fly(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 lane = Ground(_end - _start);
        float length = lane.Length();
        bool returning = _state == State.Returning;
        float next = MathF.Min(length, _distanceTravelled + _flightSpeed.Value * ctx.Dt);
        if (!_hit && next > _distanceTravelled && IsInLane(hero, lane / length, _distanceTravelled, next))
        {
            _hit = true;
            ctx.Player.ApplyDamage(returning ? _returnDamage.Value : _outboundDamage.Value, ctx.Now);
        }

        _distanceTravelled = next;
        if (next < length)
        {
            Publish(ctx.Events, returning ? BuriedKingPose.Returning : BuriedKingPose.Outbound,
                Vector3.Lerp(_start, _end, next / length));
        }
        else if (returning)
        {
            if (_fractureOnReturn)
            {
                _state = State.FractureTell;
                _remaining = _fractureTell.Value;
                Publish(ctx.Events, BuriedKingPose.FractureTell, warningSeconds: _remaining);
            }
            else
            {
                Recover(ctx, _catchRecovery.Value);
            }
        }
        else
        {
            float radius = _landingRadius.Value;
            if (!_hit && Ground(hero - _end).LengthSquared() <= radius * radius)
            {
                ctx.Player.ApplyDamage(_outboundDamage.Value, ctx.Now);
            }

            _state = State.Embedded;
            _remaining = _embeddedTime.Value;
            Publish(ctx.Events, BuriedKingPose.Embedded, _end);
        }
    }

    private bool IsInLane(Vector3 hero, Vector2 axis, float from, float to) =>
        IsInRectangle(hero, _start, axis, from, to, _laneWidth.Value);

    private static bool IsInRectangle(Vector3 hero, Vector3 origin, Vector2 axis, float from, float to, float width)
    {
        Vector2 offset = Ground(hero - origin);
        float along = Vector2.Dot(offset, axis);
        float across = MathF.Abs(offset.X * axis.Y - offset.Y * axis.X);
        return along >= from && along <= to && across <= width * 0.5f;
    }

    private void ResolveFracture(in EnemyTickContext ctx, Vector3 hero)
    {
        Vector2 lane = Ground(_end - _start);
        float length = lane.Length();
        if (length > 0f && IsInLane(hero, lane / length, 0f, length))
        {
            ctx.Player.ApplyDamage(_returnDamage.Value, ctx.Now);
        }

        Recover(ctx, _shatteredRecovery.Value);
    }

    private void StartSlam(in EnemyTickContext ctx, Vector3 center) =>
        Lock(ctx, State.SlamTell, BuriedKingPose.SlamTell, center, center, _slamTell.Value);

    private void ResolveSlam(in EnemyTickContext ctx, Vector3 hero)
    {
        float radius = _slamRadius.Value;
        if (Ground(hero - _start).LengthSquared() <= radius * radius)
        {
            ctx.Player.ApplyDamage(_slamDamage.Value, ctx.Now);
        }

        Recover(ctx, _slamRecovery.Value, BuriedKingPose.SlamImpact);
    }

    private void Lock(in EnemyTickContext ctx, State state, BuriedKingPose pose, Vector3 start, Vector3 end, float duration)
    {
        _state = state;
        _start = start;
        _end = end;
        _remaining = duration;
        Publish(ctx.Events, pose, start, warningSeconds: duration);
    }

    private void Recover(in EnemyTickContext ctx, float duration, BuriedKingPose pose = BuriedKingPose.Held)
    {
        if (_spinDebuted && _attacksSinceSpin < AttacksBetweenSpins)
        {
            _attacksSinceSpin++;
        }

        _state = pose == BuriedKingPose.Held ? State.Recovery : State.CutRecovery;
        _remaining = duration;
        Publish(ctx.Events, pose);
    }

    private bool Elapsed(float dt)
    {
        _remaining -= dt;
        return _remaining <= 0f;
    }

    private Vector2 Approach(EnemyBlackboard board, float speed)
    {
        Vector2 direction = board.PathDirectionToPlayer == Vector2.Zero ? board.DirectionToPlayer : board.PathDirectionToPlayer;
        Face(direction);
        return direction * speed;
    }

    private void Face(Vector2 direction)
    {
        if (direction != Vector2.Zero)
        {
            _facing = Vector2.Normalize(direction);
        }
    }

    private void Publish(IDomainEvents events, BuriedKingPose pose, Vector3 weapon = default,
        float fractureProgress = 0f, float warningSeconds = 0f) =>
        events.Publish(new BuriedKingStateChanged(_agent.Id, pose, _start, _end, _laneWidth.Value, _sweepDegrees.Value,
            weapon, pose is BuriedKingPose.SlamTell or BuriedKingPose.SlamImpact ? _slamRadius.Value : 0f,
            fractureProgress, warningSeconds,
            pose is BuriedKingPose.CastTell or BuriedKingPose.Embedded ? _landingRadius.Value : 0f,
            pose switch
            {
                BuriedKingPose.SweepTell or BuriedKingPose.SweepImpact => _sweepCut.Value,
                BuriedKingPose.SlamTell or BuriedKingPose.SlamImpact => _slamCut.Value,
                _ => 0f,
            }));

    private static Vector2 Ground(Vector3 vector) => new(vector.X, vector.Z);
}
