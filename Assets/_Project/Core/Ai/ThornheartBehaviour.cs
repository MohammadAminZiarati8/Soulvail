using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
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
    Pods = 3,
}

/// <summary>
/// A rooted boss that can always be shot: it grows thorn spokes, plants seed pods that burst into
/// Rootlings unless killed (JG-20c), and slams a root down a line that bares its heart to double
/// damage when it rises (JG-20b). The rotation is Roots → Pods → Roots → Slam.
/// </summary>
/// <remarks>
/// The heart opens on the slam wall's rise, read off the deadline the wall was opened with rather
/// than a subscription: <c>ThornSystem</c> ticks before the behaviours, so <c>Now</c> reaching the
/// deadline is the tick its <c>ThornRose</c> was published on. A stunned boss is not ticked, so the
/// heart then opens on its first tick back. Pods ripen on the same clock, so a pod due while the
/// boss is stunned bursts on its first tick back.
/// </remarks>
public sealed class ThornheartBehaviour : IEnemyBehaviour, IBossPhaseListener, IBossAddOwner
{
    public const float AttackGapSeconds = 1.6f, RecoverSeconds = 0.6f;
    public const float RootTelegraphSeconds = 0.9f, RootInner = 2f, RootOuter = 12f,
                       RootHalfWidth = 0.6f, RootStandSeconds = 12f, RootDamageScale = 1f,
                       RootAimJitterDeg = 20f;
    public const int RootSpokes = 3;
    public const float SlamTelegraphSeconds = 1f, SlamLength = 14f, SlamHalfWidth = 1.2f,
                       SlamDamageScale = 1.5f, SlamStandSeconds = 3f,
                       ExposedSeconds = 3f, ExposedDamageTaken = 2f;
    public const float PodTelegraphSeconds = 0.8f, PodRipenSeconds = 6f, PodClearance = 1.5f;
    public const int PodsPerCast = 2, RootlingsPerPod = 3, MaxAdds = 8;

    private const float RootlingRing = 1f;
    private const int MaxPods = MaxAdds / RootlingsPerPod;

    // Thorn-blocked open ground handed to the picker as reserved, so it prefers ground off the walls.
    private const int MaxBlocked = 32;

    private const float DegToRad = MathF.PI / 180f;

    private readonly EnemyAgent _agent;
    private readonly ThornSystem _thorns;
    private readonly IRandomStream _random;
    private readonly OpenGroundPicker _ground;
    private readonly ContentId _podId;
    private readonly ContentId _rootlingId;
    private readonly int[] _podIds = new int[MaxPods];
    private readonly float[] _podRipensAt = new float[MaxPods];
    private readonly int[] _rootlingIds = new int[MaxAdds];
    private readonly Vector3[] _blocked = new Vector3[MaxBlocked];
    private int _rootlingCount;
    private int _rotation;
    private float _remaining = AttackGapSeconds;
    private bool _recovering;
    private Vector2 _aim = Vector2.UnitY;
    private int _slamWall;
    private float _slamRisesAt;
    private float _exposedRemaining;
    private IDomainEvents _heartEvents;

