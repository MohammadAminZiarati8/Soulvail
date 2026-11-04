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
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>JG-30f: the authored final fight, including moving bodies, phase adds and the win.</summary>
[TestFixture]
public sealed class ApexStageTests
{
    private const string Root = "Assets/_Project/";
    private const string ApexPath = Root + "Data/Enemies/Apex.asset";
    private const string BossPath = Root + "Data/Enemies/ApexBoss.asset";
    private const string StonesPath = Root + "Prefabs/Arenas/Jungle/Cover_ApexStones.prefab";
    private const string JunglePath = Root + "Data/Modes/Jungle.asset";
    private const string ArenaPath = Root + "Prefabs/Arenas/Jungle/Arena_Clearing.prefab";
    private const string BootPath = Root + "Prefabs/Composition/BootScope.prefab";
    private const string PantherId = "enemy.shadow_panther";
    private const int Stage = 30;
    private const float Frame = 1f / 30f;
    // The existing late-Jungle boss probes use 33 DPS. Calibrate the Ranger's arrow to 30 at
    // her authored 1.1 shots/s. Only durability is inflated to survive deliberately eating hits;
    // no damage is injected, no enemy health is cut and no attacks or phase beats are skipped.
    private const float ExpectedDps = 33f;

    [Test]
    public void Apex_AssetValid_BossKind_Prefab_Scale()
    {
        EnemyDefinition asset = Load<EnemyDefinition>(ApexPath);
        EnemySpec enemy = asset.ToSpec();
        EnemyLook look = asset.ToLook();

        Assert.That(enemy.Id.Value, Is.EqualTo("enemy.apex"));
        Assert.That(enemy.NameKey.Key, Is.EqualTo("enemy.apex.name"));
        Assert.That(enemy.Behaviour, Is.EqualTo(EnemyBehaviourKind.Boss));
        Assert.That(enemy.MoveSpeed, Is.EqualTo(new ApexStats().RunSpeed.Value), "GD §9.2: the one fast boss.");
        Assert.That(enemy.MaxHp, Is.GreaterThan(632f));
        Assert.That((enemy.ContactDamage, enemy.XpValue), Is.EqualTo((20f, 700f)));
        Assert.That(look.BodyScale, Is.EqualTo(1.6f));
        Assert.That(look.Body == Load<EnemyView>(Root + "Prefabs/Enemies/Apex.prefab"), Is.True);
        Assert.That(look.Cover == Load<GameObject>(StonesPath), Is.True);
        Assert.That(look.Projectile, Is.Null);
    }

    [Test]
    public void ApexBoss_PhasesAt66And33_TwoShadowPanthersAt66Only()
    {
        BossSpec boss = Load<BossDefinition>(BossPath).ToSpec();

        Assert.That(boss.Id.Value, Is.EqualTo("boss.apex"));
        Assert.That(boss.EnemySpecId.Value, Is.EqualTo("enemy.apex"));
        Assert.That(boss.SpawnsCentral, Is.True);
        Assert.That(boss.BeatSeconds, Is.EqualTo(Load<BossDefinition>(Root + "Data/Enemies/GravemawBoss.asset").ToSpec().BeatSeconds));
        Assert.That(boss.Phases.Select(p => p.EntersBelow), Is.EqualTo(new[] { 1f, 0.66f, 0.33f }));
        Assert.That(boss.Phases[0].Summons, Is.Empty);
        Assert.That(boss.Phases[2].Summons, Is.Empty);
        Assert.That(boss.Phases[1].Summons.Count, Is.EqualTo(1));
        Assert.That(boss.Phases[1].Summons[0].SpecId.Value, Is.EqualTo(PantherId));
        Assert.That(boss.Phases[1].Summons[0].Count, Is.EqualTo(2));
    }

