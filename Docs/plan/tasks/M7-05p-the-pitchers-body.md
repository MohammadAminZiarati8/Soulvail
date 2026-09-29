# M7-05p — The Pitcher's body

**Size:** M · **Depends on:** M7-05o (the model), M7-05l (the Frog's body, the pattern) · **Branch:** `m7-05p-the-pitchers-body`
**Design refs:** GD §8.1, GD §9.1, GD §16.4, GD §17.1; AR §18.4 · **Ledger rows:** none

The second step of the owner's go of 2026-09-29 for the Jungle's Spitter as a pitcher plant, as
M7-05l was for the Frog: the model becomes a whole enemy body. M7-05q puts it in a run.

## Goal

`Pitcher.prefab`, a whole enemy body. It shuffles on its roots, leans back with its lid flipping open
and spits as core's shot leaves, flinches, and topples over dead, driven by the same
`EnemyAnimatorView` as the Frog. It flashes white on a hit and glows red-orange over its wind-up,
through `M_Enemy`.

## Why this shape

- **No code.** A Spitter publishes the same `EnemyTelegraph` a Husk does, with its 0.7 s wind-up, and
  releases its shot on the tick that wind-up ends (`SpitterBehaviour`). So the view's rule that
  brings `_strikeSeconds` of the clip to the end of the wind-up puts the spit on screen at the moment
  the shot leaves, exactly as it puts the Frog's tongue on the blow.
- **The Frog's body, with the Pitcher's clips.** `AC_Pitcher` is `AC_Frog` with its motions swapped,
  so the transition timings the Rootling and the Frog were tuned to carry over. The mask is the jug
  and everything it carries, as the Frog's is the spine up.
- **Each number comes from the clip.** The stride is the Shuffle's 0.44 m in 0.4 s at the prefab's
  scale; the strike is the frame the mouth is furthest forward. Tests measure both in the clip.
- **As tall as the player.** At 1.1 the plant stands 1.49 m, the Ranger's 1.5, where the Frog is 1.0.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Pitcher.prefab` | — | **New.** A variant of `Enemy.prefab` wearing `Pitcher.fbx` (rule 4) |
| `Animation/Controllers/AC_Pitcher.controller` | — | **New.** Locomotion, Attack, Death and a Flinch layer (rules 1–3) |
| `Animation/Masks/AM_Pitcher_UpperBody.mask` | — | **New.** The Flinch layer's mask (rule 3) |
| `Tests/Game/Views/PitcherBodyTests.cs` | Tests.Game | **New.** Rules 1–5 |

## Behaviour

1. **The roots follow the body.** Locomotion is a 1D blend on `Speed` from `Idle` at 0 to `Shuffle`
   at the view's `_strideSpeed`, played at `WalkRate`. Death is reached from any state on `Dead`.
2. **The spit leaves when core's shot does.** Attack plays `Attack` at `AttackRate`, entered at
   `_windupFromSeconds`. `_strikeSeconds` is the time the mouth is furthest forward, within half a
   frame.
3. **A flinch is the jug.** `Hit` plays on a Flinch layer masked by `AM_Pitcher_UpperBody`, which has
   the jug, mouth, lid and leaves, and not the hips, root or root legs. Every state has *Write
   Defaults* off, and every clip is one of `Pitcher.fbx`'s.
4. **`Pitcher.prefab` is a whole body.** It is a variant of `Enemy.prefab`, so its colliders are the
   Husk's. It switches the capsule off and wears `Pitcher.fbx` at 1.1, with its Animator on
   `AC_Pitcher`, culling transforms, no root motion, and an `EnemyAnimatorView` on that Animator. The
   feedback drives its skinned mesh and dissolves on `M_Enemy_Dissolve`.
5. **It stands about as tall as the player, and its bar floats just over it.** The mesh at the
   prefab's scale is 1.3–1.7 m tall, and `EnemyHealthBar._heightMetres` is 0.2–0.6 m above that.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pitcher_ControllerNamesItsStates` | `AC_Pitcher` / read / six parameters; the states, speeds, blend, entry offset, death condition, masked Flinch, no *Write Defaults*, only Pitcher clips (rules 1–3) |
| `Pitcher_StrideIsTheShufflesPointInTheBlend` | the blend and the view / read / `Shuffle` at `_strideSpeed`, `Idle` at 0 (rule 1) |
| `Pitcher_StrikeIsTheSpitsSnap` | the Attack clip / sampled every frame / the mouth furthest forward at `_strikeSeconds` ± half a frame, after `_windupFromSeconds` (rule 2) |
| `Pitcher_FlinchLeavesTheRootsWalking` | the mask / read / jug, mouth, lid and leaves in; hips, root and legs out (rule 3) |
| `Pitcher_IsAWholeBody` | the prefab / read / a variant of `Enemy.prefab`, the Husk's colliders, the feedback on the skinned mesh and dissolve, the capsule off, the Animator and view wired (rule 4) |
| `Pitcher_StandsAboutAsTallAsThePlayer`, `Pitcher_HealthBarFloatsJustOverItsLid` | the mesh at the model's scale, the bar's height / read / 1.3–1.7 m; 0.2–0.6 m over it (rule 5) |

## Manual verification (Editor / device)

1. **[Editor]** Seen at M7-05q: Pitchers shuffle up to their range, lean back with the lid flipping
   open and glowing red-orange, spit as the shot leaves, flash and flinch when hit, and topple and
   fade when killed.
2. **[device]** Frame time with animated Pitchers and Frogs together. Deferred with every device row.

## Out of scope

- Naming the body on an asset and rostering it (M7-05q).
- Where the glob starts. `ProjectileView` flies core's shot from the body's root, on the floor, not
  from the lid; a launch height per body is Game-side code of its own.

## As built

**The numbers.** Scale 1.1, so 1.49 m to the top of the lid, with the health bar at 1.8 m.
`_strideSpeed` 1.21 m/s, so the Spitter's 2.8 m/s plays the shuffle at 2.3×. `_windupFromSeconds` 0
and `_strikeSeconds` 0.6, the measured snap, so the Spitter's 0.7 s wind-up plays the lean-back and
the spit at 0.86×. `AC_Frog`'s timings, unchanged: `Attack` returns to Locomotion at 0.92 over
0.12 s, 1.29 s after the wind-up starts and inside the Spitter's 0.9 s recovery; `Death` plays at
1.5× and is on its side by 0.33 s, inside the 0.5 s fade; `Hit` plays at 1.4×. The mask is 5 of 16
transforms. Made by an Editor command, not by hand: the mask from the model's hierarchy, the
controller as a copy of `AC_Frog` with its motions swapped, the prefab as a variant saved from an
instance of `Enemy.prefab`.

**Findings.** *(1)* **Unity's render from the game camera found what the pose sheet did not**: at the
spit, the mouth tipped past the jug bared the jug's lip as a yellow crescent inside the rim. It was
fixed in `pitcher.py`, and M7-05o's commit, not yet pushed, was amended to carry the fix (its *As
built* finding 2). *(2)* **The glob starts at the plant's feet.** `SpitterBehaviour` fires from
`SelfPosition`, the body's root on the floor, and `ProjectileView` arcs it from there, so the shot
does not leave the lid at 1.4 m. The grey capsule had the same gap; it is a follow-up, not this task.

**Deviations.** *(1)* **No PROGRESS entry and no ROADMAP row**, as for M7-05a to M7-05o.

**Verified.** `PitcherBodyTests` 7 / 7; with `FrogBodyTests` and `EnemyAnimatorViewTests`, 32 / 32;
with every Pitcher, Frog and Rootling model and body fixture after the M7-05o fix, 40 / 40. Renders:
`Temp/Renders/pitchers_hollow_game_camera.png` and `pitchers_hollow_closeup.png`, a pack in the
Hollow from the game camera's 57°, beside two Frogs and the Ranger, in Idle, Shuffle, the wind-up,
the spit and Death. Suite counts are M7-05q's.
