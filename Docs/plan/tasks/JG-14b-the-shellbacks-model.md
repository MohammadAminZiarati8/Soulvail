# JG-14b — The Shellback's model

**Size:** M · **Depends on:** the owner's sculpt (below) · **Branch:** `dev` (direct)
**Design refs:** GD §16.4, GD §17.1; AR §18.2 (Generic, no root motion)

**Needs before the go:** the owner's name and look (beetle or tortoise), and the owner's sculpt,
cleaned by `fix_glb.py`, at `Tools/Blender/Sources/Shellback_src.glb`. Every name below takes the
owner's. Written for a **beetle**: a broad head-plate as the shield, a soft back. A tortoise has four
legs and a shell rim; the rules stand.

## Goal

`Shellback.fbx`: a skinned, smooth-shaded mesh of 400–2,500 triangles on `Rig_Shellback`, five
clips, painted from the atlas, made by one script (as JG-07b, JG-12d).

## Files

| Path (from the repository root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/shellback.py` | Blender 4.1 | **New.** Cuts, repaints, rigs and animates; `--pose-sheet <png>` |
| `Tools/Blender/Sources/Shellback_src.glb` | LFS | **New.** The owner's sculpt, the only input |
| `Assets/_Project/Art/Enemies/Shellback.fbx` | generated | The model and clips; `.meta` holds the import |
| `Assets/_Project/Tests/Game/Art/ShellbackModelTests.cs` | Tests.Game | **New.** Rules 2–7 |

## Public API

```text
PYTHONHASHSEED=0 "C:/Program Files/Blender Foundation/Blender 4.1/blender.exe" --background \
    --factory-startup --python Tools/Blender/shellback.py [-- --pose-sheet <out.png>]
```

## Behaviour

1. **One script, and a re-run makes the same model**: the FBX differs only in its header's
   timestamp. It reads the full atlas (JG-07b), never writes it.
2. **In the crowd's budget.** One skinned mesh of 400–2,500 triangles (remeshed then collapsed, as
   JG-07b's *As built*), smooth-shaded, one material slot remapped to `M_Enemy`.
3. **Every vertex weighted** to at most four bones summing to 1, none to `root`. Bones: `root`,
   `body`, `plate`, `head`, `mandible.l/.r`, and per leg an upper and a lower (`front`, `mid`,
   `back` × `.l/.r`).
4. **It imports Generic with five clips** at 30 fps, in place: `Idle` (48 f, loop), `Walk` (24 f,
   loop, one stride), `Bash` (21 f: plate dips over 0.7 s, drives forward on the last 3), `Hit`
   (12 f, the back) and `Death` (30 f, tips over). An avatar from this model, not optimised.
5. **Every clip binds, and none moves the root.**
6. **The plate is dark, the back is pale, and no reserved colour.** Repainted from existing swatches:
   plate and legs `panther_fur`, back `panther_belly`, eyes `frog_green`, mandibles `socket`. The
   back's mean luma exceeds the plate's by at least 0.10, so the open side reads as the open side.
   `RootlingModelTests.EnemyAtlas_CarriesNoReservedColour` unchanged.
7. **Its front is its widest part.** At rest, the widest slice of the front third is at least as wide
   as any slice of the rear third, length over height is at least 1.6, and length is 1.3–1.8 m.

## Tests

| Test | Given / When / Then |
|---|---|
| `ShellbackModel_IsShipped` | one `SkinnedMeshRenderer` |
| `ShellbackModel_StaysInTheCrowdsBudget` | 400–2,500 tris, one submesh, `M_Enemy` (2) |
| `ShellbackModel_EveryVertexIsWeighted` | sum 1 ± 1e-3, ≤ 4 bones, none on `root` (3) |
| `ShellbackModel_ImportsGenericWithItsClips` | Generic, own avatar, 5 clips, 2 loops, 30 fps (4) |
| `ShellbackModel_EveryClipBindsAndLeavesTheRootAlone` | every frame / binds, `root` still (5) |
| `ShellbackModel_PlateDarkBackPale` | back luma − plate ≥ 0.10 (6) |
| `ShellbackModel_IsWidestAtTheFront` | as rule 7 (7) |

## Manual verification (Editor / device)

1. **[Editor]** The pose sheet: it walks, dips and drives its plate, flinches at the back, tips over.
   It sits with the Frog, Pitcher and Panther, and in black is none of them; the plate reads as armour. **The owner rules whether the sculpt stays.**

## Out of scope

- The prefab, animator and glance spark (JG-14c). Growing the atlas: if the swatches cannot carry the
  look, that is a task of its own, opened before this one.

## As built

_Filled at merge._
