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

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class CrossfireTests
{
    private const int Capacity = 8;
    private const float WeaponDamage = 20f;
    private static readonly ContentId SkillId = new("skill.echo-archer.crossfire");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private RecordingEvents _events;
    private EnemySystem _enemies;
    private ProjectileSystem _projectiles;
    private ShadeSystem _shades;
    private PlayerCombat _player;
    private EffectRegistry _effects;
    private SkillRunner _runner;
    private SimulatedClock _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    [Test]
    public void Cast_PicksCurrentTarget_ElseNearest()
    {
        EnemyAgent nearest = Spawn(0f, 3f);
        Spawn(0f, 5f);
        EnemyAgent current = Spawn(0f, 8f);
        _player.Blackboard.CurrentTargetId = current.Id;

        Assert.That(Cast(), Is.True);
        Assert.That(_events.Of<ProjectileFired>().Select(shot => shot.Target), Is.All.EqualTo(current.Position));

        current.Blackboard.IsBehindCover = true;
        _projectiles.Clear();
        _events.Clear();
        _clock.Now = 12f;
        _runner.Tick(_clock.Now, 12f);
        Assert.That(Cast(), Is.True);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(2));
        Assert.That(_events.Of<ProjectileFired>().Select(shot => shot.Target), Is.All.EqualTo(nearest.Position));
    }

    [Test]
    public void Cast_NoneInRange_Refused_CooldownKept()
    {
        Spawn(0f, 13f);
        Spawn(0f, 4f).Blackboard.IsBehindCover = true;

        Assert.That(Cast(), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(-10f)]
    public void Cast_ConjuresTwoAtSides_OneSecond(float shadeBonus)
    {
        Vector3 player = new(1f, 0f, 2f);
        _player.Blackboard.PlayerPosition = player;
        _clock.Now = 2f;
        Spawn(0f, 6f);
        Upgrade(PlayerStat.CrossfireShades, shadeBonus);

        Assert.That(Cast(), Is.True);
        var shades = _events.Of<ShadeAppeared>();
        Assert.That(shades.Select(shade => shade.Kind), Is.EqualTo(new[] { ShadeKind.Crossfire, ShadeKind.Crossfire }));
        Assert.That(shades.Select(shade => shade.Position), Is.EqualTo(new[]
        {
            player + Vector3.UnitX * ShadeSystem.SideOffset,
            player - Vector3.UnitX * ShadeSystem.SideOffset,
        }));
        Assert.That(shades.Select(shade => shade.ExpiresAt), Is.All.EqualTo(3f));
    }

    [Test]
    public void Cast_EveryStandingShadeLooses()
    {
        EnemyAgent target = Spawn(0f, 6f);
        _shades.Spawn(ShadeKind.Image, Vector3.UnitZ, 0f, 3f, 1f);
        _shades.Spawn(ShadeKind.Decoy, -Vector3.UnitZ, 0f, 3f, 0f);
        _shades.Spawn(ShadeKind.Hall, Vector3.UnitX, 0f, 3f, 1f);
        int expired = _shades.Spawn(ShadeKind.Crossfire, Vector3.Zero, 0f, 1f, 0f);
        _clock.Now = 1f;

        Assert.That(Cast(), Is.True);
        var standing = _events.Of<ShadeAppeared>().Where(shade => shade.Id != expired).ToArray();
        var shots = _events.Of<ProjectileFired>();
        Assert.That(shots.Select(shot => shot.Origin), Is.EqualTo(standing.Select(shade => shade.Position)));
        Assert.That(shots.Select(shot => shot.Target), Is.All.EqualTo(target.Position));
        Assert.That(shots.Select(shot => shot.ShotOrigin), Is.All.EqualTo(ShotOrigin.Crossfire));
        Assert.That(_events.Of<ShadeLoosed>().Select(loose => loose.Id), Is.EqualTo(standing.Select(shade => shade.Id)));
    }

    [Test]
    public void Arrow_Damage_OnePointFiveWeapon()
    {
        Spawn(0f, 6f);
        Upgrade(PlayerStat.WeaponDamage, 4f);

        Assert.That(Cast(), Is.True);
        LandShots();

        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Amount), Is.EqualTo(new[] { 36f, 36f }));
    }

    [Test]
    public void BlockedShade_LoosesNothing()
    {
        Initialise(_events, (from, to) => from.X > 0.5f ? Vector3.Lerp(from, to, 0.5f) : null);
        Spawn(0f, 6f);

        Assert.That(Cast(), Is.True);
        Assert.That(_events.Single<ProjectileFired>().Origin.X, Is.EqualTo(-ShadeSystem.SideOffset));
        Assert.That(_events.Single<ShadeLoosed>().Id, Is.EqualTo(_shades.Standing[1].Id));
    }

    [Test]
    public void Arrow_FirstInTheWayTakesIt()
    {
        EnemyAgent first = Spawn(0f, 6f);
        EnemyAgent target = Spawn(0f, 6.1f);
        _player.Blackboard.CurrentTargetId = target.Id;

        Assert.That(Cast(), Is.True);
        LandShots();

        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Id), Is.EqualTo(new[] { first.Id, first.Id }));
        Assert.That(_projectiles.InFlightCount, Is.Zero);
    }

    [Test]
    public void ImageFireTimer_Unchanged()
    {
        EnemyAgent target = Spawn(0f, 6f);
        int image = _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 5f, 1f);
        _shades.Tick(0f, _enemies, _player);
        float before = _shades.Standing[0].NextShotAt;
        _events.Clear();
        _clock.Now = before / 2f;

        Assert.That(Cast(), Is.True);
        Assert.That(_shades.Standing[0].NextShotAt, Is.EqualTo(before));
        Assert.That(_events.Of<ShadeLoosed>().Any(loose => loose.Id == image), Is.True);
        Assert.That(_events.Of<ProjectileFired>().Select(shot => shot.Target), Is.All.EqualTo(target.Position));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Cast_FullSky_SkipsRemainingArrows(int slots)
    {
        Spawn(0f, 6f);
        for (int i = 0; i < Capacity - slots; i++)
        {
            _projectiles.Fire(new Projectile(EnemyId, 1, Vector3.Zero, -Vector3.UnitZ, 25f, 0.1f, 0f), 0f);
        }

        _events.Clear();
        Assert.That(Cast(), Is.True);
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(2));
        Assert.That(_events.Count<ShadeLoosed>(), Is.EqualTo(slots));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(slots));
    }

    [Test]
    public void CrossfireShade_NeverShoots_FadesExpired()
    {
        Spawn(0f, 2f);
        Upgrade(PlayerStat.FadeBurst, 1f);
        Assert.That(Cast(), Is.True);
        _events.Clear();

        _shades.Tick(0.5f, _enemies, _player);
        Assert.That(_shades.Standing.Length, Is.EqualTo(2));
        _shades.Tick(1f, _enemies, _player);

        Assert.That(_shades.Standing.Length, Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(_events.Of<ShadeFaded>().Select(fade => fade.Expired), Is.EqualTo(new[] { true, true }));
        Assert.That(_events.Count<ShadeBurst>(), Is.EqualTo(2));
    }

    [TestCase(1f)]
    [TestCase(10f)]
    public void Rank3_ThreeShades_TwoTimes_NineSeconds(float shadeBonus)
    {
        Spawn(0f, 6f);
        Upgrade(PlayerStat.CrossfireShades, shadeBonus);
        Upgrade(PlayerStat.CrossfireDamage, 0.5f);
        _effects.Apply(new ModifySkillCooldown(SkillId, ModifierKind.Flat, -3f), this);

        Assert.That(Cast(), Is.True);
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(9f));
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(3));
        Assert.That(_shades.Standing[2].Position, Is.EqualTo(-Vector3.UnitZ * ShadeSystem.SideOffset));
        LandShots();
        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Amount), Is.EqualTo(new[] { 40f, 40f, 40f }));
    }

    [Test]
    public void Cast_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        Spawn(0f, 6f);
        AllocationAssert.None(() =>
        {
            _clock.Now += 12f;
            _runner.Tick(12f, _clock.Now);
            Cast();
            _projectiles.Clear();
            _shades.Clear();
        });
        Assert.That(_runner.IsReady(0), Is.False);
    }

    private void Initialise(IDomainEvents events, Func<Vector3, Vector3, Vector3?> traceCover = null)
    {
        CharacterSpec character = Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { Body() });
        _player = new PlayerCombat(character, events, new RecordingIntents(), Capacity);
        var progression = new LevelTracker(Scalings.Xp(), events);
        var stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ), progression);
        _enemies = new EnemySystem(catalog, events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        _projectiles = new ProjectileSystem(events, Capacity, traceCover: traceCover);
        _shades = new ShadeSystem(_projectiles, new LureSystem(events), stats, character, events);
        _clock = new SimulatedClock();
        _effects = new EffectRegistry();
        _effects.Register<Crossfire>(new CrossfireHandler(_shades, _projectiles, _enemies, stats,
            _player.Blackboard, character, _clock));
        _effects.Register<ModifyStat>(new ModifyStatHandler(stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, events);
        _runner.Add(new SkillSpec(SkillId, new LocKey("skill.crossfire.name"), new LocKey("skill.crossfire.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(12f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) }),
                new IEffect[] { new Crossfire(1.5f) })));
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    private bool Cast() => _runner.Cast(0, _clock.Now, auto: false);

    private void LandShots() =>
        _projectiles.Tick(_clock.Now + 0.75f, _player.Blackboard.PlayerPosition, _player, _enemies);

    private void Upgrade(PlayerStat stat, float value) =>
        _effects.Apply(new ModifyStat(stat, ModifierKind.Flat, value), this);

    private EnemyAgent Spawn(float x, float z) => _enemies.Spawn(EnemyId, new Vector3(x, 0f, z));

    private static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, WeaponDamage, 1.5f, 12f, 360f, 0.15f, 25f, 0.2f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    private static EnemySpec Body() => new(EnemyId, new LocKey("enemy.husk.name"),
        1000f, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f, EnemyBehaviourKind.Static);
}
