using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using NVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>SS-15e: shipped content, live Ranger arrows, phase ownership and the Dunes floor.</summary>
[TestFixture]
public sealed class DuneclawStageTests
{
    private const string Root = "Assets/_Project/";
    private const string EnemyPath = Root + "Data/Enemies/Duneclaw.asset";
    private const string BossPath = Root + "Data/Enemies/DuneclawBoss.asset";
    private const float Frame = 1f / 30f;
    private static readonly ContentId Mode = new("mode.sunscar");
    private static readonly ContentId Ranger = new("character.ranger");
    private static readonly ContentId Duneclaw = new("enemy.duneclaw");
    private static readonly ContentId Scorpling = new("enemy.scorpling");

    [Test]
    public void Duneclaw_AssetsRegisteredAndNameLocalised()
    {
        ContentCatalog catalog = Catalog();
        EnemyDefinition asset = Load<EnemyDefinition>(EnemyPath);
        EnemySpec enemy = catalog.Enemy(Duneclaw);
        Assert.That(enemy.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(enemy.MaxHp, Is.EqualTo(asset.ToSpec().MaxHp));
        Assert.That(enemy.NameKey.Key, Is.EqualTo("enemy.duneclaw.name"));
        EnemyLook look = asset.ToLook();
        Assert.That(look.Body, Is.EqualTo(Load<GameObject>(Root + "Prefabs/Enemies/Duneclaw.prefab").GetComponent<EnemyView>()));
        Assert.That(look.Body.GetComponent<DuneclawView>(), Is.Not.Null);
        Assert.That(look.BodyScale, Is.EqualTo(1f));
        Assert.That(look.Projectile, Is.Null);
        foreach (string locale in new[] { "English", "Pseudo" })
        {
            string text = Load<LocalizationTable>(Root + $"Data/Localisation/{locale}.asset").ToDictionary()[enemy.NameKey];
            Assert.That(text, Is.Not.Empty);
            if (locale == "English")
            {
                Assert.That(text, Is.EqualTo("The Duneclaw"));
            }
        }
        BossSpec boss = catalog.Boss(new ContentId("boss.duneclaw"));
        Assert.That(boss.EnemySpecId, Is.EqualTo(Duneclaw));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(1.5f));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(boss.Phases[0].Summons, Is.Empty);
        Assert.That(boss.Phases[2].Summons, Is.Empty);
        Assert.That(boss.Phases[1].Summons.Count, Is.EqualTo(1));
        Assert.That(boss.Phases[1].Summons[0].SpecId, Is.EqualTo(Scorpling));
        Assert.That(boss.Phases[1].Summons[0].Count, Is.EqualTo(3));
    }

