using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-09a: the eleven pieces Pause and Run end wear that the popup kit does not already have (two title
/// ribbons, two emblems, three stat icons, four button glyphs), as imported for mobile.
/// </summary>
[TestFixture]
public sealed class RunEndArtTests
{
    private const string RunEndFolder = "Assets/_Project/Art/UI/RunEnd";
    private const string IconsFolder = "Assets/_Project/Art/UI/Icons";

    /// <summary>
    /// Where <c>UI_v5/cut_sheets.py</c> writes the pieces, beside the repository.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v5/png";

    private static readonly string[] RunEndPieces =
    {
        "RunEnd_RibbonDefeat", "RunEnd_RibbonVictory", "RunEnd_EmblemDefeat", "RunEnd_EmblemVictory",
    };

    private static readonly string[] Icons =
    {
        "Icon_Stage", "Icon_BossKill", "Icon_PlaceOpened", "Icon_Resume", "Icon_Skills", "Icon_Tree", "Icon_Quit",
    };

    private static IEnumerable<string> PiecePaths() => RunEndPieces.Select(n => $"{RunEndFolder}/{n}.png")
        .Concat(Icons.Select(n => $"{IconsFolder}/{n}.png"));

    [Test]
    public void RunEndArt_IsTheFourPieces()
    {
        string[] expected = RunEndPieces.Select(n => $"{RunEndFolder}/{n}.png").OrderBy(p => p).ToArray();
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { RunEndFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .ToArray();

        Assert.That(found, Is.EqualTo(expected), "RunEnd/ holds the four pieces and nothing else.");

        string[] files = Directory.GetFiles(RunEndFolder)
            .Where(f => !f.EndsWith(".meta"))
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => f)
            .ToArray();

        Assert.That(files, Is.EqualTo(expected), "no other file sits in RunEnd/.");
    }

    [Test]
    public void RunEndArt_IconsAreInIcons()
    {
        foreach (string name in Icons)
        {
            string path = $"{IconsFolder}/{name}.png";
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(path), Is.Not.Null, $"{path} is not an imported sprite.");
        }
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void RunEndArt_IsByteForByteTheCut(string path)
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
    public void RunEndArt_ImportsAsAUiSprite(string path)
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

    [Test]
    public void RunEndArt_PairsShareACanvas()
    {
        foreach ((string defeat, string victory) in new[]
        {
            ("RunEnd_RibbonDefeat", "RunEnd_RibbonVictory"),
            ("RunEnd_EmblemDefeat", "RunEnd_EmblemVictory"),
        })
        {
            Importer($"{RunEndFolder}/{defeat}.png").GetSourceTextureWidthAndHeight(out int dw, out int dh);
            Importer($"{RunEndFolder}/{victory}.png").GetSourceTextureWidthAndHeight(out int vw, out int vh);

            Assert.That((dw, dh), Is.EqualTo((vw, vh)), $"{defeat} and {victory} must be one size, or swapping them moves the piece.");
        }
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void RunEndArt_RibbonsHaveTheirBorder(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Vector4 border = Importer(path).spriteBorder;

        if (name.StartsWith("RunEnd_Ribbon"))
        {
            Vector4 pair = Importer($"{RunEndFolder}/RunEnd_RibbonDefeat.png").spriteBorder;

            Assert.That(border.x, Is.GreaterThan(0f), "the folded tails are the border.");
            Assert.That(border.z, Is.EqualTo(border.x), "both tails the same.");
            Assert.That(border.y, Is.EqualTo(0f));
            Assert.That(border.w, Is.EqualTo(0f));
            Assert.That(border, Is.EqualTo(pair), "both ribbons slice alike.");
        }
        else
        {
            Assert.That(border, Is.EqualTo(Vector4.zero), $"{name} is not sliced.");
        }
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void RunEndArt_NoMagentaLeft(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.That(tex.LoadImage(File.ReadAllBytes(path)), Is.True);
        Color32[] px = tex.GetPixels32();
        int w = tex.width;
        int h = tex.height;
        int magenta = 0;
        int strayPartial = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[y * w + x];

                if (c.a == 0)
                {
                    continue;
                }

                Color.RGBToHSV(c, out float hue, out float saturation, out _);

                if (hue * 360f >= 285f && hue * 360f <= 315f && saturation > 0.5f)
                {
                    magenta++;
                }

                if (c.a < 255 && !TouchesClear(px, w, h, x, y))
                {
                    strayPartial++;
                }
            }
        }

        Object.DestroyImmediate(tex);

        Assert.That(magenta, Is.Zero, $"{Path.GetFileName(path)}: {magenta} visible pixels are magenta-hued; the key left a fringe.");
        Assert.That(strayPartial, Is.Zero, $"{Path.GetFileName(path)}: {strayPartial} partial-alpha pixels sit away from the edge.");
    }

    private static bool TouchesClear(Color32[] px, int w, int h, int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= w || ny >= h || px[ny * w + nx].a == 0)
                {
                    return true;
                }
            }
        }

        return false;
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
