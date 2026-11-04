using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
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

/// <summary>The one move a surfaced window runs (JG-25c rule 1).</summary>
public enum GravemawMove
{
    /// <summary>A shockwave ring from the eruption's centre; the roll answers it.</summary>
    Tremor,

    /// <summary>A volley of rocks lobbed at the quarry and across its line.</summary>
    Spit,
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
public sealed class GravemawBehaviour : IEnemyBehaviour, IBossPhaseListener, IBossAddOwner
{
    /// <summary>The eruption's <c>EnemyTelegraph</c> kind.</summary>
    public const int EruptionMove = 3;

    /// <summary>The tremor's <c>EnemyTelegraph</c> kind (JG-25c).</summary>
    public const int TremorMove = 4;

    /// <summary>The spit's <c>EnemyTelegraph</c> kind (JG-25c).</summary>
    public const int SpitMove = 5;

    /// <summary>The tunnel stops this close to the ring's centre.</summary>
    public const float ArriveDistance = 0.3f;

    /// <summary>A player this close to the ring's centre is thrown against their facing (JG-25b rule 2).</summary>
    public const float ThrowCentreEpsilon = 0.01f;

    /// <summary>The most adds and opening cracks it can track; <c>MaxAdds</c> is clamped to it (JG-25d).</summary>
    public const int AddCapacity = 8;

    /// <summary>Ring B's floor: a second warning shorter than this cannot be read.</summary>
    public const float MinSecondWarnSeconds = 0.6f;

    private readonly EnemyAgent _agent;
    private readonly GravemawStats _stats;

    // Held from the start so the session row never changes when a later move draws from it.
    private readonly IRandomStream _random;

    private readonly ShockwaveSystem _shockwaves;

    private float _elapsed;
    private bool _announceBurrow = true;
    private bool _announceClose;
    private bool _hasSample;
    private bool _sampledDecoy;
    private Vector3 _sample;
    private float _sampledAt;
    private Vector2 _velocity;

    // JG-25c: this window's move, whether it is armed and when, and whether it is spent or dropped.
    private GravemawMove _windowMove;
    private bool _moveArmed;
    private bool _moveSpent = true;
    private float _armedAt;

    // The thrown player's; held for the throw so no stick velocity argues with it (JG-25b rule 4).
    private readonly PlayerMotor _motor;

    // JG-25d: the armed ring's warning, and where a phase-2 cycle is in its double surface.
    private float _warn;
    private bool _rising;
    private bool _ringIsSecond;

    // JG-25d rules 4-6: the adds that crawled out, and the cracks still opening.
    private readonly ContentId _addId;
    private readonly int[] _addIds = new int[AddCapacity];
    private readonly Vector3[] _crackAt = new Vector3[AddCapacity];
    private readonly float[] _crackLeft = new float[AddCapacity];
    private int _liveCount;
    private int _crackCount;

    public GravemawBehaviour(EnemyAgent agent, GravemawStats stats, IRandomStream random, PlayerMotor motor,
        ShockwaveSystem shockwaves, ContentId addId)
    {
        _addId = addId;
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _motor = motor ?? throw new ArgumentNullException(nameof(motor));
        _shockwaves = shockwaves ?? throw new ArgumentNullException(nameof(shockwaves));
    }

    public GravemawState State { get; private set; }

    /// <summary>The armed ring's centre; meaningful from <see cref="GravemawState.Tunnel"/> on.</summary>
    public Vector3 RingCentre { get; private set; }

    /// <summary>The move the next window will run (JG-25c rule 1).</summary>
    public GravemawMove NextMove { get; private set; }

    /// <summary>The boss phase it was last told of; 1 is the 50 % crossing (JG-25d rule 1).</summary>
    public int Phase { get; private set; }

    /// <summary>Live adds plus cracks still opening (JG-25d rule 4).</summary>
    public int AddCount => _liveCount + _crackCount;

