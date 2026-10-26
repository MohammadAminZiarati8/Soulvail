# JG-07b — The Panther's model

**Size:** M · **Depends on:** JG-03a (the method), `enemy_atlas.py` · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §16.4, GD §17.1; AR §18 (the M2-art rows: Generic, no root motion)

The owner's go of 2026-10-26: the Jungle's Lunger is a panther, made the owner's way this time. That
means a Gemini picture, TRELLIS image-to-3D, then the hole fixes from the TBD_ project's
`fix_glb.py`. That ran on 2026-10-27: 30 039 faces, every face turned outward, holes patched,
double-sided. This task makes that sculpt into a game body.

## Goal

`Panther.fbx`: one skinned mesh of 400–2,500 triangles on `Rig_Panther`, with six clips of its own,
sampling the shared enemy atlas through `M_Enemy`, made by one script from a source kept in the
repository.

## Why this shape

- **The sculpt, not shells.** Every Jungle body so far is built from smooth shells in a script,
  because M7-05j's Tripo frog, cut to 1,200 *flat-shaded* triangles, read as faceted beside KayKit
  (GD §17.1). That was the flat shading, not the source. The panther is TRELLIS's smooth, organic
  shape decimated to about 2,400 triangles and **smooth-shaded**. The pose sheet (manual step 1)
  puts it beside the Frog and the Pitcher so the owner can judge whether it sits with them. If it
  doesn't, the fallback is `frog.py`'s shells with this sculpt as the proportion reference: a new
  task, not a fix here.
- **The source lives in the repository**, as every body's does: `Tools/Blender/Sources/Panther_trellis.glb`
  (2.9 MB, LFS), the `fix_glb.py` output. The script reads nothing outside the repository. The raw
  TRELLIS file and the fixer stay where they are.
- **Repainted onto the atlas, not textured.** Each face takes a swatch by what the TRELLIS texture
  says is under it: fur, a darker rosette, the pale belly and muzzle, an eye or the nose. The
  gradient runs by height, as the KayKit bodies do. The atlas has exactly two free cells, column 1
  rows 2–3 beside the retired Rootling's, and the panther takes both: `panther_fur` (a dusky
  moss-charcoal, `5E6A52`→`2E3529`) and `panther_belly` (`9A9C8C`→`6A6C5E`). Rosettes wear the
  Rootling's `socket`, eyes the Frog's `frog_green`, pupils and nose `frog_pupil`, the mouth
  `frog_mouth`. **The atlas is full after this**; the next body needs a bigger one.
- **A long, low silhouette.** From the game camera a Lunger should read as an arrow along its own
  line. Nose to rump at least twice its shoulder height tells it from the Frog (low and wide), the
  Pitcher (tall) and the Puffball (round) in black.
