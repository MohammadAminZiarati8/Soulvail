using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class JackalModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string BodyPath = Art + "Jackal.fbx";
    private const string SlingPath = Art + "JackalSling.fbx";
    private const string SourcePath = "Tools/Blender/Sources/Jackal_src.glb";
    private const string SourceSha256 = "76add7e3f7c149567ba04a2f82a89aabd914ebad904d8ec7fa1225ed961d047b";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private static readonly Color32[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF),
        new Color32(0xFF, 0x4A, 0x1F, 0xFF),
    };

    private Texture2D _atlas;

    [TearDown]
    public void DestroyAtlas()
    {
        if (_atlas != null)
        {
            Object.DestroyImmediate(_atlas);
        }
    }

    [Test]
    public void JackalModel_IsShipped()
    {
        Assert.That(Model(BodyPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(BodyPath).GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        Assert.That(Model(SlingPath).GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(SlingPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
    }

    [Test]
    public void JackalModel_StaysInBudget()
    {
        Mesh body = Body().sharedMesh;
        Mesh sling = SlingMesh();

        Assert.That(body.GetIndexCount(0) / 3, Is.InRange(1500, 3000));
        Assert.That(sling.GetIndexCount(0) / 3, Is.InRange(1, 300));
        Assert.That(body.subMeshCount, Is.EqualTo(1));
        Assert.That(sling.subMeshCount, Is.EqualTo(1));
    }

    [Test]
    public void JackalModel_UsesTheEnemyMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.That(material, Is.Not.Null);
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(Model(SlingPath).GetComponentInChildren<MeshRenderer>(true).sharedMaterials, Is.EqualTo(new[] { material }));
    }

    [Test]
    public void JackalModel_IsWeightedToRigMedium()
    {
        SkinnedMeshRenderer body = Body();
        Transform rig = Model(BodyPath).GetComponentsInChildren<Transform>(true).Single(t => t.name == "Rig_Medium");
        Transform source = Model("Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx")
            .GetComponentsInChildren<Transform>(true).Single(t => t.name == "Rig_Medium");
        string[] rigPaths = rig.Find("root").GetComponentsInChildren<Transform>(true)
            .Select(t => AnimationUtility.CalculateTransformPath(t, rig)).ToArray();
        string[] sourcePaths = source.Find("root").GetComponentsInChildren<Transform>(true)
            .Select(t => AnimationUtility.CalculateTransformPath(t, source)).ToArray();
        Assert.That(rigPaths, Has.Length.EqualTo(23));
        Assert.That(rigPaths, Is.EquivalentTo(sourcePaths));

        Transform[] bones = body.bones;
        Mesh mesh = body.sharedMesh;
        var counts = mesh.GetBonesPerVertex();
        var weights = mesh.GetAllBoneWeights();
        Assert.That(counts.Length, Is.EqualTo(mesh.vertexCount));

        int index = 0, kiltVertices = 0;
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            Assert.That(counts[vertex], Is.InRange(1, 4), $"vertex {vertex}");
            float total = 0f, left = 0f, right = 0f;
            for (int i = 0; i < counts[vertex]; i++, index++)
            {
                BoneWeight1 influence = weights[index];
                total += influence.weight;
                string name = bones[influence.boneIndex].name;
                Assert.That(name, Is.Not.EqualTo("root"), $"vertex {vertex}");
                if (name == "upperleg.l")
                {
                    left = influence.weight;
                }

                if (name == "upperleg.r")
                {
                    right = influence.weight;
                }
            }

            Assert.That(total, Is.EqualTo(1f).Within(1e-3f), $"vertex {vertex}");
            if (left > 0.1f && right > 0.1f)
            {
                kiltVertices++;
            }
        }

        Assert.That(kiltVertices, Is.GreaterThan(0), "The joined kilt must follow both thighs.");
    }

    [Test]
    public void JackalModel_ImportsGenericInPlace()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(BodyPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.motionNodeName, Is.Null.Or.Empty);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.importAnimation, Is.False);
        Assert.That(AssetDatabase.LoadAllAssetsAtPath(BodyPath).OfType<AnimationClip>(), Is.Empty);
    }

    [Test]
    public void JackalModel_StandsMossguardHigh()
    {
        float mossguard = WorldHeight(Model(Art + "Mossguard.fbx").GetComponentInChildren<SkinnedMeshRenderer>(true));
        Assert.That(WorldHeight(Body()), Is.InRange(mossguard * 0.9f, mossguard * 1.1f));
    }

    [Test]
    public void JackalSling_HasItsStone()
    {
        GameObject model = Model(SlingPath);
        Transform stone = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Stone");
        Assert.That(stone.parent, Is.EqualTo(model.GetComponentInChildren<MeshRenderer>(true).transform));
        float distance = Vector3.Distance(model.transform.position, stone.position);
        Assert.That(distance, Is.InRange(0.48f, 0.60f), "The sling origin is at the grip and Stone sits in the pouch.");
    }

    [Test]
    public void JackalModel_StandsOffTheDunes()
    {
        Texture2D atlas = DecodeAtlas();
        Mesh mesh = Body().sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        float area = 0f, luminance = 0f, saturation = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            float weight = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
            Vector2 centroid = (uv[a] + uv[b] + uv[c]) / 3f;
            Color colour = atlas.GetPixelBilinear(centroid.x, centroid.y);
            Color.RGBToHSV(colour, out _, out float s, out _);
            area += weight;
            luminance += weight * Luma(colour);
            saturation += weight * s;
        }

        Color sand = AssetDatabase.LoadAssetAtPath<Material>(SandPath).GetColor("_BaseColor");
        Color.RGBToHSV(sand, out _, out float sandSaturation, out _);
        Assert.That(luminance / area, Is.LessThanOrEqualTo(Luma(sand) - 0.05f));
        Assert.That(saturation / area, Is.GreaterThanOrEqualTo(sandSaturation + 0.15f));
    }

    [Test]
    public void JackalModel_NoCyanOrDanger()
    {
        Texture2D atlas = DecodeAtlas();
        foreach (Vector2 uv in Body().sharedMesh.uv.Concat(SlingMesh().uv))
        {
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            foreach (Color target in Reserved)
            {
                float distance = Mathf.Max(Mathf.Abs(colour.r - target.r),
                    Mathf.Abs(colour.g - target.g), Mathf.Abs(colour.b - target.b));
                Assert.That(distance, Is.GreaterThan(0.12f), $"UV {uv} samples {colour} near {target}.");
            }
        }
    }

    [Test]
    public void JackalSource_HashPinned()
    {
        using var sha = SHA256.Create();
        using var source = File.OpenRead(SourcePath);
        string actual = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        Assert.That(actual, Is.EqualTo(SourceSha256));
        Assert.That(File.ReadAllText("Tools/Blender/jackal.py"), Does.Contain(SourceSha256));
    }

    private static GameObject Model(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(model, Is.Not.Null, path);
        return model;
    }

    private static SkinnedMeshRenderer Body() => Model(BodyPath).GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static Mesh SlingMesh() => Model(SlingPath).GetComponentInChildren<MeshFilter>(true).sharedMesh;

    private static float Luma(Color colour) => (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);

    private static float WorldHeight(SkinnedMeshRenderer renderer)
    {
        Vector3 size = renderer.sharedMesh.bounds.size;
        Transform transform = renderer.transform;
        return Mathf.Abs(transform.TransformVector(Vector3.right * size.x).y)
            + Mathf.Abs(transform.TransformVector(Vector3.up * size.y).y)
            + Mathf.Abs(transform.TransformVector(Vector3.forward * size.z).y);
    }

    private Texture2D DecodeAtlas()
    {
        _atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        Assert.That(_atlas.LoadImage(File.ReadAllBytes(Art + "Textures/T_Enemy_Albedo.png")), Is.True);
        return _atlas;
    }
}
