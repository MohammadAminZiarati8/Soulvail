using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-02a: the lower controls' eight pieces (virtual stick, roll button, manual skill buttons)
/// as imported for mobile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Import settings and dimensions are pinned to prevent silent regressions.</b> An asset whose
/// mipmaps are left on costs extra memory; one that loses its ASTC 4×4 override ships uncompressed
/// or in the default mobile format; one whose dimensions drift breaks layout math in code.
/// </para>
/// <para>
/// <b>The pieces are driven by name, not by folder.</b> There are 7 pieces in <c>Art/UI/Hud/</c>
/// and 1 icon in <c>Art/UI/Icons/</c>, bringing the total HUD sprite count to 27 across both folders; UI-04a's three tree icons make it 30.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LowerControlsArtTests
{
    private const string HudFolder = "Assets/_Project/Art/UI/Hud";
    private const string IconFolder = "Assets/_Project/Art/UI/Icons";

    /// <summary>
    /// Where <c>UI_v2/cut_sheets.py</c> writes the pieces, beside the repository.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v2/png";

    private static readonly string[] HudPieces =
    {
        "Stick_Base",
        "Stick_Knob",
        "RollButton_Ready",
        "RollButton_Cooling",
        "RollButton_Pressed",
        "SkillButton_Ready",
        "SkillButton_Cooling",
    };

    private static readonly string[] Icons =
    {
        "Icon_Roll",
    };

    private static readonly (string Name, int Width, int Height)[] Dimensions =
    {
        ("Stick_Base", 543, 482),
        ("Stick_Knob", 365, 410),
        ("RollButton_Ready", 530, 523),
        ("RollButton_Cooling", 531, 523),
        ("RollButton_Pressed", 530, 523),
        ("Icon_Roll", 621, 607),
        ("SkillButton_Ready", 348, 347),
        ("SkillButton_Cooling", 348, 347),
    };

    private static IEnumerable<string> LowerControlPaths() =>
        HudPieces.Select(name => $"{HudFolder}/{name}.png")
            .Concat(Icons.Select(name => $"{IconFolder}/{name}.png"));

    private static IEnumerable<TestCaseData> DimensionCases() =>
        Dimensions.Select(d =>
        {
            string folder = d.Name.StartsWith("Icon_") ? IconFolder : HudFolder;
            return new TestCaseData($"{folder}/{d.Name}.png", d.Width, d.Height)
                .SetName($"LowerControlsArt_HasAuthoredDimensions({d.Name})");
        });

    [Test]
    public void LowerControlsArt_IsTheEightPieces()
    {
        string[] expected = LowerControlPaths().OrderBy(p => p).ToArray();
        string[] actual = expected
            .Where(path => File.Exists(path))
            .OrderBy(p => p)
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected),
            "all eight lower control pieces must exist on disk.");
    }

    [Test]
    public void LowerControlsArt_TotalHudCountIsThirtyOne()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { HudFolder, IconFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .ToArray();

        Assert.That(found, Has.Length.EqualTo(31),
            "Hud/ and Icons/ hold exactly 31 pieces: 19 from UI-01, 8 from UI-02a, 3 tree icons from UI-04a, the shard from UI-05a.");
    }

    [TestCaseSource(nameof(LowerControlPaths))]
    public void LowerControlsArt_IsByteForByteTheCut(string path)
    {
        string cut = Path.Combine(CutFolder, Path.GetFileName(path));

        if (!File.Exists(cut))
        {
            Assert.Ignore($"{CutFolder} is not beside the repository on this machine.");
        }

        Assert.That(Hash(path), Is.EqualTo(Hash(cut)),
            $"{path} is not the piece cut_sheets.py wrote. Copy it again rather than editing it here.");
    }

    [TestCaseSource(nameof(LowerControlPaths))]
    public void LowerControlsArt_ImportsAsAUiSprite(string path)
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
    }

    [TestCaseSource(nameof(LowerControlPaths))]
    public void LowerControlsArt_HasNoMipmaps(string path)
    {
        Assert.That(Importer(path).mipmapEnabled, Is.False,
            "a screen-space control sprite needs no mip chain.");
    }

    [TestCaseSource(nameof(LowerControlPaths))]
    public void LowerControlsArt_IsAstcOnAndroid(string path)
    {
        TextureImporterPlatformSettings android = Importer(path).GetPlatformTextureSettings("Android");

        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_4x4),
            "ASTC 4×4 preserves sharp outlines on mobile.");
    }

    [TestCaseSource(nameof(LowerControlPaths))]
    public void LowerControlsArt_AreNotSliced(string path)
    {
        Assert.That(Importer(path).spriteBorder, Is.EqualTo(Vector4.zero),
            $"{Path.GetFileName(path)} is drawn at its authored aspect ratio; no slicing is applied.");
    }

    [TestCaseSource(nameof(DimensionCases))]
    public void LowerControlsArt_HasAuthoredDimensions(string path, int expectedWidth, int expectedHeight)
    {
        var importer = Importer(path);
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);

        Assert.That(width, Is.EqualTo(expectedWidth), $"{Path.GetFileName(path)} width mismatch.");
        Assert.That(height, Is.EqualTo(expectedHeight), $"{Path.GetFileName(path)} height mismatch.");
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
