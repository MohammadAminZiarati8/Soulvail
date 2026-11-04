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

/// <summary>JG-30b's seven rules, including the session's real roll and phase-beat doors.</summary>
[TestFixture]
public sealed class ApexPounceTests
{
    private const float Frame = 1f / 64f;
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private ApexStats _stats;
    private FixedRandom _random;
    private ApexBehaviour _apex;
    private RunSession _session;
    private Vector3 _body;
    private Vector3 _playerAt;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _random = new FixedRandom(0.9f).SetMisc(0f, 0f, 0f, 0f, 0f);
        _enemies = new EnemySystem(Catalog(), _events, _random, new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        _lanes = new LaneSystem(_events);
        _stats = new ApexStats();
        _apex = new ApexBehaviour(_agent, _stats, _random.Misc, _lanes);
        _session = null;
        _body = Vector3.Zero;
        _playerAt = Vector3.UnitZ * 8f;
        _now = 0f;
    }

    // Rule 1.
    [TestCase(5f)]
    [TestCase(8f)]
    [TestCase(11f)]
    public void Pick_InPounceBand_PouncesBeforeDash(float distance)
    {
        _playerAt = Vector3.UnitZ * distance;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
        Assert.That(_intents.LaneProbes, Is.Empty);
        Assert.That(_events.Single<ApexCrouched>().IsFeint, Is.False);
    }

    [TestCase(4.99f)]
    [TestCase(11.01f)]
    public void Pick_OutsidePounceBand_Dashes(float distance)
    {
        _playerAt = Vector3.UnitZ * distance;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        Assert.That(_events.Count<ApexCrouched>(), Is.Zero);
        Assert.That(_random.Capture().Misc, Is.Zero, "Only a pounce pick draws.");
    }

    [Test]
    public void Feint_NeverFirst_NeverTwiceRunning()
    {
        _stats.FeintChance.Base = 1f;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
        FinishReal();
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Tick(0.81f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
        FinishReal();
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Assert.That(_random.Capture().Misc, Is.EqualTo(4), "Even forced real picks draw once.");
    }

    [TestCase(0.1f, ApexMove.Feint)]
    [TestCase(0.29f, ApexMove.Feint)]
    [TestCase(0.3f, ApexMove.Pounce)]
    [TestCase(0.9f, ApexMove.Pounce)]
    public void Feint_DrawnFromBossStream(float roll, ApexMove expected)
    {
        _random.SetMisc(0f, roll, 0.7f);
        _apex = new ApexBehaviour(_agent, _stats, _random.Misc, _lanes);
        Tick(1f);
        FinishReal();
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(expected));
        var state = _random.Capture();
        Assert.That(state.Misc, Is.EqualTo(2));
        Assert.That(state.Spawn, Is.Zero);
        Assert.That(state.Offers, Is.Zero);
        Assert.That(state.Affixes, Is.Zero);
        Assert.That(state.Drops, Is.Zero);
    }

