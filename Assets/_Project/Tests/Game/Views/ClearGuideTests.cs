using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class ClearGuideTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/Arenas/";
    private static readonly ContentId NextArena = new("arena.jungle.grove");
    private static readonly ContentId Hollow = new("arena.jungle.hollow");
    private DomainEventHub _hub;
    private ArenaPool _pool;
    private IObjectResolver _resolver;
    private GameObject _root;
    private float _previousScale;

    [SetUp]
    public void SetUp()
    {
        _previousScale = Time.timeScale;
        Time.timeScale = 1f;
        _hub = new DomainEventHub();
        _root = new GameObject("Clear guide test");
    }

    [TearDown]
    public void TearDown()
    {
        _pool?.Dispose();
        _resolver?.Dispose();
        _pool = null;
        _resolver = null;
        Object.DestroyImmediate(_root);
        _hub.Dispose();
        Time.timeScale = _previousScale;
    }

    [Test]
    public void Driver_Sets_WritesOnce_Restores()
    {
        using var pause = new RunPause();
        using (var driver = new TimeScaleDriver(_hub, pause))
        {
            _hub.Publish(new SlowMotionStarted(0.3f));
            Assert.That(Time.timeScale, Is.EqualTo(0.3f));
            _hub.Publish(new SlowMotionStarted(0.3f));
            Assert.That(Time.timeScale, Is.EqualTo(0.3f));
            _hub.Publish(new SlowMotionEnded());
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            _hub.Publish(new SlowMotionStarted(0.3f));
        }

        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void Driver_Paused_Defers()
    {
        using var pause = new RunPause();
        using var driver = new TimeScaleDriver(_hub, pause);
        pause.Pause(PauseReason.LevelUp);
        _hub.Publish(new SlowMotionStarted(0.3f));
        driver.LateTick();
        Assert.That(Time.timeScale, Is.Zero);

        pause.Resume(PauseReason.LevelUp);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        driver.LateTick();
        Assert.That(Time.timeScale, Is.EqualTo(0.3f));

        pause.Pause(PauseReason.LevelUp);
        _hub.Publish(new SlowMotionEnded());
        Assert.That(Time.timeScale, Is.Zero);
        pause.Resume(PauseReason.LevelUp);
        driver.LateTick();
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void Guide_NoTrail()
    {
        GateGuideView guide = BuildGuide();
        ShowAt(Vector3.forward * 17f);

        Assert.That(guide.transform.Find("Trail"), Is.Null);
        foreach (LineRenderer line in guide.GetComponentsInChildren<LineRenderer>())
        {
            Assert.That(line.enabled, Is.True);
            Assert.That(line.GetPosition(16).y, Is.GreaterThan(line.GetPosition(48).y));
        }
    }

    [Test]
    public void Guide_PortalAtGate_FacesArena()
    {
        GateGuideView guide = BuildGuide();
        Vector3 gate = new(0f, 0f, 17f);
        ShowAt(gate);

        Assert.That(guide.transform.position, Is.EqualTo(gate));
        Assert.That(Vector3.Angle(guide.transform.forward, Vector3.back), Is.LessThan(0.1f));
        LineRenderer outer = Part<LineRenderer>(guide, "Outer Ring");
        Assert.That(outer.GetPosition(0).x - outer.GetPosition(32).x, Is.EqualTo(2.4f).Within(0.001f));
        Assert.That(outer.GetPosition(16).y - outer.GetPosition(48).y, Is.EqualTo(3.2f).Within(0.001f));
        Assert.That(outer.transform.TransformPoint(outer.GetPosition(48)).y, Is.Zero.Within(0.001f));
        Assert.That(outer.widthMultiplier, Is.EqualTo(0.18f));
        LineRenderer inner = Part<LineRenderer>(guide, "Inner Ring");
        Assert.That(inner.GetPosition(0).x, Is.EqualTo(outer.GetPosition(0).x * 0.8f).Within(0.001f));
        Assert.That(inner.GetPosition(16).y, Is.EqualTo(outer.GetPosition(16).y * 0.8f).Within(0.001f));

        MeshRenderer surface = Part<MeshRenderer>(guide, "Surface");
        MeshRenderer floor = Part<MeshRenderer>(guide, "Floor Glow");
        Assert.That(surface.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, Is.EqualTo(3.2f));
        Assert.That(floor.GetComponent<MeshFilter>().sharedMesh.bounds.size.x, Is.EqualTo(3.2f));
        Assert.That(Vector3.Angle(floor.transform.forward, Vector3.down), Is.LessThan(0.1f));
        Assert.That(guide.GetComponentsInChildren<Light>(), Is.Empty);
        foreach (Renderer part in guide.GetComponentsInChildren<Renderer>())
        {
            Assert.That(part.sharedMaterial, Is.SameAs(outer.sharedMaterial));
            Assert.That(part.sharedMaterial.GetFloat("_Blend"), Is.EqualTo(2f));
        }
    }

    [Test]
    public void Guide_InnerRingTurns()
    {
        GateGuideView guide = BuildGuide();
        ShowAt(Vector3.forward * 17f);
        Transform inner = Part<LineRenderer>(guide, "Inner Ring").transform;

        guide.Advance(1f);

        Assert.That(Quaternion.Angle(inner.localRotation, Quaternion.AngleAxis(90f, Vector3.forward)),
            Is.LessThanOrEqualTo(1f));
    }

    [Test]
    public void Guide_StrongHint_Values()
    {
        GateGuideView guide = BuildGuide();
        ShowAt(Vector3.forward * 17f);
        LineRenderer outer = Part<LineRenderer>(guide, "Outer Ring");
        LineRenderer inner = Part<LineRenderer>(guide, "Inner Ring");
        MeshRenderer surface = Part<MeshRenderer>(guide, "Surface");

        _hub.Publish(new GateHintChanged(true));
        Assert.That(guide.transform.localScale, Is.EqualTo(Vector3.one * 1.15f));
        AssertTint(outer, 1f);
        guide.Advance(0.125f);
        AssertTint(outer, 1.15f);
        guide.Advance(0.25f);
        AssertTint(outer, 0.85f);

        _hub.Publish(new GateHintChanged(false));
        Assert.That(guide.transform.localScale, Is.EqualTo(Vector3.one));
        AssertTint(outer, 0.55f);
        guide.Advance(0.3125f);
        AssertTint(outer, 0.7f);
        AssertTint(inner, 0.7f);
        AssertTint(surface, 0.35f);
        guide.Advance(0.625f);
        AssertTint(outer, 0.4f);
    }

    [Test]
    public void Guide_ShowsAndHides()
    {
        GateGuideView guide = BuildGuide();
        Renderer[] parts = guide.GetComponentsInChildren<Renderer>();
        Assert.That(parts, Has.Length.EqualTo(4));
        Assert.That(parts, Is.All.Matches<Renderer>(part => !part.enabled));

        _hub.Publish(new StageCleared(1, Point.Zero, default));
        Assert.That(parts, Is.All.Matches<Renderer>(part => !part.enabled));

        ShowAt(Vector3.forward * 17f);
        Assert.That(parts, Is.All.Matches<Renderer>(part => part.enabled));

        _hub.Publish(new StageTransitionStarted(1, 0.3f));
        Assert.That(parts, Is.All.Matches<Renderer>(part => !part.enabled));
        ShowAt(Vector3.right);
        Assert.That(parts, Is.All.Matches<Renderer>(part => part.enabled));

        _hub.Publish(new StageArrived(2, NextArena));
        Assert.That(parts, Is.All.Matches<Renderer>(part => !part.enabled));
        ShowAt(Vector3.left);
        Assert.That(parts, Is.All.Matches<Renderer>(part => part.enabled));

        _hub.Publish(new RunEnded(1f));
        Assert.That(parts, Is.All.Matches<Renderer>(part => !part.enabled));
        ShowAt(Vector3.back);
        Assert.That(parts, Is.All.Matches<Renderer>(part => part.enabled));
        Assert.That(guide.transform.position, Is.EqualTo(Vector3.back));
    }

    private GateGuideView BuildGuide()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GateGuideView>(Prefabs + "GateGuide.prefab");
        Assert.That(prefab, Is.Not.Null);
        var arena = AssetDatabase.LoadAssetAtPath<ArenaView>(Prefabs + "Jungle/Arena_Hollow.prefab");
        var next = AssetDatabase.LoadAssetAtPath<ArenaView>(Prefabs + "Jungle/Arena_Grove.prefab");
        var player = new GameObject("Player", typeof(CharacterController));
        player.transform.SetParent(_root.transform, false);
        _resolver = new ContainerBuilder().Build();
        _pool = new ArenaPool(_resolver, new[] { arena, next }, _root.transform, _hub, player.AddComponent<PlayerView>());
        _hub.Publish(new StageArrived(1, Hollow));

        GateGuideView guide = Object.Instantiate(prefab, _root.transform);
        guide.Construct(_hub, _pool);
        return guide;
    }

    private void ShowAt(Vector3 gate) =>
        _hub.Publish(new StageCleared(1, new Point(gate.x, gate.y, gate.z), NextArena));

    private static T Part<T>(GateGuideView guide, string name) where T : Renderer =>
        guide.transform.Find(name).GetComponent<T>();

    private static void AssertTint(Renderer renderer, float alpha)
    {
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Color expected = Palette.Player;
        expected.a = alpha;
        Color actual = properties.GetColor("_BaseColor");
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
    }
}
