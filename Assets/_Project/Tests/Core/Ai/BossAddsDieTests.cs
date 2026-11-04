using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// JG-26d: when any boss dies, everything it summoned dies on the same tick — an ordinary death
/// that pays no experience and never explodes.
/// </summary>
/// <remarks>
/// The <c>Session_</c> rows run a whole <see cref="RunSession"/>, because the boss-behaviour map
/// and the stage flow are private to it; they reach the run's <see cref="EnemySystem"/> and
/// <see cref="PlayerCombat"/> by reflection, as <c>BossDispatchTests</c> does.
/// </remarks>
[TestFixture]
public sealed class BossAddsDieTests
{
    private const string HuskId = "enemy.husk";
    private const string BombId = "enemy.bomb";
    private const string EliteId = "enemy.elite";
    private const string BossBodyId = "enemy.warden";
    private const string BossId = "boss.warden";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    private const int HuskCost = 4;
    private const float HuskXp = 12f;
    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const int BossStage = 5;
    private const float Ring = 10f;
    private const float Lethal = 1e9f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    /// <summary>Every boss <c>RunSession</c>'s factory builds, with the body each one wears.</summary>
    private static readonly object[] FactoryBosses =
    {
        new object[] { "boss.warden", "enemy.warden" },
        new object[] { "boss.croaker", "enemy.croaker" },
        new object[] { "boss.mother_pitcher", "enemy.mother_pitcher" },
        new object[] { "boss.mossguard_warlord", "enemy.mossguard_warlord" },
        new object[] { "boss.thornheart", "enemy.thornheart" },
        new object[] { "boss.gravemaw", "enemy.gravemaw" },
    };

    private RecordingEvents _events;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- Rule 1 ----------------------------------------------------------------------------------

    [Test]
    public void SpawnSummon_MarksIsSummon()
    {
        EnemySystem system = System();

        Assert.That(system.SpawnSummon(new ContentId(HuskId), Vector3.Zero).IsSummon, Is.True);
    }

    [Test]
    public void Spawn_IsNotSummon()
    {
        EnemySystem system = System();

        Assert.That(system.Spawn(new ContentId(HuskId), Vector3.Zero).IsSummon, Is.False);
        Assert.That(system.SpawnBoss(new ContentId(BossId), Vector3.Zero).IsSummon, Is.False);
    }

    [Test]
    public void PooledSummon_ReusedByWave_IsNotSummon()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();

        EnemyAgent add = system.SpawnSummon(new ContentId(HuskId), Vector3.Zero);
        system.ApplyDamage(add.Id, Lethal, 0f, player);
        system.Despawn(add.Id);

