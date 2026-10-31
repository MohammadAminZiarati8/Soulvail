# JG-14f — The Mossguard's spear

**Size:** M · **Depends on:** JG-14c · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §16.4; AR §18 (the M2-art rows)

Owner, 2026-10-31: the Mossguard carries a spear, which from above points where he faces. Raw seed 42:
`C:/Users/mazia/Documents/Codex/2026-10-23/sa/outputs/generated/MossguardSpear-8e9dbfe7-seed42_00001_.glb`
(SHA-256 `7ef268515a4cd0d4e28a02bc5913d9a332365ce77d952e187b39000b9abbca44`).

Owner size follow-up, 2026-10-31: overall archetype scale 1.1 (+10%). The whole spawned body,
props, colliders and bar scale together; core body radius follows at 0.44 m. Reach remains 2.0.

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

2026-10-31 — Built. Seed 42 raw SHA-256 verified unchanged. `--prepare-spear` keeps shaft,
head and bindings, runs `repair_scan.py`, then audits the serialized source: 179,998 triangles,
one connected outward shell, zero boundary/non-manifold edges. Source SHA-256:
`065d8cbd9ab0a5bab19054861964ab8ee9d6fbcd91fd905e9e5bbfa0e337169f`.
Runtime: 280 triangles (2,720 with body/shield), one M_Enemy slot; existing root/moss/leaf swatches.
1.8 m, pivot in middle wrap (raw y=.035), head along KayKit sword's +Y; identity on handslot.r.
Rebuild compared element-by-element: only FBX CreationTimeStamp differs. Body/shield untouched.
Strike sits between Guard and Flinch, right-arm-only mask, weight 0 except Attack; Stab peak at
14/30 s, offset 8/30 s, timed to 0.7 s within half a frame. Guard keeps Blocking; shield pose
unchanged. Write Defaults off. Reach alone changes to 2.0. Optional Strike driven by FlinchLayer.
Nine specified rows pass; preparation/determinism assertions pass. Full EditMode 4,786 / 4,785 passed / 0 failed / 1 known clock inconclusive; UTC start 02:00:50.
Zero compiler errors/new analyzer warnings; format clean. Owner size follow-up: bodyScale 1.1.
Imported prefab front/side/game and mid-thrust renders:
`C:/Users/mazia/.codex/visualizations/2026/10/31/01a18f9f-af6f-7ce2-a80c-032369daad18/Mossguard/unity{-larger,}-{guard,stab}-{front,side,game}.png`.
Manual remaining: stage 11 in-run feel and device judgement.
