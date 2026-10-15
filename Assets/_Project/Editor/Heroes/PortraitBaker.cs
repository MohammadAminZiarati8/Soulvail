using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Soulvail.Editor.Heroes;

/// <summary>
/// <c>Soulvail/Heroes/Bake Portraits</c>: each class's 3D body, head and shoulders, rendered on the
/// character preview's stage and written to <c>Art/UI/Heroes/Portrait_&lt;Class&gt;.png</c> as a UI
/// sprite, then assigned to its <see cref="CharacterDefinition"/> (UI-12a).
/// </summary>
/// <remarks>
/// <para>
/// <b>The stage stands in a preview scene</b>, not the open one, so a bake leaves the Hierarchy and the
/// scene's dirty flag as it found them. The stage's camera is told the scene, so it sees only the
/// stage's lights and the body.
/// </para>
/// <para>
/// <b>The render is <see cref="Supersample"/> times the PNG's side and averaged down by alpha</b>, so the
/// silhouette's edge is smooth against the transparent background without asking URP for MSAA on a
/// render request.
/// </para>
/// </remarks>
public static class PortraitBaker
{
    public const string MenuPath = "Soulvail/Heroes/Bake Portraits";
    public const string Folder = "Assets/_Project/Art/UI/Heroes";
    public const int Side = 512;
    public const float FieldOfViewDeg = 20f;
    public const float ShoulderFraction = 0.6f;

    private const string StagePath = "Assets/_Project/Prefabs/UI/CharacterStage.prefab";
    private const string PortraitField = "_portrait";
    private const string Log = "[PortraitBaker]";

    /// <summary>How far above the look-at the camera stands, as a share of the band's height (rule 4).</summary>
    private const float RiseShare = 0.05f;

    private const int Supersample = 2;

    [MenuItem(MenuPath)]
    private static void BakeFromMenu() => BakeAll();

    [MenuItem(MenuPath, true)]
    private static bool CanBake() => !EditorApplication.isPlaying;

    /// <summary>Bakes every <see cref="CharacterDefinition"/> with a body; returns the asset paths written.</summary>
    public static IReadOnlyList<string> BakeAll()
    {
        var written = new List<string>();

        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning($"{Log} Nothing baked: the bake runs in Edit mode only.");
            return written;
        }

        var stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        if (stagePrefab == null)
        {
            Debug.LogWarning($"{Log} Nothing baked: no stage at {StagePath}.");
            return written;
        }

        List<CharacterDefinition> definitions = Definitions();
        EnsureFolder();

        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject stage = null;
        try
        {
            stage = (GameObject)PrefabUtility.InstantiatePrefab(stagePrefab, scene);
            stage.hideFlags = HideFlags.HideAndDontSave;

            var preview = stage.GetComponent<CharacterPreview>();
            Camera camera = stage.GetComponentInChildren<Camera>(true);
            camera.scene = scene;

            // Rule 5: the pedestal, and any other renderer the stage carries, is off. The body is
            // raised after this, so its renderers are untouched.
            foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            foreach (CharacterDefinition definition in definitions)
            {
                CharacterLook look = definition.ToLook();
                if (look.Body == null)
                {
                    Debug.Log($"{Log} {definition.name} has no body; skipped.");
                    continue;
                }

                string path = $"{Folder}/{FileNameFor(new ContentId(definition.Id))}.png";
                preview.Show(look);
                camera.enabled = false;
                Pose(preview.Body);

                byte[] png = Render(camera, preview.Body);
                File.WriteAllBytes(path, png);
                Import(path);

                AssignPortrait(definition, AssetDatabase.LoadAssetAtPath<Sprite>(path), path);
                written.Add(path);
                Debug.Log($"{Log} {definition.name} → {path}");
            }
        }
        finally
        {
            // The body is a child of the stage's anchor and goes with it.
            if (stage != null)
            {
                Object.DestroyImmediate(stage);
            }

            EditorSceneManager.ClosePreviewScene(scene);
        }

