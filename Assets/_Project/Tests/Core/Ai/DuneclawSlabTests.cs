using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class DuneclawSlabTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private DuneclawStats _stats;
    private DuneclawBehaviour _boss;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _ground = Array.Empty<Vector3>();
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body() });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _agent = _enemies.Registry.Spawn(Body(), Vector3.Zero);
        _stats = new DuneclawStats();
        _boss = new DuneclawBehaviour(_agent, _stats);
        _now = 0f;
        Place(Vector3.UnitZ * 2f);
    }

    private Vector3[] _ground = Array.Empty<Vector3>();

    [Test]
    public void Slabs_UseOpenGroundOnce_WithSpacingAndStableTies()
    {
        Tick(0f); // Capture initial facing before the sense arrives.
        _ground = new[] { new Vector3(-3.5f, 8f, 0.5f), new Vector3(-3.5f, 0f, -0.5f),
            Vector3.Zero, Vector3.UnitZ * 2f, new Vector3(3.5f, 0f, 0f) };
        Tick(0f);
        var placed = _events.Of<DuneclawSlabPlaced>().ToArray();
        Assert.That(placed.Length, Is.EqualTo(2));
        Assert.That(placed[0].Position, Is.EqualTo(_ground[0]));
        Assert.That(placed[1].Position, Is.EqualTo(_ground[4]));
        Assert.That(placed[0].Slot, Is.Zero);
        Assert.That(placed[1].Slot, Is.EqualTo(1));
        _ground = new[] { new Vector3(-3.5f, 0, 0) };
        Tick(0f);
        Assert.That(_events.Count<DuneclawSlabPlaced>(), Is.EqualTo(2));
        _boss.Reset();
        _events.Clear();
        Tick(0f);
        Assert.That(_events.Count<DuneclawSlabPlaced>(), Is.EqualTo(1));
        _boss.Reset();
        _events.Clear();
        _ground = new[] { Vector3.Zero, Vector3.UnitZ * 2, new Vector3(-2f, 0, 0), new Vector3(9, 0, 0) };
        Tick(0f);
        Assert.That(_events.Count<DuneclawSlabPlaced>(), Is.Zero);
        _ground = new[] { new Vector3(3.5f, 0, 0) };
        Tick(0f);
        Assert.That(_events.Count<DuneclawSlabPlaced>(), Is.Zero, "Crowded measured placement is final.");
    }

    [TestCase(0f)]
    [TestCase(0.8f)]
    public void Slam_LockedCentreOnSlab_BreaksOnceAndPinsThreeSeconds(float offset)
    {
        SeedSlabs();
        Slam(new Vector3(3.5f, 50f, offset), true);
        Assert.That(_events.Single<DuneclawSlabShattered>().Slot, Is.EqualTo(1));
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(3f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Tick(2.99f);
        Assert.That(_boss.IsRecovering, Is.True);
        Tick(0.02f);
        _boss.OnPhaseEntered(2);
        Slam(new Vector3(3.5f, 0, offset), false);
        Assert.That(_events.Count<DuneclawSlabShattered>(), Is.EqualTo(1));
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(2f));
        Assert.That(_player.Health.Current, Is.EqualTo(84f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slam_EdgeOverlapOrPhaseCancel_PreservesSlab(bool cancel)
    {
        SeedSlabs();
        Vector3 target = new Vector3(3.5f, 0, cancel ? 0 : 0.81f);
        ArmAt(target);
        if (cancel)
            _boss.OnPhaseEntered(2);
        Place(Vector3.UnitZ * 20f);
        Tick(1f);
        Assert.That(_events.Count<DuneclawSlabShattered>(), Is.Zero);
        if (!cancel)
            Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(2f));
        _boss.OnPhaseEntered(2);
        Slam(new Vector3(3.5f, 0, 0), true);
        Assert.That(_events.Count<DuneclawSlabShattered>(), Is.EqualTo(1));
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(3f));
    }

    [Test]
    public void Reset_ClearsSlabsAndPlacementState()
    {
        SeedSlabs();
        Slam(new Vector3(3.5f, 0, 0), true);
        _boss.Reset();
        _events.Clear();
        Place(Vector3.UnitX * 2);
        _ground = new[] { new Vector3(0, 0, 3.5f), new Vector3(0, 0, -3.5f) };
        Tick(0f);
        Assert.That(_events.Count<DuneclawSlabPlaced>(), Is.EqualTo(2));
        Assert.That(_events.Of<DuneclawSlabPlaced>().First().Position, Is.EqualTo(_ground[0]));
        _boss.OnPhaseEntered(2);
        Slam(_ground[0], true);
        Assert.That(_events.Of<DuneclawImpact>().Last().RecoverySeconds, Is.EqualTo(3f));
    }

    [Test]
    public void Slabs_NoTickAllocations()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var ground = new[] { new Vector3(-3.5f, 0, 0), new Vector3(3.5f, 0, 0) };
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            player.Health.Reset();
            _boss.Reset();
            Place(Vector3.UnitZ * 2);
            _boss.Tick(new EnemyTickContext(0f, 0f, player, _intents, silent, _projectiles, _enemies, openGround: ground));
            Place(ground[1]);
            for (int i = 0; i < 12; i++)
                _boss.Tick(new EnemyTickContext(1f, i, player, _intents, silent, _projectiles, _enemies, openGround: ground));
        }, 100);
    }

    private void SeedSlabs()
    {
        _ground = new[] { new Vector3(-3.5f, 0, 0), new Vector3(3.5f, 0, 0) };
        Tick(0f);
        _boss.OnPhaseEntered(0);
    }

    private void ArmAt(Vector3 target)
    {
        Place(Vector3.UnitZ * 2);
        Tick(0f);
        Place(target);
        Tick(1f);
        Tick(_boss.Phase >= 2 ? 0.35f : 0.8f);
        Assert.That(LastArm().Move, Is.EqualTo(DuneclawMove.Slam));
    }

    private void Slam(Vector3 target, bool walkClear)
    {
        ArmAt(target);
        if (walkClear)
            Place(Vector3.UnitZ * 20f);
        Tick(1f);
    }
    private DuneclawArmed LastArm() => _events.Of<DuneclawArmed>().Last();

    private void Tick(float dt)
    {
        _now += dt;
        _boss.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies, openGround: _ground));
    }

    private void Place(Vector3 player)
    {
        _agent.Blackboard.SelfPosition = Vector3.Zero;
        _agent.Blackboard.PlayerPosition = player;
        var flat = new Vector2(player.X, player.Z);
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
    }

    private static EnemySpec Body() => new(new ContentId("enemy.duneclaw"), new LocKey("enemy.duneclaw.name"),
        500f, 1.8f, 8, 40, 300f, false, 12f, 3.2f, 1f, 0.8f, 40f, EnemyBehaviourKind.Static);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        hitIFrames: 0.5f);
}
