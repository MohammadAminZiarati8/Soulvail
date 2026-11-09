using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Views;
using UnityEngine;

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
