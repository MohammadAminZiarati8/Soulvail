using System.Collections.Generic;
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

/// <summary>JG-12f: each row pins its numbered behaviour rule.</summary>
[TestFixture]
public sealed class SprayConeViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Vfx/VFX_SprayCone.prefab";
    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject body in _objects)
        {
            if (body != null)
            {
                Object.DestroyImmediate(body);
            }
        }

        _objects.Clear();
        Physics.SyncTransforms();
    }

    // Rule 1.
    [Test]
    public void Arm_SpansTheWedge_FlatOnTheFloor()
    {
        SprayConeView cone = Cone();
        cone.Arm(new Vector3(2f, 7f, 3f), Vector2.up, 30f, 8f, 2f);
        Vector3[] vertices = cone.GetComponent<MeshFilter>().sharedMesh.vertices;
        Assert.That(SprayConeView.Rays, Is.EqualTo(32));
        for (int i = 0; i < 32; i++)
        {
            Assert.That(cone.RayLength(i), Is.EqualTo(8f).Within(0.001f));
            Assert.That(vertices[i].y, Is.Zero);
            float angle = Mathf.Atan2(vertices[i].x, vertices[i].z) * Mathf.Rad2Deg;
            Assert.That(angle, Is.EqualTo(-30f + (60f * i / 31f)).Within(0.001f));
        }

        Assert.That(cone.transform.position, Is.EqualTo(new Vector3(2f, 0.03f, 3f)));
        Assert.That(cone.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(vertices[32].magnitude, Is.LessThan(vertices[0].magnitude), "The rim has width before fill starts.");
        Assert.That(vertices[64], Is.EqualTo(Vector3.zero));
    }

    // Rule 2: eye-height cover clips; a low obstacle and a wrong layer do not.
    [Test]
    public void Arm_CoverCutsTheRaysBehindIt()
    {
        GameObject cover = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        cover.layer = LayerMask.NameToLayer("Cover");
        Assert.That(cover.layer, Is.GreaterThanOrEqualTo(0));
        cover.transform.position = new Vector3(0f, 1.1f, 3f);
        cover.transform.localScale = new Vector3(2f, 0.4f, 1f);
        GameObject low = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        low.layer = cover.layer;
        low.transform.position = new Vector3(0f, 0.2f, 1f);
        low.transform.localScale = new Vector3(10f, 0.4f, 0.2f);
        GameObject wrongLayer = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wrongLayer.transform.position = new Vector3(0f, 1.1f, 1f);
        wrongLayer.transform.localScale = new Vector3(10f, 0.4f, 0.2f);
        Physics.SyncTransforms();
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 45f, 8f, 2f);
        Assert.That(cone.RayLength(15), Is.InRange(2.5f, 2.6f));
        Assert.That(cone.RayLength(0), Is.EqualTo(8f).Within(0.001f));
        cover.transform.position = new Vector3(0f, 1.1f, 6f);
        Physics.SyncTransforms();
        cone.Step(1f);
        Assert.That(cone.RayLength(15), Is.InRange(2.5f, 2.6f), "Cover is asked only at Arm.");
    }

    // Rule 3.
    [Test]
    public void Step_FillsOverTheWindup()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        Assert.That(cone.Fill, Is.Zero);
        cone.Step(1f);
        Assert.That(cone.Fill, Is.EqualTo(0.5f));
        Assert.That(cone.GetComponent<MeshFilter>().sharedMesh.vertices[64].magnitude, Is.EqualTo(4f).Within(0.001f));
        cone.Step(2f);
        Assert.That(cone.Fill, Is.EqualTo(1f));
        Assert.That(cone.IsLive, Is.True, "Only Fire or Clear ends the promise.");
    }

    // Rule 4.
    [Test]
    public void Fire_FlashesThenHides()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        cone.Fire();
        Assert.That(cone.Fill, Is.EqualTo(1f));
        var properties = new MaterialPropertyBlock();
        cone.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(0.95f).Within(0.001f));
        cone.Step(0.14f);
        Assert.That(cone.IsLive, Is.True);
        cone.Step(0.02f);
        Assert.That(cone.IsLive, Is.False);
        Assert.That(cone.GetComponent<MeshRenderer>().enabled, Is.False);
    }

    // Rule 4.
    [Test]
    public void Clear_HidesAtOnce()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        cone.Fire();
        cone.Clear();
        Assert.That(cone.IsLive, Is.False);
        Assert.That(cone.Fill, Is.Zero);
        Assert.That(cone.GetComponent<MeshRenderer>().enabled, Is.False);
    }

    // Rule 4.
    [Test]
    public void Despawn_ThenArm_DrawsOnlyTheNew()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        cone.Fire();
        cone.OnDespawn();
        Assert.That(cone.RayLength(0), Is.Zero);
        cone.OnSpawn();
        cone.Arm(new Vector3(4f, 0f, 5f), Vector2.left, 15f, 3f, 1f);
        cone.Step(0.25f);
        Assert.That(cone.Fill, Is.EqualTo(0.25f));
        Assert.That(cone.IsLive, Is.True, "The old flash timer was cleared.");
        Vector3[] vertices = cone.GetComponent<MeshFilter>().sharedMesh.vertices;
        for (int i = 0; i < 32; i++)
        {
            Assert.That(cone.RayLength(i), Is.EqualTo(3f));
            Assert.That(vertices[i].x, Is.LessThan(0f));
        }
    }

    // Rule 5, including the radius published by the real projectile system.
    [Test]
    public void Rings_MarkedLob_GetsALandingRing() => CheckRings(true);

    // Rule 5. The player's missing look and the Pitcher's unmarked look stay quiet.
    [Test]
    public void Rings_UnmarkedLob_GetsNone() => CheckRings(false);

    // Rule 6.
    [Test]
    public void BossViews_SprayEvents_DriveACone()
    {
        using IObjectResolver container = new ContainerBuilder().Build();
        using var hub = new DomainEventHub();
        using var enemies = new EnemyViews(container, Keep(new GameObject("Enemy")).AddComponent<EnemyView>(),
            null, hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()), prewarm: 0);
        Transform parent = Keep(new GameObject("Decals")).transform;
        using var views = new BossViews(container, Decal<ShockwaveView>(), Decal<FissureView>(), Decal<BossBeatView>(),
            parent, hub, enemies, null, shockwavePrewarm: 0, fissurePrewarm: 0, beatPrewarm: 0, sprayConePrefab: Cone());
        hub.Publish(new SprayArmed(7, new System.Numerics.Vector3(2f, 0f, 3f), new System.Numerics.Vector2(1f, 0f), 30f, 8f, 2f));
        SprayConeView cone = parent.GetComponentInChildren<SprayConeView>();
        Assert.That(cone, Is.Not.Null);
        views.Step(1f);
        Assert.That(cone.Fill, Is.EqualTo(0.5f));
        hub.Publish(new SprayFired(99, false));
        Assert.That(cone.Fill, Is.EqualTo(0.5f), "Only the matching id fires.");
        hub.Publish(new SprayFired(7, false));
        Assert.That(cone.Fill, Is.EqualTo(1f), "A miss still flashes.");
        views.Step(0.16f);
        Assert.That(cone.gameObject.activeSelf, Is.False);
        hub.Publish(new SprayArmed(8, System.Numerics.Vector3.Zero, new System.Numerics.Vector2(0f, 1f), 20f, 4f, 1f));
        Assert.That(parent.GetComponentInChildren<SprayConeView>(), Is.SameAs(cone), "The cone is pooled.");
        hub.Publish(new SprayCleared(8));
        Assert.That(cone.IsLive, Is.False);
        Assert.That(cone.gameObject.activeSelf, Is.False);
        views.Dispose();
        hub.Publish(new SprayArmed(9, System.Numerics.Vector3.Zero, new System.Numerics.Vector2(0f, 1f), 20f, 4f, 1f));
        Assert.That(parent.GetComponentInChildren<SprayConeView>(), Is.Null);
    }

    // Rule 1, Traps §5: asset links and scene binding survive serialization.
    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        var cone = prefab.GetComponent<SprayConeView>();
        Assert.That(cone, Is.Not.Null);
        Assert.That(prefab.GetComponent<MeshFilter>(), Is.Not.Null);
        var renderer = prefab.GetComponent<MeshRenderer>();
        Assert.That(renderer.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_TelegraphRing.mat"), Is.True);
        Object instance = PrefabUtility.InstantiatePrefab(prefab);
        _objects.Add((GameObject)instance);
        var live = ((GameObject)instance).GetComponent<SprayConeView>();
        live.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        var properties = new MaterialPropertyBlock();
        live.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Color colour = properties.GetColor("_BaseColor");
        Assert.That(colour.r / 0.6f, Is.EqualTo(Palette.Danger.r).Within(0.001f));
        Assert.That(properties.GetTexture("_BaseMap"), Is.EqualTo(Texture2D.whiteTexture));
        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Run.unity", OpenSceneMode.Additive);
        try
        {
            bool bound = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var scope = root.GetComponentInChildren(System.Type.GetType("Soulvail.Game.Composition.RunScope, Soulvail.Game"), true);
                if (scope != null)
                {
                    bound = new SerializedObject(scope).FindProperty("_sprayConePrefab").objectReferenceValue == cone;
                }
            }

            Assert.That(bound, Is.True);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private void CheckRings(bool marked)
    {
        using IObjectResolver container = new ContainerBuilder().Build();
        using var hub = new DomainEventHub();
        var id = new ContentId("enemy.pitcher");
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook> { [id] = new EnemyLook(Color.white, 1f, marksLanding: marked) });
        Transform parent = Keep(new GameObject("Rings")).transform;
        var prefab = AssetDatabase.LoadAssetAtPath<TelegraphRingView>("Assets/_Project/Prefabs/Vfx/VFX_TelegraphRing.prefab");
        using var rings = new TelegraphRings(container, prefab, parent, hub, prewarm: 0, looks: looks);
        var shots = new ProjectileSystem(hub, 4);
        shots.Fire(new Projectile(id, 7, System.Numerics.Vector3.Zero, new System.Numerics.Vector3(4f, 0f, 3f), 2.5f, 1.7f, 10f), 0f);
        Assert.That(rings.Count, Is.EqualTo(marked ? 1 : 0));
        if (marked)
        {
            TelegraphRingView ring = parent.GetComponentInChildren<TelegraphRingView>();
            Assert.That(ring.Radius, Is.EqualTo(1.7f));
            Assert.That(ring.transform.position.x, Is.EqualTo(4f));
            Assert.That(ring.transform.position.z, Is.EqualTo(3f));
            Assert.That(ring.Fill, Is.Zero);
            rings.Step(1f);
            Assert.That(ring.Fill, Is.EqualTo(0.5f));
            rings.Step(1f);
            Assert.That(rings.Count, Is.Zero);
        }

        shots.Fire(new Projectile(new ContentId("class.ranger"), 0, System.Numerics.Vector3.Zero,
            new System.Numerics.Vector3(4f, 0f, 3f), 2.5f, 0.2f, 10f, ShotSide.AtEnemies), 0f);
        Assert.That(rings.Count, Is.Zero);
    }

    private SprayConeView Cone() => Keep(new GameObject("Cone")).AddComponent<SprayConeView>();
    private T Decal<T>() where T : Component => Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).AddComponent<T>();
    private GameObject Keep(GameObject body)
    {
        _objects.Add(body);
        return body;
    }
}
