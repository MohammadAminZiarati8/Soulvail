# M7-05l — The Frog's body

**Size:** M · **Depends on:** M7-05j (the model), M7-05h (`EnemyAnimatorView`) · **Branch:** `m7-05l-the-frogs-body`
**Design refs:** GD §9.1, GD §16.4, GD §17.1; AR §18.4 · **Ledger rows:** none

The first half of the owner's go of 2026-09-29: *"now use this frog as enemies for the first stage
… I mean use the frog instead of what we have now."* The Jungle's stage-1 enemy is the Rootling
(M7-05i), a Husk-role archetype on a body of its own. This task makes the Frog such a body, and
M7-05m puts it in the Rootling's place.

## Goal

`Frog.prefab`, a whole enemy body. It hops, lashes its tongue, flinches and flips over dead, driven
by the same `EnemyAnimatorView` as the Rootling. It flashes white on a hit and glows red-orange over
its wind-up, through `M_Enemy`.

## Why this shape

- **The Rootling's body, with the Frog's clips.** `EnemyAnimatorView` already reads only what core
  publishes, and its three numbers (stride, wind-up start, strike) are per prefab. So the Frog needs
  no code: a controller on the view's six parameters, a mask, and a variant of `Enemy.prefab`, as
  `Rootling.prefab` is.
- **Each number comes from the clip.** The stride is what the Hop covers, 0.9 m in 0.6 s at the
  model's size, times the prefab's 1.2. The strike is the frame the tongue is furthest out. Tests
  measure both in the clip, so moving the lash in `frog.py` without moving the prefab is a red row.
- **The flinch leaves the hind legs hopping.** The mask is the spine up and the forelegs, as the
  Rootling's is the spine up.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Frog.prefab` | — | **New.** A variant of `Enemy.prefab` wearing `Frog.fbx` (rule 4) |
| `Animation/Controllers/AC_Frog.controller` | — | **New.** Locomotion, Attack, Death and a Flinch layer (rules 1–3) |
| `Animation/Masks/AM_Frog_UpperBody.mask` | — | **New.** The Flinch layer's mask (rule 3) |
| `Tests/Game/Views/FrogBodyTests.cs` | Tests.Game | **New.** Rules 1–5 |

## Behaviour

1. **The legs follow the body.** Locomotion is a 1D blend on `Speed` from `Idle` at 0 to `Hop` at
   the view's `_strideSpeed`, played at `WalkRate`. Death is reached from any state on `Dead`.
2. **The tongue lands when core's blow does.** Attack plays `Attack` at `AttackRate`, entered at
   `_windupFromSeconds`. `_strikeSeconds` is the time the tongue's tip is furthest out, within half a
   frame.
3. **A flinch is the spine up.** `Hit` plays on a Flinch layer masked by `AM_Frog_UpperBody`, which
   has the spine, head, jaw, tongue and forelegs, and not the body, root or hind legs. Every state
   has *Write Defaults* off, and every clip is one of `Frog.fbx`'s.
4. **`Frog.prefab` is a whole body.** It is a variant of `Enemy.prefab`, so its colliders are the
   Husk's. It switches the capsule off and wears `Frog.fbx` at 1.2, with its Animator on `AC_Frog`,
   culling transforms, no root motion, and an `EnemyAnimatorView` on that Animator. The feedback drives
   its skinned mesh and dissolves on `M_Enemy_Dissolve`.
5. **It is about a metre tall, and its bar floats just over it.** The mesh at the prefab's scale is
   0.9–1.2 m tall, and `EnemyHealthBar._heightMetres` is 0.2–0.6 m above that.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Frog_ControllerNamesItsStates` | `AC_Frog` / read / six parameters; the states, speeds, blend, entry offset, death condition, masked Flinch, no *Write Defaults*, only Frog clips (rules 1–3) |
| `Frog_StrideIsTheHopsPointInTheBlend` | the blend and the view / read / `Hop` at `_strideSpeed`, `Idle` at 0 (rule 1) |
| `Frog_StrikeIsTheTonguesFullestReach` | the Attack clip / sampled every frame / its furthest reach at `_strikeSeconds` ± half a frame, after `_windupFromSeconds` (rule 2) |
| `Frog_FlinchLeavesTheHindLegsHopping` | the mask / read / spine, head and forelegs in; body, root and hind legs out (rule 3) |
| `Frog_IsAWholeBody` | the prefab / read / a variant of `Enemy.prefab`, the Husk's colliders, the feedback on the skinned mesh and dissolve, the capsule off, the Animator and view wired (rule 4) |
| `Frog_StandsAboutAMetreTall`, `Frog_HealthBarFloatsJustOverItsHead` | the mesh at the model's scale, the bar's height / read / 0.9–1.2 m; 0.2–0.6 m over it (rule 5) |

## Manual verification (Editor / device)

1. **[Editor]** Seen at M7-05m: frogs hop at the player, glow red-orange over the wind-up and lash
   the tongue at the blow, flash white and flinch on a hit, and flip onto their backs and fade on death.
2. **[device]** Frame time with 28 animated frogs. Deferred with every device row.

## Out of scope

- Naming the body on an asset and rostering it (M7-05m).
- The Lunger's leap: `Leap` is authored, and M7-01a owns the archetype and its animator state.

## As built

**The numbers.** Scale 1.2, so 1.04 m to the top of the eyes and 1.2 m wide. `_strideSpeed` 1.8
m/s, so the Husk's 2 m/s plays the hop at 1.11×. `_windupFromSeconds` 0 and `_strikeSeconds` 0.5,
the measured lash, so the Husk's 0.4 s wind-up plays the whole anticipation at 1.25×. `Attack` returns
to Locomotion at 0.92 over 0.12 s, `Death` plays at 1.5× and is belly up by 0.33 s, inside the 0.5 s
fade, and `Hit` plays at 1.4× and settles at 0.85 over 0.1 s: the Rootling's timings. The mask is 11 of
21 transforms. The health bar is at 1.5 m.

**Deviations.** *(1)* **The bar's height is `_heightMetres`, not the anchored position.**
`EnemyHealthBar` places the bar at `_heightMetres` when it shows. So the variant sets both to 1.5, and
the test reads the field. The Rootling's variant set only the anchor, so its bar has floated at the
shared body's 2.1 m, 0.7 m over its head. That is not changed here. *(2)* One row beyond the
Rootling's: `Frog_StrikeIsTheTonguesFullestReach` measures the strike in the clip where M7-05h read
it off the pose sheet.

**Verified.** `FrogBodyTests` 7 / 7. Render: `Temp/Renders/frogs_hollow_game_camera.png`, a pack in
the Hollow from the game camera's 57° and 16 m, beside the Ranger, one glowing mid-wind-up. Suite
counts are M7-05m's.
