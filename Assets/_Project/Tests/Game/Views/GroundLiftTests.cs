using NUnit.Framework;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class GroundLiftTests
{
    [Test]
    public void GroundLift_ClearsJungleFloor()
    {
        const string folder = "Assets/_Project/Prefabs/Arenas/Jungle";
        int arenaCount = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (!prefab.TryGetComponent(out ArenaView arena))
            {
                continue;
            }

            arenaCount++;
            Transform ground = prefab.transform.Find("Ground");
            Assert.That(ground, Is.Not.Null, path);
            Assert.That(arena.Surface, Is.Not.Null, path);
            Assert.That(arena.Surface.navMeshData, Is.Not.Null, path);

            Transform surface = arena.Surface.transform;
            Bounds bounds = arena.Surface.navMeshData.sourceBounds;
            float baseHeight = prefab.transform.position.y;
            float highest = float.NegativeInfinity;

            foreach (MeshFilter filter in ground.GetComponentsInChildren<MeshFilter>())
            {
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(vertex);
                    Vector3 local = surface.InverseTransformPoint(world);

                    if (local.x >= bounds.min.x && local.x <= bounds.max.x
                        && local.z >= bounds.min.z && local.z <= bounds.max.z)
                    {
                        highest = Mathf.Max(highest, world.y - baseHeight);
                    }
                }
            }

            Assert.That(highest, Is.GreaterThan(float.NegativeInfinity), path);
            Assert.That(highest, Is.LessThan(GroundLift.Height), path);
        }

        Assert.That(arenaCount, Is.GreaterThan(0));
    }

    [Test]
    public void GroundLift_AtLeastMinimum()
    {
        Assert.That(GroundLift.Height, Is.GreaterThanOrEqualTo(0.06f));
    }
}
