# JG-12d — Mother Pitcher's model

**Size:** M · **Depends on:** the owner's sculpt (below) · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion)

**Before the go:** the owner makes the sculpt the Panther's way: a Gemini picture (prompt in the
JG-12 report), image-to-3D, holes fixed. It lands as `Tools/Blender/Sources/MotherPitcher_src.glb`.

## Goal

`MotherPitcher.fbx`: one skinned, smooth-shaded mesh of 400–2,500 triangles on its own
`Rig_MotherPitcher`, with five clips, painted from the shared atlas, made by one script.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/mother_pitcher.py` | Blender 4.1 | **New.** Cuts, repaints, rigs and animates; `--pose-sheet <png>` |
| `Tools/Blender/Sources/MotherPitcher_src.glb` | LFS | **New.** The owner's sculpt, the only input |
| `Assets/_Project/Art/Enemies/MotherPitcher.fbx` | generated | The model and clips; `.meta` holds the import |
| `Assets/_Project/Tests/Game/Art/MotherPitcherModelTests.cs` | Tests.Game | **New.** Rules 2–7 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/mother_pitcher.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script, same bytes.** A re-run writes the same FBX (header timestamp aside). It does not
   write the atlas, which is full (JG-07b).
2. **The crowd's budget.** One skinned mesh, 400–2,500 triangles, smooth-shaded, one slot → `M_Enemy`.
3. **Every vertex weighted** to ≤ 4 bones summing to 1, none on `root`. Bones: `root`, `base`,
   `stem_1`–`stem_3`, `cup`, `lid`, `leaf_1`–`leaf_4`.
4. **Generic, five clips at 30 fps, in place:** `Idle` (60 f, loop, a slow sway), `Barrage`
   (24 f, the cup rears and heaves), `Spray` (30 f, the cup tips forward, lid wide, held from 10),
   `Hit` (12 f), `Death` (40 f, wilts and folds). Own avatar, not optimised.
5. **Every clip binds; none moves `root`.**
6. **Repainted from existing swatches**: the Pitcher's columns 4–5 and the Frog's mouth and pupil.
   Averaged luma 0.15–0.45, saturation ≤ 0.4; no reserved colour
   (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).
7. **A squat giant, not a tall jug.** At rest its width is at least 0.7 × its height, which tells
   it from the Pitcher (narrow) in silhouette.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `MotherModel_IsShipped` | the model / loaded / one `SkinnedMeshRenderer` (guards every row) |
| `MotherModel_StaysInTheCrowdsBudget` / `MotherModel_UsesTheEnemyMaterial` | mesh, renderer / read / as rule 2 |
| `MotherModel_EveryVertexIsWeighted` | bone weights / read / as rule 3 |
| `MotherModel_ImportsGenericWithItsClips` | importer, clips / read / Generic, five clips, one loop, 30 fps (rule 4) |
| `MotherModel_EveryClipBindsAndLeavesTheRootAlone` | each clip, every frame / sampled / as rule 5 |
| `MotherModel_IsDarkButReadable` | atlas under each triangle / averaged / as rule 6 |
| `MotherModel_IsSquat` | mesh at rest / bounds / as rule 7 |

Rule 1 is checked by hand: two runs and a byte compare.

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet beside the Pitcher and the Croaker: it sways, rears, sprays, flinches
   and wilts. **The owner rules whether the sculpt stays**; if not, a `pitcher.py`-style shell build
   is a new task.

## Out of scope

- The prefab and animator (JG-12e). A bud model: buds wear `Pitcher.fbx`. Growing the atlas.

## As built

_Filled at merge._
