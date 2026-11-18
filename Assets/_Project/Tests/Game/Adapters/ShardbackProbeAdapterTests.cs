using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Adapters;

public sealed class ShardbackProbeAdapterTests
{
    private readonly List<GameObject> _objects = new();
    private readonly Vector3 _origin = new(3000f, 0f, 3000f);
    private readonly Guid _lifetime = Guid.NewGuid();
    private ArenaPool _arenas;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private ShardbackProbeAdapter _adapter;
    private Rect Bounds => new(_origin.x - 20f, _origin.z - 20f, 40f, 40f);

    [SetUp]
    public void SetUp()
    {
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _arenas = new ArenaPool(_container, Array.Empty<ArenaView>(), null, _hub, Make("Player").AddComponent<PlayerView>());
        GameObject arenaObject = Make("Probe arena");
        arenaObject.transform.position = _origin;
        ArenaView arena = arenaObject.AddComponent<ArenaView>();
        var serialized = new SerializedObject(arena);
        serialized.FindProperty("_halfSize").vector2Value = new Vector2(20f, 20f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        typeof(ArenaPool).GetProperty("Active").SetValue(_arenas, arena);
        _adapter = new ShardbackProbeAdapter(LayerMask.GetMask("Cover"), _arenas);
    }

    [TearDown]
    public void TearDown()
    {
        _arenas.Dispose();
        _hub.Dispose();
        _container.Dispose();
        foreach (GameObject obj in _objects)
        { if (obj != null) { Object.DestroyImmediate(obj); } }
        _objects.Clear();
    }

    [Test]
    public void Identity_SlotsAndForeignCover_Classified()
    {
        GameObject root = Make("Pillar");
        ShardbackPillar pillar = root.AddComponent<ShardbackPillar>();
        var serialized = new SerializedObject(pillar);
        serialized.FindProperty("_slot").intValue = 2;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        GameObject first = Solid(0f, 5f);
        GameObject second = Solid(0f, 5f);
        first.transform.SetParent(root.transform, true);
        second.transform.SetParent(root.transform, true);
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 3.5f);
        pillar.Bind(7, _lifetime);
        Assert.That(_adapter.Probe(Lane(), Bounds).PillarSlot, Is.EqualTo(2));
        second.transform.SetSiblingIndex(0);
        first.name = "Unrelated name";
        Assert.That(_adapter.Probe(Lane(), Bounds).PillarSlot, Is.EqualTo(2));
        pillar.Bind(8, _lifetime);
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 3.5f);
        pillar.Bind(7, Guid.NewGuid());
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 3.5f);
        Assert.Throws<ArgumentOutOfRangeException>(() => pillar.Bind(0, _lifetime));
        Assert.Throws<ArgumentException>(() => pillar.Bind(7, Guid.Empty));
    }

    [Test]
    public void Probe_EdgesOverlapBoundary_FirstContact()
    {
        GameObject edge = Solid(0.9f, 5f);
        Check(_adapter.Probe(Lane(1), Bounds), ShardbackStopKind.Cover, 4.5f);
        edge.transform.position = _origin + Vector3.up;
        Physics.SyncTransforms();
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 0f);
        Check(_adapter.Probe(Lane(2), Bounds), ShardbackStopKind.Cover, 0f);
        edge.SetActive(false);
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.None, 12f);
        var narrow = new Rect(_origin.x - 4f, _origin.z - 4f, 8f, 8f);
        Check(_adapter.Probe(Lane(), narrow), ShardbackStopKind.Boundary, 3f);
        Check(_adapter.Probe(Lane(1), narrow), ShardbackStopKind.Boundary, 4f);
        var sideways = new Rect(_origin.x - 0.5f, _origin.z - 20f, 1f, 40f);
        Check(_adapter.Probe(Lane(1), sideways), ShardbackStopKind.Boundary, 0f);
        var diagonal = new LaneProbeIntent(7, _origin.ToNum(), new System.Numerics.Vector3(1, 8, 1), 12, 1, Lane().Key, 3);
        Check(_adapter.Probe(diagonal, narrow), ShardbackStopKind.Boundary, 4f * Mathf.Sqrt(2f) - 0.6f);
        GameObject ignored = Solid(0f, 2f);
        ignored.GetComponent<BoxCollider>().isTrigger = true;
        Solid(0f, 3f).layer = 0;
        Physics.SyncTransforms();
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.None, 12f);
        Check(_adapter.Probe(Lane(), default), ShardbackStopKind.Unknown, 0f);
    }

    [Test]
    public void Probe_OrderOverflow_NoFalseClear()
    {
        GameObject far = Solid(0f, 8f);
        GameObject near = Solid(0f, 4f);
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 2.5f);
        far.transform.position = _origin + new Vector3(0, 1, 4);
        near.transform.position = _origin + new Vector3(0, 1, 8);
        Physics.SyncTransforms();
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 2.5f);
        near.transform.position = far.transform.position;
        ShardbackPillar p2 = far.AddComponent<ShardbackPillar>();
        var serialized = new SerializedObject(p2);
        serialized.FindProperty("_slot").intValue = 2;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        p2.Bind(7, _lifetime);
        ShardbackPillar p0 = near.AddComponent<ShardbackPillar>();
        p0.Bind(7, _lifetime);
        Physics.SyncTransforms();
        Assert.That(_adapter.Probe(Lane(), Bounds).PillarSlot, Is.Zero);
        GameObject cover = Solid(0f, 4f);
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Cover, 2.5f);
        var tied = new Rect(_origin.x - 20f, _origin.z - 20f, 40f, 23.5f);
        Check(_adapter.Probe(Lane(), tied), ShardbackStopKind.Boundary, 2.5f);
        cover.SetActive(false);
        for (int i = 0; i < 64; i++)
        { Solid(0f, 6f); }
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Unknown, 0f);
        Check(_adapter.Probe(Lane(1), Bounds), ShardbackStopKind.Unknown, 0f);
        foreach (GameObject obj in _objects)
        {
            if (obj.GetComponent<BoxCollider>() != null)
            { obj.transform.position = _origin + Vector3.up; }
        }
        Physics.SyncTransforms();
        Check(_adapter.Probe(Lane(), Bounds), ShardbackStopKind.Unknown, 0f);
        Check(_adapter.Probe(Lane(1), Bounds), ShardbackStopKind.Unknown, 0f);
    }

    [Test]
    public void Probe_Footprint_AllDirections()
    {
        var bounds = new Rect(_origin.x - 4f, _origin.z - 4f, 8f, 8f);
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (Vector3 direction in directions)
        {
            for (int lane = 0; lane <= 3; lane++)
            {
                var probe = new LaneProbeIntent(7, _origin.ToNum(), direction.ToNum(), 12f, 0.2f, Lane().Key, lane);
                Check(_adapter.Probe(probe, bounds), ShardbackStopKind.Boundary, lane == 0 ? 3f : 4f);
                Assert.That(_adapter.Probe(probe, bounds).Lane, Is.EqualTo(lane));
            }
        }
        GameObject beyond = Solid(0f, 8f);
        Check(_adapter.Probe(Lane(), bounds), ShardbackStopKind.Boundary, 3f);
        beyond.transform.position = _origin + Vector3.up + Vector3.forward * 3f;
        Physics.SyncTransforms();
        Check(_adapter.Probe(Lane(), bounds), ShardbackStopKind.Cover, 1.5f);
    }

    [Test]
    public void Guards_AndResolve_MissingArena()
    {
        Assert.Throws<ArgumentException>(() => new ShardbackProbeAdapter(0, _arenas));
        Assert.Throws<ArgumentNullException>(() => new ShardbackProbeAdapter(1, null));
        Assert.Throws<ArgumentException>(() => _adapter.Probe(default, Bounds));
        Assert.Throws<ArgumentException>(() => _adapter.Probe(new LaneProbeIntent(0, _origin.ToNum(),
            System.Numerics.Vector3.UnitZ, 12, 1, Lane().Key, 0), Bounds));
        var intents = new IntentBuffer();
        var session = new Session();
        typeof(ArenaPool).GetProperty("Active").SetValue(_arenas, null);
        ((IIntentSink)intents).LaneProbe(new LaneProbeIntent(7, _origin.ToNum(), System.Numerics.Vector3.UnitZ, 12, 1));
        ((IIntentSink)intents).LaneProbe(Lane(3));
        _adapter.Resolve(intents, session);
        Assert.That(session.Count, Is.EqualTo(1));
        Check(session.Last, ShardbackStopKind.Unknown, 0f);
        Assert.That(session.Last.Lane, Is.EqualTo(3));
    }

    [Test]
    public void Probe_Warm_AllocationFree()
    {
        GameObject cover = Solid(0f, 5f);
        var intents = new IntentBuffer();
        var session = new Session();
        LaneProbeIntent charge = Lane(), fan = Lane(1);
        ((IIntentSink)intents).LaneProbe(charge);
        AllocationAssert.None(() =>
        {
            _adapter.Probe(charge, Bounds);
            _adapter.Probe(fan, Bounds);
            _adapter.Resolve(intents, session);
        }, 100);
        cover.transform.position = _origin + Vector3.up;
        Physics.SyncTransforms();
        AllocationAssert.None(() => { _adapter.Probe(charge, Bounds); _adapter.Probe(fan, Bounds); }, 100);
    }

    private LaneProbeIntent Lane(int lane = 0) => new(7, _origin.ToNum(), System.Numerics.Vector3.UnitZ,
        12f, 1f, new ShardbackProbeKey(_lifetime, 1), lane);

    private void Check(ShardbackStop stop, ShardbackStopKind kind, float distance)
    {
        Assert.That(stop.Kind, Is.EqualTo(kind));
        Assert.That(stop.Distance, Is.EqualTo(distance).Within(0.002f));
        Assert.That(stop.BossId, Is.EqualTo(7));
        Assert.That(stop.Key, Is.EqualTo(Lane().Key));
        Assert.That(stop.PillarSlot, Is.EqualTo(-1));
    }

    private GameObject Make(string name)
    {
        var obj = new GameObject(name);
        _objects.Add(obj);
        return obj;
    }

    private GameObject Solid(float x, float z)
    {
        GameObject obj = Make("Cover");
        obj.layer = LayerMask.NameToLayer("Cover");
        obj.transform.position = _origin + new Vector3(x, 1f, z);
        obj.AddComponent<BoxCollider>().size = new Vector3(1, 2, 1);
        Physics.SyncTransforms();
        return obj;
    }

    private sealed class Session : IRunSession
    {
        public int Count;
        public ShardbackStop Last;
        public bool IsRunning => true;
        public RunState State => null;
        public void Start(RunConfig config) { }
        public void Tick(WorldSnapshot snapshot) { }
        public void End() { }
        public void ReportConeHits(ReadOnlySpan<int> ids) { }
        public void ReportChargeHits(ReadOnlySpan<int> ids) { }
        public void ReportBindAnchor(int id, bool hit, System.Numerics.Vector3 point) { }
        public void ReportLaneStop(int id, bool hit, float distance) { }
        public void ReportShardbackStop(in ShardbackStop report) { Last = report; Count++; }
    }
}
