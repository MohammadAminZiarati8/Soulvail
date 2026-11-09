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
using Point = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class ClearGuideTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/Arenas/";
    private static readonly ContentId NextArena = new("arena.hollow");
    private DomainEventHub _hub;
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
    public void Guide_ShowsAndHides()
    {
        GateGuideView guide = BuildGuide(Vector3.zero);
        LineRenderer beam = Line(guide, "Beam");
        LineRenderer trail = Line(guide, "Trail");
        Assert.That(beam.enabled || trail.enabled, Is.False);

        ShowAt(new Vector3(4f, 0f, 0f));
        Assert.That(beam.enabled && trail.enabled, Is.True);
        Assert.That(beam.GetPosition(0), Is.EqualTo(new Vector3(4f, 0f, 0f)));
        Assert.That(beam.GetPosition(1).y, Is.EqualTo(14f));

        _hub.Publish(new StageTransitionStarted(1, 0.3f));
        Assert.That(beam.enabled || trail.enabled, Is.False);
        ShowAt(Vector3.right);
        _hub.Publish(new StageArrived(2, NextArena));
        Assert.That(beam.enabled || trail.enabled, Is.False);
    }

    [Test]
    public void Guide_StrongHint_Values()
    {
        GateGuideView guide = BuildGuide(Vector3.zero);
        LineRenderer beam = Line(guide, "Beam");
        LineRenderer trail = Line(guide, "Trail");
        ShowAt(Vector3.right);

        _hub.Publish(new GateHintChanged(true));
        Assert.That(beam.widthMultiplier, Is.EqualTo(2.4f).Within(0.001f));
        AssertTint(beam, 1f);
        AssertTint(trail, 1f);

        _hub.Publish(new GateHintChanged(false));
        Assert.That(beam.widthMultiplier, Is.EqualTo(1.6f));
        AssertTint(beam, 0.55f);
        AssertTint(trail, 0.55f);
    }

    [Test]
    public void Guide_TrailEndsAtGate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<ArenaView>(Prefabs + "Jungle/Arena_Hollow.prefab");
        ArenaView arena = Object.Instantiate(prefab, _root.transform);
        arena.Surface.AddData();

        GateGuideView guide = BuildGuide(arena.PlayerStart);
        ShowAt(arena.GatePosition);

        LineRenderer trail = Line(guide, "Trail");
        Assert.That(trail.positionCount, Is.GreaterThan(2), "The baked mesh should supply the path.");
        Assert.That(Vector3.Distance(trail.GetPosition(trail.positionCount - 1), arena.GatePosition),
            Is.LessThanOrEqualTo(0.1f));
    }

    private GateGuideView BuildGuide(Vector3 playerStart)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GateGuideView>(Prefabs + "GateGuide.prefab");
        Assert.That(prefab, Is.Not.Null);
        GateGuideView guide = Object.Instantiate(prefab, _root.transform);
        var player = new GameObject("Player", typeof(CharacterController));
        player.transform.SetParent(_root.transform, false);
        player.transform.position = playerStart;
        guide.Construct(_hub, player.AddComponent<PlayerView>());
        return guide;
    }

    private void ShowAt(Vector3 gate) =>
        _hub.Publish(new StageCleared(1, new Point(gate.x, gate.y, gate.z), NextArena));

    private static LineRenderer Line(GateGuideView guide, string name) =>
        guide.transform.Find(name).GetComponent<LineRenderer>();

    private static void AssertTint(LineRenderer line, float alpha)
    {
        var properties = new MaterialPropertyBlock();
        line.GetPropertyBlock(properties);
        Color expected = Palette.Player;
        expected.a = alpha;
        Color actual = properties.GetColor("_BaseColor");
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
    }
}
