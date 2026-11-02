# JG-25e — Gravemaw's model: a giant burrower with a maw, on a rig the owner picks

> **Parked** (owner 2026-11-02): Gravemaw moves to stage 25; Thornheart takes 20. Revise before building: cut the ring-exit probe from 25b (a plain knockback stopped by walls), one move per window in phase 1, stage numbers and roster in 25g, and the JG-20x references.

**Size:** M · **Depends on:** the owner's rig answer (open question 1) · **Branch:** `dev` (direct)
**Design refs:** GD §17.1; colour law (`Palette.Player` cyan is the player's alone, `Palette.Danger` is telegraphs'); art source ruling

## Goal

A ready Gravemaw body with the clips JG-20f needs, built so a better sculpt can replace the mesh
without touching anything else.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/Enemies/Gravemaw/Gravemaw.fbx` | — | **New.** Mesh, rig, seven clips (rule 3) |
| `Art/Enemies/Gravemaw/T_Gravemaw_Albedo.png` | — | **New.** Earth-brown hide, pale bone teeth, mossed back |
| `Art/Enemies/M_Gravemaw.mat` | — | **New.** The enemy material on the new albedo |
| `Prefabs/Enemies/Gravemaw.prefab` | — | **New.** Variant of `Enemy.prefab`: `Model` child, material |
| `Tests/Game/Art/GravemawModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Body:** one `SkinnedMeshRenderer` under the base's `Model` child, scale 1; the 3× size is the
   data's `BodyScale` (20g). No code names the mesh, so the owner's sculpt replaces mesh and albedo
   only.
2. **Colour:** no texel in the cyan band (175–200°, saturation > 0.35) and none within ΔE 20 of
   `Danger`; hide darker than the arena floor so it reads against it.
3. **Clips**, named exactly: `Idle`, `Rise` (0.5 s, erupting), `Dive` (0.5 s), `Spit`, `Tremor`,
   `Hit`, `Death`. Root node empty; `applyRootMotion` off; no Animation Events.
4. **Maw** is the readable weak point: an open mouth with a bright inner, so the window reads.
5. Budget: within the crowd budget `MossguardModelTests` uses; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Gravemaw_IsAVariantOfEnemy_OneModelChild` / `Gravemaw_OneSkinnedBody` (1) |
| `Gravemaw_PaintHasNoCyanAndNoDanger` (2) |
| `Gravemaw_HasTheSevenClips_NoEvents_NoRootMotion` (3) |
| `Gravemaw_StaysInBudget_EveryVertexWeighted` (5) |

## Manual verification (Editor / device)

1. **[Editor]** Scene view beside a Mossguard at their data scales: clearly huge, maw readable at
   phone zoom, hide separate from the Jungle floor.

## Out of scope

- Animator, ring, trail and views (20f); the data asset and scale (20g).

## As built

_Filled at merge._
