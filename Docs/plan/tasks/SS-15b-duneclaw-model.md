# SS-15b — Rig the Duneclaw sculpt and bake its clips

**Size:** M · **Depends on:** owner's TRELLIS sculpt · **Branch:** `dev` (direct)
**Design refs:** [GD §9.3](../../GameDesign.md#93-the-sunscar--stage-5-the-duneclaw), `Tools/Blender/scorpion.py`

## Goal

The approved sculpt fits the scorpion rig and has in-place boss clips ready for the body.

## Files

| Path | Purpose |
|---|---|
| `Tools/Blender/Sources/Duneclaw_src.glb` | Owner-supplied source, SHA-256 pinned |
| `Tools/Blender/duneclaw.py` | Repair, turnaround, rig, bake and export |
| `Assets/_Project/Art/Enemies/Duneclaw.fbx` | Skinned mesh and clips |
| `Assets/_Project/Tests/Game/Art/DuneclawModelTests.cs` | Import, rig and clip checks |

## Behaviour

1. The owner supplies the TRELLIS GLB. Verify front, sides, rear and top against the approved
   image before binding. The silhouette has six legs, a much larger right claw and a
   distinct stinger with clear space between the tail and body.
2. Refit `Rig_Scorpion` by joint position, keeping its bone names and hierarchy. Keep the
   right claw on the creature's right. Stop at the turnaround if the joints cannot be fitted.
   One skinned mesh, one material slot using the existing enemy atlas at prefab authoring,
   1,500–3,000 triangles and at most four weights per vertex. Use existing neutral swatches
   for pale chipped shell, dusty ochre and dark horn.
3. Bake `Idle`, `Skitter`, `SweepWindup`, `Sweep`, `SlamWindup`, `Slam`, `Pinned`, `Phase`,
   `Hit` and `Death` at 30 fps. Keep root in place. Wind-up clips end ready to strike; `Pinned`
   is a planted pull loop. Use Generic import, with root motion disabled on the eventual prefab.
   The sweep should carry the big claw across the arc and the slam should have a clearly raised tail.

## Tests

| Test | Given / When / Then |
|---|---|
| `DuneclawModel_ImportsSingleSkinWithinBudget` | Imported FBX / inspect mesh / budgets (2) |
| `DuneclawRig_MatchesScorpionHierarchy` | Existing rig and boss import / compare names and parents / match (2) |
| `DuneclawClips_CompleteAndInPlace` | Imported animation / inspect clips / required set, no root travel (3) |

## Manual verification (Editor)

1. Inspect the turnaround for silhouette and joint clearance before binding (1).
2. Inspect imported sweep, slam and pin clips at game zoom (3).

## Out of scope

Prefab wiring, attack VFX, audio and arena work.

## As built

_Written at merge in the commit body._
