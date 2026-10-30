# JG-14c — The Shellback's body

**Size:** M · **Depends on:** JG-14b; JG-14a (`EnemyDamaged.Blocked`) · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §5.4, GD §16.4; AR §18 (the M2-art rows)

The model becomes a body, as at JG-07c. A blocked hit gets its own read, **a glance**: today it
would flash white and flinch on a hit that did nothing.

## Goal

`Shellback.prefab`, a variant of `Enemy.prefab` on `Shellback.fbx`: walks, dips and drives its plate,
flinches, dies. A blocked hit sparks pale off the plate instead of flashing or flinching.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Shellback.prefab` | — | **New.** Variant of `Enemy.prefab` with a `Glance` spark child (rules 4–6) |
| `Animation/Controllers/AC_Shellback.controller` | — | **New.** `AC_Panther`'s shape, its clips swapped (rules 1–3) |
| `Animation/Masks/AM_Shellback_Back.mask` | — | **New.** The Flinch layer's mask (rule 3) |
| `Tests/Game/Views/ShellbackBodyTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `Game/Views/EnemyHitFeedback.cs` (`_glance` particle field; rule 7); `Game/Views/EnemyAnimatorView.cs` (rule 8); `Tests/Game/Views/EnemyHitFeedbackTests.cs`, `EnemyAnimatorViewTests.cs` (rules 7–8) |

## Public API

```csharp
// EnemyHitFeedback (serialized, private): null on every body but the Shellback's.
[SerializeField] private ParticleSystem _glance;
```

## Behaviour

1. **The feet follow the body.** Locomotion is a 1D blend on `Speed`: `Idle` at 0, `Walk` at the
   view's `_strideSpeed` (from JG-14b's stride). Played at `WalkRate`. `Death` from any state on `Dead`.
2. **The bash is the attack.** `Attack` plays `Bash` at `AttackRate`; `_strikeSeconds` is 0.7, the
   wind-up's end, within half a frame.
3. **A flinch is the back.** `Hit` on a Flinch layer masked by `AM_Shellback_Back` (`body` and the
   back legs, not `plate` or `head`), resting at weight 0 (JG-09). *Write Defaults* off everywhere;
   every clip is `Shellback.fbx`'s.
4. **A whole body.** Capsule off, `Shellback.fbx` on an Animator with `AC_Shellback` (culling
   transforms, no root motion), an `EnemyAnimatorView`, feedback on its skinned mesh dissolving on
   `M_Enemy_Dissolve`. The wind-up glows danger, the default.
5. **A heavy, low body, its bar just over it.** 1.3–1.8 m long at the prefab's scale (JG-14b rule
   7). `EnemyHealthBar._heightMetres` 0.2–0.6 m above its top. `CharacterController` radius 0.5–0.7 m.
6. **The glance spark sits on the plate**: a child `ParticleSystem` at the front, plate height, not
   playing on awake, a burst of 6–10 pale sparks on `M_Impact`, lifetime ≤ 0.25 s, no loop.
7. **A blocked hit glances.** `EnemyHitFeedback` on `EnemyDamaged` with `Blocked`: no white flash,
   and `_glance.Play()` when it is set. An unblocked hit flashes as today and never sparks.
8. **A blocked hit does not flinch.** `EnemyAnimatorView` sets no `Hit` trigger on `Blocked`.

## Tests

| Test | Given / When / Then |
|---|---|
| `ShellbackBody_LocomotionBlendsIdleWalk` | blend: 0, `_strideSpeed` on `Speed` (1) |
| `ShellbackBody_BashStrikesAtTheWindupsEnd` | `_strikeSeconds` 0.7 ± ½ frame (2) |
| `ShellbackBody_FlinchIsTheBack` | mask bones (3) |
| `ShellbackBody_EveryStateIsTheModelsAndNoWriteDefaults` | every state (3) |
| `ShellbackBody_IsAWholeBody` | prefab (4) |
| `ShellbackBody_IsHeavyWithItsBarAbove` | mesh, bar, radius (5) |
| `ShellbackBody_GlanceSitsOnThePlate` | the child, as rule 6, ahead of centre (6) |
| `HitFeedback_Blocked_SparksAndDoesNotFlash` | blocked / no flash, glance plays (7) |
| `HitFeedback_Unblocked_FlashesAndDoesNotSpark` | 10 / flash, no glance (7) |
| `AnimatorView_Blocked_DoesNotFlinch` | blocked / no `Hit` trigger (8) |

## Manual verification (Editor / device)

1. **[Editor]** Seen in a run at JG-14d.

## Out of scope

- A glance sound (no audio yet), or arrows bouncing off as bodies.
- The archetype and stage 11 (JG-14d).

## As built

_Filled at merge._
