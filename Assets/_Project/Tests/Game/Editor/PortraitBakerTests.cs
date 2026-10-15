using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Editor.Heroes;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Editor;

/// <summary>
/// UI-12a: the portrait bake's framing arithmetic, the committed PNGs as imported, and the assignment
/// that leaves a painted portrait alone.
/// </summary>
/// <remarks>
/// The PNG rows read the committed files; nothing here bakes, because an EditMode render in the open
/// Editor reads Direct3D12's quirks (Traps §9). The bake's picture is the spec's manual step.
/// </remarks>
[TestFixture]
public sealed class PortraitBakerTests
{
    private const string PlaceholderPath = "Assets/_Project/Art/UI/Icons/Icon_Placeholder.png";

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

    // ---- Rule 4: the framing -------------------------------------------------------------------

    [Test]
    public void Frame_HoldsTheTopBandOfTheBody()
    {
        Bounds body = Standing(2f);
        PortraitFraming framing = PortraitBaker.Frame(body, PortraitBaker.FieldOfViewDeg);

        float band = PortraitBaker.ShoulderFraction * 2f;

        Assert.That(framing.LookAt.y, Is.EqualTo(2f - band / 2f).Within(1e-4f));

        float top = FieldHeight(framing, framing.LookAt + Vector3.up * (band / 2f));
        float bottom = FieldHeight(framing, framing.LookAt - Vector3.up * (band / 2f));

        Assert.That(top, Is.InRange(0.99f, 1.0001f), "the band's top sits on the field's upper edge.");
        Assert.That(bottom, Is.InRange(-1.0001f, -0.99f), "the band's bottom sits on the field's lower edge.");
    }

    [Test]
    public void Frame_StandsInFront_LooksSlightlyDown()
    {
        PortraitFraming framing = PortraitBaker.Frame(Standing(2f), PortraitBaker.FieldOfViewDeg);

        Assert.That(framing.Position.z, Is.GreaterThan(framing.LookAt.z), "the camera stands on the body's +Z, its front.");
        Assert.That(framing.Position.y, Is.GreaterThan(framing.LookAt.y), "the camera looks down.");
    }

    [Test]
    public void Frame_ScalesWithTheBody()
    {
        PortraitFraming small = PortraitBaker.Frame(Standing(1f), PortraitBaker.FieldOfViewDeg);
        PortraitFraming large = PortraitBaker.Frame(Standing(2f), PortraitBaker.FieldOfViewDeg);

        float smallDistance = Vector3.Distance(small.Position, small.LookAt);
        float largeDistance = Vector3.Distance(large.Position, large.LookAt);

        Assert.That(largeDistance, Is.EqualTo(2f * smallDistance).Within(1e-4f));
    }

    // ---- Rule 6: the file name -----------------------------------------------------------------

    [Test]
    public void FileNameFor_TakesTheLastSegment()
    {
        Assert.That(PortraitBaker.FileNameFor(new ContentId("character.ranger")), Is.EqualTo("Portrait_Ranger"));
        Assert.That(PortraitBaker.FileNameFor(new ContentId("character.oathbound")), Is.EqualTo("Portrait_Oathbound"));
    }

    // ---- Rules 1, 5, 6, 7, 8: the committed bake -----------------------------------------------

    [Test]
    public void Portraits_OnePerDefinitionWithABody()
    {
        string[] expected = Bodied()
            .Select(PortraitPath)
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToArray();

        string[] found = Directory.GetFiles(PortraitBaker.Folder, "*.png")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToArray();

        Assert.That(expected, Has.Length.EqualTo(4), "the Ranger and the three classes in the Knight's body.");
        Assert.That(found, Is.EqualTo(expected), "Heroes/ holds one portrait per class with a body, and no other PNG.");
    }

