using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-12g: the Tests table's nine rows, each tied to its behaviour rule.</summary>
[TestFixture]
public sealed class CoverViewsTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Arenas/Jungle/Cover_MotherStones.prefab";
    private readonly List<GameObject> _objects = new List<GameObject>();
    private DomainEventHub _hub;
    private CoverViews _views;
    private GameObject _parent;
    private ContentId _mother;
    private ContentId _other;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _parent = Keep(new GameObject("Cover test"));
        _mother = new ContentId("enemy.mother-pitcher");
        _other = new ContentId("enemy.other-cover");
        var looks = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>
        {
            [_mother] = new EnemyLook(Color.white, 1f, cover: Prefab()),
            [_other] = new EnemyLook(Color.white, 1f, cover: Prefab()),
            [new ContentId("enemy.frog")] = EnemyLook.Default,
        });
        _views = new CoverViews(looks, _hub, _parent.transform);
    }

    [TearDown]
    public void TearDown()
    {
        _views.Dispose();
        _hub.Dispose();
        foreach (GameObject obj in _objects)
        {
            Object.DestroyImmediate(obj);
        }

        _objects.Clear();
        Physics.SyncTransforms();
    }

    // Rule 1: two looks sharing one prefab still get one instance each, built before any event.
    [Test]
    public void Cover_BuiltInactive_UnderADeactivatedRoot()
    {
        Assert.That(_views.LiveCount, Is.Zero);
        Transform root = _parent.transform.GetChild(0);
        Assert.That(root.gameObject.activeSelf, Is.False);
        Assert.That(root.childCount, Is.EqualTo(2));
        foreach (Transform cover in root)
        {
            Assert.That(cover.gameObject.activeInHierarchy, Is.False);
            Assert.That(cover.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(4));
        }

        var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
        try
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_cover").objectReferenceValue = Prefab();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(definition.ToLook().Cover == Prefab(), Is.True);
            Assert.That(EnemyLook.Default.Cover, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    // Rule 2.
    [Test]
    public void Cover_SpawnOfItsArchetype_StandsThere()
    {
        Transform instance = _parent.transform.GetChild(0).GetChild(0);
        instance.rotation = Quaternion.Euler(0f, 70f, 0f);
        Vector3 position = new Vector3(3f, 0.5f, -2f);
        Spawn(7, _mother, position);
        Assert.That(_views.LiveCount, Is.EqualTo(1));
        Assert.That(instance.gameObject.activeInHierarchy, Is.True);
        Assert.That(instance.position, Is.EqualTo(position));
        Assert.That(Quaternion.Angle(instance.rotation, Quaternion.identity), Is.LessThan(0.001f));
        Assert.That(instance.parent, Is.EqualTo(_parent.transform));
        Assert.That(_parent.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(8), "Spawn reuses the prebuilt instance.");
    }

    // Rule 2.
    [Test]
    public void Cover_OtherSpawns_PlaceNothing()
    {
        Spawn(1, new ContentId("enemy.frog"), Vector3.one);
        Spawn(2, new ContentId("enemy.unknown"), Vector3.one);
        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_parent.GetComponentsInChildren<Collider>().Length, Is.Zero);
    }

    // Rule 3: unrelated retirements leave both covers standing; only the matching owner lifts.
    [Test]
    public void Cover_Despawn_LiftsIt()
    {
        Spawn(7, _mother, Vector3.zero);
        Spawn(8, _other, Vector3.right * 20f);
        _hub.Publish(new EnemyDespawned(99));
        Assert.That(_views.LiveCount, Is.EqualTo(2));
        _hub.Publish(new EnemyDespawned(7));
        Assert.That(_views.LiveCount, Is.EqualTo(1));
        Assert.That(_parent.GetComponentsInChildren<Collider>().Length, Is.EqualTo(4));
        _hub.Publish(new EnemyDespawned(8));
        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_parent.GetComponentsInChildren<Collider>().Length, Is.Zero);
        Spawn(9, _mother, Vector3.forward);
        Assert.That(_views.LiveCount, Is.EqualTo(1), "The lifted cover can stand again.");
    }

    // Rule 3, including reuse after the silent stage sweep.
    [Test]
    public void Cover_StageArrived_LiftsAny()
    {
        Spawn(7, _mother, Vector3.zero);
        Spawn(8, _other, Vector3.right * 20f);
        _hub.Publish(new StageArrived(11, default));
        Assert.That(_views.LiveCount, Is.Zero);
        Assert.That(_parent.GetComponentsInChildren<Collider>().Length, Is.Zero);
        Spawn(1, _mother, Vector3.back);
        Assert.That(_views.LiveCount, Is.EqualTo(1));
        _views.Dispose();
        _views.Dispose();
        Spawn(2, _other, Vector3.zero);
        Assert.That(_views.LiveCount, Is.Zero, "Disposed subscriptions cannot place cover.");
        Assert.That(_parent.transform.childCount, Is.Zero);
    }

    // JG-13d rule 3.
    [Test]
    public void CoverViews_VersionRisesOnPlaceLiftSweep()
    {
        Assert.That(_views.Version, Is.Zero);
        Spawn(7, _mother, Vector3.zero);
        Assert.That(_views.Version, Is.EqualTo(1));
        _hub.Publish(new EnemyDespawned(7));
        Assert.That(_views.Version, Is.EqualTo(2));
        _hub.Publish(new StageArrived(11, default));
        Assert.That(_views.Version, Is.EqualTo(3));
    }

    // Rule 4: actual physics bounds and a ray, rather than prefab-space Collider.bounds (Traps §5).
    [Test]
    public void MotherStones_AreFourSightBlockers()
    {
        GameObject cover = Stones();
        Assert.That(cover.transform.childCount, Is.EqualTo(4));
        var sense = new LineOfSightSense(4, LayerMask.GetMask("Cover"), new PathRefreshBudget(10f, 4));
        for (int i = 0; i < 4; i++)
        {
            Transform stone = cover.transform.GetChild(i);
            Assert.That(stone.localPosition.magnitude, Is.InRange(5.5f, 6.5f));
            float yaw = Mathf.Repeat(Mathf.Atan2(stone.localPosition.x, stone.localPosition.z) * Mathf.Rad2Deg, 360f);
            Assert.That(yaw, Is.EqualTo(45f + (90f * i)).Within(0.01f));
            var collider = stone.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(collider.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")));
            Assert.That(collider.bounds.size.y, Is.GreaterThanOrEqualTo(1.6f));
            Assert.That(Mathf.Min(collider.bounds.size.x, collider.bounds.size.z), Is.GreaterThanOrEqualTo(1.6f));
            var obstacle = stone.GetComponent<NavMeshObstacle>();
            Assert.That(obstacle.enabled && obstacle.carving, Is.True);
            Assert.That(obstacle.shape, Is.EqualTo(NavMeshObstacleShape.Box));
            Assert.That(obstacle.center, Is.EqualTo(((BoxCollider)collider).center));
            Assert.That(obstacle.size, Is.EqualTo(((BoxCollider)collider).size));
            Vector3 direction = stone.localPosition.normalized;
            float distance = stone.localPosition.magnitude + 3f;
            Assert.That(Physics.Raycast(Vector3.up * LineOfSightSense.EyeHeight, direction, out RaycastHit hit,
                distance, LayerMask.GetMask("Cover"), QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider, Is.EqualTo(collider));
            Assert.That(sense.HasLineOfSight(i + 1, Vector3.zero, direction * distance, 1f, 4, 1f), Is.False);
        }
    }

    // Rule 5: place the real prefab at Spawn_06 in the real room.
    [Test]
    public void MotherStones_InTheClearing_LeaveTheRoomPlayable()
    {
        GameObject arena = Keep(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Arenas/Jungle/Arena_Clearing.prefab")));
        var view = arena.GetComponent<ArenaView>();
        Transform middle = null;
        foreach (Transform child in arena.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Spawn_06")
            {
                middle = child;
            }
        }

        Assert.That(middle, Is.Not.Null);
        GameObject cover = Stones();
        cover.transform.position = middle.position;
        Physics.SyncTransforms();
        Collider[] stones = cover.GetComponentsInChildren<Collider>();
        foreach (Collider stone in stones)
        {
            Assert.That(XzDistance(stone.bounds, middle.position), Is.GreaterThanOrEqualTo(4f));
            Assert.That(XzDistance(stone.bounds, view.PlayerStart), Is.GreaterThanOrEqualTo(2f));
            foreach (Vector3 spawn in view.SpawnPoints)
            {
                Assert.That(XzDistance(stone.bounds, spawn), Is.GreaterThanOrEqualTo(2f));
            }

            Vector3 bottom = stone.bounds.center;
            bottom.y = stone.bounds.min.y;
            Assert.That(Physics.Raycast(bottom + (Vector3.up * 0.2f), Vector3.down, out RaycastHit floor,
                0.4f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore), Is.True, "A floor supports each stone.");
            Assert.That(bottom.y, Is.EqualTo(floor.point.y).Within(0.02f));
            foreach (Collider other in stones)
            {
                if (other != stone)
                {
                    Vector3 nearest = stone.bounds.ClosestPoint(other.bounds.center);
                    Assert.That(XzDistance(other.bounds, nearest), Is.GreaterThanOrEqualTo(4f));
                }
            }
        }
    }

    // Rule 4 / Traps §5: prove the saved model links, mesh and materials, including nested imports.
    [Test]
    public void Prefab_IsDressed()
    {
        GameObject prefab = Prefab();
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.activeSelf, Is.True);
        Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(4));
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer.gameObject);
            Assert.That(source, Does.StartWith("Assets/ThirdParty/KayKit/ForestNature/Rocks/"));
            Assert.That(renderer.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Cover")));
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader.name, Does.Not.Contain("Error"));
                Assert.That(AssetDatabase.GetAssetPath(material), Does.Contain("ForestNature"));
            }
        }
    }

    // Rule 6: average the albedo actually sampled by each mesh material, including its texture.
    [Test]
    public void MotherStones_WearNoReservedColour()
    {
        foreach (Renderer renderer in Prefab().GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
            {
                Material material = renderer.sharedMaterials[slot];
                Color tint = material.GetColor("_BaseColor");
                Texture texture = material.GetTexture("_BaseMap");
                RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0);
                RenderTexture previous = RenderTexture.active;
                var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    readable.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    readable.Apply();
                    Color average = Color.black;
                    int[] indices = mesh.GetTriangles(slot);
                    Vector2[] uv = mesh.uv;
                    foreach (int index in indices)
                    {
                        Vector2 sample = Vector2.Scale(uv[index], material.GetTextureScale("_BaseMap")) + material.GetTextureOffset("_BaseMap");
                        average += readable.GetPixelBilinear(sample.x, sample.y) * tint;
                    }

                    average /= indices.Length;
                    Color.RGBToHSV(average, out _, out float saturation, out float value);
                    Assert.That(value, Is.GreaterThan(0.1f), "Stone must remain visible.");
                    Assert.That(saturation, Is.LessThan(0.25f), "Every reserved colour is saturated; stone is neutral ash.");
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(readable);
                }
            }
        }
    }

    private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

    private GameObject Stones()
    {
        GameObject stones = Keep(Object.Instantiate(Prefab()));
        Physics.SyncTransforms();
        return stones;
    }

    private GameObject Keep(GameObject obj)
    {
        _objects.Add(obj);
        return obj;
    }

    private void Spawn(int id, ContentId spec, Vector3 position)
        => _hub.Publish(new EnemySpawned(id, spec, position.ToNum()));

    private static float XzDistance(Bounds bounds, Vector3 point)
    {
        point.y = bounds.center.y;
        return Vector3.Distance(point, bounds.ClosestPoint(point));
    }
}
