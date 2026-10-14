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

_Filled at merge._
