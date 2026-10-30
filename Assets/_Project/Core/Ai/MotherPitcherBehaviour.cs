using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>A rooted boss whose living buds reduce every incoming hit to a fifth.</summary>
public sealed class MotherPitcherBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public const float ShieldedDamageTaken = 0.2f;
    public const int BarrageMove = 1, SprayMove = 2, PodMove = 3;
    public const float AttackGapSeconds = 2f, RecoverSeconds = 1f;
    public const float BarrageTelegraphSeconds = 0.8f, BarrageSpread = 3.5f;
    public const int BarrageLobs = 5;
    public const float LobSpeed = 7f, LobRadius = 1.6f;
    public const float SprayTelegraphSeconds = 1f, SprayHalfAngleDeg = 40f;
    public const float SprayRange = 12f, SprayDamageScale = 2f;
    public const float PodTelegraphSeconds = 0.8f, PodDistance = 3f;
    public const int MaxPodFrogs = 3;

    private readonly EnemyAgent _agent;
    private readonly ContentId _budId;
    private readonly ContentId _frogId;
    private readonly ProjectileSystem _projectiles;
    private readonly IRandomStream _random;
    private int _nextMove = BarrageMove;
    private int _move;
    private float _remaining = AttackGapSeconds;
    private bool _recovering;
    private Vector2 _aim;
    private Vector3 _sprayOrigin;
    private IDomainEvents _sprayEvents;

    public MotherPitcherBehaviour(EnemyAgent agent, ProjectileSystem projectiles,
        IRandomStream random, ContentId budId, ContentId frogId)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _budId = budId;
        _frogId = frogId;
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public bool IsShielded { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        bool shielded = false;
        ReadOnlySpan<EnemyAgent> agents = ctx.Enemies.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsAlive && agents[i].Spec.Id == _budId)
            {
                shielded = true;
                break;
            }
        }

        if (shielded != IsShielded)
        {
            _agent.DamageTaken.RemoveAll(this);
            if (shielded)
            {
                _agent.DamageTaken.Add(new Modifier(
                    ModifierKind.PercentMult, ShieldedDamageTaken - 1f, this));
            }

            IsShielded = shielded;
        }

        // One transition per tick: a long frame cannot consume a newly announced wind-up.
        _remaining -= ctx.Dt;
        if (_remaining <= 0f)
        {
            if (_move != 0)
            {
                Release(ctx);
                _move = 0;
                _recovering = true;
                _remaining = RecoverSeconds;
            }
            else if (_recovering)
            {
                _recovering = false;
                _remaining = AttackGapSeconds;
            }
            else
            {
                Arm(ctx);
            }
        }

        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, Vector3.Zero,
            _move == SprayMove ? _aim : _agent.Blackboard.DirectionToPlayer));
    }

    private void Arm(in EnemyTickContext ctx)
    {
        _move = _nextMove;
        _nextMove = _nextMove == PodMove ? BarrageMove : _nextMove + 1;
        if (_move == PodMove)
        {
            int frogs = 0;
            foreach (EnemyAgent agent in ctx.Enemies.Registry.Alive)
            {
                if (agent.IsAlive && agent.Spec.Id == _frogId)
                {
                    frogs++;
                }
            }

            if (frogs >= MaxPodFrogs)
            {
                _move = BarrageMove;
            }
        }

        _remaining = _move == SprayMove ? SprayTelegraphSeconds
            : _move == PodMove ? PodTelegraphSeconds : BarrageTelegraphSeconds;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, _remaining, _move));
        if (_move == SprayMove)
        {
            _aim = _agent.Blackboard.DirectionToPlayer;
            _sprayOrigin = _agent.Blackboard.SelfPosition;
            _sprayEvents = ctx.Events;
            ctx.Events.Publish(new SprayArmed(_agent.Id, _sprayOrigin, _aim,
                SprayHalfAngleDeg, SprayRange, _remaining));
        }
    }

    private void Release(in EnemyTickContext ctx)
    {
        EnemyBlackboard board = _agent.Blackboard;
        if (_move == BarrageMove)
        {
            Vector3 target = board.PlayerPosition;
            float turn = _random.NextFloat() * (2f * MathF.PI);
            for (int i = 0; i < BarrageLobs; i++)
            {
                float angle = turn + ((i - 1) * (MathF.PI / 2f));
                Vector3 offset = i == 0 ? Vector3.Zero
                    : new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * BarrageSpread;
                _projectiles.Fire(new Projectile(_agent.Spec.Id, _agent.Id,
                    board.SelfPosition, target + offset, LobSpeed, LobRadius,
                    _agent.ContactDamage.Value), ctx.Now);
            }
        }
        else if (_move == SprayMove)
        {
            Vector3 delta = board.PlayerPosition - _sprayOrigin;
            var flat = new Vector2(delta.X, delta.Z);
            float distance = flat.Length();
            bool hit = board.HasLineOfSight && distance <= SprayRange
                && (distance == 0f || Vector2.Dot(flat / distance, _aim)
                    >= MathF.Cos(SprayHalfAngleDeg * (MathF.PI / 180f)));
            if (hit)
            {
                ctx.Player.ApplyDamage(_agent.ContactDamage.Value * SprayDamageScale, ctx.Now);
            }

            _sprayEvents = null;
            ctx.Events.Publish(new SprayFired(_agent.Id, hit));
        }
        else
        {
            Vector2 away = -board.DirectionToPlayer;
            ctx.Enemies.Spawn(_frogId, board.SelfPosition
                + new Vector3(away.X, 0f, away.Y) * PodDistance);
        }
    }

    public void OnPhaseEntered(int phase)
    {
        _sprayEvents?.Publish(new SprayCleared(_agent.Id));
        _sprayEvents = null;
        _move = 0;
        _nextMove = BarrageMove;
        _recovering = false;
        _remaining = AttackGapSeconds;
    }

    public void Reset()
    {
        _agent.DamageTaken.RemoveAll(this);
        IsShielded = false;
        OnPhaseEntered(0);
    }
}
