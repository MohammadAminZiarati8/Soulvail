using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-01a: the HUD's nineteen pieces and its font, as the importer reads them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The import settings are the whole of what this task decides</b>, and every one of them is a
/// checkbox an update or a stray click resets without a word. A sprite that kept its mipmaps costs a
/// third more memory for nothing; one that lost its Android override ships as RGBA32; one that lost
/// its borders stretches a rounded end into an oval. So each is a row.
/// </para>
/// <para>
/// <b>The pieces are driven by name, not by folder.</b> A folder-driven list passes on an empty
/// folder by measuring nothing, which is <c>JungleKit_IsShipped</c>'s reason — and here the list is
/// the owner's, so <see cref="HudArt_IsTheNineteenPieces"/> pins it both ways.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HudArtTests
{
    private const string HudFolder = "Assets/_Project/Art/UI/Hud";
    private const string IconFolder = "Assets/_Project/Art/UI/Icons";
    private const string FontFolder = "Assets/_Project/Art/UI/Fonts";
    private const string FontAssetPath = FontFolder + "/LilitaOne_SDF.asset";
    private const string LiberationPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    /// <summary>
    /// Where <c>UI_v2/cut_sheets.py</c> writes the pieces, beside the repository rather than in it.
    /// </summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v2/png";

    /// <summary>The stretching pieces' left and right borders, in source pixels (rule 3).</summary>
    /// <remarks>
    /// The numbers <c>UI_v2/mockup_hud.py</c> composed the approved mockup with, and 90 for the boss
    /// frame, whose rounded end is about half its 169 px height and which the mockup never drew.
    /// </remarks>
    private static readonly Dictionary<string, float> Borders = new Dictionary<string, float>
    {
        { "HealthBar_Frame", 130f },
        { "Fill_Bar", 80f },
        { "Gloss_Streak", 120f },
        { "XpStrip_Track", 60f },
        { "Fill_BarThin", 40f },
        { "BossBar_Frame", 90f },
        { "Pill_Dark", 105f },
        { "Chip_Counter", 70f },
    };

    private static readonly string[] HudPieces =
    {
        "HealthBar_Frame", "Fill_Bar", "Gloss_Streak", "XpStrip_Track", "Fill_BarThin", "LevelStar",
        "SkillCell_Frame", "ChargePip_Empty", "ChargePip_Full", "Sweep_Disc", "Glow_Soft", "Pill_Dark",
        "Chip_Counter", "Button_Pause", "BossBar_Frame", "BossBar_PhaseSeam",
    };

    private static readonly string[] Icons = { "Icon_EnemiesLeft", "Icon_Volley", "Icon_Placeholder" };

    /// <summary>The pieces a view draws in a <c>Palette</c> colour, which must therefore be white (rule 4).</summary>
    private static readonly string[] TintedPieces = { "Fill_Bar", "Fill_BarThin", "Gloss_Streak", "Sweep_Disc", "Glow_Soft" };

    private static IEnumerable<string> PiecePaths() =>
        HudPieces.Select(name => $"{HudFolder}/{name}.png").Concat(Icons.Select(name => $"{IconFolder}/{name}.png"));

    [Test]
    public void HudArt_IsTheNineteenPieces()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { HudFolder, IconFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .ToArray();

        Assert.That(found, Is.EqualTo(PiecePaths().OrderBy(path => path).ToArray()),
            "the HUD's art is exactly the pieces UI-01 draws: one more is a sprite in the build that "
                + "nothing references, one fewer is a view with nothing to wear.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HudArt_IsByteForByteTheCut(string path)
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
    public void HudArt_ImportsAsAUiSprite(string path)
    {
        TextureImporter importer = Importer(path);
        var settings = new TextureImporterSettings();

        importer.ReadTextureSettings(settings);

        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect),
            "a tight mesh cannot be sliced, and slicing is how every bar on the band stretches.");
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f));
        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(importer.alphaIsTransparency, Is.True);
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HudArt_HasNoMipmaps(string path)
    {
        // A screen-space sprite is drawn at one size; a mip chain is a third more memory for nothing.
        Assert.That(Importer(path).mipmapEnabled, Is.False);
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void HudArt_IsAstcOnAndroid(string path)
    {
        TextureImporterPlatformSettings android = Importer(path).GetPlatformTextureSettings("Android");

        Assert.That(android.overridden, Is.True, "without the override the phone gets the default format.");
        Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_4x4),
            "ASTC 4×4, because an outline two texels wide is the whole of the look (rule 2).");
    }

    [TestCaseSource(nameof(Stretching))]
    public void HudArt_TheStretchingPiecesAreSliced(string name)
    {
        float side = Borders[name];

        Assert.That(Importer($"{HudFolder}/{name}.png").spriteBorder, Is.EqualTo(new Vector4(side, 0f, side, 0f)),
            $"{name}'s rounded ends are its left and right borders; the rest of it stretches.");
    }

    [TestCaseSource(nameof(NotStretching))]
    public void HudArt_TheRestAreNotSliced(string path)
    {
        Assert.That(Importer(path).spriteBorder, Is.EqualTo(Vector4.zero),
            $"{Path.GetFileName(path)} is drawn at its own aspect; a border would be a slice nobody asked for.");
    }

    [TestCaseSource(nameof(TintedPieces))]
    public void HudArt_TheTintedPiecesAreWhite(string name)
    {
        // Read from the file rather than the imported texture, which is compressed and not readable.
        var texture = new Texture2D(2, 2);

        try
        {
            Assert.That(texture.LoadImage(File.ReadAllBytes($"{HudFolder}/{name}.png")), Is.True);

            double saturation = 0d;
            int opaque = 0;

            foreach (Color32 pixel in texture.GetPixels32())
            {
                if (pixel.a < 128)
                {
                    continue;
                }

                Color.RGBToHSV(pixel, out _, out float s, out _);

                saturation += s;
                opaque++;
            }

            Assert.That(opaque, Is.GreaterThan(0), $"{name} has no opaque pixel.");
            Assert.That(saturation / opaque, Is.LessThan(0.08d),
                $"{name} is drawn in a Palette colour, so it must be white: a tint multiplies, and a "
                    + "cyan drawn over a coloured piece is not GD §16.4's cyan.");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void Font_IsStaticAndCoversTheHud()
    {
        TMP_FontAsset font = Font();

        Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static),
            "a dynamic font rasterises on the phone and rewrites its own asset in the Editor.");

        Assert.That(font.atlasTextures, Has.Length.EqualTo(1), "one 1024² atlas holds rule 5's set.");

        var wanted = new List<char>();

        for (int c = 0x20; c <= 0x7E; c++)
        {
            wanted.Add((char)c);
        }

        wanted.Add('·');

        for (int c = 0xC0; c <= 0xFF; c++)
        {
            wanted.Add((char)c);
        }

        char[] missing = wanted.Where(c => !font.HasCharacter(c, false, false)).ToArray();

        Assert.That(missing, Is.Empty, $"LilitaOne_SDF's atlas has no glyph for: {new string(missing)}");
    }

    [Test]
    public void Font_FallsBackToLiberation()
    {
        var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);

        Assert.That(Font().fallbackFontAssetTable, Does.Contain(liberation),
            "a character outside the atlas would draw as a box rather than in TMP's default font.");
    }

    [Test]
    public void Font_IsOutlinedInInk()
    {
        Material material = Font().material;

        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Distance Field"));

        // IsKeywordEnabled rather than shaderKeywords, which reports a name the shader never
        // declared (Traps §9).
        Assert.That(material.IsKeywordEnabled("OUTLINE_ON"), Is.True);
        Assert.That(material.IsKeywordEnabled("UNDERLAY_ON"), Is.True);

        Assert.That(Same(material.GetColor(ShaderUtilities.ID_OutlineColor), Palette.Ink), Is.True,
            "the outline is not Palette.Ink.");
        Assert.That(Same(material.GetColor(ShaderUtilities.ID_UnderlayColor), Palette.Ink), Is.True,
            "the shadow is not Palette.Ink.");

        Assert.That(material.GetFloat(ShaderUtilities.ID_OutlineWidth), Is.GreaterThan(0f));
        Assert.That(material.GetFloat(ShaderUtilities.ID_UnderlayOffsetY), Is.LessThan(0f),
            "the shadow sits below the face.");
    }

    [Test]
    public void Font_ShipsWithItsLicence()
    {
        Assert.That(File.Exists($"{FontFolder}/LilitaOne-Regular.ttf"), Is.True);
        Assert.That(File.Exists($"{FontFolder}/OFL-LilitaOne.txt"), Is.True,
            "the SIL Open Font License travels with the font it licenses.");
        Assert.That(File.ReadAllText($"{FontFolder}/OFL-LilitaOne.txt"), Does.Contain("SIL Open Font License"));
    }

    private static IEnumerable<string> Stretching() => Borders.Keys;

    private static IEnumerable<string> NotStretching() =>
        PiecePaths().Where(path => !Borders.ContainsKey(Path.GetFileNameWithoutExtension(path)));

    private static TextureImporter Importer(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        Assert.That(importer, Is.Not.Null, $"{path} is not an imported texture.");

        return importer;
    }

    private static TMP_FontAsset Font()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        Assert.That(font, Is.Not.Null, $"{FontAssetPath} is missing.");

        return font;
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();

        return System.Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    /// <summary>Equal at eight bits a channel, alpha included — PaletteTests' resolution.</summary>
    private static bool Same(Color a, Color b)
    {
        Color32 x = a;
        Color32 y = b;

        return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
    }
}
