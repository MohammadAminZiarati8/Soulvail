using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Which of Thornheart's moves is running; the value is its <c>EnemyTelegraph</c> kind.</summary>
public enum ThornheartMove
{
    None,
    Roots = 1,
    Slam = 2,
}

/// <summary>
/// A rooted boss that can always be shot: it grows thorn spokes, then slams a root down a line that
/// bares its heart to double damage when it rises (JG-20b).
/// </summary>
/// <remarks>
/// The heart opens on the slam wall's rise, read off the deadline the wall was opened with rather
/// than a subscription: <c>ThornSystem</c> ticks before the behaviours, so <c>Now</c> reaching the
/// deadline is the tick its <c>ThornRose</c> was published on. A stunned boss is not ticked, so the
/// heart then opens on its first tick back.
/// </remarks>
public sealed class ThornheartBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public const float AttackGapSeconds = 1.6f, RecoverSeconds = 0.6f;
    public const float RootTelegraphSeconds = 0.9f, RootInner = 2f, RootOuter = 12f,
                       RootHalfWidth = 0.6f, RootStandSeconds = 12f, RootDamageScale = 1f,
                       RootAimJitterDeg = 20f;
    public const int RootSpokes = 3;
    public const float SlamTelegraphSeconds = 1f, SlamLength = 14f, SlamHalfWidth = 1.2f,
                       SlamDamageScale = 1.5f, SlamStandSeconds = 3f,
                       ExposedSeconds = 3f, ExposedDamageTaken = 2f;

    private const float DegToRad = MathF.PI / 180f;

    private readonly EnemyAgent _agent;
    private readonly ThornSystem _thorns;
    private readonly IRandomStream _random;
    private ThornheartMove _nextMove = ThornheartMove.Roots;
    private float _remaining = AttackGapSeconds;
    private bool _recovering;
    private Vector2 _aim = Vector2.UnitY;
    private int _slamWall;
    private float _slamRisesAt;
    private float _exposedRemaining;
    private IDomainEvents _heartEvents;

    public ThornheartBehaviour(EnemyAgent agent, ThornSystem thorns, IRandomStream random)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _thorns = thorns ?? throw new ArgumentNullException(nameof(thorns));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public ThornheartMove Move { get; private set; }

    public bool IsExposed { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        if (IsExposed)
        {
            _exposedRemaining -= ctx.Dt;
            if (_exposedRemaining <= 0f)
            {
                CloseHeart();
            }
        }

        if (_slamWall != 0 && ctx.Now >= _slamRisesAt)
        {
            _slamWall = 0;
            OpenHeart(ctx.Events);
        }

        // One transition per tick: a long frame cannot consume a newly announced wind-up.
        _remaining -= ctx.Dt;
        if (_remaining <= 0f)
        {
            if (Move != ThornheartMove.None)
            {
                Move = ThornheartMove.None;
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
            Move == ThornheartMove.Slam ? _aim : _agent.Blackboard.DirectionToPlayer));
    }

    private void Arm(in EnemyTickContext ctx)
    {
        Move = _nextMove;
        _nextMove = Move == ThornheartMove.Roots ? ThornheartMove.Slam : ThornheartMove.Roots;
        _remaining = Move == ThornheartMove.Roots ? RootTelegraphSeconds : SlamTelegraphSeconds;
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, _remaining, (int)Move));

        Vector2 toPlayer = _agent.Blackboard.DirectionToPlayer;
        _aim = toPlayer.LengthSquared() > 0f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
        float damage = _agent.ContactDamage.Value;

        if (Move == ThornheartMove.Slam)
        {
            _slamWall = _thorns.Open(_agent.Position, _aim, SlamLength, SlamHalfWidth,
                SlamTelegraphSeconds, SlamStandSeconds, damage * SlamDamageScale, ctx.Now);
            _slamRisesAt = ctx.Now + SlamTelegraphSeconds;
            return;
        }

        float jitter = ((_random.NextFloat() * 2f) - 1f) * RootAimJitterDeg * DegToRad;
        float bearing = MathF.Atan2(_aim.Y, _aim.X) + jitter;
        for (int i = 0; i < RootSpokes; i++)
        {
            float angle = bearing + (i * (2f * MathF.PI / RootSpokes));
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector3 origin = _agent.Position + (new Vector3(direction.X, 0f, direction.Y) * RootInner);

            // A refused spoke (Open returned 0) is simply skipped; the move keeps its clock (rule 6).
            _thorns.Open(origin, direction, RootOuter - RootInner, RootHalfWidth,
                RootTelegraphSeconds, RootStandSeconds, damage * RootDamageScale, ctx.Now);
        }
    }

    private void OpenHeart(IDomainEvents events)
    {
        _agent.DamageTaken.RemoveAll(this);
        _agent.DamageTaken.Add(new Modifier(ModifierKind.PercentMult, ExposedDamageTaken - 1f, this));
        IsExposed = true;
        _exposedRemaining = ExposedSeconds;
        _heartEvents = events;
        events.Publish(new HeartExposed(_agent.Id, true, ExposedSeconds));
    }

    private void CloseHeart()
    {
        _agent.DamageTaken.RemoveAll(this);
        if (IsExposed)
        {
            IsExposed = false;
            _heartEvents?.Publish(new HeartExposed(_agent.Id, false, 0f));
        }

        _exposedRemaining = 0f;
    }

    public void OnPhaseEntered(int phase)
    {
        Move = ThornheartMove.None;
        _recovering = false;
        _remaining = AttackGapSeconds;
        _slamWall = 0;
        CloseHeart();
        _thorns.WitherAll();
    }

    public void Reset()
    {
        CloseHeart();
        _heartEvents = null;
        _slamWall = 0;
        Move = ThornheartMove.None;
        _nextMove = ThornheartMove.Roots;
        _recovering = false;
        _remaining = AttackGapSeconds;
        _aim = Vector2.UnitY;
    }
}