        EnemyAgent reused = system.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.That(reused, Is.SameAs(add), "Sanity: the free list recycled the summon's agent.");
        Assert.That(reused.IsSummon, Is.False);
    }

    // ---- Rule 2 ----------------------------------------------------------------------------------

    [Test]
    public void BossDeath_KillsEveryLivingSummon_AfterTheBoss()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();

        EnemyAgent boss = system.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));
        EnemyAgent first = system.SpawnSummon(new ContentId(HuskId), new Vector3(3f, 0f, 0f));
        EnemyAgent second = system.SpawnSummon(new ContentId(HuskId), new Vector3(-3f, 0f, 0f));

        DamageResult result = system.ApplyDamage(boss.Id, Lethal, 1f, player);

        Assert.That(result.Killed, Is.True);
        Assert.That(first.IsAlive, Is.False);
        Assert.That(second.IsAlive, Is.False);

        IReadOnlyList<EnemyDied> died = _events.Of<EnemyDied>();

        Assert.That(died.Count, Is.EqualTo(3));
        Assert.That(died[0].Id, Is.EqualTo(boss.Id), "The boss's own death comes first.");
        Assert.That(died[1].Id, Is.EqualTo(first.Id), "Then the summons, in registry order.");
        Assert.That(died[2].Id, Is.EqualTo(second.Id));
    }

    // ---- Rule 3 ----------------------------------------------------------------------------------

    [Test]
    public void BossDeath_SummonsPublishDied_CountAsKills_PayNoXp()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();

        EnemyAgent boss = system.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));
        system.SpawnSummon(new ContentId(HuskId), new Vector3(3f, 0f, 0f));
        system.SpawnSummon(new ContentId(HuskId), new Vector3(-3f, 0f, 0f));

        system.ApplyDamage(boss.Id, Lethal, 1f, player);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(3));
        Assert.That(system.DrainKills(), Is.EqualTo(3));
        Assert.That(system.DrainXp(), Is.EqualTo(BossXp), "The boss pays; its summons pay nothing.");

        var deaths = new EnemyDeath[Capacity];
        int count = system.DrainDeaths(deaths);

        Assert.That(count, Is.EqualTo(3));
        Assert.That(deaths[0].WasBoss, Is.True);
        Assert.That(deaths[1].SpecId.Value, Is.EqualTo(HuskId));
        Assert.That(deaths[2].SpecId.Value, Is.EqualTo(HuskId));
    }

    // ---- Rule 4 ----------------------------------------------------------------------------------

    [Test]
    public void BossDeath_ExplodingSummon_DoesNotExplode_PlayerUnhurt()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();
        float before = player.Health.Current;

        EnemyAgent boss = system.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));

        // At the player's feet, inside its 40 m blast: an ordinary death here would hurt.
        EnemyAgent bomb = system.SpawnSummon(new ContentId(BombId), new Vector3(0f, 0f, 1f));

        system.ApplyDamage(boss.Id, Lethal, 1f, player);

        Assert.That(bomb.IsAlive, Is.False);
        Assert.That(_events.Count<EnemyExploded>(), Is.Zero);
        Assert.That(_events.Count<EnemyChainLit>(), Is.Zero);
        Assert.That(player.Health.Current, Is.EqualTo(before));
    }

    // ---- Rule 5 ----------------------------------------------------------------------------------

    [Test]
    public void BossDeath_WaveEnemiesAndElitesLive()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();

        EnemyAgent boss = system.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));
        EnemyAgent wave = system.Spawn(new ContentId(HuskId), new Vector3(3f, 0f, 0f));
        EnemyAgent elite = system.Spawn(new ContentId(EliteId), new Vector3(-3f, 0f, 0f));
        EnemyAgent add = system.SpawnSummon(new ContentId(HuskId), new Vector3(0f, 0f, 3f));

        system.ApplyDamage(boss.Id, Lethal, 1f, player);

        Assert.That(add.IsAlive, Is.False);
        Assert.That(wave.IsAlive, Is.True);
        Assert.That(elite.IsAlive, Is.True);
    }

    [Test]
    public void SummonDeath_KillsNoOtherSummon()
    {
        EnemySystem system = System();
        PlayerCombat player = Player();

        EnemyAgent boss = system.SpawnBoss(new ContentId(BossId), new Vector3(0f, 0f, 5f));
        EnemyAgent first = system.SpawnSummon(new ContentId(HuskId), new Vector3(3f, 0f, 0f));
        EnemyAgent second = system.SpawnSummon(new ContentId(HuskId), new Vector3(-3f, 0f, 0f));

        system.ApplyDamage(first.Id, Lethal, 1f, player);

        Assert.That(first.IsAlive, Is.False);
        Assert.That(second.IsAlive, Is.True);
        Assert.That(boss.IsAlive, Is.True);
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
    }

    // ---- Rule 6 ----------------------------------------------------------------------------------

    [Test]
    public void BossStage_KillBossWithAddsStanding_StageClearsSameTick()
    {
        RunSession session = BossStageSession(BossId, BossBodyId);
        (EnemySystem enemies, PlayerCombat player, EnemyAgent boss) = StandTheBossUp(session, BossBodyId);

        enemies.SpawnSummon(new ContentId(HuskId), new Vector3(3f, 0f, 0f));
        enemies.SpawnSummon(new ContentId(HuskId), new Vector3(-3f, 0f, 0f));

        Assert.That(_events.Count<StageCleared>(), Is.Zero, "Sanity: nothing has cleared yet.");

        KillBoss(enemies, player, boss, session);

        Assert.That(enemies.LivingCount(), Is.Zero, "Nothing breathes once the boss is down.");

        session.Tick(Snapshot());

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
    }

    // ---- Rule 2, every boss ------------------------------------------------------------------------

    [TestCaseSource(nameof(FactoryBosses))]
    public void EveryBossInTheFactory_AddsDieWithIt(string bossId, string bodyId)
    {
        RunSession session = BossStageSession(bossId, bodyId);
        (EnemySystem enemies, PlayerCombat player, EnemyAgent boss) = StandTheBossUp(session, bodyId);

        Assert.That(
            ((BossBehaviour)boss.Behaviour).Inner,
            Is.Not.Null,
            "Sanity: the factory built this boss's behaviour.");

        EnemyAgent add = enemies.SpawnSummon(new ContentId(HuskId), new Vector3(3f, 0f, 0f));

        KillBoss(enemies, player, boss, session);

        Assert.That(add.IsAlive, Is.False, $"{bossId}'s summon outlived it.");
    }

    // ---- Fixture: one EnemySystem ------------------------------------------------------------------

    private const float BossXp = 300f;

    private EnemySystem System() => new EnemySystem(
        Catalog(Mode(BossId), WardenBoss(BossId, BossBodyId)),
        _events,
        new FixedRandom(),
        new DepthScaling(Scalings.Design()),
        Capacity);

    private PlayerCombat Player() => new PlayerCombat(Oathbound(), _events, new RecordingIntents(), Capacity);

    // ---- Fixture: a whole run on a boss stage ------------------------------------------------------

    private RunSession BossStageSession(string bossId, string bodyId) => new RunSession(
        Catalog(Mode(bossId), WardenBoss(bossId, bodyId)),
        new FixedRandom(0, Alternating(8_192)),
        _events,
        new RecordingIntents(),
        new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
        Capacity,
        DeviceCap,
        ProjectileCapacity);

    /// <summary>Starts the run, ticks until the boss is up, and hands back what the rows act on.</summary>
    private (EnemySystem, PlayerCombat, EnemyAgent) StandTheBossUp(RunSession session, string bodyId)
    {
        session.Start(new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            0,
            BossStage,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
            restore: null));

        for (int i = 0; i < 1_200 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(Snapshot());
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(1), "The fixture failed to stand the boss up.");

        var enemies = (EnemySystem)typeof(RunState)
            .GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session.State);
        var player = (PlayerCombat)typeof(RunState)
            .GetProperty("Combat", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session.State);

        EnemyAgent boss = enemies.Registry.Alive[0];

        Assert.That(boss.Spec.Id.Value, Is.EqualTo(bodyId));

        return (enemies, player, boss);
    }

    /// <summary>
    /// Kills the boss through the one damage door, lowering whatever invulnerability its behaviour
    /// holds first (a burrowed Gravemaw, a guarding Warlord): these rows are about what follows the
    /// death, not about how a boss is reached.
    /// </summary>
    private static void KillBoss(EnemySystem enemies, PlayerCombat player, EnemyAgent boss, RunSession session)
    {
        boss.Health.SetExternalInvulnerable(false);

        DamageResult result = enemies.ApplyDamage(boss.Id, Lethal, session.State.Time, player);

        Assert.That(result.Killed, Is.True, "Sanity: the boss died.");
    }

    private static WorldSnapshot Snapshot() => new WorldSnapshot(Capacity)
    {
        Dt = 1f / 60f,
        PlayerPosition = Vector3.Zero,
        HasGate = true,
        GatePosition = Door,
        SpawnPoints = Points(8),
    };

    // ---- Content -----------------------------------------------------------------------------------

    private static ContentCatalog Catalog(ModeSpec mode, BossSpec boss) => new ContentCatalog(
        new[] { Oathbound() },
        new[]
        {
            Husk(), Bomb(), Elite(),
            Body("enemy.warden"), Body("enemy.croaker"), Body("enemy.mother_pitcher"),
            Body("enemy.mossguard_warlord"), Body("enemy.thornheart"), Body("enemy.gravemaw"),
        },
        new[] { mode },
        skills: null,
        trees: null,
        bosses: new[] { boss });

    /// <summary>A mode of static Husks with <paramref name="bossId"/> on <see cref="BossStage"/>.</summary>
    private static ModeSpec Mode(string bossId) => new ModeSpec(
        new ContentId(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0)),
        Scalings.Xp(),
        new[] { new RosterEntry(new ContentId(HuskId), 1) },
        arenas: null,
        bossRoster: new[] { new BossRosterEntry(new ContentId(bossId), BossStage) });

    /// <summary>One phase and no authored adds: every summon here is one a row made.</summary>
    private static BossSpec WardenBoss(string bossId, string bodyId) => new BossSpec(
        new ContentId(bossId),
        new ContentId(bodyId),
        new[] { new BossPhaseSpec(1f) },
        1.5f);

    private static EnemySpec Body(string id) => new EnemySpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        maxHp: 3_200f,
        moveSpeed: 1.8f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: BossXp,
        isElite: false,
        contactDamage: 22f,
        reach: 2.5f,
        windupTime: 0.9f,
        recoverTime: 1.2f,
        aggroRange: 40f,
        behaviour: EnemyBehaviourKind.Boss);

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskXp,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static EnemySpec Elite() => new EnemySpec(
        new ContentId(EliteId),
        new LocKey("enemy.elite.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskXp,
        isElite: true,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>A chained bomber whose blast would catch a player at the origin.</summary>
    private static EnemySpec Bomb() => new EnemySpec(
        new ContentId(BombId),
        new LocKey("enemy.bomb.name"),
        maxHp: 10f,
        moveSpeed: 0.01f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskXp,
        isElite: false,
        contactDamage: 50f,
        reach: 40f,
        windupTime: 0.05f,
        recoverTime: 0.6f,
        aggroRange: 60f,
        behaviour: EnemyBehaviourKind.Bloater,
        explosion: new ExplosionSpec(40f, chainRadius: 40f));

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }
}
