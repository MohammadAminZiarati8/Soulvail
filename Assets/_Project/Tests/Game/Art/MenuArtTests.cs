using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-06a: the three sprites the main menu wears beyond the character page's, as imported for mobile.
/// </summary>
[TestFixture]
public sealed class MenuArtTests
{
    private const string MenuFolder = "Assets/_Project/Art/UI/Menu";

    /// <summary>
    /// Where <c>UI_v3/cut_sheets.py</c> writes the pieces, beside the repository.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v3/png";

    private static readonly string[] Pieces = { "Button_Blue", "Button_Blue_Pressed", "Menu_TitlePlate" };

    private static IEnumerable<string> PiecePaths() => Pieces.Select(n => $"{MenuFolder}/{n}.png");

    [Test]
    public void MenuArt_IsTheThreePieces()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { MenuFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .ToArray();

        Assert.That(found, Is.EqualTo(PiecePaths().OrderBy(p => p).ToArray()),
            "Menu/ holds the three pieces and nothing else.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void MenuArt_IsByteForByteTheCut(string path)
    {
        string cut = Path.Combine(CutFolder, Path.GetFileName(path));

        if (!File.Exists(cut))
        {
            Assert.Ignore($"{CutFolder} is not beside the repository on this machine.");
        }

        Assert.That(Hash(path), Is.EqualTo(Hash(cut)),
            $"{path} is not the piece cut_sheets.py wrote. Copy it again rather than editing it here.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void MenuArt_ImportsAsAUiSprite(string path)
    {
        TextureImporter importer = Importer(path);
        var settings = new TextureImporterSettings();

        importer.ReadTextureSettings(settings);

        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f));
        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(importer.alphaIsTransparency, Is.True);
        Assert.That(importer.mipmapEnabled, Is.False, "a screen-space sprite needs no mip chain.");

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");

        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_4x4));
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void MenuArt_HasItsBorders(string path)
    {
        TextureImporter importer = Importer(path);
        importer.GetSourceTextureWidthAndHeight(out _, out int height);
        string name = Path.GetFileNameWithoutExtension(path);

        Vector4 expected = name == "Menu_TitlePlate"
            ? new Vector4(56f, 56f, 56f, 56f)
            : new Vector4(Mathf.Round(0.37f * height), 0f, Mathf.Round(0.37f * height), 0f);

        Assert.That(importer.spriteBorder, Is.EqualTo(expected), $"{name} border mismatch.");
    }

    private static TextureImporter Importer(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Assert.That(importer, Is.Not.Null, $"{path} is not an imported texture.");
        return importer;
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return System.Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
