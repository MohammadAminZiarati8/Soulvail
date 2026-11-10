# SS-08b — The model: the owner's viper on Rig_Viper, nine clips, turnaround first

**Size:** M · **Depends on:** — · **Branch:** `ss-08b-viper-model`
**Design refs:** GD §8.8, §3 › The Sunscar, §16.4, §17.1

## Goal

`Viper.fbx`: the owner's sculpt, repaired, on a new legless `Rig_Viper` built as `scorpion.py` builds `Rig_Scorpion`, with nine clips.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Tools/Blender/viper.py` | — | repair, rig, bind, clips, checks; `brood.py`'s shape |
| `Tools/Blender/Sources/Viper_src.glb` | — | the sculpt, SHA-256 pinned |
| `Assets/_Project/Art/Enemies/Viper.fbx` (+ `.meta`) | — | Generic, clips split, in place |
| `Assets/_Project/Tests/Game/Art/ViperModelTests.cs` | Tests.Game | `BroodMotherModelTests`' rows |
| *small edits* | | `enemy_atlas.py` + `T_Enemy_Albedo.png`: `viper_*` swatches |

## Behaviour

1. **The source.** Copy the owner's `Viper-9ddc7b93-seed42_00001_.glb` (`Documents/Codex/2026-10-23/sa/outputs/generated/`, read-only) to `Sources/Viper_src.glb`, SHA-256 `4a2bdfeb1ec8e83eb2f2c093680f157c7672902ea3cf8539c59507e316808598`. Inspected: one trimesh mesh, 97 856 tris, UVs, no normals, baked colour; bounds 0.45 × 0.14 × 1.0 (Y up). A thin body, nearly straight, ~20° off Z with a slight bow; head the dense +Z end, tail tapering to −Z.
2. `repair_scan.py` closes it; reduce to 1 500–3 000 tris. Turn the head-to-tail chord onto Unity forward, head ahead; scale to **2.6 m** nose to tail.
3. **Turnaround first.** `--turnaround <png>` renders the unbound body front, side, back, top to `Temp/viper_turnaround.png` (uncommitted). Rigging waits for the owner's OK.
4. `Rig_Viper`, built in `viper.py` (`scorpion.py`'s `armature`/`pose`): `Root` (never moves) → `Spine_01` at the neck, the model's origin → `Spine_02` … `Spine_12` along the fitted centreline to the tail tip; `Spine_01` → `Head` → `Jaw`. No leg bones. Rest pose is the sculpt's bow.
5. Bound by distance to bone segments (`brood.py`'s `bind`), smoothed at joints, ≤ 4 weights, none unweighted.
6. Clips, 30 fps, in place, `Root` still: loops `Idle` 48 f, `Slither` 24 f (S-wave, 2.4 m/s), `Sidewind` 24 f (lateral, 2.8 m/s, head steady); `Coil` 27 f (tight S, head back, held from f 20); `Strike` 12 f (straight, jaw open); `Recoil` 30 f; `Stretched` 60 f (straight 45 f, then slow recoil); `Hit` 12 f; `Death` 30 f (belly up).
7. One skinned mesh, one material slot (`M_Enemy`).
8. Palette: `viper_*` swatches from the baked tones (dusty ochre, umber bands, bone belly), darker than the Dunes sand. No cyan, nothing near `#FF4A1F` (`RESERVED`). Insets only; no other swatch moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `ViperModel_IsShippedInBudget` | fbx / load / one `SkinnedMeshRenderer`, `M_Enemy` only, 1 submesh, rule 2's tris |
| `ViperModel_IsWeighted` | skin / read / all weighted, ≤ 4 |
| `ViperModel_HasRigViper` | rig / walk / rule 4's chain in order, `Head`, `Jaw`; no other bones |
| `ViperModel_ImportsItsClips` | importer / read / Generic; rule 6's clips, frames, loops |
| `ViperModel_ClipsLeaveRootAlone` | each clip / sample / `Root` still |
| `ViperModel_IsLongAndLow` | rest bounds / measure / 2.4–2.8 m long, ≥ 6 × width and height |
| `ViperModel_HeadLeads` | rest / `Head` / ahead (+Z) of the origin |
| `ViperModel_CoilTightens` | `Coil` f 26 vs `Strike` f 11 / head-to-tail extent / ≤ 0.6 × |
| `ViperModel_StandsOffTheDunes` | colour / vs Dunes sand / `ScorpionModel_StandsOffTheDunes`' margins |
| `ViperModel_NoCyanOrDanger` | swatches / hue / none in the reserved bands |
| `ViperSource_HashPinned` | source / hash / rule 1's |

Every other model test stays green (rule 8).

## Manual verification (Editor / device)

1. **[Editor]** Turnaround approved by the owner before rigging.
2. **[Editor]** Clips previewed: `Coil` a tight S from above; `Sidewind` keeps the head steady.

## Out of scope

- Prefab, animator, strip (SS-08c). Data (SS-08d). Shells; a variant.

## As built

_Commit body._
