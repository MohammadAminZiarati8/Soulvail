using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;
using V3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class PathLookViewTests
{
    private const string Root = "Assets/_Project/";
    private const string BodyPath = Root + "Prefabs/Player/Bodies/Ranger.prefab";
    private GameObject _body;
    private PathLookView _view;
    private DomainEventHub _hub;
    private PathLook Look => Load<PathLook>("Data/Paths/MarksmanLook.asset");

    [SetUp]
    public void SetUp()
    {
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath));
        _view = _body.GetComponent<PathLookView>();
        _hub = new DomainEventHub();
        _view.Construct(_hub, null, null);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_body);
        _hub.Dispose();
    }

    [Test]
    public void Chosen_AppliesLook()
    {
        Material plain = Renderer("Ranger_Head").sharedMaterial;
        _hub.Publish(new PathChosen(new ContentId("path.ranger.marksman")));
        Assert.That(_view.Look == Look, Is.True);
        Assert.That(_view.IsShimmering, Is.True);
        Assert.That(Renderer("Ranger_Head").sharedMaterials.Length, Is.EqualTo(3));
        Assert.That(Renderer("Ranger_Head").sharedMaterials[0] == plain, Is.True, "Face keeps its atlas material.");
        Assert.That(Back().childCount, Is.EqualTo(1));
        _view.Step(0.39f);
        Assert.That(_view.IsShimmering, Is.True);
        _view.Step(0.02f);
        Assert.That(_view.IsShimmering, Is.False);
        var block = new MaterialPropertyBlock();
        Renderer("Ranger_Body").GetPropertyBlock(block);
        Assert.That(block.isEmpty, Is.True, "Shimmer leaves no tint behind.");
        Renderer("Bow").GetPropertyBlock(block, 1);
        Assert.That(block.GetColor("_EmissionColor").g, Is.GreaterThan(0.5f));
    }

    [Test]
    public void Resume_AppliesSilently()
    {
        using IObjectResolver container = Content();
        RunSession session = Session(container);
        _view.Construct(_hub, session, null);
        Start(session, new[] { new ContentId("skill.marksman.steady-aim") });
        Assert.That(session.State.PathId, Is.EqualTo(new ContentId("path.ranger.marksman")));
        Assert.That(_view.Look == Look, Is.True);
        Assert.That(_view.IsShimmering, Is.False);
        Assert.That(Back().childCount, Is.EqualTo(1));
        session.End();
    }

    [Test]
    public void NewRun_PlainRanger()
    {
        var head = (SkinnedMeshRenderer)Renderer("Ranger_Head");
        Mesh plainMesh = head.sharedMesh;
        Material[] plain = head.sharedMaterials;
        var block = new MaterialPropertyBlock();
        block.SetFloat("_Smoothness", 0.3f);
        head.SetPropertyBlock(block);
        _view.Apply(Look, true);
        _view.Apply(Look, false);
        Assert.That(Back().childCount, Is.EqualTo(1), "Reapplying never stacks props.");
        _hub.Publish(new RunStarted(new ContentId("character.ranger"), 1));
        Assert.That(_view.Look, Is.Null);
        Assert.That(head.sharedMesh == plainMesh, Is.True);
        Assert.That(head.sharedMaterials, Is.EqualTo(plain));
        head.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_Smoothness"), Is.EqualTo(0.3f));
        Assert.That(Back().childCount, Is.Zero);
        _view.Dispose();
        _hub.Publish(new PathChosen(new ContentId("path.ranger.marksman")));
        Assert.That(_view.Look, Is.Null, "Disposed body no longer hears path events.");
    }

    [Test]
    public void Marksman_LookHasQuiverAndTint()
    {
        Assert.That(Load<PathDefinition>("Data/Paths/Marksman.asset").Look == Look, Is.True);
        Assert.That(Look.Props.Count, Is.EqualTo(1));
        Assert.That(Look.Props[0].Socket, Is.EqualTo("back"));
        Assert.That(AssetDatabase.GetAssetPath(Look.Props[0].Prefab), Does.EndWith("Adventurers/Props/quiver.fbx"));
        Assert.That(Look.BowTint.b, Is.GreaterThan(Look.BowTint.r));
        Assert.That(Look.ArrowTrailTint.g, Is.GreaterThan(Look.ArrowTrailTint.r));
        foreach (PathLook.MaterialSwap swap in Look.Materials)
        {
            Assert.That(swap.Material, Is.Not.Null);
            Assert.That(swap.Mesh, Is.Not.Null);
            Assert.That(swap.Mesh.subMeshCount, Is.GreaterThan(swap.Slot));
            if (swap.Renderer == "Bow")
            {
                Assert.That(swap.Material.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(swap.Mesh.blendShapeCount, Is.EqualTo(((SkinnedMeshRenderer)Renderer("Bow")).sharedMesh.blendShapeCount));
            }
            if (swap.Renderer == "Ranger_Head" && swap.Slot == 1)
            {
                Assert.That(AssetDatabase.GetAssetPath(swap.Material.GetTexture("_BaseMap")),
                    Does.EndWith("T_Jungle_Albedo.png"), "Green is an existing atlas swatch.");
            }
        }
    }

    [Test]
    public void HeroPage_Unchanged()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
        Renderer[] originals = prefab.GetComponentsInChildren<Renderer>(true);
        var meshes = new Mesh[originals.Length];
        var materials = new Material[originals.Length][];
        for (int i = 0; i < originals.Length; i++)
        {
            materials[i] = originals[i].sharedMaterials;
            meshes[i] = (originals[i] as SkinnedMeshRenderer)?.sharedMesh;
        }
        _view.Apply(Look, true);
        for (int i = 0; i < originals.Length; i++)
        {
            Assert.That(originals[i].sharedMaterials, Is.EqualTo(materials[i]));
            Assert.That((originals[i] as SkinnedMeshRenderer)?.sharedMesh == meshes[i], Is.True);
        }
        GameObject preview = Object.Instantiate(prefab);
        try
        {
            Assert.That(preview.GetComponent<PathLookView>().Look, Is.Null);
            Assert.That(preview.GetComponent<PathLookView>().IsShimmering, Is.False);
            foreach (Transform child in preview.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(child.name, Is.Not.EqualTo("quiver(Clone)"));
            }
        }
        finally
        {
            Object.DestroyImmediate(preview);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingSocket_WarnsOnce_RestApplies(bool missingProp)
    {
        PathLook look = Object.Instantiate(Look);
        try
        {
            var data = new SerializedObject(look);
            SerializedProperty prop = data.FindProperty("_props").GetArrayElementAtIndex(0);
            if (missingProp)
            {
                prop.FindPropertyRelative("_prefab").objectReferenceValue = null;
            }
            else
            {
                prop.FindPropertyRelative("_socket").stringValue = "missing";
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            string socket = missingProp ? "back" : "missing";
            LogAssert.Expect(LogType.Warning, $"PathLook '{look.name}': missing prop or socket '{socket}'; skipped.");
            _view.Apply(look, false);
            _view.Apply(look, false);
            Assert.That(Renderer("Ranger_Head").sharedMaterials.Length, Is.EqualTo(3));
            Assert.That(Back().childCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            _view.Apply(null, false);
            Object.DestroyImmediate(look);
        }
    }

    [Test]
    public void ArrowTint_PlayerOnly_ResetsOnPoolReuse()
    {
        using IObjectResolver container = new ContainerBuilder().Build();
        var prefab = Load<ProjectileView>("Prefabs/Projectiles/Arrow.prefab");
        using var shots = new ProjectileViews(container, prefab, _body.transform, _hub, prewarm: 1);
        _view.Construct(_hub, null, shots);
        Fire(1, 0);
        Assert.That(shots.TryGet(1, out ProjectileView first), Is.True);
        var trail = first.GetComponentInChildren<TrailRenderer>();
        Color plain = trail.colorGradient.Evaluate(0f);
        _hub.Publish(new PathChosen(new ContentId("path.ranger.marksman")));
        Assert.That(trail.colorGradient.Evaluate(0f).r, Is.EqualTo(Look.ArrowTrailTint.r).Within(0.01f));
        _hub.Publish(new ProjectileImpacted(1, V3.Zero, false));
        Fire(2, 7);
        Assert.That(shots.TryGet(2, out ProjectileView enemy), Is.True);
        Assert.That(enemy == first, Is.True, "Enemy borrows the same pooled body.");
        Assert.That(trail.colorGradient.Evaluate(0f), Is.EqualTo(plain));
        _hub.Publish(new ProjectileImpacted(2, V3.Zero, false));
        Fire(3, 0, true);
        Assert.That(trail.colorGradient.Evaluate(0f), Is.EqualTo(plain), "Volley keeps its own tint.");
        _hub.Publish(new ProjectileImpacted(3, V3.Zero, false));
        _hub.Publish(new RunStarted(new ContentId("character.ranger"), 1));
        Fire(4, 0);
        Assert.That(trail.colorGradient.Evaluate(0f), Is.EqualTo(plain));
    }

    private void Fire(int id, int source, bool volley = false) => _hub.Publish(new ProjectileFired(
        id, new ContentId("character.ranger"), source, V3.Zero, V3.UnitX, 1f, volley));

    private Renderer Renderer(string name)
    {
        foreach (Renderer renderer in _body.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name == name)
            {
                return renderer;
            }
        }
        Assert.Fail(name);
        return null;
    }

    private Transform Back()
    {
        foreach (Transform child in _body.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "back")
            {
                return child;
            }
        }
        Assert.Fail("back socket");
        return null;
    }

    private RunSession Session(IObjectResolver container)
    {
        var random = new FixedRandom(1, Array.Empty<float>());
        return new RunSession(container.Resolve<ContentCatalog>(), random, _hub, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(DateTimeOffset.UtcNow), _hub), 64, 28, 8);
    }

    private void Start(RunSession session, IReadOnlyList<ContentId> taken)
    {
        var mode = new ContentId("mode.jungle");
        var ranger = new ContentId("character.ranger");
        var snapshot = new RunSnapshot(RunSnapshot.CurrentVersion, mode, ranger, 1, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, DateTimeOffset.UtcNow, 11, 0f,
            10 - taken.Count, taken, new ContentId[SkillRunner.MaxManualSlots], new RunEconomy(0, 0f, 0, 0),
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode, ranger, 1, 1, SpawnPlan.Empty, snapshot));
    }

    private static IObjectResolver Content()
    {
        var builder = new ContainerBuilder();
        BootInstaller.Install(builder, new[] { Load<CharacterDefinition>("Data/Characters/Ranger.asset") },
            All<EnemyDefinition>(), new[] { Load<ModeDefinition>("Data/Modes/Jungle.asset") }, All<SkillDefinition>(),
            new[] { Load<SkillTreeDefinition>("Data/Trees/Ranger.asset") }, Load<LocalizationTable>("Data/Localisation/English.asset"),
            bosses: All<BossDefinition>());
        return builder.Build();
    }

    private static T[] All<T>() where T : Object
    {
        string[] ids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { Root + "Data" });
        var assets = new T[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            assets[i] = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(ids[i]));
        }
        return assets;
    }

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
}
