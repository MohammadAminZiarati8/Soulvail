using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-05a: the fourteen sprites the character page wears, as imported for mobile.
/// </summary>
/// <remarks>
/// The pieces are driven by name, not by folder: eleven in <c>Art/UI/Character/</c>, two violet
/// buttons in <c>Art/UI/Popup/</c>, and the shard in <c>Art/UI/Icons/</c>.
/// </remarks>
[TestFixture]
public sealed class CharacterPageArtTests
{
    private const string CharacterFolder = "Assets/_Project/Art/UI/Character";
    private const string PopupFolder = "Assets/_Project/Art/UI/Popup";
    private const string IconFolder = "Assets/_Project/Art/UI/Icons";
    private const string Background = "CharacterPage_Background";

    /// <summary>
    /// Where <c>UI_v3/cut_sheets.py</c> writes the pieces, beside the repository.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v3/png";

    private static readonly string[] CharacterPieces =
    {
        "CharacterPage_Background",
        "CharacterPage_Pedestal",
        "CharacterPage_ArrowLeft",
        "CharacterPage_ArrowRight",
        "CharacterPage_DotOff",
        "CharacterPage_DotOn",
        "CharacterPage_SkillFrame",
        "CharacterPage_SkillFrameSelected",
        "CharacterPage_LockBadge",
        "CharacterPage_CostPill",
        "CharacterPage_RoleRanged",
    };

    private static readonly string[] PopupPieces = { "Button_Violet", "Button_Violet_Pressed" };

    private static IEnumerable<string> PiecePaths() =>
        CharacterPieces.Select(n => $"{CharacterFolder}/{n}.png")
            .Concat(PopupPieces.Select(n => $"{PopupFolder}/{n}.png"))
            .Append($"{IconFolder}/Icon_SoulShard.png");

    /// <summary>
    /// The pieces cut from a magenta sheet; the background is a plain picture with no edge.
    /// </summary>
    private static IEnumerable<string> CutPaths() =>
        PiecePaths().Where(p => Path.GetFileNameWithoutExtension(p) != Background);

    [Test]
    public void CharacterArt_IsTheFourteenPieces()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { CharacterFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p)
            .ToArray();
        string[] expected = CharacterPieces.Select(n => $"{CharacterFolder}/{n}.png").OrderBy(p => p).ToArray();

        Assert.That(found, Is.EqualTo(expected), "Character/ holds the eleven pieces and nothing else.");
        Assert.That(PiecePaths().Count(File.Exists), Is.EqualTo(14), "all fourteen pieces exist on disk.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void CharacterArt_IsByteForByteTheCut(string path)
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
    public void CharacterArt_ImportsAsAUiSprite(string path)
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

    [TestCaseSource(nameof(PiecePaths))]
    public void CharacterArt_HasNoMipmaps(string path)
    {
        Assert.That(Importer(path).mipmapEnabled, Is.False,
            "a screen-space sprite needs no mip chain.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void CharacterArt_IsAstcOnAndroid(string path)
    {
        TextureImporterPlatformSettings android = Importer(path).GetPlatformTextureSettings("Android");
        bool isBackground = Path.GetFileNameWithoutExtension(path) == Background;
        TextureImporterFormat expected = isBackground ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;

        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(expected),
            "pieces keep their outlines at ASTC 4x4; the background is a soft picture and takes 6x6.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void CharacterArt_HasItsBorders(string path)
    {
        var importer = Importer(path);
        importer.GetSourceTextureWidthAndHeight(out _, out int height);
        string name = Path.GetFileNameWithoutExtension(path);

        Vector4 expected = name switch
        {
            "Button_Violet" or "Button_Violet_Pressed" => new Vector4(Mathf.Round(0.37f * height), 0f, Mathf.Round(0.37f * height), 0f),
            "CharacterPage_CostPill" => new Vector4(height / 2, 0f, height / 2, 0f),
            _ => Vector4.zero,
        };

        Assert.That(importer.spriteBorder, Is.EqualTo(expected), $"{name} border mismatch.");
    }

    [TestCaseSource(nameof(CutPaths))]
    public void CharacterArt_HasNoMagentaFringe(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.That(tex.LoadImage(File.ReadAllBytes(path)), Is.True);
        Color32[] px = tex.GetPixels32();
        int w = tex.width;
        int h = tex.height;
        int edge = 0;
        int magenta = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[y * w + x];

                if (c.a == 0 || !TouchesClear(px, w, h, x, y))
                {
                    continue;
                }

                edge++;

                if (c.r > c.g + 40 && c.b > c.g + 40 && Mathf.Abs(c.r - c.b) < 60)
                {
                    magenta++;
                }
            }
        }

        Object.DestroyImmediate(tex);

        Assert.That(edge, Is.GreaterThan(0));
        Assert.That(magenta, Is.LessThan(edge / 200 + 1),
            $"{Path.GetFileName(path)}: {magenta} of {edge} edge pixels lean magenta; the key left a fringe.");
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
