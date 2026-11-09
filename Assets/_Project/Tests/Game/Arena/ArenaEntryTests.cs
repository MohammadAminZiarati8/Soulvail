using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Soulvail.Tests.Game.Arena;

public sealed class ArenaEntryTests
{
    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas/";
    private const string HollowPath = "Jungle/Arena_Hollow.prefab";
    private static readonly ContentId Hollow = new ContentId("arena.jungle.hollow");

    private GameObject _root;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Entry test");
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_root);
        _hub.Dispose();
    }

    [Test]
    public void Pool_Arrived_TeleportsToEntry()
    {
        using IObjectResolver resolver = new ContainerBuilder().Build();
        var playerObject = new GameObject("Player");
        playerObject.transform.SetParent(_root.transform);
        PlayerView player = playerObject.AddComponent<PlayerView>();
        using var pool = new ArenaPool(resolver, new[] { LoadArena(HollowPath) }, _root.transform, _hub, player);

        _hub.Publish(new StageArrived(1, Hollow));

        Assert.That(pool.Active.HasEntry, Is.True);
        Assert.That(player.Position, Is.EqualTo(pool.Active.EntryPosition));
    }

    [UnityTest]
    public IEnumerator Pool_Sealed_NothingMoves()
    {
        using IObjectResolver resolver = new ContainerBuilder().Build();
        var playerObject = new GameObject("Player");
        playerObject.transform.SetParent(_root.transform);
        PlayerView player = playerObject.AddComponent<PlayerView>();
        using var pool = new ArenaPool(resolver, new[] { LoadArena(HollowPath) }, _root.transform, _hub, player);
        _hub.Publish(new StageArrived(1, Hollow));
        Transform entry = pool.Active.transform.GetComponentsInChildren<Transform>(true)
            .Single(child => child.name == "Entry");
        var before = EntryTransforms(entry);

        _hub.Publish(new StageEntrySealed(1));
        double end = EditorApplication.timeSinceStartup + 1d;
        while (EditorApplication.timeSinceStartup < end)
        {
            yield return null;
        }

        Assert.That(EntryTransforms(entry), Is.EqualTo(before));
    }

    [TestCase("Arena_Pillars.prefab", 3.5f)]
    [TestCase("Arena_Tiered.prefab", 4.5f)]
    [TestCase("AshenReach/Arena_Courtyard.prefab", 3.5f)]
    [TestCase("Jungle/Arena_Clearing.prefab", 3.5f)]
    [TestCase(HollowPath, 3.5f)]
    [TestCase("Jungle/Arena_Grove.prefab", 3.5f)]
    public void Arenas_NoEntryDoor(string path, float walkLength)
    {
        ArenaView arena = LoadArena(path);

        Assert.That(arena.DescribeFaults(), Is.Null);
        Assert.That(arena.HasEntry, Is.True);
        string[] names = arena.GetComponentsInChildren<Transform>(true).Select(child => child.name).ToArray();
        Assert.That(names, Does.Contain("Entry"));
        Assert.That(names, Does.Not.Contain("Entry Door").And.Not.Contain("Slab"));
        Assert.That(arena.EntryPosition.z, Is.EqualTo(arena.PlayBounds.yMin + 0.5f));
        Assert.That(arena.PlayerStart.z - arena.EntryPosition.z, Is.EqualTo(walkLength));
        Assert.That(arena.EntryPosition.x, Is.EqualTo(arena.PlayerStart.x));
        Assert.That(arena.EntryPosition.x, Is.EqualTo(arena.GatePosition.x));
        Assert.That(arena.GatePosition.z, Is.GreaterThan(arena.PlayerStart.z));
    }

    private static (Transform, Vector3, Quaternion, Vector3, bool)[] EntryTransforms(Transform entry) =>
        entry.GetComponentsInChildren<Transform>(true)
            .Select(child => (child, child.localPosition, child.localRotation, child.localScale, child.gameObject.activeSelf))
            .ToArray();

    [Test]
    public void Flash_RampsAndClears()
    {
        GateFlashView flash = _root.AddComponent<GateFlashView>();
        flash.Construct(_hub);
        Canvas canvas = _root.GetComponentInChildren<Canvas>();
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.GreaterThan(0));

        _hub.Publish(new StageTransitionStarted(1, StageFlow.FadeTime));
        Step(flash, StageFlow.FadeTime / 2f);
        Assert.That(FlashColor(canvas).a, Is.EqualTo(0.5f));
        Step(flash, StageFlow.FadeTime / 2f);
        Assert.That(FlashColor(canvas), Is.EqualTo(Color.white));

        _hub.Publish(new StageArrived(2, Hollow));
        Step(flash, StageFlow.FadeTime);
        Assert.That(FlashColor(canvas).a, Is.EqualTo(1f));
        Step(flash, 0.08f);
        Assert.That(FlashColor(canvas).a, Is.InRange(0.01f, 0.99f));
        Step(flash, 0.07f);
        Assert.That(FlashColor(canvas).a, Is.Zero);
        Assert.That(canvas.enabled, Is.False);
    }

    [Test]
    public void Flash_OutBelowFade_Refused()
    {
        GateFlashView flash = _root.AddComponent<GateFlashView>();
        using var settings = new SerializedObject(flash);
        settings.FindProperty("_outS").floatValue = 0.2f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(flash.DescribeFaults(), Is.Not.Empty);
        Assert.Throws<InvalidOperationException>(() => flash.Construct(_hub));
    }

    private static ArenaView LoadArena(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFolder + path).GetComponent<ArenaView>();

    private static void Step(MonoBehaviour view, float dt) =>
        view.GetType().GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { dt });

    private static Color FlashColor(Canvas canvas)
    {
        Component fill = canvas.transform.GetChild(0).GetComponent("Image");
        using var settings = new SerializedObject(fill);
        return settings.FindProperty("m_Color").colorValue;
    }
}
