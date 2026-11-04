using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-30c's pack, hunt, chained lanes and second landing.</summary>
[TestFixture]
public sealed class ApexPhaseTests
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
    private BossBehaviour _boss;
    private Vector3 _body;
    private Vector3 _playerAt;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _random = new FixedRandom(0.5f);
        _enemies = new EnemySystem(Catalog(), _events, _random, new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        _lanes = new LaneSystem(_events);
        _stats = new ApexStats();
        _apex = new ApexBehaviour(_agent, _stats, _random.Misc, _lanes);
        _boss = null;
        _body = Vector3.Zero;
        _playerAt = Vector3.UnitZ * 8f;
        _now = 0f;
    }

    [Test]
    public void Phase1_DashCooldownThree()
    {
        _stats.PounceMinRange.Base = 20f;
        _apex.OnPhaseEntered(1);
        Assert.That(_apex.Phase, Is.EqualTo(1));
        Assert.That(_apex.IsHunting, Is.False);
        Assert.That(_stats.DashCooldownSeconds.Base, Is.EqualTo(3.5f));
        Assert.That(_stats.DashCooldownSeconds.Value, Is.EqualTo(3f));
        Tick(1f);
        _apex.OnLaneStop(true, 0f);
        Tick(0.71f);
        Assert.That(_apex.IsRecovering, Is.True);
        _apex.OnPhaseEntered(1); // Drop recovery without clearing the actual cooldown.
        _playerAt = _body + Vector3.UnitZ * 100f;
        Tick(2.99f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
    }

    [Test]
    public void Phase1_SpawnsNothingItself()
    {
        _apex.OnPhaseEntered(1);
        Tick(1f);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void Phase2_SpeedsAndGapAndWindow_Tuned()
    {
        _apex.OnPhaseEntered(2);
        _apex.OnPhaseEntered(2);
        Assert.That(_apex.Phase, Is.EqualTo(2));
        Assert.That(_apex.IsHunting, Is.True);
        Assert.That(_stats.RunSpeed.Value, Is.EqualTo(7.15f).Within(1e-5f));
        Assert.That(_stats.DashSpeed.Value, Is.EqualTo(28.6f).Within(1e-5f));
        Assert.That(_stats.MoveGapSeconds.Value, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(_stats.PounceWindowSeconds.Value, Is.EqualTo(0.9f).Within(1e-5f));
        Assert.That(_stats.RunSpeed.ModifierCount, Is.EqualTo(1), "Repeated entry must not stack.");
        Tick(0.59f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(7.15f).Within(1e-5f));
        _playerAt = _body + Vector3.UnitZ * 8f;
        Tick(0.02f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
    }

    [Test]
    public void Reset_RemovesHuntModifiers()
    {
        var other = new object();
        _stats.RunSpeed.Add(new Modifier(ModifierKind.Flat, 2f, other));
        _apex.OnPhaseEntered(1);
        _apex.OnPhaseEntered(2);
        _apex.Reset();
        Assert.That(_apex.Phase, Is.Zero);
        Assert.That(_apex.IsHunting, Is.False);
        Assert.That(_stats.RunSpeed.Value, Is.EqualTo(7.5f));
        Assert.That(_stats.RunSpeed.ModifierCount, Is.EqualTo(1));
        Assert.That(_stats.DashSpeed.Value, Is.EqualTo(22f));
        Assert.That(_stats.MoveGapSeconds.Value, Is.EqualTo(1f));
        Assert.That(_stats.PounceWindowSeconds.Value, Is.EqualTo(1.5f));
        Assert.That(_stats.DashCooldownSeconds.Value, Is.EqualTo(3.5f));
        Assert.That(_stats.DashSpeed.ModifierCount + _stats.MoveGapSeconds.ModifierCount
            + _stats.PounceWindowSeconds.ModifierCount + _stats.DashCooldownSeconds.ModifierCount, Is.Zero);
    }

    [Test]
    public void Hunt_DashChainsThreeLanes_EachFromLastEnd_EachPointSeven()
    {
        _stats.PounceMinRange.Base = 20f;
        _apex.OnPhaseEntered(2);
        Tick(0.61f);
        for (int lane = 0; lane < 3; lane++)
        {
            Vector3 origin = _body;
            Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1));
            LaneProbeIntent probe = _intents.LaneProbes[0];
            Assert.That(probe.From, Is.EqualTo(origin));
            Vector3 direction = Vector3.Normalize(_playerAt - origin);
            Assert.That(Vector3.Distance(probe.Direction, direction), Is.LessThan(1e-5f));
            float armedAt = _now;
            _apex.OnLaneStop(true, 2f);
            Tick(0.1f);
            LaneArmed strip = _events.Of<LaneArmed>()[lane];
            Assert.That(strip.Origin, Is.EqualTo(origin));
            Assert.That(strip.Length, Is.EqualTo(2f));
            Assert.That(_now - armedAt + strip.ArmSeconds, Is.EqualTo(0.7f).Within(1e-5f));
            Tick(0.59f);
            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
            Tick(0.02f);
            float firedAt = _now;
            Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.EqualTo(28.6f).Within(1e-5f));
            _body = origin + direction * 2f;
            _playerAt = _body + (lane % 2 == 0 ? Vector3.UnitX : -Vector3.UnitZ) * 8f;
            Tick(0.01f);
            if (lane < 2)
            {
                Assert.That(_apex.IsRecovering, Is.False);
                Until(() => _intents.LaneProbes.Count == 1);
                Assert.That(_now - firedAt, Is.EqualTo(0.25f).Within(Frame));
                Assert.That(_events.Of<EnemyTelegraph>()[lane + 1].Duration, Is.EqualTo(0.7f));
            }
        }

        Assert.That(_events.Count<LaneArmed>(), Is.EqualTo(3));
        Assert.That(_apex.IsRecovering, Is.True);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.None));
    }

    [TestCase(3f)]
    [TestCase(100f)]
    public void Hunt_NeverMoreThanThreeLanes(float requested)
    {
        _stats.PounceMinRange.Base = 20f;
        _stats.HuntChainLanes.Base = requested;
        _apex.OnPhaseEntered(2);
        Tick(0.61f);
        // Deliberately leave lanes unticked so all three stay live.
        Until(() => _apex.IsRecovering, tickLanes: false);
        Assert.That(_events.Count<LaneArmed>(), Is.EqualTo(3));
        Assert.That(_lanes.ActiveCount, Is.EqualTo(3));
        Assert.That(_lanes.ActiveCount, Is.LessThanOrEqualTo(LaneSystem.Capacity));
        _apex.OnPhaseEntered(2);
        _playerAt = _body + Vector3.UnitZ * 8f;
        Tick(4f, false);
        Tick(0.1f, false);
        Assert.That(_lanes.ActiveCount, Is.EqualTo(3));
        Assert.That(_intents.LaneProbes, Is.Empty, "Wait for room before promising another strip.");
        _lanes.Clear();
        Tick(Frame);
        Assert.That(_intents.LaneProbes.Count, Is.EqualTo(1));
        Assert.That(_events.Of<EnemyTelegraph>()[3].Duration, Is.EqualTo(0.7f));
    }

    [Test]
    public void Hunt_PounceLands_CrouchesAgainPointSix_WindowAfterSecond()
    {
        _apex.OnPhaseEntered(2);
        Tick(0.61f);
        Until(() => _apex.IsAirborne);
        _playerAt = new Vector3(8f, 0f, 8f);
        Until(() => _events.Count<ApexLanded>() == 1);
        Assert.That(_apex.InWindow, Is.False);
        Assert.That(_apex.IsAirborne, Is.False);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Pounce));
        Assert.That(_events.Of<ApexLanded>()[0].WindowSeconds, Is.Zero);
        Assert.That(_events.Of<ApexCrouched>()[1].Seconds, Is.EqualTo(0.6f));
        Assert.That(_events.Of<EnemyLeapAimed>()[1].Target, Is.EqualTo(_playerAt));
        Assert.That(_events.Of<EnemyLeapAimed>()[1].From.Z, Is.EqualTo(8f).Within(1e-4f));
        Vector3 secondRing = _playerAt;
        _playerAt = secondRing + Vector3.UnitX * 10f;
        Tick(0.59f);
        Assert.That(_apex.IsAirborne, Is.False);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Until(() => _apex.InWindow);
        Assert.That(Vector3.Distance(_body, secondRing), Is.LessThan(1e-4f));
        Assert.That(_events.Of<ApexLanded>()[1].WindowSeconds, Is.EqualTo(0.9f).Within(1e-5f));
        Assert.That(_random.Capture().Misc, Is.EqualTo(1), "Second crouch does not roll a new pick.");
        Tick(0.89f);
        Assert.That(_apex.InWindow, Is.True);
        Tick(0.02f);
        Assert.That(_apex.InWindow, Is.False);
        Assert.That(_events.Count<ApexLanded>(), Is.EqualTo(2));
    }

    [Test]
    public void Hunt_FeintNotDoubled()
    {
        _stats.FeintChance.Base = 1f;
        _apex.OnPhaseEntered(2);
        Tick(0.61f);
        Until(() => _apex.InWindow);
        _apex.OnPhaseEntered(2);
        _playerAt = _body + Vector3.UnitZ * 8f;
        _events.Clear();
        Tick(8f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Tick(0.81f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        Assert.That(_events.Count<ApexCrouched>(), Is.EqualTo(1));
        Assert.That(_events.Single<ApexCrouched>().IsFeint, Is.True);
        Assert.That(_events.Count<EnemyLeapAimed>(), Is.Zero);
        Assert.That(_events.Count<ApexLanded>(), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Telegraphs_AtLeastPointSix_InEveryPhase(int phase)
    {
        _stats.PounceCrouchSeconds.Base = 0.1f;
        _stats.FeintCrouchSeconds.Base = 0.1f;
        _stats.HuntSecondCrouchSeconds.Base = 0.1f;
        _stats.DashTelegraphSeconds.Base = 0.1f;
        _stats.RakeWindupSeconds.Base = 0.1f;
        _stats.RakeSecondSeconds.Base = 0.1f;
        _stats.FeintChance.Base = 1f;
        _apex.OnPhaseEntered(phase);
        Tick(1f);
        Until(() => _apex.InWindow);
        _apex.OnPhaseEntered(phase);
        _playerAt = _body + Vector3.UnitZ * 8f;
        Tick(8f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Feint));
        Tick(0.61f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Dash));
        _apex.OnPhaseEntered(phase);
        _playerAt = _body + Vector3.UnitZ * 2f;
        Tick(1f);
        Assert.That(_apex.Move, Is.EqualTo(ApexMove.Rake));
        foreach (EnemyTelegraph tell in _events.Of<EnemyTelegraph>())
        {
            Assert.That(tell.Duration, Is.GreaterThanOrEqualTo(0.6f));
        }

        foreach (EnemyLeapAimed ring in _events.Of<EnemyLeapAimed>())
        {
            Assert.That(ring.CrouchSeconds, Is.GreaterThanOrEqualTo(0.6f));
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(phase == 2 ? 5 : 4));
    }

    [Test]
    public void PhaseEntry_WhileAirborne_AppliesHuntAndFinishesPromisedLanding()
    {
        UseBossPhases();
        Tick(1f);
        Until(() => _apex.IsAirborne);
        _enemies.ApplyDamage(_agent.Id, 2200f, _now, _player);
        Tick(Frame);
        Assert.That(_boss.IsInBeat, Is.True);
        Assert.That(_apex.IsAirborne, Is.True);
        Assert.That(_apex.IsHunting, Is.True);
        Assert.That(_stats.RunSpeed.Value, Is.EqualTo(7.15f).Within(1e-5f));
        Until(() => _apex.InWindow);
        Assert.That(_boss.IsInBeat, Is.True);
        Assert.That(_events.Count<ApexLanded>(), Is.EqualTo(1), "Finish the promised pre-hunt pounce through the beat.");
        Assert.That(_events.Single<ApexLanded>().WindowSeconds, Is.EqualTo(0.9f).Within(1e-5f));
    }

    [Test]
    public void BossThresholds_EnterPackThenHunt_WithoutAuthoredSummons()
    {
        UseBossPhases();
        _enemies.ApplyDamage(_agent.Id, 1500f, _now, _player);
        Tick(Frame);
        Assert.That(_boss.Phase, Is.EqualTo(1));
        Assert.That(_apex.Phase, Is.EqualTo(1));
        Assert.That(_stats.DashCooldownSeconds.Value, Is.EqualTo(3f));
        Assert.That(_boss.AddCount, Is.Zero);
        Tick(1.51f);
        _enemies.ApplyDamage(_agent.Id, 800f, _now, _player);
        Tick(Frame);
        Assert.That(_boss.Phase, Is.EqualTo(2));
        Assert.That(_apex.IsHunting, Is.True);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
    }

    [Test]
    public void Hunt_TicksAndPhaseModifiers_DoNotAllocate()
    {
        var silent = new SilentEvents();
        var lanes = new LaneSystem(silent);
        var apex = new ApexBehaviour(_agent, new ApexStats(), _random.Misc, lanes);
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var shots = new ProjectileSystem(silent, 8);
        _agent.Blackboard.SelfPosition = Vector3.Zero;
        _agent.Blackboard.PlayerPosition = Vector3.UnitZ * 8f;
        _agent.Blackboard.DirectionToPlayer = Vector2.UnitY;
        _agent.Blackboard.DistanceToPlayer = 8f;
        AllocationAssert.None(() =>
        {
            apex.Reset();
            apex.OnPhaseEntered(1);
            apex.OnPhaseEntered(2);
            _intents.Clear();
            _agent.Blackboard.DistanceToPlayer = 8f;
            for (int i = 1; i <= 256; i++)
            {
                apex.Tick(new EnemyTickContext(Frame, i * Frame, player, _intents, silent, shots, _enemies, lanes));
            }

            apex.OnPhaseEntered(2);
            _agent.Blackboard.DistanceToPlayer = 20f;
            for (int i = 257; i <= 768; i++)
            {
                apex.Tick(new EnemyTickContext(Frame, i * Frame, player, _intents, silent, shots, _enemies, lanes));
                lanes.Tick(i * Frame, Vector3.UnitX * 100f, player);
            }

            lanes.Clear();
        }, 100);
    }

    private void UseBossPhases()
    {
        var spec = new BossSpec(new ContentId("boss.apex"), Body().Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.66f), new BossPhaseSpec(0.33f) }, 1.5f);
        _boss = new BossBehaviour(_agent, spec, _apex);
    }

    private void Until(Func<bool> reached, bool tickLanes = true)
    {
        for (int i = 0; i < 512 && !reached(); i++)
        {
            Tick(Frame, tickLanes);
        }

        Assert.That(reached(), Is.True, "State not reached within eight seconds.");
    }

    private void Tick(float dt, bool tickLanes = true)
    {
        _now += dt;
        Vector3 delta = _playerAt - _body;
        var flat = new Vector2(delta.X, delta.Z);
        _agent.Blackboard.SelfPosition = _body;
        _agent.Blackboard.PlayerPosition = _playerAt;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _intents.Clear();
        var context = new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, _lanes);
        if (_boss is null)
        {
            _apex.Tick(context);
        }
        else
        {
            _boss.Tick(context);
        }
        _body += _intents.LastEnemyMove.Velocity * dt;
        if (tickLanes)
        {
            _lanes.Tick(_now, Vector3.UnitX * 100f, _player);
        }
    }

    private static ContentCatalog Catalog() => new(new[] { Character() }, new[] { Body() });

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
