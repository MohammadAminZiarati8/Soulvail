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
public sealed class JackalPriestModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string BodyPath = Art + "JackalPriest.fbx";
    private const string StaffPath = Art + "PriestStaff.fbx";
    private const string KayKit = "Assets/ThirdParty/KayKit/";
    private const string SourcePath = "Tools/Blender/Sources/JackalPriest_src.glb";
    private const string SourceSha256 = "d92e258071226f66b57401960ca19fd50f5d3dfb8b07a3e788ad2bf12feca577";
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
    public void PriestModel_IsShipped()
    {
        Assert.That(Model(BodyPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(BodyPath).GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        Assert.That(Model(StaffPath).GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(Model(StaffPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
    }

    [Test]
    public void PriestModel_StaysInBudget()
    {
        Mesh body = Body().sharedMesh;
        Mesh staff = Staff().sharedMesh;
        Assert.That(body.subMeshCount, Is.EqualTo(1));
        Assert.That(staff.subMeshCount, Is.EqualTo(1));
        Assert.That(body.GetIndexCount(0) / 3, Is.InRange(1500, 3000));
        Assert.That(staff.GetIndexCount(0) / 3, Is.InRange(1, 300));
    }

    [Test]
    public void PriestModel_UsesTheEnemyMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Enemies/M_Enemy.mat");
        Assert.That(material, Is.Not.Null);
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(Staff().GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(new[] { material }));
    }

    [Test]
    public void PriestModel_IsWeightedToRigMedium()
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
        int robeVertices = 0;
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
                robeVertices++;
            }
        }

        Assert.That(robeVertices, Is.GreaterThan(0), "The robe must follow both legs.");
    }

    [Test]
    public void PriestModel_ImportsGenericInPlace()
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
        AnimationClip mend = AssetDatabase.LoadAllAssetsAtPath(shared + "CombatRanged.fbx").OfType<AnimationClip>()
            .Single(clip => clip.name == "Ranged_Magic_Spellcasting");
        Assert.That(mend.isLooping, Is.True);
    }

    [Test]
    public void PriestModel_StandsJackalHigh()
    {
        var jackal = Model(Art + "Jackal.fbx").GetComponentInChildren<SkinnedMeshRenderer>(true);
        float height = WorldHeight(jackal);
        Assert.That(WorldHeight(Body()), Is.InRange(height * 0.9f, height * 1.1f));
    }

    [Test]
    public void PriestStaff_HasGlow()
    {
        GameObject model = Model(StaffPath);
        MeshFilter staff = Staff();
        Transform glow = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Glow");
        Assert.That(glow.parent, Is.EqualTo(staff.transform));
        Bounds bounds = staff.GetComponent<MeshRenderer>().bounds;
        Assert.That(glow.position.x, Is.EqualTo(bounds.center.x).Within(0.005f));
        Assert.That(glow.position.z, Is.EqualTo(bounds.center.z).Within(0.005f));
        Assert.That(bounds.max.y - glow.position.y, Is.EqualTo(0.175f).Within(0.005f));
        Assert.That(glow.position.y - bounds.min.y, Is.EqualTo(1.73f).Within(0.005f));
    }

    [Test]
    public void PriestModel_RobeLighterThanJackal()
    {
        Texture2D atlas = DecodeAtlas();
        Vector2[] robe = Body().sharedMesh.uv
            .Where(uv => uv.y == 510f / 512f && uv.x > 328f / 512f && uv.x < 376f / 512f).ToArray();
        Assert.That(robe, Is.Not.Empty);
        float jackalCloak = Luma(atlas.GetPixel(102, 511 - 256));
        var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Environment/M_SunscarSand.mat");
        float sandLuma = Luma(sand.GetColor("_BaseColor"));
        foreach (Vector2 uv in robe)
        {
            float lightness = Luma(atlas.GetPixelBilinear(uv.x, uv.y));
            Assert.That(lightness, Is.GreaterThan(Mathf.Max(jackalCloak, sandLuma)));
        }
    }

    [Test]
    public void PriestModel_NoCyanGoldOrDanger()
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
    public void PriestSource_HashPinned()
    {
        using var sha = SHA256.Create();
        using var source = File.OpenRead(SourcePath);
        string actual = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        Assert.That(actual, Is.EqualTo(SourceSha256));
        Assert.That(File.ReadAllText("Tools/Blender/jackal_priest.py"), Does.Contain(SourceSha256));
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
