using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// The Jungle's kit, as <c>Tools/Blender/jungle_kit.py</c> writes it and the importer reads it.
/// </summary>
/// <remarks>
/// The models are generated, so these rows are what stops a changed script — or an import setting
/// reset by an update — shipping a model that samples a second atlas, stands on its side or blows
/// the budget. A new model is covered by being added to the folder.
/// </remarks>
[TestFixture]
public sealed class JungleKitTests
{
    private const string KitFolder = "Assets/_Project/Art/Environment/Jungle";
    private const string JungleMaterial = "Assets/_Project/Materials/Environment/M_Jungle.mat";

    /// <summary>
    /// GD §17.1's 400-1,200 is the crowd's budget; a dressing piece may reach this, and the vine
    /// curtains, the densest pieces, sit at 1,190.
    /// </summary>
    private const int TriangleBudget = 1500;

    private static IEnumerable<string> ModelPaths() =>
        AssetDatabase.FindAssets("t:Model", new[] { KitFolder }).Select(AssetDatabase.GUIDToAssetPath);

    [Test]
    public void JungleKit_IsShipped()
    {
        // Every row below is driven by this list; an empty one would pass them all by measuring
        // nothing.
        Assert.That(ModelPaths(), Is.Not.Empty);
    }

    [TestCaseSource(nameof(ModelPaths))]
    public void JungleModel_UsesTheJungleMaterialAlone(string path)
    {
        // One atlas for the whole kit, KayKit's forest pieces included (GD §17.1): the importer
        // remaps the script's placeholder "jungle" material, and a model that kept the embedded
        // one would render glossy and batch alone.
        var expected = AssetDatabase.LoadAssetAtPath<Material>(JungleMaterial);

        foreach (Renderer renderer in Model(path).GetComponentsInChildren<Renderer>(true))
        {
            Assert.That(renderer.sharedMaterials, Is.All.EqualTo(expected), renderer.name);
        }
    }

    [TestCaseSource(nameof(ModelPaths))]
    public void JungleModel_StaysInItsBudget(string path)
    {
        int triangles = Model(path).GetComponentsInChildren<MeshFilter>(true)
            .Sum(filter => filter.sharedMesh.triangles.Length / 3);

        Assert.That(triangles, Is.LessThanOrEqualTo(TriangleBudget));
    }

    [TestCaseSource(nameof(ModelPaths))]
    public void JungleModel_ImportsWithoutARootTransform(string path)
    {
        // Blender is Z-up. The export bakes the conversion into the vertices; without it every
        // model arrives under a -90° root rotation that an arena's placement would inherit.
        GameObject model = Model(path);

        Assert.That(model.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(model.transform.localScale, Is.EqualTo(Vector3.one));
    }

    [TestCase("JungleTree_A")]
    [TestCase("JungleTree_B")]
    public void JungleTree_StandsUp(string name)
    {
        // A tree is several times taller than it is deep, so a conversion baked the wrong way —
        // identity root, vertices on their side — shows here and nowhere else.
        Bounds bounds = Model($"{KitFolder}/{name}.fbx").GetComponentsInChildren<MeshFilter>(true)[0].sharedMesh.bounds;

        Assert.That(bounds.size.y, Is.GreaterThan(bounds.size.z));
        Assert.That(bounds.min.y, Is.GreaterThan(-0.5f), "It stands on its origin.");
    }

    [TestCase("T_Jungle_Albedo")]
    [TestCase("T_JungleRuins_Albedo")]
    public void JungleAtlas_IsPointFiltered(string texture)
    {
        // KayKit's rule for palette atlases (VERSIONS.md): bilinear bleeds one swatch into the next.
        Assert.That(Importer(texture).filterMode, Is.EqualTo(FilterMode.Point));
    }

    [Test]
    public void JungleGround_Repeats()
    {
        // The ground tiles across a 100 m plane; clamped, it would be one stretched tile.
        TextureImporter importer = Importer("T_JungleGround_Albedo");

        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
        Assert.That(importer.filterMode, Is.Not.EqualTo(FilterMode.Point));
    }

    private static GameObject Model(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(model, Is.Not.Null, $"No model at {path}.");

        return model;
    }

    private static TextureImporter Importer(string texture)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath($"{KitFolder}/Textures/{texture}.png");

        Assert.That(importer, Is.Not.Null, $"No texture {texture} in {KitFolder}/Textures.");

        return importer;
    }
}
