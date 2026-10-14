using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-05b: the class's 3D body on a pedestal, rendered to a texture.
/// </summary>
/// <remarks>
/// Over the shipped <c>CharacterStage.prefab</c> and the Ranger's real body prefab, so the rows
/// describe the asset the Menu scene holds. In EditMode <c>Destroy</c> does nothing, so the
/// component's own edit-time path (<c>DestroyImmediate</c>) is the one these rows run through.
/// </remarks>
[TestFixture]
public sealed class CharacterPreviewTests
{
    private const string StagePath = "Assets/_Project/Prefabs/UI/CharacterStage.prefab";
    private const string BodyPath = "Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab";

    private GameObject _stage;
    private CharacterPreview _preview;
    private CharacterLook _look;

    [SetUp]
    public void SetUp()
    {
        _stage = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StagePath));
        _preview = _stage.GetComponent<CharacterPreview>();
        _look = new CharacterLook(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath));
    }

    [TearDown]
    public void TearDown()
    {
        if (_stage != null)
        {
            Object.DestroyImmediate(_stage);
        }
    }

    private Camera StageCamera => _stage.GetComponentInChildren<Camera>(true);

    [Test]
    public void Show_InstantiatesBodyOnTheAnchor()
    {
        _preview.Show(_look);

        Assert.That(_preview.IsShowing, Is.True);
        Assert.That(_preview.Body, Is.Not.Null);
        Assert.That(_preview.Body.transform.parent.name, Is.EqualTo("Anchor"));
        Assert.That(_preview.Body.GetComponentInChildren<Animator>(true), Is.Not.Null);

        foreach (MonoBehaviour behaviour in _preview.Body.GetComponentsInChildren<MonoBehaviour>(true))
        {
            Assert.That(behaviour.enabled, Is.False, $"{behaviour.GetType().Name} must not run on the stage.");
        }
    }

    [Test]
    public void Show_Another_ReplacesTheBody()
    {
        _preview.Show(_look);
        GameObject first = _preview.Body;
        Transform anchor = first.transform.parent;

        _preview.Show(_look);

        Assert.That(first == null, Is.True, "The first body is destroyed.");
        Assert.That(_preview.Body, Is.Not.Null);
        Assert.That(anchor.childCount, Is.EqualTo(1));
    }

    [Test]
    public void Hide_DestroysBodyAndDisablesCamera()
    {
        Assert.That(StageCamera.enabled, Is.False, "A stage with no body starts dark.");

        _preview.Show(_look);
        Assert.That(StageCamera.enabled, Is.True);
        GameObject body = _preview.Body;

        _preview.Hide();

        Assert.That(body == null, Is.True);
        Assert.That(_preview.Body, Is.Null);
        Assert.That(_preview.IsShowing, Is.False);
        Assert.That(StageCamera.enabled, Is.False);
    }

    [Test]
    public void Resize_MakesTheTextureCappedAndFreesTheOld()
    {
        Assert.That(_preview.Texture, Is.Null);

        _preview.Resize(512, 768);
        RenderTexture first = _preview.Texture;

        Assert.That(first.width, Is.EqualTo(512));
        Assert.That(first.height, Is.EqualTo(768));
        Assert.That(first.depth, Is.EqualTo(24));
        Assert.That(first.antiAliasing, Is.EqualTo(2));
        Assert.That(StageCamera.targetTexture, Is.SameAs(first));

        _preview.Resize(512, 768);
        Assert.That(_preview.Texture, Is.SameAs(first), "The same size makes nothing.");

        _preview.Resize(2048, 4096);
        Assert.That(first == null, Is.True, "A new size frees the old texture.");
        Assert.That(_preview.Texture.height, Is.EqualTo(1024));
        Assert.That(_preview.Texture.width, Is.EqualTo(512));
        Assert.That(StageCamera.targetTexture, Is.SameAs(_preview.Texture));
    }

    [Test]
    public void Destroy_FreesTheTexture()
    {
        _preview.Resize(256, 256);
        RenderTexture texture = _preview.Texture;

        // OnDestroy does not run on an object destroyed in EditMode (Traps §5), so it is invoked by hand.
        typeof(CharacterPreview)
            .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_preview, null);

        Assert.That(texture == null, Is.True);
        Assert.That(_preview.Texture, Is.Null);
    }

    [Test]
    public void Rotate_YawsTheBodyAndWraps()
    {
        _preview.Show(_look);
        Quaternion camera = StageCamera.transform.rotation;

        _preview.Rotate(100f);
        Assert.That(_preview.Body.transform.localEulerAngles.y, Is.EqualTo(100f).Within(0.01f));

        _preview.Rotate(300f);
        Assert.That(_preview.Body.transform.localEulerAngles.y, Is.EqualTo(40f).Within(0.01f));

        _preview.Rotate(-100f);
        Assert.That(_preview.Body.transform.localEulerAngles.y, Is.EqualTo(300f).Within(0.01f));
        Assert.That(StageCamera.transform.rotation, Is.EqualTo(camera), "The camera stays put.");
    }

    [Test]
    public void Stage_IsParkedFarFromTheOrigin()
    {
        Assert.That(_stage.transform.position.y, Is.EqualTo(-500f));
        Assert.That(StageCamera.cullingMask, Is.EqualTo(1), "Only the default layer.");
        Assert.That(StageCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
        Assert.That(StageCamera.backgroundColor.a, Is.EqualTo(0f));
    }

    [Test]
    public void Body_Idles_AnimatorIsUnscaled()
    {
        _preview.Show(_look);

        foreach (Animator animator in _preview.Body.GetComponentsInChildren<Animator>(true))
        {
            Assert.That(animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null, "Something to idle in.");
        }
    }
}