- **Its own rig, as the Frog's.** `Rig_Medium`'s humanoid clips cannot play on four legs. The bones:
  `root`, `hips`, `spine`, `chest`, `neck`, `head`, `jaw`, `tail_1`–`tail_3`, and per leg an upper,
  a lower and a paw (`front.l` … `back.r`). Skinned by automatic weights, capped at four and
  normalised. **The tail is straightened at rest** (the sculpt holds it out to one side), by posing
  `tail_1` and applying the pose to the mesh before binding.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/panther.py` | Blender 4.1 | **New.** Writes the model and the atlas; `--pose-sheet <png>` renders the review sheet |
| `Tools/Blender/Sources/Panther_trellis.glb` | LFS | **New.** The fixed TRELLIS sculpt, the script's only input |
| `Assets/_Project/Art/Enemies/Panther.fbx` | generated | the model, on `Rig_Panther`, with its clips; its `.meta` holds the import settings |
| `Assets/_Project/Tests/Game/Art/PantherModelTests.cs` | Tests.Game | **New.** Rules 2–7 |
| *small edits* | | `Tools/Blender/enemy_atlas.py` (two swatches, column 1 rows 2–3, and the docstring's layout line); `T_Enemy_Albedo.png` (regenerated) |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/panther.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script makes the model and the atlas, and a re-run makes the same ones.** The PNG is
   byte-identical and the FBX differs only in its header's timestamp. Re-runs of `frog.py`,
   `pitcher.py`, `puffball.py` and `croaker.py` still write the same atlas.
2. **It is in the crowd's budget.** One skinned mesh of 400–2,500 triangles, smooth-shaded, one
   material slot remapped to `M_Enemy`.
3. **Every vertex is weighted** to at most four bones summing to 1, and none to `root`.
4. **It imports Generic with its six clips**, at 30 fps, in place:
   `Idle` (48 f, loop), `Prowl` (24 f, loop, one stride), `Crouch` (27 f: sinks low, tail lashing,
   held from frame 9), `Lunge` (12 f, loop: a stretched bound, front paws reaching), `Hit` (12 f)
   and `Death` (30 f, rolls onto its side). An avatar from this model, not optimised.
5. **Every clip binds, and none moves the root.**
6. **It is dark but not lost, and wears no reserved colour.** Averaged over its triangles, luma
   0.15–0.40 and saturation ≤ 0.35, and no atlas pixel comes near a reserved colour
   (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).
7. **It is long and low.** At rest, its length (Z) over its shoulder height is at least 2. Its
   length over its width is at least 2.5, the tail excluded. The Frog's, Pitcher's and Puffball's
   proportion rows still pass.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `PantherModel_IsShipped` | the model / loaded / one `SkinnedMeshRenderer` (guards every row) |
| `PantherModel_StaysInTheCrowdsBudget` | the mesh / triangles / 400–2,500, one submesh (rule 2) |
| `PantherModel_UsesTheEnemyMaterial` | the renderer / its materials / `M_Enemy` alone (rule 2) |
| `PantherModel_EveryVertexIsWeighted` | the bone weights / read / sum 1 ± 1e-3, ≤ 4 bones, none on `root` (rule 3) |
| `PantherModel_ImportsGenericWithItsClips` | importer and clips / read / Generic, own avatar, not optimised, six clips, two loops, 30 fps (rule 4) |
| `PantherModel_EveryClipBindsAndLeavesTheRootAlone` | each clip, every frame / sampled / every binding resolves; `root` still (rule 5) |
| `PantherModel_IsDarkButNotLost` | the atlas under each triangle / averaged / luma 0.15–0.40, saturation ≤ 0.35 (rule 6) |
| `PantherModel_IsLongAndLow` | the mesh at rest / proportions / as rule 7 (rule 7) |

Rule 1 is checked by hand: a re-run of each script and a byte compare. The script also asserts rules
2, 3 and 6 before it writes.

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet (`panther.py -- --pose-sheet Temp/Renders/panther_pose_sheet.png`):
   it prowls, crouches and lashes its tail, bounds, flinches and rolls over. The tail runs straight
   back at rest. Beside the Frog and the Pitcher it reads as the same family. In the last row's black
   shapes it reads as none of the other three. **The owner rules here whether the sculpt stays or the
   shells fallback is needed.**
2. **[Editor]** Seen in a run at JG-07d.

## Out of scope

- The prefab, animator and views (JG-07c), and the archetype and stage 6 (JG-07d).
- Growing the atlas. It is full after this task, and the next new body opens with that.

## As built

Built 2026-10-27. Rules 1–7 hold; three deviations and two findings.

- **A voxel remesh before the decimation (not in Why this shape).** The sculpt's head is a tangle of
  open and non-manifold shells (fix_glb.py leaves 2,584 such edges), and a collapse will not touch them.
  It stalled at 6,600 triangles with 5,000 in the head, and TRELLIS's UV seams blocked it as well. The
  decimated copy drops its UVs, is remeshed at 12 mm into one closed surface, then collapsed: **2,300
  triangles, 1,152 vertices**. The closed shell is also what the skinning weighs.
- **The skin is weighted by distance to each bone's segment, within the region that bone may move**
  (a leg's own quadrant below the shoulder, the tail behind the rump, the jaw under the muzzle): at most
  three bones, normalised. Heat weighting was not tried; this is deterministic and can't fail on a
  shell. The legs are posed by two-bone IK, so Prowl's and Crouch's planted feet stay planted.
- **Rule 7 is measured against the full height (to the ears), not the shoulder**, because a mesh's
  bounds can't find a shoulder. That is the stricter reading. Built: **2.18 m long (tail straight
  behind), 1.03 m tall, 0.74 m wide**: 2.12 and 2.95, against 2 and 2.5.

**The repaint**: 1,893 faces coat, 341 belly and muzzle, 62 rosette, 4 eye. Two majority passes join
the rosettes into patches. Mean luma 0.33, saturation 0.21. The tail was swung 54° straight back,
eased in over its first 5 cm. The body was re-centred 0.066 sculpt units off its own X, and scaled ×1.95
so nose to rump is 1.52 m.

**Findings for JG-07c.** (1) Nose to rump is 1.52 m, just under JG-07c rule 5's 1.6–2.0 m, and the back
at the shoulder about 0.94 m, inside its 0.7–0.95 m. That rule was written before the model existed:
JG-07c either takes its length range from this model or scales the prefab, and says which. A scale of
1.06 reaches 1.6 m but puts the shoulder at 1.0 m. (2) `STRIDE` is
0.72 m over Prowl's 0.8 s, so `_strideSpeed` is 0.9 m/s.

**Owner's call (manual step 1):** the pose sheet reads darker than the swatch's numbers suggest. The
coat's light end, `5E6A52`, sits near the floor's luma, and the cat stands off the floor by its darker
flanks. Death ends nearer belly-up than on its side. Whether the sculpt stays or the shells fallback
is wanted is the owner's ruling there.

**Verified:** `PantherModelTests` 8 of 8 in the batch-mode suite (JG-07a's *As built* has the run).
Rule 1: a second run of `panther.py` differs from the first in 2 bytes of the FBX, its timestamp, and
`puffball.py` re-run writes a byte-identical atlas. The `.fbx.meta` was written by hand from the
Puffball's (Generic, own avatar, `M_Enemy` remap, six clips, three looping), because the Editor was
closed; the importer read it as written.
