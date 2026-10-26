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

_2026-10-26._ **Old Croaker stands 2.99 m in the arena**: `Croaker.fbx`'s 3.42 m × `Model` 0.4 × `Croaker.asset`'s
`_bodyScale` 2.2, which `EnemyHitFeedback` puts on the root, so the colliders stay the capsule boss's. Made by
Editor commands, as JG-03b's were: the controller through `AnimatorController`, the prefab a variant saved
from an instance of `Enemy.prefab`.

- **Each Attack state plays at 1×, from frame 0, not at `AttackRate`.** Core's telegraphs are constants
  matched to the clips at speed 1 (JG-04a/b): Slam's feet land on frame 27 = 0.9 s, Lash's tongue is
  furthest out (9.02 m) on frame 24 = 0.8 s, and Croak's call is on frame 30 = 1.0 s. One rate cannot bring
  three clips' blows to three wind-ups; `AttackRate` can hit only one. The view still writes it, so
  the controller keeps the parameter and no warning fires. `_windupFromSeconds` is 0 and `_strikeSeconds`
  0.9 (the Slam's), so the rate it writes is 1 for a slam anyway.
- **Attack is a sub-state machine** holding `Slam`, `Lash` and `Croak`. Any State goes to each of them on
  `Attack` + `Move` Equals 1/2/3 + not `Dead`, and each can transition to itself, so a chained hop's
  second telegraph restarts `Slam`, which is 1.5 s long, though the next take-off comes at 1.3 s. Each
  exits to Locomotion at 0.92. Death (1.5×) and the Flinch layer (Hit at 1.4×, `AM_Frog_UpperBody`)
  copy `AC_Frog`'s timings.
- **`_hasMove` is asked on the first wind-up, not at injection.** A pooled body may be injected while
  inactive, and an inactive Animator reports no parameters. It is still asked once (`_moveChecked`).
- **`_strideSpeed` is 1.8 m/s, the boss's move speed**, so the approach is a full Hop at 1×. Its true
  stride at this scale is ~3.7 m/s (the Frog's 1.8 × 2.75 × the Hop's ¾ speed), which would make the
  approach a half-blend at the 0.6× floor. The feet slide a little; it reads as a lumbering hop. `_hops`
  is off: the boss walks in, and the slam's leap is the `Slam` clip.
- **No floating bar.** The variant removes `EnemyHealthBar` and switches its `HealthBar` child off.
  `EnemyView.HealthBar` and `EnemyViews` already take null.

**Deviation (said here):** **`Croaker.asset`'s `_tint` is now white**, outside "the prefab". The capsule's
moss green multiplies a textured body's colour and would draw the frog at about a quarter of its
brightness. Every textured body (Frog, Pitcher, Puffball) has white. `Croaker_PrefabSize_NoFloatingBar`
pins it. `LashTelegraphSeconds` (0.8) is a local constant in the test until JG-04e puts it on
`CroakerBehaviour`.

**Verified.** `CroakerBodyTests` 4 / 4 and `EnemyAnimatorViewTests.View_WithoutMoveParam_Unchanged`.
With the Frog, Pitcher, Puffball, Croaker-model and boss-dispatch fixtures, that is 54 / 54. Full EditMode
suite through `TestRunnerApi`: **4 434: 4 432 passed / 1 failed / 1 inconclusive**, the two known rows. No
compiler warning; `dotnet format whitespace` clean on the three .cs files.
