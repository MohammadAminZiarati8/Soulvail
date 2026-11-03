using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-25b: the eruption throws a bitten player straight out to 1 m past the ring.</summary>
/// <remarks>
/// The boss stands at the origin and the quarry stands still at (0, 0, 3), so the ring arms on
/// (0, 0, 3) and every row moves the player to where the bite should find them.
/// </remarks>
[TestFixture]
public sealed class GravemawThrowTests
{
    private const float Contact = 20f;
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly Vector3 Centre = new(0f, 0f, 3f);

    /// <summary>Arm, then erupt: the chase, the warning.</summary>
    private static readonly float[] Cycle = { 0f, 2.5f, 1f };

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private PlayerCombat _player;
    private PlayerMotor _motor;
    private ProjectileSystem _projectiles;
    private GravemawBehaviour _gravemaw;
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
        _motor = new PlayerMotor(Character().Movement, Vector3.UnitZ);
        _projectiles = new ProjectileSystem(_events, 8);
        _gravemaw = new GravemawBehaviour(_agent, new GravemawStats(), new FixedRandom(0.5f).Misc, _motor,
            new ShockwaveSystem(_events), Frog);
        _now = 0f;
        Place(Centre);
        Tick(0f);
        Tick(2.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Assert.That(_gravemaw.RingCentre, Is.EqualTo(Centre));
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void Throw_Hit_EmitsIntentOutward()
    {
        Place(Centre + new Vector3(2f, 0f, 0f));
        Erupt();
        Assert.That(_intents.Throws.Count, Is.EqualTo(1));
        PlayerThrowIntent thrown = _intents.Throws[0];
        Assert.That(thrown.DirectionXZ, Is.EqualTo(new Vector2(1f, 0f)));
        Assert.That(thrown.Duration, Is.EqualTo(0.25f));
    }

    [Test]
    public void Throw_KillingBite_None()
    {
        _player.Health.ApplyDamage(100f - (Contact * 1.5f) + 1f, -10f);
        Place(Centre + new Vector3(1f, 0f, 0f));
        Erupt();
        Assert.That(_player.Health.IsDead, Is.True);
        Assert.That(_intents.Throws.Count, Is.Zero);
        Assert.That(_motor.IsHeld, Is.False);
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Throw_Direction_CentreToPlayer()
    {
        Place(Centre + new Vector3(-1f, 0f, -1f));
        Erupt();
        Vector2 direction = _intents.Throws[0].DirectionXZ;
        Assert.That(direction.X, Is.EqualTo(-MathF.Sqrt(0.5f)).Within(1e-5f));
        Assert.That(direction.Y, Is.EqualTo(-MathF.Sqrt(0.5f)).Within(1e-5f));
    }

    [Test]
    public void Throw_AtCentre_AgainstFacing()
    {
        Erupt(new Vector2(0f, 2f));
        PlayerThrowIntent thrown = _intents.Throws[0];
        Assert.That(thrown.DirectionXZ, Is.EqualTo(new Vector2(0f, -1f)));
        Assert.That(thrown.Distance, Is.EqualTo(4f));
    }

    [Test]
    public void Throw_AtCentre_NoFacing_PlusZ()
    {
        Erupt(Vector2.Zero);
        Assert.That(_intents.Throws[0].DirectionXZ, Is.EqualTo(new Vector2(0f, 1f)));
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void Throw_Distance_LandsMarginOutside()
    {
        Place(Centre + new Vector3(0f, 0f, 2.5f));
        Erupt();
        PlayerThrowIntent thrown = _intents.Throws[0];
        Assert.That(thrown.Distance, Is.EqualTo(1.5f).Within(1e-5f));
        Vector2 landing = new Vector2(0f, 2.5f) + (thrown.DirectionXZ * thrown.Distance);
        Assert.That(landing.Length(), Is.EqualTo(3f + 1f).Within(1e-5f));
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Throw_HoldsMotor_NotCombat()
    {
        Place(Centre + new Vector3(1f, 0f, 0f));
        Erupt();
        Assert.That(_motor.IsHeld, Is.True);
        Assert.That(_player.IsHeld, Is.False, "Firing runs: the combat is not held.");
        _motor.Tick(0.125f, new Vector2(1f, 0f), null);
        Assert.That(_motor.Velocity, Is.EqualTo(Vector3.Zero), "The stick does not argue with the throw.");
        _motor.Tick(0.125f, new Vector2(1f, 0f), null);
        Assert.That(_motor.IsHeld, Is.False);
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Throw_Miss_None()
    {
        Place(Centre + new Vector3(3.01f, 0f, 0f));
        Erupt();
        AssertNoThrow();
    }

    [Test]
    public void Throw_Decoy_None()
    {
        _agent.Blackboard.QuarryIsADecoy = true;
        Erupt();
        AssertNoThrow();
    }

    [Test]
    public void Throw_IFrames_None()
    {
        _player.Health.SetExternalInvulnerable(true);
        Place(Centre + new Vector3(1f, 0f, 0f));
        Erupt();
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        AssertNoThrow();
    }

    [Test]
    public void Throw_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        Place(Centre + new Vector3(1f, 0f, 0f));
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            _motor.ClearHold();
            _gravemaw.Reset();
            foreach (float dt in Cycle)
            {
                _now += dt;
                _gravemaw.Tick(new EnemyTickContext(dt, _now, player, _intents, silent, _projectiles, _enemies));
            }
        }, 2);
        Assert.That(_intents.Throws.Count, Is.EqualTo(1), "The probe never reached the throw.");
        Assert.That(player.Health.IsDead, Is.False);
    }

    private void AssertNoThrow()
    {
        Assert.That(_intents.Throws.Count, Is.Zero);
        Assert.That(_motor.IsHeld, Is.False);
    }

    private void Erupt(Vector2 facing = default)
    {
        _now += 1f;
        _intents.Clear();
        _gravemaw.Tick(new EnemyTickContext(1f, _now, _player, _intents, _events, _projectiles, _enemies,
            playerFacing: facing));
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
    }

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

    private static ContentCatalog Catalog()
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var boss = new BossSpec(new ContentId("boss.gravemaw"), Body().Id,
            new[] { new BossPhaseSpec(1f) }, 1.5f);
        return new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode }, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.gravemaw"), new LocKey("enemy.gravemaw.name"),
        3000f, 4f, 8, 40, 300f, false, Contact, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
