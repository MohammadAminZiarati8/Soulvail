# JG-14c — The Mossguard's body

**Size:** S · **Depends on:** JG-14a, JG-14b · **Branch:** `dev` (direct)
**Design refs:** GD §8.1, GD §5.4, GD §16.4; AR §18 (the M2-art rows)

A body on KayKit's clips, as JG-07c. A blocked hit, which today would flash and flinch for
nothing, instead **jolts the shield and sparks**.

## Goal

`Mossguard.prefab`: the lizard with its shield in `handslot.l`, always held up, walking, bashing,
jolting on a blocked hit, flinching on a real one, dying.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Mossguard.prefab` | — | **New.** Variant of `Enemy.prefab` (rules 4–6) |
| `Animation/Controllers/AC_Mossguard.controller` | — | **New.** `AC_Rootling`'s shape plus a Guard layer (rules 1–3) |
| `Tests/Game/Views/MossguardBodyTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `Game/Views/EnemyHitFeedback.cs` (`_glance`; rule 7); `Game/Views/EnemyAnimatorView.cs` (`Block` trigger; rule 8); `Tests/Game/Views/EnemyHitFeedbackTests.cs`, `EnemyAnimatorViewTests.cs` |

## Public API

```csharp
// EnemyHitFeedback (serialized, private): null on every body but the Mossguard's.
[SerializeField] private ParticleSystem _glance;
// EnemyAnimatorView: sets trigger "Block" on a blocked hit when its controller has one.
```

## Behaviour

1. **Legs.** Base layer: a 1D blend on `Speed`, `Idle_B` at 0, `Walking_B` at the view's
   `_strideSpeed`, played at `WalkRate`; `Death_A` from any state on `Dead`.
2. **The shield is always up.** A Guard layer on `AM_UpperBody`, weight 1, loops `Melee_Blocking`.
   On it `Attack` plays `Melee_Block_Attack` (the blow at the wind-up's 0.7 s end within half a
   frame, through `_windupFromSeconds`/`_strikeSeconds`), and `Block` plays `Melee_Block_Hit`.
3. **A real hit flinches** `Hit_A` on the Flinch layer above Guard, resting at 0 (JG-09).
   *Write Defaults* off everywhere; every clip is `Rig_Medium`'s.
4. **A whole body.** Capsule off, `Mossguard.fbx` with `AC_Mossguard` (culling transforms, no root
   motion), `EnemyAnimatorView`, feedback on its skinned mesh dissolving on `M_Enemy_Dissolve`;
   `MossguardShield.fbx` under `handslot.l` at identity. Wind-up glows danger.
5. **Bigger than the Ranger.** At the prefab's scale it stands 1.15–1.35× the Ranger's height; bar
   0.2–0.6 m over its head; `CharacterController` radius 0.45–0.6 m.
6. **The glance spark sits on the shield's face**: a child `ParticleSystem`, not playing on awake,
   a 6–10 spark burst on `M_Impact`, lifetime ≤ 0.25 s, no loop.
7. **A blocked hit sparks, never flashes.** `EnemyHitFeedback` on `EnemyDamaged.Blocked`: no white
   flash, `_glance.Play()` when set. An unblocked hit flashes as today and never sparks.
8. **A blocked hit jolts, never flinches.** `EnemyAnimatorView` on `Blocked` sets `Block` (if the
   controller has it) and never `Hit`.

## Tests

| Test | Given / When / Then |
|---|---|
| `MossguardBody_LegsBlendIdleWalk` | blend: `Idle_B` 0, `Walking_B` `_strideSpeed` (1) |
| `MossguardBody_GuardLayerHoldsTheShield` | Guard: `AM_UpperBody`, weight 1, `Melee_Blocking` (2) |
| `MossguardBody_BashLandsAtTheWindupsEnd` | blow at 0.7 ± ½ frame (2) |
| `MossguardBody_BlockPlaysBlockHit` | `Block` plays it (2) |
| `MossguardBody_RigMediumClipsNoWriteDefaults` | every state (3) |
| `MossguardBody_IsAWholeBody` | prefab; shield under `handslot.l` (4) |
| `MossguardBody_IsBiggerThanTheRanger` | heights, bar, radius (5) |
| `MossguardBody_GlanceSitsOnTheShield` | as rule 6 (6) |
| `HitFeedback_Blocked_SparksNoFlash` | blocked / no flash, glance (7) |
| `HitFeedback_Unblocked_FlashesNoSpark` | 10 / flash, no glance (7) |
| `AnimatorView_Blocked_SetsBlockNotHit` | blocked / `Block` set, `Hit` not (8) |
| `AnimatorView_NoBlockParam_SetsNothing` | a Frog / nothing, no warning (8) |

## Manual verification (Editor / device)

1. **[Editor]** Seen in a run at JG-14d.

## Out of scope

- A glance sound; JG-14d.

## As built

2026-10-31 — Built. Enemy variant with Mossguard on AC_Mossguard; shield attached at identity to
handslot.l. Rootling-shaped legs blend Idle_B/Walking_B at 0/0.8 m/s; Guard uses AM_UpperBody at 1,
blocking, bashing and jolting; Flinch rests at 0 above it. All states use Rig_Medium clips with
Write Defaults off. Bash samples the forward-most hand at frame 14; frames 8–14 map to the 0.7 s
wind-up. Guard dies with the base rather than holding up the corpse. Model scale 1.05 gives 1.23×
Ranger height; bar 1.92 m, controller radius 0.5 m. Shield-face glance emits eight M_Impact sparks
for 0.2 s. Blocked damage skips flash/Hit, plays optional sparks/Block; unblocked damage keeps the
old flash/flinch. Pool reset clears sparks and Block; death clears Block. Twelve specified tests
pass. Full EditMode: 4,769 total / 4,768 passed / 0 failed / 1 known clock inconclusive; fresh UTC
start 2026-10-31 00:55:13. Compiler errors 0; new analyzer warnings 0; format clean (5 files).
Prefab blocking render checked. Manual: see it in a Jungle run at JG-14d; owner sculpt judgement
remains owed. JG-14d/e not built.
