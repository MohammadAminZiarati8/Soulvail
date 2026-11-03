using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>JG-24b's Thornweaver, checked after Unity's importer and animation sampling.</summary>
[TestFixture]
public sealed class ThornweaverModelTests
{
    private const string Art = "Assets/_Project/Art/Enemies/";
    private const string ModelPath = Art + "Thornweaver.fbx";
    private const string MaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string MinionPath = "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx";
    private const string GeneralClips = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx";
    private const float ReservedDistance = 0.12f;

    // enemy_atlas.py's pitcher_skin cell (column 4, row 0): the crown's swatch, the one JG-24c lights.
    private const float CrownU = (4 * 64 + 32) / 512f;
    private const float CrownVMin = 1 - 128 / 512f;
    private static readonly string[] Riderless = { "root", "handslot.l", "handslot.r" };

    private static readonly string[] ClipNames =
        { "Idle_B", "Walking_B", "Ranged_Magic_Spellcasting", "Hit_A", "Death_A" };

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
    public void ThornweaverModel_StaysInTheCrowdsBudget()
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

    // Rule 3: every vertex on <= 4 rig bones summing to 1, none on root; the rig as KayKit made it.
    [Test]
    public void ThornweaverModel_EveryVertexIsWeighted()
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
        Assert.That(counts, Has.Length.EqualTo(mesh.vertexCount));
        int at = 0;

        for (int v = 0; v < counts.Length; v++)
        {
            Assert.That(counts[v], Is.InRange(1, 4), $"vertex {v}");
            float sum = 0;

            for (int k = 0; k < counts[v]; k++, at++)
            {
                Assert.That(Riderless, Has.No.Member(body.bones[weights[at].boneIndex].name), $"vertex {v}");
                sum += weights[at].weight;
            }

            Assert.That(sum, Is.EqualTo(1).Within(0.001f), $"vertex {v}");
        }
    }

    // Rule 4: five Rig_Medium clips in the model, Generic, 30 fps, two loops, the root still.
    [TestCase("Idle_B", true)]
    [TestCase("Walking_B", true)]
    [TestCase("Ranged_Magic_Spellcasting", false)]
    [TestCase("Hit_A", false)]
    [TestCase("Death_A", false)]
    public void ThornweaverModel_ImportsGenericWithItsClips(string name, bool loops)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.optimizeGameObjects, Is.False);
        AnimationClip[] clips = Clips();
        Assert.That(clips.Select(c => c.name), Is.EquivalentTo(ClipNames));
        Assert.That(clips.Count(c => c.isLooping), Is.EqualTo(2));
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

    // Rule 5: nothing it samples comes near a reserved colour, it does not glow at rest, and the
    // one swatch JG-24c may light is worn by the crown alone.
    [Test]
    public void ThornweaverModel_CrownIsTheOnlyGlow()
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
        Mesh rest = Bake(Instance(Model()));
        Vector3[] vertices = rest.vertices;
        float brow = rest.bounds.max.y * 0.82f;
        int crown = 0;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 centre = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3;
            Color32 pixel = atlas.GetPixel((int)(centre.x * atlas.width), (int)(centre.y * atlas.height));

            foreach (Color32 reserved in Reserved)
            {
                int distance = Mathf.Max(Mathf.Abs(pixel.r - reserved.r), Mathf.Abs(pixel.g - reserved.g), Mathf.Abs(pixel.b - reserved.b));
                Assert.That(distance / 255f, Is.GreaterThan(ReservedDistance), $"triangle {i / 3} near {reserved}");
            }

            if (Mathf.Abs(centre.x - CrownU) < 0.01f && centre.y > CrownVMin)
            {
                crown++;

                for (int k = 0; k < 3; k++)
                {
                    Assert.That(vertices[triangles[i + k]].y, Is.GreaterThan(brow), $"crown triangle {i / 3}");
                }
            }
        }

        Assert.That(crown, Is.GreaterThan(50), "The crown wears its swatch.");
    }

    // Rule 6: in idle it stands over the Shaman and within 100-115 % of the Mossguard.
    [Test]
    public void ThornweaverModel_StandsAtMossguardHeight()
    {
        GameObject guard = Instance(Load(Art + "Mossguard.fbx"));
        AssetDatabase.LoadAllAssetsAtPath(GeneralClips).OfType<AnimationClip>().Single(c => c.name == "Idle_B").SampleAnimation(guard, 0);
        float guardHeight = Bake(guard).bounds.size.y;
        GameObject shaman = Instance(Load(Art + "Shaman.fbx"));
        AssetDatabase.LoadAllAssetsAtPath(Art + "Shaman.fbx").OfType<AnimationClip>().Single(c => c.name == "Idle_B").SampleAnimation(shaman, 0);
        float shamanHeight = Bake(shaman).bounds.size.y;
        GameObject weaver = Instance(Model());
        Clips().Single(c => c.name == "Idle_B").SampleAnimation(weaver, 0);
        float height = Bake(weaver).bounds.size.y;
        Assert.That(height, Is.GreaterThan(shamanHeight));
        Assert.That(height / guardHeight, Is.InRange(1f, 1.15f));
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
