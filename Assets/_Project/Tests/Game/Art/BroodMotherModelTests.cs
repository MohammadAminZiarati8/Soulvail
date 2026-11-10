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

/// <summary>Import and art checks for the Sunscar broodmother.</summary>
[TestFixture]
public sealed class BroodMotherModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/BroodMother.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string SandPath = "Assets/_Project/Materials/Environment/M_SunscarSand.mat";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    private const string ScorpionPath = "Assets/_Project/Art/Enemies/Scorpion.fbx";
    private const int MinTriangles = 1500;
    private const int MaxTriangles = 3000;
    private const float DarkerThanSand = 0.05f;
    private const float MoreSaturatedThanSand = 0.15f;

    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        { "Idle", 48 }, { "Skitter", 12 }, { "Windup", 18 },
        { "Strike", 24 }, { "Hit", 12 }, { "Death", 30 },
    };

    private static readonly string[] Loops = { "Idle", "Skitter" };
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
    public void BroodModel_IsShipped()
    {
        // Every row below reads this model; a missing one would fail them all for the wrong reason.
        Assert.That(Model().GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void BroodModel_StaysInBudget()
    {
        Mesh mesh = Body().sharedMesh;

        Assert.That(mesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1), "One material slot: one draw.");
    }

    [Test]
    public void BroodModel_UsesTheEnemyMaterial()
    {
        // GD §17.1's one material across all enemies, so the Scorpion flashes and glows through
        // EnemyHitFeedback exactly as the Rootling does.
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath) }));
    }

    [Test]
    public void BroodModel_IsWeighted()
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
    public void BroodModel_ImportsGenericWithItsClips()
    {
        // Generic, so no per-frame retargeting on 28 bodies; not optimised, so the transform paths the
        // clips bind to exist; and the six clips named, the two a body cycles in looping.
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
    public void BroodModel_ClipsLeaveRootAndBroodAlone()
    {
        GameObject model = Instance();
        Transform root = RigRoot(model);
        Transform brood = root.Find("Body/Brood");
        var rests = new[] { root, brood }.Select(bone =>
            (Bone: bone, Position: bone.localPosition, Rotation: bone.localRotation, Scale: bone.localScale)).ToArray();

        foreach (AnimationClip clip in ClipsByName().Values)
        {
            Assert.That(AnimationUtility.GetCurveBindings(clip).Any(binding => binding.path.EndsWith("/Brood")), Is.False, clip.name);
            for (int frame = 0; frame <= ClipFrames[clip.name]; frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                foreach (var rest in rests)
                {
                    string where = $"{clip.name} frame {frame}, {rest.Bone.name}";
                    Assert.That(Vector3.Distance(rest.Bone.localPosition, rest.Position), Is.LessThan(1e-4f), where);
                    Assert.That(Quaternion.Angle(rest.Bone.localRotation, rest.Rotation), Is.LessThan(0.05f), where);
                    Assert.That(Vector3.Distance(rest.Bone.localScale, rest.Scale), Is.LessThan(1e-4f), where);
                }
            }
        }
    }

    [Test]
    public void BroodModel_HasRigScorpionAndBrood()
    {
        Transform scorpion = RigRoot(AssetDatabase.LoadAssetAtPath<GameObject>(ScorpionPath));
        Transform brood = RigRoot(Model());

        string[] expected = scorpion.GetComponentsInChildren<Transform>(true)
            .Select(bone => AnimationUtility.CalculateTransformPath(bone, scorpion))
            .Append("Body/Brood").ToArray();
        string[] actual = brood.GetComponentsInChildren<Transform>(true)
            .Select(bone => AnimationUtility.CalculateTransformPath(bone, brood)).ToArray();

        Assert.That(brood.parent.name, Is.EqualTo("Rig_Scorpion"));
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void BroodModel_TailClearsTheSac()
    {
        GameObject model = Instance();
        Transform torso = RigRoot(model).Find("Body");
        Transform[] chain = TailChain(torso);
        var frame = new BroodFrame(model, torso);

        foreach (AnimationClip clip in ClipsByName().Values)
        {
            for (int f = 0; f <= ClipFrames[clip.name]; f++)
            {
                clip.SampleAnimation(model, f / clip.frameRate);
                for (int link = 0; link < chain.Length; link++)
                {
                    Transform bone = chain[link];
                    Vector3 head = frame.RestPoint(bone.position);
                    Vector3 tip = frame.RestPoint(BoneTip(chain, link));
                    string where = $"{clip.name} frame {f}, {bone.name}";
                    Assert.That(Intersects(frame.Sac, head, tip), Is.False, $"{where} crosses the sac.");
                    Assert.That(Intersects(frame.Torso, head, tip), Is.False, $"{where} crosses the torso.");
                }
            }
        }
    }

    [Test]
    public void BroodModel_StrikeLandsAhead()
    {
        GameObject model = Instance();
        Transform torso = RigRoot(model).Find("Body");
        Transform[] chain = TailChain(torso);
        var frame = new BroodFrame(model, torso);
        AnimationClip strike = ClipsByName()["Strike"];

        strike.SampleAnimation(model, 2 / strike.frameRate);
        Vector3 tip = frame.RestPoint(BoneTip(chain, chain.Length - 1));
        float left = frame.RestPoint(torso.Find("Pincer_L").position).x;
        float right = frame.RestPoint(torso.Find("Pincer_R").position).x;

        Assert.That(tip.z, Is.GreaterThan(frame.Torso.max.z), "The stinger lands past the torso front.");
        Assert.That(tip.x, Is.InRange(Mathf.Min(left, right), Mathf.Max(left, right)), "The tip lands between the pincers.");
        Assert.That(Mathf.Abs(tip.x), Is.LessThanOrEqualTo(0.3f), "The tip stays near the centreline.");
    }

    [Test]
    public void BroodModel_IsBiggerThanTheScorpion()
    {
        float Footprint(GameObject model)
        {
            var instance = Object.Instantiate(model);
            _created.Add(instance);
            SkinnedMeshRenderer renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(mesh);
            renderer.BakeMesh(mesh);
            Vector3[] points = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
            return (points.Max(p => p.x) - points.Min(p => p.x)) * (points.Max(p => p.z) - points.Min(p => p.z));
        }

        Assert.That(Footprint(Model()) / Footprint(AssetDatabase.LoadAssetAtPath<GameObject>(ScorpionPath)),
            Is.InRange(1.6f, 2.2f));
    }

    [Test]
    public void BroodSource_HashPinned()
    {
        const string tools = "Tools/Blender/";
        string script = File.ReadAllText(tools + "brood.py");
        var pin = Regex.Match(script, "SOURCE_SHA256 = \"([0-9a-f]{64})\"");
        using SHA256 sha = SHA256.Create();
        string actual = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(tools + "Sources/BroodMother_src.glb")))
            .Replace("-", "").ToLowerInvariant();

        Assert.That(pin.Success, Is.True);
        Assert.That(actual, Is.EqualTo(pin.Groups[1].Value));
    }

    private static Transform RigRoot(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");

    private static Transform[] TailChain(Transform torso)
    {
        var chain = new Transform[6];
        Transform parent = torso;
        for (int i = 0; i < chain.Length; i++)
        {
            parent = parent.Find(i < 5 ? $"Tail_{i + 1}" : "Stinger");
            Assert.That(parent, Is.Not.Null);
            chain[i] = parent;
        }
        return chain;
    }

    private static Vector3 BoneTip(Transform[] chain, int link)
    {
        if (link < 4)
        {
            return chain[link + 1].position;
        }
        // The FBX has no leaf bones. These two measured rest lengths locate the distal tips.
        const float tailFiveLength = 0.305765f;
        const float stingerLength = 0.473435f;
        return chain[link].position + chain[link].up * (link == 4 ? tailFiveLength : stingerLength);
    }

    private static bool Intersects(Bounds bounds, Vector3 head, Vector3 tip)
    {
        if (bounds.Contains(head) || bounds.Contains(tip))
        {
            return true;
        }

        Vector3 span = tip - head;
        return bounds.IntersectRay(new Ray(head, span.normalized), out float entry) && entry < span.magnitude;
    }

    private sealed class BroodFrame
    {
        private readonly Transform _body;
        private readonly Matrix4x4 _rest;
        private readonly float _forward;

        // Scan-space outline of the torso, excluding the appendages and rear tail attachment.
        private const float Scale = 2.65f;
        private const float Ground = 0.28f;

        public BroodFrame(GameObject model, Transform body)
        {
            _body = body;
            _rest = body.localToWorldMatrix;
            _forward = Mathf.Sign(body.Find("Pincer_L").position.z - body.position.z);
            var torso = new Bounds();
            torso.SetMinMax(Scale * new Vector3(-0.220f, Ground - 0.250f, -0.284f),
                Scale * new Vector3(0.220f, Ground + 0.013f, 0.268f));
            Torso = torso;

            SkinnedMeshRenderer renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
            int brood = System.Array.IndexOf(renderer.bones, body.Find("Brood"));
            Assert.That(brood, Is.GreaterThanOrEqualTo(0));
            Mesh mesh = renderer.sharedMesh;
            var sac = new Bounds();
            bool first = true;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (weights[i].boneIndex0 == brood && weights[i].weight0 > 0.99f)
                {
                    Vector3 point = RestPoint(renderer.transform.TransformPoint(vertices[i]));
                    if (first)
                    {
                        sac.center = point;
                        first = false;
                    }
                    sac.Encapsulate(point);
                }
            }

            Assert.That(first, Is.False, "The Brood bone needs sac vertices.");
            Sac = sac;
        }

        public Bounds Torso { get; }
        public Bounds Sac { get; }

        public Vector3 RestPoint(Vector3 world)
        {
            Vector3 point = _rest.MultiplyPoint3x4(_body.InverseTransformPoint(world));
            point.z *= _forward;
            return point;
        }
    }

    [Test]
    public void BroodModel_NoCyanOrDanger()
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
    public void BroodModel_StandsOffTheDunes()
    {
        // Averaged over its surface, the scorpion colour against the sand: each
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
            $"The broodmother averages {luminance / area:F3} luma on sand of {sandLuminance:F3}.");
        Assert.That(saturation / area, Is.GreaterThanOrEqualTo(sandSaturation + MoreSaturatedThanSand),
            $"The broodmother averages {saturation / area:F3} saturation on sand of {sandSaturation:F3}.");
    }

    private static GameObject Model()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Assert.That(model, Is.Not.Null, $"No model at {ModelPath}. Run Tools/Blender/brood.py.");

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
