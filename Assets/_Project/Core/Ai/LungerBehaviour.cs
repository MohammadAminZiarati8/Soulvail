using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Where a <see cref="LungerBehaviour"/> is in its cycle.</summary>
public enum LungerState
{
    /// <summary>Walking the path to the player until it is in range and off cooldown.</summary>
    Stalk,

    /// <summary>Standing, its lane armed, committed to <c>EnemyBlackboard.LungeDirection</c>.</summary>
    Windup,

    /// <summary>Crossing the lane it warned of, at <c>LungeSpec.Speed</c>.</summary>
    Dash,

    /// <summary>Standing after the dash, open to punishment.</summary>
    Recover,

    /// <summary>A pack's flanker swinging round to the player's side (JG-21b).</summary>
    Flank,
}

/// <summary>
/// GD §8.1's Lunger: stalks, stops, warns with a lane down its line, and dashes the lane's length.
/// JG-07a.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lane is the hit.</b> The wind-up opens one <see cref="LaneSystem"/> lane, armed for exactly
/// the wind-up, and the lane bites whoever stands in it on the tick it ends — the tick the dash leaves.
/// The behaviour applies no damage of its own: one rule for one danger, the read Old Croaker's lash
/// already taught (JG-04e). The body then crosses the strip it warned of.
/// </para>
/// <para>
/// <b>It commits to where the player was.</b> The direction is caught on entering the wind-up, in
/// <see cref="EnemyBlackboard.LungeDirection"/>, and every intent until the next stalk faces it.
/// </para>
/// <para>
/// <b>A lane outlives its Lunger.</b> One killed or reset mid-wind-up leaves its strip on the floor,
/// and the strip bites, as a fissure does: the warning was honest when it was drawn.
/// </para>
/// <para>
/// <b>A pair hunts from two angles</b> (JG-21b). With a <see cref="PackSpec"/> it takes the nearest
/// unpaired agent of its own spec as a partner; the lower id leads and is the Lunger above, the
/// higher flanks to the player's side and winds up the tick its leader does. Ids only grow and
/// <see cref="EnemyRegistry.Alive"/> is in spawn order, so the leader is always ticked first and its
/// wind-up is already visible when the flanker looks.
/// </para>
/// <para>
/// The context is unpacked into fields on the way in and cleared in a <c>finally</c> on the way out,
/// as <see cref="ChaserBehaviour"/> does it and for its reason.
/// </para>
/// </remarks>
public sealed class LungerBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private readonly StateMachine<LungerState> _machine;

    private float _now;

    private IIntentSink _intents;
    private IDomainEvents _events;
    private LaneSystem _lanes;
    private PetSystem _pets;
    private int _attackPetId;
    private bool _bitPet;
    private EnemyRegistry _registry;

    /// <summary>Seconds until an unpaired pack hunter looks for a partner again.</summary>
    private float _pairTimer;

    /// <summary>Seconds in the current <see cref="LungerState.Flank"/>.</summary>
    private float _flankTimer;

    /// <summary>The run time of the last wind-up's start, so a flanker can tell it began this tick.</summary>
    private float _windupAt = float.NegativeInfinity;

    /// <summary>Metres of the current dash already covered, so the last tick stops on its length.</summary>
    private float _dashed;

    /// <summary>The way it last faced while stalking: the dash's fallback when the player is on top of it.</summary>
    private Vector2 _facing = new Vector2(0f, 1f);

    /// <param name="agent">The enemy this drives. Its spec must carry a <see cref="LungeSpec"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public LungerBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        _machine = new StateMachine<LungerState>(LungerState.Stalk);

        _machine.OnTick(LungerState.Stalk, TickStalk);

        _machine.OnEnter(LungerState.Windup, EnterWindup);
        _machine.OnTick(LungerState.Windup, TickWindup);

        _machine.OnEnter(LungerState.Dash, EnterDash);
        _machine.OnTick(LungerState.Dash, TickDash);

        _machine.OnEnter(LungerState.Recover, EnterRecover);
        _machine.OnTick(LungerState.Recover, TickRecover);

        _machine.OnEnter(LungerState.Flank, EnterFlank);
        _machine.OnTick(LungerState.Flank, Flank);

        _machine.Start();
    }

    /// <summary>Where it is in its cycle.</summary>
    public LungerState State => _machine.Current;

    /// <summary>Whether it is the higher id of a pair, the one that swings to the side (JG-21b).</summary>
    public bool IsFlanker => _agent.Blackboard.PartnerId >= 0 && _agent.Blackboard.PartnerId < _agent.Id;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <paramref name="ctx"/> carries no <see cref="EnemyTickContext.Lanes"/> (rule 9): a Lunger that
    /// cannot warn must not dash, and nothing would report it if it simply did.
    /// </exception>
    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Lanes is null)
        {
            throw new InvalidOperationException(
                "A Lunger was ticked with no LaneSystem on its EnemyTickContext. RunSession passes "
                    + "one; a context built anywhere else for a Lunger must too.");
        }

        _now = ctx.Now;
        _intents = ctx.Intents;
        _events = ctx.Events;
        _lanes = ctx.Lanes;
        _pets = ctx.Pets;
        _registry = ctx.Enemies.Registry;

        try
        {
            if (_agent.Spec.Pack is not null)
            {
                KeepPartner(ctx.Dt);
            }

            _machine.Tick(ctx.Dt);
        }
        finally
        {
            _intents = null;
            _events = null;
            _lanes = null;
            _pets = null;
            _registry = null;
        }
    }

    /// <summary>A fresh Lunger: stalking, off cooldown. A lane already open stays open (rule 10).</summary>
    public void Reset()
    {
        _machine.Transition(LungerState.Stalk);

        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;
        blackboard.NextAttackAt = 0f;
        blackboard.LungeDirection = Vector2.Zero;
        blackboard.PartnerId = -1;

        _dashed = 0f;
        _pairTimer = 0f;
        _flankTimer = 0f;
        _windupAt = float.NegativeInfinity;
        _facing = new Vector2(0f, 1f);
    }

    /// <summary>Seconds between an unpaired pack hunter's looks for a partner (JG-21b rule 1).</summary>
    private const float PairInterval = 0.5f;

    /// <summary>Metres from its goal at which a flanker counts as there (JG-21b rule 5).</summary>
    private const float ArriveDistance = 1f;

    private LungeSpec Lunge => _agent.Spec.Lunge;

    /// <summary>Rule 1: the chaser's walk, path first, until in range and off cooldown.</summary>
    private void TickStalk(float dt)
    {
        if (IsFlanker)
        {
            _machine.Transition(LungerState.Flank);

            Flank(dt);

            return;
        }

        EnemyBlackboard blackboard = _agent.Blackboard;

        if (blackboard.DistanceToPlayer <= Lunge.Range && _now >= blackboard.NextAttackAt)
        {
            // Deferred by the machine until this handler returns, so EnterWindup runs after the
            // stop below: this tick's one intent is the halt, and the wind-up starts from it.
            _machine.Transition(LungerState.Windup);

            Stand(blackboard.DirectionToPlayer != Vector2.Zero ? blackboard.DirectionToPlayer : _facing);

            return;
        }

        Vector2 direction = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        if (direction != Vector2.Zero)
        {
            _facing = direction;
        }

        // The agent's stat, not the spec's float, so depth scaling reaches the walk (M2-03 rule 7).
        float speed = _agent.MoveSpeed.Value;

        Emit(new Vector3(direction.X * speed, 0f, direction.Y * speed), _facing);
    }

    /// <summary>Rule 2: commit to the player's line, telegraph, and open the lane.</summary>
    private void EnterWindup()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;
        _windupAt = _now;
        _attackPetId = blackboard.QuarryPetId;
        _bitPet = false;

        Vector2 aim = blackboard.DirectionToPlayer != Vector2.Zero ? blackboard.DirectionToPlayer : _facing;

        blackboard.LungeDirection = Vector2.Normalize(aim);

        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));

        // Pet lunges use the windup tell above. A lane would also deal area damage to the player.
        if (_attackPetId != 0)
        {
            return;
        }

        // A dropped lane (a full table) costs the warning, not the move (rule 8). The agent's stat,
        // so a deep Lunger's lane bites as hard as its depth says.
        _lanes.Open(
            _agent.Position,
            new Vector3(blackboard.LungeDirection.X, 0f, blackboard.LungeDirection.Y),
            Lunge.Length,
            Lunge.HalfWidth,
            _agent.Spec.WindupTime,
            _agent.ContactDamage.Value,
            _now);
    }

    /// <summary>Rule 3: stand facing the committed line until the lane is due.</summary>
    private void TickWindup(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.LungeDirection);

        if (blackboard.StateTimer >= _agent.Spec.WindupTime)
        {
            _machine.Transition(LungerState.Dash);
        }
    }

    private void EnterDash()
    {
        _agent.Blackboard.StateTimer = 0f;

        _dashed = 0f;
    }

    /// <summary>
    /// Rule 4: along the committed line at <see cref="LungeSpec.Speed"/>. The last tick is cut to the
    /// distance left, so the dash covers the lane's length and not a tick past it.
    /// </summary>
    private void TickDash(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        if (!_bitPet && _attackPetId != 0 && _attackPetId == blackboard.QuarryPetId
            && blackboard.DistanceToPlayer <= _agent.Spec.Reach)
        {
            _bitPet = true;
            _pets?.ApplyDamage(_attackPetId, _agent.ContactDamage.Value, _now, _agent.Id);
        }

        float remaining = Lunge.Length - _dashed;

        if (!(remaining > 0f))
        {
            _machine.Transition(LungerState.Recover);

            Stand(blackboard.LungeDirection);

            return;
        }

        float speed = dt > 0f ? MathF.Min(Lunge.Speed, remaining / dt) : Lunge.Speed;

        _dashed += speed * dt;

        Vector2 line = blackboard.LungeDirection;

        Emit(new Vector3(line.X * speed, 0f, line.Y * speed), line);
    }

    /// <summary>Rule 5: the cooldown counts from the dash's end.</summary>
    private void EnterRecover()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;
        blackboard.NextAttackAt = _now + Lunge.Cooldown;
    }

    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.LungeDirection);

        if (blackboard.StateTimer >= _agent.Spec.RecoverTime)
        {
            _facing = blackboard.LungeDirection;

            _machine.Transition(LungerState.Stalk);
        }
    }

    /// <summary>
    /// JG-21b rules 1 and 6: drop a partner that died or no longer names this one, and, while
    /// unpaired, look every <see cref="PairInterval"/> s for the nearest unpaired one of the same spec.
    /// </summary>
    private void KeepPartner(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        if (blackboard.PartnerId >= 0)
        {
            if (_registry.TryGet(blackboard.PartnerId, out EnemyAgent partner)
                && partner.IsAlive
                && partner.Blackboard.PartnerId == _agent.Id)
            {
                return;
            }

            blackboard.PartnerId = -1;
            _pairTimer = 0f;

            if (State == LungerState.Flank)
            {
                _machine.Transition(LungerState.Stalk);
            }
        }

        _pairTimer -= dt;

        if (_pairTimer > 0f)
        {
            return;
        }

        _pairTimer = PairInterval;

        float radius = _agent.Spec.Pack.PartnerRadius;
        float nearest = radius * radius;
        EnemyAgent chosen = null;

        foreach (EnemyAgent other in _registry.Alive)
        {
            if (other.Id == _agent.Id
                || !other.IsAlive
                || other.Blackboard.PartnerId >= 0
                || other.Behaviour is not LungerBehaviour
                || !other.Spec.Id.Equals(_agent.Spec.Id))
            {
                continue;
            }

            float distance = DistanceSquaredXZ(other.Position, _agent.Position);

            if (distance <= nearest)
            {
                nearest = distance;
                chosen = other;
            }
        }

        if (chosen is not null)
        {
            blackboard.PartnerId = chosen.Id;
            chosen.Blackboard.PartnerId = _agent.Id;
        }
    }

    private void EnterFlank() => _flankTimer = 0f;

    /// <summary>
    /// JG-21b rules 3–5: walk to the side, join the leader's wind-up the tick it starts, or strike
    /// alone once at the goal or past the timeout while the leader is not winding up.
    /// </summary>
    private void Flank(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        _flankTimer += dt;

        if (!_registry.TryGet(blackboard.PartnerId, out EnemyAgent leader)
            || leader.Behaviour is not LungerBehaviour partner)
        {
            _machine.Transition(LungerState.Stalk);

            Stand(_facing);

            return;
        }

        Vector3 goal = FlankGoal(leader.Position);
        Vector3 delta = goal - _agent.Position;
        delta.Y = 0f;
        float distance = delta.Length();

        bool leaderWinding = partner.State == LungerState.Windup;
        bool leaderJustWound = leaderWinding && partner._windupAt == _now;
        bool alone = !leaderWinding
            && (distance <= ArriveDistance || _flankTimer >= _agent.Spec.Pack.FlankTimeout);

        if (blackboard.DistanceToPlayer <= Lunge.Range
            && _now >= blackboard.NextAttackAt
            && (leaderJustWound || alone))
        {
            _machine.Transition(LungerState.Windup);

            Stand(blackboard.DirectionToPlayer != Vector2.Zero ? blackboard.DirectionToPlayer : _facing);

            return;
        }

        if (distance <= ArriveDistance)
        {
            if (blackboard.DirectionToPlayer != Vector2.Zero)
            {
                _facing = blackboard.DirectionToPlayer;
            }

            Stand(_facing);

            return;
        }

        Vector2 heading = new Vector2(delta.X / distance, delta.Z / distance);
        _facing = heading;

        float speed = _agent.MoveSpeed.Value;

        if (dt > 0f)
        {
            speed = MathF.Min(speed, distance / dt);
        }

        Emit(new Vector3(heading.X * speed, 0f, heading.Y * speed), heading);
    }

    /// <summary>
    /// <c>LungeSpec.Range - 1</c> m from the player, <c>FlankAngleDegrees</c> round from the leader's
    /// bearing, on whichever side this one is already nearer.
    /// </summary>
    private Vector3 FlankGoal(Vector3 leaderPosition)
    {
        Vector3 player = _agent.Blackboard.PlayerPosition;

        Vector2 bearing = new Vector2(leaderPosition.X - player.X, leaderPosition.Z - player.Z);
        bearing = bearing.LengthSquared() > 1e-6f ? Vector2.Normalize(bearing) : new Vector2(0f, 1f);

        float radius = MathF.Max(0f, Lunge.Range - 1f);
        float angle = _agent.Spec.Pack.FlankAngleDegrees * (MathF.PI / 180f);
        float sin = MathF.Sin(angle);
        float cos = MathF.Cos(angle);

        Vector2 left = new Vector2(bearing.X * cos - bearing.Y * sin, bearing.X * sin + bearing.Y * cos) * radius;
        Vector2 right = new Vector2(bearing.X * cos + bearing.Y * sin, -bearing.X * sin + bearing.Y * cos) * radius;
        Vector2 self = new Vector2(_agent.Position.X - player.X, _agent.Position.Z - player.Z);

        Vector2 side = Vector2.DistanceSquared(self, left) <= Vector2.DistanceSquared(self, right) ? left : right;

        return new Vector3(player.X + side.X, _agent.Position.Y, player.Z + side.Y);
    }

    private static float DistanceSquaredXZ(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X;
        float z = a.Z - b.Z;

        return x * x + z * z;
    }

    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
