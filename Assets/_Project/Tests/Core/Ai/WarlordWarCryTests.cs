using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class WarlordWarCryTests
{
    private const string BodyId = "enemy.mossguard_warlord";
    private const string GuardId = "enemy.mossguard";
    private const string BossId = "boss.mossguard_warlord";
    private const string OtherBossId = "boss.other";
    private const int Capacity = 8;
    private const float Tolerance = 1e-4f;

    private static readonly Vector3 Near = Vector3.UnitZ * 3f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private LaneSystem _lanes;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private WarlordBehaviour _warlord;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, 8);
        _lanes = new LaneSystem(_events);
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Body(BodyId, 1000f), Body(GuardId, 10f) },
            modes: null, skills: null, trees: null, bosses: new[] { Boss(BossId, 0), Boss(OtherBossId, 1) });
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), Capacity,
            (agent, spec) => spec.Id == new ContentId(BossId) ? Warlord(agent) : null);
        _agent = _enemies.Registry.Spawn(Body(BodyId, 1000f), Vector3.Zero);
        _warlord = Warlord(_agent);
        _now = 0f;
        Place(Vector3.Zero, Near);
    }

    // Rule 1: the war cry outranks every move once armed, while short of guards and with room.
    [Test]
    public void WarCry_BeforeFirstAt_NotArmed()
    {
        Tick(7.9f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
        Assert.That(WarCryTelegraphs(), Is.Zero);
    }

    [Test]
    public void WarCry_Ready_OutranksSweep()
    {
        Tick(8f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        EnemyTelegraph telegraph = _events.Single<EnemyTelegraph>();
        Assert.That(telegraph.Id, Is.EqualTo(_agent.Id));
        Assert.That(telegraph.Duration, Is.EqualTo(1f));
        Assert.That(telegraph.Move, Is.EqualTo(3));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void WarCry_TwoGuards_NotArmed()
    {
        Summon();
        Tick(0.8f);
        Tick(14f);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
        Assert.That(WarCryTelegraphs(), Is.EqualTo(1));
    }

    [Test]
    public void WarCry_FullRegistry_NotArmed()
    {
        while (_enemies.Registry.AliveCount < Capacity)
        {
            _enemies.Spawn(new ContentId(GuardId), Vector3.UnitX * 20f);
        }

        Tick(8f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Sweep));
        Assert.That(WarCryTelegraphs(), Is.Zero);
    }

    // Rule 2: one second of telegraph, then up to two summons for no XP.
    [Test]
    public void WarCry_TelegraphsOneSecond_ThenSummonsTwo()
    {
        Tick(8f);
        Tick(0.5f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        Assert.That(_warlord.GuardCount, Is.Zero);
        Tick(0.5f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.None));
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
        Assert.That(Guards().Count, Is.EqualTo(2));
    }

    [Test]
    public void WarCry_OneAlive_SummonsOne()
    {
        Summon();
        Kill(Guards()[0]);
        Tick(0.8f);
        Assert.That(_warlord.GuardCount, Is.EqualTo(1));
        int spawned = _events.Count<EnemySpawned>();
        Tick(14f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        Tick(1f);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
        Assert.That(_events.Count<EnemySpawned>() - spawned, Is.EqualTo(1));
    }

    [Test]
    public void Summon_PaysNoXp()
    {
        Summon();
        foreach (EnemyAgent guard in Guards())
        {
            Assert.That(guard.PaysXp, Is.False);
        }
    }

    [Test]
    public void Summon_PicksFarthestSpacedOpenGround()
    {
        Summon(new Vector3(10f, 0f, 0f), new Vector3(11f, 0f, 1f), new Vector3(0f, 0f, -9f),
            new Vector3(-9f, 0f, 0f));
        List<EnemyAgent> guards = Guards();
        Assert.That(guards[0].Position, Is.EqualTo(new Vector3(11f, 0f, 1f)));
        Assert.That(guards[1].Position, Is.EqualTo(new Vector3(0f, 0f, -9f)));
    }

    [Test]
    public void Summon_KeepsPlayerClearance()
    {
        Place(Vector3.Zero, Vector3.UnitZ * 9f);
        Summon(new Vector3(0f, 0f, 10f), new Vector3(8f, 0f, 0f), new Vector3(-8f, 0f, 0f));
        List<EnemyAgent> guards = Guards();
        Assert.That(guards[0].Position, Is.EqualTo(new Vector3(8f, 0f, 0f)));
        Assert.That(guards[1].Position, Is.EqualTo(new Vector3(-8f, 0f, 0f)));
    }

    [Test]
    public void Summon_NoGround_RingAwayFromPlayer()
    {
        Summon();
        float side = 3f * MathF.Sqrt(0.5f);
        List<EnemyAgent> guards = Guards();
        Assert.That(guards[0].Position.X, Is.EqualTo(-side).Within(Tolerance));
        Assert.That(guards[0].Position.Z, Is.EqualTo(-side).Within(Tolerance));
        Assert.That(guards[1].Position.X, Is.EqualTo(side).Within(Tolerance));
        Assert.That(guards[1].Position.Z, Is.EqualTo(-side).Within(Tolerance));
    }

    // Rule 3: the roll-call tracks only its own living guards.
    [Test]
    public void RollCall_DeadGuardLeaves()
    {
        Summon();
        List<EnemyAgent> guards = Guards();
        Kill(guards[0]);
        Tick(0.01f);
        Assert.That(_warlord.GuardCount, Is.EqualTo(1));
        _enemies.Registry.Despawn(guards[1].Id);
        Tick(0.01f);
        Assert.That(_warlord.GuardCount, Is.Zero);
    }

    [Test]
    public void RollCall_StageMossguardIgnored()
    {
        _enemies.Spawn(new ContentId(GuardId), Vector3.UnitX);
        Tick(0.1f);
        Assert.That(_warlord.GuardCount, Is.Zero);
        Assert.That(_warlord.IsLocked, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
    }

    // Rule 4: a guard within 3.5 m widens the guard; a daze still drops it.
    [Test]
    public void Lock_GuardWithinRadius_Ninety()
    {
        Summon(Vector3.UnitX * 3f, -Vector3.UnitX * 3f);
        Assert.That(_warlord.IsLocked, Is.True);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(90f));
        Place(Vector3.Zero, new Vector3(3f, 0f, 1f));
        Tick(0.01f);
        Assert.That(_agent.IsVulnerable, Is.False);
    }

    [Test]
    public void Lock_GuardAway_Sixty()
    {
        Summon(Vector3.UnitX * 3f, -Vector3.UnitX * 3f);
        Place(-Vector3.UnitZ * 5f, -Vector3.UnitZ * 2f);
        Tick(0.01f);
        Assert.That(_warlord.IsLocked, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
    }

    [Test]
    public void Lock_Dazed_Zero()
    {
        Summon(new Vector3(3f, 0f, 6f), new Vector3(-3f, 0f, 6f));
        Place(Vector3.Zero, Vector3.UnitZ * 10f);
        Tick(0.8f);
        Tick(1.4f);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.Charge));
        _warlord.OnLaneStop(true, 6f);
        Tick(0.1f);
        Tick(0.8f);
        Place(Vector3.UnitZ * 6f, Vector3.UnitZ * 10f);
        Tick(0.01f);
        Assert.That(_warlord.IsDazed, Is.True);
        Assert.That(_warlord.IsLocked, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.Zero);
        Tick(2f);
        Assert.That(_warlord.IsDazed, Is.False);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(90f));
    }

    [Test]
    public void Lock_PublishesOnChange()
    {
        Summon(Vector3.UnitX * 3f, -Vector3.UnitX * 3f);
        Tick(0.01f);
        Place(-Vector3.UnitZ * 5f, -Vector3.UnitZ * 2f);
        Tick(0.01f);
        IReadOnlyList<WarlordGuardArc> arcs = _events.Of<WarlordGuardArc>();
        Assert.That(arcs.Count, Is.EqualTo(3));
        Assert.That(arcs[0].HalfArcDeg, Is.EqualTo(60f));
        Assert.That(arcs[1].HalfArcDeg, Is.EqualTo(90f));
        Assert.That(arcs[2].HalfArcDeg, Is.EqualTo(60f));
    }

    // Rule 5: the beat despawns its guards and rearms the war cry from the beat's end.
    [Test]
    public void PhaseBeat_DespawnsGuards_ResetsCooldown()
    {
        UseBoss(BossId);
        EnemyAgent stage = _enemies.Spawn(new ContentId(GuardId), Vector3.UnitX * 20f);
        EnemyTick(8f);
        EnemyTick(1f);
        List<EnemyAgent> guards = Guards();
        guards.Remove(stage);
        Assert.That(guards.Count, Is.EqualTo(2));
        Place(Vector3.Zero, -Near);
        EnemyTick(0.01f);
        _enemies.ApplyDamage(_agent.Id, 600f, _now, _player);
        EnemyTick(0.01f);
        var boss = (BossBehaviour)_agent.Behaviour;
        Assert.That(boss.IsInBeat, Is.True);
        Assert.That(_warlord.GuardCount, Is.Zero);
        Assert.That(_enemies.Registry.TryGet(guards[0].Id, out _), Is.False);
        Assert.That(_enemies.Registry.TryGet(guards[1].Id, out _), Is.False);
        Assert.That(stage.IsAlive, Is.True);
        EnemyTick(1.49f);
        Assert.That(boss.IsInBeat, Is.True);
        int before = WarCryTelegraphs();
        float elapsed = 0f;
        while (WarCryTelegraphs() == before && elapsed < 20f)
        {
            EnemyTick(0.1f);
            elapsed += 0.1f;
        }

        Assert.That(elapsed, Is.GreaterThanOrEqualTo(8f - Tolerance));
        Assert.That(elapsed, Is.LessThan(11.5f));
    }

    [Test]
    public void PhaseBeat_OtherBosses_Unchanged()
    {
        UseBoss(OtherBossId);
        EnemyAgent stage = _enemies.Spawn(new ContentId(GuardId), Vector3.UnitX * 20f);
        EnemyTick(0.1f);
        var boss = (BossBehaviour)_agent.Behaviour;
        Assert.That(boss.Inner, Is.Null);
        Assert.That(boss.AddCount, Is.EqualTo(1));
        _enemies.ApplyDamage(_agent.Id, 600f, _now, _player);
        EnemyTick(0.1f);
        Assert.That(boss.IsInBeat, Is.True);
        Assert.That(boss.AddCount, Is.EqualTo(1));
        Assert.That(stage.IsAlive, Is.True);
        Assert.That(Guards().Count, Is.EqualTo(2));
    }

    // Rule 6: reset forgets without despawning; ticking allocates nothing.
    [Test]
    public void Reset_EmptiesRollCall()
    {
        Summon(Vector3.UnitX * 3f, -Vector3.UnitX * 3f);
        _warlord.Reset();
        Assert.That(_warlord.GuardCount, Is.Zero);
        Assert.That(_warlord.IsLocked, Is.False);
        Assert.That(Guards().Count, Is.EqualTo(2));
        Tick(0.01f);
        Assert.That(_warlord.GuardHalfArcDeg, Is.EqualTo(60f));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        Summon(Vector3.UnitX * 3f, -Vector3.UnitX * 3f);
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, 8);
        var projectiles = new ProjectileSystem(silent, 8);
        var lanes = new LaneSystem(silent);
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            Place(Vector3.Zero, Near);
            _warlord.Tick(new EnemyTickContext(0.1f, 1f, player, _intents, silent, projectiles, _enemies, lanes));
            Place(-Vector3.UnitZ * 5f, -Vector3.UnitZ * 2f);
            _warlord.Tick(new EnemyTickContext(0.1f, 1f, player, _intents, silent, projectiles, _enemies, lanes));
        }, 100);
    }

    private void Summon(params Vector3[] ground)
    {
        Tick(8f, ground);
        Assert.That(_warlord.Move, Is.EqualTo(WarlordMove.WarCry));
        Tick(1f, ground);
        Assert.That(_warlord.GuardCount, Is.EqualTo(2));
    }

    private void UseBoss(string bossId)
    {
        _enemies.Registry.Despawn(_agent.Id);
        _agent = _enemies.SpawnBoss(new ContentId(bossId), Vector3.Zero);
        _warlord = ((BossBehaviour)_agent.Behaviour).Inner as WarlordBehaviour;
        Place(Vector3.Zero, Near);
    }

    private void Tick(float dt, Vector3[] ground = null)
    {
        _now += dt;
        _warlord.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies,
            _lanes, ground));
    }

    private void EnemyTick(float dt)
    {
        _now += dt;
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies,
            _lanes));
    }

    private void Place(Vector3 self, Vector3 player)
    {
        Vector3 delta = player - self;
        var flat = new Vector2(delta.X, delta.Z);
        _agent.Blackboard.SelfPosition = self;
        _agent.Blackboard.PlayerPosition = player;
        _agent.Blackboard.DirectionToPlayer = flat == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(flat);
        _agent.Blackboard.PathDirectionToPlayer = _agent.Blackboard.DirectionToPlayer;
        _agent.Blackboard.DistanceToPlayer = flat.Length();
    }

    private void Kill(EnemyAgent agent) => _enemies.ApplyDamage(agent.Id, 1000f, _now, _player);

    private int WarCryTelegraphs()
    {
        int count = 0;
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            if (telegraph.Move == (int)WarlordMove.WarCry)
            {
                count++;
            }
        }

        return count;
    }

    private List<EnemyAgent> Guards()
    {
        var guards = new List<EnemyAgent>();
        foreach (EnemyAgent agent in _enemies.Registry.Alive)
        {
            if (agent.IsAlive && agent.Spec.Id == new ContentId(GuardId))
            {
                guards.Add(agent);
            }
        }

        return guards;
    }

    private WarlordBehaviour Warlord(EnemyAgent agent) =>
        new(agent, _projectiles, new FixedRandom(0.5f).Misc, new ContentId(GuardId));

    private static BossSpec Boss(string id, int adds) => new(new ContentId(id), new ContentId(BodyId),
        adds == 0
            ? new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }
            : new[]
            {
                new BossPhaseSpec(1f, new[] { new AddWave(new ContentId(GuardId), adds) }),
                new BossPhaseSpec(0.5f, new[] { new AddWave(new ContentId(GuardId), adds) }),
            }, 1.5f);

    private static EnemySpec Body(string id, float hp) => new(new ContentId(id), new LocKey(id + ".name"),
        hp, 1.8f, 8, 40, 300f, false, 20f, 2.5f, 0.9f, 1.2f, 40f,
        id == BodyId ? EnemyBehaviourKind.Boss : EnemyBehaviourKind.Static);

    private static CharacterSpec Character() => new(new ContentId("character.test"),
        new LocKey("character.test.name"), new LocKey("character.test.description"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
