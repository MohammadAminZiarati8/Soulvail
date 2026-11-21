using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Runs the ground combo while the boss wrapper handles phase beats.</summary>
public sealed class MatriarchBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private enum State { ShoveReady, Tell, PounceGap, Recovery }

    private const float FinalPhaseHealth = 0.33f;
    private readonly EnemyAgent _agent;
    private readonly MatriarchStats _stats;
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
            float turnRate = Math.Clamp(_stats.TurnDegreesPerSecond.Value, 0f, MatriarchStats.TurnLimit);
            _facing = PlayerMotor.RotateTowards(_facing, heading, turnRate * dt);
        }

        // Finish at most the state that was active at the start of this tick. A late
        // frame never spends its excess time on the newly published warning.
        _remaining = MathF.Max(0f, _remaining - dt);
        Vector3 velocity = Vector3.Zero;
        switch (_state)
        {
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
            case State.Tell:
                ctx.Events.Publish(new MatriarchTellProgress(_agent.Id, 1f - _remaining / _armed.Seconds));
                if (_remaining == 0f)
                {
                    velocity = Impact(ctx, origin, hero, dt);
                }
                break;
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, new Vector2(_facing.X, _facing.Z)));
    }

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _state = State.ShoveReady;
        _remaining = 0f;
        _secondPounce = false;
        _armed = default;
        ClearTell();
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _facing = Vector3.UnitZ;
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
