# JG-14f — The Mossguard's spear

**Size:** M · **Depends on:** JG-14c · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §16.4; AR §18 (the M2-art rows)

Owner, 2026-10-31: the Mossguard carries a spear, which from above points where he faces. Raw seed 42:
`C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated/MossguardSpear-8e9dbfe7-seed42_00001_.glb`
(SHA-256 `7ef268515a4cd0d4e28a02bc5913d9a332365ce77d952e187b39000b9abbca44`).

## Goal

A spear in `handslot.r`; the shield shove becomes a 2 m right-arm stab over the raised shield.

## Files

| Path (under `Assets/_Project/`; tools from the root) | Kind | Purpose |
|---|---|---|
| `Tools/Blender/mossguard.py` | Blender 4.1 | `--prepare-spear <raw> --review-dir <dir>`; spear export |
| `Tools/Blender/Sources/MossguardSpear_src.glb` | LFS | **New.** The prepared source |
| `Art/Enemies/MossguardSpear.fbx` | generated | **New.** Static prop |
| `Animation/Masks/AM_Mossguard_RightArm.mask` | — | **New.** The Strike layer's mask |
| `Animation/Controllers/AC_Mossguard.controller` | — | Strike layer (rules 5–6) |
| *small edits* | | `Prefabs/Enemies/Mossguard.prefab` (rule 4); `Game/Views/EnemyAnimatorView.cs` (rule 7); `Data/Enemies/Mossguard.asset` (`_reach` 2.0); `Tests/Game/Art/MossguardModelTests.cs`, `Tests/Game/Views/MossguardBodyTests.cs`, `EnemyAnimatorViewTests.cs`, `Tests/Game/Authoring/JungleTests.cs` (the reach row) |

## Behaviour

1. **Prepared like the shield**: shaft, head and bindings kept, crumbs dropped, `repair_scan.py` run;
   one closed, connected, outward source. An export re-run differs only in the FBX timestamp.
2. **In budget.** 120–300 triangles, one `M_Enemy` slot, existing swatches (shaft `pitcher_root`,
   head `croak_moss`, vines `pitcher_leaf`). The three meshes ≤ 2,800 (JG-14b's row widens).
3. **It fits the hand.** Pivot at the middle grip wrap, 1.6–2.0 m long. At identity under `handslot.r`
   the head points the way KayKit's `sword_1handed.fbx` blade points under the same slot.
4. **The prefab carries it** under `handslot.r` at identity.
5. **The stab is the right arm alone.** A Strike layer above Guard and below Flinch, masked by
   `AM_Mossguard_RightArm` (`upperarm.r` to `handslot.r`), resting at weight 0. `Attack` plays
   `Melee_1H_Attack_Stab` there; Guard no longer has an `Attack` state. The thrust lands at the
   0.7 s wind-up's end within half a frame (`_windupFromSeconds`, `_strikeSeconds`).
6. **The shield stays up**: Guard still loops `Melee_Blocking`. *Write Defaults* off; `Rig_Medium` clips.
7. **The view drives a layer named `Strike`**, when present, by `FlinchLayer.Drive(animator, layer,
   attackStateId)`: 1 while the stab plays, else 0. Without one, nothing changes.
8. **It reaches 2 m.** `enemy.mossguard`'s reach is 2.0; nothing else in its numbers moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `MossguardSpear_StaysInBudget` | 120–300 tris, `M_Enemy`; the three ≤ 2,800 (2) |
| `MossguardSpear_FitsTheHand` | pivot, length, head along the sword's axis (3) |
| `MossguardBody_CarriesTheSpear` | prefab / spear under `handslot.r` at identity (4) |
| `MossguardBody_StrikeLayerIsTheRightArm` | layer order, mask bones, weight 0, `Attack` → stab (5) |
| `MossguardBody_StabLandsAtTheWindupsEnd` | 0.7 ± ½ frame (5) |
| `MossguardBody_GuardHoldsThroughTheStab` | Guard: `Melee_Blocking`, no `Attack` state (6) |
| `AnimatorView_DrivesAStrikeLayer` | a controller with Strike, attack / weight 1, then 0 (7) |
| `AnimatorView_NoStrikeLayer_Unchanged` | a Frog / no error, as before (7) |
| `Jungle_Mossguard_IsABulwarkWithItsNumbers` | reach 2.0 (8) |

## Manual verification (Editor / device)

1. **[Editor]** Renders: the spear in his right hand, pointing forward over the shield. Stage 11: the
   stab lands 2 m out, shield up.

## Out of scope

- Re-cutting the body or shield. A spear trail. Retuning damage or wind-up.

## As built

_Filled at merge._