        AssetDatabase.SaveAssets();
        return written;
    }

    /// <summary>
    /// Rule 8: sets <paramref name="definition"/>'s portrait to <paramref name="baked"/> when the field
    /// is empty or already holds the file at <paramref name="bakedPath"/>; anything else is a painted
    /// portrait and is left alone. Returns whether it assigned.
    /// </summary>
    public static bool AssignPortrait(CharacterDefinition definition, Sprite baked, string bakedPath)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var serialized = new SerializedObject(definition);
        SerializedProperty field = serialized.FindProperty(PortraitField);
        Object current = field.objectReferenceValue;

        if (current != null && AssetDatabase.GetAssetPath(current) != bakedPath)
        {
            Debug.Log($"{Log} {definition.name} keeps its painted portrait, {AssetDatabase.GetAssetPath(current)}; " +
                      $"{bakedPath} is written but not assigned.");
            return false;
        }

        if (current != baked)
        {
            field.objectReferenceValue = baked;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);

            if (EditorUtility.IsPersistent(definition))
            {
                AssetDatabase.SaveAssetIfDirty(definition);
            }
        }

        return true;
    }

    /// <summary>
    /// Rule 4: the camera holds the top <see cref="ShoulderFraction"/> of <paramref name="body"/>'s
    /// height in a vertical field of <paramref name="fieldOfViewDeg"/>, looking at the band's middle
    /// from in front (+Z) and a little above, so it looks very slightly down.
    /// </summary>
    /// <remarks>
    /// The distance puts the band's top exactly on the field's upper edge. Raised by <c>rise</c> and
    /// looking at the band's middle from a horizontal distance <c>z</c>, a point <c>s</c> above the
    /// look-at projects to <c>s·z / (z² + rise² − s·rise)</c> of the field's half-height tangent; set to
    /// the tangent at the top, <c>s = half</c>, that is a quadratic in <c>z</c>, and the far root is the
    /// camera's. The band's bottom then sits just inside the lower edge.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The bounds have no height, or the field is not 0–180°.</exception>
    public static PortraitFraming Frame(Bounds body, float fieldOfViewDeg)
    {
        if (!(body.size.y > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(body), body, "A body to frame needs a height.");
        }

        if (!(fieldOfViewDeg > 0f && fieldOfViewDeg < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldOfViewDeg), fieldOfViewDeg, "A field of view is between 0 and 180 degrees.");
        }

        float band = body.size.y * ShoulderFraction;
        float half = band * 0.5f;
        float rise = band * RiseShare;
        float tan = Mathf.Tan(fieldOfViewDeg * 0.5f * Mathf.Deg2Rad);

        float root = Mathf.Sqrt(half * half - 4f * tan * tan * (rise * rise - half * rise));
        float distance = (half + root) / (2f * tan);

        var lookAt = new Vector3(body.center.x, body.max.y - half, body.center.z);
        return new PortraitFraming(lookAt + new Vector3(0f, rise, distance), lookAt);
    }

    /// <summary>"Portrait_" and the id's last segment in PascalCase: <c>character.ranger</c> → <c>Portrait_Ranger</c>.</summary>
    public static string FileNameFor(ContentId character)
    {
        string value = character.Value ?? string.Empty;
        string segment = value.Substring(value.LastIndexOf('.') + 1);

        var name = new StringBuilder("Portrait_", 9 + segment.Length);
        bool upper = true;
        foreach (char c in segment)
        {
            if (c == '_' || c == '-')
            {
                upper = true;
                continue;
            }

            name.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return name.ToString();
    }

    /// <summary>Rule 1: every definition in the project, in path order.</summary>
    private static List<CharacterDefinition> Definitions()
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:CharacterDefinition"))
        {
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        paths.Sort(StringComparer.Ordinal);

        var definitions = new List<CharacterDefinition>(paths.Count);
        foreach (string path in paths)
        {
            definitions.Add(AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path));
        }

        return definitions;
    }

    /// <summary>Rule 3: the first frame of the controller's default state, never a T-pose.</summary>
    private static void Pose(GameObject body)
    {
        foreach (Animator animator in body.GetComponentsInChildren<Animator>(true))
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    /// <summary>Rules 4 and 5: the camera framed on the body, rendered transparent, encoded.</summary>
    private static byte[] Render(Camera camera, GameObject body)
    {
        Transform anchor = body.transform.parent;
        Bounds world = RendererBounds(body);
        PortraitFraming framing = Frame(new Bounds(anchor.InverseTransformPoint(world.center), world.size), FieldOfViewDeg);

        camera.transform.position = anchor.TransformPoint(framing.Position);
        camera.transform.LookAt(anchor.TransformPoint(framing.LookAt), anchor.up);
        camera.fieldOfView = FieldOfViewDeg;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        int side = Side * Supersample;
        var target = new RenderTexture(side, side, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        target.Create();

        var texture = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            camera.aspect = 1f;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                throw new InvalidOperationException($"{camera.name} does not accept a URP render request.");
            }

            RenderPipeline.SubmitRenderRequest(camera, request);
            texture.SetPixels32(Downsample(Read(target), side, Supersample));
            return texture.EncodeToPNG();
        }
        finally
        {
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
        }
    }

    private static Bounds RendererBounds(GameObject body)
    {
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException($"{body.name} has no renderer to frame.");
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static Color32[] Read(RenderTexture source)
    {
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = source;
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply(false);
            return texture.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// Each <paramref name="factor"/>² block to one pixel: alpha averaged, colour averaged by alpha, so the
    /// clear colour's black never bleeds into the edge.
    /// </summary>
    private static Color32[] Downsample(Color32[] source, int sourceSide, int factor)
    {
        int side = sourceSide / factor;
        int samples = factor * factor;
        var result = new Color32[side * side];

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < factor; dy++)
                {
                    for (int dx = 0; dx < factor; dx++)
                    {
                        Color32 c = source[(y * factor + dy) * sourceSide + x * factor + dx];
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                }

                result[y * side + x] = a == 0
                    ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)((a + samples / 2) / samples));
            }
        }

        return result;
    }

    /// <summary>Rule 7: the UI sprite's import, set here so a fresh bake needs no hand-built meta.</summary>
    private static void Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        importer.spritePixelsPerUnit = 100f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = Side;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Android",
            overridden = true,
            maxTextureSize = Side,
            format = TextureImporterFormat.ASTC_4x4,
        });
        importer.SaveAndReimport();
    }

    /// <summary>Rule 6: the folder, made if it is missing.</summary>
    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
        }
    }
}