    [TestCaseSource(nameof(PortraitPaths))]
    public void Portraits_ImportAsAUiSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Assert.That(importer, Is.Not.Null, $"{path} is not an imported texture.");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);

        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f));
        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(importer.alphaIsTransparency, Is.True);
        Assert.That(importer.mipmapEnabled, Is.False);
        Assert.That(importer.maxTextureSize, Is.EqualTo(PortraitBaker.Side));

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_4x4));
        Assert.That(android.maxTextureSize, Is.EqualTo(PortraitBaker.Side));

        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        Assert.That((width, height), Is.EqualTo((PortraitBaker.Side, PortraitBaker.Side)));
    }

    [TestCaseSource(nameof(PortraitPaths))]
    public void Portraits_AreTransparentAtTheCorners(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _created.Add(texture);
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, $"{path} is not a PNG.");

        int last = texture.width - 1;
        foreach ((int x, int y) in new[] { (0, 0), (last, 0), (0, last), (last, last) })
        {
            Assert.That(texture.GetPixel(x, y).a, Is.Zero, $"{path} corner ({x}, {y}) is not transparent.");
        }

        int centre = texture.width / 2;
        bool opaque = Enumerable.Range(0, texture.height).Any(y => texture.GetPixel(centre, y).a >= 1f);
        Assert.That(opaque, Is.True, $"{path}: no opaque pixel down the centre column, so no body was drawn.");
    }

    [Test]
    public void Definitions_PointAtTheirPortrait()
    {
        foreach (CharacterDefinition definition in Bodied())
        {
            string path = PortraitPath(definition);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            Assert.That(sprite, Is.Not.Null, $"{path} has no sprite.");
            // Unity's equality, not reference equality: after a reimport the field and LoadAssetAtPath
            // can hand out two managed wrappers over the one sprite, with one instance id.
            Assert.That(definition.ToLook().Portrait == sprite, Is.True, $"{definition.name} does not point at {path}.");
        }
    }

    [Test]
    public void Assign_LeavesAPaintedPortraitAlone()
    {
        var painted = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPath);
        var baked = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPaths().First());
        Assert.That(painted, Is.Not.Null, $"No sprite at {PlaceholderPath}.");
        CharacterDefinition definition = Throwaway(painted);

        bool assigned = PortraitBaker.AssignPortrait(definition, baked, PortraitPaths().First());

        Assert.That(assigned, Is.False);
        Assert.That(definition.ToLook().Portrait, Is.SameAs(painted), "a painted portrait stays.");
    }

    [Test]
    public void Assign_SetsAnEmptyField()
    {
        var baked = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPaths().First());
        CharacterDefinition definition = Throwaway(null);

        bool assigned = PortraitBaker.AssignPortrait(definition, baked, PortraitPaths().First());

        Assert.That(assigned, Is.True);
        Assert.That(definition.ToLook().Portrait, Is.SameAs(baked));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static IEnumerable<string> PortraitPaths() => Bodied().Select(PortraitPath);

    private static IEnumerable<CharacterDefinition> Bodied() => AssetDatabase.FindAssets("t:CharacterDefinition")
        .Select(AssetDatabase.GUIDToAssetPath)
        .OrderBy(p => p, System.StringComparer.Ordinal)
        .Select(AssetDatabase.LoadAssetAtPath<CharacterDefinition>)
        .Where(d => d.ToLook().Body != null);

    private static string PortraitPath(CharacterDefinition definition) =>
        $"{PortraitBaker.Folder}/{PortraitBaker.FileNameFor(new ContentId(definition.Id))}.png";

    /// <summary>A body <paramref name="height"/> m tall, its feet at the origin.</summary>
    private static Bounds Standing(float height) =>
        new Bounds(new Vector3(0f, height / 2f, 0f), new Vector3(height / 2f, height, height / 2f));

    /// <summary>
    /// Where <paramref name="point"/> lands in the framing's vertical field: −1 at its lower edge, +1 at
    /// its upper.
    /// </summary>
    private static float FieldHeight(PortraitFraming framing, Vector3 point)
    {
        Vector3 forward = (framing.LookAt - framing.Position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right);
        Vector3 toPoint = point - framing.Position;
        float tan = Mathf.Tan(PortraitBaker.FieldOfViewDeg * 0.5f * Mathf.Deg2Rad);

        return Vector3.Dot(toPoint, up) / Vector3.Dot(toPoint, forward) / tan;
    }

    private CharacterDefinition Throwaway(Sprite portrait)
    {
        var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
        definition.name = "PortraitThrowaway";
        _created.Add(definition);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_portrait").objectReferenceValue = portrait;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }
}
