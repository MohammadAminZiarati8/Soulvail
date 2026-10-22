# JG-03b — The Puffball's body

**Size:** M · **Depends on:** JG-03a (the model), M7-05p (the Pitcher's body, the pattern) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §9.1, GD §16.4, GD §17.1; AR §18.4

The second step of the owner's go of 2026-10-22, as M7-05p was for the Pitcher: the model becomes a
whole enemy body. *"It swells and its cap glows cyan-white during the windup; the burst is a puff of
spores, not fire."* JG-03c puts it in a run.

## Goal

`Puffball.prefab`, a whole enemy body. It waddles, swells and glows cyan-white over the Bloater's
wind-up, bursts in a puff of spores when it dies, and squashes when hit, driven by the same
`EnemyAnimatorView` as the Pitcher.

## Why this shape

- **No AI code, and no new view.** `BloaterBehaviour` publishes the same `EnemyTelegraph` a Husk does
  and kills itself on the tick its wind-up ends, so `EnemyAnimatorView`'s rule brings the clip's
  largest swell to the detonation. A Bloater always dies there, so `Burst` is its `Death` state.
- **The Pitcher's controller, with the Puffball's clips.** `AC_Puffball` is `AC_Pitcher` with its
  motions and mask swapped, so the tuned transitions carry over. The flinch mask is the cap: the foot
  keeps waddling under a hit.
- **The glow and the burst are two small switches on `EnemyHitFeedback`**, which already owns what a
  wind-up and a death look like. `_sporeTelegraph` glows the new `Palette.SporeGlow`, a cyan-white,
  where every other body glows danger red-orange; GD §16.4 gives telegraphs that colour and the owner
  ruled otherwise for this body, so it is one named exception, not a colour authored on a prefab.
  `_deathBurst` names a particle system to play on the body's death and clear on its return to the pool.
- **The burst rides the corpse.** `VFX_SporeBurst` is a child of the body: it plays on `EnemyDied`,
  which a Bloater's detonation and any other death both publish, simulates in world space and is gone
  inside `EnemySystem.CorpseTime`, before the body goes back to the pool. Its puffs are soft billboards
  on URP's own `ParticlesUnlit` material (a package asset, so no new material), tinted by the
  particles' own cream-to-khaki colours, and slowed by drag so the cloud stops near the 3 m blast. The blast
  ring `TelegraphRings` draws stays red-orange: that is the danger, the spores are the look.
- **Fat.** At 1.3 the mushroom stands 1.33 m and 1.27 m across, bulkier than the Frog at 1.2.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Puffball.prefab` | — | **New.** A variant of `Enemy.prefab` wearing `Puffball.fbx` and `VFX_SporeBurst` (rules 4, 5, 7) |
| `Animation/Controllers/AC_Puffball.controller` | — | **New.** Locomotion, Attack, Death and a Flinch layer (rules 1–3) |
| `Animation/Masks/AM_Puffball_Cap.mask` | — | **New.** The Flinch layer's mask (rule 3) |
| `Prefabs/Vfx/VFX_SporeBurst.prefab` | — | **New.** The puff of spores (rule 7) |
| `Tests/Game/Views/PuffballBodyTests.cs` | Tests.Game | **New.** Rules 1–5, 7 |
| *small edits* | | `Game/Views/EnemyHitFeedback.cs` (`_sporeTelegraph`, `_deathBurst`); `Game/Presentation/Palette.cs` (`SporeGlow`); `Tests/Game/Views/EnemyHitFeedbackTests.cs` (rules 6, 7) |

## Public API

```csharp
// Palette
public static readonly Color SporeGlow; // #D4F8FF
// EnemyHitFeedback (serialized, private)
[SerializeField] private bool _sporeTelegraph;        // the wind-up glows SporeGlow, not Danger
[SerializeField] private ParticleSystem _deathBurst;  // played on this body's EnemyDied, cleared by ResetVisuals
```

## Behaviour

1. **The foot follows the body.** Locomotion is a 1D blend on `Speed` from `Idle` at 0 to `Waddle`
   at the view's `_strideSpeed`, played at `WalkRate`. Death plays `Burst`, from any state on `Dead`.
2. **The swell peaks when core's detonation lands.** Attack plays `Swell` at `AttackRate`, entered at
   `_windupFromSeconds`. `_strikeSeconds` is the time the cap is largest, within half a frame.
3. **A flinch is the cap.** `Hit` plays on a Flinch layer masked by `AM_Puffball_Cap`, which has the
   cap and not the root, body or stalk. Every state has *Write Defaults* off, and every clip is one of
   `Puffball.fbx`'s.
4. **`Puffball.prefab` is a whole body.** A variant of `Enemy.prefab`, so its colliders are the Husk's.
   It switches the capsule off and wears `Puffball.fbx` at 1.3, its Animator on `AC_Puffball`, culling
   transforms, no root motion, with an `EnemyAnimatorView` on that Animator. The feedback drives its
   skinned mesh, dissolves on `M_Enemy_Dissolve` without a stretch, glows spore-white and bursts
   `VFX_SporeBurst`.
5. **It is fat, and its bar floats just over it.** The mesh at the prefab's scale is 1.2–1.5 m tall
   and at least 1.15 m wide; `EnemyHealthBar._heightMetres` is 0.2–0.6 m above its top.
6. **A spore wind-up glows cyan-white.** With `_sporeTelegraph` on, the wind-up's emission rises in
   `Palette.SporeGlow` and drops at the strike, exactly as the danger glow does; off, it is danger, as
   before. `SporeGlow` is far from `Palette.Danger`.
7. **The burst plays on this body's death and is gone before the body is.** `_deathBurst` plays on
   an `EnemyDied` for this body's id and not for another's, and `ResetVisuals` stops and clears it.
   `VFX_SporeBurst` is one burst at its start, not looping, not playing on awake, simulated in world
   space with shape-only scaling, every particle dead within `EnemySystem.CorpseTime`, and no colour
   it can take near a reserved one.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Puffball_ControllerNamesItsStates` | `AC_Puffball` / read / six parameters; the states, speeds, blend, entry offset, death condition, masked Flinch, no *Write Defaults*, only Puffball clips (rules 1–3) |
| `Puffball_StrideIsTheWaddlesPointInTheBlend` | the blend and the view / read / `Waddle` at `_strideSpeed`, `Idle` at 0 (rule 1) |
| `Puffball_StrikeIsTheSwellsPeak` | the Swell clip / sampled every frame / the cap largest at `_strikeSeconds` ± half a frame, after `_windupFromSeconds` (rule 2) |
| `Puffball_FlinchLeavesTheFootWaddling` | the mask / read / cap in; root, body and stalk out (rule 3) |
| `Puffball_IsAWholeBody` | the prefab / read / variant of `Enemy.prefab`, the Husk's colliders, feedback wired with spore glow and burst, capsule off, Animator and view wired (rule 4) |
| `Puffball_IsFat`, `Puffball_HealthBarFloatsJustOverItsCap` | the mesh at the model's scale, the bar's height / read / 1.2–1.5 m tall, ≥ 1.15 m wide; 0.2–0.6 m over it (rule 5) |
| `Puffball_SporeBurstIsAOneShotPuff` | `VFX_SporeBurst` / read / rule 7's settings, lifetime + burst time ≤ `CorpseTime`, gradient keys and start colours ≥ 0.12 from every reserved colour (rule 7) |
| `EnemyHitFeedbackTests.Glow_SporeWindUpRisesToSporeGlow`, `Glow_SporeGlowIsNotDanger` | a body with `_sporeTelegraph` / a wind-up / halfway glow is `SporeGlow` × half, black at the strike; `IsDanger(SporeGlow)` false (rule 6) |
| `EnemyHitFeedbackTests.Burst_PlaysOnItsOwnDeath`, `Burst_ResetClearsIt` | a body with a `_deathBurst` / another's death, then its own, then a reset / not playing, playing, stopped with no particles (rule 7) |

## Manual verification (Editor / device)

1. **[Editor]** Seen at JG-03c: Puffballs waddle at the player, swell and glow cyan-white when close,
   burst into a cream-khaki puff of spores inside the red-orange blast ring, squash when hit, and
   collapse when killed at range too.
2. **[device]** Frame time with several bursts at once. Deferred with every device row.

## Out of scope

- Naming the body on an asset and rostering it (JG-03c).
- A spore cloud that outlives the corpse, or that lingers as a hazard: it is a look, and core's blast
  is instantaneous.
- The enemy showcase scene (M7-05r's) — not asked for.

## As built

**The numbers.** `Model` at 1.3: 1.33 m tall, 1.27 m across, bar at 1.7 m. `_strideSpeed` 1.755 m/s
(the Waddle's nominal 1.35 m/s × 1.3), so the Bloater's 2.2 m/s plays it at 1.25×. `_windupFromSeconds`
0 and `_strikeSeconds` 0.7, measured (the cap's largest, 1.36), so the Bloater's 0.8 s wind-up plays
`Swell` at 0.875×. `AC_Pitcher`'s timings, unchanged: `Burst` at 1.5× is flat by 0.2 s, inside the
0.5 s fade; `Hit` at 1.4×. The mask is 1 of 7 transforms. `VFX_SporeBurst`: 36 soft puffs at t = 0,
0.8–1.5 m, 5–9 m/s under a drag of 3, so they stop short of 3 m and are about 2.3 m out at 0.5 s;
lifetime 0.35–0.5 s, cream to pale khaki, fading out. Made by Editor commands, as M7-05p's were: the
mask from the model's hierarchy, the controller a copy of `AC_Pitcher` with its motions swapped, the
prefab a variant saved from an instance of `Enemy.prefab`, the burst built through the particle API.

**Findings.** *(1)* **Unity's render from the game camera found what the pose sheet did not**, as at
M7-05p: the eyes, a little below the cap's equator, sat on the lower rim seen from 57° and the mushroom
read as faceless. `puffball.py` now places them at 0.75 m, and `Puffball.fbx` was re-exported
(`Temp/Renders/puffball_unity.png`, the Frog and the Pitcher beside it). *(2)* **A pale body under an
additive glow clips to white.** The cream cap at 0.9 × `SporeGlow` renders white-hot with cyan at its
edges, which is the owner's cyan-white; it is also close to the white hit flash, told apart by the 0.8 s
it lasts and the swell under it. *(3)* **The first puff was muddy**: khaki stacked over the green floor
read as smoke, so the colours moved toward cream.

**Deviations.** *(1)* **`Tools/Blender/puffball.py` and `Puffball.fbx` changed** (finding 1), outside the
Files table; `PuffballModelTests` still 8 / 8. *(2)* **`Tests/Game/Presentation/PaletteTests.cs`**:
`Palette_ThePactFrameIsNotATenthColour` counts the palette's colours so a new one is argued there; it
counts 26 and names `SporeGlow`. *(3)* **The puffs draw on URP's `ParticlesUnlit`**, not `M_Impact`:
`M_Impact` has no texture, so a billboard on it is a square. The spec was corrected before the burst
was built.

**Verified.** `PuffballBodyTests` 8 / 8; with `PuffballModelTests`, `EnemyHitFeedbackTests` (4 new rows),
`PitcherBodyTests` and `FrogBodyTests`, 54 / 54. EditMode suite through `TestRunnerApi`:
**4 409 — 4 407 passed / 1 failed / 1 inconclusive**, the two known rows. No compiler warning.
