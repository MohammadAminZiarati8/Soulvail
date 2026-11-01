using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>JG-15g: Unity answers the Warlord's charge lane with a Cover-mask sphere cast.</summary>
[TestFixture]
public sealed class LaneProbeAdapterTests
{
    private const float Reach = 14f, Radius = 0.8f;
    private readonly List<GameObject> _objects = new();
    private readonly Vector3 _origin = new(2000f, 0f, 2000f);
    private LaneProbeAdapter _probe;

    [SetUp]
    public void SetUp() => _probe = new LaneProbeAdapter(LayerMask.GetMask("Cover"));

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject obj in _objects)
        {
            if (obj != null)
            {
                Object.DestroyImmediate(obj);
            }
        }
        _objects.Clear();
    }

    [Test]
    public void LaneProbe_StoneAhead_ReportsDistance()
    {
        GameObject stones = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Arenas/Jungle/Cover_WarlordStones.prefab"), _origin, Quaternion.identity);
        _objects.Add(stones);
        Physics.SyncTransforms();
        BoxCollider box = stones.GetComponentInChildren<BoxCollider>();
        Vector3 toStone = box.bounds.center - _origin;
        toStone.y = 0f;

        Assert.That(_probe.Probe(Lane(_origin, toStone.normalized), out float distance), Is.True);
        Assert.That(distance, Is.GreaterThan(3f).And.LessThan(toStone.magnitude),
            "The sphere stops at the stone's face, short of its centre.");
    }

    [Test]
    public void LaneProbe_Clear_ReportsMiss()
    {
        // A Default solid (a boundary wall or tree) never stops a charge; only Cover dazes.
        Solid(_origin + (Vector3.forward * 5f), 0);
        Assert.That(_probe.Probe(Lane(_origin, Vector3.forward), out float distance), Is.False);
        Assert.That(distance, Is.Zero);

        Solid(_origin + (Vector3.forward * 20f), LayerMask.NameToLayer("Cover"));
        Assert.That(_probe.Probe(Lane(_origin, Vector3.forward), out _), Is.False, "Beyond the lane's reach.");
        Assert.That(_probe.Probe(new LaneProbeIntent(1, _origin.ToNum(), System.Numerics.Vector3.Zero, Reach, Radius),
            out _), Is.False, "No direction asks nothing.");
    }

    [Test]
    public void Resolve_AnswersEach()
    {
        Solid(_origin + (Vector3.forward * 5f), LayerMask.NameToLayer("Cover"));
        var intents = new IntentBuffer();
        var session = new RecordingSession();
        ((IIntentSink)intents).LaneProbe(Lane(_origin, Vector3.forward, 7));
        ((IIntentSink)intents).LaneProbe(Lane(_origin, Vector3.back, 9));

        _probe.Resolve(intents, session);

        Assert.That(session.Answers.Count, Is.EqualTo(2));
        Assert.That(session.Answers[0].Id, Is.EqualTo(7));
        Assert.That(session.Answers[0].Hit, Is.True);
        Assert.That(session.Answers[0].Distance, Is.EqualTo(4.5f - Radius).Within(0.02f),
            "Sphere travel to the face 4.5 m out.");
        Assert.That(session.Answers[1], Is.EqualTo((9, false, 0f)));
    }

    [Test]
    public void Constructor_RefusesAnEmptyMask() =>
        Assert.Throws<ArgumentException>(() => _ = new LaneProbeAdapter(0));

    private LaneProbeIntent Lane(Vector3 from, Vector3 direction, int id = 1) =>
        new(id, from.ToNum(), direction.ToNum(), Reach, Radius);

    private void Solid(Vector3 at, int layer)
    {
        var obj = new GameObject("Solid") { layer = layer };
        obj.transform.position = at + Vector3.up;
        obj.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        _objects.Add(obj);
        Physics.SyncTransforms();
    }

    private sealed class RecordingSession : IRunSession
    {
        public readonly List<(int Id, bool Hit, float Distance)> Answers = new();
        public bool IsRunning => true;
        public RunState State => null;
        public void Start(RunConfig config) { }
        public void Tick(WorldSnapshot snapshot) { }
        public void ReportConeHits(ReadOnlySpan<int> ids) { }
        public void ReportChargeHits(ReadOnlySpan<int> ids) { }
        public void ReportBindAnchor(int targetId, bool hit, System.Numerics.Vector3 point) { }
        public void End() { }
        public void ReportLaneStop(int enemyId, bool hit, float distance) => Answers.Add((enemyId, hit, distance));
    }
}