    [Test]
    public void Duneclaw_StageFiveRangerDamageProbe()
    {
        using var fight = new Fight();
        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.Id == fight.BossId), 150f);
        float elapsed = fight.Session.State.Time - fight.SpawnedAt;
        TestContext.WriteLine($"Duneclaw {Load<EnemyDefinition>(EnemyPath).ToSpec().MaxHp} base HP; " +
            $"DT-01 stage-5 Ranger level {fight.ArrivalLevel}, authored 13-damage bow at 1.1/s, " +
            $"no tree picks or actives, natural level growth; {elapsed:F2} s including both beats. " +
            "Only player durability is inflated to soak attacks; damage and boss HP are unmodified.");
        Assert.That(elapsed, Is.InRange(75f, 90f));
        Assert.That(fight.Events.Count<PlayerDied>(), Is.Zero);
        Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(fight.Events.Of<BossBeatStarted>().Select(e => e.Seconds), Is.EqualTo(new[] { 1.5f, 1.5f }));
        Assert.That(fight.Events.Count<BossBeatEnded>(), Is.EqualTo(2));
    }

    [Test]
    public void StageFive_WrapperDispatchesDuneclawAndThreeScorplings()
    {
        using var fight = new Fight();
        fight.OnlyBoss = true; // Leave all three adds alive for the 33% cleanup assertion.
        fight.Until(() => fight.Events.Of<BossPhaseChanged>().Any(e => e.Phase == 2), 140f);
        Assert.That(fight.Events.Count<WaveStarted>(), Is.EqualTo(1), "The director announces one boss wave.");
        Assert.That(fight.Events.Count<SpawnTelegraphed>(), Is.Zero, "No normal wave queue accompanies the direct boss spawn.");
        EnemySpawned boss = fight.Events.Of<EnemySpawned>().First();
        Assert.That(boss.SpecId, Is.EqualTo(Duneclaw));
        Assert.That(boss.Position, Is.EqualTo(fight.CentralSpawn));
        Assert.That(fight.Events.Of<StageArrived>().Single().ArenaId.Value, Is.EqualTo("arena.sunscar.dunes"));
        Assert.That(fight.Events.Of<BossPhaseChanged>().All(p => p.EnemyId == boss.Id && p.OfPhases == 3), Is.True,
            "The ordinary segmented boss HUD receives its usual identity and phase count.");
        var adds = fight.Events.Of<EnemySpawned>().Where(e => e.SpecId == Scorpling).ToArray();
        Assert.That(adds.Length, Is.EqualTo(3));
        Assert.That(adds.All(e => !fight.Session.State.IsEnemyAlive(e.Id)), Is.True);
        Assert.That(fight.Events.Count<EnemyDespawned>(), Is.EqualTo(3));
        Assert.That(fight.Events.Of<DuneclawArmed>().Select(e => e.Move).Distinct(),
            Is.EquivalentTo(new[] { DuneclawMove.Sweep, DuneclawMove.Slam }), "Real inner behaviour, not a wrapper with no attacks.");
        int arms = fight.Events.Count<DuneclawArmed>();
        fight.Until(() => fight.Events.Count<DuneclawArmed>() >= arms + 2, 15f);
        Assert.That(fight.Events.Of<DuneclawArmed>().Skip(arms).Select(e => e.Move).Take(2),
            Is.EqualTo(new[] { DuneclawMove.Sweep, DuneclawMove.Slam }));
    }

    [Test]
    public void Scorplings_NoXpAndClearOnBossDeath()
    {
        // 1.5 arrows of scaled health: first arrow enters 66% just above 33%; the next
        // kills directly from phase 1. This exercises death cleanup independently of phase 2.
        using var fight = new Fight(shortFight: true);
        fight.OnlyBoss = true;
        fight.Until(() => fight.Events.Of<BossPhaseChanged>().Any(e => e.Phase == 1), 20f);
        Assert.That(fight.Events.Count<EnemySpawned>(), Is.EqualTo(4));
        float xp = fight.Session.State.Xp;
        int level = fight.Session.State.Level;
        fight.OnlyBoss = false;
        fight.OnlyAdd = true;
        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.SpecId == Scorpling), 10f);
        Assert.That((fight.Session.State.Level, fight.Session.State.Xp), Is.EqualTo((level, xp)));
        Assert.That(fight.Alive.Count(e => e.Value.SpecId == Scorpling), Is.GreaterThan(0));
        fight.OnlyAdd = false;
        fight.OnlyBoss = true;
        fight.Until(() => fight.Events.Of<EnemyDied>().Any(e => e.Id == fight.BossId), 15f);
        Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(fight.Alive, Is.Empty, "Death clears survivors without needing the 33% phase.");
        Assert.That(fight.Events.Of<EnemySpawned>().All(e => !fight.Session.State.IsEnemyAlive(e.Id)), Is.True);
    }

    [Test]
    public void StageFive_BossDeathOpensPortalToNormalStageSix()
    {
        using var fight = new Fight();
        fight.Until(() => fight.Events.Count<StageCleared>() > 0, 150f);
        StageCleared clear = fight.Events.Single<StageCleared>();
        Assert.That(clear.Stage, Is.EqualTo(5));
        Assert.That(clear.GatePosition, Is.EqualTo(fight.Arena.GatePosition.ToNum()));
        Assert.That(clear.GatePosition.Z, Is.GreaterThan(fight.Arena.PlayerStart.z));
        Assert.That(clear.NextArenaId.Value, Is.EqualTo("arena.sunscar.dunes"));
        Assert.That(fight.Events.Count<ModeFinished>(), Is.Zero);
        Assert.That(fight.Events.Count<RunEnded>(), Is.Zero);
        fight.Player = clear.GatePosition;
        fight.Until(() => fight.Session.State.StageIndex == 6 && fight.Events.Count<WaveStarted>() > 0, 15f);
        Assert.That(fight.Events.Of<StageArrived>().Select(e => e.Stage), Is.EqualTo(new[] { 5, 6 }));
        ModeSpec mode = Catalog().Mode(Mode);
        Assert.That(mode.FinalStage, Is.EqualTo(30));
        Assert.That(mode.TryGetBossFor(6, out _), Is.False);
        Assert.That(mode.TryGetScriptedStage(6, out _), Is.False);
        Assert.That(fight.Events.Of<EnemySpawned>().Last().SpecId, Is.Not.EqualTo(Duneclaw));
    }

    [Test]
    public void DunesFloor_HasTwoSlabLocationsWithClearExits()
    {
        using var floor = new Floor();
        using var fight = new Fight();
        fight.Ground = floor.Spots;
        fight.Until(() => fight.Events.Count<DuneclawSlabPlaced>() == 2, 15f);
        var slabs = fight.Events.Of<DuneclawSlabPlaced>().ToArray();
        var stats = new DuneclawStats();
        Assert.That(slabs.Select(e => e.Slot), Is.EqualTo(new[] { 0, 1 }));
        foreach (DuneclawSlabPlaced slab in slabs)
        {
            Assert.That(floor.Spots, Does.Contain(slab.Position));
            foreach (NVector3 anchor in new[] { fight.CentralSpawn, fight.Arena.PlayerStart.ToNum() })
            {
                Assert.That(NVector3.Distance(slab.Position, anchor), Is.GreaterThanOrEqualTo(stats.SlabMinimumSpacing.Value));
            }
            bool exit = false;
            // A continuous, player-width walking escape from the locked circle, before impact.
            float distance = stats.SlamRadius.Value + 0.5f;
            for (int direction = 0; direction < 8 && !exit; direction++)
            {
                float angle = direction * Mathf.PI / 4f;
                Vector3 start = slab.Position.ToUnity() + Floor.Offset;
                Vector3 end = start + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!NavMesh.SamplePosition(end, out NavMeshHit hit, 0.1f, NavMesh.AllAreas)
                    || NavMesh.Raycast(start, hit.position, out _, NavMesh.AllAreas))
                {
                    continue;
                }
                exit = true;
                for (int step = 0; step <= 10; step++)
                {
                    Vector3 point = Vector3.Lerp(start, hit.position, step / 10f);
                    exit &= !Physics.CheckCapsule(point + Vector3.up * 0.3f, point + Vector3.up,
                        0.3f, LayerMask.GetMask("Cover"), QueryTriggerInteraction.Ignore);
                }
            }
            Assert.That(exit, Is.True, $"Walking escape at {slab.Position}");
            Assert.That(distance / Catalog().Character(Ranger).Movement.Speed, Is.LessThan(stats.SlamWindupSeconds.Value));
        }
        Assert.That(NVector3.Distance(slabs[0].Position, slabs[1].Position), Is.GreaterThanOrEqualTo(stats.SlabMinimumSpacing.Value));
        TestContext.WriteLine($"Dunes boss {fight.CentralSpawn}; measured slabs {slabs[0].Position}, {slabs[1].Position}; 2 m clear walking exits.");
    }

    private static ContentCatalog Catalog(bool shortFight = false)
    {
        var boot = new SerializedObject(Load<GameObject>(Root + "Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        CharacterSpec[] characters = BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec());
        EnemySpec[] enemies = BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec());
        ModeSpec[] modes = BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec());
        if (shortFight)
        {
            var preliminary = new ContentCatalog(characters, enemies, modes);
            var random = new SeededRandom(12);
            int level = new TestStart(preliminary).At(Mode, Ranger, random.Seed, random.Capture(), 5).Level;
            float arrow = 13f * (1f + (0.02f * (level - 1)));
            EnemyDefinition copy = Object.Instantiate(Load<EnemyDefinition>(EnemyPath));
            try
            {
                var row = new SerializedObject(copy);
                row.FindProperty("_maxHp").floatValue = arrow * 1.5f / (1f + (0.06f * 4));
                row.ApplyModifiedPropertiesWithoutUndo();
                enemies = enemies.Select(e => e.Id == Duneclaw ? copy.ToSpec() : e).ToArray();
            }
            finally { Object.DestroyImmediate(copy); }
        }
        CharacterDefinition ranger = Object.Instantiate(Load<CharacterDefinition>(Root + "Data/Characters/Ranger.asset"));
        try
        {
            var row = new SerializedObject(ranger);
            row.FindProperty("_maxHp").floatValue = 100000f;
            row.ApplyModifiedPropertiesWithoutUndo();
            characters = characters.Select(c => c.Id == Ranger ? ranger.ToSpec() : c).ToArray();
        }
        finally { Object.DestroyImmediate(ranger); }
        return new ContentCatalog(characters, enemies, modes,
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
    }

    private sealed class Fight : IDisposable
    {
        public readonly RecordingEvents Events = new();
        public readonly IntentBuffer Intents = new();
        public readonly Dictionary<int, EnemySpawned> Alive = new();
        public readonly RunSession Session;
        public readonly ArenaView Arena = Load<GameObject>(Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab").GetComponent<ArenaView>();
        public readonly int ArrivalLevel;
        public readonly NVector3 CentralSpawn;
        public NVector3 Player;
        public IReadOnlyList<NVector3> Ground = Array.Empty<NVector3>();
        public bool OnlyBoss, OnlyAdd;
        public int BossId;
        public float SpawnedAt;
        private readonly WorldSnapshot _world = new(64);
        private readonly Dictionary<int, NVector3> _positions = new();
        private int _read;

        public Fight(bool shortFight = false)
        {
            ContentCatalog catalog = Catalog(shortFight);
            var random = new SeededRandom(12);
            Session = new RunSession(catalog, random, Events, Intents,
                new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), Events), 64, 28, 64);
            RunSnapshot restore = new TestStart(catalog).At(Mode, Ranger, random.Seed, random.Capture(), 5);
            ArrivalLevel = restore.Level;
            Player = Arena.PlayerStart.ToNum();
            CentralSpawn = Arena.SpawnPoints.OrderBy(p => p.sqrMagnitude).First().ToNum();
            Session.Start(new RunConfig(Mode, Ranger, random.Seed, 5, SpawnPlan.Empty, restore));
        }

        public void Until(Func<bool> done, float seconds)
        {
            for (int i = 0; i < seconds / Frame && !done(); i++)
                Tick();
            Assert.That(done(), Is.True, $"Condition timed out at {Session.State.Time:F2} s; enemies {Alive.Count}.");
        }

        private void Tick()
        {
            _world.Clear();
            _world.Dt = Frame;
            _world.PlayerPosition = Player;
            _world.HasGate = true;
            _world.GatePosition = Arena.GatePosition.ToNum();
            _world.SpawnPoints = Arena.SpawnPoints.Select(p => p.ToNum()).ToArray();
            _world.OpenGround = Ground;
            foreach (EnemySpawned enemy in Alive.Values)
            {
                if ((OnlyBoss && enemy.SpecId != Duneclaw) || (OnlyAdd && enemy.SpecId != Scorpling))
                    continue;
                _world.AddEnemy() = new EnemySense { Id = enemy.Id, Position = _positions[enemy.Id], HasLineOfSight = true };
            }
            Intents.Clear();
            Session.Tick(_world);
            for (; _read < Events.All.Count; _read++)
            {
                switch (Events.All[_read])
                {
                    case EnemySpawned spawned:
                        Alive.Add(spawned.Id, spawned);
                        _positions[spawned.Id] = spawned.Position;
                        if (spawned.SpecId == Duneclaw)
                        { BossId = spawned.Id; SpawnedAt = Session.State.Time; }
                        break;
                    case EnemyDied died:
                        Alive.Remove(died.Id);
                        break;
                    case EnemyDespawned gone:
                        Alive.Remove(gone.Id);
                        break;
                }
            }
            foreach (EnemyMoveIntent move in Intents.EnemyMoves)
            {
                if (Alive.ContainsKey(move.Id))
                    _positions[move.Id] += move.Velocity * Frame;
            }
        }

        public void Dispose() => Session.End();
    }

    private sealed class Floor : IDisposable
    {
        public static readonly Vector3 Offset = new(2000f, 0f, 2000f);
        private readonly GameObject _parent;
        private readonly NavMeshDataInstance _nav;
        public readonly IReadOnlyList<NVector3> Spots;

        public Floor()
        {
            _parent = new GameObject("SS-15e isolated floor");
            _parent.SetActive(false);
            GameObject room = Object.Instantiate(Load<GameObject>(Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab"), _parent.transform);
            room.transform.position = Offset;
            ArenaView arena = room.GetComponent<ArenaView>();
            arena.Surface.enabled = false;
            _parent.SetActive(true);
            _nav = NavMesh.AddNavMeshData(arena.Surface.navMeshData, Offset, Quaternion.identity);
            var sense = new OpenGroundSense(LayerMask.GetMask("Cover"), null);
            sense.Sense(arena);
            Spots = sense.Spots.Select(p => p - Offset.ToNum()).ToArray();
        }

        public void Dispose()
        {
            _nav.Remove();
            Object.DestroyImmediate(_parent);
            Physics.SyncTransforms();
        }
    }

    private static TSpec[] BootSpecs<TDefinition, TSpec>(SerializedObject boot, string field, Func<TDefinition, TSpec> convert)
        where TDefinition : ScriptableObject
    {
        SerializedProperty rows = boot.FindProperty(field);
        return Enumerable.Range(0, rows.arraySize).Select(i => convert((TDefinition)rows.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray();
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