    // Rules 2 and 3.
    [Test]
    public void Crouch_PublishesCrouchedAimedTelegraph_LandingFixed()
    {
        Tick(1f);
        Assert.That(_events.All[0], Is.TypeOf<ApexCrouched>());
        Assert.That(_events.All[1], Is.TypeOf<EnemyLeapAimed>());
        Assert.That(_events.All[2], Is.TypeOf<EnemyTelegraph>());
        ApexCrouched crouched = _events.Single<ApexCrouched>();
        EnemyLeapAimed aimed = _events.Single<EnemyLeapAimed>();
        EnemyTelegraph tell = _events.Single<EnemyTelegraph>();
        Assert.That(crouched.Id, Is.EqualTo(_agent.Id));
        Assert.That(crouched.IsFeint, Is.False);
        Assert.That(crouched.Seconds, Is.EqualTo(0.8f));
        Assert.That(aimed.Id, Is.EqualTo(_agent.Id));
        Assert.That(aimed.From, Is.EqualTo(Vector3.Zero));
        Assert.That(aimed.Target, Is.EqualTo(Vector3.UnitZ * 8f));
        Assert.That(aimed.Radius, Is.EqualTo(2.2f));
        Assert.That(aimed.CrouchSeconds, Is.EqualTo(0.8f));
        Assert.That(aimed.AirSeconds, Is.EqualTo(0.6f));
        Assert.That(tell.Id, Is.EqualTo(_agent.Id));
        Assert.That(tell.Duration, Is.EqualTo(0.8f));
        Assert.That(tell.Move, Is.EqualTo(3));
        _playerAt = Vector3.UnitX * 20f;
        Tick(0.4f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        FinishReal();
        Assert.That(Vector3.Distance(_body, aimed.Target), Is.LessThan(1e-4f));
        Assert.That(_events.Single<ApexLanded>().At, Is.EqualTo(aimed.Target));
        Assert.That(_events.Count<EnemyLeapAimed>(), Is.EqualTo(1));
    }

    [Test]
    public void Air_FliesStraightForAirSeconds_BitesNothingOnWay()
    {
        Tick(1f);
        _playerAt = Vector3.UnitZ * 4f;
        Until(() => _apex.IsAirborne, 1f);
        float movingSeconds = Frame;
        while (!_apex.InWindow && movingSeconds < 1f)
        {
            Assert.That(_player.Health.Current, Is.EqualTo(100f));
            Tick(Frame);
            if (_apex.IsAirborne)
            {
                movingSeconds += Frame;
                Assert.That(_intents.LastEnemyMove.Velocity.X, Is.Zero);
            }
        }

        Assert.That(_apex.InWindow, Is.True);
        Assert.That(movingSeconds, Is.EqualTo(0.6f).Within(Frame));
        Assert.That(_body.Z, Is.EqualTo(8f).Within(1e-4f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "The player crossed the flight, outside its landing.");
    }

    [Test]
    public void Land_PlayerInRing_Bitten()
    {
        SessionToAir();
        _playerAt += Vector3.UnitX * 2.2f;
        SessionUntil(() => _apex.InWindow, 1f);
        Assert.That(_session.State.PlayerHp, Is.EqualTo(60f));
        PlayerDamaged bite = _events.Single<PlayerDamaged>();
        Assert.That(bite.ToHp, Is.EqualTo(40f));
        Assert.That(bite.Blocked, Is.False);
    }

    [Test]
    public void Land_PlayerOut_Unhurt()
    {
        SessionToAir();
        _playerAt += Vector3.UnitX * 2.21f;
        SessionUntil(() => _apex.InWindow, 1f);
        Assert.That(_session.State.PlayerHp, Is.EqualTo(100f));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Land_RollIFrames_Unhurt()
    {
        SessionToAir();
        for (int i = 0; i < 25; i++)
        {
            SessionTick();
        }

        _session.MovementSkill();
        SessionUntil(() => _apex.InWindow, 0.5f);
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1));
        Assert.That(_events.Single<PlayerDamaged>().Blocked, Is.True,
            "Keep the sensed player inside the ring, so only the actual roll's i-frames can save them.");
        Assert.That(_session.State.PlayerHp, Is.EqualTo(100f));
    }

    // Rule 4.
    [Test]
    public void Land_PublishesLanded_StandsWindow_PicksNothing()
    {
        Tick(1f);
        FinishReal();
        ApexLanded landed = _events.Single<ApexLanded>();
        Assert.That(landed.Id, Is.EqualTo(_agent.Id));
        Assert.That(landed.WindowSeconds, Is.EqualTo(1.5f));
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        _playerAt = _body + Vector3.UnitZ * 2f; // Would rake immediately if a pick escaped the window.
        Tick(1.49f);
        Assert.That(_apex.InWindow, Is.True);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);
        Assert.That(_apex.InWindow, Is.False);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Tick(1.01f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        Assert.That(_events.Count<ApexLanded>(), Is.EqualTo(1));
    }

    [Test]
    public void Cooldown_CountsFromLanding()
    {
        Tick(1f);
        FinishReal();
        _playerAt = _body + Vector3.UnitZ * 3.5f; // Neither rake nor dash can occupy the pick.
        Tick(1.51f);
        Tick(5.48f);
        _playerAt = _body + Vector3.UnitZ * 8f;
        Tick(0f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash), "6.99 s after landing is still on cooldown.");
        _apex.OnPhaseEntered(1);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint), "The landing cooldown has now elapsed.");
    }

    // Rule 5.
    [Test]
    public void Feint_NoAimedEvent_ThenArmsDashAtOnce()
    {
        _stats.DashCooldownSeconds.Base = 100f;
        _stats.PounceCooldownSeconds.Base = 0f;
        _playerAt = Vector3.UnitZ * 4f;
        Tick(1f);
        _apex.OnLaneStop(true, 0f);
        Tick(0.71f); // Dash ends and starts its long cooldown.
        Assert.That(_apex.IsRecovering, Is.True);
        NextPick();
        FinishReal();
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        _events.Clear();
        _intents.Clear();
        Tick(0.79f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        Assert.That(_events.Count<EnemyLeapAimed>(), Is.Zero);
        Assert.That(_events.Count<ApexLanded>(), Is.Zero);
        Assert.That(_events.Single<EnemyTelegraph>().Move, Is.EqualTo(1));
        Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1), "Dash cooldown cannot prevent the follow-up.");
    }

    [Test]
    public void Feint_PublishesRinglessCrouch_RestartsPounceCooldown()
    {
        Tick(1f);
        FinishReal();
        _events.Clear();
        NextPick();
        Assert.That(_events.Single<ApexCrouched>().IsFeint, Is.True);
        Assert.That(_events.Single<ApexCrouched>().Seconds, Is.EqualTo(0.8f));
        Assert.That(_events.Single<EnemyTelegraph>().Move, Is.EqualTo(4));
        Assert.That(_events.Count<EnemyLeapAimed>(), Is.Zero);
        Tick(0.81f);
        _apex.OnPhaseEntered(1);
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash), "The feint restarted the pounce cooldown.");
        NextPick();
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
    }

    // Rule 6.
    [TestCase(false)]
    [TestCase(true)]
    public void PhaseEntry_DropsCrouch_AirborneStillLands(bool airborne)
    {
        StartSession();
        SessionUntil(() => _apex.Move == ApexMove.Pounce, 2f);
        if (airborne)
        {
            SessionUntil(() => _apex.IsAirborne, 1f);
        }

        _enemies.ApplyDamage(_agent.Id, 1500f, _session.State.Time, _player);
        SessionTick();
        var boss = (BossBehaviour)_agent.Behaviour;
        Assert.That(boss.IsInBeat, Is.True, "The real boss phase machine entered its beat.");
        if (airborne)
        {
            SessionUntil(() => _apex.InWindow, 1f);
            Assert.That(boss.IsInBeat, Is.True, "The pounce finished during the phase beat.");
            Assert.That(_events.Single<ApexLanded>().At, Is.EqualTo(Vector3.UnitZ * 8f));
            Assert.That(Vector3.Distance(_body, Vector3.UnitZ * 8f), Is.LessThan(1e-3f));
        }
        else
        {
            Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
            for (int i = 0; i < 80; i++)
            {
                SessionTick();
            }

            Assert.That(_events.Count<ApexLanded>(), Is.Zero);
            Assert.That(_apex.IsAirborne, Is.False);
            Assert.That(_body, Is.EqualTo(Vector3.Zero));
        }
    }

    [Test]
    public void Reset_ClearsWindowAirCooldownAndFeintHistory()
    {
        Tick(1f);
        FinishReal();
        _apex.Reset();
        Assert.That(_apex.InWindow, Is.False);
        _body = Vector3.Zero;
        _playerAt = Vector3.UnitZ * 8f;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce), "First pick after reset is real and off cooldown.");
        Until(() => _apex.IsAirborne, 1f);
        _apex.Reset();
        Assert.That(_apex.IsAirborne, Is.False);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
    }

    // Rule 7.
    [Test]
    public void Telegraphs_AtLeastPointSix()
    {
        _stats.PounceCrouchSeconds.Base = 0.1f;
        _stats.FeintCrouchSeconds.Base = 0.1f;
        _stats.DashTelegraphSeconds.Base = 0.1f;
        Tick(1f);
        Assert.That(_events.Single<EnemyLeapAimed>().CrouchSeconds, Is.EqualTo(0.6f));
        Tick(0.59f);
        Assert.That(_apex.IsAirborne, Is.False);
        FinishReal();
        NextPick();
        Tick(0.59f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        _apex.OnPhaseEntered(1);
        _stats.RakeWindupSeconds.Base = 0.1f;
        _stats.RakeSecondSeconds.Base = 0.1f;
        _playerAt = _body + Vector3.UnitZ * 2f;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        Tick(0.59f);
        Assert.That(_player.Health.Current, Is.EqualTo(60f), "Rake also keeps its minimum warning.");
        foreach (EnemyTelegraph tell in _events.Of<EnemyTelegraph>())
        {
            Assert.That(tell.Duration, Is.GreaterThanOrEqualTo(0.6f));
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(4));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var stats = new ApexStats();
        stats.FeintChance.Base = 1f;
        stats.PounceCooldownSeconds.Base = 0f;
        var lanes = new LaneSystem(silent);
        var apex = new ApexBehaviour(_agent, stats, new FixedRandom().Misc, lanes);
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var shots = new ProjectileSystem(silent, 8);
        AllocationAssert.None(() =>
        {
            apex.Reset();
            _intents.Clear();
            Place(Vector3.Zero, Vector3.UnitZ * 8f);
            apex.Tick(new EnemyTickContext(1f, 1f, player, _intents, silent, shots, _enemies));
            for (int i = 1; i <= 100; i++)
            {
                apex.Tick(new EnemyTickContext(Frame, 1f + i * Frame, player, _intents, silent, shots, _enemies));
            }

            apex.OnPhaseEntered(1);
            apex.Tick(new EnemyTickContext(1f, 4f, player, _intents, silent, shots, _enemies));
            apex.Tick(new EnemyTickContext(0.81f, 4.81f, player, _intents, silent, shots, _enemies));
            apex.OnLaneStop(false, 0f);
            apex.Tick(new EnemyTickContext(0.1f, 4.91f, player, _intents, silent, shots, _enemies));
            lanes.Clear();
        }, 100);
    }

    private void FinishReal() => Until(() => _apex.InWindow, 2f);

    private void NextPick()
    {
        _apex.OnPhaseEntered(1);
        _body = Vector3.Zero;
        _playerAt = Vector3.UnitZ * 8f;
        Tick(8f);
    }

    private void Until(Func<bool> arrived, float budget)
    {
        for (int i = 0; i < (int)MathF.Ceiling(budget / Frame) && !arrived(); i++)
        {
            Tick(Frame);
        }

        Assert.That(arrived(), Is.True, "State not reached within budget.");
    }

    private void Tick(float dt)
    {
        _now += dt;
        Place(_body, _playerAt);
        _intents.Clear();
        _apex.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes));
        // This fixture integrates flight only; hunt/dash movement is covered by JG-30a's fixtures.
        if (_apex.IsAirborne)
        {
            _body += _intents.LastEnemyMove.Velocity * dt;
        }
    }

    private void Place(Vector3 self, Vector3 player)
    {
        Vector3 delta = player - self;
        var flat = new Vector2(delta.X, delta.Z);
        _agent.Blackboard.SelfPosition = self;
        _agent.Blackboard.PlayerPosition = player;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
    }

    private void SessionToAir()
    {
        StartSession();
        SessionUntil(() => _apex.IsAirborne, 3f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    private void StartSession()
    {
        _session = new RunSession(Catalog(), _random, _events, _intents,
            new RunRecorder(_random, new FixedClock(default), _events), 8, 8, 8);
        _session.Start(new RunConfig(new ContentId("mode.test"), Character().Id, 0, 1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()), restore: null));
        _enemies = (EnemySystem)typeof(RunState).GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_session.State);
        _agent = _enemies.SpawnBoss(new ContentId("boss.apex"), Vector3.Zero);
        _apex = (ApexBehaviour)((BossBehaviour)_agent.Behaviour).Inner;
        _body = Vector3.Zero;
        _playerAt = Vector3.UnitZ * 8f;
        _events.Clear();
        SessionTick(1f); // Spend the opening move gap with a stationary sensed body.
    }

    private void SessionUntil(Func<bool> arrived, float budget)
    {
        for (int i = 0; i < (int)MathF.Ceiling(budget / Frame) && !arrived(); i++)
        {
            SessionTick();
        }

        Assert.That(arrived(), Is.True, "Session state not reached within budget.");
    }

    private void SessionTick(float dt = Frame)
    {
        var snapshot = new WorldSnapshot(8) { Dt = dt, PlayerPosition = _playerAt };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = _agent.Id;
        sense.Position = _body;
        sense.PathDirectionToPlayer = Vector2.UnitY;
        sense.HasLineOfSight = true;
        _intents.Clear();
        _session.Tick(snapshot);
        _body += _intents.LastEnemyMove.Velocity * dt;
    }

    private static ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.apex"), Body().Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.apex"), new LocKey("enemy.apex.name"),
        3200f, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 5f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f));
}
