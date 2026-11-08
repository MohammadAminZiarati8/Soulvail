using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>PL-04b: the imported Hawk mesh, paint and clips.</summary>
[TestFixture]
public sealed class HawkModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Pets/Hawk.fbx";
    private const string PrefabPath = "Assets/_Project/Prefabs/Pets/Hawk.prefab";
    private const string MaterialPath = "Assets/_Project/Materials/Pets/M_Hawk.mat";
    private const string HawkAtlasPath = "Assets/_Project/Art/Pets/Textures/T_Hawk_Albedo.png";
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
    public void Hawk_OneModelChild_RigHawkBones()
    {
        GameObject prefab = Load<GameObject>(PrefabPath);
        Assert.That(prefab.transform.childCount, Is.EqualTo(1));
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
        Transform model = prefab.transform.Find("Model");
        Assert.That(model, Is.Not.Null);
        Assert.That(model.localScale, Is.EqualTo(Vector3.one));
        Assert.That(model.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(model.localRotation, Is.EqualTo(Quaternion.identity));
        SkinnedMeshRenderer[] bodies = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(bodies, Has.Length.EqualTo(1));
        Assert.That(bodies[0].transform.IsChildOf(model), Is.True);
        Material material = Load<Material>(MaterialPath);
        Assert.That(bodies[0].sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(Load<Texture2D>(HawkAtlasPath)));
        Transform rig = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Rig_Hawk");
        Assert.That(rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig).Select(t => t.name),
            Is.EquivalentTo(new[] { "root", "body", "neck", "head", "wing.l.1", "wing.l.2", "wing.r.1", "wing.r.2", "tail", "leg.l", "leg.r" }));
        Assert.That(rig.Find("root"), Is.Not.Null);
    }

    [Test]
    public void Paint_HasPlayerCyan_NoDangerGoldLime()
    {
        Texture2D atlas = Decode(HawkAtlasPath);
        Color32[] texels = atlas.GetPixels32();
        var player = (Color32)Palette.Player;
        Assert.That(texels.Count(texel => texel.Equals(player)), Is.GreaterThanOrEqualTo(texels.Length * 0.02f));
        Vector3 danger = Lab(Palette.Danger);
        foreach (Color32 texel in texels.Distinct())
        {
            Color.RGBToHSV(texel, out float hue, out float saturation, out float value);
            float degrees = hue * 360f;
            bool goldOrLime = degrees >= 35f && degrees <= 105f && saturation > 0.35f && value > 0.35f;
            Assert.That(goldOrLime, Is.False, texel.ToString());
            Assert.That(Vector3.Distance(Lab(texel), danger), Is.GreaterThanOrEqualTo(20f), texel.ToString());
        }

        Color32[] paint = Body(Load<GameObject>(ModelPath)).sharedMesh.uv
            .Select(uv => (Color32)atlas.GetPixel((int)(uv.x * atlas.width), (int)(uv.y * atlas.height)))
            .ToArray();
        Assert.That(paint, Does.Contain(player));
    }

    [Test]
    public void Wingspan_InBand()
    {
        Bounds bounds = Bake(Instance());
        Assert.That(bounds.size.x, Is.InRange(1.4f, 1.8f));
    }

    [Test]
    public void Hawk_HasTheFiveClips_NoEvents_NoRootMotion()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(Load<GameObject>(PrefabPath).GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);
        Dictionary<string, AnimationClip> clips = Clips();
        Assert.That(clips.Keys, Is.EquivalentTo(new[] { "Glide", "Flap", "Dive", "Strike", "Climb" }));
        GameObject model = Instance();
        Transform root = Bone(model, "root");
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;
        Vector3 scale = root.localScale;
        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.isLooping, Is.EqualTo(new[] { "Glide", "Flap" }.Contains(clip.name)), clip.name);
            Assert.That(AnimationUtility.GetCurveBindings(clip).Length, Is.GreaterThan(0), clip.name);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True, $"{clip.name}: {binding.path}");
            }

            for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
            {
                clip.SampleAnimation(model, frame / clip.frameRate);
                Assert.That(Vector3.Distance(root.localPosition, position), Is.LessThan(0.0001f), clip.name);
                Assert.That(Quaternion.Angle(root.localRotation, rotation), Is.LessThan(0.01f), clip.name);
                Assert.That(Vector3.Distance(root.localScale, scale), Is.LessThan(0.0001f), clip.name);
            }
        }
    }

    [Test]
    public void Hawk_InBudget_EveryVertexWeighted()
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Mesh mesh = body.sharedMesh;
        Assert.That(mesh.triangles.Length / 3, Is.LessThan(1250), "Half the crowd budget.");
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount));
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        int at = 0;
        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v}");
            float sum = 0;
            for (int k = 0; k < counts[v]; k++, at++)
            {
                BoneWeight1 weight = weights[at];
                Assert.That(float.IsFinite(weight.weight) && weight.weight > 0, Is.True);
                Assert.That(weight.boneIndex, Is.InRange(0, body.bones.Length - 1));
                Assert.That(body.bones[weight.boneIndex].name, Is.Not.EqualTo("root"));
                sum += weight.weight;
            }

            Assert.That(sum, Is.EqualTo(1f).Within(0.001f), $"vertex {v}");
        }
    }

    private static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private static SkinnedMeshRenderer Body(GameObject model) => model.GetComponentInChildren<SkinnedMeshRenderer>(true);
    private static Transform Bone(GameObject model, string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    private static Dictionary<string, AnimationClip> Clips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
        .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);

    private GameObject Instance()
    {
        var instance = Object.Instantiate(Load<GameObject>(ModelPath));
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
        Assert.That(points.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), Is.True);
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    private Texture2D Decode(string path)
    {
        var texture = new Texture2D(2, 2);
        _created.Add(texture);
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True);
        texture.filterMode = FilterMode.Point;
        return texture;
    }

    // CIE 1976 L*a*b*, sRGB D65, matching the shipped Gravemaw paint check.
    private static Vector3 Lab(Color colour)
    {
        float r = Mathf.GammaToLinearSpace(colour.r), g = Mathf.GammaToLinearSpace(colour.g), b = Mathf.GammaToLinearSpace(colour.b);
        float x = F(((0.4124f * r) + (0.3576f * g) + (0.1805f * b)) / 0.95047f);
        float y = F((0.2126f * r) + (0.7152f * g) + (0.0722f * b));
        float z = F(((0.0193f * r) + (0.1192f * g) + (0.9505f * b)) / 1.08883f);
        return new Vector3((116 * y) - 16, 500 * (x - y), 200 * (y - z));

        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t) + (16f / 116f);
    }
}
