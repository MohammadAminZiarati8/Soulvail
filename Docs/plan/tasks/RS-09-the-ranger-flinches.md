# RS-09 — The Ranger flinches when hit

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** RS-02a (the Ranger's animator), RS-03d (the roll), JG-04 (the Croaker's masked flinch)

## Goal

A hit that hurts the Ranger plays a short upper-body flinch over whatever the legs are doing.

## Why this shape

- **`PlayerAnimatorView` already flinches; the Ranger never used it.** Its body runs `RangerAnimatorView`
  and `AC_Ranger`, which had no `Hit`. The guards are that view's, plus the killing blow and the roll.
- **Upper body only, on a layer of its own.** CC §4.2: being hit never roots the player, so the legs keep
  the stick. The layer reuses `Upper Body`'s mask and sits above it, so the draw keeps time underneath.
- **KayKit `Hit_A`** (`Rig_Medium_General`), at speed 1.5 (≈ 0.45 s): a read, not a stun.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Animation/Controllers/AC_Ranger.controller` | — | `Hit` trigger; `Flinch` layer (rule 6) |
| `Game/Views/RangerAnimatorView.cs` | Game | subscribes to `PlayerDamaged`; rules 1–5 |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | rules 1–6 |

## Public API

```csharp
public const string RangerAnimatorView.FlinchLayer = "Flinch";
```

## Behaviour

1. A `PlayerDamaged` that is not `Blocked`, has `ToHp > 0` and `HpFraction > 0` sets `Hit`.
2. A blocked hit, or one the Aegis took whole (`ToHp` 0), does not.
3. A killing blow (`HpFraction` 0) does not: the death answers it.
4. A hit between `ChargeStarted` and `ChargeEnded` does not.
5. A hit after `PlayerDied` does not.
6. `AC_Ranger` has a `Hit` trigger and a top layer `Flinch`: weight 1, override, `Upper Body`'s mask;
   default state `Empty` (no motion); Any State → `Hit` on `Hit`; `Hit` plays `Hit_A` and returns to
   `Empty` at its end, or at once when `Dodging`; no state writes defaults.

## Tests

| Test | Rule |
|---|---|
| `Flinch_AHurtingHitSetsHit` | 1 |
| `Flinch_ABlockedOrShieldedHitDoesNot` | 2 |
| `Flinch_AKillingBlowDoesNot` | 3 |
| `Flinch_NotMidRoll` (and after `ChargeEnded` it does) | 4 |
| `Flinch_ACorpseDoesNot` | 5 |
| `AcRanger_HasAnUpperBodyFlinchLayer` (the asset) | 6 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 1, stand by a Frog: each bite jerks the Ranger's chest and arms; the legs keep running
   when moving; the bow still shoots. A roll through a hit shows no flinch.

## Out of scope

- A hit flash or tint, camera shake, a hit sound, a random `Hit_A`/`Hit_B` pick, other classes' bodies.

## As built

As specified. The layer was built through the `AnimatorController` API in the Editor (diff to
`AC_Ranger` is additive: one parameter, one layer, two states, three transitions). `Hit` → `Empty`
at exit time 0.85 with a 0.15 s fade; Any State → `Hit` 0.05 s, re-triggerable, so a second bite
restarts the flinch. The view's rule 4 guard is a `_rolling` latch set by `ChargeStarted` and
cleared by `ChargeEnded` (`Flinch_NotMidRoll`); the controller's `Dodging` exit covers a roll that
starts mid-flinch (`AcRanger_HasAnUpperBodyFlinchLayer`). EditMode 4 534 pass, 0 fail,
1 inconclusive (animator clock, by construction).
