using System.Collections.Generic;
using System.Reflection;
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
    private const string BurstPath = "Assets/_Project/Prefabs/Vfx/VFX_SprayBurst.prefab";
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

        Assert.That(cone.transform.position, Is.EqualTo(new Vector3(2f, SprayConeView.GroundOffset, 3f)));
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

    // JG-13a rule 3: Mother's 2.5 s wind-up, as SprayArmed carries it.
    [Test]
    public void Step_FillsOverATwoAndAHalfSecondWindup()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 40f, 12f, 2.5f);
        cone.Step(1f);
        Assert.That(cone.Fill, Is.EqualTo(0.4f).Within(1e-5f));
        cone.Step(1.5f);
        Assert.That(cone.Fill, Is.EqualTo(1f));
    }

    // Rule 4.
    [Test]
    public void Fire_FlashesThenHides()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        cone.Fire();
        Assert.That(cone.Fill, Is.EqualTo(1f));
        foreach (Color colour in Colours(cone))
        {
            Assert.That(colour.a, Is.EqualTo(0.95f).Within(0.001f));
            Assert.That(Palette.IsDanger(Srgb(colour)), Is.True);
        }

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

    // Rule 5. The player's missing look and an unmarked look stay quiet.
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
        // JG-13g: vertex colours need a particle shader, transparent for the fan, opaque for the goo.
        var renderer = prefab.GetComponent<MeshRenderer>();
        Material fan = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_SprayCone.mat");
        Assert.That(renderer.sharedMaterial == fan, Is.True);
        Assert.That(fan.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        Assert.That(fan.GetFloat("_Surface"), Is.EqualTo(1f));
        Assert.That(fan.renderQueue, Is.GreaterThanOrEqualTo(3000));
        Assert.That(fan.GetColor("_BaseColor"), Is.EqualTo(Color.white));
        var burstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath);
        Assert.That(burstPrefab, Is.Not.Null);
        var burst = burstPrefab.GetComponent<SprayBurstView>();
        Assert.That(burst, Is.Not.Null);
        Assert.That(burstPrefab.GetComponent<MeshFilter>(), Is.Not.Null);
        Assert.That(burstPrefab.GetComponent<MeshRenderer>().sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_Impact.mat"), Is.True);
        Object instance = PrefabUtility.InstantiatePrefab(prefab);
        _objects.Add((GameObject)instance);
        var live = ((GameObject)instance).GetComponent<SprayConeView>();
        live.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        Assert.That(Palette.IsDanger(Srgb(Colours(live)[0])), Is.True);
        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Run.unity", OpenSceneMode.Additive);
        try
        {
            bool bound = false;
            bool burstBound = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var scope = root.GetComponentInChildren(System.Type.GetType("Soulvail.Game.Composition.RunScope, Soulvail.Game"), true);
                if (scope != null)
                {
                    var serialized = new SerializedObject(scope);
                    bound = serialized.FindProperty("_sprayConePrefab").objectReferenceValue == cone;
                    burstBound = serialized.FindProperty("_sprayBurstPrefab").objectReferenceValue == burst;
                }
            }

            Assert.That(bound, Is.True);
            Assert.That(burstBound, Is.True);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // JG-13 playtest: every floor decal shows on the stone slabs, and the fan's rim reads.
    [Test]
    public void Decals_DrawAboveTheJungleStone_WithAThickRim()
    {
        float stoneTop = 0f;
        foreach (string arena in new[] { "Arena_Hollow", "Arena_Clearing" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Arenas/Jungle/{arena}.prefab");
            Assert.That(prefab, Is.Not.Null, arena);
            foreach (Renderer floor in prefab.GetComponentsInChildren<Renderer>(true))
            {
                // Flat floor pieces only; walls, trees and cover are meant to stand over a decal.
                if (floor.bounds.size.y < 0.3f)
                {
                    stoneTop = Mathf.Max(stoneTop, floor.bounds.max.y);
                }
            }
        }

        Assert.That(stoneTop, Is.GreaterThan(0f), "No raised floor found; the row would pass on nothing.");
        Assert.That(SprayConeView.GroundOffset, Is.GreaterThan(stoneTop + 0.01f), "The stone hides the spray fan.");
        var decals = new (string Path, System.Type View)[]
        {
            ("Prefabs/Vfx/VFX_TelegraphRing.prefab", typeof(TelegraphRingView)),
            ("Prefabs/Vfx/VFX_Fissure.prefab", typeof(FissureView)),
            ("Prefabs/Vfx/VFX_Shockwave.prefab", typeof(ShockwaveView)),
            ("Prefabs/Vfx/VFX_ConsecrateZone.prefab", typeof(ZoneView)),
            ("Prefabs/UI/Reticle.prefab", typeof(ReticleView)),
            ("Prefabs/Player/Player.prefab", typeof(FocusGlowView)),
        };
        foreach ((string path, System.Type view) in decals)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/{path}");
            Assert.That(prefab, Is.Not.Null, path);
            Component decal = prefab.GetComponentInChildren(view, true);
            Assert.That(decal, Is.Not.Null, $"{path} has no {view.Name}.");
            Assert.That(new SerializedObject(decal).FindProperty("_groundOffset").floatValue, Is.GreaterThan(stoneTop + 0.01f),
                $"The stone hides {view.Name}.");
        }

        object lane = typeof(BossViews).GetField("LaneGroundOffset", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        Assert.That((float)lane, Is.GreaterThan(stoneTop + 0.01f), "The stone hides the tongue lane.");

        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        Vector3[] vertices = cone.GetComponent<MeshFilter>().sharedMesh.vertices;
        Assert.That(vertices[0].magnitude - vertices[SprayConeView.Rays].magnitude, Is.EqualTo(SprayConeView.EdgeWidth).Within(0.001f));
        Assert.That(SprayConeView.EdgeWidth, Is.GreaterThanOrEqualTo(0.15f), "The fan's border is a hairline.");
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

    // JG-13g rule 7.
    [Test]
    public void Arm_DrawsAFaintAreaUnderABrightEdge()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        Mesh mesh = cone.GetComponent<MeshFilter>().sharedMesh;
        Color[] colours = mesh.colors;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < SprayConeView.Rays * 2; i++)
        {
            Assert.That(colours[i].a, Is.GreaterThanOrEqualTo(0.85f), "The rim is near-opaque.");
            Assert.That(Palette.IsDanger(Srgb(colours[i])), Is.True);
        }

        for (int t = triangles.Length - 12; t < triangles.Length; t++)
        {
            Assert.That(colours[triangles[t]].a, Is.GreaterThanOrEqualTo(0.85f), "The sides are near-opaque.");
        }

        // The first triangles are the area, drawn under everything, out to the rim's inner edge.
        Color area = colours[triangles[0]];
        Assert.That(area.a, Is.InRange(0.05f, 0.3f));
        Assert.That(Palette.IsDanger(Srgb(area)), Is.True);
        Assert.That(mesh.vertices[triangles[1]].magnitude, Is.EqualTo(8f - SprayConeView.EdgeWidth).Within(0.001f));
    }

    // JG-13g rule 8.
    [Test]
    public void Step_FillBrightensTowardItsFront()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        cone.Step(1f);
        Mesh mesh = cone.GetComponent<MeshFilter>().sharedMesh;
        int fill = (SprayConeView.Rays - 1) * 3;
        int[] triangles = mesh.triangles;
        Color tail = Srgb(mesh.colors[triangles[fill]]);
        Color front = Srgb(mesh.colors[triangles[fill + 1]]);
        Assert.That(mesh.vertices[triangles[fill]], Is.EqualTo(Vector3.zero));
        Assert.That(mesh.vertices[triangles[fill + 1]].magnitude, Is.EqualTo(4f).Within(0.001f));
        Assert.That(tail.a, Is.LessThanOrEqualTo(0.3f));
        Assert.That(front.a, Is.GreaterThan(tail.a + 0.5f));
        Assert.That(front.g, Is.GreaterThan(Palette.Danger.g), "The front is lifted toward white.");
        Assert.That(front.b, Is.GreaterThan(Palette.Danger.b));
        Assert.That(front.r, Is.GreaterThan(front.g), "Still the danger hue.");
    }

    // JG-13g rule 9.
    [Test]
    public void Step_EdgePulsesFasterAsItFills()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        Assert.That(cone.PulseRate, Is.EqualTo(SprayConeView.SlowPulseHz));
        float first = Colours(cone)[0].a;
        cone.Step(0.125f);
        Assert.That(Colours(cone)[0].a, Is.GreaterThan(first + 0.05f), "The edge beats on simulated time.");
        for (int i = 0; i < 60; i++)
        {
            cone.Step(0.03f);
            Assert.That(Colours(cone)[0].a, Is.InRange(0.85f, 1f));
        }

        cone.Step(float.NaN);
        cone.Step(2f);
        Assert.That(cone.PulseRate, Is.EqualTo(SprayConeView.FastPulseHz));
        Assert.That(SprayConeView.FastPulseHz, Is.GreaterThan(SprayConeView.SlowPulseHz * 2f));
    }

    // JG-13g rule 10: the goo stops where the fan stops; a stone takes it upright.
    [Test]
    public void Burst_LandsAtEachClippedEnd()
    {
        GameObject stone = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        stone.layer = LayerMask.NameToLayer("Cover");
        stone.transform.position = new Vector3(1f, 1.1f, 5f);
        stone.transform.localScale = new Vector3(2f, 0.4f, 1f);
        Physics.SyncTransforms();
        var origin = new Vector3(1f, 0f, 2f);
        SprayConeView cone = Cone();
        cone.Arm(origin, Vector2.up, 45f, 8f, 2f);
        Assert.That(cone.IsBlocked(15), Is.True);
        Assert.That(cone.IsBlocked(0), Is.False);
        SprayBurstView burst = Burst();
        burst.Play(cone);
        Assert.That(burst.IsLive, Is.True);
        foreach (int ray in new[] { 0, 15 })
        {
            float along = Along(burst.SplatAt(ray) - origin, cone.RayDirection(ray));
            Assert.That(along, Is.InRange(cone.RayLength(ray) - 0.2f, cone.RayLength(ray)));
        }

        Assert.That(burst.SplatAt(0).y, Is.InRange(SprayConeView.GroundOffset, 0.2f), "Flat on the floor, over the fan.");
        Assert.That(burst.SplatNormal(0), Is.EqualTo(Vector3.up));
        Assert.That(burst.SplatAt(15).y, Is.GreaterThan(0.3f), "Up on the stone.");
        Assert.That(Vector3.Dot(burst.SplatNormal(15), cone.RayDirection(15)), Is.LessThan(-0.5f), "It faces back toward the mouth.");
        Assert.That(burst.SplatNormal(15).y, Is.GreaterThan(0.5f), "Tilted up, so the camera sees it.");
        for (int step = 0; step < 60 && burst.IsLive; step++)
        {
            for (int ray = 0; ray < SprayConeView.Rays; ray++)
            {
                for (int drop = 0; drop < SprayBurstView.DropsPerRay; drop++)
                {
                    Vector3 at = burst.DropAt(ray, drop);
                    Assert.That(Along(at - origin, cone.RayDirection(ray)), Is.LessThanOrEqualTo(cone.RayLength(ray) + 0.001f),
                        $"Ray {ray} drop {drop} passed the clipped end.");
                    Assert.That(at.y, Is.GreaterThan(0f));
                }
            }

            burst.Step(0.02f);
        }
    }

    // JG-13g rule 11.
    [Test]
    public void Burst_SplashesThenGoes()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        SprayBurstView burst = Burst();
        burst.Play(cone);
        Assert.That(burst.Lifetime, Is.InRange(SprayBurstView.SplatSeconds, 1f));
        Assert.That(burst.SplatScale(0), Is.Zero, "Nothing has landed yet.");
        burst.Step(float.NaN);
        burst.Step(-1f);
        burst.Step(float.PositiveInfinity);
        Assert.That(burst.SplatScale(0), Is.Zero);
        float peak = 0f;
        float last = 0f;
        float elapsed = 0f;
        while (burst.IsLive && elapsed < 2f)
        {
            last = burst.SplatScale(0);
            peak = Mathf.Max(peak, last);
            burst.Step(0.01f);
            elapsed += 0.01f;
        }

        Assert.That(peak, Is.GreaterThan(0.9f));
        Assert.That(last, Is.LessThan(0.5f), "The splat folds before the burst ends.");
        Assert.That(elapsed, Is.LessThan(1f));
        Assert.That(burst.IsLive, Is.False);
        Assert.That(burst.SplatScale(0), Is.Zero);
        burst.Play(cone);
        burst.OnDespawn();
        Assert.That(burst.IsLive, Is.False);
        Assert.That(burst.Lifetime, Is.Zero);
        cone.Clear();
        burst.Play(cone);
        Assert.That(burst.IsLive, Is.False, "A dead fan has no rays to follow.");
    }

    // JG-13g rule 12.
    [Test]
    public void Burst_WearsMothersGoo()
    {
        SprayConeView cone = Cone();
        cone.Arm(Vector3.zero, Vector2.up, 30f, 8f, 2f);
        SprayBurstView burst = Burst();
        burst.Play(cone);
        Color[] colours = burst.GetComponent<MeshFilter>().sharedMesh.colors;
        Assert.That(colours, Is.Not.Empty);
        foreach (Color colour in colours)
        {
            Assert.That(colour == Palette.Vertex(Palette.MotherGoo) || colour == Palette.Vertex(Palette.MotherAcid), Is.True, colour.ToString());
        }

        foreach (Color reserved in new[] { Palette.Player, Palette.Danger, Palette.Veilrot, Palette.Essence, Palette.Neutral })
        {
            foreach (Color goo in new[] { Palette.MotherGoo, Palette.MotherAcid })
            {
                Assert.That(Vector3.Distance(new Vector3(goo.r, goo.g, goo.b), new Vector3(reserved.r, reserved.g, reserved.b)),
                    Is.GreaterThan(0.1f));
            }
        }

        var pod = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_SeedPod.mat");
        Color body = pod.GetColor("_BaseColor");
        Assert.That(Palette.MotherGoo.g, Is.EqualTo(body.g).Within(0.01f), "Her body green, as the pod wears it.");
    }

    // JG-13g rules 10–11, through the events.
    [Test]
    public void BossViews_SprayFired_PlaysABurst()
    {
        using IObjectResolver container = new ContainerBuilder().Build();
        using var hub = new DomainEventHub();
        using var enemies = new EnemyViews(container, Keep(new GameObject("Enemy")).AddComponent<EnemyView>(),
            null, hub, new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()), prewarm: 0);
        Transform parent = Keep(new GameObject("Decals")).transform;
        using var views = new BossViews(container, Decal<ShockwaveView>(), Decal<FissureView>(), Decal<BossBeatView>(),
            parent, hub, enemies, null, shockwavePrewarm: 0, fissurePrewarm: 0, beatPrewarm: 0,
            sprayConePrefab: Cone(), sprayBurstPrefab: Burst());
        hub.Publish(new SprayFired(7, true));
        Assert.That(parent.GetComponentInChildren<SprayBurstView>(), Is.Null, "No fan, no goo.");
        hub.Publish(new SprayArmed(7, new System.Numerics.Vector3(2f, 0f, 3f), new System.Numerics.Vector2(1f, 0f), 30f, 8f, 2f));
        SprayConeView cone = parent.GetComponentInChildren<SprayConeView>();
        views.Step(1f);
        Assert.That(parent.GetComponentInChildren<SprayBurstView>(), Is.Null);
        hub.Publish(new SprayFired(7, false));
        SprayBurstView burst = parent.GetComponentInChildren<SprayBurstView>();
        Assert.That(burst, Is.Not.Null);
        Assert.That(burst.IsLive, Is.True);
        Vector3 end = cone.transform.position + cone.RayEnd(0);
        Assert.That(burst.SplatAt(0).x, Is.EqualTo(end.x).Within(0.001f));
        Assert.That(burst.SplatAt(0).z, Is.EqualTo(end.z).Within(0.001f));
        views.Step(0.16f);
        Assert.That(cone.gameObject.activeSelf, Is.False);
        Assert.That(burst.gameObject.activeSelf, Is.True, "The goo outlives the flash.");
        views.Step(1f);
        Assert.That(burst.IsLive, Is.False);
        Assert.That(burst.gameObject.activeSelf, Is.False);
        hub.Publish(new SprayArmed(8, System.Numerics.Vector3.Zero, new System.Numerics.Vector2(0f, 1f), 20f, 4f, 1f));
        hub.Publish(new SprayFired(8, true));
        Assert.That(parent.GetComponentInChildren<SprayBurstView>(), Is.SameAs(burst), "The burst is pooled.");
        views.Dispose();
        Assert.That(burst == null, Is.True, "Disposal destroys the pool.");
    }

    private static float Along(Vector3 offset, Vector3 direction) => (offset.x * direction.x) + (offset.z * direction.z);
    // Vertex colours are written linear (Palette.Vertex); read them back as the palette's sRGB.
    private static Color Srgb(Color colour) => QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.gamma : colour;
    private static Color[] Colours(SprayConeView cone) => cone.GetComponent<MeshFilter>().sharedMesh.colors;
    private SprayBurstView Burst() => Keep(new GameObject("Burst")).AddComponent<SprayBurstView>();
    private SprayConeView Cone() => Keep(new GameObject("Cone")).AddComponent<SprayConeView>();
    private T Decal<T>() where T : Component => Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).AddComponent<T>();
    private GameObject Keep(GameObject body)
    {
        _objects.Add(body);
        return body;
    }
}
