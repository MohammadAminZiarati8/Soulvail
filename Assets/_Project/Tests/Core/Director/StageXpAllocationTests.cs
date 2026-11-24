using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Director;

[TestFixture]
public sealed class StageXpAllocationTests
{
    private const int Capacity = 64;
    private const float Arrival = SpawnDirector.TelegraphTime;

    private readonly ContentId _heavy = new("enemy.heavy");
    private readonly ContentId _light = new("enemy.light");
    private readonly ContentId _otherLight = new("enemy.other-light");
    private readonly ContentId _otherHeavy = new("enemy.other-heavy");
    private readonly ContentId _mother = new("enemy.mother");
    private readonly ContentId _child = new("enemy.child");
    private readonly ContentId _bossBody = new("enemy.boss");
    private readonly ContentId _boss = new("boss.test");

    private readonly Vector3[] _points =
    {
        new(10f, 0f, 0f), new(-10f, 0f, 0f), new(0f, 0f, 10f), new(0f, 0f, -10f),
    };

    private readonly ScalingSpec _scaling = new(
        new BudgetCurve(16f, 0f, 0f),
        new WaveCurve(1, 1000, 1, 1),
        new ConcurrencyCurve(16, 1000),
        new StatCurve(0.06f, 4f, 1, 1),
        new StatCurve(0.035f, 3f, 1, 1),
        new StatCurve(0.02f, 1.3f, 5, 0));

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private SpawnDirector _director;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private FixedRandom _random;
    private ContentCatalog _catalog;

