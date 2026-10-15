using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-11h-a: the thirteen pieces the home screen wears (the tab bar's plate and tiles, the place and
/// hero card frames, two place pictures), as imported for mobile.
/// </summary>
[TestFixture]
public sealed class HomeArtTests
{
    private const string HomeFolder = "Assets/_Project/Art/UI/Home";

    /// <summary>
    /// Where <c>UI_v4/cut_sheets.py</c> writes the pieces, beside the repository.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v4/png";

    private static readonly string[] Tiles =
    {
        "TabBar_Heroes", "TabBar_Play", "TabBar_Places",
    };

    private static readonly string[] Frames =
    {
        "PlaceCard_Frame", "PlaceCard_FrameSelected", "HeroCard_Frame", "HeroCard_FrameSelected",
    };

    private static readonly string[] Pictures = { "PlaceCard_Jungle", "PlaceCard_Soon" };

    private static readonly string[] Pieces = Tiles
        .Concat(Tiles.Select(t => t + "On"))
        .Concat(new[] { "TabBar_Plate" })
        .Concat(Frames)
        .Concat(Pictures)
        .ToArray();

    private static IEnumerable<string> PiecePaths() => Pieces.Select(n => $"{HomeFolder}/{n}.png");

    [Test]
    public void HomeArt_IsTheThirteenPieces()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { HomeFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .ToArray();

        Assert.That(Pieces, Has.Length.EqualTo(13));
        Assert.That(found, Is.EqualTo(PiecePaths().OrderBy(p => p).ToArray()),
            "Home/ holds the thirteen pieces and nothing else.");

        string[] files = Directory.GetFiles(HomeFolder)
            .Where(f => !f.EndsWith(".meta"))
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => f)
            .ToArray();

        Assert.That(files, Is.EqualTo(PiecePaths().OrderBy(p => p).ToArray()), "no other file sits in Home/.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HomeArt_IsByteForByteTheCut(string path)
    {
        string cut = Path.Combine(CutFolder, Path.GetFileName(path));

        if (!Directory.Exists(CutFolder))
        {
            Assert.Ignore($"{CutFolder} is not beside the repository on this machine.");
        }

        Assert.That(File.Exists(cut), Is.True, $"{cut}: cut_sheets.py wrote no such piece.");
        Assert.That(Hash(path), Is.EqualTo(Hash(cut)),
            $"{path} is not the piece cut_sheets.py wrote. Copy it again rather than editing it here.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HomeArt_ImportsAsAUiSprite(string path)
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
        bool picture = Pictures.Contains(Path.GetFileNameWithoutExtension(path));

        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(picture ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4));

        if (picture)
        {
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.None), "a picture is opaque.");
            Assert.That(importer.maxTextureSize, Is.EqualTo(2048));
        }
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HomeArt_HasItsBorders(string path)
    {
        TextureImporter importer = Importer(path);
        string name = Path.GetFileNameWithoutExtension(path);
        Vector4 border = importer.spriteBorder;

        if (name == "TabBar_Plate")
        {
            Assert.That(border.x, Is.GreaterThan(0f), "the caps are the border.");
            Assert.That(border.z, Is.EqualTo(border.x), "both caps the same.");
            Assert.That(border.y, Is.EqualTo(0f));
            Assert.That(border.w, Is.EqualTo(0f));
        }
        else if (Frames.Contains(name))
        {
            Assert.That(border.x, Is.GreaterThan(0f), $"{name} left");
            Assert.That(border.y, Is.GreaterThan(0f), $"{name} bottom");
            Assert.That(border.z, Is.GreaterThan(0f), $"{name} right");
            Assert.That(border.w, Is.GreaterThan(0f), $"{name} top");
        }
        else
        {
            Assert.That(border, Is.EqualTo(Vector4.zero), $"{name} is not sliced.");
        }
    }

    [Test]
    public void HomeArt_PairsShareACanvas()
    {
        var pairs = new List<(string Off, string On)>();

        pairs.AddRange(Tiles.Select(t => (t, t + "On")));
        pairs.Add(("PlaceCard_Frame", "PlaceCard_FrameSelected"));
        pairs.Add(("HeroCard_Frame", "HeroCard_FrameSelected"));

        Assert.That(pairs, Has.Count.EqualTo(5));

        foreach ((string off, string on) in pairs)
        {
            Importer($"{HomeFolder}/{off}.png").GetSourceTextureWidthAndHeight(out int offWidth, out int offHeight);
            Importer($"{HomeFolder}/{on}.png").GetSourceTextureWidthAndHeight(out int onWidth, out int onHeight);

            Assert.That((offWidth, offHeight), Is.EqualTo((onWidth, onHeight)), $"{off} and {on} must be one size, or swapping them moves the piece.");
            Assert.That(Importer($"{HomeFolder}/{off}.png").spriteBorder, Is.EqualTo(Importer($"{HomeFolder}/{on}.png").spriteBorder), $"{off} and {on} slice alike.");
        }
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
