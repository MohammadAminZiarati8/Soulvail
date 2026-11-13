using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>Import and art checks for the Sunscar cobra (SS-12b).</summary>
[TestFixture]
public sealed class CobraModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Cobra.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    private const int MinTriangles = 1500;
    private const int MaxTriangles = 3000;
    private const float DarkerThanSand = 0.05f;
    private const float MoreSaturatedThanSand = 0.15f;
    private const float FoldedHood = 0.35f;
    private const float SpreadHood = 0.6f;
    private const float RearLift = 0.35f;

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        { "Idle", 48 }, { "Slither", 24 }, { "Rear", 21 }, { "Spit", 9 }, { "Hit", 12 }, { "Death", 30 },
    };

    private static readonly string[] Loops = { "Idle", "Slither" };
    private static readonly Color32[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF),
        new Color32(0xFF, 0x4A, 0x1F, 0xFF),
    };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    [Test]
    public void CobraModel_IsShippedInBudget()
    {
        SkinnedMeshRenderer[] renderers = Model().GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(renderers, Has.Length.EqualTo(1));
        Assert.That(renderers[0].sharedMesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(renderers[0].sharedMesh.subMeshCount, Is.EqualTo(1));
        Material material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
        Assert.That(material, Is.Not.Null);
        Assert.That(renderers[0].sharedMaterials, Is.EqualTo(new[] { material }));
    }

    [Test]
    public void CobraModel_IsWeighted()
    {
        SkinnedMeshRenderer body = Body();
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();

        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));

        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v} is weighted to {counts[v]} bones");

            float sum = 0f;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                sum += weights[at].weight;

                Assert.That(body.bones[weights[at].boneIndex].name, Is.Not.EqualTo("Root"),
                    $"vertex {v} rides the root, which no clip moves.");
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"vertex {v}'s weights sum to {sum}");
        }
    }

    [Test]
    public void CobraModel_HasRigViperAndHood()
    {
        Transform root = RigRoot(Model());
        Assert.That(root.parent.name, Is.EqualTo("Rig_Viper"));
        Transform spine = root;
        for (int i = 1; i <= 12; i++)
        {
            spine = spine.Find($"Spine_{i:00}");
            Assert.That(spine, Is.Not.Null, $"Spine_{i:00}");
        }

        Assert.That(root.Find("Spine_01/Head/Jaw"), Is.Not.Null);
        Assert.That(root.Find("Spine_01/Hood_L"), Is.Not.Null);
        Assert.That(root.Find("Spine_01/Hood_R"), Is.Not.Null);
        Assert.That(root.GetComponentsInChildren<Transform>(true), Has.Length.EqualTo(17));
        Assert.That(Vector3.Distance(root.Find("Spine_01").position, root.position), Is.LessThan(1e-4f));
    }

    [Test]
    public void CobraModel_ImportsItsClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);

        Dictionary<string, AnimationClip> clips = ClipsByName();

        Assert.That(clips.Keys, Is.EquivalentTo(ClipFrames.Keys));

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.isLooping, Is.EqualTo(Loops.Contains(clip.name)), $"{clip.name}'s loop flag");
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.length * clip.frameRate, Is.EqualTo(ClipFrames[clip.name]).Within(0.01f), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
        }
    }

    [Test]
    public void CobraModel_ClipsLeaveRootAlone()
    {
        GameObject model = Instance();
        Transform root = RigRoot(model);
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;
        Vector3 scale = root.localScale;
        foreach (AnimationClip clip in ClipsByName().Values)
        {
            for (int frame = 0; frame <= ClipFrames[clip.name]; frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                string context = $"{clip.name} frame {frame}";
                Assert.That(Vector3.Distance(root.localPosition, position), Is.LessThan(1e-4f), context);
                Assert.That(Quaternion.Angle(root.localRotation, rotation), Is.LessThan(0.05f), context);
                Assert.That(Vector3.Distance(root.localScale, scale), Is.LessThan(1e-4f), context);
            }
        }
    }

    [Test]
    public void CobraModel_RearOpensHood()
    {
        GameObject model = Instance();
        Dictionary<string, AnimationClip> clips = ClipsByName();
        clips["Idle"].SampleAnimation(model, 0f);
        float folded = HoodWidth(model);
        clips["Rear"].SampleAnimation(model, ClipFrames["Rear"] / 30f);
        float spread = HoodWidth(model);
        Assert.That(folded, Is.LessThanOrEqualTo(FoldedHood));
        Assert.That(spread, Is.GreaterThanOrEqualTo(SpreadHood));
    }

    [Test]
    public void CobraModel_RearLiftsHead()
    {
        GameObject model = Instance();
        Transform head = RigRoot(model).Find("Spine_01/Head");
        Dictionary<string, AnimationClip> clips = ClipsByName();
        clips["Idle"].SampleAnimation(model, 0f);
        float resting = model.transform.InverseTransformPoint(head.position).y;
        clips["Rear"].SampleAnimation(model, ClipFrames["Rear"] / 30f);
        float reared = model.transform.InverseTransformPoint(head.position).y;
        Assert.That(reared - resting, Is.GreaterThanOrEqualTo(RearLift));
    }

    [Test]
    public void CobraModel_IsLongAndLow()
    {
        Vector3 size = BoundsOf(Instance()).size;
        Assert.That(size.z, Is.InRange(2.2f, 2.6f));
        Assert.That(size.y, Is.LessThanOrEqualTo(0.4f));
    }

    [Test]
    public void CobraModel_StandsOffTheDunes()
    {
        // Averaged over its surface, the cobra colour against the sand: each
        // triangle samples its swatch at its centroid and counts for its area.
        Mesh mesh = Body().sharedMesh;
        Texture2D atlas = Decode(AtlasPath);
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;

        float area = 0f, luminance = 0f, saturation = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            float weight = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            Vector2 centroid = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3f;
            Color colour = atlas.GetPixelBilinear(centroid.x, centroid.y);

            Color.RGBToHSV(colour, out _, out float s, out _);

            area += weight;
            luminance += weight * Luma(colour);
            saturation += weight * s;
        }

        Material sand = AssetDatabase.LoadAssetAtPath<Material>(SandPath);
        Assert.That(sand, Is.Not.Null);
        Color sandColour = sand.GetColor("_BaseColor");
        Color.RGBToHSV(sandColour, out _, out float sandSaturation, out _);
        float sandLuminance = Luma(sandColour);

        Assert.That(luminance / area, Is.LessThanOrEqualTo(sandLuminance - DarkerThanSand),
            $"The cobra averages {luminance / area:F3} luma on sand of {sandLuminance:F3}.");
        Assert.That(saturation / area, Is.GreaterThanOrEqualTo(sandSaturation + MoreSaturatedThanSand),
            $"The cobra averages {saturation / area:F3} saturation on sand of {sandSaturation:F3}.");
    }

    [Test]
    public void CobraModel_NoCyanGoldGreenOrDanger()
    {
        Texture2D atlas = Decode(AtlasPath);
        foreach (Vector2 uv in Body().sharedMesh.uv)
        {
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            foreach (Color32 reserved in Reserved)
            {
                Color target = reserved;
                float distance = Mathf.Max(Mathf.Abs(colour.r - target.r),
                    Mathf.Abs(colour.g - target.g), Mathf.Abs(colour.b - target.b));
                Assert.That(distance, Is.GreaterThan(0.12f), $"UV {uv} samples {colour} near {reserved}.");
            }

            Color.RGBToHSV(colour, out float hue, out float saturation, out float value);
            bool gold = hue >= 0.09f && hue <= 0.17f && saturation > 0.55f && value > 0.72f;
            bool green = hue >= 0.2f && hue <= 0.5f && saturation > 0.2f;
            Assert.That(gold, Is.False, $"UV {uv} samples gold at {colour}.");
            Assert.That(green, Is.False, $"UV {uv} samples green at {colour}.");
        }
    }

    [Test]
    public void CobraSource_HashPinned()
    {
        const string tools = "Tools/Blender/";
        string script = File.ReadAllText(tools + "cobra.py");
        var pin = Regex.Match(script, "SOURCE_SHA256 = \"([0-9a-f]{64})\"");
        using SHA256 sha = SHA256.Create();
        string actual = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(tools + "Sources/Cobra_src.glb")))
            .Replace("-", "").ToLowerInvariant();

        Assert.That(pin.Success, Is.True);
        Assert.That(actual, Is.EqualTo(pin.Groups[1].Value));
    }

    private static Transform RigRoot(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");

    private Vector3[] Baked(GameObject model, SkinnedMeshRenderer body)
    {
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(mesh);
        body.BakeMesh(mesh);
        return mesh.vertices.Select(p => model.transform.InverseTransformPoint(body.transform.TransformPoint(p)))
            .ToArray();
    }

    private Bounds BoundsOf(GameObject model)
    {
        Vector3[] points = Baked(model, model.GetComponentInChildren<SkinnedMeshRenderer>());
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    /// <summary>The sideways span of the vertices the hood bones carry at least half of.</summary>
    private float HoodWidth(GameObject model)
    {
        SkinnedMeshRenderer body = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3[] points = Baked(model, body);
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
        float min = float.MaxValue, max = float.MinValue;
        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            float hood = 0f;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                if (body.bones[weights[at].boneIndex].name.StartsWith("Hood_"))
                {
                    hood += weights[at].weight;
                }
            }

            if (hood >= 0.5f)
            {
                min = Mathf.Min(min, points[v].x);
                max = Mathf.Max(max, points[v].x);
            }
        }

        Assert.That(max, Is.GreaterThan(min), "No vertex rides the hood bones.");

        return max - min;
    }

    private static GameObject Model()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Assert.That(model, Is.Not.Null, $"No model at {ModelPath}. Run Tools/Blender/cobra.py.");

        return model;
    }

    private static SkinnedMeshRenderer Body() => Model().GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static Dictionary<string, AnimationClip> ClipsByName() =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

    private static float Luma(Color c) => (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);

    private GameObject Instance()
    {
        var instance = (GameObject)Object.Instantiate(Model());
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);

        return instance;
    }

    /// <summary>
    /// A texture as its file holds it, decoded rather than read from the import — which is compressed
    /// and not readable, and is not what this rule is about.
    /// </summary>
    private Texture2D Decode(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(texture);

        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);

        texture.filterMode = FilterMode.Point;

        return texture;
    }
}
