# JG-08c — Old Croaker hits softer

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** JG-04 (Old Croaker), GD §12 (balance bands)

## Goal

Old Croaker's every hit (touch, slam, shockwave, tongue) does **12** instead of 22, so the Ranger's 90 HP
takes about 8 hits instead of 4.

## Why this shape

- **One number feeds all four attacks.** `CroakerBehaviour` puts `ContactDamage` on the fissure, the
  shockwave and the lane, and contact uses it too. A single data change lowers them all.
- **12** = 13 at stage 5 after the Jungle's +2 % a stage (`DepthScaling`). That is just under the Panther's
  14, and the boss's threat stays its pattern, not its numbers. The owner rules after a playtest.
- The Warden (Descent) is untouched.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Croaker.asset` | — | `_contactDamage` 22 → 12 |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | rule 1 |

Only these files change. No code changes.

## Behaviour

1. `enemy.croaker`'s `ContactDamage` is 12, and the Warden's is unchanged.

## Tests

| Test | Rule |
|---|---|
| `OldCroaker_HitsForTwelve`: `Croaker.asset` built, `ContactDamage` == 12; Warden's as authored | 1 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 5: a tongue or slam takes about one HP-bar segment (≈ 13 of 90). The fight is survivable
   with a few mistakes.

## Out of scope

- Separate per-attack numbers. The fight's length (~1 min) and Old Croaker's HP.

## As built

Built 2026-10-29. `Croaker.asset` now authors 12 contact damage; the existing Croaker behavior
uses that stat for touch, fissure, shockwave, and tongue. `OldCroaker_HitsForTwelve` pins the
asset's value and the Warden's unchanged 22.

Verification: one full EditMode run after JG-08b and JG-08c — 4,522 total, 4,521 pass, 0 fail,
1 inconclusive (animator clock by construction). `dotnet format whitespace` and `git diff --check`
clean. Manual Editor check remains: stage 5, take a tongue or slam hit and confirm roughly 13 HP
at that depth.
