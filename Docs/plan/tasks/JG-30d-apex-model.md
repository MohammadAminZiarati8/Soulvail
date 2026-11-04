# JG-30d — The Apex's model on `Rig_Panther`

**Size:** M · **Depends on:** the owner's TRELLIS sculpt (prompt below) · **Branch:** `dev` (direct)
**Design refs:** GD §17.1; colour law (`Palette.Player` cyan is the player's, `Palette.Danger` is telegraphs'); art source ruling; `Tools/Blender/panther.py`

## Goal

A panther matriarch clearly bigger and heavier than any panther, built the way the Panther was:
a TRELLIS sculpt, repainted onto the enemy atlas and rigged on `Rig_Panther`, with the clips the
Apex needs.

## Owner step: the sculpt

Make the picture in Gemini, run it through TRELLIS, and save the result as
`Tools/Blender/Sources/Apex_trellis.glb`. Prompt:

```
A huge jungle panther matriarch, full body, side three-quarter view, standing on all four legs,
plain white background. Stylised low-poly game character in the KayKit style: smooth simple
shapes, no fur strands. Heavy shoulders, long body, long thick tail, a short thick ruff of fur
around the neck, ears back. Dark charcoal-violet coat with pale bone-white scar stripes along the
flanks and a pale muzzle. Eyes small and dark. Mouth closed. No armour, no saddle, no props, no
cyan, no bright red.
```

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/apex.py` (repo root) | — | **New.** `panther.py`'s pipeline on the new sculpt, with the clips below |
| `Art/Enemies/Apex.fbx` | — | **New.** One skinned mesh on `Rig_Panther`, its clips |
| `Art/Enemies/Textures/T_Enemy_Albedo.png` | — | Gains the coat and scar swatches (`enemy_atlas.py`) |
| `Prefabs/Enemies/Apex.prefab` | — | **New.** Variant of `Enemy.prefab`: `Model` child |
| `Tests/Game/Art/ApexModelTests.cs` | Tests.Game | **New** |
| *small edits* | | `Tools/Blender/enemy_atlas.py` gains two swatches |

## Behaviour

1. **Body:** one `SkinnedMeshRenderer` under the `Model` child, scale 1. Her size is the data's
   `BodyScale` (30f). No code names the mesh, so a better sculpt replaces it alone.
2. **Rig:** `Rig_Panther`'s bone names, plus a `Head` bone the view hangs the eye glow on (30e).
3. **Colour:** no texel in the cyan band (175–200°, saturation > 0.35), none within ΔE 20 of
   `Danger`; coat darker than the Jungle floor. The eyes are dark, so only the view lights them.
4. **Clips**, named exactly: `Idle`, `Run`, `Crouch` (holdable), `Pounce` (airborne), `Land`,
   `Dash`, `Rake` (two swipes, 1.0 s), `Roar`, `Hit`, `Death`. Root still; no root motion; no
   Animation Events.
5. **Budget:** within the crowd budget `MossguardModelTests` uses; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Apex_IsAVariantOfEnemy_OneModelChild` / `Apex_OneSkinnedBody` (1) |
| `Apex_RigHasPantherBones_AndHead` (2) |
| `Apex_PaintHasNoCyanAndNoDanger` (3) |
| `Apex_HasTheTenClips_NoEvents_NoRootMotion` (4) |
| `Apex_StaysInBudget_EveryVertexWeighted` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Beside a Shadow panther at data scales: clearly the matriarch, scars readable at
   phone zoom, coat separate from the floor.

## Out of scope

- Animator, eye glow, streak and view (30e); data and scale (30f).
