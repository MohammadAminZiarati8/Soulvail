using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Soulvail.Editor.Look;

/// <summary>
/// <c>Soulvail/Capture Look</c>: a PNG of what the Device Simulator shows, the world and every
/// Screen Space – Overlay canvas composited, at the simulated phone's size (UI-07b).
/// </summary>
/// <remarks>
/// <c>ScreenCapture</c> reads the Game view, not the Simulator, and <c>Camera.Render</c> is not URP's
/// path, so both passes are rendered on demand through <c>SubmitRenderRequest</c>. The Run scene's
/// canvases share the world's layer, so the UI pass hides the world by disabling its renderers rather
/// than by a culling mask (Traps §9).
/// </remarks>
public static class LookCapture
{
    private const string MenuPath = "Soulvail/Capture Look";
    private const string FolderName = "Soulvail_Looks";
    private const string GameViewType = "UnityEditor.GameView";
    private const string SimulatorType = "UnityEditor.DeviceSimulation.SimulatorWindow";

    /// <summary>Far below the arena, so no world-space canvas falls inside the UI camera's view.</summary>
    private static readonly Vector3 UiCameraPosition = new(0f, -10000f, 0f);

    private const float UiPlaneDistance = 10f;

    [MenuItem(MenuPath)]
    private static void CaptureFromMenu() => Capture();

    /// <summary>Writes the capture and returns its path, or logs why not and returns null.</summary>
    public static string Capture()
    {
        string refusal = Refusal();
        if (refusal != null)
        {
            Debug.LogWarning($"[LookCapture] Nothing written: {refusal}");
            return null;
        }

        Camera world = Camera.main;
        if (world == null)
        {
            Debug.LogWarning("[LookCapture] Nothing written: the scene has no camera tagged MainCamera.");
            return null;
        }

        int width = Screen.width;
        int height = Screen.height;
        float dpi = Screen.dpi;

        Color32[] worldPixels = RenderWorld(world, width, height);
        if (worldPixels == null)
            return null;

        Color32[] uiPixels = RenderUi(width, height);
        if (uiPixels == null)
            return null;

        LookCompositor.Unpremultiply(uiPixels);
        Color32[] composite = LookCompositor.Over(worldPixels, uiPixels);

        string folder = Path.Combine(Path.GetDirectoryName(ProjectRoot()), FolderName);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(
            folder, LookCompositor.FileName(SceneManager.GetActiveScene().name, width, height, dpi, DateTime.Now));

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(composite);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        Debug.Log($"[LookCapture] {width} × {height} @ {dpi:0} dpi → {path}");
        return path;
    }

    [MenuItem(MenuPath, true)]
    private static bool CanCapture() => EditorApplication.isPlaying;

    /// <summary>Rule 1: Play mode, a Simulator, and no Game view beside it.</summary>
    private static string Refusal()
    {
        if (!EditorApplication.isPlaying)
            return "the capture works in Play mode only.";

        bool simulator = false;
        bool gameView = false;
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            string type = window.GetType().FullName;
            simulator |= type == SimulatorType;
            gameView |= type == GameViewType;
        }

        if (!simulator)
            return "no Device Simulator window is open.";
        if (gameView)
        {
            return "a Game view is open beside the Simulator, and Screen.* mixes the two views' values " +
                   "while both are open. Close the Game view and capture again.";
        }

        return null;
    }

    private static Color32[] RenderWorld(Camera camera, int width, int height)
    {
        RenderTexture target = NewTarget(width, height);
        try
        {
            return Submit(camera, target) ? Read(target) : null;
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    /// <summary>Rule 3: every overlay canvas onto a transparent camera, the world's renderers off.</summary>
    private static Color32[] RenderUi(int width, int height)
    {
        var canvases = new List<CanvasState>();
        var renderers = new List<Renderer>();
        RenderTexture target = NewTarget(width, height);
        var cameraObject = new GameObject("LookCapture.UiCamera") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            cameraObject.transform.position = UiCameraPosition;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = UiPlaneDistance * 2f;
            camera.targetTexture = target;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled)
                    continue;
                renderer.enabled = false;
                renderers.Add(renderer);
            }

            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;
                canvases.Add(new CanvasState(canvas));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = UiPlaneDistance;
            }

            if (canvases.Count == 0)
            {
                Debug.LogWarning("[LookCapture] Nothing written: no active Screen Space – Overlay canvas.");
                return null;
            }

            // One render draws them all in sorting order: canvases on one camera sort by their
            // sorting layer and order, as overlay canvases do.
            Canvas.ForceUpdateCanvases();
            return Submit(camera, target) ? Read(target) : null;
        }
        finally
        {
            for (int i = 0; i < canvases.Count; i++)
                canvases[i].Restore();
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }

            if (canvases.Count > 0)
                Canvas.ForceUpdateCanvases();
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(target);
        }
    }

    private static bool Submit(Camera camera, RenderTexture target)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
        {
            Debug.LogWarning($"[LookCapture] Nothing written: {camera.name} does not accept a URP render request.");
            return false;
        }

        RenderPipeline.SubmitRenderRequest(camera, request);
        return true;
    }

    private static RenderTexture NewTarget(int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        target.Create();
        return target;
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

    private static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath);

    /// <summary>What rule 6 puts back on a canvas.</summary>
    private readonly struct CanvasState
    {
        private readonly Canvas _canvas;
        private readonly RenderMode _renderMode;
        private readonly Camera _worldCamera;
        private readonly float _planeDistance;

        public CanvasState(Canvas canvas)
        {
            _canvas = canvas;
            _renderMode = canvas.renderMode;
            _worldCamera = canvas.worldCamera;
            _planeDistance = canvas.planeDistance;
        }

        public void Restore()
        {
            if (_canvas == null)
                return;
            _canvas.renderMode = _renderMode;
            _canvas.worldCamera = _worldCamera;
            _canvas.planeDistance = _planeDistance;
        }
    }
}
