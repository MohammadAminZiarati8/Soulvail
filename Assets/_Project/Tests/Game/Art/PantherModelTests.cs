using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// The Panther's model, as <c>Tools/Blender/panther.py</c> writes it and the importer reads it (JG-07b):
/// the Jungle's Lunger, TRELLIS's sculpt cut to the crowd's budget and repainted onto the atlas, on a
/// skeleton of its own.
/// </summary>
/// <remarks>
/// <see cref="PuffballModelTests"/>' rows for a fourth generated body, for their reason: they stop a
/// changed script or an import setting reset by an update from shipping a body that has blown the
/// crowd's budget, lost a clip or moved its root. Two rows differ. Its colour is held dark but not
/// lost: a charcoal coat stands off the green floor by being darker, not by being loud. And its
/// silhouette is long and low, an arrow along the line it dashes (GD §17.1). The shared atlas's
/// reserved colours are <see cref="RootlingModelTests"/>' rows, which read every pixel of it.
/// </remarks>
[TestFixture]
public sealed class PantherModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Panther.fbx";
    private const string FrogPath = "Assets/_Project/Art/Enemies/Frog.fbx";
    private const string PitcherPath = "Assets/_Project/Art/Enemies/Pitcher.fbx";
    private const string PuffballPath = "Assets/_Project/Art/Enemies/Puffball.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";

    /// <summary>GD §17.1's crowd budget.</summary>
    private const int MinTriangles = 400;

    private const int MaxTriangles = 2500;

    /// <summary>
    /// How light the Panther may be on average, Rec. 709 luma over the atlas's stored values, and how
    /// saturated: dark enough to stand off a floor near 0.39, not so dark it is a hole in it, and no
    /// louder than the floor. Built at 0.33 and 0.21 (JG-07b).
    /// </summary>
    private const float MinLuminance = 0.15f;

    private const float MaxLuminance = 0.40f;

    private const float MaxSaturation = 0.35f;

    /// <summary>
    /// Length over height and over width at rest: an arrow along its dash. Built at 2.12 (2.18 m long,
    /// 1.03 m tall) and 2.95 (0.74 m wide), the tail straightened behind it.
    /// </summary>
    private const float LongAtLeast = 2f;

    private const float NarrowAtLeast = 2.5f;

    private const float FrogAtMost = 1f;

    private const float PitcherAtLeast = 1.5f;

    private const float PuffballAtLeast = 1f;

    private const float PuffballAtMost = 1.35f;

    private static readonly string[] Clips = { "Crouch", "Death", "Hit", "Idle", "Lunge", "Prowl" };

    private static readonly string[] Loops = { "Idle", "Lunge", "Prowl" };

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
    public void PantherModel_IsShipped()
    {
        // Every row below reads this model; a missing one would fail them all for the wrong reason.
        Assert.That(Model(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void PantherModel_StaysInTheCrowdsBudget()
    {
        Mesh mesh = Body(ModelPath).sharedMesh;

        Assert.That(mesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1), "One material slot: one draw.");
    }

    [Test]
    public void PantherModel_UsesTheEnemyMaterial()
    {
        // GD §17.1's one material across all enemies, so the Panther flashes and glows through
        // EnemyHitFeedback exactly as the Frog does.
        Assert.That(Body(ModelPath).sharedMaterials, Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath) }));
    }

    [Test]
    public void PantherModel_EveryVertexIsWeighted()
    {
        SkinnedMeshRenderer body = Body(ModelPath);
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

                Assert.That(body.bones[weights[at].boneIndex].name, Is.Not.EqualTo("root"),
                    $"vertex {v} rides the root, which no clip moves.");
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"vertex {v}'s weights sum to {sum}");
        }
    }

    [Test]
    public void PantherModel_ImportsGenericWithItsClips()
    {
        // Generic, so no per-frame retargeting on 28 bodies; not optimised, so the transform paths the
        // clips bind to exist; and the six clips named, the three a body cycles in looping.
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);

        Dictionary<string, AnimationClip> clips = ClipsByName();

        Assert.That(clips.Keys.OrderBy(n => n), Is.EqualTo(Clips));

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.isLooping, Is.EqualTo(Loops.Contains(clip.name)), $"{clip.name}'s loop flag");
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
        }
    }

    [Test]
    public void PantherModel_EveryClipBindsAndLeavesTheRootAlone()
    {
        // Core owns an enemy's position: a clip that moved the root would carry the body off its
        // collider. Each clip is sampled at every frame into a live copy of the model.
        GameObject model = Instance();
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        Vector3 rest = root.localPosition;

        foreach (AnimationClip clip in ClipsByName().Values)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which the Panther does not have.");
            }

            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);

                Assert.That((root.localPosition - rest).magnitude, Is.LessThan(1e-4f),
                    $"{clip.name} moves the root at frame {frame}.");
            }
        }
    }

    [Test]
    public void PantherModel_IsDarkButNotLost()
    {
        // Averaged over its surface, the colour the Panther samples: a charcoal coat and a grey belly,
        // darker than the floor but not black, and no louder than it. Each triangle samples its swatch
        // at its centroid and counts for its area.
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

        Assert.That(luminance / area, Is.InRange(MinLuminance, MaxLuminance),
            $"The Panther averages {luminance / area:F3} luma.");
        Assert.That(saturation / area, Is.LessThanOrEqualTo(MaxSaturation),
            $"The Panther averages {saturation / area:F3} saturation.");
    }

    [Test]
    public void PantherModel_IsLongAndLow()
    {
        // GD §17.1: enemy silhouettes must be told apart as black shapes. The Panther is an arrow along
        // the line it dashes, long against its height and its width; the other three keep their own
        // proportions. The meshes at rest, Y up, Z forward.
        Bounds panther = Body(ModelPath).sharedMesh.bounds;
        Bounds frog = Body(FrogPath).sharedMesh.bounds, pitcher = Body(PitcherPath).sharedMesh.bounds;
        Bounds puffball = Body(PuffballPath).sharedMesh.bounds;

        Assert.That(panther.size.z / panther.size.y, Is.GreaterThanOrEqualTo(LongAtLeast),
            $"The Panther is {panther.size.z:F2} m long and {panther.size.y:F2} m tall.");
        Assert.That(panther.size.z / panther.size.x, Is.GreaterThanOrEqualTo(NarrowAtLeast),
            $"The Panther is {panther.size.z:F2} m long and {panther.size.x:F2} m wide.");
        Assert.That(frog.size.y / frog.size.x, Is.LessThanOrEqualTo(FrogAtMost));
        Assert.That(pitcher.size.y / pitcher.size.x, Is.GreaterThanOrEqualTo(PitcherAtLeast));
        Assert.That(puffball.size.y / puffball.size.x, Is.InRange(PuffballAtLeast, PuffballAtMost));
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
