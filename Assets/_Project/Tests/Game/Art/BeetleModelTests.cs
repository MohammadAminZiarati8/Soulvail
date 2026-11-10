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

/// <summary>Import and art checks for the Sunscar bombardier beetle.</summary>
[TestFixture]
public sealed class BeetleModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Beetle.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    private const int MinTriangles = 400;
    private const int MaxTriangles = 2500;
    private const float DarkerThanSand = 0.05f;
    private const float MoreSaturatedThanSand = 0.15f;

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        { "Idle", 48 }, { "Walk", 16 }, { "Turn", 24 }, { "Aim", 21 },
        { "Fire", 12 }, { "Hit", 12 }, { "Death", 30 },
    };

    private static readonly string[] Loops = { "Idle", "Walk", "Turn" };
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
    public void BeetleModel_IsShipped()
    {
        // Every row below reads this model; a missing one would fail them all for the wrong reason.
        Assert.That(Model().GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void BeetleModel_StaysInTheCrowdsBudget()
    {
        Mesh mesh = Body().sharedMesh;

        Assert.That(mesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1), "One material slot: one draw.");
    }

    [Test]
    public void BeetleModel_UsesTheEnemyMaterial()
    {
        // GD §17.1's one material across all enemies, so the Beetle flashes and glows through
        // EnemyHitFeedback exactly as the Rootling does.
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath) }));
    }

    [Test]
    public void BeetleModel_EveryVertexIsWeighted()
    {
        SkinnedMeshRenderer body = Body();
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();

        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));

        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That((int)counts[v], Is.InRange(1, 4), $"vertex {v} is weighted to {counts[v]} bones");

            float sum = 0f;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                Assert.That(weights[at].weight, Is.GreaterThan(0f));
                sum += weights[at].weight;

                Assert.That(body.bones[weights[at].boneIndex].name, Is.Not.EqualTo("Root"),
                    $"vertex {v} rides the root, which no clip moves.");
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"vertex {v}'s weights sum to {sum}");
        }
    }

    [Test]
    public void BeetleModel_ImportsGenericWithItsClips()
    {
        // Generic, so no per-frame retargeting on 28 bodies; not optimised, so the transform paths the
        // clips bind to exist; and the seven named clips, with Idle, Walk and Turn looping.
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
    public void BeetleModel_EveryClipBindsAndLeavesTheRootAlone()
    {
        // Core owns an enemy's position: a clip that moved the root would carry the body off its
        // collider. Each clip is sampled at every frame into a live copy of the model.
        GameObject model = Instance();
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");
        Vector3 rest = root.localPosition;
        Quaternion restRotation = root.localRotation;
        Vector3 restScale = root.localScale;

        foreach (AnimationClip clip in ClipsByName().Values)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings, Is.Not.Empty, clip.name);
            foreach (EditorCurveBinding binding in bindings)
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which the Beetle does not have.");
            }

            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);

                Assert.That((root.localPosition - rest).magnitude, Is.LessThan(1e-4f),
                    $"{clip.name} moves the root at frame {frame}.");
                Assert.That(Quaternion.Angle(root.localRotation, restRotation), Is.LessThan(0.05f), clip.name);
                Assert.That((root.localScale - restScale).magnitude, Is.LessThan(1e-4f), clip.name);
            }
        }
    }

    [Test]
    public void BeetleModel_HasTheRigsBones()
    {
        Transform root = Model().GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");
        Assert.That(root.parent.name, Is.EqualTo("Rig_Beetle"));
        Transform body = root.Find("Body");
        Assert.That(body, Is.Not.Null);

        foreach (string side in new[] { "L", "R" })
        {
            for (int leg = 1; leg <= 3; leg++)
            {
                Assert.That(body.Find($"Leg_{side}_{leg}_Upper/Leg_{side}_{leg}_Lower"), Is.Not.Null,
                    $"Leg {side} {leg}");
            }
        }

        Assert.That(body.Find("Head"), Is.Not.Null);
        Transform vent = body.Find("Abdomen/Vent");
        Assert.That(vent, Is.Not.Null);
        Vector3 localDirection = Model().transform.InverseTransformDirection(vent.forward);
        Assert.That(localDirection.z, Is.LessThan(-0.3f), "Vent faces backwards.");
        Assert.That(localDirection.y, Is.GreaterThan(0.3f), "Vent faces up.");
        GameObject model = Instance();
        Transform movingVent = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Vent");
        Dictionary<string, AnimationClip> clips = ClipsByName();
        float restHeight = movingVent.position.y;
        clips["Aim"].SampleAnimation(model, clips["Aim"].length);
        float raisedHeight = movingVent.position.y;
        Assert.That(raisedHeight, Is.GreaterThan(restHeight + 0.2f), "Aim raises the vent.");
        clips["Fire"].SampleAnimation(model, 1f / 30f);
        Assert.That(movingVent.position.y, Is.EqualTo(raisedHeight + 0.02f).Within(0.03f), "Fire begins at full lift.");
    }

    [Test]
    public void BeetleModel_IsWiderThanTall()
    {
        SkinnedMeshRenderer body = Instance().GetComponentInChildren<SkinnedMeshRenderer>();
        var bindMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(bindMesh);
        body.BakeMesh(bindMesh);
        Vector3[] points = bindMesh.vertices.Select(body.transform.TransformPoint).ToArray();
        float width = points.Max(point => point.x) - points.Min(point => point.x);
        float height = points.Max(point => point.y) - points.Min(point => point.y);

        Assert.That(width, Is.GreaterThanOrEqualTo(1.5f * height));
    }

    [Test]
    public void BeetleModel_NoCyanOrDanger()
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
        }
    }

    [Test]
    public void BeetleModel_StandsOffTheDunes()
    {
        // Averaged over its surface, the beetle colour against the sand: each
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
            $"The Beetle averages {luminance / area:F3} luma on sand of {sandLuminance:F3}.");
        Assert.That(saturation / area, Is.GreaterThanOrEqualTo(sandSaturation + MoreSaturatedThanSand),
            $"The Beetle averages {saturation / area:F3} saturation on sand of {sandSaturation:F3}.");
    }

    private static GameObject Model()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Assert.That(model, Is.Not.Null, $"No model at {ModelPath}. Run Tools/Blender/beetle.py.");

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

    [Test]
    public void BeetleSource_HashPinned()
    {
        string blender = Path.Combine(Application.dataPath, "..", "Tools", "Blender");
        string script = File.ReadAllText(Path.Combine(blender, "beetle.py"));
        Match pin = Regex.Match(script, "^SOURCE_SHA256 = \"([0-9a-f]{64})\"\r?$", RegexOptions.Multiline);
        Assert.That(pin.Success, Is.True, "beetle.py must declare the raw GLB SHA-256.");

        using var source = File.OpenRead(Path.Combine(blender, "Sources", "Beetle_src.glb"));
        using var sha = SHA256.Create();
        string actual = System.BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        Assert.That(actual, Is.EqualTo(pin.Groups[1].Value));
    }
}
