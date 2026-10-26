# JG-07c — The Panther's body

**Size:** M · **Depends on:** JG-07b · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §16.4; AR §18 (the M2-art rows)

The panther model becomes a whole enemy body, as the Puffball did at JG-03b, with one line of view
code. Old Croaker's `LaneArmed` views already draw the strip, and they listen to every lane, not the
boss's alone. They also shoot the frog's tongue down every lane, which is the one line: the tongue
waits for `LaneArmed.Tongue` (JG-07a rule 12). Otherwise the body only has to crouch while the strip
arms and bound while it dashes.

## Goal

`Panther.prefab`, a variant of `Enemy.prefab` wearing `Panther.fbx`, with an animator that prowls,
crouches on the wind-up, bounds through the dash, flinches and dies.

## Why this shape

- **The dash is locomotion, not an attack.** `EnemyAnimatorView` already feeds `Speed` and `WalkRate`
  from the body's real velocity. The locomotion blend therefore runs `Idle` (0) → `Prowl` (the stride
  speed) → `Lunge` (6 m/s). The 24 m/s dash lands on `Lunge` with no new parameter and no event.
  `WalkRate` clamps at 4, which plays the 12-frame bound at 0.1 s a stride while dashing, so the clip
  is authored slow for that (JG-07b).
- **The wind-up is the attack.** `EnemyTelegraph` plays `Attack` → `Crouch`, entered at
  `_windupFromSeconds`, held to the end of the 0.9 s wind-up. Then `Speed` takes it into `Lunge`.
- **The flinch is the head and chest**, so a hit mid-prowl doesn't stop the legs.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Panther.prefab` | — | **New.** A variant of `Enemy.prefab` wearing `Panther.fbx` (rules 4, 5) |
| `Animation/Controllers/AC_Panther.controller` | — | **New.** Locomotion, Attack, Death and a Flinch layer (rules 1–3) |
| `Animation/Masks/AM_Panther_Front.mask` | — | **New.** The Flinch layer's mask (rule 3) |
| `Tests/Game/Views/PantherBodyTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `Game/Views/BossViews.cs` (`LaneStrip` keeps `Tongue`; `OnLaneFired` shoots only then); `Tests/Game/Views/BossViewTests.cs` (rule 7) |

## Public API

None new. `BossViews.LaneStrip` gains a `Tongue` field copied from `LaneArmed`.

## Behaviour

1. **The feet follow the body, the dash included.** Locomotion is a 1D blend on `Speed`: `Idle` at 0,
   `Prowl` at the view's `_strideSpeed`, `Lunge` at 6. It is played at `WalkRate`. Death plays
   `Death` from any state on `Dead`.
2. **The crouch is held for the wind-up.** `Attack` plays `Crouch` at `AttackRate`, entered at
   `_windupFromSeconds`. `_strikeSeconds` is 0.9, the wind-up's end, within half a frame.
3. **A flinch is the front.** `Hit` plays on a Flinch layer masked by `AM_Panther_Front` (neck, head,
   jaw, chest), not the root, hips, legs or tail. Every state has *Write Defaults* off, and every clip
   is one of `Panther.fbx`'s.
4. **`Panther.prefab` is a whole body.** A variant of `Enemy.prefab`, so its colliders are the Husk's.
   It switches the capsule off and wears `Panther.fbx`, its Animator on `AC_Panther` (culling
   transforms, no root motion) with an `EnemyAnimatorView` on it. Feedback drives its skinned mesh and
   dissolves on `M_Enemy_Dissolve`. The wind-up glows danger, the default.
5. **It is a big cat, and its bar floats just over it.** At the prefab's scale the mesh is 1.6–2.0 m
   nose to rump and 0.7–0.95 m at the shoulder. `EnemyHealthBar._heightMetres` sits 0.2–0.6 m above
   its top.
6. **Its colliders fit the dash.** The `CharacterController` radius is at most 0.45 m, so a 1.2 m
   lane's width (two half-widths of 0.6) holds the body. It is not wider than the strip it warned of.
7. **A tongue strikes only down a tongue's lane.** On `LaneFired`, the strip flashes for every lane,
   and a tongue shoots only when that lane's `LaneArmed` said `Tongue`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `PantherBody_LocomotionBlendsIdleProwlLunge` | the controller / the blend tree / three motions at 0, `_strideSpeed`, 6, on `Speed` (rule 1) |
| `PantherBody_CrouchHoldsToTheWindupsEnd` | the view's numbers and the clip / read / `_strikeSeconds` 0.9 ± half a frame (rule 2) |
| `PantherBody_FlinchIsTheFront` | the mask / read / neck, head, jaw, chest only (rule 3) |
| `PantherBody_EveryStateIsTheModelsAndNoWriteDefaults` | every state / read / clip from `Panther.fbx`, *Write Defaults* off (rule 3) |
| `PantherBody_IsAWholeBody` | the prefab / loaded / as rule 4 (rule 4) |
| `PantherBody_IsABigCatWithItsBarAbove` | the mesh at the prefab's scale / bounds / as rule 5 (rule 5) |
| `PantherBody_FitsItsLane` | the controller / radius / ≤ 0.45 (rule 6) |
| `Lane_WithoutATongue_FlashesButShootsNone` *(BossViewTests)* | a lane armed with `Tongue` false, then fired / views stepped / the strip flashes, no `TongueView` taken (rule 7) |
| `Lane_WithATongue_ShootsOne` *(BossViewTests)* | the same with `Tongue` true / stepped / one tongue, as JG-06a (rule 7) |

## Manual verification (Editor / device)

1. **[Editor]** Seen in a run at JG-07d.

## Out of scope

- A dash trail or dust. If the owner wants one after playing it, it's a JG-06b-sized task of its own.
- The archetype and stage 6 (JG-07d).

## As built

_Filled at merge._
