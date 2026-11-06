using System;
using System.Collections.Generic;
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
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Combat;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class MirrorImageTests
{
    private const int Capacity = 16;
    private static readonly ContentId SkillId = new("skill.echo-archer.mirror-image");
    private RecordingEvents _events;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private ShadeSystem _shades;
    private EffectRegistry _effects;
    private SkillRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        CharacterSpec character = ShadeSystemTests.Character();
        var catalog = new ContentCatalog(new[] { character }, new[] { ShadeSystemTests.Body() });
        _player = new PlayerCombat(character, _events, new RecordingIntents(), Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        var projectiles = new ProjectileSystem(_events, Capacity);
        _shades = new ShadeSystem(projectiles, new LureSystem(_events), _stats, character, _events);
        _effects = new EffectRegistry();
        _effects.Register<MirrorImage>(new MirrorImageHandler(_shades, projectiles, _enemies, _stats,
            _player.Blackboard, character, new SimulatedClock()));
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, _events);
        _runner.Add(Skill());
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    [Test]
    public void Rank1_OneShade_SixSeconds()
    {
        Spawn(3f);
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);

        ShadeAppeared shade = _events.Single<ShadeAppeared>();
        Assert.That(shade.Kind, Is.EqualTo(ShadeKind.Image));
        Assert.That(shade.Position, Is.EqualTo(1.2f * Vector3.UnitX));
        Assert.That(shade.ExpiresAt, Is.EqualTo(6f));
        Assert.That(_shades.Standing[0].DamageOfWeapon, Is.EqualTo(0.5f));
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(14f));
    }

    [Test]
    public void Rank4_ThreeShades_Positions()
    {
        _player.Blackboard.PlayerPosition = new Vector3(1f, 2f, 3f);
        Spawn(3f);
        _effects.Apply(new ModifyStat(PlayerStat.MirrorImageShades, ModifierKind.Flat, 1f), this);
        _effects.Apply(new ModifyStat(PlayerStat.MirrorImageSeconds, ModifierKind.Flat, 2f), this);
        _effects.Apply(new ModifyStat(PlayerStat.MirrorImageShades, ModifierKind.Flat, 1f), new object());
        _effects.Apply(new ModifySkillCooldown(SkillId, ModifierKind.Flat, -3f), this);

        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);

        var shades = _events.Of<ShadeAppeared>();
        Vector3 player = _player.Blackboard.PlayerPosition;
        Assert.That(shades.Select(shade => shade.Position), Is.EqualTo(new[]
        {
            player + (1.2f * Vector3.UnitX), player - (1.2f * Vector3.UnitX), player - (1.2f * Vector3.UnitZ),
        }));
        Assert.That(shades.All(shade => shade.ExpiresAt == 8f), Is.True);
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(11f));
    }

    [TestCase(-5f, 1)]
    [TestCase(9f, 3)]
    public void Count_ClampsToSupportedPositions(float value, int count)
    {
        _stats.MirrorImageShades.Base = value;
        Spawn(3f);
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(count));
    }

    [Test]
    public void Cast_NoneInRange_Refused_CooldownKept()
    {
        Spawn(13f);
        Spawn(2f).Blackboard.IsBehindCover = true;

        Assert.That(_runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);
    }

    [Test]
    public void AutoCast_RequiresOneEnemyInAcquireRange()
    {
        Spawn(3f);
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<ShadeAppeared>(), Is.Zero);

        _player.Blackboard.EnemiesInAcquireRange = 1;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(1));
        Assert.That(_runner.IsReady(0), Is.False);
    }

    [Test]
    public void Session_ShadeArrow_AfterPlayerShots()
    {
        RunSession baseline = StartSession(1000f, 4f * Vector3.UnitZ, out WorldSnapshot firstSnapshot);
        int frames = 0;
        while (_events.Count<ProjectileFired>() == 0 && frames < 100)
        {
            baseline.Tick(firstSnapshot);
            frames++;
        }

        Assert.That(_events.Single<ProjectileFired>().ShotOrigin, Is.EqualTo(ShotOrigin.Ranger));
        baseline.End();
        _events.Clear();

        RunSession session = StartSession(1000f, 4f * Vector3.UnitZ, out WorldSnapshot snapshot);
        for (int i = 1; i < frames; i++)
        {
            session.Tick(snapshot);
        }

        session.CastSkillNow(SkillId);
        Assert.That(_events.Count<ShadeAppeared>(), Is.EqualTo(1));
        session.Tick(snapshot);

        var arrows = _events.Of<ProjectileFired>();
        Assert.That(arrows.Select(arrow => arrow.ShotOrigin), Is.EqualTo(new[] { ShotOrigin.Ranger, ShotOrigin.Shade }));
        Assert.That(arrows[0].Id, Is.LessThan(arrows[1].Id));

        session.End();
        Assert.That(_events.Single<ShadeFaded>().Expired, Is.False);
    }

    [Test]
    public void ShadeKill_PaysXp()
    {
        RunSession session = StartSession(1f, 1.4f * Vector3.UnitX, out WorldSnapshot snapshot);
        session.CastSkillNow(SkillId);
        session.Tick(snapshot);
        session.Tick(snapshot);

        Assert.That(_events.Single<ProjectileFired>().ShotOrigin, Is.EqualTo(ShotOrigin.Shade));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(session.State.Xp, Is.GreaterThan(0f));
    }

    private EnemyAgent Spawn(float x) => _enemies.Spawn(ShadeSystemTests.EnemyId, x * Vector3.UnitX);

    private RunSession StartSession(float hp, Vector3 position, out WorldSnapshot snapshot)
    {
        CharacterSpec character = ShadeSystemTests.Character();
        ContentId modeId = new("mode.mirror-test");
        var mode = new ModeSpec(modeId, new LocKey("mode.mirror.name"), 1, true, 0, Scalings.Design(),
            Scalings.Xp(), Array.Empty<RosterEntry>());
        SkillSpec first = Passive("skill.test.first", PlayerStat.MaxHp);
        SkillSpec second = Passive("skill.test.second", PlayerStat.MoveSpeed);
        var tree = new SkillTreeSpec(new ContentId("tree.test"), character.Id,
            new[] { Branch(SkillId), Branch(first.Id), Branch(second.Id) });
        var catalog = new ContentCatalog(new[] { character }, new[] { ShadeSystemTests.Body(hp) },
            new[] { mode }, new[] { Skill(), first, second }, new[] { tree });
        var random = new FixedRandom(7);
        var session = new RunSession(catalog, random, _events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, character.Id, 7, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 3, 0f, 0,
            new[] { SkillId }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        var spawn = new SpawnPlan(new[] { new SpawnPlan.Entry(ShadeSystemTests.EnemyId, position) });
        session.Start(new RunConfig(modeId, character.Id, 7, 1, spawn, saved));
        session.SetAutoCast(SkillId, false);
        EnemySpawned enemy = _events.Single<EnemySpawned>();
        snapshot = new WorldSnapshot(Capacity) { Dt = 0.02f };
        snapshot.AddEnemy() = new EnemySense { Id = enemy.Id, Position = enemy.Position, HasLineOfSight = true };
        return session;
    }

    private static SkillSpec Passive(string id, PlayerStat stat) =>
        new(new ContentId(id), new LocKey("skill.test.name"), new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { new ModifyStat(stat, ModifierKind.Flat, 1f) });

    private static SkillBranchSpec Branch(ContentId id) =>
        new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });

    private static SkillSpec Skill() => new(SkillId, new LocKey("skill.mirror.name"), new LocKey("skill.mirror.desc"),
        SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(14f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) }),
            new IEffect[] { new MirrorImage(0.5f) }));
}
