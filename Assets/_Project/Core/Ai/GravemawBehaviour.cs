using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Where Gravemaw is in its burrow cycle (JG-25a rule 1).</summary>
public enum GravemawState
{
    /// <summary>Underground, walking at the quarry.</summary>
    Chase,

    /// <summary>The ring is warning; underground, tunnelling to its centre.</summary>
    Tunnel,

    /// <summary>Erupted and standing; the one state it can be hurt in.</summary>
    Surfaced,

    /// <summary>Standing, untargetable again, before the next chase.</summary>
    Dive,
}

/// <summary>
/// Stage 25's boss (JG-25a): it burrows and chases, fixes a ring on where the quarry will be a
/// second from now, tunnels there and erupts, then stands surfaced and hurtable for a window.
/// </summary>
/// <remarks>
/// <para>
/// The prediction samples <c>Blackboard.PlayerPosition</c> against <c>ctx.Now</c>, not the ticked
/// <c>Dt</c>: a stunned or beat-held boss is not ticked, and dividing the quarry's travel over a
/// stun by one frame's time would read a sprint. The estimate restarts when the quarry turns into
/// a decoy or back, so the jump between the two is never read as speed.
/// </para>
/// <para>
/// The guard is written every tick rather than on transitions: <c>BossBehaviour</c> lowers it at
/// the end of a phase beat, and the first tick after the beat must raise it again.
/// </para>
/// </remarks>
public sealed class GravemawBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    /// <summary>The eruption's <c>EnemyTelegraph</c> kind; JG-25c's tremor is 4 and spit 5.</summary>
    public const int EruptionMove = 3;

    /// <summary>The tunnel stops this close to the ring's centre.</summary>
    public const float ArriveDistance = 0.3f;

    private readonly EnemyAgent _agent;
    private readonly GravemawStats _stats;

    // JG-25c draws the spit's spread from it; held from the start so the session row never changes.
    private readonly IRandomStream _random;

    private float _elapsed;
    private bool _announceBurrow = true;
    private bool _announceClose;
    private bool _hasSample;
    private bool _sampledDecoy;
    private Vector3 _sample;
    private float _sampledAt;
    private Vector2 _velocity;

    public GravemawBehaviour(EnemyAgent agent, GravemawStats stats, IRandomStream random)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public GravemawState State { get; private set; }

    /// <summary>The armed ring's centre; meaningful from <see cref="GravemawState.Tunnel"/> on.</summary>
    public Vector3 RingCentre { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        Sample(ctx.Now);

        if (_announceClose)
        {
            _announceClose = false;
            ctx.Events.Publish(new GravemawWindow(_agent.Id, false, 0f));
        }

        if (_announceBurrow)
        {
            _announceBurrow = false;
            ctx.Events.Publish(new GravemawBurrowed(_agent.Id));
        }

        // One transition per tick, on the cycle's own ticked clock (rule 1).
        _elapsed += ctx.Dt;
        Vector3 velocity = Vector3.Zero;
        Vector2 facing = _agent.Blackboard.DirectionToPlayer;

        switch (State)
        {
            case GravemawState.Chase:
                velocity = Chase(ctx);
                break;

            case GravemawState.Tunnel:
                if (_elapsed >= _stats.WarnSeconds.Value)
                {
                    Erupt(ctx);
                    break;
                }

                velocity = Tunnel(ref facing);
                break;

            case GravemawState.Surfaced:
                if (_elapsed >= _stats.WindowSeconds.Value)
                {
                    State = GravemawState.Dive;
                    _elapsed = 0f;
                    ctx.Events.Publish(new GravemawWindow(_agent.Id, false, 0f));
                }

                break;

            default:
                if (_elapsed >= _stats.DiveSeconds.Value)
                {
                    State = GravemawState.Chase;
                    _elapsed = 0f;
                    ctx.Events.Publish(new GravemawBurrowed(_agent.Id));
                }

                break;
        }

        Guard(State != GravemawState.Surfaced);
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facing));
    }

    /// <summary>The beat drops the ring and sends it back underground to a fresh chase (rule 10).</summary>
    public void OnPhaseEntered(int phase)
    {
        _announceClose |= State == GravemawState.Surfaced;
        State = GravemawState.Chase;
        _elapsed = 0f;
        _announceBurrow = true;
    }

    public void Reset()
    {
        Guard(false);
        State = GravemawState.Chase;
        RingCentre = Vector3.Zero;
        _elapsed = 0f;
        _announceBurrow = true;
        _announceClose = false;
        _hasSample = false;
        _velocity = Vector2.Zero;
    }

    // Rule 3: walk at the quarry; the ring arms once the chase is long enough and the predicted
    // spot is within what the tunnel covers in the warning.
    private Vector3 Chase(in EnemyTickContext ctx)
    {
        float speed = _agent.MoveSpeed.Value;
        Vector2 toQuarry = _agent.Blackboard.DirectionToPlayer;

        if (_elapsed >= _stats.ChaseSeconds.Value)
        {
            Vector3 predicted = Predicted();
            float reach = speed * _stats.TunnelSpeedScale.Value * _stats.WarnSeconds.Value;

            if (!(FlatDistanceSquared(_agent.Position, predicted) > reach * reach))
            {
                Arm(ctx, predicted);
                return Vector3.Zero;
            }
        }

        return new Vector3(toQuarry.X * speed, 0f, toQuarry.Y * speed);
    }

    // Rule 5: the centre is fixed here and nothing moves it.
    private void Arm(in EnemyTickContext ctx, Vector3 centre)
    {
        State = GravemawState.Tunnel;
        RingCentre = centre;
        _elapsed = 0f;

        float warn = _stats.WarnSeconds.Value;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, warn, EruptionMove));
        ctx.Events.Publish(new GravemawRingArmed(_agent.Id, centre, _stats.RingRadius.Value, warn));
    }

    // Rule 6: a root's gate takes this velocity away, so a rooted tunnel stops short (rule 9).
    private Vector3 Tunnel(ref Vector2 facing)
    {
        float x = RingCentre.X - _agent.Position.X;
        float z = RingCentre.Z - _agent.Position.Z;
        float distance = MathF.Sqrt((x * x) + (z * z));

        if (!(distance > ArriveDistance))
        {
            return Vector3.Zero;
        }

        facing = new Vector2(x / distance, z / distance);
        float speed = _agent.MoveSpeed.Value * _stats.TunnelSpeedScale.Value;

        return new Vector3(facing.X * speed, 0f, facing.Y * speed);
    }

    // Rule 7: the ring bites at its centre wherever the body got to; a decoy draws it but takes nothing.
    private void Erupt(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        float radius = _stats.RingRadius.Value;
        bool hit = false;

        if (!board.QuarryIsADecoy
            && !(FlatDistanceSquared(board.PlayerPosition, RingCentre) > radius * radius))
        {
            DamageResult bite = ctx.Player.ApplyDamage(
                _agent.ContactDamage.Value * _stats.RingDamageScale.Value, ctx.Now);
            hit = !bite.Blocked;
        }

        ctx.Events.Publish(new GravemawErupted(_agent.Id, RingCentre, radius, hit));

        State = GravemawState.Surfaced;
        _elapsed = 0f;
        ctx.Events.Publish(new GravemawWindow(_agent.Id, true, _stats.WindowSeconds.Value));
    }

    // Rule 4: an exponentially smoothed velocity with PredictWindowSeconds as its time constant.
    private void Sample(float now)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 at = board.PlayerPosition;

        if (!_hasSample || board.QuarryIsADecoy != _sampledDecoy)
        {
            _hasSample = true;
            _sampledDecoy = board.QuarryIsADecoy;
            _sample = at;
            _sampledAt = now;
            _velocity = Vector2.Zero;
            return;
        }

        float elapsed = now - _sampledAt;
        if (!(elapsed > 0f))
        {
            return;
        }

        var measured = new Vector2((at.X - _sample.X) / elapsed, (at.Z - _sample.Z) / elapsed);
        float window = _stats.PredictWindowSeconds.Value;
        float blend = window > 0f ? 1f - MathF.Exp(-elapsed / window) : 1f;
        _velocity += (measured - _velocity) * blend;
        _sample = at;
        _sampledAt = now;
    }

    private Vector3 Predicted()
    {
        Vector3 quarry = _agent.Blackboard.PlayerPosition;
        Vector2 lead = _velocity * _stats.LeadSeconds.Value;
        float max = _stats.MaxLead.Value;
        float length = lead.Length();

        if (length > max)
        {
            lead *= max > 0f ? max / length : 0f;
        }

        return new Vector3(quarry.X + lead.X, quarry.Y, quarry.Z + lead.Y);
    }

    private void Guard(bool burrowed)
    {
        _agent.IsVulnerable = !burrowed;
        _agent.Health.SetExternalInvulnerable(burrowed);
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;

        return (x * x) + (z * z);
    }
}
