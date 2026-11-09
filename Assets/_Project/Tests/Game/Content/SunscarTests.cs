using System;
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
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Content;

[TestFixture]
public sealed class SunscarTests
{
    private const string Root = "Assets/_Project/";
    private const string DunesPath = Root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab";
    private const string GrovePath = Root + "Prefabs/Arenas/Jungle/Arena_Grove.prefab";
    private const string CourtyardPath = Root + "Prefabs/Arenas/AshenReach/Arena_Courtyard.prefab";
    private static readonly ContentId Sunscar = new("mode.sunscar");
    private static readonly ContentId Jungle = new("mode.jungle");
    private static readonly ContentId Ranger = new("character.ranger");
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
    public void Sunscar_Mode_NoEnemies()
    {
        ModeSpec mode = Mode("Sunscar");
        Assert.That(mode.Roster, Is.Empty);
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
    public void Sunscar_Stage1_SpawnsNothingNeverClears()
    {
        ContentCatalog catalog = Catalog(Mode("Sunscar"));
        var random = new SeededRandom(7);
        var events = new RecordingEvents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), events);
        var session = new RunSession(catalog, random, events, new RecordingIntents(), recorder, 64, 28, 64);
        RunSnapshot snapshot = new TestStart(catalog).At(Sunscar, Ranger, random.Seed, random.Capture(), 1);

        try
        {
            session.Start(new RunConfig(Sunscar, Ranger, random.Seed, 1, SpawnPlan.Empty, snapshot));
            StageArrived arrival = events.Of<StageArrived>().Single();
            Assert.That(arrival.Stage, Is.EqualTo(1));
            Assert.That(arrival.ArenaId, Is.EqualTo(Dunes));

            var world = new WorldSnapshot(64) { Dt = 1f / 32f };
            for (int frame = 0; frame < 60 * 32; frame++)
            {
                session.Tick(world);
            }

            Assert.That(session.State.Time, Is.EqualTo(60f));
            Assert.That(session.State.StageIndex, Is.EqualTo(1));
            Assert.That(session.State.EnemyCount, Is.Zero);
            Assert.That(events.Count<EnemySpawned>(), Is.Zero);
            Assert.That(events.Count<StageCleared>(), Is.Zero);
            Assert.That(events.Count<StageArrived>(), Is.EqualTo(1));
        }
        finally
        {
            session.End();
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
            modes: modes);

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
