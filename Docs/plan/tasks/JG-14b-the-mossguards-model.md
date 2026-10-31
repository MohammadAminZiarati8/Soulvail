# JG-14b — The Mossguard's model

**Size:** M · **Depends on:** the owner's two sculpts · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion)

**Source preparation, approved 2026-10-31:** the owner approved the repaired lizard and requested
that its procedure be saved and the detailed model imported into Unity while they made the shield.
`Tools/Blender/Sources/Mossguard_src.glb` is that approved master; it remains byte-identical.
The owner then selected shield seed 42, with its backing and grip explicitly retained.
Use [the source workflow](../../../Tools/Blender/README.md) and `repair_scan.py` for fragmented scans.
`fix_glb.py` remains available for legacy source workflows.

Preparation also ships `mossguard_source.py`, `Art/Enemies/MossguardSource.fbx` and its dedicated
`Textures/T_MossguardSource_Albedo.png`: a static detailed source preview, outside the runtime Files
table below, explicitly requested by the owner. Do not use it as the final body or spawn it in runs.
The preview remains separate from the budgeted, rigged runtime body below.

**Selected shield and conversion reading (2026-10-31):** raw seed 42 remains in the generator's
`outputs/generated/MossguardShield-24b8be8b-seed42_00001_.glb` (SHA-256
`0018de945a997ae3fbd4f934f33425817004c075d1f2dd26f444095c21cea29c`). The reference's wooden
backing and rear grips are intentional. `mossguard.py --prepare-shield <raw.glb> --review-dir <dir>`
fits the rear assembly against the front, turns its grips toward the wielder, reuses the outside wood
texture on the exposed inside, removes the reddish hanging nub, then invokes `repair_scan.py`.
Reimport strips only detached reconstruction crumbs; the front, backing and grips form one closed
connected source. The final source SHA-256 is
`d58e6350a648a0512fe3b40549263dea655fbe5bf655ff2fa8911f2e15d23ed2`.

The runtime reduction reserves 152 shield triangles for the existing diamond/branch emblem and four
crossed vine bindings; automatic reduction erased these shallow details. The rest retains the frame,
backing and open grips. These reliefs sample the existing stone/leaf swatches. The +X source grip
becomes the pivot, bringing the shield over the chest at identity on `handslot.l`; the static FBX
bakes axis conversion so its Unity root has identity rotation. Body eye meshes survive reduction;
regions restrict distance weighting, with the tail riding `hips`. No rig or atlas edits.

Review artifacts (outside the repository):
`C:/Users/mazia/.codex/visualizations/2026/10/31/01a18f51-1d94-71a3-85fd-61c35225b722/Mossguard/`.
`pose-sheet.png` rows follow rule 7's clip order; columns are three phases from the game/front and
three-quarter views. `unity-<clip>-0/1/2.png` shows the imported material from front, three-quarter
and game angles. `unity-shield-0/1/2.png` and `shield-reimport-front/side/back.png` verify exports.

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

2026-10-31 — Built. Approved body master and detailed static preview preserved unchanged. Seed 42's
backing fitted against the face, both rear grips/openings kept, reddish nub removed; `repair_scan.py`
closure and texture transfer reused. Final source: 179,834 triangles, one connected shell, zero
boundary/non-manifold edges, consistent outward normals; serialized mesh, opacity and reimported
front/side/back verified. `mossguard.py` ships a 2,056-triangle body on untouched Rig_Medium and a
384-triangle shield (2,440 combined), one M_Enemy slot each, existing atlas only. Side-grip pivot,
identity attachment, +Z face, 1.05 x 0.63 m; explicit crest/bindings survive the reduction. Every
vertex has normalized regional weights to at most two bones; tail rides hips. Seven KayKit clips
bind and sample with root still; blocking stays upright ahead of the chest. Exported pose sheet and
Unity views checked, no arm/tail tearing. Rebuild differs only in FBX timestamps. Eight model tests
pass. Full EditMode: 4,757 total / 4,756 passed / 0 failed / 1 known inconclusive; fresh UTC start
2026-10-31 00:43:04. Format clean; no new compiler/analyzer diagnostics. Owner visual judgement of
the runtime pose sheet remains owed. JG-14c–e untouched. Push held by explicit owner instruction.
