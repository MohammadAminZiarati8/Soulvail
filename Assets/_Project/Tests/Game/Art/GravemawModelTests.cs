using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// JG-25e: Gravemaw's body as <c>Tools/Blender/gravemaw.py</c> writes it — TRELLIS seed 43's shape in
/// seed 42's paint, on a rig of its own, the maw's throat a renderer apart.
/// </summary>
[TestFixture]
public sealed class GravemawModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/Gravemaw/";
    private const string ModelPath = Art + "Gravemaw.fbx";
    private const string AlbedoPath = Art + "T_Gravemaw_Albedo.png";
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Gravemaw.prefab";
    private const string EnemyPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Gravemaw.mat";
    private const string GroundPath = "Assets/_Project/Art/Environment/Jungle/Textures/T_JungleGround_Albedo.png";
    private const int TextureSize = 2048;

    // A boss, one on screen: the crowd's 2 800 would melt the teeth that make the maw read.
    private const int TriangleFloor = 4000;
    private const int TriangleCeiling = 12000;

    private static readonly string[] ClipNames = { "Death", "Dive", "Hit", "Idle", "Rise", "Spit", "Tremor" };

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

    // Rule 1: an Enemy variant, the base mesh parked; Body and Maw skinned on one rig under Model at
    // scale 1, about 1.6 m long, the maw toward +Z.
    [Test]
    public void Gravemaw_IsAVariantOfEnemy_TwoSkinnedRenderers()
    {
        GameObject prefab = Load<GameObject>(PrefabPath);
        Transform model = prefab.transform.Find("Model");

        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.SameAs(Load<GameObject>(EnemyPath)));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        Assert.That(model, Is.Not.Null);
        Assert.That(model.localScale, Is.EqualTo(Vector3.one));

        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(renderers.Select(r => r.name), Is.EquivalentTo(new[] { "Body", "Maw" }));
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { Load<Material>(MaterialPath) }), renderer.name);
            Assert.That(renderer.rootBone, Is.SameAs(renderers[0].rootBone), renderer.name);
        }

        GameObject instance = Instance();
        Bounds body = Baked(instance, "Body");
        Bounds maw = Baked(instance, "Maw");
        Assert.That(body.size.z, Is.InRange(1.5f, 1.7f), $"Gravemaw is {body.size.z:F2} m long.");
        Assert.That(body.size.z, Is.GreaterThan(body.size.y).And.GreaterThan(body.size.x), "Long along its path.");
        Assert.That(body.min.y, Is.InRange(-0.05f, 0.05f), "Its belly on the ground.");
        Assert.That(maw.center.z, Is.GreaterThan(body.center.z + (0.6f * body.extents.z)), "The maw leads, toward +Z.");
    }

    // Rule 2: no player cyan, nothing within ΔE 20 of Danger, and the hide under the Jungle floor.
    [Test]
    public void Gravemaw_PaintHasNoCyanAndNoDanger_HideUnderFloor()
    {
        Color32[] paint = Pixels(AlbedoPath);
        Vector3 danger = Lab(Palette.Danger);

        foreach (Color32 texel in paint.Distinct())
        {
            Color.RGBToHSV(texel, out float hue, out float saturation, out _);
            Assert.That(hue * 360 >= 175 && hue * 360 <= 200 && saturation > 0.35f, Is.False, $"{texel} is the player's cyan.");
            Assert.That(Vector3.Distance(Lab(texel), danger), Is.GreaterThanOrEqualTo(20f), $"{texel} is within ΔE 20 of Danger.");
        }

        Mesh body = Renderer(Load<GameObject>(ModelPath), "Body").sharedMesh;
        float hide = Median(body.uv.Select(uv => Luma(Texel(paint, uv))));
        float floor = Pixels(GroundPath).Average(Luma);
        Assert.That(hide, Is.LessThan(floor), $"Hide {hide:F3} against the floor's {floor:F3}.");
    }

    // Rule 3: seven Generic clips named exactly, Idle alone looping, no events; Rise and Dive half a
    // second, Rise from under the ground and Dive back under it; the root never moves.
    [Test]
    public void Gravemaw_HasTheSevenClips_NoEvents_NoRootMotion()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(Load<GameObject>(PrefabPath).GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);

        Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
        Assert.That(clips.Keys.OrderBy(n => n), Is.EqualTo(ClipNames));

        GameObject model = Instance();
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
        Vector3 rest = root.localPosition;
        Quaternion restTurn = root.localRotation;

        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.isLooping, Is.EqualTo(clip.name == "Idle"), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                    $"{clip.name} animates '{binding.path}', which Gravemaw does not have.");
            }

            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                Assert.That((root.localPosition - rest).magnitude, Is.LessThan(1e-4f), $"{clip.name} moves the root at {frame}.");
                Assert.That(Quaternion.Angle(root.localRotation, restTurn), Is.LessThan(0.01f), $"{clip.name} turns the root at {frame}.");
            }
        }

        Assert.That(clips["Rise"].length, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(clips["Dive"].length, Is.EqualTo(0.5f).Within(0.01f));
        clips["Rise"].SampleAnimation(model, 0f);
        Assert.That(Baked(model, "Body").max.y, Is.LessThan(0f), "Rise starts under the ground.");
        clips["Dive"].SampleAnimation(model, clips["Dive"].length);
        Assert.That(Baked(model, "Body").max.y, Is.LessThan(0f), "Dive ends under the ground.");
    }

    // Rule 4: the throat is a bright warm amber, off Danger, so the lit window reads.
    [Test]
    public void Gravemaw_MawIsBrightAtTheFront()
    {
        Mesh maw = Renderer(Load<GameObject>(ModelPath), "Maw").sharedMesh;
        Color32[] paint = Pixels(AlbedoPath);
        Color32[] texels = maw.uv.Select(uv => Texel(paint, uv)).ToArray();
        var mean = new Color(texels.Average(c => c.r / 255f), texels.Average(c => c.g / 255f), texels.Average(c => c.b / 255f));

        Color.RGBToHSV(mean, out float hue, out float saturation, out float value);
        Assert.That(hue * 360, Is.InRange(30f, 60f), $"Amber throat, not {mean}.");
        Assert.That(saturation, Is.GreaterThan(0.4f));
        Assert.That(value, Is.GreaterThan(0.6f), "Lit, not a dark ochre.");
        Assert.That(Load<Material>(MaterialPath).IsKeywordEnabled("_EMISSION"), Is.True, "25f lights it through emission.");
        Assert.That(Load<Material>(MaterialPath).GetColor("_EmissionColor"), Is.EqualTo(Color.black), "Dark until 25f lights it.");
    }

    // Rule 5: the boss budget, one submesh each, and every vertex on 1–4 bones, none on the root.
    [Test]
    public void Gravemaw_StaysInBudget_EveryVertexWeighted()
    {
        SkinnedMeshRenderer[] renderers = Load<GameObject>(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(renderers.Sum(r => r.sharedMesh.triangles.Length / 3), Is.InRange(TriangleFloor, TriangleCeiling));

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            Mesh mesh = renderer.sharedMesh;
            byte[] counts = mesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();

            Assert.That(mesh.subMeshCount, Is.EqualTo(1), renderer.name);
            Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount), renderer.name);
            Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount), renderer.name);

            int at = 0;
            for (int v = 0; v < counts.Length; v++)
            {
                Assert.That(counts[v], Is.InRange(1, 4), $"{renderer.name} vertex {v} is on {counts[v]} bones.");
                float sum = 0f;
                for (int k = 0; k < counts[v]; k++, at++)
                {
                    sum += weights[at].weight;
                    Assert.That(renderer.bones[weights[at].boneIndex].name, Is.Not.EqualTo("root"),
                        $"{renderer.name} vertex {v} rides the root, which no clip moves.");
                }

                Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"{renderer.name} vertex {v}'s weights sum to {sum}.");
            }
        }
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"{path}; run Tools/Blender/gravemaw.py.");

        return asset;
    }

    private static SkinnedMeshRenderer Renderer(GameObject model, string name) =>
        model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == name);

    private static float Luma(Color32 c) => ((0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b)) / 255f;

    private static Color32 Texel(Color32[] pixels, Vector2 uv) =>
        pixels[(Mathf.Clamp((int)(uv.y * TextureSize), 0, TextureSize - 1) * TextureSize) + Mathf.Clamp((int)(uv.x * TextureSize), 0, TextureSize - 1)];

    private static float Median(IEnumerable<float> values)
    {
        float[] sorted = values.OrderBy(v => v).ToArray();
        return sorted[sorted.Length / 2];
    }

    // CIE 1976 L*a*b* from sRGB, D65.
    private static Vector3 Lab(Color colour)
    {
        float r = Mathf.GammaToLinearSpace(colour.r), g = Mathf.GammaToLinearSpace(colour.g), b = Mathf.GammaToLinearSpace(colour.b);
        float x = F(((0.4124f * r) + (0.3576f * g) + (0.1805f * b)) / 0.95047f);
        float y = F((0.2126f * r) + (0.7152f * g) + (0.0722f * b));
        float z = F(((0.0193f * r) + (0.1192f * g) + (0.9505f * b)) / 1.08883f);
        return new Vector3((116 * y) - 16, 500 * (x - y), 200 * (y - z));

        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t) + (16f / 116f);
    }

    /// <summary>A live copy of the prefab's model, at the origin.</summary>
    private GameObject Instance()
    {
        var instance = (GameObject)Object.Instantiate(Load<GameObject>(PrefabPath).transform.Find("Model").gameObject);
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _created.Add(instance);

        return instance;
    }

    /// <summary>The renderer's posed mesh in the model's space.</summary>
    private Bounds Baked(GameObject model, string name)
    {
        SkinnedMeshRenderer renderer = Renderer(model, name);
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(mesh);
        renderer.BakeMesh(mesh);
        Vector3[] points = mesh.vertices.Select(p => model.transform.InverseTransformPoint(renderer.transform.TransformPoint(p))).ToArray();
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    private Color32[] Pixels(string path)
    {
        var texture = new Texture2D(2, 2);
        _created.Add(texture);
        texture.LoadImage(File.ReadAllBytes(path));
        return texture.GetPixels32();
    }
}
