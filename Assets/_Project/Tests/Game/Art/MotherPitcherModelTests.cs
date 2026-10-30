using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-12d: the sculpt's imported mesh, rig, clips and shared-atlas repaint.</summary>
[TestFixture]
public sealed class MotherPitcherModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/MotherPitcher.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const int MinTriangles = 400;
    private const int MaxTriangles = 2500;
    private static readonly Dictionary<string, int> ClipFrames = new Dictionary<string, int>
    {
        ["Barrage"] = 24,
        ["Death"] = 40,
        ["Hit"] = 12,
        ["Idle"] = 60,
        ["Spray"] = 30,
    };
    private static readonly string[] Bones =
    {
        "base", "cup", "leaf_1", "leaf_2", "leaf_3", "leaf_4", "lid", "root", "stem_1", "stem_2", "stem_3",
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
    public void MotherModel_IsShipped()
    {
        // Every row below reads this model; a missing one would fail them all for the wrong reason.
        Assert.That(Model(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void MotherModel_StaysInTheCrowdsBudget()
    {
        Mesh mesh = Body(ModelPath).sharedMesh;

        Assert.That(mesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1), "One material slot: one draw.");

        // UV seams may split vertices, but smooth normals must agree across each split.
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Assert.That(normals, Has.Length.EqualTo(vertices.Length));
        var normalAt = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Assert.That(normals[i].sqrMagnitude, Is.GreaterThan(0.99f));
            if (normalAt.TryGetValue(vertices[i], out Vector3 normal))
            {
                Assert.That(Vector3.Dot(normal, normals[i]), Is.GreaterThan(0.999f), $"Hard seam at vertex {i}.");
            }
            else
            {
                normalAt.Add(vertices[i], normals[i]);
            }
        }
    }

    [Test]
    public void MotherModel_UsesTheEnemyMaterial()
    {
        // GD 17.1's one material across all enemies, so Mother Pitcher flashes and glows through
        // EnemyHitFeedback exactly as the Frog does.
        Material material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
        Assert.That(material, Is.Not.Null);
        Assert.That(Body(ModelPath).sharedMaterials, Is.EqualTo(new[] { material }));
    }

    [Test]
    public void MotherModel_EveryVertexIsWeighted()
    {
        SkinnedMeshRenderer body = Body(ModelPath);
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();

        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));

        Transform rig = Model(ModelPath).GetComponentsInChildren<Transform>(true).Single(t => t.name == "Rig_MotherPitcher");
        Assert.That(rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig).Select(t => t.name).OrderBy(n => n),
            Is.EqualTo(Bones));

        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v} is weighted to {counts[v]} bones");

            float sum = 0f;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                sum += weights[at].weight;

                Assert.That(body.bones[weights[at].boneIndex].name, Is.Not.EqualTo("root"),
                    $"vertex {v} rides the root, which no clip moves.");
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"vertex {v}'s weights sum to {sum}");
        }
    }

    [Test]
    public void MotherModel_ImportsGenericWithItsClips()
    {
        // Generic, so no per-frame retargeting on 28 bodies; not optimised, so the transform paths the
        // clips bind to exist; and the five clips named, Idle looping.
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.animationCompression, Is.EqualTo(ModelImporterAnimationCompression.Off));
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(importer.importNormals, Is.EqualTo(ModelImporterNormals.Import));
        Assert.That(Model(ModelPath).GetComponent<Animator>().avatar.isValid, Is.True);

        Dictionary<string, AnimationClip> clips = ClipsByName();

        Assert.That(clips.Keys.OrderBy(n => n), Is.EqualTo(ClipFrames.Keys.OrderBy(n => n)));

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.isLooping, Is.EqualTo(clip.name == "Idle"), $"{clip.name}'s loop flag");
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(Mathf.RoundToInt(clip.length * clip.frameRate), Is.EqualTo(ClipFrames[clip.name]), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
        }
    }

    [Test]
    public void MotherModel_EveryClipBindsAndLeavesTheRootAlone()
    {
        // Core owns an enemy's position: a clip that moved the root would carry the body off its
        // collider. Each clip is sampled at every frame into a live copy of the model.
        GameObject model = Instance();
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        Vector3 modelPosition = model.transform.localPosition;
        Vector3 rest = root.localPosition;
        Transform cup = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "cup");
        Transform lid = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "lid");
        Quaternion restRotation = root.localRotation;
        Vector3 restScale = root.localScale;

        foreach (AnimationClip clip in ClipsByName().Values)
        {
            Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Not.Empty, clip.name);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which Mother Pitcher does not have.");
            }

            clip.SampleAnimation(model, 0f);
            Quaternion cupStart = cup.rotation;
            Quaternion heldCup = Quaternion.identity, heldLid = Quaternion.identity;
            float motion = 0f;
            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                motion = Mathf.Max(motion, Quaternion.Angle(cupStart, cup.rotation));
                if (clip.name == "Spray" && frame == 10)
                {
                    heldCup = cup.rotation;
                    heldLid = lid.rotation;
                }
                else if (clip.name == "Spray" && frame > 10)
                {
                    Assert.That(Quaternion.Angle(heldCup, cup.rotation), Is.LessThan(0.1f), $"Spray cup at {frame}");
                    Assert.That(Quaternion.Angle(heldLid, lid.rotation), Is.LessThan(0.1f), $"Spray lid at {frame}");
                }

                Assert.That((model.transform.localPosition - modelPosition).magnitude, Is.LessThan(1e-4f), clip.name);
                Assert.That(Quaternion.Angle(root.localRotation, restRotation), Is.LessThan(1e-3f), clip.name);
                Assert.That((root.localScale - restScale).magnitude, Is.LessThan(1e-4f), clip.name);

                Assert.That((root.localPosition - rest).magnitude, Is.LessThan(1e-4f),
                    $"{clip.name} moves the root at frame {frame}.");
            }

            Assert.That(motion, Is.GreaterThan(1f), $"{clip.name} must move the cup.");
        }
    }

    [Test]
    public void MotherModel_IsDarkButReadable()
    {
        // Surface-area average of the atlas sampled at each triangle centroid. Each
        // triangle counts in proportion to its area.
        Mesh mesh = Body(ModelPath).sharedMesh;
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

        Assert.That(area, Is.GreaterThan(0f));
        Assert.That(luminance / area, Is.InRange(0.15f, 0.45f));
        Assert.That(saturation / area, Is.LessThanOrEqualTo(0.4f));

        // Every triangle uses only Pitcher columns 4-5 or Frog mouth/pupil cells.
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 centroid = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3f;
            int column = Mathf.FloorToInt(centroid.x * 8);
            int row = Mathf.FloorToInt((1f - centroid.y) * 4);
            Assert.That(column == 4 || column == 5 || ((column == 2 || column == 3) && row == 2), Is.True,
                $"Triangle {i / 3} samples column {column}, row {row}.");
        }
    }

    [Test]
    public void MotherModel_IsSquat()
    {
        Bounds bounds = Body(ModelPath).sharedMesh.bounds;
        Assert.That(bounds.size.x, Is.GreaterThanOrEqualTo(0.7f * bounds.size.y));
    }

    private static GameObject Model(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(model, Is.Not.Null, $"No model at {path}. Run its script under Tools/Blender.");

        return model;
    }

    private static SkinnedMeshRenderer Body(string path) => Model(path).GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static Dictionary<string, AnimationClip> ClipsByName() =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .ToDictionary(c => c.name);

    private static float Luma(Color c) => (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);

    private GameObject Instance()
    {
        var instance = (GameObject)Object.Instantiate(Model(ModelPath));
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
