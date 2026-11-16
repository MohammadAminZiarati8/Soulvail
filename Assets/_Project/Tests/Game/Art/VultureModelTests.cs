using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

[TestFixture]
public sealed class VultureModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Vulture.fbx";
    private const string HawkPath = "Assets/_Project/Art/Pets/Hawk.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string SourcePath = "Tools/Blender/Sources/Vulture_src.glb";
    private const string SourceHash = "4ee6d652aeb4a6ea456ca371f3e5a92ff8c90f3226fc57dd95656c525f85ab8f";

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        { "Glide", 48 }, { "Flap", 12 }, { "Dive", 20 }, { "Strike", 15 }, { "Climb", 22 },
        { "Land", 15 }, { "Idle_Ground", 48 }, { "Hit", 12 }, { "Death", 30 },
    };

    private static readonly string[] Loops = { "Glide", "Flap", "Idle_Ground" };
    private static readonly Color32[] ReservedColours =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF),
        new Color32(0xFF, 0x4A, 0x1F, 0xFF),
        new Color32(0xFB, 0xBF, 0x24, 0xFF),
    };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object created in _created)
        {
            Object.DestroyImmediate(created);
        }

        _created.Clear();
    }

    [Test]
    public void VultureModel_IsShippedInBudget()
    {
        SkinnedMeshRenderer[] bodies = Load<GameObject>(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(bodies, Has.Length.EqualTo(1));
        Assert.That(bodies[0].sharedMaterials, Is.EqualTo(new[] { Load<Material>(MaterialPath) }));
        Mesh mesh = bodies[0].sharedMesh;
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(mesh.triangles.Length / 3, Is.InRange(1500, 3000));
        Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount));
    }

    [Test]
    public void VultureModel_IsWeighted()
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        int at = 0;
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            Assert.That(counts[vertex], Is.InRange(1, 4), $"vertex {vertex}");
            float sum = 0f;
            for (int index = 0; index < counts[vertex]; index++, at++)
            {
                BoneWeight1 weight = weights[at];
                Assert.That(float.IsFinite(weight.weight) && weight.weight > 0f, Is.True);
                Assert.That(weight.boneIndex, Is.InRange(0, body.bones.Length - 1));
                Assert.That(body.bones[weight.boneIndex].name, Is.Not.EqualTo("root"));
                sum += weight.weight;
            }

            Assert.That(sum, Is.EqualTo(1f).Within(0.001f), $"vertex {vertex}");
        }
    }

    [Test]
    public void VultureModel_HasRigHawk()
    {
        Transform rig = Bone(Load<GameObject>(ModelPath), "Rig_Hawk");
        Assert.That(rig.Find("root/body"), Is.Not.Null);
        Assert.That(rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig).Select(t => t.name),
            Is.EquivalentTo(new[] { "root", "body", "neck", "head", "tail", "wing.l.1", "wing.l.2", "wing.r.1", "wing.r.2", "leg.l", "leg.r" }));
    }

    [Test]
    public void VultureModel_ImportsItsClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Dictionary<string, AnimationClip> clips = Clips();
        Assert.That(importer.clipAnimations.Select(clip => clip.name), Is.EquivalentTo(ClipFrames.Keys));
        Assert.That(clips.Keys, Is.EquivalentTo(ClipFrames.Keys));
        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.length * clip.frameRate, Is.EqualTo(ClipFrames[clip.name]).Within(0.01f), clip.name);
            Assert.That(clip.isLooping, Is.EqualTo(Loops.Contains(clip.name)), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
        }
    }

    [Test]
    public void VultureModel_ClipsLeaveRootAlone()
    {
        GameObject model = Instance(ModelPath);
        Transform root = Bone(model, "root");
        Vector3 rest = root.localPosition;
        Quaternion restRotation = root.localRotation;
        Vector3 restScale = root.localScale;
        foreach (AnimationClip clip in Clips().Values)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings, Is.Not.Empty, clip.name);
            foreach (EditorCurveBinding binding in bindings)
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name}: {binding.path}");
            }

            for (int frame = 0; frame <= ClipFrames[clip.name]; frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                Assert.That(Vector3.Distance(root.localPosition, rest), Is.LessThan(0.0001f), $"{clip.name} frame {frame}");
                Assert.That(Quaternion.Angle(root.localRotation, restRotation), Is.LessThan(0.05f), $"{clip.name} frame {frame}");
                Assert.That(Vector3.Distance(root.localScale, restScale), Is.LessThan(0.0001f), $"{clip.name} frame {frame}");
            }
        }
    }

    [Test]
    public void VultureModel_OutspansTheHawk()
    {
        float vulture = Bake(Instance(ModelPath)).size.x;
        float hawk = Bake(Instance(HawkPath)).size.x;
        Assert.That(vulture, Is.InRange(2.4f, 2.8f));
        Assert.That(vulture, Is.GreaterThanOrEqualTo(1.6f * hawk));
    }

    [Test]
    public void VultureModel_GroundPoseFoldsWings()
    {
        GameObject model = Instance(ModelPath);
        float rest = Bake(model).size.x;
        Dictionary<string, AnimationClip> clips = Clips();
        clips["Idle_Ground"].SampleAnimation(model, 0f);
        Bounds standing = Bake(model);
        Assert.That(standing.size.x, Is.LessThanOrEqualTo(0.45f * rest));
        Assert.That(standing.min.y, Is.EqualTo(0f).Within(0.02f));

        clips["Land"].SampleAnimation(model, 12f / clips["Land"].frameRate);
        Bounds landed = Bake(model);
        Assert.That(landed.min.y, Is.EqualTo(standing.min.y).Within(0.02f));
        Assert.That(landed.size.x, Is.EqualTo(standing.size.x).Within(0.02f));
    }

    [Test]
    public void VultureModel_StandsOffTheDunes()
    {
        Mesh mesh = Body(Load<GameObject>(ModelPath)).sharedMesh;
        Texture2D atlas = DecodeAtlas();
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        float area = 0f, luma = 0f, saturation = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            float weight = 0.5f * Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude;
            Vector2 uv = (uvs[a] + uvs[b] + uvs[c]) / 3f;
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            Color.RGBToHSV(colour, out _, out float s, out _);
            luma += weight * Luma(colour);
            saturation += weight * s;
            area += weight;
        }

        Color sand = Load<Material>(SandPath).GetColor("_BaseColor");
        Color.RGBToHSV(sand, out _, out float sandSaturation, out _);
        Assert.That(luma / area, Is.LessThanOrEqualTo(Luma(sand) - 0.05f));
        Assert.That(saturation / area, Is.GreaterThanOrEqualTo(sandSaturation + 0.15f));
    }

    [Test]
    public void VultureModel_NoCyanGoldOrDanger()
    {
        Texture2D atlas = DecodeAtlas();
        foreach (Vector2 uv in Body(Load<GameObject>(ModelPath)).sharedMesh.uv.Distinct())
        {
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            Color.RGBToHSV(colour, out float hue, out float saturation, out float value);
            float degrees = hue * 360f;
            bool pink = degrees >= 300f && degrees <= 355f && saturation > 0.18f && value > 0.25f;
            bool gold = degrees >= 35f && degrees <= 65f && saturation > 0.35f && value > 0.55f;
            bool cyan = degrees >= 155f && degrees <= 205f && saturation > 0.25f;
            Assert.That(pink || gold || cyan, Is.False, $"UV {uv} samples {colour}.");
            foreach (Color32 reserved in ReservedColours)
            {
                Color target = reserved;
                float distance = Mathf.Max(Mathf.Abs(colour.r - target.r),
                    Mathf.Abs(colour.g - target.g), Mathf.Abs(colour.b - target.b));
                Assert.That(distance, Is.GreaterThan(0.12f), $"UV {uv} samples {colour} near {reserved}.");
            }
        }
    }

    [Test]
    public void VultureSource_HashPinned()
    {
        using FileStream stream = File.OpenRead(SourcePath);
        using SHA256 algorithm = SHA256.Create();
        Assert.That(BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(),
            Is.EqualTo(SourceHash));
    }

    private static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private static SkinnedMeshRenderer Body(GameObject model) => model.GetComponentInChildren<SkinnedMeshRenderer>(true);
    private static Transform Bone(GameObject model, string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    private static float Luma(Color colour) => (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);
    private static Dictionary<string, AnimationClip> Clips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
        .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__")).ToDictionary(clip => clip.name);

    private GameObject Instance(string path)
    {
        var instance = Object.Instantiate(Load<GameObject>(path));
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _created.Add(instance);
        return instance;
    }

    private Bounds Bake(GameObject model)
    {
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(mesh);
        SkinnedMeshRenderer renderer = Body(model);
        renderer.BakeMesh(mesh);
        Vector3[] points = mesh.vertices.Select(p => model.transform.InverseTransformPoint(renderer.transform.TransformPoint(p))).ToArray();
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            Assert.That(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z), Is.True);
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    private Texture2D DecodeAtlas()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(texture);
        Assert.That(texture.LoadImage(File.ReadAllBytes(AtlasPath)), Is.True);
        return texture;
    }
}
