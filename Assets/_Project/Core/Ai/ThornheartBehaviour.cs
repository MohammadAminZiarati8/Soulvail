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
/// damage when it rises (JG-20b). The rotation is Roots → Pods → Roots → Slam. From 66 % a
/// strangling ring closes in and bites anyone outside it, and spokes stop short of a strafe lane
/// inside it; from 33 % the grove is faster and plants more (JG-20d).
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

    // JG-20d: the strangling ring from phase 1 (66 %), the faster grove from phase 2 (33 %). It
    // opens at 14 m, not 17: the Clearing is ~37 m across, so 17 put its edge on the walls (JG-20j).
    public const float RingStartRadius = 14f, RingRadius = 11f, RingCloseSeconds = 6f,
                       RingPulseSeconds = 0.5f, RingDamageScale = 0.25f, StrafeLane = 3f;
    public const int PhaseTwoSpokes = 4;
    public const float PhaseThreeRootTelegraphSeconds = 0.7f, PhaseThreeAttackGapSeconds = 1.1f;
    public const int PhaseThreePodsPerCast = 3;

    /// <summary>Phase 2's cap: room for one full cast, which <see cref="MaxAdds"/> is not.</summary>
    public const int PhaseThreeMaxAdds = PhaseThreePodsPerCast * RootlingsPerPod;

    private const float RootlingRing = 1f;
    private const int MaxPods = PhaseThreeMaxAdds / RootlingsPerPod;

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
    private readonly int[] _rootlingIds = new int[PhaseThreeMaxAdds];
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
    private bool _ringUp;
    private bool _ringAnnounced;
    private float _ringElapsed;
    private float _pulseRemaining = RingPulseSeconds;

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

    /// <summary>The boss's phase, 0 until the 66 % crossing (JG-20d rule 1).</summary>
    public int Phase { get; private set; }

    /// <summary>The ring's radius this tick, metres from the boss; 0 while there is no ring.</summary>
    public float RingRadiusNow => _ringUp
        ? RingStartRadius + ((RingRadius - RingStartRadius) * MathF.Min(_ringElapsed / RingCloseSeconds, 1f))
        : 0f;

    private float AttackGap => Phase >= 2 ? PhaseThreeAttackGapSeconds : AttackGapSeconds;

    private float RootTelegraph => Phase >= 2 ? PhaseThreeRootTelegraphSeconds : RootTelegraphSeconds;

    private int Spokes => Phase >= 1 ? PhaseTwoSpokes : RootSpokes;

    private int PodsThisCast => Phase >= 2 ? PhaseThreePodsPerCast : PodsPerCast;

    private int AddCap => Phase >= 2 ? PhaseThreeMaxAdds : MaxAdds;

    public void Tick(in EnemyTickContext ctx)
    {
        RollCall(ctx);
        Strangle(ctx);

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
                _remaining = AttackGap;
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
            _ => RootTelegraph,
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

        // JG-20d rule 4: with a ring up a spoke stops a lane short of where the ring ends — not of
        // where it is, since a spoke outstands the close and would otherwise cross the lane later.
        float outer = _ringUp ? MathF.Min(RootOuter, RingRadius - StrafeLane) : RootOuter;
        int spokes = Spokes;
        float telegraph = RootTelegraph;
        float jitter = ((_random.NextFloat() * 2f) - 1f) * RootAimJitterDeg * DegToRad;
        float bearing = MathF.Atan2(_aim.Y, _aim.X) + jitter;
        for (int i = 0; i < spokes; i++)
        {
            float angle = bearing + (i * (2f * MathF.PI / spokes));
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector3 origin = _agent.Position + (new Vector3(direction.X, 0f, direction.Y) * RootInner);

            // A refused spoke (Open returned 0) is simply skipped; the move keeps its clock (rule 6).
            _thorns.Open(origin, direction, outer - RootInner, RootHalfWidth,
                telegraph, RootStandSeconds, damage * RootDamageScale, ctx.Now);
        }
    }

    // JG-20d rules 2–3. The ring is announced on the first tick after the beat (the beat does not
    // tick the inner behaviour) and closes on this behaviour's own clock, so a stunned boss's ring
    // holds still and does not bite. Outside it, a pulse every RingPulseSeconds; stepping in resets.
    private void Strangle(in EnemyTickContext ctx)
    {
        if (!_ringUp)
        {
            return;
        }

        if (!_ringAnnounced)
        {
            _ringAnnounced = true;
            ctx.Events.Publish(new StrangleRing(_agent.Id, RingStartRadius, RingRadius, RingCloseSeconds));
        }
        else
        {
            _ringElapsed += ctx.Dt;
        }

        float radius = RingRadiusNow;
        Vector3 player = _agent.Blackboard.PlayerPosition;
        float x = player.X - _agent.Position.X;
        float z = player.Z - _agent.Position.Z;
        if (!((x * x) + (z * z) > radius * radius))
        {
            _pulseRemaining = RingPulseSeconds;
            return;
        }

        _pulseRemaining -= ctx.Dt;
        if (_pulseRemaining <= 0f)
        {
            _pulseRemaining = RingPulseSeconds;
            DamageResult bite = ctx.Player.ApplyDamage(_agent.ContactDamage.Value * RingDamageScale, ctx.Now);

            // JG-20j: a bite the player felt is drawn, so "nothing hit me" has an answer on screen.
            if (!bite.Blocked)
            {
                ctx.Events.Publish(new StrangleBite(_agent.Id, bite.ToHp));
            }
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
        int pods = PodsThisCast;
        int cap = AddCap;
        for (int n = 0; n < pods; n++)
        {
            if (AddCount + RootlingsPerPod > cap || registry.AliveCount >= registry.Capacity)
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
            if (registry.AliveCount >= registry.Capacity || _rootlingCount >= PhaseThreeMaxAdds)
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

    /// <summary>
    /// The beat: the armed move drops, the heart closes and every wall withers (JG-20b rule 8);
    /// from phase 1 the ring reopens wide and closes again (JG-20d rule 2).
    /// </summary>
    public void OnPhaseEntered(int phase)
    {
        Phase = phase;
        Move = ThornheartMove.None;
        _recovering = false;
        _remaining = AttackGap;
        _slamWall = 0;
        CloseHeart();
        _thorns.WitherAll();
        DropRing();
        _ringUp = phase >= 1;
    }

    private void DropRing()
    {
        _ringUp = false;
        _ringAnnounced = false;
        _ringElapsed = 0f;
        _pulseRemaining = RingPulseSeconds;
    }

    public void Reset()
    {
        CloseHeart();
        DropRing();
        Phase = 0;
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
