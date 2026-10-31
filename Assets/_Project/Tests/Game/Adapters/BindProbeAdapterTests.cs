using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Adapters;

[TestFixture]
public sealed class BindProbeAdapterTests
{
    private readonly List<GameObject> _objects = new();
    private readonly Vector3 _origin = new(1000f, 0f, 1000f);
    private DomainEventHub _hub;
    private BindingChainView _chains;
    private BindProbeAdapter _probe;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _probe = new BindProbeAdapter(LayerMask.GetMask("Cover"));
        _chains = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BindingChainView>(
            "Assets/_Project/Prefabs/Vfx/VFX_BindingChain.prefab"));
        _objects.Add(_chains.gameObject);
        _chains.Construct(_hub, null);
    }

    [TearDown]
    public void TearDown()
    {
        _chains.Dispose();
        _hub.Dispose();
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
    public void Probe_HitsAStone()
    {
        GameObject stone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab"), _origin, Quaternion.identity);
        _objects.Add(stone);
        Physics.SyncTransforms();
        var box = stone.GetComponentInChildren<BoxCollider>();
        Assert.That(box.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")));
        Vector3 start = new Vector3(box.bounds.min.x - 2f, _origin.y, box.bounds.center.z);
        Assert.That(_probe.Probe(Probe(start, Vector3.right), out Vector3 hit), Is.True);
        Assert.That(hit.x, Is.EqualTo(box.bounds.min.x).Within(0.05f));
    }

    [Test]
    public void Probe_HitsTheWall()
    {
        Solid(_origin + Vector3.forward * 2f, 0);
        Assert.That(_probe.Probe(Probe(_origin), out Vector3 hit), Is.True);
        Assert.That(hit.z, Is.EqualTo(_origin.z + 1.5f).Within(0.01f));
        Assert.That(hit.y, Is.EqualTo(1.1f).Within(0.01f));
    }

    [Test]
    public void Probe_MissesOpenGround()
    {
        GameObject floor = Solid(_origin + Vector3.down * 0.25f, 0);
        floor.transform.position = _origin + Vector3.down * 0.25f;
        floor.GetComponent<BoxCollider>().size = Vector3.one;
        floor.transform.localScale = new Vector3(12f, 0.5f, 12f);
        Physics.SyncTransforms();
        Assert.That(_probe.Probe(Probe(_origin), out _), Is.False);
    }

    [Test]
    public void Probe_SphereGrazesTree_AndIgnoresEnemyAndTriggers()
    {
        GameObject enemy = Solid(_origin + Vector3.forward, LayerMask.NameToLayer("Enemy"));
        GameObject trigger = Solid(_origin + Vector3.forward * 2f, 0);
        trigger.GetComponent<Collider>().isTrigger = true;
        Assert.That(_probe.Probe(Probe(_origin), out _), Is.False);
        GameObject tree = Solid(_origin + new Vector3(0.65f, 0f, 3f), 0);
        Assert.That(_probe.Probe(Probe(_origin), out _), Is.True, "The 0.2 m sphere catches an off-axis solid.");
        Assert.That(enemy, Is.Not.Null);
        Assert.That(tree, Is.Not.Null);
    }

    [Test]
    public void Probe_AnsweredSameFrame()
    {
        Solid(_origin + Vector3.forward * 2f, 0);
        var intents = new IntentBuffer();
        var session = new RecordingSession();
        ((IIntentSink)intents).BindProbe(Probe(_origin));
        ((IIntentSink)intents).BindProbe(new BindProbeIntent(2, (_origin + Vector3.right * 10f).ToNum(),
            System.Numerics.Vector3.UnitZ, 4f));
        _probe.Resolve(intents, session);
        Assert.That(session.Answers.Count, Is.EqualTo(2));
        Assert.That(session.Answers[0], Is.EqualTo((1, true)));
        Assert.That(session.Answers[1], Is.EqualTo((2, false)));
        Assert.That(session.Points[0].Y, Is.EqualTo(1.1f).Within(0.01f));
    }

    [TestCase(BindAnchor.Enemy)]
    [TestCase(BindAnchor.Obstacle)]
    public void Chain_SpansTargetToAnchor_AndFollows(BindAnchor kind)
    {
        Transform target = Body("Target", _origin);
        Transform anchor = Body("Anchor", _origin + Vector3.forward * 3f);
        Vector3 point = _origin + new Vector3(0f, 1.1f, 4f);
        LineRenderer line = _chains.Show(new BindingShotBound(1, kind, 2, point.ToNum(), 2f), target, anchor);
        float sag = line.GetPosition(1).y;
        _chains.Step(0.15f);
        Vector3 end = kind == BindAnchor.Enemy ? anchor.position + Vector3.up * 1.1f : point;
        Assert.That(line.GetPosition(0), Is.EqualTo(target.position + Vector3.up * 1.1f));
        Assert.That(line.GetPosition(2), Is.EqualTo(end));
        Assert.That(line.GetPosition(1).y, Is.GreaterThan(sag));
        Assert.That(line.transform.childCount, Is.EqualTo(2), "One clasp at either end.");
        target.position += Vector3.right;
        anchor.position += Vector3.left;
        _chains.Step(0f);
        Assert.That(line.GetPosition(0), Is.EqualTo(target.position + Vector3.up * 1.1f));
        Assert.That(line.GetPosition(2), Is.EqualTo(kind == BindAnchor.Enemy ? anchor.position + Vector3.up * 1.1f : point));
    }

    [Test]
    public void Chain_EndsWithStun()
    {
        LineRenderer line = Show();
        _chains.Step(5f);
        Assert.That(_chains.ActiveCount, Is.EqualTo(1), "Use the actual end event, including extended stuns.");
        _hub.Publish(new EnemyStatusChanged(1, EnemyStatus.Stunned, 0f));
        _chains.Step(0.06f);
        Assert.That(line.startColor.a, Is.InRange(0.4f, 0.6f));
        _chains.Step(0.07f);
        Assert.That(_chains.ActiveCount, Is.Zero);
        Assert.That(line.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Alone_ShowsSnap()
    {
        Transform target = Body("Alone", _origin);
        LineRenderer line = _chains.Show(new BindingShotBound(1, BindAnchor.None, 0, default, 0.75f), target, null);
        Assert.That(line.loop, Is.True);
        Assert.That(line.positionCount, Is.EqualTo(12));
        float initial = (line.GetPosition(0) - target.position).x;
        _chains.Step(0.15f);
        Assert.That((line.GetPosition(0) - target.position).x, Is.LessThan(initial));
        _hub.Publish(new EnemyStatusChanged(1, EnemyStatus.Stunned, 0f));
        _chains.Step(0.13f);
        Assert.That(_chains.ActiveCount, Is.Zero);
    }

    [Test]
    public void Chains_Pooled()
    {
        int objects = _chains.GetComponentsInChildren<Transform>(true).Length;
        LineRenderer first = Show();
        _chains.Clear();
        LineRenderer again = Show();
        Assert.That(again, Is.SameAs(first));
        Assert.That(_chains.PoolSize, Is.EqualTo(32));
        Assert.That(_chains.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects));
    }

    [TestCase("death")]
    [TestCase("despawn")]
    [TestCase("stage")]
    [TestCase("anchor")]
    public void Chains_EndAtLifecycleBoundary(string boundary)
    {
        Show();
        if (boundary == "death")
        {
            _hub.Publish(new EnemyDied(1, new ContentId("enemy.husk"), default));
        }
        else if (boundary == "despawn")
        {
            _hub.Publish(new EnemyDespawned(1));
        }
        else if (boundary == "anchor")
        {
            _hub.Publish(new EnemyDespawned(2));
        }
        else
        {
            _hub.Publish(default(StageTransitionStarted));
        }
        Assert.That(_chains.ActiveCount, Is.Zero);
    }

    [Test]
    public void BindArrow_CyanRope_RestoresOnPoolReturn_AndClearsAtBoundary()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        using IObjectResolver container = builder.Build();
        var arrow = AssetDatabase.LoadAssetAtPath<ProjectileView>("Assets/_Project/Prefabs/Projectiles/Arrow.prefab");
        using var views = new ProjectileViews(container, arrow, Body("Arrows", Vector3.zero), _hub, prewarm: 1);
        _hub.Publish(new ProjectileFired(1, new ContentId("character.ranger"), 0, default,
            System.Numerics.Vector3.UnitZ * 4f, 1f));
        Assert.That(views.TryGet(1, out ProjectileView shot), Is.True);
        TrailRenderer trail = shot.GetComponentInChildren<TrailRenderer>();
        float time = trail.time;
        _hub.Publish(new SkillCast(new ContentId("skill.marksman.binding-shot"), 12f, false));
        Assert.That(trail.time, Is.EqualTo(0.12f));
        Assert.That(trail.startColor, Is.EqualTo(Soulvail.Game.Presentation.Palette.Player));
        _hub.Publish(default(StageTransitionStarted));
        Assert.That(views.Count, Is.Zero);
        Assert.That(trail.time, Is.EqualTo(time));
        Assert.That(views.PooledCount, Is.EqualTo(1));
    }

    [Test]
    public void Chain_BoundEventResolvesThePooledEnemyBodies()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_hub);
        using IObjectResolver container = builder.Build();
        EnemyView template = Body("Enemy template", _origin).gameObject.AddComponent<EnemyView>();
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>());
        using var enemies = new EnemyViews(container, template, Body("Enemies", _origin), _hub, looks, 2);
        _chains.Dispose();
        _chains.Construct(_hub, enemies);
        _hub.Publish(new EnemySpawned(1, new ContentId("enemy.husk"), _origin.ToNum()));
        _hub.Publish(new EnemySpawned(2, new ContentId("enemy.husk"), (_origin + Vector3.forward * 3f).ToNum()));
        _hub.Publish(new BindingShotBound(1, BindAnchor.Enemy, 2, default, 2f));
        Assert.That(_chains.ActiveCount, Is.EqualTo(1));
        _hub.Publish(new EnemyDespawned(2));
        Assert.That(_chains.ActiveCount, Is.Zero);
    }

    [Test]
    public void Cast_UsesQuickNormalDrawThenRelease()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab");
        GameObject body = Object.Instantiate(prefab);
        _objects.Add(body);
        var animator = body.GetComponentInChildren<Animator>();
        var view = body.GetComponentInChildren<RangerAnimatorView>();
        view.Construct(_hub);
        _hub.Publish(new SkillCast(new ContentId("skill.marksman.binding-shot"), 12f, false));
        animator.Update(0f);
        int layer = animator.GetLayerIndex(RangerAnimatorView.UpperBodyLayer);
        Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName("Draw"), Is.True);
        Assert.That(animator.GetFloat("ShotSpeed"), Is.EqualTo(4f));
        Assert.That(animator.GetBool("Release"), Is.False);
        view.Step(0.1f);
        Assert.That(animator.GetBool("Release"), Is.True);
    }

    private LineRenderer Show() => _chains.Show(new BindingShotBound(1, BindAnchor.Enemy, 2, default, 2f),
        Body("Target", _origin), Body("Anchor", _origin + Vector3.forward * 3f));

    private BindProbeIntent Probe(Vector3 start, Vector3 direction = default) => new(1, start.ToNum(),
        (direction == default ? Vector3.forward : direction).ToNum(), 4f);

    private Transform Body(string name, Vector3 at)
    {
        var obj = new GameObject(name);
        obj.transform.position = at;
        _objects.Add(obj);
        return obj.transform;
    }

    private GameObject Solid(Vector3 at, int layer)
    {
        GameObject obj = Body("Solid", at + Vector3.up).gameObject;
        obj.layer = layer;
        var box = obj.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 2f, 1f);
        Physics.SyncTransforms();
        return obj;
    }

    private sealed class RecordingSession : IRunSession
    {
        public readonly List<(int Id, bool Hit)> Answers = new();
        public readonly List<System.Numerics.Vector3> Points = new();
        public bool IsRunning => true;
        public RunState State => null;
        public void Start(RunConfig config) { }
        public void Tick(WorldSnapshot snapshot) { }
        public void ReportConeHits(ReadOnlySpan<int> ids) { }
        public void ReportChargeHits(ReadOnlySpan<int> ids) { }
        public void End() { }
        public void ReportBindAnchor(int id, bool hit, System.Numerics.Vector3 point)
        {
            Answers.Add((id, hit));
            Points.Add(point);
        }
    }
}
