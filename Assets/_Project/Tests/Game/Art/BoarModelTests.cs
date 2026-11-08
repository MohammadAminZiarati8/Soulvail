using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>PL-05b: the imported Boar mesh, paint and clips.</summary>
[TestFixture]
public sealed class BoarModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = "Assets/_Project/Art/Pets/Boar.fbx";
    private const string PantherPath = Art + "Panther.fbx";
    private const string PrefabPath = "Assets/_Project/Prefabs/Pets/Boar.prefab";
    private const string MaterialPath = "Assets/_Project/Materials/Pets/M_Boar.mat";
    private const string BoarAtlasPath = "Assets/_Project/Art/Pets/Textures/T_Boar_Albedo.png";
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
    public void Boar_OneModelChild_PantherBones()
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
        Transform[] expected = Body(Load<GameObject>(PantherPath)).bones;
        Transform[] actual = bodies[0].bones;
        Assert.That(actual.Select(b => b.name), Is.EquivalentTo(expected.Select(b => b.name)));
        foreach (Transform bone in expected)
        {
            Assert.That(actual.Single(b => b.name == bone.name).parent.name, Is.EqualTo(bone.parent.name), bone.name);
        }

        Transform head = actual.Single(b => b.name == "head");
        Transform chest = actual.Single(b => b.name == "chest");
        Assert.That(head.position.y, Is.LessThan(chest.position.y), "Low head.");
        foreach (Transform ankle in actual.Where(b => b.name.StartsWith("paw.")))
        {
            Assert.That(ankle.position.y, Is.LessThan(chest.position.y * 0.25f), ankle.name);
        }

        Material material = Load<Material>(MaterialPath);
        Assert.That(bodies[0].sharedMaterials, Is.EqualTo(new[] { material }));
        Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(Load<Texture2D>(BoarAtlasPath)));
    }

    [Test]
    public void Paint_HasPlayerCyan_NoDangerGoldLime()
    {
        Texture2D atlas = Decode(BoarAtlasPath);
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

        AssertCyanOnBothFlanks(atlas);
    }

    private static void AssertCyanOnBothFlanks(Texture2D atlas)
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Mesh mesh = body.sharedMesh;
        var cyan = (Color32)Palette.Player;
        bool positive = false, negative = false;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        for (int i = 0; i < uv.Length; i++)
        {
            int x = Mathf.Clamp((int)(uv[i].x * atlas.width), 0, atlas.width - 1);
            int y = Mathf.Clamp((int)(uv[i].y * atlas.height), 0, atlas.height - 1);
            if (!((Color32)atlas.GetPixel(x, y)).Equals(cyan))
            {
                continue;
            }

            float side = body.transform.TransformPoint(vertices[i]).x;
            positive |= side > 0.1f;
            negative |= side < -0.1f;
        }

        Assert.That(positive && negative, Is.True, "Player cyan must be painted on both flanks.");
    }

    [Test]
    public void Boar_HasChargeAndIdle_NoEvents_NoRootMotion()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(Load<GameObject>(PrefabPath).GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);
        Dictionary<string, AnimationClip> clips = Clips();
        Assert.That(clips.Keys, Is.EquivalentTo(new[] { "Idle", "Charge" }));
        GameObject model = Instance();
        Transform root = Bone(model, "root");
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;
        Vector3 scale = root.localScale;
        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.isLooping, Is.True, clip.name);
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
    public void Boar_HalfCrowdBudget_EveryVertexWeighted()
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Mesh mesh = body.sharedMesh;
        Assert.That(mesh.triangles.Length / 3, Is.LessThan(2500 / 2), "Half the Frog crowd budget.");
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