    public void Tick(in EnemyTickContext ctx)
    {
        RollCall(ctx.Enemies.Registry);
        Cracks(ctx);

        // JG-25d rule 2: the throw is not the quarry's own speed, so B's estimate starts after it.
        if (_rising && _elapsed < _stats.ThrowSeconds.Value)
        {
            _hasSample = false;
        }

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
                if (_elapsed >= _warn)
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
                    DropMove();
                    ctx.Events.Publish(new GravemawWindow(_agent.Id, false, 0f));
                    break;
                }

                Move(ctx);
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
        Phase = phase;
        _rising = false;
        _ringIsSecond = false;
        _announceClose |= State == GravemawState.Surfaced;
        State = GravemawState.Chase;
        _elapsed = 0f;
        _announceBurrow = true;
        DropMove();
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
        NextMove = GravemawMove.Tremor;
        DropMove();
        Phase = 0;
        _warn = 0f;
        _rising = false;
        _ringIsSecond = false;
        _liveCount = 0;
        _crackCount = 0;
    }

    /// <summary>The beat takes every add with it and closes every crack still opening (JG-25d rule 6).</summary>
    public void ClearAdds(in EnemyTickContext ctx)
    {
        for (int i = 0; i < _liveCount; i++)
        {
            if (ctx.Enemies.Registry.TryGet(_addIds[i], out EnemyAgent add) && add.IsAlive)
            {
                ctx.Enemies.DespawnAtEndOfTick(_addIds[i]);
            }
        }

        _liveCount = 0;
        _crackCount = 0;
    }

    // Rule 3: walk at the quarry; the ring arms once the chase is long enough and the predicted
    // spot is within what the tunnel covers in the warning.
    private Vector3 Chase(in EnemyTickContext ctx)
    {
        // JG-25d rule 2: between A and B it lies still, then rings B with no reach check.
        if (_rising)
        {
            if (_elapsed >= _stats.DoubleGapSeconds.Value)
            {
                _rising = false;
                _ringIsSecond = true;
                Arm(ctx, Predicted(), MathF.Max(MinSecondWarnSeconds, _stats.SecondWarnSeconds.Value));
            }

            return Vector3.Zero;
        }

        float speed = _agent.MoveSpeed.Value;
        Vector2 toQuarry = _agent.Blackboard.DirectionToPlayer;
        float chase = Phase >= 1 ? _stats.PhaseTwoChaseSeconds.Value : _stats.ChaseSeconds.Value;

        if (_elapsed >= chase)
        {
            Vector3 predicted = Predicted();
            float reach = speed * _stats.TunnelSpeedScale.Value * _stats.WarnSeconds.Value;

            if (!(FlatDistanceSquared(_agent.Position, predicted) > reach * reach))
            {
                Arm(ctx, predicted, _stats.WarnSeconds.Value);
                return Vector3.Zero;
            }
        }

        return new Vector3(toQuarry.X * speed, 0f, toQuarry.Y * speed);
    }

    // Rule 5: the centre is fixed here and nothing moves it. JG-25h: it is the open spot nearest
    // the aim that the tunnel reaches in the warning, so the body never rises inside cover.
    private void Arm(in EnemyTickContext ctx, Vector3 aim, float warn)
    {
        float reach = _agent.MoveSpeed.Value * _stats.TunnelSpeedScale.Value * warn;
        Vector3 centre = OpenSpot(ctx.OpenGround, aim, reach);
        State = GravemawState.Tunnel;
        RingCentre = centre;
        _elapsed = 0f;
        _warn = warn;

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

            if (hit && !ctx.Player.Health.IsDead)
            {
                Throw(ctx, board.PlayerPosition, radius);
            }
        }

        ctx.Events.Publish(new GravemawErupted(_agent.Id, RingCentre, radius, hit));

        if (Phase >= 1)
        {
            OpenCracks(ctx);

            // JG-25d rule 2: A goes straight back under; only B opens the window and its one move.
            if (!_ringIsSecond)
            {
                State = GravemawState.Chase;
                _elapsed = 0f;
                _rising = true;
                ctx.Events.Publish(new GravemawBurrowed(_agent.Id));
                return;
            }

            _ringIsSecond = false;
        }

