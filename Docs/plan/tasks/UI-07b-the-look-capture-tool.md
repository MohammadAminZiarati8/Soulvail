# UI-07b — A look-capture tool

**Size:** S · **Depends on:** UI-07a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** — · **Ledger rows:** none

## Goal

One Editor menu item writes a PNG of what the Device Simulator shows — the world and every screen-space canvas, composited — so a layout is checked from a file at a phone's size and density, not by eye in the Game view.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Editor/Look/LookCapture.cs` | Editor | the menu item `Soulvail/Capture Look`; renders both passes, composites, writes |
| `Editor/Look/LookCompositor.cs` | Editor | pure: alpha-composites the UI pass over the world pass, names the file |
| `Tests/Game/Editor/LookCompositorTests.cs` | Tests.Game | one row per pure rule |
| *small edits* | | `Soulvail.Tests.Game.asmdef` references `Soulvail.Editor` |

## Public API

```csharp
namespace Soulvail.Editor.Look
{
    public static class LookCapture        // [MenuItem("Soulvail/Capture Look")], Play mode only
    { public static string Capture(); }    // the PNG's path, or null with a logged reason

    public static class LookCompositor
    {
        public static Color32[] Over(Color32[] world, Color32[] ui);   // same length, straight alpha
        public static string FileName(string scene, int width, int height, float dpi, DateTime at);
        // "Run_1080x2400_420dpi_20261014-153012.png"
    }
}
```

## Behaviour

1. **Play mode only, Simulator only.** Outside Play, or with a Game view window open beside the Simulator, the item logs why and writes nothing: `Screen.*` mixes the two views' values while both are open, so a capture taken then is of no device ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)).
2. **The world pass** renders the scene's main camera into a `RenderTexture` of `Screen.width × Screen.height` through `RenderPipeline.SubmitRenderRequest` with a `UniversalRenderPipeline.SingleCameraRequest`. Neither `Camera.Render` (not URP's path) nor `ScreenCapture` (it reads the Game view, not the Simulator) is used.
3. **The UI pass** renders every active Screen Space – Overlay canvas into a second texture of the same size: each is switched to Screen Space – Camera on a capture camera that clears to transparent, rendered in `sortingOrder`, and switched back in a `finally`. The Run scene's canvases are on `Default`, beside the world ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)), so no culling mask separates them: the world's renderers are disabled for the UI pass and restored after it.
4. **`Over`** composites the UI over the world pixel by pixel with straight alpha; arrays of unequal length throw.
5. **The file** goes to `Soulvail_Looks/` beside the repository (where `Soulvail_GeminiSamples/` already lives), named by `FileName` from the active scene, the size and `Screen.dpi`, and its path is logged. Nothing enters `Assets/` or git.
6. **No scene state outlives the capture**: canvases' render modes, cameras and renderers are as they were, and the capture camera and textures are destroyed.

## Tests

| Test | Given / When / Then |
|---|---|
| `Over_OpaqueUi_IsTheUi` | rule 4 |
| `Over_TransparentUi_IsTheWorld` | rule 4 |
| `Over_HalfAlpha_Blends` | rule 4: red world under blue UI at alpha 128 → within 1 of (127, 0, 128, 255) |
| `Over_LengthMismatch_Throws` | rule 4 |
| `FileName_NamesSceneSizeDpiAndTime` | rule 5 |

Rules 1–3 and 6 are Editor-window and render-pipeline behaviour; they are the manual steps.

## Manual verification

1. **[Editor]** Menu, then a run, in the Simulator at 2658 × 960 @ 480 dpi with the Game view closed: the PNG matches the Simulator, canvases over the world, and finding 1's broken page reproduces in the file.
2. **[Editor]** With the Game view open too, the item refuses and says why.
3. **[Editor]** After a capture the run plays on and the Console is clean.
4. **[Editor]** Opening the Simulator adds `com.unity.device-simulator.devices` to `Packages/manifest.json`: revert it before handing over ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)).

## Out of scope

Several sizes in one click, comparison against a reference image, and a `Screen` seam for the presenters (each reads `Screen.dpi` itself; the tool works with that).

## As built

As specified, with four deviations.

1. **`LookCompositor.Unpremultiply`** is a public member the spec did not list. UGUI and TMP blend `One OneMinusSrcAlpha`, so the UI pass reads back premultiplied over the transparent clear, and rule 4's straight-alpha `Over` would darken every antialiased edge. `LookCapture` unpremultiplies the UI pass before `Over`. It has no test row of its own; the Run capture's text edges are the check ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)).
2. **`Soulvail.Editor.asmdef`** gained `UnityEngine.UI`, `Unity.RenderPipelines.Core.Runtime` and `Unity.RenderPipelines.Universal.Runtime`, for `Canvas` and `SingleCameraRequest`. A small edit beyond the Files table.
3. **Rule 3 renders once, not once per canvas.** Every overlay canvas goes onto one capture camera, and canvases on one camera sort by sorting layer and order, as overlay canvases do. The capture camera also sits 10 000 units below the arena, so no world-space canvas falls in its view.
4. **The menu item is greyed out outside Play** (a validate function), as well as `Capture()` refusing with a reason.

**Manual verification, done by Claude through the MCP:** in the Simulator at 2658 × 960 @ 480 dpi, with the Game view open the capture refused and logged why (`Screen` read 480 × 800 @ 120 at that moment); with the Game view closed it wrote `Menu_2658x960_480dpi_….png` and `Run_2658x960_480dpi_….png` to `Soulvail_Looks/`, both matching the Simulator: the HUD, stick and dash button over the arena, with no fringes. After the capture no overlay canvas was left in camera mode, the capture camera was gone, the renderers that were off before were the only ones off after, and the run played on with a clean Console. **Finding 1's broken page was not reproduced in a file**: the run was entered through Continue, so the class page was not shown; it is UI-08d's to capture. The Menu capture does reproduce [UI-07a](UI-07a-the-look-pass-specs.md)'s finding 2: the title and both buttons draw in LiberationSans. With the Simulator closed, the item refuses with "no Device Simulator window is open". `Packages/manifest.json` was not modified this time (the Simulator was opened through `EditorWindow.GetWindow`), so there was nothing to revert.
