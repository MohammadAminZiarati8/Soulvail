using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-13f: the five rows pin flight, swelling, cleanup, lifetime and colour.</summary>
[TestFixture]
public sealed class SeedPodViewTests
{
    private const string PodPath = "Assets/_Project/Prefabs/Vfx/VFX_SeedPod.prefab";
    private const string ShotPath = "Assets/_Project/Prefabs/Projectiles/PitcherSpit_Pod.prefab";
    private const string MaterialPath = "Assets/_Project/Materials/M_SeedPod.mat";
    private readonly List<GameObject> _objects = new List<GameObject>();
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _enemies;
    private BossViews _views;
    private Transform _parent;

    [SetUp]
    public void SetUp()
    {
        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
        _enemies = new EnemyViews(_container, Keep(new GameObject("Enemy")).AddComponent<EnemyView>(),
            null, _hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()), prewarm: 0);
        _parent = Keep(new GameObject("Pods")).transform;
    }

    [TearDown]
    public void TearDown()
    {
        _views?.Dispose();
        _views = null;
        _enemies.Dispose();
        _hub.Dispose();
        _container.Dispose();
        foreach (GameObject body in _objects)
        {
            if (body != null)
            {
                Object.DestroyImmediate(body);
            }
        }

        _objects.Clear();
    }

    // Rule 1: real look data drives both existing renderers, with no Frog attack enabled.
    [Test]
    public void Frog_FliesAPodAndMarksItsLanding()
    {
        var frog = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Frog.asset");
        var pitcher = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Pitcher.asset");
        EnemyLook look = frog.ToLook();
        var pod = AssetDatabase.LoadAssetAtPath<ProjectileView>(ShotPath);
        var spit = AssetDatabase.LoadAssetAtPath<ProjectileView>("Assets/_Project/Prefabs/Projectiles/PitcherSpit.prefab");
        Assert.That(look.Projectile, Is.EqualTo(pod));
        Assert.That(look.MarksLanding, Is.True);
        Assert.That(new SerializedObject(frog).FindProperty("_projectileStandoffRange").floatValue, Is.Zero);
        Assert.That(frog.ToSpec().Projectile, Is.Null);
        Assert.That(pitcher.ToLook().Projectile, Is.EqualTo(spit));
        Assert.That(pitcher.ToLook().MarksLanding, Is.True, "JG-13 playtest: the Pitcher marks its lob too.");
        Assert.That(pitcher.ToSpec().Projectile.StandoffRange, Is.EqualTo(14f));
        Assert.That(PrefabUtility.GetPrefabAssetType(pod), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(pod), Is.EqualTo(spit));
        Assert.That(pod.transform.Find("Blob").GetComponent<Renderer>().bounds.size.x, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(pod.Impact, Is.Null, "Hatching has no spit impact.");

        ContentId id = frog.ToSpec().Id;
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook> { [id] = look });
        using var projectiles = new ProjectileViews(_container, spit, _parent, _hub, enemyLooks: looks);
        Transform ringParent = Keep(new GameObject("Rings")).transform;
        using var rings = new TelegraphRings(_container,
            AssetDatabase.LoadAssetAtPath<TelegraphRingView>("Assets/_Project/Prefabs/Vfx/VFX_TelegraphRing.prefab"),
            ringParent, _hub, prewarm: 0, looks: looks);
        var shots = new ProjectileSystem(_hub, 4);
        int shotId = shots.Fire(new Projectile(id, 7, System.Numerics.Vector3.Zero,
            new System.Numerics.Vector3(4f, 0f, 3f), 2.5f, 1.6f, 10f), 0f);
        Assert.That(projectiles.TryGet(shotId, out ProjectileView flight), Is.True);
        Assert.That(flight.transform.Find("Blob").GetComponent<Renderer>().sharedMaterial,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath)));
        Assert.That(flight.Impact, Is.Null);
        Assert.That(rings.Count, Is.EqualTo(1));
        TelegraphRingView ring = ringParent.GetComponentInChildren<TelegraphRingView>();
        Assert.That(ring.Radius, Is.EqualTo(1.6f));
        Assert.That(ring.transform.position.x, Is.EqualTo(4f));
        Assert.That(ring.transform.position.z, Is.EqualTo(3f));
        Assert.That(ring.Fill, Is.Zero);
        projectiles.Step(1f);
        rings.Step(1f);
        Assert.That(ring.Fill, Is.EqualTo(0.5f));
        Assert.That(flight.transform.position.y, Is.GreaterThan(0f), "The pod arcs.");
        rings.Step(1f);
        Assert.That(rings.Count, Is.Zero);
        shots.Fire(new Projectile(new ContentId("class.ranger"), 0, System.Numerics.Vector3.Zero,
            new System.Numerics.Vector3(4f, 0f, 3f), 2.5f, 0.2f, 10f, ShotSide.AtEnemies), 0f);
        Assert.That(rings.Count, Is.Zero, "Player arrows do not gain a pod ring.");
    }

    // Rules 2 and 4: the snapshot step is the only animation clock.
    [Test]
    public void Pod_SwellsOverTheHatch()
    {
        CreateViews();
        _hub.Publish(new PodPlanted(1, 7, new System.Numerics.Vector3(2f, 0f, 3f), 1f));
        SeedPodView pod = _parent.GetComponentInChildren<SeedPodView>();
        Assert.That(pod, Is.Not.Null);
        Assert.That(pod.transform.position, Is.EqualTo(new Vector3(2f, 0f, 3f)));
        _views.Step(0f);
        Assert.That(pod.Swell, Is.Zero);
        Assert.That(pod.transform.localScale, Is.EqualTo(Vector3.one * SeedPodView.StartScale));
        _views.Step(0.125f);
        Assert.That(Quaternion.Angle(pod.transform.localRotation, Quaternion.identity), Is.GreaterThan(1f));
        _views.Step(0.375f);
        Assert.That(pod.Swell, Is.EqualTo(0.5f));
        Assert.That(pod.transform.localScale.x, Is.EqualTo(0.85f).Within(0.0001f));
        _views.Step(0.5f);
        Assert.That(pod.Swell, Is.EqualTo(1f));
        Assert.That(pod.transform.localScale, Is.EqualTo(Vector3.one));
        _views.Step(10f);
        Assert.That(pod.gameObject.activeSelf, Is.True, "Core's hatch event removes it.");
        Assert.That(pod.Swell, Is.EqualTo(1f));
        foreach (string message in new[] { "Update", "LateUpdate", "FixedUpdate" })
        {
            Assert.That(typeof(SeedPodView).GetMethod(message,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic), Is.Null);
        }

        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Run.unity", OpenSceneMode.Additive);
        try
        {
            bool bound = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var scope = root.GetComponentInChildren(Type.GetType("Soulvail.Game.Composition.RunScope, Soulvail.Game"), true);
                if (scope != null)
                {
                    bound = new SerializedObject(scope).FindProperty("_seedPodPrefab").objectReferenceValue
                        == AssetDatabase.LoadAssetAtPath<SeedPodView>(PodPath);
                }
            }

            Assert.That(bound, Is.True, "Run binds the planted pod.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // Rule 3: each event returns only the pods it owns, and a reused pod starts clean.
    [Test]
    public void Pod_GoesOnHatchDeathAndArrival()
    {
        CreateViews();
        SeedPodView first = Plant(1, 7);
        SeedPodView second = Plant(2, 7);
        SeedPodView third = Plant(3, 8);
        _views.Step(0.25f);
        _hub.Publish(new PodHatched(99));
        Assert.That(_parent.GetComponentsInChildren<SeedPodView>().Length, Is.EqualTo(3));
        _hub.Publish(new PodHatched(1));
        AssertReset(first);
        Assert.That(second.gameObject.activeSelf && third.gameObject.activeSelf, Is.True);
        _hub.Publish(new EnemyDied(7, new ContentId("enemy.mother-pitcher"), System.Numerics.Vector3.Zero));
        AssertReset(second);
        Assert.That(third.gameObject.activeSelf, Is.True);
        _hub.Publish(new StageArrived(11, default));
        AssertReset(third);
        Assert.That(_parent.GetComponentsInChildren<SeedPodView>().Length, Is.Zero);
        SeedPodView reused = Plant(4, 8);
        Assert.That(new[] { first, second, third }, Does.Contain(reused));
        Assert.That(reused.Swell, Is.Zero);
        Assert.That(_parent.GetComponentsInChildren<SeedPodView>(true).Length, Is.EqualTo(3), "No pool growth.");
        _hub.Publish(new EnemyDespawned(7));
        Assert.That(reused.gameObject.activeSelf, Is.True);
        _hub.Publish(new EnemyDespawned(8));
        AssertReset(reused);
        // Replacing an id cannot strand the old body.
        Plant(5, 8);
        Plant(5, 8);
        Assert.That(_parent.GetComponentsInChildren<SeedPodView>().Length, Is.EqualTo(1));
    }

    // Rule 4: active and pooled bodies die with the run, and no subscriber rents afterward.
    [Test]
    public void BossViews_DisposeUnsubscribesPods()
    {
        CreateViews();
        Plant(1, 7);
        SeedPodView[] bodies = _parent.GetComponentsInChildren<SeedPodView>(true);
        Assert.That(bodies.Length, Is.EqualTo(3));
        _views.Dispose();
        _views.Dispose();
        _hub.Publish(new PodPlanted(2, 7, System.Numerics.Vector3.Zero, 1f));
        _hub.Publish(new PodHatched(1));
        _hub.Publish(new EnemyDespawned(7));
        _hub.Publish(new EnemyDied(7, default, System.Numerics.Vector3.Zero));
        _hub.Publish(new StageArrived(11, default));
        Assert.That(_parent.GetComponentsInChildren<SeedPodView>(true), Is.Empty);
        foreach (SeedPodView body in bodies)
        {
            Assert.That(body == null, Is.True);
        }
    }

    // Rule 5: sample Mother's dominant body cell (croak_moss), not her chartreuse rim.
    [Test]
    public void SeedPod_WearsMothersGreen()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Enemies/MotherPitcher.fbx");
        var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var atlas = new Texture2D(2, 2);
        try
        {
            ImageConversion.LoadImage(atlas, File.ReadAllBytes(AssetDatabase.GetAssetPath(body.sharedMaterial.mainTexture)));
            Color swatch = atlas.GetPixelBilinear(7.5f / 8f, 0.5f / 4f);
            Color green = material.GetColor("_BaseColor");
            Assert.That(green.r, Is.EqualTo(swatch.r).Within(0.01f));
            Assert.That(green.g, Is.EqualTo(swatch.g).Within(0.01f));
            Assert.That(green.b, Is.EqualTo(swatch.b).Within(0.01f));
            Assert.That(green.g, Is.GreaterThan(green.r));
            Assert.That(green.r, Is.GreaterThan(green.b));
            foreach (Color reserved in new[] { Palette.Player, Palette.Danger, Palette.Veilrot, Palette.Essence, Palette.Neutral })
            {
                Assert.That(Vector3.Distance(new Vector3(green.r, green.g, green.b),
                    new Vector3(reserved.r, reserved.g, reserved.b)), Is.GreaterThan(0.1f));
            }

            foreach (string path in new[] { PodPath, ShotPath })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled)
                    {
                        Assert.That(renderer.sharedMaterial, Is.EqualTo(material), path);
                    }
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(atlas);
        }
    }

    private void CreateViews() => _views = new BossViews(_container, Decal<ShockwaveView>(), Decal<FissureView>(),
        Decal<BossBeatView>(), _parent, _hub, _enemies, null, shockwavePrewarm: 0, fissurePrewarm: 0,
        beatPrewarm: 0, seedPodPrefab: AssetDatabase.LoadAssetAtPath<SeedPodView>(PodPath));

    private SeedPodView Plant(int id, int boss)
    {
        _hub.Publish(new PodPlanted(id, boss, new System.Numerics.Vector3(id, 0f, 0f), 1f));
        foreach (SeedPodView pod in _parent.GetComponentsInChildren<SeedPodView>())
        {
            if (pod.transform.position.x == id)
            {
                return pod;
            }
        }

        Assert.Fail("No planted pod for " + id);
        return null;
    }

    private static void AssertReset(SeedPodView pod)
    {
        Assert.That(pod.gameObject.activeSelf, Is.False);
        Assert.That(pod.Swell, Is.Zero);
        Assert.That(pod.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(pod.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(pod.transform.localScale, Is.EqualTo(Vector3.one * SeedPodView.StartScale));
    }

    private T Decal<T>() where T : Component => Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).AddComponent<T>();

    private GameObject Keep(GameObject body)
    {
        _objects.Add(body);
        return body;
    }
}
