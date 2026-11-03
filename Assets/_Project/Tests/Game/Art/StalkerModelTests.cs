using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-22b's Chameleon stalker, checked after Unity's importer and animation sampling.</summary>
[TestFixture]
public sealed class StalkerModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Stalker.fbx";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string MinionPath = "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx";
    private const string GeneralClips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx";
    private const float ReservedDistance = 0.12f;
    private static readonly string[] Riderless = { "root", "handslot.l", "handslot.r" };

    private static readonly Color32[] Reserved =
    {
        new Color32(0x22, 0xD3, 0xEE, 0xFF), // the player
        new Color32(0xFF, 0x4A, 0x1F, 0xFF), // danger
        new Color32(0xA8, 0x55, 0xF7, 0xFF), // Veilrot
        new Color32(0xFB, 0xBF, 0x24, 0xFF), // reward
    };

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (Object obj in _created)
        {
            Object.DestroyImmediate(obj);
        }

        _created.Clear();
    }

    // Rule 2: one skinned, smooth-shaded mesh in the crowd's budget, on M_Enemy alone.
    [Test]
    public void StalkerModel_StaysInTheCrowdsBudget()
    {
        SkinnedMeshRenderer[] skins = Model().GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(skins, Has.Length.EqualTo(1));
        Assert.That(Model().GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
        Mesh mesh = skins[0].sharedMesh;
        Assert.That(mesh.triangles.Length / 3, Is.InRange(400, 2500));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount));
        Assert.That(skins[0].sharedMaterials, Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) }));
    }

    // Rule 3: every vertex on <= 4 rig bones summing to 1, none on root; the rig as KayKit made it;
    // the tail's curl on the spine and hips alone.
    [Test]
    public void StalkerModel_EveryVertexIsWeighted()
    {
        Dictionary<string, Transform> expected = Rig(AssetDatabase.LoadAssetAtPath<GameObject>(MinionPath));
        Dictionary<string, Transform> actual = Rig(Model());
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
        Assert.That(actual.Count, Is.EqualTo(24), "Rig_Medium and its 23 bones.");

        foreach (var pair in expected)
        {
            Transform bone = actual[pair.Key];
            Assert.That(Vector3.Distance(bone.localPosition, pair.Value.localPosition), Is.LessThan(0.001f), pair.Key);
            Assert.That(Quaternion.Angle(bone.localRotation, pair.Value.localRotation), Is.LessThan(0.1f), pair.Key);
        }

        SkinnedMeshRenderer body = Body();
        Mesh mesh = body.sharedMesh;
        byte[] counts = mesh.GetBonesPerVertex().ToArray();
        BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
        Vector3[] vertices = mesh.vertices;
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        int at = 0, tail = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v}");
            bool inTail = Mathf.Abs(vertices[v].x) > 0.34f && vertices[v].y > 0.2f && vertices[v].y < 0.75f;
            tail += inTail ? 1 : 0;
            float sum = 0;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                string name = body.bones[weights[at].boneIndex].name;
                Assert.That(Riderless, Has.No.Member(name), $"vertex {v}");

                if (inTail)
                {
                    Assert.That(name, Is.EqualTo("hips").Or.EqualTo("spine"), $"tail vertex {v}");
                }

                sum += weights[at].weight;
            }

            Assert.That(sum, Is.EqualTo(1).Within(0.001f), $"vertex {v}");
        }

        Assert.That(tail, Is.GreaterThan(50), "The curl lies out beside the hips.");
    }

    // Rule 4: six Rig_Medium clips in the model, Generic, 30 fps, three loops, the root still.
    [TestCase("Idle_B", true)]
    [TestCase("Sneaking", true)]
    [TestCase("Crouching", true)]
    [TestCase("Melee_Unarmed_Attack_Punch_A", false)]
    [TestCase("Hit_A", false)]
    [TestCase("Death_A", false)]
    public void StalkerModel_ImportsGenericWithItsClips(string name, bool loops)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.optimizeGameObjects, Is.False);
        AnimationClip[] clips = Clips();
        Assert.That(clips.Select(c => c.name), Is.EquivalentTo(new[]
            { "Idle_B", "Sneaking", "Crouching", "Melee_Unarmed_Attack_Punch_A", "Hit_A", "Death_A" }));
        Assert.That(clips.Count(c => c.isLooping), Is.EqualTo(3));
        AnimationClip clip = clips.Single(c => c.name == name);
        Assert.That(clip.frameRate, Is.EqualTo(30));
        Assert.That(clip.isLooping, Is.EqualTo(loops));
        GameObject model = Instance(Model());
        Transform root = model.transform.Find("Rig_Medium/root");
        Assert.That(root, Is.Not.Null);
        Vector3 position = root.localPosition;
        Quaternion rotation = root.localRotation;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True, binding.path);
        }

        foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            clip.SampleAnimation(model, clip.length * fraction);
            Assert.That(Vector3.Distance(root.localPosition, position), Is.LessThan(0.001f), "In place.");
            Assert.That(Quaternion.Angle(root.localRotation, rotation), Is.LessThan(0.1f), "In place.");
            Mesh baked = Bake(model);
            Assert.That(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
            Assert.That(baked.bounds.size.magnitude, Is.InRange(0.5f, 4f));
        }
    }

    // Rule 5: nothing it samples comes near a reserved colour, and it does not glow at rest.
    [Test]
    public void StalkerModel_HasNoGlowAndNoReservedColour()
    {
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(atlas);
        Assert.That(atlas.LoadImage(File.ReadAllBytes(Art + "Textures/T_Enemy_Albedo.png")), Is.True);
        Material material = Body().sharedMaterial;
        Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
        Assert.That(material.GetTexture("_EmissionMap"), Is.Null);
        Mesh mesh = Body().sharedMesh;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 centre = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3;
            Color32 pixel = atlas.GetPixel((int)(centre.x * atlas.width), (int)(centre.y * atlas.height));

            foreach (Color32 reserved in Reserved)
            {
                int distance = Mathf.Max(Mathf.Abs(pixel.r - reserved.r), Mathf.Abs(pixel.g - reserved.g), Mathf.Abs(pixel.b - reserved.b));
                Assert.That(distance / 255f, Is.GreaterThan(ReservedDistance), $"triangle {i / 3} near {reserved}");
            }
        }
    }

    // Rule 6: lurking (Crouching), it stands 75-90 % of the idle Mossguard's height.
    [Test]
    public void StalkerModel_IsLowerThanTheMossguard()
    {
        GameObject guard = Instance(Load(Art + "Mossguard.fbx"));
        AssetDatabase.LoadAllAssetsAtPath(GeneralClips).OfType<AnimationClip>().Single(c => c.name == "Idle_B").SampleAnimation(guard, 0);
        float guardHeight = Bake(guard).bounds.size.y;
        GameObject stalker = Instance(Model());
        AnimationClip crouch = Clips().Single(c => c.name == "Crouching");

        foreach (float fraction in new[] { 0f, 0.5f })
        {
            crouch.SampleAnimation(stalker, crouch.length * fraction);
            Assert.That(Bake(stalker).bounds.size.y / guardHeight, Is.InRange(0.75f, 0.9f), $"at {fraction}");
        }
    }

    private static GameObject Model() => Load(ModelPath);

    private static SkinnedMeshRenderer Body() => Model().GetComponentInChildren<SkinnedMeshRenderer>(true);

    private static AnimationClip[] Clips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
        .Where(c => !c.name.StartsWith("__preview__")).ToArray();

    private static GameObject Load(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(model, Is.Not.Null, path);
        return model;
    }

    private static Dictionary<string, Transform> Rig(GameObject model) => model.GetComponentsInChildren<Transform>(true)
        .Select(t => (Transform: t, Path: AnimationUtility.CalculateTransformPath(t, model.transform)))
        .Where(p => p.Path.StartsWith("Rig_Medium")).ToDictionary(p => p.Path, p => p.Transform);

    private GameObject Instance(GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        _created.Add(instance);
        return instance;
    }

    private Mesh Bake(GameObject model)
    {
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(baked);
        model.GetComponentInChildren<SkinnedMeshRenderer>(true).BakeMesh(baked);
        return baked;
    }
}
