# Image-to-3D source workflow

Use this workflow for future generated characters and props. Mossguard's repaired source was
approved by the owner on 2026-10-31. CLI/MCP only; keep the running TRELLIS server alone when idle.

## Generate and choose

1. Preserve the reference and raw output. Identify silhouette, proportions, face, colors and
   attachments before generating. A single front view does not establish the back or depth.
2. Use the existing local TRELLIS setup rather than install another generator. Its launchers live
   in `C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/`; ComfyUI serves `127.0.0.1:8188`.
   Inspect `/queue` first. Never interrupt another job. The tested HQ profile uses 512/Q8,
   25 sampling steps, 100k faces, 2048 textures and seeds 42–44. Higher modes are not tested here.
3. Render every candidate from front, side and back under the same lighting. Pick by likeness,
   not seed number. Mossguard used seed 42. Missing irises were restored as separate small meshes;
   that was a character-specific correction, not something to apply to every source.

## Repair before importing or rigging

`repair_scan.py` accepts a GLB whose largest mesh has one textured material and UVs. Other meshes,
such as modeled eyes, are retained. The source is never overwritten. Blender 4.1, headless:

```powershell
$env:PYTHONHASHSEED = '0'
& 'C:/Program Files/Blender Foundation/Blender 4.1/blender.exe' --background --factory-startup --python Tools/Blender/repair_scan.py -- '<raw.glb>' '<closed.glb>' --preview-dir '<review-directory>' --blend '<review.blend>' --report '<repair-report.json>'
```

The procedure that fixed Mossguard:

- Inspect opacity, transmission and backface culling separately from topology. A disappearing
  rear can come from wrong-facing/open polygons even when the material is opaque.
- Preserve the original textured surface as the transfer source. Weld the working copy, recalculate
  normals, add a thin shell with **simple extrusion and even offset off**, then voxel reconstruct.
  Defaults are thickness 0.006 and voxel size 0.002 times the largest source dimension.
- Limit the source reconstruction to 180k triangles. This is a detailed master budget, not the
  game's crowd budget. Reject a distorted bounding box or loss of the main surface.
- Rebuild a fresh mesh data block to discard invalid scan/modifier corner data. Remove isolated
  degenerate specks, fill remaining small rims, and verify **zero boundary/non-manifold edges**.
- Unwrap a fresh UV layout. For each new face, choose one nearest source triangle and interpolate
  only within that triangle's UV island. Bake color without lighting. Independently interpolating
  UVs chosen per vertex crosses unrelated islands and produces colored speckles.
- Use explicit opaque alpha, zero transmission, double-sided materials and smooth shading.
  Verify the GLB actually contains the body mesh, not just its object name or eyes.
- Reimport the exported model and compare front/back/side silhouettes, materials and textures.
  Inspect the rear and thin armor in rendered previews. Edge counts alone are insufficient.
  Check the saved Blender file again; saving can expose invalid data left in memory.

Do not repair by turning on transparency or merely hiding backface culling. Do not voxelize an
open fragmented scan without closing its sheets first: that can keep only small enclosed parts.
Do not use solidify's even offset on this scan: thin/degenerate corners can explode outward.

The optional Blender review opens to the back camera with material preview and X-ray off.
Save a resumed review to a new filename if Blender has loaded the previous one as a library.

## Keep source approval separate from game conversion

Store the approved source under `Sources/` with Git LFS. Mossguard's `Mossguard_src.glb` is the
approved repaired master (SHA-256 `c2e4998b7ebbaF4aa212c8905bed4da3e5a9785da8ef695ed00d354509d9c39b`,
case-insensitive). The original raw outputs remain in the TRELLIS output directory/review archive.

Import a clearly named source preview when requested. Never wire its dense static mesh into enemy
spawning. JG-14b still requires the unchanged KayKit rig, a 1,200–2,200-triangle body, atlas repaint,
valid skin weights and clip checks, plus the shield. Preserve the approved master throughout.
