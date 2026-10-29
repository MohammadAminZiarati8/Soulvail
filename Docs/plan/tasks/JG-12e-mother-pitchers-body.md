# JG-12e — Mother Pitcher's body

**Size:** S · **Depends on:** JG-12c (move numbers), JG-12d (the model) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 7; GD §16.4; AR §18.2 (the M2-art rows)

## Goal

`MotherPitcher.prefab`, a variant of `Enemy.prefab` wearing `MotherPitcher.fbx`, with an animator that
sways, rears for the barrage and the pod, tips forward for the spray, flinches and wilts.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/MotherPitcher.prefab` | — | **New.** The body (rules 4–6) |
| `Animation/Controllers/AC_MotherPitcher.controller` | — | **New.** Idle, Attack by `Move`, Flinch layer, Death (rules 1–3) |
| `Tests/Game/Views/MotherPitcherBodyTests.cs` | Tests.Game | **New.** Rules 1–6 |

## Public API

None. `MotherPitcherBodyTests.BodyScale` = 2.5, the `_bodyScale` JG-12h authors.

## Behaviour

1. **Rooted.** The base layer's default is `Idle`; no locomotion blend; `EnemyAnimatorView._hops` off.
   `Dead` plays `Death` from any state.
2. **The attack follows the move.** `Attack` picks by `Move`: `BarrageMove` and `PodMove` play
   `Barrage`, `SprayMove` plays `Spray`, entered at `_windupFromSeconds`, its blow at `_strikeSeconds`
   lining up with the wind-up's end within half a frame.
3. **A flinch never stops a move.** `Hit` plays on a Flinch layer; every state has *Write Defaults*
   off, and every clip is one of `MotherPitcher.fbx`'s.
4. **A whole body.** Capsule off; the Animator on `AC_MotherPitcher` (culling transforms, no root
   motion) with an `EnemyAnimatorView`; feedback drives the skinned mesh and dissolves on
   `M_Enemy_Dissolve`; the wind-up glows danger.
5. **A giant.** At `BodyScale` the mesh is 3.5–4.5 m tall, and `EnemyHealthBar._heightMetres` sits
   0.2–0.6 m above its top.
6. **The player cannot stand in her.** The `CharacterController` radius is 1.0–1.6 m and the trigger
   capsule covers the cup.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `MotherBody_IsRootedAndDies` | the controller / read / `Idle` default, no blend tree, `Death` on `Dead`; `_hops` false (rule 1) |
| `MotherBody_AttackFollowsTheMove` | the controller / `Move` 1, 2, 3 / `Barrage`, `Spray`, `Barrage` (rule 2) |
| `MotherBody_BlowLandsAtTheWindupsEnd` | view numbers and clips / read / within half a frame (rule 2) |
| `MotherBody_EveryStateIsTheModelsAndNoWriteDefaults` | every state / read / as rule 3 |
| `MotherBody_IsAWholeBody` | the prefab / loaded / as rule 4 (Traps §5's dressing included) |
| `MotherBody_IsAGiantWithItsBarAbove` | mesh at `BodyScale` / bounds / as rule 5 |
| `MotherBody_CannotBeStoodIn` | colliders / read / as rule 6 |

## Manual verification (Editor / device)

1. **[Editor]** Seen in a run at JG-12h.

## Out of scope

- The bud's body: `Pitcher.prefab` at a smaller `_bodyScale` (JG-12h). The wedge and lob marks
  (JG-12f). The stones (JG-12g).

## As built

_Filled at merge._
