using System.IO;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Editor.Vfx;

/// <summary>
/// Writes the mask every telegraph ring samples: a white disc inscribed in the texture, a brighter rim,
/// alpha outside. The circle is computed rather than drawn, so it is reproducible and a retune is a
/// constant and a re-run.
/// </summary>
/// <remarks>
/// The disc's edge sits at half the texture's width, so on the Quad whose half-width is the ring's radius
/// the drawn edge is the radius core tested. Colour stays a tint from the palette; the texture is white.
/// </remarks>
internal static class TelegraphRingTextureGenerator
{
    private const string Folder = "Assets/_Project/Art/Vfx";
    private const string Path = Folder + "/T_TelegraphRing_Mask.png";
    private const int Size = 256;

    /// <summary>The share of the radius, from the edge in, that carries the full-alpha rim.</summary>
    private const float RimShare = 0.08f;

    /// <summary>The fill's alpha inside the rim.</summary>
    private const float FillAlpha = 0.45f;

    [MenuItem("Soulvail/VFX/Generate Telegraph Ring Mask")]
    public static void Generate()
    {
        EnsureFolder("Assets/_Project", "Art");
        EnsureFolder("Assets/_Project/Art", "Vfx");

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        var pixels = new Color32[Size * Size];
        float radius = Size * 0.5f;
        float rimStart = radius * (1f - RimShare);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // One texel of antialiasing on each edge: the outer edge is 50 % at exactly the radius.
                float coverage = Mathf.Clamp01(radius - d + 0.5f);
                float rim = Mathf.Clamp01(d - rimStart + 0.5f);
                float alpha = Mathf.Lerp(FillAlpha, 1f, rim) * coverage;
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        File.WriteAllBytes(Path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.isReadable = false;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Android",
            overridden = true,
            maxTextureSize = Size,
            format = TextureImporterFormat.ASTC_6x6,
        });
        importer.SaveAndReimport();
        Debug.Log($"Telegraph ring mask written to {Path}.");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
