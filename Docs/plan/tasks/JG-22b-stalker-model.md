# JG-22b — The Chameleon stalker's model

**Size:** M · **Depends on:** the owner's sculpt · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion); memory "art source ruling"

**Needs before the go:** the owner's TRELLIS sculpt from the prompt below, cleaned by
`fix_glb.py`, at `Tools/Blender/Sources/Stalker_src.glb`.

> Image prompt: *a lean chameleon-lizard hunter standing upright on two legs, low crouched stance,
> long curled tail, huge turret eyes, small frill crest, bare clawed hands, leaf-green skin with
> darker olive bands, no clothes, no weapon, stylised low-poly game character, neutral T-pose,
> plain grey background.*

## Goal

`Stalker.fbx` on KayKit's unchanged `Rig_Medium` with its six clips, painted from the enemy atlas,
made by one script as `shaman.py` made the Shaman.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/stalker.py` | Blender 4.1 | **New.** Fits, repaints, weights, exports; `--pose-sheet <png>` |
| `Tools/Blender/Sources/Stalker_src.glb` | LFS | **New.** The owner's sculpt, the only input |
| `Assets/_Project/Art/Enemies/Stalker.fbx` | generated | Model; `.meta` holds the import |
| `Assets/_Project/Tests/Game/Art/StalkerModelTests.cs` | Tests.Game | **New.** Rules 2–6 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/stalker.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script, and a re-run makes the same file** (FBX header aside). It reads the atlas, never
   writes it; the source's hash is pinned.
2. **In the crowd's budget**: one skinned mesh, 400–2,500 triangles, smooth-shaded, one slot
   remapped to `M_Enemy`.
3. **Every vertex weighted** to ≤ 4 `Rig_Medium` bones summing to 1, none to `root`; the rig's 23
   bones unchanged; the tail rides `spine`/`hips` weights.
4. **Six clips** from `Rig_Medium`, Generic, 30 fps, in place: `Idle_B` (loop), `Sneaking` (loop,
   the prowl), `Crouching` (loop, the lurk), `Melee_Unarmed_Attack_Punch_A` (the strike), `Hit_A`,
   `Death_A`.
5. **No reserved colour, no emissive**: nothing cyan, red-orange, violet or gold;
   `RootlingModelTests.EnemyAtlas_CarriesNoReservedColour` unchanged.
6. **Lower than the Mossguard**: crouched idle height 75–90 % of the Mossguard's.

## Tests

| Test | Given / When / Then |
|---|---|
| `StalkerModel_StaysInTheCrowdsBudget` | 400–2,500 tris, `M_Enemy` (2) |
| `StalkerModel_EveryVertexIsWeighted` | sum 1 ± 1e-3, ≤ 4, none on `root` (3) |
| `StalkerModel_ImportsGenericWithItsClips` | Generic, 6 clips, 3 loops, 30 fps, root still (4) |
| `StalkerModel_HasNoGlowAndNoReservedColour` | no emissive swatch (5) |
| `StalkerModel_IsLowerThanTheMossguard` | as rule 6 (6) |

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet: it sneaks, crouches, lunges, flinches, dies. In black beside the
   Mossguard and Shaman it is neither. **The owner rules whether the sculpt stays.**

## Out of scope

- Prefab, animator, cloak (JG-22c).

## As built

_Filled at merge._
