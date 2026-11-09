using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The camera's field of view follows the screen's shape: the short side keeps its angle, so a
/// portrait phone shows as much ground across as a landscape one did up and down (UI-08b).
/// </summary>
/// <remarks>
/// <c>OnEnable</c> and <c>LateUpdate</c> do not run in EditMode (Traps §5), so the fixture calls
/// them itself through reflection, once, in the setup. The camera's aspect is set by hand: with
/// no screen behind it an unset aspect is whatever the Game view happens to be.
/// </remarks>
[TestFixture]
public sealed class FollowCameraTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject _target;
    private GameObject _cameraObject;
    private Camera _camera;
    private FollowCamera _follow;

    [SetUp]
    public void CreateCamera()
    {
        _target = new GameObject("Target");
        _cameraObject = new GameObject("Camera");
        _camera = _cameraObject.AddComponent<Camera>();
        _follow = _cameraObject.AddComponent<FollowCamera>();

        typeof(FollowCamera).GetField("_target", Private).SetValue(_follow, _target.transform);
    }

    [TearDown]
    public void Destroy()
    {
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_target);
    }

    private void Invoke(string method)
    {
        typeof(FollowCamera).GetMethod(method, Private).Invoke(_follow, null);
    }

    [Test]
    public void VerticalFov_Landscape_IsTheShortSide()
    {
        Assert.That(FollowCamera.VerticalFov(60f, 16f / 9f), Is.EqualTo(60f).Within(0.001f));
    }

    [Test]
    public void VerticalFov_Portrait_KeepsHorizontalAtShortSide()
    {
        float aspect = 9f / 20f;
        float vertical = FollowCamera.VerticalFov(60f, aspect);

        // Back to the horizontal: 2·atan(tan(vertical/2) · aspect).
        float horizontal = 2f * Mathf.Atan(Mathf.Tan(vertical * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;

        Assert.That(horizontal, Is.EqualTo(60f).Within(0.1f));
        Assert.That(vertical, Is.GreaterThan(100f).And.LessThan(108f));
    }

    [Test]
    public void VerticalFov_Square_IsTheShortSide()
    {
        Assert.That(FollowCamera.VerticalFov(60f, 1f), Is.EqualTo(60f).Within(0.001f));
    }

    [Test]
    public void Footprint_Pitch57_Matches()
    {
        Rect footprint = FollowCamera.Footprint(57f, 16f, 60f, 2.1667f);

        Assert.That(footprint.xMin, Is.EqualTo(-20f).Within(0.02f));
        Assert.That(footprint.xMax, Is.EqualTo(20f).Within(0.02f));
        Assert.That(footprint.yMin, Is.EqualTo(-8f).Within(0.05f));
        Assert.That(footprint.yMax, Is.EqualTo(17.6f).Within(0.05f));
    }

    [TestCase(20f)]
    [TestCase(30f)]
    public void Footprint_ShallowPitch_FarIsInfinite(float pitch)
    {
        Rect footprint = FollowCamera.Footprint(pitch, 16f, 60f, 2f);
        Assert.That(footprint.yMax, Is.EqualTo(float.PositiveInfinity));
        Assert.That(FollowCamera.ClampFocus(Vector2.one, footprint, new Rect(-15f, -15f, 30f, 30f), 2f).y,
            Is.Zero, "A horizon-reaching footprint has a stable, finite focus.");
    }

    [Test]
    public void ClampFocus_ArenaFits_Centres()
    {
        Rect footprint = new Rect(-20f, -8f, 40f, 26f);
        Rect bounds = new Rect(-10f, -7f, 30f, 22f);

        Vector2 focus = FollowCamera.ClampFocus(new Vector2(100f, -100f), footprint, bounds, 2f);

        Assert.That(focus + footprint.center, Is.EqualTo(bounds.center));
    }

    [Test]
    public void ClampFocus_ArenaLarger_StopsAtGrownEdge()
    {
        Rect footprint = new Rect(-16.4f, -8f, 32.8f, 25.6f);
        Rect bounds = new Rect(-22f, -14f, 44f, 28f);

        Vector2 focus = FollowCamera.ClampFocus(new Vector2(100f, 0f), footprint, bounds, 2f);

        Assert.That(focus.x + footprint.xMax, Is.EqualTo(24f).Within(0.001f));
    }

    [Test]
    public void ClampFocus_Inside_Unchanged()
    {
        Rect footprint = new Rect(-10f, -8f, 20f, 26f);
        Rect bounds = new Rect(-22f, -20f, 44f, 40f);
        var focus = new Vector2(3f, -2f);

        Assert.That(FollowCamera.ClampFocus(focus, footprint, bounds, 2f), Is.EqualTo(focus));
    }

    [Test]
    public void LateUpdate_NoPool_Unclamped()
    {
        Invoke("OnEnable");
        Vector3 offset = _cameraObject.transform.position;
        _target.transform.position = new Vector3(40f, 0f, 40f);
        typeof(FollowCamera).GetMethod("Step", Private).Invoke(_follow, new object[] { 10f });

        Assert.That(Vector3.Distance(_cameraObject.transform.position, _target.transform.position + offset),
            Is.LessThan(0.01f));
    }

    [Test]
    public void LateUpdate_Clamp_AllocatesNothing()
    {
        const string path = "Assets/_Project/Prefabs/Arenas/Arena_Pillars.prefab";
        ArenaView prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>();
        using IObjectResolver container = new ContainerBuilder().Build();
        using var hub = new DomainEventHub();
        PlayerView player = _target.AddComponent<PlayerView>();
        using var pool = new ArenaPool(container, new[] { prefab }, null, hub, player);
        _follow.Construct(pool);
        _camera.aspect = 16f / 9f;
        _target.transform.position = new Vector3(100f, 0f, 0f);
        Invoke("OnEnable");

        hub.Publish(new StageArrived(1, prefab.Id));
        Assert.That(pool.Active, Is.Not.Null);
        _target.transform.position = new Vector3(100f, 0f, 0f);
        var lateUpdate = (Action)Delegate.CreateDelegate(typeof(Action), _follow,
            typeof(FollowCamera).GetMethod("LateUpdate", Private));

        AllocationAssert.None(lateUpdate);

        Rect footprint = FollowCamera.Footprint(57f, 16f, 60f, _camera.aspect);
        Assert.That(_cameraObject.transform.position.x + footprint.xMax,
            Is.EqualTo(pool.Active.PlayBounds.xMax + 2f).Within(0.001f));
    }

    [Test]
    public void FollowCamera_SetsFovOnAspectChangeOnly()
    {
        _camera.aspect = 9f / 20f;
        Invoke("OnEnable");

        Assert.That(_camera.fieldOfView, Is.EqualTo(FollowCamera.VerticalFov(60f, 9f / 20f)).Within(0.001f),
            "Enabling the camera applies the field for the aspect it finds.");

        // A sentinel nothing in the component would write: if a frame with the same aspect
        // rewrites the field, the sentinel is gone.
        _camera.fieldOfView = 42f;
        Invoke("LateUpdate");
        Invoke("LateUpdate");

        Assert.That(_camera.fieldOfView, Is.EqualTo(42f), "A frame whose aspect has not changed writes nothing.");

        _camera.aspect = 16f / 9f;
        Invoke("LateUpdate");

        Assert.That(_camera.fieldOfView, Is.EqualTo(60f).Within(0.001f), "A frame whose aspect has changed writes the new field.");
    }
}
