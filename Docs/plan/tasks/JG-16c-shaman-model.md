# JG-16c — The Moss Shaman's model

**Size:** M · **Depends on:** the owner's two sculpts · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion)

**Needs before the go:** the owner's TRELLIS sculpts, cleaned by `fix_glb.py`, at
`Tools/Blender/Sources/Shaman_src.glb` (body) and `ShamanStaff_src.glb` (staff).

## Goal

`Shaman.fbx` on KayKit's unchanged `Rig_Medium` with its six clips, and `ShamanStaff.fbx` on
`handslot.r`, both painted from the enemy atlas, made by one script as JG-14b made the Mossguard.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/shaman.py` | Blender 4.1 | **New.** Fits, repaints, weights, exports; `--pose-sheet <png>` |
| `Tools/Blender/Sources/Shaman_src.glb`, `ShamanStaff_src.glb` | LFS | **New.** The owner's sculpts, the only inputs |
| `Assets/_Project/Art/Enemies/Shaman.fbx`, `ShamanStaff.fbx` | generated | Models; `.meta` holds the import |
| `Assets/_Project/Tests/Game/Art/ShamanModelTests.cs` | Tests.Game | **New.** Rules 2–7 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/shaman.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script, and a re-run makes the same files** (FBX headers aside). It reads the atlas, never
   writes it. The sources' hashes are pinned in the script, as `mossguard.py` pins its body.
2. **Body in the crowd's budget**: one skinned mesh, 400–2,500 triangles, smooth-shaded, one slot
   remapped to `M_Enemy`. **Staff**: one static mesh, ≤ 400 triangles, `M_Enemy`.
3. **Every body vertex weighted** to ≤ 4 `Rig_Medium` bones summing to 1, none to `root`; the rig's
   23 bones unchanged so KayKit clips bind.
4. **Six clips** from `Rig_Medium`, Generic, 30 fps, in place: `Idle_B` (loop), `Walking_B` (loop),
   `Running_A` (loop, the flee), `Ranged_Magic_Spellcasting` (the cast), `Hit_A`, `Death_A`.
5. **The staff sits in the right hand**: its grip within 0.05 m of `handslot.r` in `Idle_B`, its
   seed above the head in the cast's peak frame.
6. **No reserved colour**; the seed is the only emissive part, a pale lime from the atlas, never
   cyan, red-orange, violet or gold. `RootlingModelTests.EnemyAtlas_CarriesNoReservedColour` unchanged.
7. **Slimmer than the Mossguard**: at rest, its widest shoulder slice is narrower than the
   Mossguard's, and its height with the crest is within ±10 % of the Mossguard's.

## Tests

| Test | Given / When / Then |
|---|---|
| `ShamanModel_StaysInTheCrowdsBudget` | body 400–2,500 tris, staff ≤ 400, `M_Enemy` (2) |
| `ShamanModel_EveryVertexIsWeighted` | sum 1 ± 1e-3, ≤ 4, none on `root` (3) |
| `ShamanModel_ImportsGenericWithItsClips` | Generic, 6 clips, 3 loops, 30 fps, root still (4) |
| `ShamanModel_StaffSitsInTheHand` | grip ≤ 0.05 m; seed over head at cast peak (5) |
| `ShamanModel_SeedIsTheOnlyGlow` | one emissive swatch, none reserved (6) |
| `ShamanModel_IsSlimmerThanTheMossguard` | as rule 7 (7) |

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet: it walks, runs, raises the staff, flinches, dies. In black beside the
   Mossguard, Frog, Pitcher and Panther it is none of them. **The owner rules whether the sculpt stays.**

## Out of scope

- Prefab, animator, ring (JG-16d). Growing the atlas: a task of its own, opened before this one.

## As built

_Filled at merge._
