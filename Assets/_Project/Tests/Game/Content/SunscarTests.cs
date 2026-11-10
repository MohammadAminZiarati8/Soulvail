using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using NumericsVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Content;

[TestFixture]
public sealed class SunscarTests
{
    private const string Root = "Assets/_Project/";
    private const string DunesPath = Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab";
    private const string QuicksandGroup = "Quicksand patches";
    private const string GrovePath = Root + "Prefabs/Arenas/Jungle/Arena_Grove.prefab";
    private const string CourtyardPath = Root + "Prefabs/Arenas/AshenReach/Arena_Courtyard.prefab";
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Jungle = new("mode.jungle");
    private static readonly ContentId Ranger = new("character.ranger");
    private const string ScorpionPath = Root + "Prefabs/Enemies/Scorpion.prefab";
    private static readonly ContentId Scorpion = new("enemy.scorpion");
    private static readonly ContentId Beetle = new("enemy.beetle");
    private static readonly ContentId Dunes = new("arena.sunscar.dunes");

    [Test]
    public void Sunscar_Mode_ListedAfterJungle()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Id, Is.EqualTo(Sunscar));
        Assert.That(mode.IsListed, Is.True);
        Assert.That(mode.UnlockedBy, Is.EqualTo(Jungle));
        Assert.That(mode.FinalStage, Is.EqualTo(30));
    }

    [Test]
    public void Sunscar_Mode_RosterIsScorpionThenBeetle()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Roster.Select(entry => (entry.SpecId, entry.IntroducedAtStage, entry.RetiredAtStage)),
            Is.EqualTo(new[] { (Scorpion, 1, 0), (Beetle, 2, 0) }));
        Assert.That(mode.BossRoster, Is.Empty);
        Assert.That(mode.ScriptedStages, Is.Empty);
    }

    [Test]
    public void Sunscar_Arena_OnlyDunesEveryStage()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Arenas, Has.Count.EqualTo(1));
        for (int stage = 1; stage <= 30; stage++)
        {
            Assert.That(mode.ArenaFor(stage, 7), Is.EqualTo(Dunes), $"Stage {stage}");
        }
    }

    [Test]
    public void Scorpion_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Scorpion);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Scorpion.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.scorpion.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((36f, 2.2f, 1, 4, 18f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((8f, 1.5f, 0.6f, 0.8f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Chaser));
        Assert.That((spec.HopTime, spec.HopRestTime), Is.EqualTo((0f, 0f)));
        Assert.That(definition.ToLook().Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(ScorpionPath).GetComponent<EnemyView>()));
        Assert.That(spec.Skitter, Is.Not.Null);
        SkitterSpec skitter = spec.Skitter;
        Assert.That((skitter.RingRadius, skitter.FanDegrees, skitter.SpreadTimeout, skitter.ZoneLength, skitter.ZoneHalfWidth),
            Is.EqualTo((4f, 120f, 3f, 1.8f, 0.5f)));
    }

    [Test]
    public void Scorpion_ExistingEnemies_NoSkitter()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Scorpion)
            {
                Assert.That(spec.Skitter, Is.Null, path);
            }
        }
    }

    [Test]
    public void Scorpion_Strip_MatchesSpec()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Scorpion);
        SkitterSpec skitter = definition.ToSpec().Skitter;
        var tell = new SerializedObject(definition.ToLook().Body.GetComponent<ScorpionTellView>());
        Assert.That(tell.FindProperty("_stripLength").floatValue, Is.EqualTo(skitter.ZoneLength));
        Assert.That(tell.FindProperty("_stripHalfWidth").floatValue, Is.EqualTo(skitter.ZoneHalfWidth));
    }

    [Test]
    public void Scorpion_NameKey_InEveryLanguage()
    {
        var key = new LocKey("enemy.scorpion.name");
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Sand Scorpion"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Beetle_Asset_MatchesDesign()
    {
        EnemyDefinition definition = BootEnemies().Single(enemy => enemy.ToSpec().Id == Beetle);
        Assert.That(definition, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root + "Data/Enemies/Beetle.asset")));
        EnemySpec spec = definition.ToSpec();
        Assert.That(spec.NameKey.Key, Is.EqualTo("enemy.beetle.name"));
        Assert.That((spec.MaxHp, spec.MoveSpeed, spec.TargetPriority, spec.ThreatCost, spec.XpValue),
            Is.EqualTo((28f, 2.8f, 3, 7, 21f)));
        Assert.That(spec.IsLight, Is.True);
        Assert.That(spec.IsElite, Is.False);
        Assert.That((spec.ContactDamage, spec.Reach, spec.WindupTime, spec.RecoverTime, spec.AggroRange),
            Is.EqualTo((12f, 1.2f, 0.7f, 2.3f, 30f)));
        Assert.That(spec.Behaviour, Is.EqualTo(EnemyBehaviourKind.Spitter));
        Assert.That(spec.Projectile, Is.Not.Null);
        ProjectileSpec projectile = spec.Projectile;
        Assert.That((projectile.StandoffRange, projectile.Speed, projectile.Radius, projectile.Count),
            Is.EqualTo((12f, 6f, 1.6f, 1)));
        Assert.That(spec.Bombardier, Is.Not.Null);
        BombardierSpec bombardier = spec.Bombardier;
        Assert.That((bombardier.TurnRateDegrees, bombardier.StartConeDegrees, bombardier.DropConeDegrees),
            Is.EqualTo((45f, 10f, 20f)));
        Assert.That(spec.Slinger, Is.Null);

        EnemyLook look = definition.ToLook();
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(look.Body, Is.Not.Null);
        Assert.That(look.Body, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Enemies/Beetle.prefab").GetComponent<EnemyView>()));
        Assert.That(look.Projectile, Is.Not.Null);
        Assert.That(look.Projectile, Is.SameAs(
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Projectiles/BeetleGlob.prefab").GetComponent<ProjectileView>()));
    }

    [Test]
    public void Beetle_ExistingEnemies_NoBombardier()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EnemySpec spec = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();
            if (spec.Id != Beetle)
            {
                Assert.That(spec.Bombardier, Is.Null, path);
            }
        }
    }

    [Test]
    public void Beetle_Standoff_InsideTheStormsClearing()
    {
        EnemySpec spec = BootEnemies().Select(enemy => enemy.ToSpec()).Single(enemy => enemy.Id == Beetle);
        var storm = Arena(DunesPath).GetComponentInChildren<SunscarSandstormView>(true);
        Assert.That(spec.Projectile.StandoffRange, Is.LessThan(storm.ClearRadius));
    }

    [Test]
    public void Beetle_NameKey_InEveryLanguage()
    {
        var key = new LocKey("enemy.beetle.name");
        var english = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/English.asset");
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(Root + "Data/Localisation/Pseudo.asset");
        Assert.That(english.ToDictionary()[key], Is.EqualTo("Bombardier Beetle"));
        Assert.That(pseudo.ToDictionary()[key], Is.Not.Empty);
    }

    [Test]
    public void Sunscar_RosterByStage_BeetleFrom2()
    {
        ModeSpec mode = Mode("Sunscar");
        var roster = new RosterEntry[mode.Roster.Count];
        int count = mode.RosterFor(1, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion }));
        count = mode.RosterFor(2, roster);
        Assert.That(roster.Take(count).Select(entry => entry.SpecId), Is.EqualTo(new[] { Scorpion, Beetle }));
    }

    [Test]
    public void Sunscar_Stage1_WavesClearsOpensPortal() => AssertStageWavesClear(1, Scorpion);

    [Test]
    public void Sunscar_Stage2_WavesClear() => AssertStageWavesClear(2, Scorpion, Beetle);

    private static void AssertStageWavesClear(int stage, params ContentId[] expectedEnemies)
    {
        const float frameTime = 1f / 32f;
        ContentCatalog catalog = Catalog(Mode("Sunscar"));
        var random = new SeededRandom(7);
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, intents, recorder, 64, 28, 64);
        RunSnapshot snapshot = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), stage);

        try
        {
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, stage, SpawnPlan.Empty, snapshot));
            StageArrived arrival = events.Of<StageArrived>().Single();
            Assert.That((arrival.Stage, arrival.ArenaId), Is.EqualTo((stage, Dunes)));

            var world = new WorldSnapshot(64);
            NumericsVector3[] points =
            {
                new(-8f, 0f, 8f), new(8f, 0f, 8f),
                new(-8f, 0f, -8f), new(8f, 0f, -8f),
            };
            var positions = new Dictionary<int, NumericsVector3>();
            NumericsVector3 player = NumericsVector3.Zero;
            int tellsRead = 0;
            for (int frame = 0; frame < 180 * 32 && events.Count<StageCleared>() == 0; frame++)
            {
                // Walk the bodies by the previous tick's intents, then report the living ones in sight.
                foreach (var move in intents.EnemyMoves)
                {
                    if (positions.TryGetValue(move.Id, out NumericsVector3 position))
                    {
                        positions[move.Id] = position + (move.Velocity * frameTime);
                    }
                }

                intents.Clear();
                world.Clear();
                world.Dt = frameTime;
                world.SpawnPoints = points;
                // Step away from each locked strip, then stand still to let the Ranger fire.
                IReadOnlyList<EnemyTelegraph> tells = events.Of<EnemyTelegraph>();
                for (; tellsRead < tells.Count; tellsRead++)
                {
                    if (positions.TryGetValue(tells[tellsRead].Id, out NumericsVector3 attacker))
                    {
                        NumericsVector3 away = player - attacker;
                        away = away.LengthSquared() > 1e-4f ? NumericsVector3.Normalize(away) : NumericsVector3.UnitX;
                        player += away * 3f;
                    }
                }

                world.PlayerPosition = player;
                foreach (EnemySpawned spawned in events.Of<EnemySpawned>())
                {
                    if (!session.State.IsEnemyAlive(spawned.Id))
                    {
                        continue;
                    }

                    if (!positions.ContainsKey(spawned.Id))
                    {
                        positions.Add(spawned.Id, spawned.Position);
                    }

                    world.AddEnemy() = new EnemySense
                    {
                        Id = spawned.Id,
                        Position = positions[spawned.Id],
                        HasLineOfSight = true,
                    };
                }

                session.Tick(world);
                Assert.That(events.Count<PlayerDied>(), Is.Zero, "The Ranger must survive to clear the waves.");
            }

            Assert.That(events.Count<WaveStarted>(), Is.GreaterThan(0));
            Assert.That(events.Of<EnemySpawned>().Select(e => e.SpecId).Distinct(), Is.EquivalentTo(expectedEnemies));
            Assert.That(events.Count<StageCleared>(), Is.EqualTo(1));
        }
        finally
        {
            session.End();
        }
    }

    [Test]
    public void Quicksand_Arenas_OnlyDunesWithClearFootprints()
    {
        const float margin = 4f;
        const string arenas = Root + "Prefabs/Arenas";
        int dunesFound = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { arenas }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!prefab.TryGetComponent(out ArenaView arena))
            {
                continue;
            }

            Transform patches = arena.transform.Find(QuicksandGroup);
            if (arena.Id != Dunes)
            {
                Assert.That(patches, Is.Null, arena.name);
                continue;
            }

            dunesFound++;
            Assert.That(patches, Is.Not.Null);
            Assert.That(patches.childCount, Is.InRange(2, 4));
            Vector3[] anchors = { arena.EntryPosition, arena.PlayerStart, arena.GatePosition };
            foreach (Transform patch in patches)
            {
                Bounds footprint = patch.GetComponent<MeshRenderer>().bounds;
                Assert.That(footprint.size.x, Is.GreaterThan(1f), patch.name);
                Assert.That(footprint.size.z, Is.GreaterThan(1f), patch.name);
                foreach (Vector3 anchor in anchors)
                {
                    var groundAnchor = new Vector3(anchor.x, footprint.center.y, anchor.z);
                    Assert.That(footprint.SqrDistance(groundAnchor), Is.GreaterThanOrEqualTo(margin * margin), patch.name);
                }

                foreach (string coverGroup in new[] { "Sandstone spurs", "Wind-carved rim", "Gate" })
                {
                    foreach (var cover in arena.transform.Find(coverGroup).GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Bounds coverFootprint = cover.bounds;
                        coverFootprint.Encapsulate(new Vector3(coverFootprint.center.x, footprint.center.y, coverFootprint.center.z));
                        Assert.That(footprint.Intersects(coverFootprint), Is.False, $"{patch.name} overlaps {cover.name}");
                    }
                }
            }
        }

        Assert.That(dunesFound, Is.EqualTo(1));
    }

    [Test]
    public void Quicksand_Renderers_UseSharedGroundMaterialOnly()
    {
        Transform patches = Arena(DunesPath).transform.Find(QuicksandGroup);
        Assert.That(patches, Is.Not.Null);
        Assert.That(patches.GetComponents<Component>(), Has.Length.EqualTo(1));
        var shared = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Environment/M_SunscarQuicksand.mat");
        Assert.That(shared, Is.Not.Null);
        Assert.That(shared.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(shared.mainTexture, Is.EqualTo(
            AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/UI/Hud/Glow_Soft.png")));
        Assert.That(shared.renderQueue, Is.LessThan((int)UnityEngine.Rendering.RenderQueue.Transparent - 100));
        foreach (Transform patch in patches)
        {
            Assert.That(patch.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
            Assert.That(patch.childCount, Is.Zero);
            Assert.That(patch.GetComponents<Component>().Select(component => component.GetType()),
                Is.EquivalentTo(new[] { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer) }));
            var mesh = patch.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(Mathf.Abs(Vector3.Dot(patch.forward, Vector3.up)), Is.GreaterThan(0.999f));
            Material[] materials = patch.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(materials.Select(material => material.shader.name), Has.No.Member("Soulvail/Cloak"));
            Assert.That(materials, Is.EqualTo(new[] { shared }));
        }
    }

    [Test]
    public void Dunes_Renderers_NoCyanOrDangerHue()
    {
        Renderer[] renderers = Arena(DunesPath).GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null, renderer.name);
                string property = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Assert.That(material.HasProperty(property), Is.True, material.name);
                Color.RGBToHSV(material.GetColor(property), out float hue, out float saturation, out _);
                float degrees = hue * 360f;
                bool cyan = degrees >= 155f && degrees <= 205f;
                bool danger = degrees <= 25f || degrees >= 350f;
                Assert.That(saturation > 0.05f && (cyan || danger), Is.False, material.name);
            }
        }
    }

    [Test]
    public void Dunes_NavMesh_StartReachesPortal()
    {
        ArenaView dunes = Arena(DunesPath);
        Assert.That(dunes.Surface.navMeshData, Is.Not.Null);
        NavMeshDataInstance mesh = NavMesh.AddNavMeshData(dunes.Surface.navMeshData);
        try
        {
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(dunes.PlayerStart, dunes.GatePosition, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
        }
        finally
        {
            mesh.Remove();
        }
    }

    [Test]
    public void Dunes_Layout_MatchesGrove()
    {
        ArenaView dunes = Arena(DunesPath);
        ArenaView grove = Arena(GrovePath);
        Assert.That(dunes.HasEntry && dunes.HasGate, Is.True);
        Assert.That(dunes.PlayBounds.size, Is.EqualTo(new Vector2(36f, 36f)));
        Assert.That(dunes.EntryPosition, Is.EqualTo(grove.EntryPosition));
        Assert.That(dunes.PlayerStart, Is.EqualTo(grove.PlayerStart));
        Assert.That(dunes.GatePosition, Is.EqualTo(grove.GatePosition));
    }

    [Test]
    public void Courtyard_Untouched()
    {
        var courtyard = new ContentId("arena.ashen-reach.courtyard");
        Assert.That(Arena(CourtyardPath).Id, Is.EqualTo(courtyard));
        Assert.That(Mode("Descent").Arenas.Select(row => row.ArenaId), Does.Contain(courtyard));
        Assert.That(Mode("Sunscar").Arenas.Select(row => row.ArenaId), Has.No.Member(courtyard));
    }

    [Test]
    public void TestStartPanel_CanStartLockedSunscar()
    {
        ContentCatalog catalog = Catalog(Mode("Jungle"), Mode("Sunscar"));
        Assert.That(PlaceUnlocks.IsUnlocked(Sunscar, PlayerProfile.Default, catalog), Is.False);
        var pending = new PendingRun();
        var loader = new RecordingLoader();
        var root = new GameObject("Test start");
        root.SetActive(false);
        try
        {
            var menu = root.AddComponent<MenuPresenter>();
            typeof(MenuPresenter).GetProperty(nameof(MenuPresenter.SelectedCharacter)).SetValue(menu, Ranger);
            typeof(MenuPresenter).GetProperty(nameof(MenuPresenter.SelectedMode)).SetValue(menu, Jungle);

            var panel = root.AddComponent<TestStartPanel>();
            panel.Construct(pending, loader, catalog, menu);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(TestStartPanel).GetField("_stage", flags).SetValue(panel, 1);
            typeof(TestStartPanel).GetMethod("CyclePlace", flags).Invoke(panel, new object[] { 1 });
            typeof(TestStartPanel).GetMethod("Begin", flags).Invoke(panel, null);

            Assert.That(loader.Loaded, Is.EqualTo(SceneLoader.Run));
            Assert.That(pending.Snapshot.HasValue, Is.True);
            Assert.That(pending.ModeId, Is.EqualTo(Sunscar));
            Assert.That(pending.CharacterId, Is.EqualTo(Ranger));
            Assert.That(pending.Snapshot.Value.StageIndex, Is.EqualTo(1));
            Assert.That(menu.SelectedMode, Is.EqualTo(Jungle));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static ModeSpec Mode(string name) =>
        AssetDatabase.LoadAssetAtPath<ModeDefinition>(Root + $"Data/Modes/{name}.asset").ToSpec();

    private static ArenaView Arena(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>();

    private static ContentCatalog Catalog(params ModeSpec[] modes) =>
        new(new[] { AssetDatabase.LoadAssetAtPath<CharacterDefinition>(Root + "Data/Characters/Ranger.asset").ToSpec() },
            BootEnemies().Select(enemy => enemy.ToSpec()).ToArray(), modes: modes);

    private static EnemyDefinition[] BootEnemies()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Composition/BootScope.prefab");
        var serialized = new SerializedObject(prefab.GetComponent<BootScope>());
        SerializedProperty enemies = serialized.FindProperty("_enemies");
        var definitions = new EnemyDefinition[enemies.arraySize];
        for (int i = 0; i < definitions.Length; i++)
        {
            definitions[i] = (EnemyDefinition)enemies.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        return definitions;
    }

    private sealed class RecordingLoader : SceneLoader
    {
        public string Loaded { get; private set; }

        public override Task LoadAsync(string sceneName)
        {
            Loaded = sceneName;
            return Task.CompletedTask;
        }
    }
}
