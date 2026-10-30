using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>JG-13d: real navigation and physics facts, including the shipped Mother's room.</summary>
public sealed class OpenGroundSenseTests : InputTestFixture
{
    private const string ClearingPath = "Assets/_Project/Prefabs/Arenas/Jungle/Arena_Clearing.prefab";
    private const string StonesPath = "Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab";
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly ContentId _mother = new ContentId("enemy.mother-pitcher");
    private DomainEventHub _hub;
    private CoverViews _covers;
    private OpenGroundSense _sense;
    private NavMeshData _data;
    private ArenaPool _pool;
    private EnemyViews _enemies;
    private IObjectResolver _container;
    private InputAdapter _input;
    private PlayerView _player;

    [SetUp]
    public void SetUpSense()
    {
        _hub = new DomainEventHub();
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [_mother] = new EnemyLook(Color.white, 1f, cover: AssetDatabase.LoadAssetAtPath<GameObject>(StonesPath)),
        });
        _covers = new CoverViews(looks, _hub, Keep(new GameObject("Covers")).transform);
        _sense = new OpenGroundSense(LayerMask.GetMask("Cover"), _covers);
    }

    [TearDown]
    public void TearDownSense()
    {
        _pool?.Dispose();
        _enemies?.Dispose();
        _input?.Dispose();
        _container?.Dispose();
        _covers.Dispose();
        _hub.Dispose();
        foreach (GameObject obj in _objects)
        {
            Object.DestroyImmediate(obj);
        }

        _objects.Clear();
        if (_data != null)
        {
            Object.DestroyImmediate(_data);
        }

        Physics.SyncTransforms();
    }

    [Test]
    public void Sense_KeepsOnlyFloor()
    {
        ArenaView arena = FloorWithHole();
        _sense.Sense(arena);
        Assert.That(_sense.Spots.Count, Is.InRange(20, OpenGroundSense.MaxSpots));
        foreach (System.Numerics.Vector3 spot in _sense.Spots)
        {
            Assert.That(Mathf.Abs(spot.X) < 1f && Mathf.Abs(spot.Z) < 1f, Is.False, "The hole has no floor.");
            Assert.That(Mathf.Abs(spot.X), Is.LessThanOrEqualTo(5f));
            Assert.That(Mathf.Abs(spot.Z), Is.LessThanOrEqualTo(5f));
            AssertOnFloor(spot.ToUnity());
        }

        // Interior samples stay on the 1 m lattice; only the edge may snap within FloorProbe.
        Assert.That(_sense.Spots, Has.Some.Matches<System.Numerics.Vector3>(p => Mathf.Abs(p.X - 3f) < 0.01f && Mathf.Abs(p.Z - 3f) < 0.01f));
    }

    [Test]
    public void Sense_KeepsClearOfCover()
    {
        ArenaView arena = FloorWithHole();
        Collider cover = Box(new Vector3(3f, 0.5f, 0f), LayerMask.NameToLayer("Cover"));
        Collider other = Box(new Vector3(-3f, 0.5f, 0f), 0);
        _sense.Sense(arena); // Deliberately no SyncTransforms in the fixture.
        foreach (System.Numerics.Vector3 spot in _sense.Spots)
        {
            Assert.That(Vector3.Distance(spot.ToUnity(), cover.ClosestPoint(spot.ToUnity())), Is.GreaterThanOrEqualTo(1f));
        }

        Assert.That(_sense.Spots, Has.Some.Matches<System.Numerics.Vector3>(p => Vector3.Distance(p.ToUnity(), other.ClosestPoint(p.ToUnity())) < 0.6f));
    }

    [Test]
    public void Sense_OnlyOnChange()
    {
        ArenaView arena = FloorWithHole();
        _sense.Sense(arena);
        _sense.Sense(arena);
        Assert.That(_sense.Senses, Is.EqualTo(1));
        Spawn(Vector3.zero);
        _sense.Sense(arena);
        Assert.That(_sense.Senses, Is.EqualTo(2));
        _sense.Sense(Clearing());
        Assert.That(_sense.Senses, Is.EqualTo(3));
    }

    [Test]
    public void Build_AfterStonesStand_ReportsNoneInside()
    {
        SnapshotBuilder builder = Builder();
        var snapshot = new WorldSnapshot(8);
        builder.Build(snapshot, 0.02f);
        var before = new List<System.Numerics.Vector3>(snapshot.OpenGround);
        Spawn(MotherPosition(_pool.Active));
        builder.Build(snapshot, 0.02f);
        Assert.That(snapshot.OpenGround.Count, Is.GreaterThan(0));
        int newlyBlocked = 0;
        foreach (System.Numerics.Vector3 spot in before)
        {
            Vector3 point = spot.ToUnity();
            if (Physics.CheckCapsule(point, point + Vector3.up * LineOfSightSense.EyeHeight,
                OpenGroundSense.CoverClearance, LayerMask.GetMask("Cover"), QueryTriggerInteraction.Ignore))
            {
                newlyBlocked++;
                Assert.That(snapshot.OpenGround, Has.No.Member(spot));
            }
        }

        Assert.That(newlyBlocked, Is.GreaterThan(0), "Stones must replace previously reported ground.");
        AssertClear(snapshot.OpenGround);
    }

    [Test]
    public void Build_ReportsTheSpotsByReference()
    {
        SnapshotBuilder builder = Builder();
        var snapshot = new WorldSnapshot(8);
        builder.Build(snapshot, 0.02f);
        Assert.That(snapshot.OpenGround, Is.SameAs(_sense.Spots));
        Assert.That(snapshot.OpenGround.Count, Is.GreaterThan(0));
        _pool.Dispose();
        // A builder with no arena must clear a previously populated sense and report its list.
        builder = new SnapshotBuilder(_player, _input, _enemies, null, null, null, null, _sense);
        builder.Build(snapshot, 0.02f);
        Assert.That(snapshot.OpenGround, Is.SameAs(_sense.Spots));
        Assert.That(snapshot.OpenGround.Count, Is.Zero);
    }

    [Test]
    public void Sense_AllocatesNothing()
    {
        ArenaView arena = FloorWithHole();
        _sense.Sense(arena);
        AllocationAssert.None(() =>
        {
            _sense.Sense(null);
            _sense.Sense(arena);
        }, 100);
        Assert.That(_sense.Senses, Is.GreaterThan(200), "Each measured iteration actually re-senses.");
    }

    [Test]
    public void Clearing_WithMotherStones_HasOpenGround()
    {
        ArenaView arena = Clearing();
        Vector3 mother = MotherPosition(arena);
        Spawn(mother);
        _sense.Sense(arena);
        int inRing = 0;
        foreach (System.Numerics.Vector3 spot in _sense.Spots)
        {
            AssertOnFloor(spot.ToUnity());
            float distance = Vector3.Distance(spot.ToUnity(), mother);
            if (distance >= 4f && distance <= 8f)
            {
                inRing++;
            }
        }

        Assert.That(inRing, Is.GreaterThanOrEqualTo(40));
        Assert.That(_sense.Spots.Count, Is.LessThanOrEqualTo(OpenGroundSense.MaxSpots));
        AssertClear(_sense.Spots);
    }

    private SnapshotBuilder Builder()
    {
        _container = new ContainerBuilder().Build();
        _player = Keep(new GameObject("Player")).AddComponent<PlayerView>();
        EnemyView template = Keep(new GameObject("Enemy template")).AddComponent<EnemyView>();
        _enemies = new EnemyViews(_container, template, null, _hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()));
        _input = new InputAdapter();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClearingPath);
        _pool = new ArenaPool(_container, new[] { prefab.GetComponent<ArenaView>() }, null, _hub, _player);
        _hub.Publish(new StageArrived(10, prefab.GetComponent<ArenaView>().Id));
        return new SnapshotBuilder(_player, _input, _enemies, null, null, _pool, null, _sense);
    }

    private ArenaView Clearing() => Keep(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ClearingPath))).GetComponent<ArenaView>();

    private ArenaView FloorWithHole()
    {
        var sources = new List<NavMeshBuildSource>();
        AddFloor(sources, new Vector3(-3f, -0.1f, 0f), new Vector3(4f, 0.2f, 10f));
        AddFloor(sources, new Vector3(3f, -0.1f, 0f), new Vector3(4f, 0.2f, 10f));
        AddFloor(sources, new Vector3(0f, -0.1f, -3f), new Vector3(2f, 0.2f, 4f));
        AddFloor(sources, new Vector3(0f, -0.1f, 3f), new Vector3(2f, 0.2f, 4f));
        NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
        _data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(10f, 2f, 10f)), Vector3.zero, Quaternion.identity);
        // Use a fully authored arena's component, replacing only its navigation data.
        ArenaView arena = Clearing();
        NavMeshSurface surface = arena.Surface;
        surface.RemoveData();
        surface.navMeshData = _data;
        surface.AddData();
        return arena;
    }

    private static void AddFloor(List<NavMeshBuildSource> sources, Vector3 centre, Vector3 size)
        => sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one), size = size, area = 0 });

    private Collider Box(Vector3 position, int layer)
    {
        GameObject box = Keep(new GameObject("Box") { layer = layer });
        box.transform.position = position;
        return box.AddComponent<BoxCollider>();
    }

    private void AssertClear(IReadOnlyList<System.Numerics.Vector3> spots)
    {
        Collider[] stones = _objects[0].GetComponentsInChildren<Collider>();
        Assert.That(stones.Length, Is.EqualTo(4));
        foreach (System.Numerics.Vector3 spot in spots)
        {
            foreach (Collider stone in stones)
            {
                Vector3 point = spot.ToUnity();
                Assert.That(Vector3.Distance(point, stone.ClosestPoint(point)), Is.GreaterThanOrEqualTo(1f));
            }
        }
    }

    private static Vector3 MotherPosition(ArenaView arena)
    {
        foreach (Transform marker in arena.GetComponentsInChildren<Transform>())
        {
            if (marker.name == "Spawn_06")
            {
                return marker.position;
            }
        }

        Assert.Fail("Clearing must author Spawn_06.");
        return default;
    }

    private static void AssertOnFloor(Vector3 spot)
    {
        Assert.That(NavMesh.SamplePosition(spot, out NavMeshHit hit, 0.01f, NavMesh.AllAreas), Is.True);
        Assert.That(Vector3.Distance(spot, hit.position), Is.LessThan(0.01f));
    }

    private void Spawn(Vector3 point) => _hub.Publish(new EnemySpawned(7, _mother, point.ToNum()));

    private GameObject Keep(GameObject obj)
    {
        _objects.Add(obj);
        return obj;
    }
}
