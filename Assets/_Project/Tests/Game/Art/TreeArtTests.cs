using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-04a: the tree screen's twenty-two pieces (the frame, the class block, the selection and the
/// Learn button) as imported for mobile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three folders, by who else will wear them.</b> The frame, the card and the buttons are any
/// popup's, so they live in <c>Art/UI/Popup/</c>; the tiles, badges, links and the class plate are the
/// tree's alone, in <c>Art/UI/Tree/</c>; the three icons join the HUD's in <c>Art/UI/Icons/</c>.
/// </para>
/// <para>
/// <b>The borders are the ones <c>UI_v2/mockup_tree.py</c> composed the approved mockup with</b>, in
/// Unity's order (left, bottom, right, top). <c>Button_Grey</c>, which the mockup never drew, takes
/// <c>Button_Green</c>'s: its rounded ends are the same height.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TreeArtTests
{
    private const string PopupFolder = "Assets/_Project/Art/UI/Popup";
    private const string TreeFolder = "Assets/_Project/Art/UI/Tree";
    private const string IconFolder = "Assets/_Project/Art/UI/Icons";

    /// <summary>Where <c>UI_v2/cut_sheets.py</c> writes the pieces, beside the repository.</summary>
    private const string CutFolder = "../Soulvail_GeminiSamples/UI_v2/png";

    private static readonly string[] PopupPieces =
    {
        "Popup_Panel", "Popup_TitleRibbon", "Button_Close", "Button_Green", "Button_Green_Pressed",
        "Button_Grey", "Card_Stone",
    };

    private static readonly string[] TreePieces =
    {
        "TreeBlock_Panel", "TreeTile_Taken", "TreeTile_Available", "TreeTile_Locked",
        "TreeTile_SelectedRing", "TreeBadge_Check", "TreeBadge_Plus", "TreeBadge_Lock", "TreeLink_Lit",
        "TreeLink_Dark", "ClassPlate", "Emblem_Ranger",
    };

    private static readonly string[] Icons = { "Icon_SkillPoint", "Icon_QuickDraw", "Icon_FleetFoot" };

    /// <summary>The stretching pieces' borders: left, bottom, right, top, in source pixels.</summary>
    private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
    {
        { "Popup_Panel", new Vector4(110f, 110f, 110f, 110f) },
        { "TreeBlock_Panel", new Vector4(110f, 110f, 110f, 110f) },
        { "Card_Stone", new Vector4(80f, 80f, 80f, 80f) },
        { "Popup_TitleRibbon", new Vector4(200f, 0f, 200f, 0f) },
        { "Button_Green", new Vector4(130f, 0f, 130f, 0f) },
        { "Button_Green_Pressed", new Vector4(130f, 0f, 130f, 0f) },
        { "Button_Grey", new Vector4(130f, 0f, 130f, 0f) },
        { "ClassPlate", new Vector4(100f, 90f, 100f, 90f) },
        { "TreeLink_Lit", new Vector4(0f, 60f, 0f, 60f) },
        { "TreeLink_Dark", new Vector4(0f, 60f, 0f, 60f) },
    };

    private static IEnumerable<string> PiecePaths() =>
        PopupPieces.Select(name => $"{PopupFolder}/{name}.png")
            .Concat(TreePieces.Select(name => $"{TreeFolder}/{name}.png"))
            .Concat(Icons.Select(name => $"{IconFolder}/{name}.png"));

    [Test]
    public void TreeArt_IsTheTwentyTwoPieces()
    {
        string[] found = AssetDatabase.FindAssets("t:Texture2D", new[] { PopupFolder, TreeFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .ToArray();
        string[] expected = PiecePaths().Where(path => !path.StartsWith(IconFolder)).OrderBy(path => path).ToArray();

        Assert.That(found, Is.EqualTo(expected),
            "Popup/ and Tree/ hold exactly UI-04a's nineteen pieces: one fewer is a view with nothing to wear, one more is unreviewed art.");
        Assert.That(Icons.Select(name => $"{IconFolder}/{name}.png").Count(File.Exists), Is.EqualTo(3),
            "the three tree icons are in Icons/.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void TreeArt_IsByteForByteTheCut(string path)
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
    public void TreeArt_ImportsAsAUiSprite(string path)
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
    public void TreeArt_HasNoMipmaps(string path)
    {
        Assert.That(Importer(path).mipmapEnabled, Is.False, "a screen-space sprite needs no mip chain.");
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void TreeArt_IsAstcOnAndroid(string path)
    {
        TextureImporterPlatformSettings android = Importer(path).GetPlatformTextureSettings("Android");

        Assert.That(android.overridden, Is.True, "Android override must be enabled.");
        Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_4x4));
    }

    [TestCaseSource(nameof(PiecePaths))]
    public void TreeArt_HasItsBorders(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Vector4 expected = Borders.TryGetValue(name, out Vector4 border) ? border : Vector4.zero;

        Assert.That(Importer(path).spriteBorder, Is.EqualTo(expected),
            $"{name}: a stretching piece keeps its corners, and every other piece is drawn whole.");
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
