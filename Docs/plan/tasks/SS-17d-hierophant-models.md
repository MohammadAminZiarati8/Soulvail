# SS-17d — Hierophant and floor seal models

**Size:** M · **Depends on:** SS-17c and both owner-supplied TRELLIS GLBs · **Branch:** `ss-17d-hierophant-models`

**Design ref:** [GD §9.5](../../GameDesign.md#95-the-sunscar--stage-15-the-sunscar-hierophant).

## Goal

Replace both prototype meshes with the approved body and floor seal.

## Files

| Path from repository root | Purpose |
|---|---|
| `Tools/Blender/Sources/Hierophant_src.glb` | Owner's body source |
| `Tools/Blender/Sources/HierophantSeal_src.glb` | Owner's seal source |
| `Tools/Blender/hierophant.py` | Deterministic preparation and FBX export |
| `Assets/_Project/Art/Enemies/Hierophant.fbx` | Rigged body |
| `Assets/_Project/Art/Environment/HierophantSeal.fbx` | Reusable tile |
| `Assets/_Project/Prefabs/Enemies/Hierophant.prefab` | Bind final body |
| `Assets/_Project/Prefabs/Arenas/Sunscar/HierophantSeal.prefab` | Bind final tile and retain separate light renderer |
| `Assets/_Project/Tests/Game/Art/HierophantModelTests.cs` | Import, rig and final prefab assertions |

Unity `.meta` companions follow their assets. Reuse project atlas/materials. Recheck the
Files table against the supplied GLBs before building if they need additional material files.

## API

The existing boss and seal prefab references are the asset boundary. SS-17a and SS-17b event and view contracts remain in force.

## Behaviour

1. Import the body on the Jackal Priest's KayKit `Rig_Medium` hierarchy. Match the prototype grip and ground alignment, keep the staff separate and preserve compatible animation bindings. Idle, walk, sweep, raise and recovery work with root motion off.
2. Centre the seal pivot in XZ, align the top with the arena floor and keep it nonblocking. Bind the same mesh into all three placements and drive the separate light renderer with the existing view.
3. Preserve the prototype's gameplay anchors, collider, attack dimensions, timings and cue bindings. Use the supplied stepped headdress and wide shoulders to distinguish the boss silhouette from the priest add. Keep the warm ivory, sandstone and umber palette and paint the carved seal without emission for its idle state.

Tests > Behaviour > API > Files. Built code wins; record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Body_BindsRigAndSeparateStaff` | Imported FBX and prefab / load and sample controller poses / compatible rig, existing staff grip, grounded pose and root motion off |
| `Seal_BindsFinalMeshAndIndependentLight` | Imported seal and prefab / load / flush pivot, mesh bound, separate light and no movement/shot blocker |
| `ModelSwap_PreservesFightBindings` | Final prefabs / inspect anchors and run existing view/stage tests / original footprint, collision and cue contract |

## Manual verification

1. **[Editor]** Inspect walk, staff sweep, staff raise and recovery for clipping. Check the seal looks flush and track the independent light through one cast.
2. **[device]** Check the boss reads distinctly beside the regular priest and the seal can be identified while the hero is moving.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
