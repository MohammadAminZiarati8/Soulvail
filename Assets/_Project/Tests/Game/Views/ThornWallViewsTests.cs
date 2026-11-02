using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using CoreVector2 = System.Numerics.Vector2;
using CoreVector3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// JG-20e: the census that turns the three thorn events into walls, and the one wall that must
/// never trap the player it just bit. Built from the shipped <c>FX_ThornWall.prefab</c>, so the
/// collider's layer and exclusion are the asset's, not a fixture's.
/// </summary>
/// <remarks>
/// <c>Awake</c> and <c>Update</c> never run in EditMode, so every clock is driven through
/// <see cref="ThornWallView.Step"/>.
/// </remarks>
[TestFixture]
public sealed class ThornWallViewsTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Vfx/FX_ThornWall.prefab";

    private GameObject _prefab;
    private GameObject _parent;
    private GameObject _playerObject;
    private PlayerView _player;
    private DomainEventHub _hub;
    private ThornWallViews _views;

    [SetUp]
    public void CreateCensus()
    {
        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(_prefab, Is.Not.Null, PrefabPath);

        _parent = new GameObject("ThornWallParent");
        _playerObject = new GameObject("ThornWallPlayer");
        _player = _playerObject.AddComponent<PlayerView>();
        _playerObject.transform.position = new Vector3(50f, 0f, 50f);

        _hub = new DomainEventHub();
        _views = new ThornWallViews(_prefab, _hub, _parent.transform, _player);
    }

    [TearDown]
    public void DestroyCensus()
    {
        _views?.Dispose();
        _views = null;

        Object.DestroyImmediate(_parent);
        Object.DestroyImmediate(_playerObject);
    }

    [Test]
    public void Pool_PrewarmedInactive_ThirteenthRefused()
    {
        Assert.That(_views.PooledCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_views.PoolRoot.gameObject.activeSelf, Is.False, "built under an inactive root");
        Assert.That(_views.PoolRoot.childCount, Is.EqualTo(ThornSystem.Capacity));

        for (int id = 1; id <= ThornSystem.Capacity; id++)
        {
            Arm(id);
        }

        LogAssert.Expect(LogType.Warning, new Regex("no wall left for thorn 13"));
        Arm(13);
        Arm(14);
        LogAssert.NoUnexpectedReceived();

        Assert.That(_views.LiveCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_views.PooledCount, Is.Zero);
        Assert.That(_views.TryGet(13, out _), Is.False);
    }

    [Test]
    public void Armed_PlacedAndSized_StripDanger_NoCollider()
    {
        _hub.Publish(new ThornArmed(
            1,
            new CoreVector3(2f, 0f, 3f),
            new CoreVector2(1f, 0f),
            4f,
            0.5f,
            0.8f));

        Assert.That(_views.TryGet(1, out ThornWallView wall), Is.True);
        Assert.That(wall.gameObject.activeInHierarchy, Is.True);
        Assert.That(Vector3.Distance(wall.transform.position, new Vector3(4f, 0f, 3f)), Is.LessThan(1e-4f));
        Assert.That(Vector3.Angle(wall.transform.forward, Vector3.right), Is.LessThan(0.01f));
        Assert.That(wall.Collider.size.x, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(wall.Collider.size.z, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(wall.IsSolid, Is.False);
        Assert.That(wall.IsStripShown, Is.True);
        Assert.That(Palette.IsDanger(wall.StripColour), Is.True);
        Assert.That(wall.StripFill, Is.Zero);

        wall.Step(0.4f);
        Assert.That(wall.StripFill, Is.EqualTo(0.5f).Within(1e-4f));

        wall.Step(1f);
        Assert.That(wall.StripFill, Is.EqualTo(1f));
        Assert.That(wall.IsSolid, Is.False, "the arm alone never makes it solid");
    }

    [Test]
    public void Rose_PlayerClear_Solid()
    {
        Arm(1);
        _hub.Publish(new ThornRose(1, false));

        Assert.That(_views.TryGet(1, out ThornWallView wall), Is.True);
        Assert.That(wall.IsSolid, Is.True);
        Assert.That(wall.IsStripShown, Is.False);

        wall.Step(ThornWallView.RiseSeconds);
        Assert.That(wall.RiseFraction, Is.EqualTo(1f));
    }

    [Test]
    public void Rose_PlayerInside_SolidOnceClear()
    {
        Arm(1);
        Assert.That(_views.TryGet(1, out ThornWallView wall), Is.True);

        _playerObject.transform.position = wall.transform.position;
        _hub.Publish(new ThornRose(1, true));

        Assert.That(wall.IsSolid, Is.False, "a bitten player is not trapped inside");
        wall.Step(0.5f);
        Assert.That(wall.IsSolid, Is.False);

        _playerObject.transform.position = wall.transform.position + (wall.transform.right * 3f);
        wall.Step(0.016f);
        Assert.That(wall.IsSolid, Is.True);
    }

    [Test]
    public void Collider_CoverLayer_ExcludesEnemy()
    {
        BoxCollider box = _prefab.GetComponent<BoxCollider>();

        Assert.That(box, Is.Not.Null);
        Assert.That(_prefab.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")));
        Assert.That(box.excludeLayers.value & LayerMask.GetMask("Enemy"), Is.EqualTo(LayerMask.GetMask("Enemy")));
        Assert.That(box.excludeLayers.value & LayerMask.GetMask("Default"), Is.Zero, "the player is not excluded");
        Assert.That(box.enabled, Is.False, "no collider until it rises");
        Assert.That(_prefab.GetComponentInChildren<NavMeshObstacle>(true), Is.Null, "no NavMesh change");
        Assert.That(_prefab.GetComponent<ThornWallView>().Collider, Is.SameAs(box));
    }

    [Test]
    public void Withered_ColliderOff_ReturnsToPool()
    {
        Arm(1);
        _hub.Publish(new ThornRose(1, false));
        Assert.That(_views.TryGet(1, out ThornWallView wall), Is.True);
        wall.Step(ThornWallView.RiseSeconds);

        _hub.Publish(new ThornWithered(1));

        Assert.That(wall.IsSolid, Is.False, "off at once");
        Assert.That(_views.LiveCount, Is.EqualTo(1), "still sinking");

        wall.Step(ThornWallView.WitherSeconds * 0.5f);
        Assert.That(wall.RiseFraction, Is.GreaterThan(0f));

        wall.Step(ThornWallView.WitherSeconds);
        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_views.PooledCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(wall.transform.parent, Is.SameAs(_views.PoolRoot));
        Assert.That(wall.Id, Is.EqualTo(ThornWallView.Unbound));
    }

    [Test]
    public void StageArrived_ReturnsAll()
    {
        Arm(1);
        Arm(2);
        Arm(3);
        _hub.Publish(new ThornRose(2, false));

        _hub.Publish(new StageArrived(21, new ContentId("arena.jungle")));

        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_views.PooledCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_views.PoolRoot.childCount, Is.EqualTo(ThornSystem.Capacity));
    }

    [Test]
    public void Step_ArmingAndWaitingForPlayer_AllocatesNothing()
    {
        Arm(1);
        Arm(2);
        Assert.That(_views.TryGet(1, out ThornWallView arming), Is.True);
        Assert.That(_views.TryGet(2, out ThornWallView waiting), Is.True);

        _playerObject.transform.position = waiting.transform.position;
        _hub.Publish(new ThornRose(2, true));

        AllocationAssert.None(
            () =>
            {
                arming.Step(0f);
                waiting.Step(0.001f);
            },
            1_000);
    }

    private void Arm(int id) =>
        _hub.Publish(new ThornArmed(
            id,
            new CoreVector3(id * 2f, 0f, 0f),
            new CoreVector2(0f, 1f),
            3f,
            0.5f,
            0.6f));
}
