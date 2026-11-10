using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class BroodDropTests
{
    private const int Capacity = 64;
    private const int Count = 3;
    private const float Delay = 0.5f;
    private const float Radius = 1.2f;
    private const float Xp = 5f;
    private const float Arrival = SpawnDirector.TelegraphTime;

    private readonly ContentId _motherId = new ContentId("enemy.brood-scorpion");
    private readonly ContentId _childId = new ContentId("enemy.scorpling");
    private readonly ContentId _plainId = new ContentId("enemy.scorpion");
    private readonly ContentId _bossBodyId = new ContentId("enemy.boss");
    private readonly ContentId _bossId = new ContentId("boss.test");
    private readonly Vector3[] _points = { new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f) };

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private SpawnDirector _director;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private IDomainEvents _sink;
    private FixedRandom _random;
    private ContentCatalog _catalog;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        Build(_events);
    }

    [Test]
    public void NoBrood_DeathUnchanged()
    {
        EnemyAgent scorpion = _enemies.Spawn(_plainId, Vector3.Zero);
        Kill(scorpion, 0f);
        Tick(1f);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(1));
        Assert.That(_events.Count<EnemyBroodSwelling>(), Is.Zero);
        Assert.That(_events.Count<EnemyBroodBurst>(), Is.Zero);
    }

    [TestCase("unknown")]
    [TestCase("boss")]
    [TestCase("elite")]
    [TestCase("brood")]
    public void Catalog_BadChild_Throws(string kind)
    {
        EnemySpec child = kind switch
        {
            "unknown" => null,
            "boss" => Enemy(_childId, behaviour: EnemyBehaviourKind.Boss),
            "elite" => Enemy(_childId, elite: true),
            "brood" => Enemy(_childId, brood: new BroodSpec(_plainId, Count, Delay, Radius)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        EnemySpec mother = Enemy(_motherId, brood: new BroodSpec(_childId, Count, Delay, Radius));
        EnemySpec[] enemies = child is null
            ? new[] { mother, Enemy(_plainId) }
            : new[] { mother, Enemy(_plainId), child };

        var error = Assert.Throws<ArgumentException>(() => new ContentCatalog(new[] { Character() }, enemies));
        Assert.That(error.Message, Does.Contain(_childId.Value));
    }

    [Test]
    public void Death_PaysThenSwells()
    {
        EnemyAgent mother = _enemies.Spawn(_motherId, Vector3.Zero);
        _events.Clear();

        Kill(mother, 0f);

        Assert.That(_enemies.PendingXp, Is.EqualTo(Xp));
        Assert.That(_enemies.PendingKills, Is.EqualTo(1));
        Assert.That(mother.BroodPending, Is.True);
        Assert.That(_events.All.Count, Is.EqualTo(3));
        Assert.That(_events.All[0], Is.TypeOf<EnemyDamaged>());
        Assert.That(_events.All[1], Is.TypeOf<EnemyDied>());
        var swelling = (EnemyBroodSwelling)_events.All[2];
        Assert.That(swelling.Id, Is.EqualTo(mother.Id));
        Assert.That(swelling.Duration, Is.EqualTo(Delay));
    }

    [Test]
    public void BeforeDelay_NoChildren()
    {
        EnemyAgent mother = _enemies.Spawn(_motherId, Vector3.Zero);
        Kill(mother, 0f);
        _events.Clear();

        Tick(0.45f);

        Assert.That(mother.BroodPending, Is.True);
        Assert.That(mother.Brood.Length, Is.Zero);
        Assert.That(_events.All, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AfterDelay_DropsThreeRound(bool turn)
    {
        Build(_events, motherBehaviour: turn ? EnemyBehaviourKind.Chaser : EnemyBehaviourKind.Static);
        var center = new Vector3(2f, 3f, 4f);
        EnemyAgent mother = _enemies.Spawn(_motherId, center);

        float death = 0f;

        if (turn)
        {
            mother.Blackboard.DirectionToPlayer = Vector2.UnitX;
            mother.Blackboard.DistanceToPlayer = 5f;
            Tick(0f);
            Tick(0.05f);
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitX));
            mother.Blackboard.DirectionToPlayer = Vector2.Zero;
            mother.Blackboard.DistanceToPlayer = float.PositiveInfinity;
            death = 0.1f;
            Tick(death);
            Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.Zero));
        }

        Kill(mother, death);
        _events.Clear();
        Tick(death + Delay);

        Assert.That(_events.All.Count, Is.EqualTo(1 + Count));
        var burst = (EnemyBroodBurst)_events.All[0];
        Assert.That(burst.Id, Is.EqualTo(mother.Id));
        Assert.That(burst.Position, Is.EqualTo(center));
        Assert.That(mother.BroodPending, Is.False);
        Assert.That(mother.Brood.Length, Is.EqualTo(Count));

        for (int i = 0; i < Count; i++)
        {
            var spawned = (EnemySpawned)_events.All[i + 1];
            Assert.That(spawned.Id, Is.EqualTo(mother.Brood[i]));
            Vector3 offset = spawned.Position - center;
            var angle = (turn ? MathF.PI / 2f : 0f) + i * (2f * MathF.PI / Count);
            var expected = new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * Radius;
            Assert.That(Vector3.Distance(offset, expected), Is.LessThan(0.0001f));
        }
    }

    [Test]
    public void Children_TakeThreeFanSlots()
    {
        EnemyAgent mother = Drop();

        float first = SkitterSpec.SlotOffsetDegrees(mother.Brood[0], 90f);
        float second = SkitterSpec.SlotOffsetDegrees(mother.Brood[1], 90f);
        float third = SkitterSpec.SlotOffsetDegrees(mother.Brood[2], 90f);
        Assert.That(first, Is.Not.EqualTo(second).And.Not.EqualTo(third));
        Assert.That(second, Is.Not.EqualTo(third));
    }

    [Test]
    public void Children_PayOwnXp()
    {
        EnemyAgent mother = Drop();
        _enemies.DrainXp();

        KillChildren(mother, Delay);

        Assert.That(_enemies.DrainXp(), Is.EqualTo(Count * Xp));
    }

    [Test]
    public void TakenByBoss_DropsNothing()
    {
        EnemyAgent boss = _enemies.SpawnBoss(_bossId, Vector3.Zero);
        EnemyAgent mother = _enemies.SpawnSummon(_motherId, Vector3.Zero);
        _events.Clear();

        _enemies.ApplyDamage(boss.Id, 1000f, 0f, _player);
        Tick(1f);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(2));
        Assert.That(mother.BroodPending, Is.False);
        Assert.That(_events.Count<EnemyBroodSwelling>(), Is.Zero);
        Assert.That(_events.Count<EnemySpawned>(), Is.Zero);
    }

    [Test]
    public void SummonMother_ChildrenAreSummons()
    {
        EnemyAgent mother = Drop(summon: true);

        foreach (int id in mother.Brood)
        {
            Assert.That(_enemies.Registry.TryGet(id, out EnemyAgent child), Is.True);
            Assert.That(child.IsSummon, Is.True);
            Assert.That(child.PaysXp, Is.False);
        }

        KillChildren(mother, Delay);
        Assert.That(_enemies.DrainXp(), Is.Zero);
    }

    [Test]
    public void EliteMother_ChildrenPlain()
    {
        Build(_events, eliteMother: true);
        _events.Clear();

        EnemyAgent mother = Drop();

        Assert.That(mother.Spec.IsElite, Is.True);
        var spawns = _events.Of<EnemySpawned>();
        Assert.That(spawns[0].IsElite, Is.True);

        for (int i = 1; i <= Count; i++)
        {
            Assert.That(spawns[i].IsElite, Is.False);
        }
    }

    [Test]
    public void Wave_HeldWhilePending()
    {
        EnemyAgent mother = SpawnWaveMother();
        Kill(mother, Arrival);

        Tick(Arrival + 0.3f);

        Assert.That(mother.BroodPending, Is.True);
        Assert.That(_director.IsStageComplete, Is.False);
        Assert.That(_events.Count<WaveCleared>(), Is.Zero);
    }

    [Test]
    public void Wave_ClearsOnLastChild()
    {
        EnemyAgent mother = SpawnWaveMother();
        Kill(mother, Arrival);
        float burst = Arrival + Delay;
        Tick(burst);
        Assert.That(mother.Brood.Length, Is.EqualTo(Count));

        for (int i = 0; i < Count; i++)
        {
            Assert.That(_events.Count<WaveCleared>(), Is.Zero);
            _enemies.Execute(mother.Brood[i], burst, _player);
            Tick(burst);
        }

        Assert.That(_events.Count<WaveCleared>(), Is.EqualTo(1));
        Assert.That(_director.IsStageComplete, Is.True);
    }

    [Test]
    public void LongTick_StillBursts()
    {
        EnemyAgent mother = SpawnWaveMother();
        Kill(mother, Arrival);
        int id = mother.Id;

        float afterCorpseTime = Arrival + 1f;
        Tick(afterCorpseTime);
        Assert.That(mother.Brood.Length, Is.EqualTo(Count));
        Assert.That(_events.Count<EnemyBroodBurst>(), Is.EqualTo(1));
        Tick(afterCorpseTime + 0.01f);
        Assert.That(_enemies.Registry.TryGet(id, out _), Is.False);
        Assert.That(_events.Count<WaveCleared>(), Is.Zero);

        KillChildren(mother, afterCorpseTime + 0.01f);
        Tick(afterCorpseTime + 0.01f);
        Assert.That(_events.Count<WaveCleared>(), Is.EqualTo(1));
    }

    [Test]
    public void Cap_CountsBroodWeight()
    {
        Begin(Plan(10, 1));

        for (int i = 0; i < 7; i++)
        {
            _enemies.Spawn(_plainId, Vector3.Zero);
        }

        Tick(0f);
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero);

        Kill(_enemies.Registry.Alive[0], 0f);
        Tick(0f);
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1));
    }

    [Test]
    public void Burst_NeverExceedsCap()
    {
        const int cap = 8;
        Begin(Plan(cap, 3));
        Tick(0f);
        Tick(0.4f);
        Assert.That(_director.PendingCount, Is.EqualTo(2));
        Tick(Arrival);
        Tick(1.25f);
        Assert.That(_enemies.LivingCount(), Is.EqualTo(2));

        foreach (EnemyAgent mother in _enemies.Registry.Alive)
        {
            Kill(mother, 1.25f);
        }

        Tick(1.5f);
        Assert.That(_director.PendingCount, Is.Zero);
        Tick(1.75f);
        Assert.That(_enemies.LivingCount(), Is.EqualTo(2 * Count).And.LessThanOrEqualTo(cap));
        Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(2));
        Assert.That(_events.Count<EnemyBroodBurst>(), Is.EqualTo(2));
    }

    [Test]
    public void Reset_DropsPending()
    {
        EnemyAgent mother = SpawnWaveMother();
        Kill(mother, Arrival);
        _events.Clear();

        _enemies.Clear();
        _director.Clear();
        Tick(Arrival + 1f);

        Assert.That(_events.All, Is.Empty);
        Assert.That(_enemies.Registry.AliveCount, Is.Zero);
    }

    [Test]
    public void Burst_NoAlloc()
    {
        Build(new SilentEvents());
        WavePlan plan = Plan(10, 1);
        bool allWavesClearedAfterBurst = true;

        void Cycle()
        {
            Begin(plan);
            Tick(0f);
            Tick(Arrival);
            EnemyAgent mother = _enemies.Registry.Alive[0];
            Kill(mother, Arrival);
            float burst = Arrival + Delay;
            Tick(burst);
            bool heldForChildren = !_director.IsStageComplete;
            KillChildren(mother, burst);
            Tick(burst);
            allWavesClearedAfterBurst &= mother.Brood.Length == Count
                && heldForChildren && _director.IsStageComplete;

            // Return the same bodies in reverse order so the next cycle reuses the same kinds.
            for (int i = mother.Brood.Length - 1; i >= 0; i--)
            {
                _enemies.Despawn(mother.Brood[i]);
            }

            _enemies.Despawn(mother.Id);
            _enemies.Clear();
            _director.Clear();
        }

        AllocationAssert.None(Cycle, 3);
        Assert.That(allWavesClearedAfterBurst, Is.True);
    }

    private EnemyAgent Drop(bool summon = false)
    {
        EnemyAgent mother = summon
            ? _enemies.SpawnSummon(_motherId, Vector3.Zero)
            : _enemies.Spawn(_motherId, Vector3.Zero);
        Kill(mother, 0f);
        Tick(Delay);
        return mother;
    }

    private void Kill(EnemyAgent agent, float now) => _enemies.Execute(agent.Id, now, _player);

    private void KillChildren(EnemyAgent mother, float now)
    {
        foreach (int id in mother.Brood)
        {
            _enemies.Execute(id, now, _player);
        }
    }

    private void Tick(float now)
    {
        _enemies.Tick(new EnemyTickContext(1f / 60f, now, _player, _intents, _sink, _projectiles, _enemies));
        _director.Tick(now, Vector3.Zero, _random.Spawn);
    }

    private EnemyAgent SpawnWaveMother()
    {
        Begin(Plan(10, 1));
        Tick(0f);
        Tick(Arrival);
        return _enemies.Registry.Alive[0];
    }

    private void Begin(WavePlan plan) => _director.Begin(plan, _points, 0f);

    private WavePlan Plan(int cap, int mothers)
    {
        ScalingSpec scaling = Scalings.Design();
        var mode = new ModeSpec(
            new ContentId("mode.brood-test"), new LocKey("mode.brood-test.name"), 1, true, 0,
            scaling, Scalings.Xp(), new[] { new RosterEntry(_motherId, 1) },
            scriptedStages: new[] { new ScriptedStage(1, new[] { new ScriptedSpawn(1, _motherId, mothers) }) });
        var plan = new WavePlan(1, 1);
        new WaveComposer(_catalog, new ThreatBudget(scaling, cap)).Compose(1, mode, plan, _random.Spawn);
        return plan;
    }

    private void Build(
        IDomainEvents events,
        bool eliteMother = false,
        EnemyBehaviourKind motherBehaviour = EnemyBehaviourKind.Static)
    {
        var boss = new BossSpec(_bossId, _bossBodyId, new[] { new BossPhaseSpec(1f) }, 1f);
        var brood = new BroodSpec(_childId, Count, Delay, Radius);
        CharacterSpec character = Character();
        _catalog = new ContentCatalog(new[] { character }, new[]
        {
            Enemy(_motherId, eliteMother, motherBehaviour, brood),
            Enemy(_childId),
            Enemy(_plainId),
            Enemy(_bossBodyId, behaviour: EnemyBehaviourKind.Boss),
        }, bosses: new[] { boss });
        _random = new FixedRandom(0f);
        _sink = events;
        _intents = new RecordingIntents();
        _enemies = new EnemySystem(_catalog, events, _random, new DepthScaling(Scalings.Design()), Capacity);
        _director = new SpawnDirector(_enemies, events);
        _player = new PlayerCombat(character, events, _intents, Capacity);
        _projectiles = new ProjectileSystem(events, Capacity);
    }

    private static EnemySpec Enemy(
        ContentId id,
        bool elite = false,
        EnemyBehaviourKind behaviour = EnemyBehaviourKind.Static,
        BroodSpec brood = null) =>
        new EnemySpec(id, new LocKey("enemy.test.name"), 36f, 2f, 1, 4, Xp, elite,
            0f, 1.2f, 0.4f, 0.6f, 30f, behaviour, brood: brood);

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId("character.oathbound"),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
