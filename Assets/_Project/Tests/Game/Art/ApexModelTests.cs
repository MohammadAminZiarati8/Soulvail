using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-30d: the shipped matriarch, after FBX import, skinning and clip sampling.</summary>
[TestFixture]
public sealed class ApexModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Apex.fbx";
    private const string PantherPath = Art + "Panther.fbx";
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Apex.prefab";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string AtlasPath = Art + "Textures/T_Enemy_Albedo.png";
    private const string FloorPath = "Assets/_Project/Art/Environment/Jungle/Textures/T_JungleGround_Albedo.png";
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
    public void Apex_IsAVariantOfEnemy_OneModelChild()
    {
        GameObject prefab = Load<GameObject>(PrefabPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab) ==
            Load<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy.prefab"), Is.True);
        Assert.That(prefab.transform.Cast<Transform>().Count(t => t.name == "Model"), Is.EqualTo(1));
        Transform model = prefab.transform.Find("Model");
        Assert.That(model.localScale, Is.EqualTo(Vector3.one));
        Assert.That(model.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(Quaternion.Angle(model.localRotation, Quaternion.identity), Is.LessThan(0.01f));
        Assert.That(prefab.transform.Find("Mesh").gameObject.activeSelf, Is.False);
        Assert.That(model.GetComponent<Animator>().runtimeAnimatorController, Is.EqualTo(Load<RuntimeAnimatorController>("Assets/_Project/Animation/Controllers/AC_Apex.controller")));
    }

    [Test]
    public void Apex_OneSkinnedBody()
    {
        GameObject prefab = Load<GameObject>(PrefabPath);
        SkinnedMeshRenderer[] bodies = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(bodies, Has.Length.EqualTo(1));
        Assert.That(bodies[0].transform.IsChildOf(prefab.transform.Find("Model")), Is.True);
        Assert.That(bodies[0].sharedMaterials, Is.EqualTo(new[] { Load<Material>(MaterialPath) }));
        Assert.That(bodies[0].sharedMesh, Is.EqualTo(Body(Load<GameObject>(ModelPath)).sharedMesh));
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer && !renderer.name.StartsWith("Eye"))
            {
                Assert.That(renderer.gameObject.activeSelf, Is.False, "The base capsule stays parked.");
            }
        }
    }

    [Test]
    public void Apex_RigHasPantherBones_AndHead()
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Transform[] expected = Body(Load<GameObject>(PantherPath)).bones;
        Assert.That(body.bones.Select(b => b.name), Is.EquivalentTo(expected.Select(b => b.name).Append("Head")));
        foreach (Transform bone in expected)
        {
            Transform actual = body.bones.Single(b => b.name == bone.name);
            Assert.That(actual.parent.name, Is.EqualTo(bone.parent.name), bone.name);
        }

        Transform head = body.bones.Single(b => b.name == "Head");
        Assert.That(head.parent.name, Is.EqualTo("head"));
        Assert.That(Vector3.Distance(head.position, head.parent.position), Is.LessThan(0.001f));
        Assert.That(body.bones.Single(b => b.name == "root").parent.name, Is.EqualTo("Rig_Panther"));
    }

    [Test]
    public void Apex_PaintHasNoCyanAndNoDanger()
    {
        Texture2D atlas = Decode(AtlasPath);
        Vector3 danger = Lab(Palette.Danger);
        // Scan every texel, including the full gradients between the values the mesh samples.
        foreach (Color32 texel in atlas.GetPixels32().Distinct())
        {
            Color.RGBToHSV(texel, out float hue, out float saturation, out _);
            Assert.That(hue * 360 >= 175 && hue * 360 <= 200 && saturation > 0.35f, Is.False, texel.ToString());
            Assert.That(Vector3.Distance(Lab(texel), danger), Is.GreaterThanOrEqualTo(20f), texel.ToString());
        }

        Mesh mesh = Body(Load<GameObject>(ModelPath)).sharedMesh;
        Vector2[] uv = mesh.uv;
        Assert.That(uv.All(p => Mathf.Abs(p.x - (120f / 512)) < 0.001f ||
            Mathf.Abs(p.x - (72f / 512)) < 0.001f || Mathf.Abs(p.x - (224f / 512)) < 0.001f), Is.True);
        Color[] coat = uv.Where(p => Mathf.Abs(p.x - (120f / 512)) < 0.001f).Select(p => atlas.GetPixelBilinear(p.x, p.y)).ToArray();
        Assert.That(coat.Length, Is.GreaterThan(mesh.vertexCount / 2), "Charcoal is the dominant paint.");
        float floor = Decode(FloorPath).GetPixels().Average(Luma);
        Assert.That(coat.Max(Luma), Is.LessThan(floor), "Every coat shade stays under the Jungle floor.");
        Assert.That(coat.Average(Luma), Is.GreaterThan(0.14f), "Dark charcoal, not a black hole.");
        Assert.That(uv.Count(p => Mathf.Abs(p.x - (72f / 512)) < 0.001f), Is.GreaterThan(30), "Pale scars and muzzle survive reduction.");
        Color[] eyes = uv.Where(p => p.x > 0.3f).Select(p => atlas.GetPixelBilinear(p.x, p.y)).ToArray();
        Assert.That(eyes.Length, Is.GreaterThan(0));
        Assert.That(eyes.Max(Luma), Is.LessThan(0.12f), "Only the later view lights the eyes.");
    }

    [Test]
    public void Apex_HasTheTenClips_NoEvents_NoRootMotion()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False);
        Assert.That(importer.motionNodeName, Is.Empty);
        Assert.That(Load<GameObject>(PrefabPath).GetComponentInChildren<Animator>(true).applyRootMotion, Is.False);
        Dictionary<string, AnimationClip> clips = Clips();
        Assert.That(clips.Keys, Is.EquivalentTo(new[] { "Idle", "Run", "Crouch", "Pounce", "Land", "Dash", "Rake", "Roar", "Hit", "Death" }));
        GameObject model = Instance();
        Transform root = Bone(model, "root");
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;
        Vector3 scale = root.localScale;
        foreach (AnimationClip clip in clips.Values)
        {
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, clip.name);
            Assert.That(clip.frameRate, Is.EqualTo(30f), clip.name);
            Assert.That(clip.isLooping, Is.EqualTo(new[] { "Idle", "Run", "Dash" }.Contains(clip.name)), clip.name);
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

            Bounds bounds = Bake(model);
            Assert.That(float.IsFinite(bounds.size.sqrMagnitude), Is.True, clip.name);
            Assert.That(bounds.size.magnitude, Is.InRange(1.5f, 3.5f), clip.name);
        }
    }

    [Test]
    public void Apex_CrouchIsHoldable_RakeSwipesTwiceInOneSecond()
    {
        GameObject model = Instance();
        Dictionary<string, AnimationClip> clips = Clips();
        Transform hips = Bone(model, "hips");
        clips["Idle"].SampleAnimation(model, 0);
        float standing = hips.position.y;
        clips["Crouch"].SampleAnimation(model, 0.3f);
        float held = hips.position.y;
        Assert.That(held, Is.LessThan(standing - 0.08f));
        clips["Crouch"].SampleAnimation(model, clips["Crouch"].length);
        Assert.That(hips.position.y, Is.EqualTo(held).Within(0.001f), "Hold the low body after the wind-up.");
        Assert.That(clips["Rake"].length, Is.EqualTo(1f).Within(0.001f));
        clips["Rake"].SampleAnimation(model, 9f / 30);
        Assert.That(Bone(model, "paw.front.l").position.y,
            Is.GreaterThan(Bone(model, "paw.front.r").position.y + 0.04f), "First swipe lifts the left paw.");
        clips["Rake"].SampleAnimation(model, 20f / 30);
        Assert.That(Bone(model, "paw.front.r").position.y,
            Is.GreaterThan(Bone(model, "paw.front.l").position.y + 0.04f), "Second swipe lifts the right paw.");
    }

    [Test]
    public void Apex_StaysInBudget_EveryVertexWeighted()
    {
        SkinnedMeshRenderer body = Body(Load<GameObject>(ModelPath));
        Mesh mesh = body.sharedMesh;
        Assert.That(mesh.triangles.Length / 3, Is.InRange(1200, 2200), "Mossguard's body budget.");
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
                Assert.That(body.bones[weight.boneIndex].name, Is.Not.EqualTo("root").And.Not.EqualTo("Head"));
                sum += weight.weight;
            }

            Assert.That(sum, Is.EqualTo(1f).Within(0.001f), $"vertex {v}");
        }
    }

    [Test]
    public void Apex_AtPlannedDataScale_IsHeavierThanPanther()
    {
        Bounds apex = Bake(Instance());
        var panther = Object.Instantiate(Load<GameObject>(PantherPath));
        _created.Add(panther);
        Bounds other = Bake(panther);
        Assert.That(apex.min.y, Is.InRange(-0.02f, 0.02f));
        Assert.That(apex.size.z, Is.InRange(2f, 2.4f));
        // 30f owns the actual 1.6 BodyScale; this preview comparison does not set it on the prefab.
        Assert.That(apex.size.x * 1.6f, Is.GreaterThan(other.size.x * 1.5f));
        Assert.That(apex.size.y * 1.6f, Is.GreaterThan(other.size.y * 1.6f));
        Assert.That(apex.size.z * 1.6f, Is.GreaterThan(other.size.z * 1.5f));
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

    private static float Luma(Color c) => (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);

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