    [Test]
    public void ApexStones_FourClearOfStarts_BlockLaneProbe()
    {
        // Isolate physics from whichever scene the owner has open.
        var offset = new UnityEngine.Vector3(2000f, 0f, 2000f);
        ArenaView arena = Load<GameObject>(ArenaPath).GetComponent<ArenaView>();
        UnityEngine.Vector3 bossStart = arena.SpawnPoints.OrderBy(p => p.sqrMagnitude).First();
        // CoverViews anchors this prefab at the boss's spawn, rather than the arena origin.
        GameObject stones = Object.Instantiate(Load<GameObject>(StonesPath), offset + bossStart, Quaternion.identity);
        try
        {
            Physics.SyncTransforms();
            BoxCollider[] boxes = stones.GetComponentsInChildren<BoxCollider>();
            Assert.That(boxes.Length, Is.EqualTo(4));
            var probe = new LaneProbeAdapter(LayerMask.GetMask("Cover"));
            var stats = new ApexStats();
            foreach (BoxCollider box in boxes)
            {
                Assert.That(box.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")), box.name);
                Assert.That(box.isTrigger, Is.False, box.name);
                foreach (UnityEngine.Vector3 start in new[] { arena.PlayerStart, bossStart })
                {
                    UnityEngine.Vector3 nearest = box.ClosestPoint(offset + start);
                    Assert.That(UnityEngine.Vector3.Distance(nearest, offset + start), Is.GreaterThanOrEqualTo(6f), box.name);
                }
                // Sweep the actual Apex lane radius through each stone, then prove the same
                // query misses with that collider disabled. Geometry, not a layer-name check.
                UnityEngine.Vector3 centre = box.bounds.center;
                var from = new UnityEngine.Vector3(centre.x, 0f, centre.z - 5f);
                var lane = new LaneProbeIntent(1, from.ToNum(), Vector3.UnitZ, stats.DashLength.Value, stats.DashHalfWidth.Value);
                Assert.That(probe.Probe(lane, out float distance), Is.True, box.name);
                Assert.That(distance, Is.GreaterThan(0f).And.LessThan(5f), box.name);
                box.enabled = false;
                Physics.SyncTransforms();
                Assert.That(probe.Probe(lane, out _), Is.False, box.name + " negative control");
                box.enabled = true;
            }
        }
        finally
        {
            Object.DestroyImmediate(stones);
        }
    }

    [Test]
    public void ApexStones_GapsAtLeastThree()
    {
        GameObject stones = Object.Instantiate(Load<GameObject>(StonesPath), new UnityEngine.Vector3(2000f, 0f, 2000f), Quaternion.identity);
        try
        {
            Physics.SyncTransforms();
            BoxCollider[] boxes = stones.GetComponentsInChildren<BoxCollider>();
            Assert.That(boxes.Length, Is.EqualTo(4));
            for (int i = 0; i < boxes.Length; i++)
            {
                Bounds a = boxes[i].bounds;
                Assert.That(Mathf.Min(a.size.x, a.size.z), Is.GreaterThanOrEqualTo(2.5f), "Broad kit stones read at phone zoom.");
                Assert.That(boxes[i].GetComponentInChildren<MeshRenderer>(), Is.Not.Null);
                for (int j = i + 1; j < boxes.Length; j++)
                {
                    Bounds b = boxes[j].bounds;
                    float x = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
                    float z = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
                    Assert.That(Mathf.Sqrt(x * x + z * z), Is.GreaterThanOrEqualTo(3f), $"{boxes[i].name} / {boxes[j].name}");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(stones);
        }
    }

    [Test]
    public void Stage30_BossIsApex_CroakerElsewhereUnchanged()
    {
        ModeSpec jungle = Load<ModeDefinition>(JunglePath).ToSpec();
        Assert.That(jungle.FinalStage, Is.EqualTo(Stage));
        Assert.That(jungle.IsEndless, Is.False);
        Assert.That(jungle.TryGetScriptedStage(Stage, out _), Is.False);
        for (int stage = 1; stage <= Stage; stage++)
        {
            bool hasBoss = jungle.TryGetBossFor(stage, out ContentId boss);
            Assert.That(hasBoss, Is.EqualTo(stage % 5 == 0), $"Stage {stage}");
            if (!hasBoss)
            {
                continue;
            }
            string expected = stage switch
            {
                10 => "boss.mother_pitcher",
                15 => "boss.mossguard_warlord",
                20 => "boss.thornheart",
                25 => "boss.gravemaw",
                30 => "boss.apex",
                _ => "boss.croaker",
            };
            Assert.That(boss.Value, Is.EqualTo(expected), $"Stage {stage}");
        }
    }

    [Test]
    public void Apex_NameLocalised()
    {
        var key = Load<EnemyDefinition>(ApexPath).ToSpec().NameKey;
        Assert.That(Load<LocalizationTable>(Root + "Data/Localisation/English.asset").ToDictionary()[key], Is.EqualTo("The Apex"));
        Assert.That(Load<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset").ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Apex_ProbeFight_SeventyFiveToOneTwenty()
    {
        var fight = Fight();
        TestContext.WriteLine($"Stage-30 Ranger, {ExpectedDps:F0} expected DPS, no dodges: {fight.Seconds:F2} s; Apex base HP {Load<EnemyDefinition>(ApexPath).ToSpec().MaxHp:F0}.");
        Assert.That(fight.Seconds, Is.InRange(75f, 120f));
        Assert.That(fight.Events.Count<PlayerDied>(), Is.Zero);
        Assert.That(fight.DamageTaken, Is.GreaterThan(0f), "The stationary Ranger actually ate attacks.");
        Assert.That(fight.Events.Of<BossPhaseChanged>().Select(e => e.Phase), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(fight.Events.Of<EnemySpawned>().Count(e => e.SpecId.Value == PantherId), Is.EqualTo(2));
        Assert.That(fight.AloneAt33, Is.True, "The 33% beat clears the two panthers.");
    }

    [Test]
    public void Stage30_ApexDeath_EndsRunAsWin()
    {
        var fight = Fight();
        Assert.That(fight.StillRunning, Is.False);
        Assert.That(fight.Events.Of<EnemySpawned>().First().SpecId.Value, Is.EqualTo("enemy.apex"));
        Assert.That(fight.Events.Count<StageCleared>(), Is.EqualTo(1));
        Assert.That(fight.Events.Single<ModeFinished>().FinalStage, Is.EqualTo(Stage));
        Assert.That(fight.Events.Single<ModeFinished>().ModeId.Value, Is.EqualTo("mode.jungle"));
        Assert.That(fight.Events.Count<RunEnded>(), Is.EqualTo(1));
        Assert.That(fight.Events.Count<PlayerDied>(), Is.Zero);
        var gone = new HashSet<int>(fight.Events.Of<EnemyDied>().Select(e => e.Id)
            .Concat(fight.Events.Of<EnemyDespawned>().Select(e => e.Id)));
        Assert.That(fight.Events.Of<EnemySpawned>().All(e => gone.Contains(e.Id)), Is.True,
            "The boss dies and every summoned panther is killed or cleared at a phase beat.");
        int finished = fight.Events.All.ToList().FindIndex(e => e is ModeFinished);
        int ended = fight.Events.All.ToList().FindIndex(e => e is RunEnded);
        Assert.That(ended, Is.GreaterThan(finished), "The win is announced before the session ends.");
    }

    private static (float Seconds, RecordingEvents Events, float DamageTaken, bool AloneAt33, bool StillRunning) Fight()
    {
        ContentCatalog catalog = Catalog();
        var random = new SeededRandom(12);
        var events = new RecordingEvents();
        var intents = new IntentBuffer();
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
        var alive = new Dictionary<int, EnemySpawned>();
        var at = new Dictionary<int, Vector3>();
        int read = 0;
        int apexId = 0;
        float began = -1f;
        float died = -1f;
        bool aloneAt33 = false;
        ArenaView arena = Load<GameObject>(ArenaPath).GetComponent<ArenaView>();
        Vector3 playerStart = arena.PlayerStart.ToNum();
        Vector3[] spawnPoints = arena.SpawnPoints.Select(p => p.ToNum()).ToArray();
        var world = new WorldSnapshot(64)
        {
            Dt = Frame,
            PlayerPosition = playerStart,
            SpawnPoints = spawnPoints,
        };
        var offset = new UnityEngine.Vector3(2000f, 0f, 2000f);
        GameObject stones = Object.Instantiate(Load<GameObject>(StonesPath), offset, Quaternion.identity);
        var laneProbe = new LaneProbeAdapter(LayerMask.GetMask("Cover"));
        try
        {
            Physics.SyncTransforms();
            // No tree picks or actives: the probe isolates the established expected-DPS budget.
            session.Start(new RunConfig(new ContentId("mode.jungle"), new ContentId("character.ranger"), random.Seed, Stage, SpawnPlan.Empty, restore: null));
            for (int frame = 0; frame < 5400 && session.IsRunning; frame++)
            {
                world.Clear();
                world.Dt = Frame;
                world.PlayerPosition = playerStart;
                world.SpawnPoints = spawnPoints;
                foreach (EnemySpawned enemy in alive.Values)
                {
                    ref EnemySense sense = ref world.AddEnemy();
                    sense.Id = enemy.Id;
                    sense.Position = at[enemy.Id];
                    sense.Velocity = Vector3.Zero;
                    sense.HasLineOfSight = true;
                    sense.PathDirectionToPlayer = System.Numerics.Vector2.Zero;
                }
                intents.Clear();
                session.Tick(world);
                bool entered33 = false;
                for (; read < events.All.Count; read++)
                {
                    switch (events.All[read])
                    {
                        case EnemySpawned spawned:
                            alive.Add(spawned.Id, spawned);
                            at[spawned.Id] = spawned.Position;
                            if (spawned.SpecId.Value == "enemy.apex")
                            {
                                apexId = spawned.Id;
                                began = session.State.Time;
                                stones.transform.position = offset + spawned.Position.ToUnity();
                                Physics.SyncTransforms();
                            }
                            break;
                        case EnemyDied dead:
                            alive.Remove(dead.Id);
                            if (dead.Id == apexId)
                            {
                                died = session.State.Time;
                            }
                            break;
                        case EnemyDespawned despawned:
                            alive.Remove(despawned.Id);
                            break;
                        case BossPhaseChanged phase:
                            entered33 |= phase.Phase == 2;
                            break;
                    }
                }
                if (entered33)
                {
                    aloneAt33 = alive.Count == 1 && alive.ContainsKey(apexId);
                }
                foreach (EnemyMoveIntent move in intents.EnemyMoves)
                {
                    if (alive.ContainsKey(move.Id))
                    {
                        at[move.Id] += move.Velocity * Frame;
                    }
                }
                if (session.IsRunning)
                {
                    foreach (LaneProbeIntent lane in intents.LaneProbes)
                    {
                        var shifted = new LaneProbeIntent(lane.EnemyId, lane.From + offset.ToNum(), lane.Direction, lane.Reach, lane.Radius);
                        bool hit = laneProbe.Probe(shifted, out float distance);
                        session.ReportLaneStop(lane.EnemyId, hit, distance);
                    }
                }
            }
            Assert.That(began, Is.GreaterThanOrEqualTo(0f), "The authored boss spawned.");
            Assert.That(died, Is.GreaterThan(began), "The Ranger's real arrows killed the authored boss before the timeout.");
            return (died - began, events, session.State.PlayerMaxHp - session.State.PlayerHp, aloneAt33, session.IsRunning);
        }
        finally
        {
            session.End();
            Object.DestroyImmediate(stones);
        }
    }

    private static ContentCatalog Catalog()
    {
        var boot = new SerializedObject(Load<GameObject>(BootPath).GetComponent<BootScope>());
        CharacterSpec[] characters = BootSpecs<CharacterDefinition, CharacterSpec>(boot, "_characters", d => d.ToSpec());
        CharacterDefinition ranger = Object.Instantiate(Load<CharacterDefinition>(Root + "Data/Characters/Ranger.asset"));
        try
        {
            var row = new SerializedObject(ranger);
            row.FindProperty("_maxHp").floatValue = 100000f;
            float rate = row.FindProperty("_weaponSwingsPerSecond").floatValue;
            row.FindProperty("_weaponDamage").floatValue = ExpectedDps / rate;
            row.ApplyModifiedPropertiesWithoutUndo();
            CharacterSpec probe = ranger.ToSpec();
            characters = characters.Select(c => c.Id == probe.Id ? probe : c).ToArray();
        }
        finally
        {
            Object.DestroyImmediate(ranger);
        }
        return new ContentCatalog(
            characters,
            BootSpecs<EnemyDefinition, EnemySpec>(boot, "_enemies", d => d.ToSpec()),
            BootSpecs<ModeDefinition, ModeSpec>(boot, "_modes", d => d.ToSpec()),
            BootSpecs<SkillDefinition, SkillSpec>(boot, "_skills", d => d.ToSpec()),
            BootSpecs<SkillTreeDefinition, SkillTreeSpec>(boot, "_trees", d => d.ToSpec()),
            BootSpecs<BossDefinition, BossSpec>(boot, "_bosses", d => d.ToSpec()));
    }

    private static TSpec[] BootSpecs<TDefinition, TSpec>(SerializedObject boot, string field, System.Func<TDefinition, TSpec> convert)
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
