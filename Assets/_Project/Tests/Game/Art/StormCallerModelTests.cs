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
public sealed class StormCallerModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string BodyPath = Art + "StormCaller.fbx";
    private const string StaffPath = Art + "CallerStaff.fbx";
    private const string KayKit = "Assets/ThirdParty/KayKit/";
    private const string SourcePath = "Tools/Blender/Sources/StormCaller_src.glb";
    private const string SourceSha256 = "953ce4b27aceb8984cfcc34dd2aee4aa0cf90605030c23b6e32c9e90731c1c9a";
    private static readonly Color32[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF),
        new Color32(0xFF, 0x4A, 0x1F, 0xFF),
        new Color32(0xA8, 0x55, 0xF7, 0xFF),
        new Color32(0xFB, 0xBF, 0x24, 0xFF),
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
    public void CallerModel_IsShipped()
    {
        Assert.That(Model(BodyPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(BodyPath).GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        Assert.That(Model(StaffPath).GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(StaffPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
    }

    [Test]
    public void CallerModel_StaysInBudget()
    {
        Mesh body = Body().sharedMesh;
        Mesh staff = Staff().sharedMesh;
        Assert.That(body.subMeshCount, Is.EqualTo(1));
        Assert.That(staff.subMeshCount, Is.EqualTo(1));
        Assert.That(body.GetIndexCount(0) / 3, Is.InRange(1500, 3000));
        Assert.That(staff.GetIndexCount(0) / 3, Is.InRange(1, 300));
    }

    [Test]
    public void CallerModel_UsesTheEnemyMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_Enemy.mat");
        Assert.That(material, Is.Not.Null);
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(Staff().GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(new[] { material }));
    }

    [Test]
    public void CallerModel_IsWeightedToRigMedium()
    {
        SkinnedMeshRenderer body = Body();
        Transform rig = Rig(BodyPath);
        Transform source = Rig(KayKit + "Skeletons/Characters/Skeleton_Minion.fbx");
        string[] paths = BonePaths(rig);
        Assert.That(paths, Has.Length.EqualTo(23));
        Assert.That(paths, Is.EquivalentTo(BonePaths(source)));
        foreach (string path in paths)
        {
            Transform bone = rig.Find(path);
            Transform original = source.Find(path);
            Vector3 rest = rig.InverseTransformPoint(bone.position);
            Vector3 expected = source.InverseTransformPoint(original.position);
            Assert.That(Vector3.Distance(rest, expected), Is.LessThan(0.001f), path);
        }

        var counts = body.sharedMesh.GetBonesPerVertex();
        var weights = body.sharedMesh.GetAllBoneWeights();
        Assert.That(counts.Length, Is.EqualTo(body.sharedMesh.vertexCount));
        int index = 0;
        int cloakVertices = 0;
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            Assert.That(counts[vertex], Is.InRange(1, 4), $"vertex {vertex}");
            float total = 0f;
            float leftLeg = 0f;
            float rightLeg = 0f;
            for (int influence = 0; influence < counts[vertex]; influence++, index++)
            {
                BoneWeight1 weight = weights[index];
                total += weight.weight;
                string name = body.bones[weight.boneIndex].name;
                Assert.That(name, Is.Not.EqualTo("root"), $"vertex {vertex}");
                if (name == "upperleg.l" || name == "lowerleg.l")
                {
                    leftLeg += weight.weight;
                }
                else if (name == "upperleg.r" || name == "lowerleg.r")
                {
                    rightLeg += weight.weight;
                }
            }

            Assert.That(total, Is.EqualTo(1f).Within(1e-3f), $"vertex {vertex}");
            if (leftLeg > 0.1f && rightLeg > 0.1f)
            {
                cloakVertices++;
            }
        }

        Assert.That(cloakVertices, Is.GreaterThan(0), "The cloak must follow both legs.");
    }

    [Test]
    public void CallerModel_ImportsGenericInPlace()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(BodyPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.motionNodeName, Is.Null.Or.Empty);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.importAnimation, Is.False);
        Assert.That(AssetDatabase.LoadAllAssetsAtPath(BodyPath).OfType<AnimationClip>(), Is.Empty);

        const string shared = KayKit + "Animations/Rig_Medium/Rig_Medium_";
        Assert.That(ClipNames(shared + "General.fbx"), Does.Contain("Idle_B").And.Contain("Hit_A").And.Contain("Death_A"));
        Assert.That(ClipNames(shared + "MovementBasic.fbx"), Does.Contain("Walking_B").And.Contain("Running_A"));
        AnimationClip channel = AssetDatabase.LoadAllAssetsAtPath(shared + "CombatRanged.fbx").OfType<AnimationClip>()
            .Single(clip => clip.name == "Ranged_Magic_Spellcasting");
        Assert.That(channel.isLooping, Is.True);
    }

    [Test]
    public void CallerModel_StandsJackalHigh()
    {
        var jackal = Model(Art + "Jackal.fbx").GetComponentInChildren<SkinnedMeshRenderer>(true);
        float height = WorldHeight(jackal);
        Assert.That(WorldHeight(Body()), Is.InRange(height * 0.9f, height * 1.1f));
    }

    [Test]
    public void CallerStaff_HasGlowInTheRing()
    {
        GameObject model = Model(StaffPath);
        MeshFilter staff = Staff();
        Transform glow = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Glow");
        Assert.That(glow.parent, Is.EqualTo(staff.transform));
        Vector3[] vertices = staff.sharedMesh.vertices.Select(staff.transform.TransformPoint).ToArray();
        Vector3[] ring = vertices.Where(point => point.y > glow.position.y - 0.18f).ToArray();
        Assert.That(ring, Is.Not.Empty);
        var bounds = new Bounds(ring[0], Vector3.zero);
        foreach (Vector3 point in ring)
        {
            bounds.Encapsulate(point);
        }

        Assert.That(bounds.size.x, Is.InRange(0.35f, 0.45f));
        Assert.That(Vector3.Distance(glow.position, bounds.center), Is.LessThan(0.05f));
        Assert.That(glow.position.y, Is.EqualTo(1.19f).Within(0.005f));
        Assert.That(vertices.Min(point => point.y), Is.EqualTo(-0.60f).Within(0.005f));

        // Project along the head's thin axis. A disc or spoke would cover Glow.
        int[] triangles = staff.sharedMesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 a = vertices[triangles[i]] - glow.position;
            Vector2 b = vertices[triangles[i + 1]] - glow.position;
            Vector2 c = vertices[triangles[i + 2]] - glow.position;
            Assert.That(CoversOrigin(a, b, c), Is.False, $"triangle {i / 3}");
        }
    }

    [Test]
    public void CallerModel_DarkerThanJackalAndPriest()
    {
        Texture2D atlas = DecodeAtlas();
        Vector2[] cloak = Body().sharedMesh.uv
            .Where(uv => uv.y == 510f / 512f && uv.x > 320f / 512f && uv.x < 328f / 512f).ToArray();
        Assert.That(cloak, Is.Not.Empty);
        float jackalCloak = Luma(atlas.GetPixel(102, 511 - 256));
        float priestRobe = Luma(atlas.GetPixel(352, 510));
        Assert.That(jackalCloak, Is.LessThan(priestRobe));
        foreach (Vector2 uv in cloak)
        {
            float lightness = Luma(atlas.GetPixelBilinear(uv.x, uv.y));
            Assert.That(lightness, Is.LessThan(jackalCloak));
        }
    }

    [Test]
    public void CallerModel_NoCyanGoldOrDanger()
    {
        Texture2D atlas = DecodeAtlas();
        foreach (Vector2 uv in Body().sharedMesh.uv.Concat(Staff().sharedMesh.uv))
        {
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            foreach (Color target in Reserved)
            {
                float distance = Mathf.Max(Mathf.Abs(colour.r - target.r),
                    Mathf.Abs(colour.g - target.g), Mathf.Abs(colour.b - target.b));
                Assert.That(distance, Is.GreaterThan(0.12f), $"UV {uv} samples {colour} near {target}.");
            }

            Color.RGBToHSV(colour, out float hue, out float saturation, out float value);
            bool gold = hue >= 0.09f && hue <= 0.17f && saturation > 0.55f && value > 0.72f;
            Assert.That(gold, Is.False, $"UV {uv} samples gold at {colour}.");
        }
    }

    [Test]
    public void CallerSource_HashPinned()
    {
        using var sha = SHA256.Create();
        using var source = File.OpenRead(SourcePath);
        string actual = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        Assert.That(actual, Is.EqualTo(SourceSha256));
        Assert.That(File.ReadAllText("Tools/Blender/storm_caller.py"), Does.Contain(SourceSha256));
    }

    private static GameObject Model(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(model, Is.Not.Null, path);
        return model;
    }

    private static SkinnedMeshRenderer Body() => Model(BodyPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
    private static MeshFilter Staff() => Model(StaffPath).GetComponentInChildren<MeshFilter>(true);
    private static Transform Rig(string path) => Model(path).GetComponentsInChildren<Transform>(true)
        .Single(transform => transform.name == "Rig_Medium");

    private static string[] BonePaths(Transform rig) => rig.Find("root").GetComponentsInChildren<Transform>(true)
        .Select(transform => AnimationUtility.CalculateTransformPath(transform, rig)).ToArray();

    private static string[] ClipNames(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
        .Select(clip => clip.name).ToArray();

    private static float Luma(Color colour) => (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);

    private static float Cross(Vector2 a, Vector2 b) => (a.x * b.y) - (a.y * b.x);

    private static bool CoversOrigin(Vector2 a, Vector2 b, Vector2 c)
    {
        if (Mathf.Abs(Cross(b - a, c - a)) < 1e-8f)
        {
            return false;
        }

        float ab = Cross(a, b);
        float bc = Cross(b, c);
        float ca = Cross(c, a);
        return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
    }

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
