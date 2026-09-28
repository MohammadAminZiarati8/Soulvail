using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// The Rootling's model and the shared enemy atlas, as <c>Tools/Blender/rootling.py</c> writes them
/// and the importer reads them (M7-05f).
/// </summary>
/// <remarks>
/// The model is generated, so these rows are what stops a changed script — or an import setting
/// reset by an update — shipping a body that has left <c>Rig_Medium</c>, blown the crowd's budget,
/// turned green on a green floor or taken a colour GD §16.4 reserves. Its rig is compared with
/// <c>Skeleton_Minion.fbx</c>'s, the KayKit enemy the script reads it from, because a Generic clip
/// binds by transform path and plays a bone at whatever offset the clip's own rig had.
/// </remarks>
[TestFixture]
public sealed class RootlingModelTests
{
    private const string ModelPath = "Assets/_Project/Art/Enemies/Rootling.fbx";
    private const string AtlasPath = "Assets/_Project/Art/Enemies/Textures/T_Enemy_Albedo.png";
    private const string MinionPath = "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx";
    private const string ClipFolder = "Assets/ThirdParty/KayKit/Animations/Rig_Medium";
    private const string EnemyMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy.mat";
    private const string DissolveMaterialPath = "Assets/_Project/Materials/Enemies/M_Enemy_Dissolve.mat";

    /// <summary>GD §17.1's crowd budget.</summary>
    private const int MinTriangles = 400;

    private const int MaxTriangles = 1200;

    /// <summary>
    /// How light the Rootling must be on average — Rec. 709 luma over the atlas's stored values.
    /// The Jungle's floor sits near 0.4 in the same measure, so a body at 0.55 or more stands off it.
    /// </summary>
    private const float MinLuminance = 0.55f;

    /// <summary>HSV saturation, averaged: bone, fungus and dry bark, not moss-green.</summary>
    private const float MaxSaturation = 0.25f;

    /// <summary>
    /// How close in any one channel a pixel may come to a reserved colour. The script refuses the
    /// same distance when it writes the atlas; this is the check at the other end (AR §18.3).
    /// </summary>
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
    public void RootlingModel_IsShipped()
    {
        // Every row below reads this model; a missing one would fail them all for the wrong reason.
        Assert.That(Model().GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void RootlingModel_WearsRigMedium()
    {
        GameObject model = Model();
        GameObject minion = AssetDatabase.LoadAssetAtPath<GameObject>(MinionPath);

        Dictionary<string, Transform> ours = Rig(model);
        Dictionary<string, Transform> theirs = Rig(minion);

        Assert.That(ours.Keys, Is.EquivalentTo(theirs.Keys),
            "The same bones under the same names at the same depth — Rig_Medium, unchanged.");

        foreach (KeyValuePair<string, Transform> pair in theirs)
        {
            Transform bone = ours[pair.Key];

            Assert.That((bone.localPosition - pair.Value.localPosition).magnitude, Is.LessThan(0.001f),
                $"{pair.Key} sits {bone.localPosition} where Rig_Medium has {pair.Value.localPosition}; " +
                "a clip would pull it back and drag the mesh with it.");
            Assert.That(Quaternion.Angle(bone.localRotation, pair.Value.localRotation), Is.LessThan(0.1f), pair.Key);
            Assert.That((bone.localScale - pair.Value.localScale).magnitude, Is.LessThan(0.001f), pair.Key);
        }
    }

    [Test]
    public void RootlingModel_EveryRigMediumClipBinds()
    {
        GameObject model = Model();
        int clips = 0;

        foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { ClipFolder }).Select(AssetDatabase.GUIDToAssetPath))
        {
            foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            {
                if (clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                clips++;

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    Assert.That(binding.path.Length == 0 || model.transform.Find(binding.path) != null, Is.True,
                        $"{clip.name} animates '{binding.path}', which the Rootling does not have.");
                }
            }
        }

        Assert.That(clips, Is.GreaterThan(100), "Sanity: the eight Rig_Medium files were found.");
    }

