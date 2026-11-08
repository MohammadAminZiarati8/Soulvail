using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Num3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

public sealed class RoarRingViewTests
{
    private const string Root = "Assets/_Project/";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly PetRoared _roar = new PetRoared(1, new Num3(2f, 0f, 4f), 3f);
    private GameObject _parent;
    private DomainEventHub _hub;
    private RoarRingView _ring;

    [TearDown]
    public void Dispose()
    {
        _ring?.Dispose();
        _hub?.Dispose();
        Object.DestroyImmediate(_parent);
        _ring = null;
        _hub = null;
        _parent = null;
    }

    [Test]
    public void PetRoared_PlaysHowl_UnknownIgnored()
    {
        _parent = new GameObject("Pet roar test");
        _hub = new DomainEventHub();
        using var resolver = new ContainerBuilder().Build();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Pets/Hound.prefab");
        using var views = new PetViews(resolver, prefab.GetComponent<PetView>(), _parent.transform, _hub);
        _hub.Publish(new PetSpawned(1, Num3.Zero));
        PetView pet = _parent.GetComponentInChildren<PetView>();
        Animator animator = pet.GetComponentInChildren<Animator>();
        int howl = Animator.StringToHash("Howl");
        animator.Rebind();
        animator.Update(0f);

        _hub.Publish(new PetRoared(2, _roar.Position, _roar.Radius));
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(howl));

        pet.Leap(Vector3.forward, 0.25f);
        _hub.Publish(_roar);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(howl));
        pet.StepLeap(0.25f);

        pet.Fall(Vector3.zero);
        _hub.Publish(_roar);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(howl));
        pet.Rise();

        _hub.Publish(_roar);
        animator.Update(0.02f);
        animator.Update(0.02f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(howl));
    }

    [Test]
    public void Ring_SizedToRadius_FadesInPointFour_ThenPooled()
    {
        LineRenderer line = CreateRing();
        _hub.Publish(_roar);

        Assert.That(line.gameObject.activeInHierarchy, Is.True);
        Assert.That(line.transform.position, Is.EqualTo(new Vector3(2f, 0.06f, 4f)));
        Assert.That(line.GetPosition(line.positionCount - 1).magnitude,
            Is.EqualTo(line.GetPosition(0).magnitude).Within(0.001f));
        float startRadius = line.GetPosition(0).magnitude;
        Assert.That(startRadius, Is.GreaterThan(0f).And.LessThan(_roar.Radius));

        _ring.Step(0.2f);
        Assert.That(line.gameObject.activeInHierarchy, Is.True);
        Assert.That(line.GetPosition(0).magnitude, Is.GreaterThan(startRadius).And.LessThan(_roar.Radius));
        Assert.That(line.startColor.a, Is.EqualTo(0.5f).Within(1f / 255f));

        _ring.Step(0.2f);
        Assert.That(line.GetPosition(0).magnitude, Is.EqualTo(_roar.Radius).Within(0.001f));
        Assert.That(line.startColor.a, Is.Zero);
        Assert.That(line.endColor.a, Is.Zero);
        Assert.That(line.gameObject.activeSelf, Is.False);

        _hub.Publish(_roar);
        Assert.That(_parent.GetComponentsInChildren<LineRenderer>(true), Is.EqualTo(new[] { line }));
        Assert.That(line.gameObject.activeInHierarchy, Is.True);
        Assert.That(line.GetPosition(0).magnitude, Is.EqualTo(startRadius));
        Assert.That(line.startColor.a, Is.EqualTo(1f));
    }

    [Test]
    public void Ring_IsPlayerCyan()
    {
        LineRenderer line = CreateRing();
        _hub.Publish(_roar);

        Assert.That(line.startColor, Is.EqualTo(Palette.Player));
        Assert.That(line.endColor, Is.EqualTo(Palette.Player));
    }

    [Test]
    public void RunScope_MissingRingPrefab_Throws()
    {
        const string scenePath = Root + "Scenes/Run.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (opened)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        try
        {
            RunScope source = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RunScope>(true)).Single();
            _parent = new GameObject("Undressed scope");
            _parent.SetActive(false);
            var scope = _parent.AddComponent<RunScope>();
            EditorUtility.CopySerialized(source, scope);

            var data = new SerializedObject(scope);
            data.FindProperty("_roarRingPrefab").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();

            var exception = Assert.Throws<TargetInvocationException>(() =>
                typeof(RunScope).GetMethod("Configure", Private).Invoke(scope, new object[] { new ContainerBuilder() }));

            Assert.That(exception.InnerException, Is.TypeOf<MissingReferenceException>());
            Assert.That(exception.InnerException.Message, Does.Contain("_roarRingPrefab"));
        }
        finally
        {
            if (opened)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private LineRenderer CreateRing()
    {
        _parent = new GameObject("Roar ring test");
        _hub = new DomainEventHub();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Vfx/RoarRing.prefab");
        _ring = new RoarRingView(_hub, prefab.GetComponent<LineRenderer>(), _parent.transform);
        return _parent.GetComponentInChildren<LineRenderer>(true);
    }
}
