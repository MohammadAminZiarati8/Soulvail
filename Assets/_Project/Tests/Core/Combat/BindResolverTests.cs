using System;
using System.Collections.Generic;
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
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class BindResolverTests
{
    private const int Capacity = 16;
    private const float Hp = 1_000f;
    private const float Tolerance = 1e-4f;
    private static readonly ContentId BindId = new("skill.binding-shot");
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private BindResolver _binds;
    private ProjectileSystem _projectiles;
    private BindingShotHandler _handler;
    private EffectRegistry _effects;
    private SkillRunner _runner;
    private SimulatedClock _clock;
    private BindingShot _shot;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Initialise(_events);
    }

    private void Initialise(IDomainEvents events)
    {
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), events, _intents, Capacity);
        _stats = new PlayerStats(_player, new PlayerMotor(Character().Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        _enemies = new EnemySystem(Catalog(), events, new FixedRandom(7), new DepthScaling(Scalings.Design()), Capacity);
        _binds = new BindResolver(_enemies, _stats, _player.Blackboard, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity, _binds);
        _clock = new SimulatedClock();
        _shot = new BindingShot(4f, 20f, 2f, 0.75f, 2f);
        _handler = new BindingShotHandler(_projectiles, _enemies, _stats, _player.Blackboard, Character(), _clock);
        _effects = new EffectRegistry();
        _effects.Register<BindingShot>(_handler);
        _effects.Register<ModifyStat>(new ModifyStatHandler(_stats));
        _runner = new SkillRunner(_effects, _player.Blackboard, events);
        _runner.Add(Skill(_shot));
        _effects.Register<ModifySkillCooldown>(new ModifySkillCooldownHandler(_runner));
    }

    [Test]
    public void Target_CurrentElseNearest()
    {
        EnemyAgent near = Spawn(3f);
        EnemyAgent current = Spawn(7f, 2f);
        _player.Blackboard.CurrentTargetId = current.Id;
        _handler.Apply(_shot, this);
        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(current.Position));
        _projectiles.Clear();
        _events.Clear();
        Move(current, new Vector3(13f, 100f, 0f));
        _handler.Apply(_shot, this);
        Assert.That(_events.Single<ProjectileFired>().Target, Is.EqualTo(near.Position));
    }

    [Test]
    public void NoTarget_Refused_CooldownKept()
    {
        EnemyAgent corpse = Spawn(2f);
        _enemies.ApplyDamage(corpse.Id, Hp * 2f, 0f, _player);
        Spawn(13f);
        _player.Blackboard.EnemiesInAcquireRange = 1;
        Assert.That(_runner.Cast(0, 0f, auto: false), Is.False);
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_projectiles.InFlightCount, Is.Zero);
        Spawn(4f);
        _runner.Tick(0f, 0f);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1), "Refusal also kept the auto gap.");
    }

    [Test]
    public void Arrow_UsesWeaponSpeedAndDamage_OnlyBindsOnHit()
    {
        EnemyAgent body = Spawn(5f);
        _handler.Apply(_shot, this);
        ProjectileFired fired = _events.Single<ProjectileFired>();
        Assert.That(fired.FlightTime, Is.EqualTo(5f / 25f).Within(Tolerance));
        Step(0.01f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp));
        Assert.That(_intents.BindProbes.Count, Is.Zero);
        Step(0.2f);
        Assert.That(body.Health.Current, Is.EqualTo(Hp - 34f).Within(Tolerance));
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(1));
    }

    [Test]
    public void EnemyBehind_InCone_Binds()
    {
        EnemyAgent target = Spawn(4f);
        EnemyAgent farther = Spawn(7.5f);
        EnemyAgent nearest = Spawn(6f, 0.2f);
        _binds.Hit(target.Id, _shot);
        Step();
        BindingShotBound bound = _events.Single<BindingShotBound>();
        Assert.That(bound.Anchor, Is.EqualTo(BindAnchor.Enemy));
        Assert.That(bound.TargetId, Is.EqualTo(target.Id));
        Assert.That(bound.AnchorEnemyId, Is.EqualTo(nearest.Id));
        Assert.That(bound.AnchorPoint, Is.EqualTo(nearest.Position));
        Assert.That(_intents.BindProbes.Count, Is.Zero, "Enemy wins before a collider is asked.");
        Assert.That(Remaining(farther), Is.Zero);
    }

    [Test]
    public void EnemyBeside_OutOfCone_Ignored()
    {
        EnemyAgent target = Spawn(4f);
        Spawn(4f, 2f);
        Spawn(2f);
        Spawn(8.01f);
        _binds.Hit(target.Id, _shot);
        Step();
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(1));
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
    }

    [Test]
    public void Direction_UsesPlayerAndTargetAtHit_XZ()
    {
        EnemyAgent target = Spawn(4f);
        _handler.Apply(_shot, this);
        Move(target, new Vector3(4f, 50f, 0f));
        _player.Blackboard.PlayerPosition = new Vector3(4f, 100f, -3f);
        EnemyAgent anchor = Spawn(4f, 2f);
        Step();
        Assert.That(_events.Single<BindingShotBound>().AnchorEnemyId, Is.EqualTo(anchor.Id));
    }

    [Test]
    public void Anchor_DeadIgnored_ReachInclusive_TieLowestId()
    {
        EnemyAgent target = Spawn(4f);
        EnemyAgent dead = Spawn(5f);
        _enemies.ApplyDamage(dead.Id, Hp * 2f, 0f, _player);
        EnemyAgent first = Spawn(8f);
        Spawn(8f);
        _binds.Hit(target.Id, _shot);
        Step();
        Assert.That(_events.Single<BindingShotBound>().AnchorEnemyId, Is.EqualTo(first.Id));
    }

    [Test]
    public void NoEnemy_EmitsProbe()
    {
        EnemyAgent target = Probe();
        BindProbeIntent probe = _intents.BindProbes[0];
        Assert.That(probe.TargetId, Is.EqualTo(target.Id));
        Assert.That(probe.From, Is.EqualTo(target.Position));
        Assert.That(probe.Direction, Is.EqualTo(Vector3.UnitX));
        Assert.That(probe.Reach, Is.EqualTo(4f));
        Assert.That(Remaining(target), Is.Zero);
    }

    [Test]
    public void ProbeHit_BindsToObstacle()
    {
        EnemyAgent target = Probe();
        var point = new Vector3(6f, 0f, 0f);
        _binds.ReportAnchor(target.Id, true, point);
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero, "Facts only record.");
        Step();
        BindingShotBound bound = _events.Single<BindingShotBound>();
        Assert.That(bound.Anchor, Is.EqualTo(BindAnchor.Obstacle));
        Assert.That(bound.AnchorPoint, Is.EqualTo(point));
        Assert.That(bound.AnchorEnemyId, Is.Zero);
        Assert.That(Remaining(target), Is.EqualTo(2f));
    }

    [Test]
    public void ProbeMiss_Alone()
    {
        EnemyAgent target = Probe();
        _binds.ReportAnchor(target.Id, false, default);
        Step();
        Assert.That(_events.Single<BindingShotBound>().Anchor, Is.EqualTo(BindAnchor.None));
        Assert.That(Remaining(target), Is.EqualTo(0.75f));
    }

    [Test]
    public void NoAnswer_Alone()
    {
        EnemyAgent target = Probe();
        Step();
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero, "One unanswered tick is still pending.");
        Step();
        Assert.That(_events.Single<BindingShotBound>().Anchor, Is.EqualTo(BindAnchor.None));
        Assert.That(Remaining(target), Is.EqualTo(0.75f));
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(1));
    }

    [Test]
    public void Bound_BothStunned()
    {
        EnemyAgent target = Spawn(4f);
        EnemyAgent anchor = Spawn(6f);
        _handler.Apply(_shot, this);
        Step();
        Assert.That(Remaining(target), Is.EqualTo(2f));
        Assert.That(Remaining(anchor), Is.EqualTo(2f));
        Assert.That(anchor.Health.Current, Is.EqualTo(Hp), "The chain does no splash damage.");
    }

    [Test]
    public void Alone_ShortStun()
    {
        EnemyAgent target = Probe();
        Upgrade(1f);
        _binds.ReportAnchor(target.Id, false, default);
        Step();
        Assert.That(Remaining(target), Is.EqualTo(0.75f), "BindStun never buffs the alone stun.");
        Assert.That(_events.Single<BindingShotBound>().Seconds, Is.EqualTo(0.75f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Boss_Immune(bool bound)
    {
        EnemyAgent target = _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(4f, 0f, 0f));
        EnemyAgent anchor = bound ? _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(6f, 0f, 0f)) : null;
        _binds.Hit(target.Id, _shot);
        Step();
        if (!bound)
        {
            _binds.ReportAnchor(target.Id, false, default);
            Step();
        }

        Assert.That(Remaining(target), Is.Zero);
        if (bound)
        {
            Assert.That(Remaining(anchor), Is.Zero);
        }
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero, "JG-26b: nothing held, no rope.");
    }

    /// <summary>JG-26b rule 1, the playtest case: a boss shot against a stone shows no rope.</summary>
    [Test]
    public void Boss_ToStone_NoRope()
    {
        EnemyAgent target = _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(4f, 0f, 0f));
        _binds.Hit(target.Id, _shot);
        Step();
        _binds.ReportAnchor(target.Id, true, new Vector3(6f, 0f, 0f));
        Step();

        Assert.That(Remaining(target), Is.Zero);
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
    }

    /// <summary>JG-26b rule 1: a boss tied to an ordinary enemy still shows the rope that holds the enemy.</summary>
    [Test]
    public void Boss_ToEnemy_RopeHoldsTheEnemy()
    {
        EnemyAgent target = _enemies.SpawnBoss(new ContentId("boss.test"), new Vector3(4f, 0f, 0f));
        EnemyAgent anchor = Spawn(6f);
        _binds.Hit(target.Id, _shot);
        Step();

        Assert.That(Remaining(target), Is.Zero);
        Assert.That(Remaining(anchor), Is.GreaterThan(0f));
        BindingShotBound bound = _events.Single<BindingShotBound>();
        Assert.That(bound.Anchor, Is.EqualTo(BindAnchor.Enemy));
        Assert.That(bound.AnchorEnemyId, Is.EqualTo(anchor.Id));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeadTarget_Nothing(bool afterProbe)
    {
        EnemyAgent target = afterProbe ? Probe() : Spawn(4f);
        if (!afterProbe)
        {
            _binds.Hit(target.Id, _shot);
        }

        _enemies.ApplyDamage(target.Id, Hp * 2f, 1f, _player);
        Assert.That(target.IsAlive, Is.False, "The target died before resolution.");
        _binds.ReportAnchor(target.Id, true, new Vector3(6f, 0f, 0f));
        Step();
        Step();
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
        Assert.That(Remaining(target), Is.Zero);
    }

    [Test]
    public void LaterProjectileKillsTarget_NoBindAfterHitPass()
    {
        EnemyAgent target = Spawn(4f);
        Spawn(6f);
        _handler.Apply(_shot, this);
        _projectiles.Fire(new Projectile(Character().Id, 0, Vector3.Zero, target.Position,
            25f, 0.1f, Hp * 2f, ShotSide.AtEnemies), 0f);
        Step();
        Assert.That(target.IsAlive, Is.False);
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
        Assert.That(_intents.BindProbes.Count, Is.Zero);
    }

    [Test]
    public void Bound_PublishesOnce()
    {
        EnemyAgent target = Probe();
        _binds.ReportAnchor(999, true, default);
        _binds.ReportAnchor(target.Id, true, new Vector3(6f, 0f, 0f));
        _binds.ReportAnchor(target.Id, false, default);
        Step();
        _binds.ReportAnchor(target.Id, false, default);
        Step();
        Step();
        Assert.That(_events.Count<BindingShotBound>(), Is.EqualTo(1));
        Assert.That(_events.Single<BindingShotBound>().Anchor, Is.EqualTo(BindAnchor.Obstacle));
    }

    [Test]
    public void Rank_StunAddsUp()
    {
        EnemyAgent target = Spawn(4f);
        EnemyAgent anchor = Spawn(6f);
        Upgrade(0.4f);
        Upgrade(0.6f);
        _binds.Hit(target.Id, _shot);
        Step();
        Assert.That(_stats.Has(PlayerStat.BindStun), Is.True);
        Assert.That(new CombatantStats(target).Has(PlayerStat.BindStun), Is.False);
        Assert.That(Remaining(target), Is.EqualTo(3f).Within(Tolerance));
        Assert.That(Remaining(anchor), Is.EqualTo(3f).Within(Tolerance));
        Assert.That(_events.Single<BindingShotBound>().Seconds, Is.EqualTo(3f).Within(Tolerance));
    }

    [Test]
    public void Auto_OneEnemy_TwelveSeconds_Rank4Nine()
    {
        Spawn(4f);
        _runner.Tick(0f, 0f);
        Assert.That(_projectiles.InFlightCount, Is.Zero);
        _player.Blackboard.EnemiesInAcquireRange = 1;
        _runner.Tick(0f, 0f);
        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(12f));
        _runner.Tick(0f, 11.99f);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
        _effects.Apply(new ModifySkillCooldown(BindId, ModifierKind.Flat, -3f), this);
        _clock.Now = 12f;
        _runner.Tick(0f, 12f);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(2));
        Assert.That(_runner.EffectiveCooldownOf(0), Is.EqualTo(9f));
    }

    [Test]
    public void Boundary_DropsPendingProbe()
    {
        EnemyAgent target = Probe();
        _projectiles.Clear(); // StageFlow's boundary door, which also clears the resolver.
        _binds.ReportAnchor(target.Id, true, new Vector3(6f, 0f, 0f));
        Step();
        Step();
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
        Assert.That(Remaining(target), Is.Zero);
        _handler.Apply(_shot, this);
        Step(2f);
        _binds.ReportAnchor(target.Id, false, default);
        Step(3f);
        Assert.That(_events.Single<BindingShotBound>().Anchor, Is.EqualTo(BindAnchor.None));
    }

    [Test]
    public void ConcurrentProbes_CompactionKeepsAnswersWithTargets()
    {
        EnemyAgent first = Spawn(4f);
        EnemyAgent second = Spawn(0f, 4f);
        EnemyAgent third = Spawn(-4f);
        _binds.Hit(first.Id, _shot);
        _binds.Hit(second.Id, _shot);
        _binds.Hit(third.Id, _shot);
        Step();
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(3));
        _binds.ReportAnchor(second.Id, true, new Vector3(0f, 0f, 6f));
        Step();
        Assert.That(Remaining(second), Is.EqualTo(2f));
        Assert.That(Remaining(first), Is.Zero);
        _binds.ReportAnchor(third.Id, true, new Vector3(-6f, 0f, 0f));
        Step();
        Assert.That(Remaining(first), Is.EqualTo(0.75f));
        Assert.That(Remaining(third), Is.EqualTo(2f));
        Assert.That(_events.Count<BindingShotBound>(), Is.EqualTo(3));
    }

    [Test]
    public void Session_RegistersEffect_ResolvesFactInFollowingProjectileStep()
    {
        SkillSpec active = Skill(_shot);
        var fillerB = new ContentId("skill.filler-b");
        var fillerC = new ContentId("skill.filler-c");
        var tree = new SkillTreeSpec(new ContentId("tree.test"), Character().Id, new[]
        {
            Branch(active.Id), Branch(fillerB), Branch(fillerC),
        });
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"),
            1, true, 0, Scalings.Design(), Scalings.Xp(), Array.Empty<RosterEntry>());
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body() }, new[] { mode },
            new[] { active, Passive(fillerB), Passive(fillerC) }, new[] { tree });
        var random = new FixedRandom(7);
        var session = new RunSession(catalog, random, _events, _intents,
            new RunRecorder(random, new FixedClock(default), _events), Capacity, Capacity, Capacity);
        Assert.Throws<InvalidOperationException>(() => session.ReportBindAnchor(1, false, default));
        var restore = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, Character().Id, 7, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { active.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, Character().Id, 7, 1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(Body().Id, new Vector3(4f, 0f, 0f)) }), restore));
        EnemySpawned spawned = _events.Single<EnemySpawned>();
        var snapshot = new WorldSnapshot(Capacity) { Dt = 0.01f };
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = spawned.Id;
        sense.Position = spawned.Position;
        sense.HasLineOfSight = true;
        session.Tick(snapshot);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
        Assert.That(_intents.BindProbes.Count, Is.Zero, "The arrow is still in flight.");
        snapshot.Dt = 0.2f;
        session.Tick(snapshot);
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(1));
        session.ReportBindAnchor(spawned.Id, true, new Vector3(6f, 0f, 0f));
        Assert.That(_events.Count<BindingShotBound>(), Is.Zero);
        snapshot.Dt = 0.01f;
        session.Tick(snapshot);
        Assert.That(_events.Single<BindingShotBound>().Anchor, Is.EqualTo(BindAnchor.Obstacle));
        session.End();
        Assert.Throws<InvalidOperationException>(() => session.ReportBindAnchor(spawned.Id, false, default));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        Initialise(new SilentEvents());
        EnemyAgent target = Spawn(4f);
        EnemyAgent anchor = Spawn(6f);
        target.Health.MaxHp.Base = 1e8f;
        target.Health.Reset();
        var senses = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref senses.AddEnemy();
        sense.Id = anchor.Id;
        sense.HasLineOfSight = true;
        _player.Blackboard.EnemiesInAcquireRange = 1;
        int casts = 0;
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            senses.Enemies[0].Position = new Vector3(6f, 0f, 0f);
            _enemies.Ingest(senses);
            _clock.Now += 12f;
            _runner.Tick(0f, _clock.Now);
            Step(_clock.Now + 0.2f); // Enemy anchor, damage and both stuns.
            casts++;
            senses.Enemies[0].Position = new Vector3(6f, 0f, 6f);
            _enemies.Ingest(senses);
            for (int answer = 0; answer < 4; answer++)
            {
                _handler.Apply(_shot, this);
                Step(_clock.Now + 0.2f);
                if (answer < 2)
                {
                    _binds.ReportAnchor(target.Id, answer == 0, new Vector3(6f, 0f, 0f));
                }
                else if (answer == 3)
                {
                    _projectiles.Clear();
                }

                Step(_clock.Now + 0.3f);
                Step(_clock.Now + 0.4f); // Timeout or no-op after clear.
            }
        });
        Assert.That(casts, Is.GreaterThan(10_000));
        Assert.That(target.Health.Current, Is.LessThan(1e8f - 100_000f));
        Assert.That(_intents.BindProbes.Count, Is.EqualTo(4), "All measured probe paths stayed live.");
    }

    [Test]
    public void Guards()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(bad, 20f, 2f, 0.75f, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(4f, bad, 2f, 0.75f, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(4f, 20f, bad, 0.75f, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(4f, 20f, 2f, bad, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(4f, 20f, 2f, 0.75f, bad));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new BindingShot(4f, 91f, 2f, 0.75f, 2f));
        Assert.Throws<ArgumentNullException>(() => _handler.CanCast(null));
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(_shot, null));
        Assert.Throws<ArgumentException>(() => new Projectile(Character().Id, 0, Vector3.Zero,
            Vector3.UnitX, 25f, 0.1f, 1f, bind: _shot));
        EnemyAgent target = Probe();
        Assert.Throws<ArgumentOutOfRangeException>(() => _binds.ReportAnchor(target.Id, true,
            new Vector3(float.NaN, 0f, 0f)));
    }

    private EnemyAgent Probe()
    {
        EnemyAgent target = Spawn(4f);
        _handler.Apply(_shot, this);
        Step();
        return target;
    }

    private void Step(float now = 1f) => _projectiles.Tick(now, _player.Blackboard.PlayerPosition, _player, _enemies);
    private float Remaining(EnemyAgent agent) => _enemies.Remaining(agent.Id, new ContentId(EnemyStatuses.StunnedKey));
    private EnemyAgent Spawn(float x, float z = 0f) => _enemies.Spawn(new ContentId("enemy.husk"), new Vector3(x, 0f, z));
    private void Upgrade(float seconds) => _effects.Apply(new ModifyStat(PlayerStat.BindStun, ModifierKind.Flat, seconds), new object());

    private void Move(EnemyAgent agent, Vector3 position)
    {
        var snapshot = new WorldSnapshot(Capacity);
        ref EnemySense sense = ref snapshot.AddEnemy();
        sense.Id = agent.Id;
        sense.Position = position;
        sense.HasLineOfSight = true;
        _enemies.Ingest(snapshot);
    }

    private static SkillSpec Skill(BindingShot shot) => new(BindId, new LocKey("skill.binding.name"),
        new LocKey("skill.binding.desc"), SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(12f,
            new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 1f) }),
            new IEffect[] { shot }));

    private static SkillBranchSpec Branch(ContentId id) => new(new LocKey("branch.test"),
        new IReadOnlyList<ContentId>[] { new[] { id } });

    private static SkillSpec Passive(ContentId id) => new(id, new LocKey("skill.filler.name"),
        new LocKey("skill.filler.desc"), SkillKind.Passive,
        new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.Flat, 1f) });

    private static CharacterSpec Character() => new(new ContentId("character.ranger"),
        new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 17f, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 0f, 0f, 0.05f), null, 0.5f);

    private static EnemySpec Body(bool boss = false) => new(new ContentId(boss ? "enemy.boss" : "enemy.husk"),
        new LocKey("enemy.husk.name"), Hp, 2f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f, 30f,
        boss ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);

    private static ContentCatalog Catalog() => new(new[] { Character() }, new[] { Body(), Body(true) },
        bosses: new[] { new BossSpec(new ContentId("boss.test"), new ContentId("enemy.boss"),
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f) });
}
