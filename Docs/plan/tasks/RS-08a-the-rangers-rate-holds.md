# RS-08a — The Ranger's rate holds, and the first arrow is quick

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CC §4.2 (cadence), CC §4.3 (Focus)

## Goal

The Ranger shoots at one rate that only Quick Draw changes, and the first arrow after he stops leaves
in about a quarter of a second, with the bow's draw timed to that arrow.

## Why this shape

- **The ramp is data.** `FocusTracker` adds nothing when `MaxMultiplier` is 1. The Ranger's 1.3 is the only
  change to his rate besides Quick Draw, so it becomes 1. The stand-still clock keeps counting.
- **The slow first arrow has two causes.** The arrow leaves at `DamageFrame` 0.72 of a 0.91 s swing (0.65 s).
  `RangerAnimatorView.ShotSpeed` times the draw from the gap since the last shot, clamped to ≥ 1, so after a
  pause full draw lands at 0.72 s. Both change: the frame becomes 0.3 (0.27 s), and the event carries the
  draw time so the view never guesses it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Characters/Ranger.asset` | — | `_focusMaxMultiplier` 1, `_weaponDamageFrame` 0.3 |
| `Core/Combat/Weapon.cs` | Core | `DrawSeconds`: start-to-damage-frame time of the current swing |
| `Core/Events/CombatEvents.cs` | Core | `PlayerAttacked.DrawSeconds` |
| `Core/Combat/PlayerCombat.cs` | Core | publishes `Weapon.DrawSeconds` on `PlayerAttacked` |
| `Game/Views/RangerAnimatorView.cs` | Game | `ShotSpeed(drawSeconds)` fits full draw into the event's draw time |
| `Tests/Core/Combat/WeaponTests.cs` | Tests.Core | rules 3, 4 |
| `Tests/Game/Authoring/RangerTests.cs` | Tests.Game | rules 1, 2 |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | rule 5 |
| *ripple* | | `RangerSandboxLoop` and any fixture that builds `PlayerAttacked` |

## Public API

```csharp
public sealed class Weapon { public float DrawSeconds { get; } }          // 0 at rest
public readonly struct PlayerAttacked {
    public readonly float DrawSeconds;
    public PlayerAttacked(Vector2 facingXZ, WeaponKind kind = WeaponKind.Cone, float drawSeconds = 0f);
}
public static float RangerAnimatorView.ShotSpeed(float drawSeconds);
```

## Behaviour

1. Standing still never changes the Ranger's `Weapon.FireRate`. Only a modifier from a skill does.
2. The Ranger's arrow leaves 0.3 of a swing after it starts. His rate, 1.1 a second, is unchanged.
3. `Weapon.DrawSeconds` is `DamageFrame × interval`, sampled at swing start and held for the swing. 0 after `Reset`.
4. A swing that starts on the first still tick after moving looses its arrow `DrawSeconds` later. No
   leftover wait from the moving time.
5. `ShotSpeed(d)` = `FullDrawNormalized × AuthoredShotSeconds / d`, clamped to `[1, MaxShotSpeed]`.
   A non-positive or non-finite `d` gives `MaxShotSpeed`. The gap since the last shot is no longer read.

## Tests

| Test | Rule |
|---|---|
| `Ranger_FocusDoesNotRamp`: built spec, 3 s still, `FireRate.Value` == 1.1 | 1 |
| `Ranger_ReleasesAtThirtyPercent`: `Weapon.DamageFrame` == 0.3, rate 1.1 | 2 |
| `DrawSeconds_IsFrameTimesInterval_HeldThroughTheSwing` | 3 |
| `DrawSeconds_ZeroAfterReset` | 3 |
| `FirstSwingAfterRest_DamageFrameAtDrawSeconds` | 4 |
| `ShotSpeed_FitsFullDrawIntoDrawSeconds` (0.27 s → ≈ 2.67) | 5 |
| `ShotSpeed_ClampsAndRefusesNonFinite` | 5 |

## Manual verification (Editor / device)

1. **[Editor]** Stand still 5 s near a Frog: the arrows keep one steady beat.
2. **[Editor]** Run, stop: the bow draws fast and the arrow leaves in about ¼ s.

## Out of scope

- Other classes' Focus ramps and `PlayerAnimatorView` (the swing classes are switched off).

## As built

_2026-10-28._ As specced. `Ranger.asset`: Focus ×1, damage frame 0.3. `Weapon.DrawSeconds` set at swing
start (`DamageFrame × interval`), zeroed by `Reset`. `PlayerAttacked` gained `DrawSeconds` (default 0);
`PlayerCombat` and `RangerSandboxLoop` publish it. `RangerAnimatorView.ShotSpeed(drawSeconds)` =
0.6 × 1.2 / d, clamped `[1, 4]`; its `_clock` and `_lastShotAt` are gone with the gap.

Tests: the seven rows, plus `PlayerCombat_PublishesPlayerAttacked_OnSwingStart` now checks the draw time
and `Attack_TheShotSpeedFollowsTheDrawTimeNotTheGap` covers the view's use of it. Ripple: the
`Ranger_NumbersAreTheTables` row (0.3, ×1, 14 DPS planted); the four gap rows in
`RangerAnimatorViewTests` replaced. The Ranger's exact draw is 0.3 / 1.1 = 0.273 s → ×2.64 (the spec's
≈ 2.67 rounds the draw to 0.27).

Left alone: the sandbox's own `_releaseFraction` 0.72 (`RangerSandboxScope`, sandbox scene only).

EditMode 4 519 — 4 517 pass, 1 fail (`CharacterPreviewTests.Resize_…`, Traps §7), 1 inconclusive.
