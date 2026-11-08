using System;
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

public sealed class HowlDrawnTests
{
    private const string Root = "Assets/_Project/";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly PackHowled _howl = new(new Num3(2f, 0f, 4f));
    private GameObject _parent;
    private DomainEventHub _hub;

    [SetUp]
    public void SetUp()
    {
        _parent = new GameObject("Howl test");
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        _hub.Dispose();
        Object.DestroyImmediate(_parent);
    }

    [Test]
    public void Howled_EveryStandingHoundHowls_FallenDoesNot()
    {
        using var resolver = new ContainerBuilder().Build();
        using var views = new PetViews(resolver, Hound(), _parent.transform, _hub);
        for (int id = 1; id <= 3; id++)
        {
            _hub.Publish(new PetSpawned(id, Num3.Zero, isLead: id == 1));
        }

        PetView[] pets = _parent.GetComponentsInChildren<PetView>();
        foreach (PetView pet in pets)
        {
            Animator animator = pet.GetComponentInChildren<Animator>();
            animator.Rebind();
            animator.Update(0f);
        }

        _hub.Publish(new PetFell(1, Num3.Zero));
        _hub.Publish(_howl);
        Assert.That(pets, Has.Length.EqualTo(3));
        foreach (PetView pet in pets)
        {
            Animator animator = pet.GetComponentInChildren<Animator>();
            animator.Update(0.02f);
            animator.Update(0.02f);
            string expected = pet.Id == 1 ? "Fall" : "Howl";
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(Animator.StringToHash(expected)), $"Pet {pet.Id}");
        }
    }

    [Test]
    public void Glow_EndsWithBuff_ExtendedByRehowl_CutOnFall()
    {
        PetView pet = SpawnHound();
        var body = pet.GetComponentInChildren<SkinnedMeshRenderer>();
        Material original = body.sharedMaterial;

        pet.Howl(4f, 10f);
        Material glow = body.sharedMaterial;
        Assert.That(glow, Is.Not.SameAs(original));
        pet.StepHowl(13.99f);
        Assert.That(body.sharedMaterial, Is.SameAs(glow));
        pet.StepHowl(14f);
        Assert.That(body.sharedMaterial, Is.SameAs(original));

        pet.Howl(4f, 20f);
        pet.Howl(5f, 23f);
        pet.StepHowl(24f);
        Assert.That(body.sharedMaterial, Is.SameAs(glow));
        pet.StepHowl(28f);
        Assert.That(body.sharedMaterial, Is.SameAs(original));

        pet.Howl(4f, 30f);
        pet.Fall(Vector3.zero);
        pet.Howl(4f, 31f);
        Assert.That(body.sharedMaterial, Is.SameAs(original));
    }

    [Test]
    public void Pulse_SixMetres_HalfSecond_ThenPooled()
    {
        using var pulse = new HowlPulseView(_hub, PulsePrefab(), _parent.transform);
        var line = _parent.GetComponentInChildren<LineRenderer>(true);
        Assert.That(line.gameObject.activeSelf, Is.False);

        _hub.Publish(_howl);
        Assert.That(line.transform.position, Is.EqualTo(new Vector3(2f, 0.06f, 4f)));
        Assert.That(line.GetPosition(0).magnitude, Is.Zero);

        pulse.Step(0.25f);
        Assert.That(line.gameObject.activeInHierarchy, Is.True);
        Assert.That(line.GetPosition(0).magnitude, Is.EqualTo(3f).Within(0.001f));
        Assert.That(line.GetPosition(line.positionCount - 1), Is.EqualTo(line.GetPosition(0)));
        Assert.That(line.startColor.a, Is.EqualTo(0.5f).Within(1f / 255f));

        pulse.Step(0.25f);
        Assert.That(line.GetPosition(0).magnitude, Is.EqualTo(6f).Within(0.001f));
        Assert.That(line.startColor.a, Is.Zero);
        Assert.That(line.endColor.a, Is.Zero);
        Assert.That(line.gameObject.activeSelf, Is.False);

        _hub.Publish(_howl);
        Assert.That(_parent.GetComponentsInChildren<LineRenderer>(true), Is.EqualTo(new[] { line }));
        Assert.That(line.gameObject.activeInHierarchy, Is.True);
        Assert.That(line.startColor.a, Is.EqualTo(1f));
    }

    [Test]
    public void Pulse_And_Glow_PlayerCyan()
    {
        float health = 0.4f;
        PetView pet = SpawnHound(_ => health);
        using var pulse = new HowlPulseView(_hub, PulsePrefab(), _parent.transform);
        var fill = (RectTransform)pet.transform.Find("Pet Health/Fill");
        Assert.That(fill.anchorMax.x, Is.EqualTo(0.4f));

        health = 0.6f;
        pet.Howl(4f, Time.time);
        _hub.Publish(_howl);
        typeof(PetView).GetMethod("LateUpdate", Private).Invoke(pet, null);

        var line = _parent.GetComponentInChildren<LineRenderer>();
        Assert.That(line.startColor, Is.EqualTo(Palette.Player));
        Assert.That(line.endColor, Is.EqualTo(Palette.Player));
        Material material = pet.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
        Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
        Color glow = material.GetColor("_EmissionColor");
        Assert.That(glow.b, Is.GreaterThan(0f).And.LessThan(Palette.Player.b));
        Assert.That(glow.r / glow.b, Is.EqualTo(Palette.Player.r / Palette.Player.b).Within(0.001f));
        Assert.That(glow.g / glow.b, Is.EqualTo(Palette.Player.g / Palette.Player.b).Within(0.001f));
        Assert.That(fill.anchorMax.x, Is.EqualTo(0.6f));
    }

    [Test]
    public void RunScope_MissingPulsePrefab_Throws()
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
            _parent.SetActive(false);
            var scope = _parent.AddComponent<RunScope>();
            EditorUtility.CopySerialized(source, scope);

            var data = new SerializedObject(scope);
            SerializedProperty field = data.FindProperty("_howlPulsePrefab");
            Assert.That(field.objectReferenceValue, Is.EqualTo(PulsePrefab()));
            field.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();

            var exception = Assert.Throws<TargetInvocationException>(() =>
                typeof(RunScope).GetMethod("Configure", Private).Invoke(scope, new object[] { new ContainerBuilder() }));
            Assert.That(exception.InnerException, Is.TypeOf<MissingReferenceException>());
            Assert.That(exception.InnerException.Message, Does.Contain("_howlPulsePrefab"));
        }
        finally
        {
            if (opened)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private PetView SpawnHound(Func<int, float?> readHealth = null)
    {
        PetView pet = Object.Instantiate(Hound(), _parent.transform);
        Animator animator = pet.GetComponentInChildren<Animator>();
        animator.Rebind();
        animator.Update(0f);
        pet.Bind(1, Vector3.zero, readHealth?.Invoke(1) ?? 1f, null, readHealth);
        return pet;
    }

    private static PetView Hound() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Pets/Hound.prefab").GetComponent<PetView>();

    private static LineRenderer PulsePrefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Vfx/HowlPulse.prefab").GetComponent<LineRenderer>();
}
