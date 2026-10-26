# JG-04d — Old Croaker's body

**Size:** M · **Depends on:** JG-04a, JG-04c · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 7, GD §16.4

## Goal

`Croaker.prefab` replaces the capsule. It hops while approaching, plays `Slam`, `Lash` or `Croak` by
the telegraph's `Move`, flinches and dies on its back.

## Why this shape

- **The Frog's body, scaled.** `AC_Croaker` is `AC_Frog` with the Croaker's clips. It adds an Attack
  sub-state chosen by a new `Move` int parameter.
- **`EnemyAnimatorView` sets `Move` before it fires `Attack`.** Unity warns on a parameter a
  controller lacks, so the view checks once, at bind, whether it has `Move` (`_hasMove`). Every existing
  controller lacks it, so no other body changes.

## Files

| Path (under `Assets/_Project/`) | Purpose |
|---|---|
| `Prefabs/Enemies/Croaker.prefab` | **New.** Variant of `Enemy.prefab` wearing `Croaker.fbx` |
| `Animation/Controllers/AC_Croaker.controller` | **New.** Locomotion; Attack → Slam / Lash / Croak by `Move`; Flinch layer; Death |
| `Tests/Game/Views/CroakerBodyTests.cs` | **New.** Rules 1–4 |
| *small edits* | `Game/Views/EnemyAnimatorView.cs` (`Move` int, set only when the controller has it); `Data/Enemies/Croaker.asset` (the prefab); `Tests/Game/Views/EnemyAnimatorViewTests.cs` (rule 2) |

## Behaviour

1. Locomotion blends `Idle` → `Hop` on `Speed`; death plays `Death` from any state.
2. A telegraph with `Move` n plays clip n. The `Slam` impact frame lands on the telegraph's end, and
   the `Lash` reach frame lands on its strike, within half a frame. A controller without `Move` behaves
   as before.
3. The flinch uses `AM_Frog_UpperBody`, and every state has *Write Defaults* off.
4. The body reads as a boss on a phone: it is ≥ 2.8 m tall in the arena, and the boss bar, not a
   floating bar, shows its health.

## Tests

| Test | Rule |
|---|---|
| `Croaker_LocomotionAndDeath` | 1 |
| `Croaker_MovePicksTheClip_ImpactOnTheTelegraphsEnd` / `View_WithoutMoveParam_Unchanged` | 2 |
| `Croaker_FlinchMask_WriteDefaultsOff` | 3 |
| `Croaker_PrefabSize_NoFloatingBar` | 4 |

## Manual verification

1. **[Editor]** Jungle stage 5: the frog hops in, crouches and leaps, and lands on the red-orange circle
   as the ring goes out. It croaks after each phase beat as Frogs appear. On death it rolls onto its back.
2. **[Editor]** At 411 × 914 the circle, the ring and the frog all fit on screen together.

## Out of scope

Dust or impact VFX; audio; the mud hazard.

## As built

_Filled at merge._
