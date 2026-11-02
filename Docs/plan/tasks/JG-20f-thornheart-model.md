# JG-20f — Thornheart's model: a strangler tree round a glowing heart, and its pod

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §17.1; colour law (`Palette.Player` cyan is the player's alone, `Palette.Danger` is telegraphs'); art source ruling (generated models; a tree needs no KayKit rig)

## Goal

A ready Thornheart body and pod body with the clips JG-20g needs, built from parts so a better
sculpt can replace any mesh without touching code.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/Enemies/Thornheart/Thornheart.fbx` | — | **New.** Parts: `Trunk`, `Crown`, `Heart`, `Root_0..5` |
| `Art/Enemies/Thornheart/T_Thornheart_Albedo.png` | — | **New.** Grey-green bark, thorn tips, moss |
| `Art/Enemies/M_Thornheart.mat` | — | **New.** The enemy material on the new albedo |
| `Animation/Clips/A_Thornheart_*.anim` | — | **New.** Seven Generic transform clips (rule 3) |
| `Prefabs/Enemies/Thornheart.prefab` | — | **New.** Variant of `Enemy.prefab`: `Model` child, parts, material |
| `Prefabs/Enemies/ThornPod.prefab` | — | **New.** Variant of `Pitcher_Bud.prefab`: thorned shell, own tint |
| `Tests/Game/Art/ThornheartModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Body:** rigid parts under the base's `Model` child, scale 1; size is the data's `BodyScale`
   (20h). No skeleton: clips animate the parts' transforms (Generic). No code names a mesh.
2. **Source** (owner, 2026-11-02): TRELLIS `Thornheart-a18b1dc0-seed43_00001_.glb` and
   `ThornPod-7a6a1b10-seed42_00001_.glb` in `C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated`
   (read-only; copy, never edit there). Both are open and self-overlapping, so each is rebuilt in
   Blender 4.1: voxel remesh to one closed shell, decimate to budget, bake the TRELLIS colour onto
   new UVs, then split the heart crystal, leaf ring, trunk (with shrine) and the six roots.
3. **Heart:** its own renderer in the crater atop the trunk, open to the sky, repainted gold (off
   `Danger`), with an emissive channel 20g lights — the readable weak point.
4. **Clips**, named exactly: `Idle` (sway, loops), `Roots` (arms dig), `Slam` (one root swings
   down), `Plant`, `Exposed` (bark parts open), `Hit`, `Death`. Root node empty;
   `applyRootMotion` off; no Animation Events.
5. **Colour:** no texel in the cyan band (175–200°, saturation > 0.35) and none within ΔE 20 of
   `Danger`; bark darker than the Jungle floor. The pod differs from the Pitcher bud at a glance.
6. **Budget:** within the crowd budget `MossguardModelTests` uses.

## Tests

| Test | Given / When / Then |
|---|---|
| `Thornheart_IsAVariantOfEnemy_HasTheParts` (1) / `Thornheart_Closed_NoBoundaryEdges` (2) |
| `Thornheart_HeartHasEmissiveRenderer` (3) |
| `Thornheart_HasTheSevenClips_NoEvents_NoRootMotion` (4) |
| `Thornheart_PaintHasNoCyanAndNoDanger` / `ThornPod_IsABudVariant_OwnTint` (5) |
| `Thornheart_StaysInBudget` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Scene view beside a Mossguard at data scales: clearly huge, the heart crater readable
   at phone zoom, the pod not mistaken for a Pitcher bud.

## Out of scope

- Animator controller and views (20g); data and scale (20h). The owner may hand over a sculpt later;
  it replaces the meshes only.

## As built

_Filled at merge._
