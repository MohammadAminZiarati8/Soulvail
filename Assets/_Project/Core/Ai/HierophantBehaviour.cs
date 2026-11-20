using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Locked ground attacks. The boss wrapper owns invulnerability beats and summons.</summary>
public sealed class HierophantBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    private readonly EnemyAgent _agent;
    private readonly HierophantStats _stats;
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

    public HierophantBehaviour(EnemyAgent agent, HierophantStats stats)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    public void Tick(in EnemyTickContext ctx)
    {
        if (_sealHalfWidth == 0f)
            PlaceSeals(ctx);

        Vector3 velocity = Vector3.Zero;
        float dt = MathF.Max(0f, ctx.Dt);
        float available = MathF.Max(0f, dt - _remaining);
        _remaining = MathF.Max(0f, _remaining - dt);
        if (_telling)
        {
            ctx.Events.Publish(new HierophantTellProgress(_agent.Id,
                Math.Clamp(1f - _remaining / _armed.Seconds, 0f, 1f)));
            // An impact starts a fresh clock. This tick's excess time belongs to the tell.
            if (_remaining == 0f)
                Impact(ctx);
        }
        else if (_remaining == 0f)
        {
            velocity = Prepare(ctx, dt, available);
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, _facing));
    }

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

        float range = _stats.SweepRange.Value;
        if (!(range > 0f) || !float.IsFinite(range))
            throw new ArgumentOutOfRangeException(nameof(_stats.SweepRange));

        if (distance <= range)
        {
            _chaseSeconds = 0f;
            if (distance == 0f || TurnTowards(direction, dt))
            {
                Arm(ctx, new HierophantArmed(_agent.Id, _next, -1, origin, _facing,
                    range, 0f, _stats.SweepHalfAngleDegrees.Value, _stats.SweepTellSeconds.Value),
                    _stats.SweepDamage.Value, _stats.SweepRecoverySeconds.Value);
            }
            return Vector3.Zero;
        }

        float chaseLimit = _stats.SweepChaseSeconds.Value;
        if (!(chaseLimit > 0f) || !float.IsFinite(chaseLimit))
            throw new ArgumentOutOfRangeException(nameof(_stats.SweepChaseSeconds));
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
            new Vector3(origin.X, 0f, origin.Z), facing, reach, _sealHalfWidth, 0f, _stats.SealTellSeconds.Value),
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

        if (_phase == 2 && armed.Move == HierophantMove.SunSeal && _firstSealSlot < 0)
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
        EnemyBlackboard board = _agent.Blackboard;
        bool seal = _armed.Move == HierophantMove.SunSeal;
        var offset = new Vector2(board.PlayerPosition.X - _armed.Origin.X,
            board.PlayerPosition.Z - _armed.Origin.Z);
        if (!board.QuarryIsADecoy && !board.QuarryIsAPet && Contains(offset, seal))
            ctx.Player.ApplyDamage(_damage, ctx.Now);

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
            _next = seal ? HierophantMove.StaffSweep : HierophantMove.SunSeal;
            _remaining = _recovery;
        }
        _telling = false;
        ctx.Events.Publish(new HierophantImpact(_agent.Id, _armed.Move, _armed.SealSlot,
            firstOfPair ? 0f : _recovery));
    }

    private bool Contains(Vector2 offset, bool seal)
    {
        if (seal)
        {
            float along = Vector2.Dot(_armed.Facing, offset);
            float across = _armed.Facing.Y * offset.X - _armed.Facing.X * offset.Y;
            return along >= 0f && along <= _armed.Reach && MathF.Abs(across) <= _armed.HalfWidth;
        }

        float distance = offset.Length();
        if (!(distance <= _armed.Reach) || !float.IsFinite(distance))
            return false;

        // Keep the angular comparison in double precision so a rounded float radians
        // threshold cannot exclude a point on the authored edge.
        double dot = (double)_armed.Facing.X * offset.X + (double)_armed.Facing.Y * offset.Y;
        double cross = (double)_armed.Facing.X * offset.Y - (double)_armed.Facing.Y * offset.X;
        return Math.Atan2(Math.Abs(cross), dot) <= _armed.HalfAngleDegrees * (Math.PI / 180d);
    }

    private Vector3 Ground(Vector2 flat) => new(flat.X, 0f, flat.Y);

    public void OnPhaseEntered(int phase)
    {
        _phase = phase;
        _telling = false;
        _next = HierophantMove.SunSeal;
        _firstSealSlot = -1;
        _remaining = 0f;
        _chaseSeconds = 0f;
        _armed = default;
    }

    public void Reset()
    {
        OnPhaseEntered(0);
        _sealHalfWidth = 0f;
        _facing = Vector2.UnitY;
    }
}