    [SetUp]
    public void SetUp()
    {
        CharacterSpec character = new(
            new ContentId("character.oathbound"),
            new LocKey("character.oathbound.name"),
            new LocKey("character.oathbound.description"),
            100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
        _catalog = new ContentCatalog(new[] { character }, new[]
        {
            Body(_heavy, 2f), Body(_light, 1f), Body(_otherLight, 1f), Body(_otherHeavy, 2f),
            Body(_mother, 24f, brood: new BroodSpec(_child, 3, 0.5f, 1.2f)),
            Body(_child, 3f), Body(_bossBody, 40f, EnemyBehaviourKind.Boss),
        }, bosses: new[] { new BossSpec(_boss, _bossBody, new[] { new BossPhaseSpec(1f) }, 1f) });
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _random = new FixedRandom(0f);
        _enemies = new EnemySystem(_catalog, _events, _random, new DepthScaling(_scaling), Capacity,
            usesCampaignBossXp: true);
        _director = new SpawnDirector(_enemies, _events);
        _player = new PlayerCombat(character, _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, Capacity);
    }

    [TestCase(11, 4, 2, 2, 3)]
    [TestCase(1, 1, 0, 0, 0)]
    public void Allocate_UsesWeightsAndSpendsEveryPoint(int budget, int first, int second, int third, int fourth)
    {
        WavePlan plan = Plan(1, new ScriptedSpawn(1, _heavy, 1), new ScriptedSpawn(1, _light, 1),
            new ScriptedSpawn(1, _otherLight, 1), new ScriptedSpawn(1, _otherHeavy, 1));

        Assert.That(ClearRegular(plan, budget, 0f, 0.4f, reverse: false),
            Is.EqualTo(new[] { first, second, third, fourth }));
    }

    [TestCase(true, false, 0.36f)]
    [TestCase(true, true, 0.9f)]
    [TestCase(false, false, 0.36f)]
    [TestCase(false, true, 0.9f)]
    public void RegularStage_KillOrderAndTelegraphTimingPreserveBudget(bool scripted, bool reverse, float step)
    {
        WavePlan plan = scripted
            ? Plan(1, new ScriptedSpawn(1, _heavy, 2), new ScriptedSpawn(1, _light, 2))
            : Plan(1);
        Assert.That(plan.BodyCount(1), Is.EqualTo(4));

        float[] shares = ClearRegular(plan, 11, 0f, step, reverse);
        Assert.That(shares, Is.EqualTo(scripted ? new[] { 4f, 2f, 3f, 2f } : new[] { 3f, 3f, 3f, 2f }));
        Assert.That(shares.Sum(), Is.EqualTo(11));

        _director.Clear();
        Assert.That(ClearRegular(Plan(2), null, 100f, step, reverse), Is.EqualTo(new[] { 2f, 2f, 2f, 2f }));
    }

    [Test]
    public void RegularBrood_ChildrenPayTheirOwnShares()
    {
        _director.Begin(Plan(1, new ScriptedSpawn(1, _mother, 1)), _points, 0f, regularXpBudget: 30);
        Tick(0f);
        Tick(Arrival);
        EnemyAgent mother = _enemies.Registry.Alive[0];

        Assert.That(Kill(mother.Id, Arrival), Is.EqualTo(22));
        Tick(Arrival);
        Assert.That(_director.IsStageComplete, Is.False);
        Assert.That(mother.Brood.Length, Is.Zero);

        float burst = Arrival + 0.5f;
        Tick(burst);
        Assert.That(_director.IsStageComplete, Is.False);
        int[] children = mother.Brood.ToArray();
        Assert.That(children.Select(id => Kill(id, burst)).ToArray(), Is.EqualTo(new[] { 3f, 3f, 2f }));
        Tick(burst);
        Assert.That(_director.IsStageComplete, Is.True);
    }

    [Test]
    public void LegacyAndBossSpawns_KeepTheirRewardPaths()
    {
        Assert.That(ClearRegular(Plan(1), null, 0f, 0.4f, reverse: false), Is.EqualTo(new[] { 2f, 2f, 2f, 2f }));
        _enemies.Clear();
        _director.Begin(Plan(2), _points, 10f, _boss, regularXpBudget: 11);
        Tick(10f);
        EnemyAgent summon = _enemies.SpawnSummon(_mother, Vector3.Zero);
        Assert.That(Kill(summon.Id, 10f), Is.Zero);
        Tick(10.5f);
        Assert.That(summon.Brood.Length, Is.EqualTo(3));
        foreach (int child in summon.Brood)
        {
            Assert.That(Kill(child, 10.5f), Is.Zero);
        }

        Assert.That(_enemies.ApplyDamage(_director.BossEnemyId, 100_000f, 10.5f, _player).Killed, Is.True);
        Assert.That(_enemies.DrainXp(), Is.Zero);
        var drain = typeof(EnemySystem).GetMethod("DrainBossRewards", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(drain.Invoke(_enemies, null), Is.EqualTo(1));
        Assert.That(drain.Invoke(_enemies, null), Is.Zero);
    }

    private float[] ClearRegular(WavePlan plan, int? budget, float start, float step, bool reverse)
    {
        _director.Begin(plan, _points, start, regularXpBudget: budget);
        int count = plan.BodyCount(1);
        for (int tick = 0; tick < 64 && _director.Unspawned > 0; tick++)
        {
            Tick(start + tick * step);
        }

        int[] ids = _enemies.Registry.Alive.ToArray().Where(agent => agent.IsAlive).Select(agent => agent.Id).ToArray();
        Assert.That(ids.Length, Is.EqualTo(count));
        var shares = new float[count];
        float deathTime = start + 64 * step;
        for (int i = 0; i < count; i++)
        {
            int slot = reverse ? count - i - 1 : i;
            shares[slot] = Kill(ids[slot], deathTime);
        }

        Tick(deathTime);
        Assert.That(_director.IsStageComplete, Is.True);
        return shares;
    }

    private float Kill(int id, float now)
    {
        Assert.That(_enemies.Execute(id, now, _player).Killed, Is.True);
        return _enemies.DrainXp();
    }

    private void Tick(float now)
    {
        _enemies.Tick(new EnemyTickContext(1f / 60f, now, _player, _intents, _events, _projectiles, _enemies));
        _director.Tick(now, Vector3.Zero, _random.Spawn);
    }

    private WavePlan Plan(int stage, params ScriptedSpawn[] spawns)
    {
        var mode = new ModeSpec(new ContentId("mode.test"), new LocKey("mode.test.name"), 1, true, 0,
            _scaling, Scalings.Xp(), spawns.Length == 0
                ? new[] { new RosterEntry(_heavy, 1) }
                : spawns.Select(spawn => new RosterEntry(spawn.SpecId, 1)).ToArray(),
            scriptedStages: spawns.Length == 0 ? null : new[] { new ScriptedStage(stage, spawns) });
        var plan = new WavePlan(1, Math.Max(1, spawns.Length));
        new WaveComposer(_catalog, new ThreatBudget(_scaling, Capacity)).Compose(stage, mode, plan, _random.Spawn);
        return plan;
    }

    private static EnemySpec Body(ContentId id, float xp, EnemyBehaviourKind behaviour = EnemyBehaviourKind.Static,
        BroodSpec brood = null) => new(id, new LocKey(id.Value + ".name"), 36f, 2f, 1, 4, xp, false,
            0f, 1.2f, 0.4f, 0.6f, 30f, behaviour, brood: brood);
}
