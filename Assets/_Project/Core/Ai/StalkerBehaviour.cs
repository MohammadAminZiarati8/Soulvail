using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public enum StalkerState { Prowl, Lurk, Windup, Strike, Recover }

/// <summary>
/// The Chameleon stalker (JG-22a): walks to a spot behind the player, hides while it stands there,
/// and strikes down a warned lane once the player is close and looking away.
/// </summary>
/// <remarks>
/// Hidden lives on the agent rather than here, because the reveal is decided at
/// <c>EnemySystem</c>'s damage door and the candidate list reads it; this class only ever hides it
/// in <see cref="StalkerState.Lurk"/> and shows it on leaving.
/// </remarks>
public sealed class StalkerBehaviour : IEnemyBehaviour
{
    /// <summary>Metres from its goal at which a prowl counts as arrived (rule 1).</summary>
    public const float ArriveDistance = 0.5f;

    /// <summary>Metres the goal may drift from a lurking stalker before it prowls again (rule 2).</summary>
    public const float DriftDistance = 3f;

    private readonly EnemyAgent _agent;
    private float _stateTimer;
    private float _struck;
    private float _nextStrikeAt;
    private Vector2 _line;
    private Vector2 _facing;

    public StalkerBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Reset();
    }

    public StalkerState State { get; private set; }

    public void Reset()
    {
        State = StalkerState.Prowl;
        _stateTimer = 0f;
        _struck = 0f;
        _nextStrikeAt = 0f;
        _line = Vector2.Zero;
        _facing = Vector2.UnitY;
    }

    public void Tick(in EnemyTickContext ctx)
    {
        if (ctx.Lanes is null)
        {
            throw new InvalidOperationException(
                "A Stalker was ticked with no LaneSystem on its EnemyTickContext. RunSession passes "
                    + "one; a context built anywhere else for a Stalker must too.");
        }

        StalkerSpec spec = _agent.Spec.Stalker;
        Vector3 player = _agent.Blackboard.PlayerPosition;
        Vector2 toPlayer = new Vector2(player.X - _agent.Position.X, player.Z - _agent.Position.Z);
        float distance = toPlayer.Length();
        Vector2 look = distance > 1e-3f ? toPlayer / distance : _facing;
        // With no facing to hide behind it keeps to its own side of the player.
        Vector2 behind = ctx.PlayerFacing.LengthSquared() > 1e-6f ? Vector2.Normalize(ctx.PlayerFacing) : look;
        Vector2 goal = new Vector2(player.X, player.Z) - behind * spec.BehindDistance;
        Vector2 toGoal = goal - new Vector2(_agent.Position.X, _agent.Position.Z);

        switch (State)
        {
            case StalkerState.Prowl:
                Prowl(ctx, toGoal, look);
                break;

            case StalkerState.Lurk:
                Lurk(ctx, spec, toGoal, look, distance, behind);
                break;

            case StalkerState.Windup:
                _stateTimer += ctx.Dt;
                Emit(ctx, Vector2.Zero, _line);
                if (_stateTimer >= _agent.Spec.WindupTime)
                {
                    State = StalkerState.Strike;
                    _struck = 0f;
                }

                break;

            case StalkerState.Strike:
                StrikeStep(ctx, spec);
                break;

            case StalkerState.Recover:
                _stateTimer += ctx.Dt;
                Emit(ctx, Vector2.Zero, _line);
                if (_stateTimer >= _agent.Spec.RecoverTime)
                {
                    State = StalkerState.Prowl;
                }

                break;
        }
    }

    private void Prowl(in EnemyTickContext ctx, Vector2 toGoal, Vector2 look)
    {
        float distance = toGoal.Length();
        if (distance <= ArriveDistance)
        {
            State = StalkerState.Lurk;
            _stateTimer = 0f;
            Emit(ctx, Vector2.Zero, look);
            return;
        }

        Vector2 heading = toGoal / distance;
        float speed = _agent.MoveSpeed.Value;
        if (ctx.Dt > 0f)
        {
            speed = MathF.Min(speed, distance / ctx.Dt);
        }

        Emit(ctx, heading * speed, heading);
    }

    private void Lurk(in EnemyTickContext ctx, StalkerSpec spec, Vector2 toGoal, Vector2 look,
        float distance, Vector2 behind)
    {
        // Rule 2: the spot it chose has gone; it shows itself and walks again.
        if (toGoal.Length() > DriftDistance)
        {
            Show(ctx);
            State = StalkerState.Prowl;
            Prowl(ctx, toGoal, look);
            return;
        }

        _stateTimer += ctx.Dt;

        // Rules 3 and 7: only from hiding, off cooldown, close, and behind the player's facing. The
        // bearing is stalker − player, so it is −look.
        float cosLimit = MathF.Cos(spec.BehindAngleDegrees * (MathF.PI / 180f));
        if (_agent.IsHidden
            && ctx.Now >= _nextStrikeAt
            && distance <= spec.StrikeRange
            && ctx.PlayerFacing.LengthSquared() > 1e-6f
            && Vector2.Dot(behind, -look) <= cosLimit)
        {
            EnterWindup(ctx, spec, look);
            return;
        }

        if (!_agent.IsHidden && _stateTimer >= spec.CloakDelay && ctx.Now >= _agent.RevealedUntil)
        {
            _agent.IsHidden = true;
            ctx.Events.Publish(new EnemyHiddenChanged(_agent.Id, true));
        }

        Emit(ctx, Vector2.Zero, look);
    }

    private void EnterWindup(in EnemyTickContext ctx, StalkerSpec spec, Vector2 look)
    {
        Show(ctx);
        State = StalkerState.Windup;
        _stateTimer = 0f;
        _line = look;

        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));

        // A dropped lane (a full table) costs the warning, not the move — the Lunger's rule.
        ctx.Lanes.Open(
            _agent.Position,
            new Vector3(_line.X, 0f, _line.Y),
            spec.StrikeLength,
            spec.StrikeHalfWidth,
            _agent.Spec.WindupTime,
            _agent.ContactDamage.Value,
            ctx.Now);

        Emit(ctx, Vector2.Zero, _line);
    }

    private void StrikeStep(in EnemyTickContext ctx, StalkerSpec spec)
    {
        float remaining = spec.StrikeLength - _struck;
        if (!(remaining > 0f))
        {
            // Rule 4: the cooldown counts from the strike's end.
            State = StalkerState.Recover;
            _stateTimer = 0f;
            _nextStrikeAt = ctx.Now + spec.StrikeCooldown;
            Emit(ctx, Vector2.Zero, _line);
            return;
        }

        float speed = ctx.Dt > 0f ? MathF.Min(spec.StrikeSpeed, remaining / ctx.Dt) : spec.StrikeSpeed;
        _struck += speed * ctx.Dt;
        Emit(ctx, _line * speed, _line);
    }

    private void Show(in EnemyTickContext ctx)
    {
        if (_agent.IsHidden)
        {
            _agent.IsHidden = false;
            ctx.Events.Publish(new EnemyHiddenChanged(_agent.Id, false));
        }
    }

    private void Emit(in EnemyTickContext ctx, Vector2 velocity, Vector2 facing)
    {
        _facing = facing;
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, new Vector3(velocity.X, 0f, velocity.Y), facing));
    }
}
