using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-14b's generated assets, checked after Unity's importer and animation sampling.</summary>
[TestFixture]
public sealed class MossguardModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Mossguard.fbx";
    private const string ShieldPath = Art + "MossguardShield.fbx";
    private const string SpearPath = Art + "MossguardSpear.fbx";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string MinionPath = "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx";
    private const string ClipFolder = "Assets/ThirdParty/KayKit/Animations/Rig_Medium";
    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object obj in _created)
        {
            Object.DestroyImmediate(obj);
        }

        _created.Clear();
    }

    [Test]
    public void MossguardModel_IsShipped()
    {
        Assert.That(Model().GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Shield().GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Shield().GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.False);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>(), Is.Empty);
    }

    [Test]
    public void MossguardModel_SitsOnRigMedium()
    {
        Dictionary<string, Transform> expected = Rig(AssetDatabase.LoadAssetAtPath<GameObject>(MinionPath));
        Dictionary<string, Transform> actual = Rig(Model());
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));

        foreach (var pair in expected)
        {
            Transform bone = actual[pair.Key];
            Assert.That(Vector3.Distance(bone.localPosition, pair.Value.localPosition), Is.LessThan(0.001f), pair.Key);
            Assert.That(Quaternion.Angle(bone.localRotation, pair.Value.localRotation), Is.LessThan(0.1f), pair.Key);
            Assert.That(Vector3.Distance(bone.localScale, pair.Value.localScale), Is.LessThan(0.001f), pair.Key);
        }
    }

    [Test]
    public void MossguardModel_WristsMeetTheHands()
    {
        SkinnedMeshRenderer body = Body();

        foreach (string name in new[] { "hand.l", "hand.r" })
        {
            Vector3 hand = body.bones.Single(t => t.name == name).position;
            float nearest = body.sharedMesh.vertices.Min(v => Vector3.Distance(body.transform.TransformPoint(v), hand));
            Assert.That(nearest, Is.LessThanOrEqualTo(0.08f), name);
        }
    }

    [Test]
    public void MossguardModel_StaysInTheCrowdsBudget()
    {
        Mesh body = Body().sharedMesh;
        Mesh shield = Shield().GetComponentInChildren<MeshFilter>(true).sharedMesh;
        int bodyTriangles = body.triangles.Length / 3;
        int shieldTriangles = shield.triangles.Length / 3;
        Assert.That(bodyTriangles, Is.InRange(1200, 2200));
        Assert.That(shieldTriangles, Is.InRange(150, 400));
        int spearTriangles = Load(SpearPath).GetComponentInChildren<MeshFilter>(true).sharedMesh.triangles.Length / 3;
        Assert.That(bodyTriangles + shieldTriangles + spearTriangles, Is.LessThanOrEqualTo(2800));
        Assert.That(body.subMeshCount, Is.EqualTo(1));
        Assert.That(shield.subMeshCount, Is.EqualTo(1));
        Assert.That(body.normals, Has.Length.EqualTo(body.vertexCount));
        Assert.That(shield.normals, Has.Length.EqualTo(shield.vertexCount));
    }

    [Test]
    public void MossguardSpear_StaysInBudget()
    {
        GameObject spear = Load(SpearPath);
        Mesh mesh = spear.GetComponentInChildren<MeshFilter>(true).sharedMesh;
        Assert.That(mesh.triangles.Length / 3, Is.InRange(120, 300));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(spear.GetComponentInChildren<MeshRenderer>(true).sharedMaterials,
            Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) }));
        int total = mesh.triangles.Length / 3 + Body().sharedMesh.triangles.Length / 3
            + Shield().GetComponentInChildren<MeshFilter>(true).sharedMesh.triangles.Length / 3;
        Assert.That(total, Is.LessThanOrEqualTo(2800));
        // Atlas UVs stay in exactly the three existing spear swatches.
        Assert.That(mesh.uv.Select(uv => (Mathf.FloorToInt(uv.x * 8), Mathf.FloorToInt((1f - uv.y) * 4))).Distinct(),
            Is.EquivalentTo(new[] { (5, 0), (5, 1), (7, 3) }));
        Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount));
    }

    [Test]
    public void MossguardSpear_FitsTheHand()
    {
        GameObject spear = Load(SpearPath);
        MeshFilter filter = spear.GetComponentInChildren<MeshFilter>(true);
        Vector3[] vertices = filter.sharedMesh.vertices.Select(v => spear.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
        Vector3 min = vertices.Aggregate(Vector3.Min), max = vertices.Aggregate(Vector3.Max);
        Assert.That(max.y - min.y, Is.InRange(1.6f, 2f));
        Assert.That(SurfaceDistance(Vector3.zero, vertices, filter.sharedMesh.triangles), Is.LessThan(0.08f), "Pivot inside the middle grip wrap.");
        Assert.That(Mathf.Abs((max.y + min.y) * 0.5f), Is.LessThan(0.15f));
        GameObject sword = Load("Assets/ThirdParty/KayKit/Adventurers/Props/sword_1handed.fbx");
        Bounds blade = sword.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
        Assert.That(blade.center.y, Is.GreaterThan(0f));
        Assert.That(max.y, Is.GreaterThan(-min.y), "Head toward KayKit's +Y blade axis.");
        float headWidth = vertices.Where(v => v.y > max.y * 0.6f).Max(v => v.x) - vertices.Where(v => v.y > max.y * 0.6f).Min(v => v.x);
        float buttWidth = vertices.Where(v => v.y < min.y * 0.6f).Max(v => v.x) - vertices.Where(v => v.y < min.y * 0.6f).Min(v => v.x);
        Assert.That(headWidth, Is.GreaterThan(buttWidth * 1.3f), "The broad stone head, not the shaft butt, points along the blade.");
        Assert.That(spear.transform.localRotation, Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void MossguardModel_EveryVertexIsWeighted()
    {
        SkinnedMeshRenderer body = Body();
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v}");
            float sum = 0;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                BoneWeight1 weight = weights[at];
                Assert.That(weight.weight, Is.GreaterThan(0));
                Assert.That(float.IsNaN(weight.weight) || float.IsInfinity(weight.weight), Is.False);
                string name = body.bones[weight.boneIndex].name;
                Assert.That(name, Is.Not.EqualTo("root").And.Not.StartsWith("handslot"));
                sum += weight.weight;
            }

            Assert.That(sum, Is.EqualTo(1).Within(0.001f), $"vertex {v}");
        }
    }

    [Test]
    public void MossguardShield_FitsTheHand()
    {
        GameObject shield = Shield();
        MeshFilter filter = shield.GetComponentInChildren<MeshFilter>(true);
        Vector3[] vertices = filter.sharedMesh.vertices.Select(v => shield.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
        Vector3 min = vertices.Aggregate(Vector3.Min);
        Vector3 max = vertices.Aggregate(Vector3.Max);
        Assert.That(max.y - min.y, Is.InRange(0.9f, 1.2f));
        Assert.That(max.x - min.x, Is.InRange(0.5f, 0.7f));
        Assert.That(max.z - min.z, Is.LessThan(0.38f), "Backing sits close behind the stone.");
        int[] triangles = filter.sharedMesh.triangles;
        Assert.That(SurfaceDistance(Vector3.zero, vertices, triangles), Is.LessThan(0.065f), "Origin meets the retained grip.");
        Assert.That(SurfaceDistance(new Vector3(0, 0, 0.065f), vertices, triangles), Is.GreaterThan(0.025f),
            "The hand opening between grip and backing stays clear.");
        Assert.That(min.z, Is.LessThan(0), "Grip has thickness behind its pivot.");
        Assert.That(max.z, Is.GreaterThan(0.15f), "Stone face lies ahead of the grip along +Z.");
        Assert.That(vertices.Where(v => v.z > max.z - 0.08f).Average(v => v.z), Is.GreaterThan(0.15f));
        Assert.That(shield.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(Quaternion.Angle(shield.transform.localRotation, Quaternion.identity), Is.LessThan(0.1f));
        Assert.That(shield.transform.localScale, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void MossguardModel_WearsTheAtlas()
    {
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(atlas);
        Assert.That(atlas.LoadImage(File.ReadAllBytes(Art + "Textures/T_Enemy_Albedo.png")), Is.True);
        atlas.filterMode = FilterMode.Point;
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.That(material, Is.Not.Null);

        foreach (Renderer renderer in new Renderer[] { Body(), Shield().GetComponentInChildren<MeshRenderer>(true) })
        {
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            float area = 0, light = 0;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                float weight = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude;
                Vector2 center = (uv[a] + uv[b] + uv[c]) / 3;
                Color color = atlas.GetPixelBilinear(center.x, center.y);
                area += weight;
                light += weight * ((0.2126f * color.r) + (0.7152f * color.g) + (0.0722f * color.b));
            }

            Assert.That(light / area, Is.InRange(0.15f, 0.45f), renderer.name);
        }
    }

    [Test]
    public void MossguardModel_PlaysTheKayKitClips()
    {
        string[] names = { "Idle_B", "Walking_B", "Melee_Blocking", "Melee_Block_Attack", "Melee_Block_Hit", "Hit_A", "Death_A" };
        AnimationClip[] clips = AssetDatabase.FindAssets("t:Model", new[] { ClipFolder })
            .Select(AssetDatabase.GUIDToAssetPath).SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>()
            .Where(c => names.Contains(c.name)).ToArray();
        Assert.That(clips.Select(c => c.name), Is.EquivalentTo(names));

        foreach (AnimationClip clip in clips)
        {
            var model = Object.Instantiate(Model());
            model.hideFlags = HideFlags.HideAndDontSave;
            _created.Add(model);
            Transform root = model.transform.Find("Rig_Medium/root");
            Assert.That(root, Is.Not.Null);
            Vector3 position = root.localPosition;
            Quaternion rotation = root.localRotation;
            Vector3 scale = root.localScale;
            var shield = Object.Instantiate(Shield());
            _created.Add(shield);
            Transform slot = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "handslot.l");
            shield.transform.SetParent(slot, false);

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name}: {binding.path}");
            }

            foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                clip.SampleAnimation(model, clip.length * fraction);
                Assert.That(Vector3.Distance(root.localPosition, position), Is.LessThan(0.001f), clip.name);
                Assert.That(Quaternion.Angle(root.localRotation, rotation), Is.LessThan(0.1f), clip.name);
                Assert.That(Vector3.Distance(root.localScale, scale), Is.LessThan(0.001f), clip.name);
                var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                _created.Add(baked);
                model.GetComponentInChildren<SkinnedMeshRenderer>(true).BakeMesh(baked);
                Assert.That(baked.vertexCount, Is.EqualTo(Body().sharedMesh.vertexCount));
                Assert.That(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True, clip.name);
                Assert.That(baked.bounds.size.magnitude, Is.InRange(0.5f, 4f), clip.name);

                if (clip.name == "Melee_Blocking")
                {
                    Assert.That(Vector3.Dot(shield.transform.forward, model.transform.forward), Is.GreaterThan(0.95f));
                    Assert.That(Vector3.Dot(shield.transform.up, model.transform.up), Is.GreaterThan(0.95f));
                    Transform chest = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "chest");
                    Assert.That(Vector3.Dot(shield.transform.position - chest.position, model.transform.forward), Is.GreaterThan(0));
                }
            }
        }
    }

    private static GameObject Model() => Load(ModelPath);
    private static GameObject Shield() => Load(ShieldPath);
    private static SkinnedMeshRenderer Body() => Model().GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static GameObject Load(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(model, Is.Not.Null, path);
        return model;
    }

    private static Dictionary<string, Transform> Rig(GameObject model) => model.GetComponentsInChildren<Transform>(true)
        .Select(t => (Transform: t, Path: AnimationUtility.CalculateTransformPath(t, model.transform)))
        .Where(p => p.Path.StartsWith("Rig_Medium")).ToDictionary(p => p.Path, p => p.Transform);

    private static float SurfaceDistance(Vector3 point, Vector3[] vertices, int[] triangles)
    {
        float nearest = float.PositiveInfinity;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]] - point;
            Vector3 b = vertices[triangles[i + 1]] - point;
            Vector3 c = vertices[triangles[i + 2]] - point;
            nearest = Mathf.Min(nearest, SegmentDistance(a, b), SegmentDistance(b, c), SegmentDistance(c, a));
            Vector3 normal = Vector3.Cross(b - a, c - a);

            if (normal.sqrMagnitude < 1e-12f)
            {
                continue;
            }

            Vector3 projection = normal * (Vector3.Dot(normal, a) / normal.sqrMagnitude);

            if (Vector3.Dot(Vector3.Cross(b - a, projection - a), normal) >= 0
                && Vector3.Dot(Vector3.Cross(c - b, projection - b), normal) >= 0
                && Vector3.Dot(Vector3.Cross(a - c, projection - c), normal) >= 0)
            {
                nearest = Mathf.Min(nearest, projection.magnitude);
            }
        }

        return nearest;
    }

    private static float SegmentDistance(Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        return (a + (ab * Mathf.Clamp01(Vector3.Dot(-a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f)))).magnitude;
    }
}
