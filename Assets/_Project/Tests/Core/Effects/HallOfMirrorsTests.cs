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
public sealed class HallOfMirrorsTests
{
    private const int Capacity = 8;
    private const float WeaponDamage = 20f;
    private static readonly ContentId SkillId = new("skill.echo-archer.hall-of-mirrors");
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
    public void Cast_NoneInRange_Refused_CooldownKept()
    {
        Spawn(0f, 13f);
        Spawn(0f, 4f).Blackboard.IsBehindCover = true;
        EnemyAgent dead = Spawn(0f, 6f);
        _enemies.ApplyDamage(dead.Id, 2000f, 0f, _player);

        Assert.That(Cast(), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_events.Count<HallRaised>(), Is.Zero);
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);
    }

    [Test]
    public void Cast_PlayerLineBlocked_RefusedEvenWhenShadeLinesAreClear()
    {
        Initialise(_events, (from, to) => from == Vector3.Zero ? Vector3.Lerp(from, to, 0.5f) : null);
        Spawn(0f, 6f);

        Assert.That(Cast(), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
    }

    [TestCase(0f)]
    [TestCase(-10f)]
    public void Rank1_FourShades_OnThreeMetreCircle_FiveSeconds(float shadeBonus)
    {
        Vector3 center = new(1f, 2f, 3f);
        _player.Blackboard.PlayerPosition = center;
        _clock.Now = 2f;
        Spawn(1f, 6f);
        Upgrade(PlayerStat.HallShades, shadeBonus);

        Assert.That(Cast(), Is.True);
        var shades = _events.Of<ShadeAppeared>();
        Assert.That(shades.Count, Is.EqualTo(4));
        Assert.That(shades.Select(shade => shade.Kind), Is.All.EqualTo(ShadeKind.Hall));
        Assert.That(shades.Select(shade => shade.ExpiresAt), Is.All.EqualTo(7f));
        Vector3[] directions = { Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitZ };
        for (int i = 0; i < directions.Length; i++)
        {
            Assert.That(Vector3.Distance(shades[i].Position, center + directions[i] * 3f), Is.LessThan(0.00001f));
        }

        _shades.Tick(6.9f, _enemies, _player);
        Assert.That(_shades.Standing.Length, Is.EqualTo(4));
        _shades.Tick(7f, _enemies, _player);
        Assert.That(_shades.Standing.Length, Is.Zero);
    }

    [Test]
    public void Shades_ShootAtSeventyPercent()
    {
        Spawn(0f, 6f);
        Upgrade(PlayerStat.WeaponDamage, 4f);

        Assert.That(Cast(), Is.True);
        _shades.Tick(_clock.Now, _enemies, _player);
        LandShots();

        Assert.That(_events.Of<ProjectileFired>().Select(shot => shot.ShotOrigin), Is.All.EqualTo(ShotOrigin.Shade));
        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Amount),
            Is.EqualTo(Enumerable.Repeat(0.7f * 24f, 4)).Within(0.00001f));
    }

    [Test]
    public void OverCap_OldestLeaveFirst()
    {
        Spawn(0f, 6f);
        int oldest = _shades.Spawn(ShadeKind.Image, Vector3.Zero, 0f, 6f, 1f);
        int second = _shades.Spawn(ShadeKind.Image, Vector3.UnitX, 0f, 6f, 1f);
        int newest = _shades.Spawn(ShadeKind.Image, Vector3.UnitZ, 0f, 6f, 1f);
        Upgrade(PlayerStat.HallShades, 2f);

        Assert.That(Cast(), Is.True);
        Assert.That(_shades.Standing.Length, Is.EqualTo(ShadeSystem.Capacity));
        Assert.That(_shades.Standing[0].Id, Is.EqualTo(second));
        Assert.That(_shades.Standing[1].Id, Is.EqualTo(newest));
        ShadeFaded fade = _events.Single<ShadeFaded>();
        Assert.That(fade.Id, Is.EqualTo(oldest));
        Assert.That(fade.Expired, Is.False);
    }

    [Test]
    public void HallRaised_BeforeAppeared()
    {
        Vector3 center = new(1f, 2f, 3f);
        _player.Blackboard.PlayerPosition = center;
        Spawn(1f, 6f);
        _events.Clear();

        Assert.That(Cast(), Is.True);
        HallRaised raised = _events.Single<HallRaised>();
        Assert.That(raised.Center, Is.EqualTo(center));
        Assert.That(raised.Radius, Is.EqualTo(3f));
        Assert.That(raised.Seconds, Is.EqualTo(5f));
        var hallEvents = _events.All.Where(evt => evt is HallRaised or ShadeAppeared);
        Assert.That(hallEvents.Select(evt => evt.GetType()), Is.EqualTo(
            new[] { typeof(HallRaised) }.Concat(Enumerable.Repeat(typeof(ShadeAppeared), 4))));
    }

    [Test]
    public void PlayerWalksAway_ShadesStay()
    {
        Spawn(0f, 6f);
        Assert.That(Cast(), Is.True);
        var positions = _events.Of<ShadeAppeared>().Select(shade => shade.Position).ToArray();
        _player.Blackboard.PlayerPosition = 20f * Vector3.UnitX;

        _shades.Tick(1f, _enemies, _player);

        Assert.That(_shades.Standing.ToArray().Select(shade => shade.Position), Is.EqualTo(positions));
        Assert.That(_events.Of<ProjectileFired>().Select(shot => shot.Origin), Is.EqualTo(positions));
    }

    [TestCase(2f)]
    [TestCase(10f)]
    public void Rank3_SixShades_SevenSeconds_OnePointZero(float shadeBonus)
    {
        Spawn(0f, 6f);
        Upgrade(PlayerStat.HallShades, shadeBonus);
        Upgrade(PlayerStat.HallSeconds, 2f);
        Upgrade(PlayerStat.HallDamage, 0.3f);

        Assert.That(Cast(), Is.True);
        Assert.That(_shades.Standing.Length, Is.EqualTo(6));
        Assert.That(_events.Single<HallRaised>().Seconds, Is.EqualTo(7f));
        for (int i = 0; i < _shades.Standing.Length; i++)
        {
            Shade shade = _shades.Standing[i];
            Shade next = _shades.Standing[(i + 1) % 6];
            Assert.That(shade.ExpiresAt, Is.EqualTo(7f));
            Assert.That(shade.Position.Length(), Is.EqualTo(3f).Within(0.00001f));
            Assert.That(Vector3.Distance(shade.Position, next.Position), Is.EqualTo(3f).Within(0.00001f));
        }

        _shades.Tick(_clock.Now, _enemies, _player);
        LandShots();
        Assert.That(_events.Of<EnemyDamaged>().Select(hit => hit.Amount), Is.EqualTo(Enumerable.Repeat(20f, 6)));
    }

    [Test]
    public void Cast_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        Spawn(0f, 6f);
        Upgrade(PlayerStat.HallShades, 2f);
        AllocationAssert.None(() =>
        {
            _clock.Now += 30f;
            _runner.Tick(30f, _clock.Now);
            Cast();
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
        _effects.Register<HallOfMirrors>(new HallOfMirrorsHandler(_shades, _projectiles, _enemies, stats,
            _player.Blackboard, character, _clock, events));
        _effects.Register<ModifyStat>(new ModifyStatHandler(stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, events);
        _runner.Add(new SkillSpec(SkillId, new LocKey("skill.hall-of-mirrors.name"), new LocKey("skill.hall-of-mirrors.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) }),
                new IEffect[] { new HallOfMirrors(0.7f) })));
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
