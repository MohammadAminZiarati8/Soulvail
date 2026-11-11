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

/// <summary>Import and art checks for the Sunscar antlion.</summary>
[TestFixture]
public sealed class AntlionModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Antlion.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    private const int MinTriangles = 1500;
    private const int MaxTriangles = 3000;
    private const float LumaMargin = 0.05f;
    private const float MoreSaturatedThanSand = 0.15f;
    private const float JawSwatchStart = 354f / 512f;
    private const float JawSwatchEnd = 356f / 512f;

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        { "Idle", 48 }, { "Walk", 16 }, { "DigIn", 18 }, { "Erupt", 12 },
        { "Surfaced", 30 }, { "Hit", 12 }, { "Death", 30 },
    };

    private static readonly string[] Loops = { "Idle", "Walk", "Surfaced" };
    private static readonly Color32[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF),
        new Color32(0xFF, 0x4A, 0x1F, 0xFF),
        new Color32(0xA8, 0x55, 0xF7, 0xFF),
        new Color32(0xFB, 0xBF, 0x24, 0xFF),
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
    public void AntlionModel_IsShippedInBudget()
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
    public void AntlionModel_IsWeighted()
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
    public void AntlionModel_ImportsItsClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.motionNodeName, Is.Empty);
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
    public void AntlionModel_HasRigBeetleAndMandibles()
    {
        GameObject model = Model();
        Transform root = RigRoot(model);
        Assert.That(root.parent.name, Is.EqualTo("Rig_Beetle"));
        Transform body = root.Find("Body");
        Assert.That(body, Is.Not.Null);
        Transform head = body.Find("Head");
        Assert.That(head, Is.Not.Null);
        Assert.That(model.transform.InverseTransformPoint(head.position).z, Is.GreaterThan(0f));
        Assert.That(body.Find("Abdomen"), Is.Not.Null);
        Assert.That(head.Find("Mandible_L"), Is.Not.Null);
        Assert.That(head.Find("Mandible_R"), Is.Not.Null);
        foreach (string side in new[] { "L", "R" })
        {
            for (int leg = 1; leg <= 3; leg++)
            {
                Assert.That(body.Find($"Leg_{side}_{leg}_Upper/Leg_{side}_{leg}_Lower"), Is.Not.Null);
            }
        }

        Assert.That(root.GetComponentsInChildren<Transform>(true), Has.Length.EqualTo(18));
    }

    [Test]
    public void AntlionModel_ClipsLeaveRootAlone()
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
    public void AntlionModel_IsLongAndLow()
    {
        Vector3 size = BoundsOf(Instance()).size;
        Assert.That(size.z, Is.InRange(1.3f, 1.5f));
        Assert.That(size.y, Is.LessThanOrEqualTo(0.55f * size.x));
        Assert.That(size.x, Is.InRange(0.9f, 1.1f));
    }

    [Test]
    public void AntlionModel_DigInSinks()
    {
        GameObject model = Instance();
        Transform body = RigRoot(model).Find("Body");
        float rest = model.transform.InverseTransformPoint(body.position).y;
        AnimationClip digIn = ClipsByName()["DigIn"];

        digIn.SampleAnimation(model, digIn.length);

        float buried = model.transform.InverseTransformPoint(body.position).y;
        Assert.That(rest - buried, Is.GreaterThanOrEqualTo(0.5f));
    }

    [Test]
    public void AntlionModel_EruptSnapsJaws()
    {
        GameObject model = Instance();
        Transform head = RigRoot(model).Find("Body/Head");
        Transform[] jaws = { head.Find("Mandible_L"), head.Find("Mandible_R") };
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        AnimationClip erupt = ClipsByName()["Erupt"];
        BoneWeight[] weights = skin.sharedMesh.boneWeights;

        erupt.SampleAnimation(model, 4 / 30f);
        Quaternion[] open = jaws.Select(jaw => jaw.localRotation).ToArray();
        Vector3[] openPoints = BakedPoints(model);
        int[] tips = jaws.Select(jaw =>
        {
            int boneIndex = System.Array.IndexOf(skin.bones, jaw);
            Assert.That(boneIndex, Is.GreaterThanOrEqualTo(0), jaw.name);
            return Enumerable.Range(0, openPoints.Length)
                .Where(i => weights[i].boneIndex0 == boneIndex && weights[i].weight0 > 0.99f)
                .OrderBy(i => (openPoints[i] - model.transform.InverseTransformPoint(jaw.position)).sqrMagnitude)
                .Last();
        }).ToArray();

        erupt.SampleAnimation(model, 10 / 30f);
        for (int i = 0; i < jaws.Length; i++)
        {
            Assert.That(Quaternion.Angle(open[i], jaws[i].localRotation),
                Is.GreaterThanOrEqualTo(25f), jaws[i].name);
        }

        Vector3[] closedPoints = BakedPoints(model);
        Assert.That(Vector3.Distance(closedPoints[tips[0]], closedPoints[tips[1]]),
            Is.LessThan(Vector3.Distance(openPoints[tips[0]], openPoints[tips[1]])));
    }

    private static Transform RigRoot(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");

    private Vector3[] BakedPoints(GameObject model)
    {
        var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(mesh);
        body.BakeMesh(mesh);
        return mesh.vertices.Select(p =>
            model.transform.InverseTransformPoint(body.transform.TransformPoint(p))).ToArray();
    }

    private Bounds BoundsOf(GameObject model)
    {
        Vector3[] points = BakedPoints(model);
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    [Test]
    public void AntlionSource_HashPinned()
    {
        const string tools = "Tools/Blender/";
        string script = File.ReadAllText(tools + "antlion.py");
        var pin = Regex.Match(script, "SOURCE_SHA256 = \"([0-9a-f]{64})\"");
        using SHA256 sha = SHA256.Create();
        string actual = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(tools + "Sources/Antlion_src.glb")))
            .Replace("-", "").ToLowerInvariant();

        Assert.That(pin.Success, Is.True);
        Assert.That(actual, Is.EqualTo(pin.Groups[1].Value));
    }

    [Test]
    public void AntlionModel_NoCyanOrDanger()
    {
        Texture2D atlas = Decode(AtlasPath);
        foreach (Vector2 uv in Body().sharedMesh.uv)
        {
            Color colour = atlas.GetPixelBilinear(uv.x, uv.y);
            Color.RGBToHSV(colour, out float hue, out float saturation, out _);
            Assert.That(saturation < 0.1f || hue < 0.45f || hue > 0.73f, Is.True,
                $"UV {uv} samples blue or cyan {colour}.");
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
    public void AntlionModel_StandsOffTheDunes()
    {
        SkinnedMeshRenderer body = Body();
        Mesh mesh = body.sharedMesh;
        Texture2D atlas = Decode(AtlasPath);
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;

        float jawArea = 0f, jawLuma = 0f;
        float bodyArea = 0f, bodyLuma = 0f, bodySaturation = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            float area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
            Vector2 centroid = (uv[a] + uv[b] + uv[c]) / 3f;
            Color colour = atlas.GetPixelBilinear(centroid.x, centroid.y);
            if (centroid.x >= JawSwatchStart && centroid.x < JawSwatchEnd)
            {
                jawArea += area;
                jawLuma += area * Luma(colour);
            }
            else
            {
                bodyArea += area;
                bodyLuma += area * Luma(colour);
                Color.RGBToHSV(colour, out _, out float saturation, out _);
                bodySaturation += area * saturation;
            }
        }

        Material sand = AssetDatabase.LoadAssetAtPath<Material>(SandPath);
        Assert.That(sand, Is.Not.Null);
        Color sandColour = sand.GetColor("_BaseColor");
        Color.RGBToHSV(sandColour, out _, out float sandSaturation, out _);
        Assert.That(jawArea, Is.GreaterThan(0f));
        Assert.That(bodyArea, Is.GreaterThan(0f));
        Assert.That(jawLuma / jawArea, Is.GreaterThanOrEqualTo((bodyLuma / bodyArea) + LumaMargin));
        Assert.That(bodyLuma / bodyArea, Is.LessThanOrEqualTo(Luma(sandColour) - LumaMargin));
        Assert.That(bodySaturation / bodyArea, Is.GreaterThanOrEqualTo(sandSaturation + MoreSaturatedThanSand));
    }

    private static GameObject Model()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Assert.That(model, Is.Not.Null, $"No model at {ModelPath}. Run Tools/Blender/antlion.py.");

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