    [Test]
    public void RootlingModel_StaysInTheCrowdsBudget()
    {
        Mesh mesh = Body().sharedMesh;

        Assert.That(mesh.triangles.Length / 3, Is.InRange(MinTriangles, MaxTriangles));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1), "One material slot: one draw.");
    }

    [Test]
    public void RootlingModel_UsesTheEnemyMaterial()
    {
        // GD §17.1's one material across all enemies: the importer remaps the script's placeholder
        // "enemy", and a body that kept the embedded one would render glossy and batch alone.
        Assert.That(Body().sharedMaterials, Is.EqualTo(new[] { AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath) }));
    }

    [Test]
    public void RootlingModel_EveryVertexIsWeighted()
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

                string bone = body.bones[weights[at].boneIndex].name;

                Assert.That(Riderless, Has.No.Member(bone),
                    $"vertex {v} rides {bone}, which carries nothing on a KayKit body.");
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-3f), $"vertex {v}'s weights sum to {sum}");
        }
    }

    [Test]
    public void RootlingModel_IsPale()
    {
        // Averaged over its surface, the colour the Rootling samples: light and unsaturated, so it
        // reads against the Jungle's green floor (the owner's direction of 2026-09-27). Each
        // triangle samples its swatch at its centroid and counts for its area.
        Mesh mesh = Body().sharedMesh;
        Texture2D atlas = Atlas();
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
            luminance += weight * ((0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b));
            saturation += weight * s;
        }

        Assert.That(luminance / area, Is.GreaterThanOrEqualTo(MinLuminance), "The Rootling is dark.");
        Assert.That(saturation / area, Is.LessThanOrEqualTo(MaxSaturation), "The Rootling is saturated.");
    }

    [Test]
    public void EnemyAtlas_CarriesNoReservedColour()
    {
        Color32[] pixels = Atlas().GetPixels32();

        foreach (Color32 reserved in Reserved)
        {
            int nearest = pixels.Min(p => Mathf.Max(Mathf.Abs(p.r - reserved.r), Mathf.Abs(p.g - reserved.g), Mathf.Abs(p.b - reserved.b)));

            Assert.That(nearest / 255f, Is.GreaterThan(ReservedDistance),
                $"A pixel comes within {nearest / 255f:F3} of {reserved}, which GD §16.4 reserves.");
        }
    }

    [Test]
    public void RootlingModel_ImportsGenericWithoutAnimation()
    {
        // KayKit's own rigs' settings (VERSIONS.md): Generic, so no per-frame retargeting on 28
        // bodies; not optimised, so the transform paths the clips bind to exist; no clips of its own.
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
        Assert.That(importer.importAnimation, Is.False);
        Assert.That(importer.optimizeGameObjects, Is.False);
    }

    [Test]
    public void EnemyAtlas_IsPointFiltered()
    {
        // KayKit's rule for palette atlases (VERSIONS.md): bilinear bleeds one swatch into the next.
        Assert.That(((TextureImporter)AssetImporter.GetAtPath(AtlasPath)).filterMode, Is.EqualTo(FilterMode.Point));
    }

    [Test]
    public void EnemyMaterial_CanGlow()
    {
        // Emission on and black at rest, so EnemyHitFeedback's property block can flash a textured
        // body white and glow it red-orange over a wind-up (M7-05h). A texture multiplied by
        // _BaseColor cannot flash white; emission can.
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        foreach (string path in new[] { EnemyMaterialPath, DissolveMaterialPath })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            Assert.That(material, Is.Not.Null, path);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"), path);
            Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(atlas), path);
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True, path);

            // What keeps the keyword: URP's material validation switches _EMISSION off on import,
            // and whenever the material is opened in the Inspector, unless the GI flags carry an
            // emissive bit — EmissiveIsBlack and None both lose it at a black colour (Traps §5).
            Assert.That(material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive,
                Is.Not.EqualTo(MaterialGlobalIlluminationFlags.None), $"{path} would lose _EMISSION to URP.");

            Color emission = material.GetColor("_EmissionColor");

            Assert.That(emission.r + emission.g + emission.b, Is.Zero, $"{path} glows at rest.");
        }

        Assert.That(AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath).GetFloat("_Surface"), Is.Zero, "Opaque.");
        Assert.That(AssetDatabase.LoadAssetAtPath<Material>(DissolveMaterialPath).GetFloat("_Surface"), Is.EqualTo(1f),
            "The dissolve's twin is transparent, or an alpha in the property block would fade nothing.");
    }

    private static GameObject Model()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Assert.That(model, Is.Not.Null, $"No model at {ModelPath}. Run Tools/Blender/rootling.py.");

        return model;
    }

    private static SkinnedMeshRenderer Body() => Model().GetComponentInChildren<SkinnedMeshRenderer>(true);

    /// <summary>Every transform from <c>Rig_Medium</c> down, keyed by its path from the model's root.</summary>
    private static Dictionary<string, Transform> Rig(GameObject model) =>
        model.GetComponentsInChildren<Transform>(true)
            .Select(t => (Transform: t, Path: AnimationUtility.CalculateTransformPath(t, model.transform)))
            .Where(p => p.Path.StartsWith("Rig_Medium"))
            .ToDictionary(p => p.Path, p => p.Transform);

    /// <summary>
    /// The atlas as the script wrote it, decoded from the file rather than read from the imported
    /// texture — which is compressed and not readable, and is not what this rule is about.
    /// </summary>
    private Texture2D Atlas()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        _created.Add(texture);

        Assert.That(texture.LoadImage(File.ReadAllBytes(AtlasPath)), Is.True, AtlasPath);

        texture.filterMode = FilterMode.Point;

        return texture;
    }
}
