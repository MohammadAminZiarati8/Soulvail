using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class BuriedKingViewTests
{
    private const int KingId = 30;
    private readonly List<GameObject> _objects = new();
    private DomainEventHub _hub;
    private IObjectResolver _container;
    private EnemyViews _views;
    private EnemyView _body;
    private Vector3 _origin;

    [SetUp]
    public void Rent()
    {
        _origin = new Vector3(2000f, 0f, 2000f);
        var prefab = AssetDatabase.LoadAssetAtPath<EnemyView>("Assets/_Project/Prefabs/Enemies/BuriedKing.prefab");
        _hub = new DomainEventHub();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        var random = new FixedRandom(7);
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _hub);
        builder.RegisterInstance<IRunSession>(new RunSession(new ContentCatalog(Array.Empty<CharacterSpec>()),
            random, _hub, new RecordingIntents(), recorder, 8, 8, 8));
        _container = builder.Build();
        _views = new EnemyViews(_container, prefab, null, _hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        _hub.Publish(new EnemySpawned(KingId, BuriedKingBehaviour.PrototypeBodyId, _origin.ToNum()));
        Assert.That(_views.TryGet(KingId, out _body), Is.True);
    }

    [TearDown]
    public void Return()
    {
        _views?.Dispose();
        _hub?.Dispose();
        _container?.Dispose();
        foreach (GameObject resource in _objects)
        {
            Object.DestroyImmediate(resource);
        }

        _objects.Clear();
    }

    [Test]
    public void Lunge_ClearanceIncludesLaneAndEscape()
    {
        Rect bounds = new(_origin.x - 12f, _origin.z - 12f, 24f, 24f);
        Assert.That(Measure(Vector3.forward, bounds), Is.EqualTo(8f));
        Assert.That(Measure(Vector3.forward, default), Is.Zero);

        Rect nearWall = new(bounds.xMin, bounds.yMin, 18f, bounds.height);
        Assert.That(Measure(Vector3.forward + Vector3.right, nearWall), Is.InRange(5f, 7f));

        // Cover can cut into the warning while leaving the King's centerline clear.
        GameObject side = CoverAt(1.8f, 7.25f);
        Assert.That(Measure(Vector3.forward, bounds), Is.InRange(5f, 7f));
        side.SetActive(false);

        // One open side is enough. Two walls just outside the warning leave no exit.
        CoverAt(-2.6f, 4f, 8f);
        Assert.That(Measure(Vector3.forward, bounds), Is.EqualTo(8f));
        CoverAt(2.6f, 4f, 8f);
        Assert.That(Measure(Vector3.forward, bounds), Is.LessThan(5f));
    }

    [TestCase("center")]
    [TestCase("diagonal wall")]
    [TestCase("cover")]
    [TestCase("blocked after warning")]
    public void Stage30_TestStartChecksTheLeapInDunes(string scenario)
    {
        _hub.Publish(new EnemyDespawned(KingId));
        const string root = "Assets/_Project/";
        var room = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab");
        var arena = Object.Instantiate(room, _origin, Quaternion.identity).GetComponent<ArenaView>();
        _objects.Add(arena.gameObject);
        var mode = AssetDatabase.LoadAssetAtPath<ModeDefinition>(root + "Data/Modes/Sunscar.asset").ToSpec();
        var rangerAsset = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<CharacterDefinition>(root + "Data/Characters/Ranger.asset"));
        var rangerSettings = new SerializedObject(rangerAsset);
        // The fixture baits the leap at full King health. The Ranger cannot shoot from bait range.
        rangerSettings.FindProperty("_weaponRange").floatValue = 0.01f;
        rangerSettings.ApplyModifiedPropertiesWithoutUndo();
        var ranger = rangerAsset.ToSpec();
        Object.DestroyImmediate(rangerAsset);
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Composition/BootScope.prefab");
        var source = new SerializedObject(boot.GetComponent<BootScope>());
        var catalog = new ContentCatalog(new[] { ranger },
            Definitions<EnemyDefinition>(source, "_enemies").Select(asset => asset.ToSpec()).ToArray(),
            new[] { mode }, bosses: Definitions<BossDefinition>(source, "_bosses").Select(asset => asset.ToSpec()).ToArray());
        var random = new SeededRandom(12);
        var intents = new RecordingIntents();
        var recorder = new RunRecorder(random, new FixedClock(DateTimeOffset.UnixEpoch), _hub);
        var session = new RunSession(catalog, random, _hub, intents, recorder, 64, 28, 64);
        var restore = new TestStart(catalog).At(mode.Id, ranger.Id, random.Seed, random.Capture(), 30);
        var poses = new List<BuriedKingStateChanged>();
        using var watch = _hub.Subscribe<BuriedKingStateChanged>(poses.Add);
        int id = 0;
        using var spawn = _hub.Subscribe<EnemySpawned>(evt => id = evt.Id);
        int damageEvents = 0;
        using var damage = _hub.Subscribe<EnemyDamaged>(_ => damageEvents++);
        session.Start(new RunConfig(mode.Id, ranger.Id, random.Seed, 30, SpawnPlan.Empty, restore));

        const float dt = 1f / 60f;
        var world = new WorldSnapshot(64)
        {
            Dt = dt,
            PlayerPosition = arena.PlayerStart.ToNum(),
            SpawnPoints = arena.SpawnPoints.Select(point => point.ToNum()).ToArray(),
        };
        Vector3 kingStart = _origin;
        Vector3 hero = _origin + 12f * Vector3.forward;
        if (scenario == "diagonal wall")
        {
            kingStart += 11f * Vector3.back;
            hero = kingStart + 5f * (Vector3.right + Vector3.back).normalized;
        }
        else if (scenario == "cover")
        {
            hero = _origin + 6f * Vector3.forward;
            CoverAt(1.8f, 7.25f);
        }

        for (int frame = 0; frame < 240 && id == 0; frame++)
        {
            session.Tick(world);
            intents.Clear();
        }

        Assert.That(_views.TryGet(id, out _body), Is.True);
        _body.Bind(id, kingStart);
        world.PlayerPosition = hero.ToNum();
        Physics.SyncTransforms();
        _views.CopyInto(world, arena.PlayBounds);
        world.Enemies[0].HasLineOfSight = true;
        world.Enemies[0].KingLungeClearance = 0f;
        session.Tick(world);
        float walkSpeed = intents.EnemyMoves.Single(move => move.Id == id).Velocity.Length();
        Assert.That(walkSpeed, Is.EqualTo(2.8f).Within(0.001f));

        float fastestApproach = 0f;
        for (int frame = 0; frame < 300; frame++)
        {
            world.EnemyCount = 0;
            Physics.SyncTransforms();
            _views.CopyInto(world, arena.PlayBounds);
            world.Enemies[0].HasLineOfSight = true;
            intents.Clear();
            int count = poses.Count;
            session.Tick(world);
            if (poses.Count > count && poses[^1].Pose == BuriedKingPose.LungeTell)
            {
                Vector3 side = Vector3.Cross(Vector3.up, (poses[^1].End - poses[^1].Start).ToUnity()).normalized;
                world.PlayerPosition = (hero + 3f * side).ToNum();
                if (scenario == "blocked after warning")
                {
                    CoverAt(0f, 7f);
                }
            }

            foreach (EnemyMoveIntent move in intents.EnemyMoves)
            {
                if (poses.Count > 0 && poses[^1].Pose == BuriedKingPose.LungeApproach)
                {
                    fastestApproach = Mathf.Max(fastestApproach, move.Velocity.Length());
                }

                if (move.Id == id)
                {
                    _body.Apply(move, dt);
                }
            }

            if (poses.Count > 0 && poses[^1].Pose == BuriedKingPose.LungeRecovery)
            {
                break;
            }
        }

        var tell = poses.Single(state => state.Pose == BuriedKingPose.LungeTell);
        var recovery = poses.Single(state => state.Pose == BuriedKingPose.LungeRecovery);
        float length = (tell.End - tell.Start).Length();
        Assert.That(tell.Width, Is.EqualTo(4f));
        Assert.That(recovery.End, Is.EqualTo(tell.End));
        if (scenario == "center" || scenario == "blocked after warning")
        {
            Assert.That(fastestApproach, Is.EqualTo(1.6f * walkSpeed).Within(0.001f));
            Assert.That(length, Is.EqualTo(8f).Within(0.001f));
        }
        else
        {
            Assert.That(length, Is.InRange(5f, 7.99f));
        }

        Assert.That(recovery.CutSeconds, Is.EqualTo(scenario == "blocked after warning" ? 0f : 0.15f));
        Assert.That(damageEvents, Is.Zero);
        session.End();
    }

    private static IEnumerable<T> Definitions<T>(SerializedObject source, string field) where T : ScriptableObject
    {
        SerializedProperty rows = source.FindProperty(field);
        for (int i = 0; i < rows.arraySize; i++)
        {
            yield return (T)rows.GetArrayElementAtIndex(i).objectReferenceValue;
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Lunge_LockedWarningMatchesSingleHit_AndOnlyConfirmedLandingFlashes(bool landed)
    {
        Vector3 end = _origin + new Vector3(3f, 0f, 4f);
        Publish(BuriedKingPose.LungeTell, end, 1f, 0.15f);
        GameObject warnings = GameObject.Find("Buried King warnings");
        Transform warning = warnings.transform.Find("Attack warning");
        Transform fill = warnings.transform.Find("Attack warning fill");
        SlamDustView dust = warnings.GetComponentInChildren<SlamDustView>();
        MeshRenderer warningRenderer = warning.GetComponent<MeshRenderer>();
        MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
        Vector3 side = 2f * Vector3.Cross(Vector3.up, end - _origin).normalized;
        Vector3 lift = Vector3.up * warning.GetComponent<MeshFilter>().sharedMesh.vertices[0].y;
        var corners = new[] { _origin - side + lift, _origin + side + lift, end + side + lift, end - side + lift };
        Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        Assert.That(warningRenderer.enabled, Is.True);

        _body.Bind(KingId, _origin + Vector3.right);
        Publish(BuriedKingPose.LungeTravel, end, 0.55f, 0.15f);
        Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        Assert.That(warningRenderer.enabled, Is.True);
        Assert.That(dust.IsLive, Is.False);

        Publish(BuriedKingPose.LungeRecovery, end, 0.9f, landed ? 0.15f : 0f);
        Assert.That(fillRenderer.enabled, Is.EqualTo(landed));
        Assert.That(dust.IsLive, Is.EqualTo(landed));
        if (landed)
        {
            Assert.That(dust.transform.position, Is.EqualTo(end));
            Assert.That(warning.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(corners));
        }

        Publish(BuriedKingPose.Cleared, end, 0f, 0f);
        Assert.That(warningRenderer.enabled, Is.False);
        Assert.That(fillRenderer.enabled, Is.False);
        Assert.That(dust.IsLive, Is.False);
        _hub.Publish(new EnemyDespawned(KingId));
        Assert.That(warnings == null, Is.True);
    }

    private float Measure(Vector3 direction, Rect bounds)
    {
        Physics.SyncTransforms();
        var snapshot = new WorldSnapshot(1) { PlayerPosition = (_origin + direction * 10f).ToNum() };
        _views.CopyInto(snapshot, bounds);
        return snapshot.Enemies[0].KingLungeClearance;
    }

    private GameObject CoverAt(float x, float z, float length = 0.4f)
    {
        var cover = new GameObject("Leap test cover");
        _objects.Add(cover);
        cover.layer = LayerMask.NameToLayer(ArenaView.CoverLayerName);
        cover.transform.position = _origin + new Vector3(x, 1f, z);
        cover.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2f, length);
        return cover;
    }

    private void Publish(BuriedKingPose pose, Vector3 end, float duration, float cut)
    {
        // Feed the actual view contract without duplicating the core state machine here.
        var change = (BuriedKingStateChanged)System.Activator.CreateInstance(typeof(BuriedKingStateChanged),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { KingId, pose, _origin.ToNum(), end.ToNum(), 4f, 0f, default(System.Numerics.Vector3),
                0f, 0f, duration, 0f, cut }, null);
        _hub.Publish(change);
    }
}
