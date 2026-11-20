# SS-18d — Sundial Colossus TRELLIS body

**Size:** M · **Depends on:** SS-18c and the owner's TRELLIS GLB · **Branch:** `ss-18d-colossus-model`

**Design ref:** [GD §9.6](../../GameDesign.md#96-the-sunscar--stage-20-the-sundial-colossus).

## Goal

Replace the prototype with a rigged sandstone giant and preserve the live fight contract.

## Files

| Path from repository root | Purpose |
|---|---|
| `Tools/Blender/Sources/SundialColossus_src.glb` | Owner's source mesh |
| `Tools/Blender/sundial_colossus.py` | Deterministic mesh, rig and clip export |
| `Assets/_Project/Art/Enemies/SundialColossus.fbx` | Rigged body with bundled clips |
| `Assets/_Project/Animation/Controllers/AC_SundialColossus.controller` | Bind final clips to the existing pose states |
| `Assets/_Project/Prefabs/Enemies/SundialColossus.prefab` | Replace prototype body and bind final rig |
| `Assets/_Project/Tests/Game/Art/ColossusModelTests.cs` | Import, clip and prefab parity checks |
| `Assets/_Project/Tests/Game/Views/ColossusViewTests.cs` | Update the existing prototype shape assertion |

Unity `.meta` companions follow their assets. Reuse an existing palette material. If the GLB
requires dedicated material or texture assets, revise this file table before building.

## API

Keep the existing prefab reference and the SS-18b animator state names and event contract.

## Behaviour

1. Align the source to the KayKit `Rig_Medium` hierarchy, feet on the prefab ground plane and front along +Z. Preserve the right forearm wedge, recessed dial and pale sandstone palette. Weight the large arm to keep the wedge rigid through the sweep while allowing the shoulder and elbow to move. Export reproducibly from the supplied GLB with Blender and `PYTHONHASHSEED=0`.
2. Provide grounded idle and walk plus poses for the first sweep, return, raised-foot stomp and arm pinned into the floor. Bind those clips to the SS-18b states. Match sweep contacts to all three impact slots in both directions, and stomp contact to the impact event. Root motion and animation events stay off.
3. Preserve prefab GUID, gameplay root, scale, collider, floor warnings and cue bindings. The rigged body stays within the prototype's combat footprint and pooled reuse restores its neutral pose.

Tests > Behaviour > API > Files. Built code wins. Record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Body_UsesGroundedRigAndRightWedge` | Imported FBX / inspect / medium rig and skin, grounded pose, oversized right forearm |
| `Clips_MatchEventDrivenAttackPoses` | Controller and view / sample tell, slots, stomp and recovery / compatible bindings, pinned arm, no root motion or clip events |
| `ModelSwap_PreservesFightBindings` | Final prefab and view fixture / load, bind and reuse / rig shows through old prefab reference, same collision and scoped warning lifecycle |

## Manual verification

1. **[Editor]** Drive the final model's two sweeps and stomp in the live Stage 20 fight. Check hand contact, foot contact, clipping and the pinned recovery.
2. **[device]** Verify the wedge and chest dial read at the normal portrait camera scale.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
