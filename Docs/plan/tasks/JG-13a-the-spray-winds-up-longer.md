# JG-13a — The spray winds up longer

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 7; JG-12c rule 3, JG-12e rule 2, JG-12f rule 3

## Goal

Mother Pitcher's spray winds up for 2.5 s, not 1 s, and her body, wedge and follow-through keep time
with it. (Worst case a stone's shadow is ~4.3 m away: 1.3 s at 3.2 m/s, so 2.5 s leaves reaction room.)

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyAnimatorView.cs` | Game | `_releaseAtClipSpeed`: the attack's rate returns to 1 when the wind-up ends |
| `Tests/Game/Views/EnemyAnimatorViewTests.cs` | Tests.Game | rule 4's two rows |
| *small edits* | | `Core/Ai/MotherPitcherBehaviour.cs` (`SprayTelegraphSeconds` 2.5); `Prefabs/Enemies/MotherPitcher.prefab` (`_releaseAtClipSpeed` on); `Tests/Core/Ai/MotherPitcherAttackTests.cs` (rule 1 row); `Tests/Game/Views/SprayConeViewTests.cs` (rule 3 row); `Tests/Game/Views/MotherPitcherBodyTests.cs` (rule 4's prefab row) |
| *ripple* | | `MotherPitcherAttackTests` rows that `Step(1f)` through a spray step the new wind-up |

## Public API

```csharp
public const float SprayTelegraphSeconds = 2.5f;   // MotherPitcherBehaviour; barrage and pod stay 0.8
// EnemyAnimatorView
[SerializeField] private bool _releaseAtClipSpeed;   // off by default
```

## Behaviour

1. **The spray winds up 2.5 s.** `EnemyTelegraph.Duration` and `SprayArmed.Seconds` are 2.5; the bite
   is decided at 2.5 s and not on any tick before. Barrage and pod wind-ups are unchanged.
2. **Her blow lands at the wind-up's end.** `MotherBody_BlowLandsAtTheWindupsEnd` passes unchanged for
   all three moves (it reads the constant): the Spray release lands at 2.5 s within half a frame.
3. **The wedge fills over the wind-up.** A cone armed from a 2.5 s `SprayArmed` fills 0.4 at 1 s and
   reaches 1 at 2.5 s.
4. **The follow-through plays at clip speed.** With `_releaseAtClipSpeed` on, `AttackRate` returns to
   1 once `EnemyTelegraph.Duration` has passed in `Step`; a new telegraph restarts the count. Off, the
   rate holds as today, so no other enemy changes. `MotherPitcher.prefab` has it on.
   *Why:* at 2.5 s the Spray rate is ~0.13, so the stream after the blow would crawl past her next move.

## Tests

| Test | Given / When / Then |
|---|---|
| `Mother_Spray_WindsUpForTwoAndAHalfSeconds` | Mother arms a spray / step 2.49 s, then 0.02 s / telegraph and armed seconds 2.5; no `SprayFired` at 2.49, one after |
| `MotherBody_BlowLandsAtTheWindupsEnd` | existing row, rerun / — / passes with the new constant |
| `Step_FillsOverATwoAndAHalfSecondWindup` | cone armed for 2.5 s / step 1 s, then 1.5 s / `Fill` 0.4, then 1 |
| `Attack_ReleasesAtClipSpeedAfterTheWindup` | flag on, telegraph 2.5 s / step 2.4 s, then 0.2 s / `AttackRate` = the wind-up rate, then 1 |
| `Attack_WithoutReleaseFlag_RateHolds` | flag off, telegraph 0.8 s / step 2 s / `AttackRate` unchanged |
| `MotherBody_ReleasesAtClipSpeed` | `MotherPitcher.prefab` / read its view / `_releaseAtClipSpeed` on |

## Manual verification (Editor / device)

1. **[Editor]** Test start 10: the spray wedge grows for 2.5 s; standing in a stone's shadow at the
   bite takes no damage; after the blow her stream plays at normal speed.
2. **[device]** The 2.5 s wedge reads on a 6-inch screen.

## Out of scope

- Re-authoring the Spray clip; changing barrage or pod timing; any other enemy's animator.

## As built

2026-10-30. `SprayTelegraphSeconds` 2.5 (barrage, pod 0.8). `EnemyAnimatorView._releaseAtClipSpeed`
(off by default): a telegraph arms a countdown of its `Duration`; `Step` sets `AttackRate` to 1 when
it runs out; a new telegraph restarts it; `Forget` clears it. On in `MotherPitcher.prefab` only.

Ripple in `MotherPitcherAttackTests`: `Finish()` steps `SprayTelegraphSeconds` then 1 s; spray rows
step 2.5 s; `Mother_EveryWindup_AtLeastPointSix` finishes each wind-up with a 10 s step; the
allocation row's middle tick is 2.5 s, so all three moves still release. `Mother_Spray_HoldsItsAim`
now steps 2.49 + 0.02 and leaves the seconds check to the new rule 1 row.

`MotherBody_BlowLandsAtTheWindupsEnd` passes unchanged at 2.5 s.
EditMode 4 665 pass, 0 fail, 1 known inconclusive. Compile errors 0, warnings 0, format clean.
Manual steps 1–2 not yet run.