    public ThornheartBehaviour(EnemyAgent agent, ThornSystem thorns, IRandomStream random,
        ContentId podId, ContentId rootlingId)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _thorns = thorns ?? throw new ArgumentNullException(nameof(thorns));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _ground = new OpenGroundPicker(_random);
        _podId = podId;
        _rootlingId = rootlingId;
    }

    public ThornheartMove Move { get; private set; }

    /// <summary>Living pods and Rootlings; a pod counts as the Rootlings it would grow (rule 4).</summary>
    public int AddCount => (PodCount * RootlingsPerPod) + _rootlingCount;

    public int PodCount { get; private set; }

    public bool IsExposed { get; private set; }

    public void Tick(in EnemyTickContext ctx)
    {
        RollCall(ctx);

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
                if (Move == ThornheartMove.Pods)
                {
                    Plant(ctx);
                }

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
        Move = _rotation switch
        {
            1 => ThornheartMove.Pods,
            3 => ThornheartMove.Slam,
            _ => ThornheartMove.Roots,
        };
        _rotation = (_rotation + 1) % 4;
        _remaining = Move switch
        {
            ThornheartMove.Pods => PodTelegraphSeconds,
            ThornheartMove.Slam => SlamTelegraphSeconds,
            _ => RootTelegraphSeconds,
        };
        ctx.Events.Publish(new EnemyTelegraph(_agent.Id, _remaining, (int)Move));
        if (Move == ThornheartMove.Pods)
        {
            return;
        }

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

    // Rule 2: each pod is one pick round the player; a pick the picker can't make, or one that still
    // lands on a wall or on a pod (the picker falls back to crowded ground), is skipped.
    private void Plant(in EnemyTickContext ctx)
    {
        Vector3 player = _agent.Blackboard.PlayerPosition;
        int blocked = 0;
        for (int i = 0; i < ctx.OpenGround.Count && blocked < MaxBlocked; i++)
        {
            if (_thorns.Blocks(ctx.OpenGround[i], PodClearance))
            {
                _blocked[blocked++] = ctx.OpenGround[i];
            }
        }

        EnemyRegistry registry = ctx.Enemies.Registry;
        for (int n = 0; n < PodsPerCast; n++)
        {
            if (AddCount + RootlingsPerPod > MaxAdds || registry.AliveCount >= registry.Capacity)
            {
                return;
            }

            if (!_ground.TryPick(ctx, player, player, _blocked.AsSpan(0, blocked), out Vector3 at)
                || _thorns.Blocks(at, PodClearance) || NearPod(registry, at))
            {
                continue;
            }

            int pod = ctx.Enemies.SpawnSummon(_podId, at).Id;
            _podIds[PodCount] = pod;
            _podRipensAt[PodCount] = ctx.Now + PodRipenSeconds;
            PodCount++;
            ctx.Events.Publish(new ThornPodPlanted(_agent.Id, pod, at, PodRipenSeconds));
        }
    }

    private bool NearPod(EnemyRegistry registry, Vector3 at)
    {
        for (int i = 0; i < PodCount; i++)
        {
            if (registry.TryGet(_podIds[i], out EnemyAgent pod))
            {
                float x = pod.Position.X - at.X;
                float z = pod.Position.Z - at.Z;
                if (!((x * x) + (z * z) >= PodClearance * PodClearance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Rules 3 and 5: the dead leave on the tick they go (registry ids are never reused); a pod alive
    // at its deadline goes and grows Rootlings in its place.
    private void RollCall(in EnemyTickContext ctx)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        int kept = 0;
        for (int i = 0; i < _rootlingCount; i++)
        {
            if (registry.TryGet(_rootlingIds[i], out EnemyAgent rootling) && rootling.IsAlive)
            {
                _rootlingIds[kept++] = _rootlingIds[i];
            }
        }

        _rootlingCount = kept;

        int pods = PodCount;
        PodCount = 0;
        for (int i = 0; i < pods; i++)
        {
            if (!registry.TryGet(_podIds[i], out EnemyAgent pod) || !pod.IsAlive)
            {
                continue;
            }

            if (ctx.Now >= _podRipensAt[i])
            {
                ctx.Enemies.DespawnAtEndOfTick(pod.Id);
                Ripen(ctx, pod.Position);
                continue;
            }

            _podIds[PodCount] = _podIds[i];
            _podRipensAt[PodCount] = _podRipensAt[i];
            PodCount++;
        }
    }

    // Evenly on a ring round the pod; a full registry skips the rest (rule 3).
    private void Ripen(in EnemyTickContext ctx, Vector3 at)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        for (int i = 0; i < RootlingsPerPod; i++)
        {
            if (registry.AliveCount >= registry.Capacity || _rootlingCount >= MaxAdds)
            {
                return;
            }

            float angle = i * (2f * MathF.PI / RootlingsPerPod);
            Vector3 spot = at + (new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * RootlingRing);
            _rootlingIds[_rootlingCount++] = ctx.Enemies.SpawnSummon(_rootlingId, spot).Id;
        }
    }

    /// <summary>The phase beat: every pod and Rootling goes, so no pod ripens through it (rule 5).</summary>
    public void ClearAdds(in EnemyTickContext ctx)
    {
        EnemyRegistry registry = ctx.Enemies.Registry;
        for (int i = 0; i < PodCount; i++)
        {
            if (registry.TryGet(_podIds[i], out EnemyAgent pod) && pod.IsAlive)
            {
                ctx.Enemies.DespawnAtEndOfTick(pod.Id);
            }
        }

        for (int i = 0; i < _rootlingCount; i++)
        {
            if (registry.TryGet(_rootlingIds[i], out EnemyAgent rootling) && rootling.IsAlive)
            {
                ctx.Enemies.DespawnAtEndOfTick(rootling.Id);
            }
        }

        PodCount = 0;
        _rootlingCount = 0;
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
        _rotation = 0;
        PodCount = 0;
        _rootlingCount = 0;
        _recovering = false;
        _remaining = AttackGapSeconds;
        _aim = Vector2.UnitY;
    }
}
