# RS-01b — The mesh on Rig_Medium

**Size:** S · **Depends on:** RS-01a, and the owner's handback it lists · **Branch:** `rs-01b-the-mesh-on-rig-medium`
**Design refs:** GD §16.4, §17.1; AR §18 (the M2-art rows: Generic, no root motion, no Animation Events) · **Ledger rows:** none

## Goal

The Gemini Ranger becomes one clean mesh of at most 8 900 triangles. It is skinned onto KayKit's
unchanged `Rig_Medium` and exported as an FBX that plays every `Rig_Medium` clip. The deformation is
checked on a pose sheet before Unity sees it, and the three props are rebuilt beside it.

## The budget, ruled at RS-00

**GD §17.1's 400–1,200 triangles is the crowd's budget.** It sits beside *"cheap enough for 28 enemies
on a mid-range phone"* and *"one shared material and atlas across all enemies"*. The player is one
body against a frame budget of 100k on the low tier. So the Ranger's budget is **the KayKit Ranger's
own 8 900**: a body no heavier than the one it stands beside. The design documents are this project's
drafts, so this is ruled rather than asked, and the owner overrides it by naming another number.

## Files

| Path (from the repo root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/fit_to_rig.py` | Blender 4.1 | **New.** Loads the generated body and `Rig_Medium`, stands the body in the rig's frame, and reports each joint's fit (rules 1–3) |
| `Tools/Blender/rebuild_and_skin.py` | Blender 4.1 | **New.** Remesh, decimate, unwrap, bake, weight, export. With `--prop`, a rigid prop instead (rules 4–8) |
| `Tools/Blender/pose_sheet.py` | Blender 4.1 | **New.** Plays chosen `Rig_Medium` clips on the exported body and renders one contact sheet (rule 9) |
| *small edits* | Docs | `Tools/Blender/README.md` — the three scripts' arguments and outputs |
| *outputs* | LFS | `ArtSource/Characters/Ranger/Ranger.blend` (the working file); under `Export/`: `Ranger.fbx`, `T_Ranger_Albedo.png`, `Bow.fbx`, `Quiver.fbx`, `Arrow.fbx`, `pose_sheet.png` and `fit_report.txt` |

Nothing under `Assets/` changes. [RS-01c](RS-01c-the-ranger-in-unity.md) brings `Export/` in.

## Public API

```text
blender -b --factory-startup --python Tools/Blender/fit_to_rig.py       -- <generated body> <rig.fbx> <out.blend> [--up Z] [--front -Y]
blender -b --factory-startup --python Tools/Blender/rebuild_and_skin.py -- <in.blend> <out-dir> [--tris 8900] [--voxel 0.015] [--texture 1024]
blender -b --factory-startup --python Tools/Blender/rebuild_and_skin.py -- --prop <generated prop> <out.fbx> --grip <x,y,z> [--tris N]
blender -b --factory-startup --python Tools/Blender/pose_sheet.py       -- <body.fbx> <Rig_Medium clips dir> <out.png>

<rig.fbx> is Assets/ThirdParty/KayKit/Adventurers/Characters/Ranger.fbx: its armature is the one the
clips were authored on, and its meshes are discarded on load.
```

## Behaviour

1. **The rig is KayKit's, and it does not change.** It keeps the 23 bones, the same names and the
   same rest pose, taken from `Ranger.fbx` itself. Unity binds a Generic clip by transform path, so
   equal names are what let all 139 clips play without retargeting. M2-art chose Generic, not
   Humanoid.
2. **The body is stood in the rig's frame, and nothing more is inferred.**
   - **Where it stands.** Feet on 0, facing the rig's front, and the T-pose arms along the rig's arms.
   - **Its size.** Scaled uniformly until its height is the KayKit Ranger's 2.27, within 3 %.
   - **Axes.** `--up` and `--front` name the tool's convention.
   - **What it refuses.** A body whose height is not along its longest axis, which is a body lying
     down, is refused and named.
3. **The fit report says whether the body can wear the rig.** For each of the 20 deforming joints it
   answers two questions:
   - **Is the bone head inside the body?** Tested by ray parity. **A joint outside the body fails the
     task.** The remedy is a new Gemini roll or a hand edit in Blender, never a moved bone (rule 1).
   - **How far is it from the middle of the limb there?** Measured as a fraction of the limb's width
     at that height. More than 25 % off-centre is a warning, and the pose sheet judges it (rule 9).
4. **The mesh is rebuilt so its weights can be computed.**
   - **Why rebuild.** Image-to-3D meshes are rarely manifold, and Blender's bone-heat weighting fails
     on a mesh that is not.
   - **Remesh.** A voxel remesh, starting at `--voxel 0.015` on a 2.27-unit body. The size kept is
     stated in *As built*.
   - **Decimate** to at most `--tris`.
   - **Shade and unwrap.** Smooth shading, then a fresh smart-project unwrap.
5. **The colours are baked, not repainted.**
   - **The bake.** The base colour is baked from the generated mesh onto the rebuilt one (Cycles,
     selected to active, a small cage extrusion), `--texture` pixels square, as
     `T_Ranger_Albedo.png`.
   - **One material.** KayKit's flat colours come through as the generated texture carries them.
   - **Palette check.** No pixel may be within ΔE 10 of `#FF4A1F`, `#A855F7` or `#FBBF24`
     (GD §16.4). It is checked on the baked texture and reported with pixel counts.
6. **The head is rigid, and the rest is heat-weighted.**
   - **The head.** Every vertex above the neck line (the `head` bone's rest height) is weighted 1.0
     to `head`. That is KayKit's own choice: `Ranger_Head` has one vertex group. A face that bends
     with the neck looks broken at phone scale.
   - **The rest.** Automatic weights from the 20 deforming bones only. `root`, `handslot.l` and
     `handslot.r` carry nothing.
   - **Limits.** At most 4 influences per vertex, normalised, and no vertex unweighted.
     [RS-01c](RS-01c-the-ranger-in-unity.md) reads the project's skin-weight quality and says so if
     it is below 4.
7. **The export matches KayKit's, so Unity imports it the same way.**
   - **Contents.** FBX, with one armature named `Rig_Medium` carrying the 23 bones, the mesh named
     `Ranger_Body`, no animation, and transforms applied.
   - **Add leaf bones is off.** Blender's default adds `_end` bones that no clip names.
   - **Axes.** The axis settings are the ones that give the KayKit Ranger's orientation and size.
     This is checked by importing both FBXs into one scene and comparing bounds.
8. **The props are rigid, and each is pivoted where a hand holds it.** Each prop is rebuilt as the
   body is (rules 4, 5), with no rig.
   - **Budgets.** No more triangles than KayKit's own prop of the same kind: bow 684
     (`bow_withString`), quiver 255, arrow 52, as measured at RS-00.
   - **Origin.** At `--grip`: the bow's handle, the quiver's strap, the arrow's nock.
   - **Axes.** Matched to KayKit's `bow_withString.fbx`, so a prop parents to a handslot with no
     offset.
9. **The pose sheet is the deformation check.** The exported body, reloaded from its FBX, plays seven
   clips from the `Rig_Medium` FBXs: `Idle_A`, `Running_A`, `Ranged_Bow_Draw`, `Ranged_Bow_Release`,
   `Dodge_Forward`, `Hit_A` and `Death_A`. Each shows three frames, front and three-quarter, on one
   sheet. The owner reads the elbows, knees, shoulders, hips and neck on it. It is the last look
   before Unity.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

Each script checks its own output and exits non-zero on a failure. Each row is a run the builder
makes and records.

| Run | Given / When / Then |
|---|---|
| `Fit_TheKayKitRangerFitsItsOwnRig` | `Ranger.fbx`'s own meshes, joined, as the "generated" body / `fit_to_rig` / all 20 joints inside, no warning — the script's control, rule 3 |
| `Fit_AJointOutsideFails` | that body with one arm scaled to 50 % along its length / `fit_to_rig` / non-zero, naming the joints left outside — rule 3 |
| `Fit_ABodyLyingDownIsRefused` | the control rotated 90° / `fit_to_rig` / non-zero, naming the axes — rule 2 |
| `Body_KeepsItsBudgetAndItsRig` | the Ranger export / reloaded / ≤ 8 900 triangles; 23 bones named as `Rig_Medium`'s, rest heads equal within 1 mm; no `_end` bone — rules 1, 4, 7 |
| `Body_EveryVertexIsWeighted` | the export / reloaded / every vertex's weights sum to 1 ± 1e-4 over at most 4 groups; none on `root` or a handslot; the head's vertices all 1.0 on `head` — rule 6 |
| `Body_StandsAsTheKayKitRangerStands` | both FBXs in one scene / — / feet on 0, facing one way, heights within 3 % — rules 2, 7 |
| `Body_CarriesNoForbiddenColour` | `T_Ranger_Albedo.png` / the palette check / zero pixels within ΔE 10 of the three reserved colours — rule 5 |
| `Props_KeepTheirBudgetsAndGrips` | the three prop exports / reloaded / triangles within rule 8's budgets; origin at the grip — rule 8 |
| `PoseSheet_PlaysEveryClipItNames` | the export / `pose_sheet` / seven clips found in the `Rig_Medium` FBXs, 42 frames rendered — rule 9 |

## Manual verification (Editor / device)

1. **[owner]** Read `fit_report.txt`. *Expected: no joint outside, and any warnings named.*
2. **[owner]** Read `pose_sheet.png`. *Expected: elbows and knees bend without collapsing, shoulders
   rise without tearing the chest, the head turns as one piece, and nothing pokes through at the
   hips in `Death_A`.* A failing sheet is a re-roll or a hand fix, stated in *As built*.

## Out of scope

- **Unity.** [RS-01c](RS-01c-the-ranger-in-unity.md).
- **Levels of detail.** One body. A LOD is M8-03's device tiering, if a phone asks.
- **Cloth, hair or face rigging.** `Rig_Medium` has none of them, and the prompts ask for nothing that
  needs them.
- **Moving a bone to fit the mesh.** Rule 1: the clips would snap it back.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