        State = GravemawState.Surfaced;
        _elapsed = 0f;
        _windowMove = NextMove;
        NextMove = NextMove == GravemawMove.Tremor ? GravemawMove.Spit : GravemawMove.Tremor;
        _moveArmed = false;
        _moveSpent = false;
        ctx.Events.Publish(new GravemawWindow(_agent.Id, true, _stats.WindowSeconds.Value));
    }

    // JG-25c rules 1, 2, 4: one move a window, armed MoveStartSeconds in and released one
    // telegraph later, each on its own tick.
    private void Move(in EnemyTickContext ctx)
    {
        if (_moveSpent)
        {
            return;
        }

        float telegraph = _stats.MoveTelegraphSeconds.Value;

        if (!_moveArmed)
        {
            if (_elapsed >= _stats.MoveStartSeconds.Value)
            {
                _moveArmed = true;
                _armedAt = _elapsed;
                int kind = _windowMove == GravemawMove.Tremor ? TremorMove : SpitMove;
                ctx.Events.Publish(new EnemyTelegraph(_agent.Id, telegraph, kind));
            }

            return;
        }

        if (_elapsed - _armedAt < telegraph)
        {
            return;
        }

        DropMove();

        if (_windowMove == GravemawMove.Tremor)
        {
            Tremor(ctx);
        }
        else
        {
            Spit(ctx);
        }
    }

    // Rule 2: a full system drops the ring (ShockwaveSystem rule 8); a non-positive number skips it.
    private void Tremor(in EnemyTickContext ctx)
    {
        float speed = _stats.TremorSpeed.Value;
        float reach = _stats.TremorMaxRadius.Value;
        float thickness = _stats.TremorThickness.Value;
        float damage = _agent.ContactDamage.Value * _stats.TremorDamageScale.Value;

        if (!(speed > 0f) || !(reach > 0f) || !(thickness > 0f) || !(damage > 0f))
        {
            return;
        }

        _shockwaves.Emit(RingCentre, speed, reach, damage, thickness, ctx.Now);
    }

    // Rule 4: one rock at the quarry, the rest paired outward across the line, the Warlord's lob.
    private void Spit(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        Vector3 self = board.SelfPosition;
        Vector3 target = board.PlayerPosition;
        Vector2 line = board.DirectionToPlayer;

        if (!(line.LengthSquared() > 0f))
        {
            line = Vector2.UnitY;
        }

        var across = new Vector3(-line.Y, 0f, line.X) * _stats.SpitSpread.Value;
        float speed = _stats.SpitSpeed.Value;
        float radius = _stats.SpitRadius.Value;
        float damage = _agent.ContactDamage.Value * _stats.SpitDamageScale.Value;
        int spears = (int)MathF.Max(0f, _stats.SpitSpears.Value);

        for (int i = 0; i < spears; i++)
        {
            int step = (i + 1) / 2;
            Vector3 offset = across * ((i & 1) == 1 ? step : -step);
            ctx.Projectiles.Fire(new Projectile(_agent.Spec.Id, _agent.Id, self, target + offset,
                speed, radius, damage), ctx.Now);
        }
    }

    // Rule 5: a window's end or a beat takes the armed move with it, unreleased.
    private void DropMove()
    {
        _moveArmed = false;
        _moveSpent = true;
    }

    // JG-25b: straight out from the centre to ThrowMargin past the edge; the body walks it and a
    // wall stops it short, so core never needs to know where the walls are.
    private void Throw(in EnemyTickContext ctx, Vector3 player, float radius)
    {
        var outward = new Vector2(player.X - RingCentre.X, player.Z - RingCentre.Z);
        float from = outward.Length();
        Vector2 direction;

        if (from > ThrowCentreEpsilon)
        {
            direction = outward / from;
        }
        else
        {
            direction = -ctx.PlayerFacing;
            float length = direction.Length();
            direction = length > 0f ? direction / length : Vector2.UnitY;
        }

        float seconds = _stats.ThrowSeconds.Value;
        float distance = MathF.Max(0f, radius + _stats.ThrowMargin.Value - from);

        if (!(seconds > 0f) || !(distance > 0f))
        {
            return;
        }

        ctx.Intents.Throw(new PlayerThrowIntent(direction, distance, seconds));
        _motor.Hold(seconds);
    }

    // JG-25d rule 4: farthest open ground from the body first, clear of the player and of every
    // crack still opening; too little ground opens fewer.
    private void OpenCracks(in EnemyTickContext ctx)
    {
        int per = Count(_stats.AddsPerSurface.Value);
        if (per == 0 || AddCount + per > Count(_stats.MaxAdds.Value))
        {
            return;
        }

        Vector3 self = _agent.Position;
        Vector3 player = _agent.Blackboard.PlayerPosition;
        float clearance = OpenGroundPicker.PlayerClearance * OpenGroundPicker.PlayerClearance;
        float seconds = _stats.CrackSeconds.Value;

        for (int n = 0; n < per; n++)
        {
            int best = -1;
            float bestDistance = -1f;

            for (int i = 0; i < ctx.OpenGround.Count; i++)
            {
                Vector3 point = ctx.OpenGround[i];
                if (!(FlatDistanceSquared(point, player) >= clearance) || !Spaced(point))
                {
                    continue;
                }

                float distance = FlatDistanceSquared(point, self);
                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                return;
            }

            Vector3 at = ctx.OpenGround[best];
            _crackAt[_crackCount] = at;
            _crackLeft[_crackCount] = seconds;
            _crackCount++;
            ctx.Events.Publish(new GravemawCrack(_agent.Id, at, seconds));
        }
    }

    private bool Spaced(Vector3 point)
    {
        float spacing = _stats.CrackSpacing.Value;
        for (int i = 0; i < _crackCount; i++)
        {
            if (!(FlatDistanceSquared(point, _crackAt[i]) >= spacing * spacing))
            {
                return false;
            }
        }

        return true;
    }

    // JG-25d rule 5: on ticked time, so a stun holds the cracks too; a full registry skips the add.
    private void Cracks(in EnemyTickContext ctx)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        int kept = 0;

        for (int i = 0; i < _crackCount; i++)
        {
            float left = _crackLeft[i] - ctx.Dt;
            if (left > 0f)
            {
                _crackAt[kept] = _crackAt[i];
                _crackLeft[kept++] = left;
                continue;
            }

            if (registry.AliveCount < registry.Capacity && _liveCount < AddCapacity)
            {
                _addIds[_liveCount++] = ctx.Enemies.SpawnSummon(_addId, _crackAt[i]).Id;
            }
        }

        _crackCount = kept;
    }

    // JG-25d rule 6: a dead or despawned add leaves the count on the tick it goes.
    private void RollCall(EnemyRegistry registry)
    {
        int kept = 0;
        for (int i = 0; i < _liveCount; i++)
        {
            if (registry.TryGet(_addIds[i], out EnemyAgent add) && add.IsAlive)
            {
                _addIds[kept++] = _addIds[i];
            }
        }

        _liveCount = kept;
    }

    private static int Count(float value) => (int)MathF.Min(AddCapacity, MathF.Max(0f, value));

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

    // JG-25h rules 2-3: no open ground known leaves the aim; none in reach takes the nearest anywhere.
    private Vector3 OpenSpot(IReadOnlyList<Vector3> open, Vector3 aim, float reach)
    {
        int best = -1;
        int nearest = -1;
        float bestDistance = float.MaxValue;
        float nearestDistance = float.MaxValue;
        float reachSquared = reach * reach;

        for (int i = 0; i < open.Count; i++)
        {
            float distance = FlatDistanceSquared(open[i], aim);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }

            if (distance < bestDistance && !(FlatDistanceSquared(open[i], _agent.Position) > reachSquared))
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best >= 0)
        {
            return open[best];
        }

        return nearest >= 0 ? open[nearest] : aim;
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;

        return (x * x) + (z * z);
    }
}
