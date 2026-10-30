# JG-14b — The Mossguard's model

**Size:** M · **Depends on:** the owner's two sculpts · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion)

**Needs before the go:** the owner's TRELLIS sculpts, cleaned by `fix_glb.py`: the lizard warrior in a
T-pose, and its bark-and-stone tower shield (owner's pick, 2026-10-31).

## Goal

`Mossguard.fbx`, the lizard skinned onto KayKit's `Rig_Medium` the Rootling's way (`rootling.py`),
and `MossguardShield.fbx`, a static prop for `handslot.l`, both painted from the atlas by one script.
It has no clips of its own: `Rig_Medium`'s library plays on it.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/mossguard.py` | Blender 4.1 | **New.** Fits, cuts, repaints, skins; `--pose-sheet <png>` |
| `Tools/Blender/Sources/Mossguard_src.glb` | LFS | **New.** The body sculpt |
| `Tools/Blender/Sources/MossguardShield_src.glb` | LFS | **New.** The shield sculpt |
| `Assets/_Project/Art/Enemies/Mossguard.fbx` | generated | Skinned body; `.meta` holds the import |
| `Assets/_Project/Art/Enemies/MossguardShield.fbx` | generated | Static shield |
| `Assets/_Project/Tests/Game/Art/MossguardModelTests.cs` | Tests.Game | **New.** Rules 2–7 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/mossguard.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script; a re-run differs only in the FBX timestamps.** It reads the full atlas (JG-07b)
   and never writes it.
2. **The rig is KayKit's, untouched**: `Skeleton_Minion.fbx`'s armature as loaded, as `rootling.py`.
   The mesh is fitted to it: scaled to the rig's height, then the arms beyond the shoulders scaled
   along X so each wrist meets its `hand` bone. The tail rides `hips`.
3. **In the crowd's budget.** Body 1,200–2,200 triangles, shield 150–400, together ≤ 2,500; smooth
   shading, one slot each remapped to `M_Enemy`.
4. **Every body vertex weighted** to ≤ 4 bones summing to 1, none to `root` or a `handslot`
   (distance to each bone's segment, inside its region, as JG-07b's *As built*).
5. **The shield fits the hand.** Pivot at its grip, face toward +Z when parented to `handslot.l`
   at identity, 0.9–1.2 m tall, 0.5–0.7 m wide.
6. **Painted from existing swatches**: scales `panther_fur`, belly and throat `panther_belly`, bark
   `pitcher_root`, leaves `pitcher_leaf`, stone `croak_moss`, eyes `frog_green`, claws `socket`, each
   face by the sculpt's texture under it. Mean luma 0.15–0.45; no reserved colour
   (`RootlingModelTests.EnemyAtlas_CarriesNoReservedColour`, unchanged).
7. **The clips JG-14c uses play on it**: `Idle_B`, `Walking_B`, `Melee_Blocking`, `Melee_Block_Attack`,
   `Melee_Block_Hit`, `Hit_A`, `Death_A` bind every curve, and `root` stays still.

## Tests

| Test | Given / When / Then |
|---|---|
| `MossguardModel_IsShipped` | a `SkinnedMeshRenderer`; shield a `MeshRenderer` |
| `MossguardModel_SitsOnRigMedium` | bone names and rest poses equal `Skeleton_Minion`'s (2) |
| `MossguardModel_WristsMeetTheHands` | nearest vertex to each `hand` bone ≤ 0.08 m (2) |
| `MossguardModel_StaysInTheCrowdsBudget` | as rule 3 (3) |
| `MossguardModel_EveryVertexIsWeighted` | as rule 4 (4) |
| `MossguardShield_FitsTheHand` | pivot, facing, size as rule 5 (5) |
| `MossguardModel_WearsTheAtlas` | luma band; `M_Enemy` (6) |
| `MossguardModel_PlaysTheKayKitClips` | each clip of rule 7 / binds, `root` still (7) |

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet: walks, holds the shield up, bashes, takes a hit on the shield, dies.
   The shield stays in front of the chest while blocking, with no arm or tail tearing. From the game
   camera the shield reads as a dark bar on its front. **The owner rules whether the sculpts stay.**

## Out of scope

- JG-14c's prefab and animator. Growing the atlas.

## As built

_Filled at merge._
