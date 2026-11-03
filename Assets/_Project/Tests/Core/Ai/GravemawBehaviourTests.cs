using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-25a: Gravemaw burrows, chases, rings the predicted spot, erupts and stands open.</summary>
[TestFixture]
public sealed class GravemawBehaviourTests
{
    private const float Speed = 4f;
    private const float Contact = 20f;

    /// <summary>Exact in binary, so 40 steps make 2.5 s to the bit.</summary>
    private const float Step = 0.0625f;

    private const float Frame = 1f / 64f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private GravemawBehaviour _gravemaw;
    private RunSession _session;
    private Vector3 _bossAt;
    private Vector3 _playerAt;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        _gravemaw = new GravemawBehaviour(_agent, new GravemawStats(), new FixedRandom(0.5f).Misc);
        _session = null;
        _now = 0f;
        Place(new Vector3(0f, 0f, 3f));
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void Cycle_RunsInOrder()
    {
        Tick(0f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Assert.That(_events.Single<GravemawBurrowed>().BossId, Is.EqualTo(_agent.Id));
        Tick(2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Tick(1f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
        Tick(4f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        Tick(0.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Assert.That(_events.Count<GravemawBurrowed>(), Is.EqualTo(2));
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Burrowed_IsInvulnerableAndUntargetable()
    {
        Tick(0f);
        AssertGuard(true);
        Tick(2.5f);
        AssertGuard(true);
        Tick(1f);
        Tick(4f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        AssertGuard(true);
    }

    [Test]
    public void Surfaced_IsVulnerable()
    {
        Surface();
        AssertGuard(false);
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void Chase_WalksToPlayer_EndsAfterSecondsAndInReach()
    {
        Tick(0f);
        Tick(2.4375f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase), "In reach, but not 2.5 s yet.");
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(new Vector3(0f, 0f, Speed)));
        Tick(Step);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));

        SetUp();
        Place(new Vector3(0f, 0f, 12.5f));
        Tick(0f);
        Tick(5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase), "12.5 m is past the 12 m tunnel.");
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(new Vector3(0f, 0f, Speed)));
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Predict_StraightRun_Leads()
    {
        Run(new Vector3(-6f, 0f, 3f), new Vector3(3f, 0f, 0f), 2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        AssertNear(_gravemaw.RingCentre, new Vector3(4.5f, 0f, 3f), 0.02f);
    }

    [Test]
    public void Predict_Standing_OnPlayer()
    {
        Place(new Vector3(1f, 0f, 3f));
        Run(new Vector3(1f, 0f, 3f), Vector3.Zero, 2.5f);
        Assert.That(_gravemaw.RingCentre, Is.EqualTo(new Vector3(1f, 0f, 3f)));
    }

    [Test]
    public void Predict_Clamped()
    {
        Run(new Vector3(-24f, 0f, 2f), new Vector3(10f, 0f, 0f), 2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        AssertNear(_gravemaw.RingCentre, new Vector3(7f, 0f, 2f), 0.01f);
    }

    [Test]
    public void Predict_Decoy_AimsAtDecoy()
    {
        Run(new Vector3(-6f, 0f, 3f), new Vector3(3f, 0f, 0f), 1.25f);
        var decoy = new Vector3(2f, 0f, 2f);
        _agent.Blackboard.QuarryIsADecoy = true;
        Run(decoy, Vector3.Zero, 1.25f, first: false);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Assert.That(_gravemaw.RingCentre, Is.EqualTo(decoy), "The jump to the decoy is not read as speed.");
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Ring_TelegraphsOneSecond_NeverMoves()
    {
        ArmRing();
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Duration, Is.EqualTo(1f));
        Assert.That(telegraph.Move, Is.EqualTo(GravemawBehaviour.EruptionMove));
        GravemawRingArmed ring = _events.Single<GravemawRingArmed>();
        Assert.That(ring.At, Is.EqualTo(new Vector3(0f, 0f, 3f)));
        Assert.That(ring.Radius, Is.EqualTo(3f));
        Assert.That(ring.Seconds, Is.EqualTo(1f));

        Place(new Vector3(5f, 0f, -2f));
        Tick(0.5f);
        Assert.That(_gravemaw.RingCentre, Is.EqualTo(ring.At));
        Assert.That(_events.Count<GravemawRingArmed>(), Is.EqualTo(1));
    }

    // ---- Rule 6 -------------------------------------------------------------------------------------

    [Test]
    public void Tunnel_ArrivesAtCentre()
    {
        StartSession(new Vector3(0f, 0f, 9f));
        SessionUntil(() => _gravemaw.State == GravemawState.Tunnel, 4f);
        Vector3 centre = _gravemaw.RingCentre;
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "The arming tick stands.");
        SessionFor(Frame);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(),
            Is.EqualTo(_agent.MoveSpeed.Value * 3f).Within(1e-3f));
        SessionFor(0.9f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Assert.That(Flat(_bossAt - centre), Is.LessThanOrEqualTo(GravemawBehaviour.ArriveDistance));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    // ---- Rule 7 -------------------------------------------------------------------------------------

    [Test]
    public void Eruption_InsideRing_Hits()
    {
        ArmRing();
        Place(new Vector3(2.9f, 0f, 3f));
        Tick(0.9375f);
        Assert.That(_events.Count<GravemawErupted>(), Is.Zero);
        Tick(Step);
        GravemawErupted erupted = _events.Single<GravemawErupted>();
        Assert.That(erupted.Hit, Is.True);
        Assert.That(erupted.At, Is.EqualTo(new Vector3(0f, 0f, 3f)));
        Assert.That(erupted.Radius, Is.EqualTo(3f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f - (Contact * 1.5f)));
    }

    [Test]
    public void Eruption_Outside_Misses()
    {
        ArmRing();
        Place(new Vector3(3.01f, 0f, 3f));
        Tick(1f);
        Assert.That(_events.Single<GravemawErupted>().Hit, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    [Test]
    public void Eruption_Decoy_Misses()
    {
        ArmRing();
        _agent.Blackboard.QuarryIsADecoy = true;
        Tick(1f);
        Assert.That(_events.Single<GravemawErupted>().Hit, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
    }

    // ---- Rule 8 -------------------------------------------------------------------------------------

    [Test]
    public void Window_FourSeconds_ThenDiveThenChase()
    {
        Surface();
        GravemawWindow open = _events.Single<GravemawWindow>();
        Assert.That(open.Open, Is.True);
        Assert.That(open.Seconds, Is.EqualTo(4f));
        Tick(3.9375f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
        Tick(Step);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        Assert.That(_events.Of<GravemawWindow>()[1].Open, Is.False);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero), "Dive stands.");
        Tick(0.4375f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        Tick(Step);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Assert.That(_events.Count<GravemawBurrowed>(), Is.EqualTo(2));
    }

    // ---- Rule 9 -------------------------------------------------------------------------------------

    [Test]
    public void Stun_StretchesWindow()
    {
        StartSession(new Vector3(0f, 0f, 3f));
        SessionUntil(() => _gravemaw.State == GravemawState.Surfaced, 5f);
        SessionFor(2f);
        Assert.That(_enemies.Stun(_agent.Id, 2f), Is.True, "A boss keeps half: 1 s.");
        SessionFor(2.9f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced), "3.9 s ticked of 4.");
        SessionFor(0.2f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
    }

    [Test]
    public void Root_Tunnel_RingStillBites()
    {
        StartSession(new Vector3(0f, 0f, 9f));
        SessionUntil(() => _gravemaw.State == GravemawState.Tunnel, 4f);
        Vector3 centre = _gravemaw.RingCentre;
        Assert.That(_enemies.Root(_agent.Id, 4f), Is.True);
        SessionUntil(() => _gravemaw.State == GravemawState.Surfaced, 1.5f);
        Assert.That(Flat(_bossAt - centre), Is.GreaterThan(8f), "Rooted, the body stayed put.");
        GravemawErupted erupted = _events.Single<GravemawErupted>();
        Assert.That(erupted.At, Is.EqualTo(centre));
        Assert.That(erupted.Hit, Is.True);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
    }

    [Test]
    public void Slow_ChaseSlower()
    {
        StartSession(new Vector3(0f, 0f, 20f));
        SessionFor(Frame);
        float full = _intents.LastEnemyMove.Velocity.Length();
        Assert.That(full, Is.EqualTo(_agent.MoveSpeed.Value).Within(1e-4f));
        Assert.That(_enemies.Slow(_agent.Id, 0.5f, 4f), Is.True);
        SessionFor(Frame);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(full * 0.5f).Within(1e-3f));
    }

    // ---- Rule 10 ------------------------------------------------------------------------------------

    [Test]
    public void Phase_DropsRing_ToChase()
    {
        ArmRing();
        _gravemaw.OnPhaseEntered(1);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Tick(1f);
        Assert.That(_events.Count<GravemawErupted>(), Is.Zero, "The dropped ring never bites.");
        Assert.That(_events.Count<GravemawBurrowed>(), Is.EqualTo(2));
        AssertGuard(true);

        Tick(1.5f);
        Tick(1f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
        _gravemaw.OnPhaseEntered(2);
        Tick(0f);
        Assert.That(_events.Of<GravemawWindow>()[1].Open, Is.False, "A dropped window says it closed.");
        AssertGuard(true);
    }

    [Test]
    public void Reset_LiftsInvulnerability()
    {
        ArmRing();
        AssertGuard(true);
        _gravemaw.Reset();
        AssertGuard(false);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Tick(0f);
        Assert.That(_events.Count<GravemawBurrowed>(), Is.EqualTo(2));
    }

    // ---- Rule 11 ------------------------------------------------------------------------------------

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        AllocationAssert.None(() =>
        {
            _gravemaw.Reset();
            float now = 0f;
            for (int i = 0; i < 80; i++)
            {
                now += 0.125f;
                Place(new Vector3(i * 0.1f, 0f, 3f));
                _intents.Clear();
                _gravemaw.Tick(new EnemyTickContext(0.125f, now, player, _intents, silent, projectiles, _enemies));
            }

            _gravemaw.OnPhaseEntered(1);
        }, 100);
    }

    private void ArmRing()
    {
        Tick(0f);
        Tick(2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
    }

    private void Surface()
    {
        ArmRing();
        Tick(1f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
    }

    private void Run(Vector3 start, Vector3 velocity, float seconds, bool first = true)
    {
        float from = _now;
        if (first)
        {
            Place(start);
            Tick(0f);
        }

        int steps = (int)(seconds / Step);
        for (int i = 1; i <= steps; i++)
        {
            Place(start + (velocity * (_now + Step - from)));
            Tick(Step);
        }
    }

    private void AssertGuard(bool guarded)
    {
        Assert.That(_agent.IsVulnerable, Is.EqualTo(!guarded));
        Assert.That(_agent.Health.IsInvulnerable, Is.EqualTo(guarded));
        float before = _agent.Health.Current;
        _agent.Health.ApplyDamage(1f, _now);
        Assert.That(_agent.Health.Current, Is.EqualTo(before - (guarded ? 0f : 1f)));
    }

    private static void AssertNear(Vector3 actual, Vector3 expected, float tolerance) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThanOrEqualTo(tolerance),
            $"{actual} is not within {tolerance} of {expected}.");

    private void Tick(float dt)
    {
        _now += dt;
        _intents.Clear();
        _gravemaw.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies));
    }

    private void Place(Vector3 player)
    {
        EnemyBlackboard board = _agent.Blackboard;
        var flat = new Vector2(player.X, player.Z);
        board.SelfPosition = Vector3.Zero;
        board.PlayerPosition = player;
        board.DistanceToPlayer = flat.Length();
        board.DirectionToPlayer = flat.LengthSquared() > 0f ? Vector2.Normalize(flat) : Vector2.Zero;
        board.PathDirectionToPlayer = board.DirectionToPlayer;
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    /// <summary>A real session, so stun, root and slow reach the boss through <c>EnemySystem</c>.</summary>
    private void StartSession(Vector3 player)
    {
        var random = new FixedRandom(0.5f);
        _session = new RunSession(Catalog(), random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), 8, 8, 8);
        _session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        _enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_session.State);
        _agent = _enemies.SpawnBoss(new ContentId("boss.gravemaw"), Vector3.Zero);
        _gravemaw = (GravemawBehaviour)((BossBehaviour)_agent.Behaviour).Inner;
        _bossAt = Vector3.Zero;
        _playerAt = player;
    }

    /// <summary>The body is held still while it chases, so only the tunnel moves it.</summary>
    private void SessionTick()
    {
        var snapshot = new WorldSnapshot(8) { Dt = Frame, PlayerPosition = _playerAt };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = _agent.Id;
        sense.Position = _bossAt;
        sense.PathDirectionToPlayer = Vector2.Normalize(new Vector2(_playerAt.X - _bossAt.X, _playerAt.Z - _bossAt.Z));
        sense.HasLineOfSight = true;
        bool tunnelling = _gravemaw.State == GravemawState.Tunnel;
        _intents.Clear();
        _session.Tick(snapshot);
        if (tunnelling)
        {
            _bossAt += _intents.LastEnemyMove.Velocity * Frame;
        }
    }

    private void SessionFor(float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);
        for (int i = 0; i < frames; i++)
        {
            SessionTick();
        }
    }

    private void SessionUntil(Func<bool> done, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);
        for (int i = 0; i < frames && !done(); i++)
        {
            SessionTick();
        }

        Assert.That(done(), Is.True, "The awaited state never came.");
    }

    private static ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.gravemaw"), Body().Id,
            new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.gravemaw"), new LocKey("enemy.gravemaw.name"),
        3000f, Speed, 8, 40, 300f, false, Contact, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
