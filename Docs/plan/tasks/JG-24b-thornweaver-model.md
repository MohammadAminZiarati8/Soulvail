# JG-24b — The Thornweaver's model

**Size:** M · **Depends on:** the owner's sculpt · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion); memory "art source ruling"

**Needs before the go:** the owner's TRELLIS sculpt from the prompt below, cleaned by
`fix_glb.py`, at `Tools/Blender/Sources/Thornweaver_src.glb`.

> Image prompt: *a hunched bramble crone made of twisted bark and vines, standing on two legs, long
> thin arms ending in thorny twig fingers, a crown of thorns, moss hanging from the shoulders, no
> face but two hollow knots for eyes, dark brown bark and moss green, stylised low-poly game
> character, neutral T-pose, plain grey background.*

## Goal

`Thornweaver.fbx` on KayKit's unchanged `Rig_Medium` with its five clips, painted from the enemy
atlas, made by one script as `shaman.py` made the Shaman. Not a lizard: the Jungle has three.

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/thornweaver.py` | Blender 4.1 | **New.** Fits, repaints, weights, exports; `--pose-sheet <png>` |
| `Tools/Blender/Sources/Thornweaver_src.glb` | LFS | **New.** The owner's sculpt, the only input |
| `Assets/_Project/Art/Enemies/Thornweaver.fbx` | generated | Model; `.meta` holds the import |
| `Assets/_Project/Tests/Game/Art/ThornweaverModelTests.cs` | Tests.Game | **New.** Rules 2–6 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/thornweaver.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script, and a re-run makes the same file** (FBX header aside). It reads the atlas, never
   writes it; the source's hash is pinned.
2. **In the crowd's budget**: one skinned mesh, 400–2,500 triangles, smooth-shaded, one slot
   remapped to `M_Enemy`.
3. **Every vertex weighted** to ≤ 4 `Rig_Medium` bones summing to 1, none to `root`; the rig's 23
   bones unchanged.
4. **Five clips** from `Rig_Medium`, Generic, 30 fps, in place: `Idle_B` (loop), `Walking_B`
   (loop), `Ranged_Magic_Spellcasting` (the cast), `Hit_A`, `Death_A`.
5. **No reserved colour**: nothing cyan, red-orange, violet or gold; at most one emissive swatch, a
   pale moss green on the crown; `RootlingModelTests.EnemyAtlas_CarriesNoReservedColour` unchanged.
6. **Taller than the Shaman**, stooped: height within 100–115 % of the Mossguard's.

## Tests

| Test | Given / When / Then |
|---|---|
| `ThornweaverModel_StaysInTheCrowdsBudget` | 400–2,500 tris, `M_Enemy` (2) |
| `ThornweaverModel_EveryVertexIsWeighted` | sum 1 ± 1e-3, ≤ 4, none on `root` (3) |
| `ThornweaverModel_ImportsGenericWithItsClips` | Generic, 5 clips, 2 loops, 30 fps, root still (4) |
| `ThornweaverModel_CrownIsTheOnlyGlow` | ≤ 1 emissive swatch, none reserved (5) |
| `ThornweaverModel_StandsAtMossguardHeight` | as rule 6 (6) |

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet: it walks, casts, flinches, dies. In black beside the Mossguard,
   Shaman and Stalker it is none of them. **The owner rules whether the sculpt stays.**

## Out of scope

- Prefab, animator, cast glow (JG-24c).

## As built

_Filled at merge._
