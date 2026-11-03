using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum ThornweaverState { Approach, Hold, Cast }

/// <summary>
/// The Thornweaver (JG-24a): holds its distance and plants short thorn walls on the run's
/// <see cref="ThornSystem"/> just beyond the player, across the retreat away from it.
/// </summary>
/// <remarks>
/// It remembers its walls by id and the table they live in, never the context: an id means nothing
/// without its table, and the death door (<c>EnemySystem</c>) has no context to hand it.
/// </remarks>
public sealed class ThornweaverBehaviour : IEnemyBehaviour
{
    /// <summary>Seconds from spawn to the first cast; then every <see cref="ThornweaverSpec.CastInterval"/>.</summary>
    public const float FirstCastSeconds = 2f;

    /// <summary>Metres inside the hold distance at which it backs off (rule 1).</summary>
    public const float BackOffBand = 3f;

    /// <summary>Metres beyond the hold distance it tolerates before walking in again.</summary>
    public const float HoldSlack = 0.25f;

    private readonly EnemyAgent _agent;
    private readonly int[] _walls = new int[ThornSystem.Capacity];
    private ThornSystem _table;
    private int _wallCount;
    private float _castCooldown;
    private float _castTimer;
    private bool _backingOff;
    private Vector2 _facing;

    public ThornweaverBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Reset();
    }

    public ThornweaverState State { get; private set; }

    /// <summary>How many of its own walls are arming or standing (rule 5).</summary>
    public int WallCount
    {
        get
        {
            int live = 0;
            for (int i = 0; i < _wallCount; i++)
            {
                if (_table.IsLive(_walls[i]))
                { live++; }
            }

            return live;
        }
    }

    public void Reset()
    {
        State = ThornweaverState.Approach;
        _table = null;
        _wallCount = 0;
        _castCooldown = FirstCastSeconds;
        _castTimer = 0f;
        _backingOff = false;
        _facing = Vector2.UnitY;
    }

    /// <summary>Withers its own walls and no other's (rule 6). Called by <c>EnemySystem</c> on its death.</summary>
    public void WitherWalls()
    {
        for (int i = 0; i < _wallCount; i++)
        {
            _table.Wither(_walls[i]);
        }

        _wallCount = 0;
        _table = null;
    }

    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Thorns is null)
        {
            throw new InvalidOperationException(
                "A Thornweaver was ticked with no ThornSystem on its EnemyTickContext. RunSession passes "
                    + "one; a context built anywhere else for a Thornweaver must too.");
        }

        ThornweaverSpec spec = _agent.Spec.Thornweaver;
        EnemyBlackboard bb = _agent.Blackboard;
        Vector2 look = bb.DirectionToPlayer.LengthSquared() > 1e-6f ? bb.DirectionToPlayer : _facing;

        Prune();
        _castCooldown -= ctx.Dt;

        if (State == ThornweaverState.Cast)
        {
            _castTimer += ctx.Dt;
            if (_castTimer < spec.CastSeconds)
            {
                Emit(ctx, Vector2.Zero, look);
                return;
            }

            Plant(ctx, spec, look);
            State = ThornweaverState.Hold;
        }
        else if (_castCooldown <= 0f && bb.DistanceToPlayer <= spec.CastRange && _wallCount < spec.MaxWalls)
        {
            // Rule 2; the interval runs from this start, so a refused wall waits a whole one (rule 4).
            State = ThornweaverState.Cast;
            _castTimer = 0f;
            _castCooldown = spec.CastInterval;
            _backingOff = false;
            ctx.Events.Publish(new EnemyTelegraph(_agent.Id, spec.CastSeconds));
            Emit(ctx, Vector2.Zero, look);
            return;
        }

        Move(ctx, spec, bb, look);
    }

    /// <summary>Rule 1: walk the path in to the hold distance; back off out to it from inside the band.</summary>
    private void Move(in EnemyTickContext ctx, ThornweaverSpec spec, EnemyBlackboard bb, Vector2 look)
    {
        float distance = bb.DistanceToPlayer;
        float speed = _agent.MoveSpeed.Value;

        if (_backingOff && distance >= spec.HoldDistance)
        { _backingOff = false; }
        else if (!_backingOff && distance < spec.HoldDistance - BackOffBand)
        { _backingOff = true; }

        if (_backingOff)
        {
            State = ThornweaverState.Approach;
            Emit(ctx, -look * Clamp(speed, spec.HoldDistance - distance, ctx.Dt), look);
            return;
        }

        if (distance > spec.HoldDistance + HoldSlack)
        {
            State = ThornweaverState.Approach;
            Vector2 inbound = bb.PathDirectionToPlayer != Vector2.Zero ? bb.PathDirectionToPlayer : look;
            Emit(ctx, inbound * Clamp(speed, distance - spec.HoldDistance, ctx.Dt), look);
            return;
        }

        State = ThornweaverState.Hold;
        Emit(ctx, Vector2.Zero, look);
    }

    /// <summary>
    /// Rule 3: a wall centred <see cref="ThornweaverSpec.BeyondDistance"/> past the player on the
    /// line from the weaver, running across it, read where the player stands now.
    /// </summary>
    private void Plant(in EnemyTickContext ctx, ThornweaverSpec spec, Vector2 look)
    {
        Vector3 player = _agent.Blackboard.PlayerPosition;
        Vector2 line = new Vector2(player.X - _agent.Position.X, player.Z - _agent.Position.Z);
        line = line.LengthSquared() > 1e-6f ? Vector2.Normalize(line) : look;

        Vector2 across = new Vector2(-line.Y, line.X);
        float half = spec.WallLength * 0.5f;
        Vector3 origin = new Vector3(
            player.X + (line.X * spec.BeyondDistance) - (across.X * half),
            player.Y,
            player.Z + (line.Y * spec.BeyondDistance) - (across.Y * half));

        int id = ctx.Thorns.Open(origin, across, spec.WallLength, spec.WallHalfWidth, spec.ArmSeconds,
            spec.StandSeconds, _agent.ContactDamage.Value, ctx.Now, _agent.Id);

        // Rule 4: refused is a spent cast and nothing more.
        if (id != 0)
        {
            _table = ctx.Thorns;
            _walls[_wallCount] = id;
            _wallCount++;
        }
    }

    /// <summary>Rule 5: a wall that withered or was cleared stops counting.</summary>
    private void Prune()
    {
        int write = 0;
        for (int read = 0; read < _wallCount; read++)
        {
            if (_table.IsLive(_walls[read]))
            {
                _walls[write] = _walls[read];
                write++;
            }
        }

        _wallCount = write;
        if (_wallCount == 0)
        { _table = null; }
    }

    private static float Clamp(float speed, float remaining, float dt) =>
        dt > 0f ? MathF.Min(speed, MathF.Max(0f, remaining) / dt) : speed;

    private void Emit(in EnemyTickContext ctx, Vector2 velocity, Vector2 facing)
    {
        _facing = facing;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, new Vector3(velocity.X, 0f, velocity.Y), facing));
    }
}
