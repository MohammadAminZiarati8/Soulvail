using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class ShadeSystemTests
{
    internal static readonly ContentId EnemyId = new("enemy.husk");
    private const int ProjectileCapacity = 8;
    private RecordingEvents _events;
    private ProjectileSystem _projectiles;
    private ShadeSystem _shades;
    private EnemySystem _enemies;
    private PlayerCombat _player;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [Test]
    public void Spawn_PublishesAppeared()
    {
        Vector3 position = Vector3.UnitX;
        int id = _shades.Spawn(ShadeKind.Decoy, position, 2f, 6f, 0f);

        ShadeAppeared appeared = _events.Single<ShadeAppeared>();
        Assert.That(id, Is.EqualTo(1));
        Assert.That(appeared.Id, Is.EqualTo(id));
        Assert.That(appeared.Kind, Is.EqualTo(ShadeKind.Decoy));
        Assert.That(appeared.Position, Is.EqualTo(position));
        Assert.That(appeared.ExpiresAt, Is.EqualTo(8f));
        Assert.That(_shades.Standing.Length, Is.EqualTo(1));
    }

    [Test]
    public void Spawn_Full_OldestLeavesNotExpired()
    {
        for (int i = 0; i <= ShadeSystem.Capacity; i++)
        {
            _shades.Spawn(ShadeKind.Image, i * Vector3.UnitX, i, 10f, 0.5f);
        }

        ShadeFaded faded = _events.Single<ShadeFaded>();
        Assert.That(faded.Id, Is.EqualTo(1));
        Assert.That(faded.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(faded.Expired, Is.False);
        Assert.That(_shades.Standing.Length, Is.EqualTo(ShadeSystem.Capacity));
        Assert.That(_shades.Standing[0].Id, Is.EqualTo(2));
        Assert.That(_events.All[^2], Is.TypeOf<ShadeFaded>());
    }

    [Test]
    public void Expiry_PublishesFadedExpired()
    {
        _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 1f, 0.5f);
        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);

        _shades.Tick(1f, _enemies, _player);

        Assert.That(_events.Single<ShadeFaded>().Expired, Is.True);
        Assert.That(_events.Count<ShadeLoosed>(), Is.Zero);
        Assert.That(_shades.Standing.Length, Is.Zero);
    }

    [TestCase(ShadeKind.Image, TestName = "Image_ShootsNearest_AtFireRate_ShareOfWeapon")]
    [TestCase(ShadeKind.Hall, TestName = "Hall_ShootsNearest_AtFireRate_ShareOfWeapon")]
    public void ShootingShade_FiresAtCadence(ShadeKind kind)
    {
        _enemies.Spawn(EnemyId, 7f * Vector3.UnitZ);
        EnemyAgent near = _enemies.Spawn(EnemyId, 2f * Vector3.UnitZ);
        _player.Weapon.FireRate.Base = 2f;
        _player.Weapon.Damage.Base = 20f;
        _player.HitAndRun.MomentumPerMetre.Base = 0.02f;
        _player.HitAndRun.OnMoved(3f, 0f);
        int id = _shades.Spawn(kind, Vector3.Zero, 0f, 2f, 0.5f);

        _shades.Tick(0f, _enemies, _player);
        ProjectileFired arrow = _events.Single<ProjectileFired>();
        ShadeLoosed loosed = _events.Single<ShadeLoosed>();
        Assert.That(loosed.Id, Is.EqualTo(id));
        Assert.That(loosed.Target, Is.EqualTo(near.Position));
        Assert.That(arrow.Target, Is.EqualTo(near.Position));
        Assert.That(arrow.ShotOrigin, Is.EqualTo(ShotOrigin.Shade));
        _projectiles.Tick(0.25f, Vector3.Zero, _player, _enemies);
        Assert.That(_events.Single<EnemyDamaged>().Amount, Is.EqualTo(10f));

        _shades.Tick(0.49f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.EqualTo(1));
        _shades.Tick(0.5f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.EqualTo(2));
    }

    [TestCase(ShadeKind.Decoy, TestName = "Decoy_NeverShoots")]
    [TestCase(ShadeKind.Crossfire, TestName = "Crossfire_NeverShoots")]
    public void SilentShade_NeverShoots(ShadeKind kind)
    {
        _shades.Spawn(kind, Vector3.Zero, 0f, 1f, 0.5f);
        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);

        _shades.Tick(0f, _enemies, _player);

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Count<ShadeLoosed>(), Is.Zero);
    }

    [Test]
    public void NoTarget_Waits()
    {
        _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 1f, 0.5f);
        _shades.Tick(0f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.Zero);

        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);
        _shades.Tick(0.01f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.EqualTo(1));
    }

    [Test]
    public void FullSky_Waits()
    {
        _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 1f, 0.5f);
        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);
        var shot = new Projectile(EnemyId, 1, Vector3.Zero, Vector3.UnitX, 25f, 0.1f, 0f);
        for (int i = 0; i < ProjectileCapacity; i++)
        {
            _projectiles.Fire(shot, 0f);
        }

        _shades.Tick(0f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.Zero);
        _projectiles.Clear();
        _shades.Tick(0.01f, _enemies, _player);
        Assert.That(_events.Count<ShadeLoosed>(), Is.EqualTo(1));
    }

    [Test]
    public void ShadeHit_NoHitAndRunBurst()
    {
        _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 1f, 0.5f);
        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);
        _player.HitAndRun.HitSpeedBurst.Base = 0.2f;

        _shades.Tick(0f, _enemies, _player);
        _projectiles.Tick(0.5f, Vector3.Zero, _player, _enemies);

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(1));
        Assert.That(_player.HitAndRun.SpeedBonus(0.5f), Is.Zero);
    }

    [Test]
    public void Clear_AllLeaveNotExpired()
    {
        _shades.Spawn(ShadeKind.Decoy, Vector3.UnitX, 0f, 1f, 0f);
        _shades.Spawn(ShadeKind.Hall, Vector3.UnitZ, 0f, 3f, 0.5f);
        _shades.Clear();

        Assert.That(_shades.Standing.Length, Is.Zero);
        Assert.That(_events.Of<ShadeFaded>().Select(fade => fade.Id), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(_events.Of<ShadeFaded>().Any(fade => fade.Expired), Is.False);
        Assert.That(_shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 6f, 0.5f), Is.EqualTo(3));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        _enemies.Spawn(EnemyId, 3f * Vector3.UnitZ);
        AllocationAssert.None(() =>
        {
            _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 1f, 0.5f);
            _shades.Tick(0f, _enemies, _player);
            _shades.Tick(1f, _enemies, _player);
            _projectiles.Clear();
        });
    }

    private void Initialise(IDomainEvents events)
    {
        CharacterSpec character = Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { Body() });
        _player = new PlayerCombat(character, events, new RecordingIntents(), ProjectileCapacity);
        var stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        _enemies = new EnemySystem(catalog, events, new FixedRandom(7), new DepthScaling(Scalings.Design()), ProjectileCapacity);
        _projectiles = new ProjectileSystem(events, ProjectileCapacity);
        _shades = new ShadeSystem(_projectiles, new LureSystem(events), stats, character, events);
    }

    internal static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    internal static EnemySpec Body(float hp = 1000f) => new(EnemyId, new LocKey("enemy.husk.name"),
        hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
}
