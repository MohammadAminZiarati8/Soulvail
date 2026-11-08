# BT-01 — Two axes at once, attack speed sets the cadence, Swift Hands

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.6 (Weapon row), CH §3.6.4 (Axe tier 2)

## Goal

He throws both axes together every 1.2 s, only with both in hand and only standing; attack speed
shortens the interval and speeds the flight alike; Third Axe becomes Swift Hands (+20% attack speed).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/AxeSystem.cs` | Core | `TryThrowPair`; `MaxCarry = 2`; flight speed × `PaceFactor` |
| `Core/Combat/PlayerCombat.cs` | Core | Ammo gate needs every carried axe in hand; the damage frame calls `TryThrowPair` |
| `Data/Characters/Beastmaster.asset` | data | `_weaponSwingsPerSecond` 0.8333, `_weaponFiresWhileMoving` 0 |
| `Data/Skills/Beastmaster/ThirdAxe.asset` | data | Renamed `SwiftHands.asset` through `AssetDatabase.RenameAsset` (GUID kept, the tree holds); id `skill.beastmaster.swift-hands` |
| `Data/Effects/Beastmaster/ThirdAxeAxeCarry.asset` | data | Renamed `SwiftHandsFireRate.asset`: `ModifyStat(FireRate, Percent, +0.2)` |
| `Core/Save/SaveMigrations.cs` | Core | One run step: `skill.beastmaster.third-axe` → `skill.beastmaster.swift-hands` |
| `Tests/Core/Combat/AxePairTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSnapshot.CurrentVersion` +1 · `English.asset` name/description · migration test · stat coverage |

## Public API

```csharp
public sealed class AxeSystem {
    public const int MaxCarry = 2;
    public const float PairSpreadDegrees = 8f;
    public float PaceFactor { get; }   // FireRate.Value / base swings per second, floor 0.1
    public int TryThrowPair(Vector3 hand, Vector3 toward, float damage, float now); // axes thrown
}
```

## Behaviour

1. **Carry is two at most:** `AxeCarry` resolves through `Math.Min(MaxCarry, …)`; a modifier past 2 does nothing.
2. **The pair:** `TryThrowPair` throws every axe in hand at once, ±`PairSpreadDegrees` either side of
   `toward` (one axe in hand flies straight at it); each turn point is the full `WeaponRange`
   along its own direction, whatever the target's distance. One `AxeThrown` per axe.
3. **Both in hand:** no swing starts unless `InHand == carry limit`; the old single-axe reservation goes.
4. **Cadence:** a throw every `1 / FireRate` s (1.2 s base). Swift Hands' +20% gives 1.0 s.
5. **Pace:** out and back flight speed is `AxeSpeed × PaceFactor`; with any attack-speed bonus the
   round trip still ends before the next throw for a stationary player at full range.
6. **Stands to throw:** `FiresWhileMoving` 0; axes already out keep flying and homing while he moves.
7. **Save:** a v(n) run holding `third-axe` loads holding `swift-hands`; other ids untouched.

## Tests

| Test | Given / When / Then |
|---|---|
| `Carry_ModifierPastTwo_StaysTwo` (1) | |
| `ThrowPair_TwoInHand_BothFlySpread` / `ThrowPair_CloseTarget_TurnsAtFullRange` (2) | |
| `OneAxeHome_NoSwing` (3) | |
| `Cadence_Base_OneThrowPer1_2s` / `Cadence_SwiftHands_OneThrowPerSecond` (4) | |
| `Pace_PlusTwentyPercent_FlightTwentyPercentFaster` / `Pace_BothBackBeforeNextThrow` (5) | |
| `Moving_NoThrow_AxesStillReturn` (6) | |
| `Migrate_ThirdAxe_BecomesSwiftHands` (7) | |
| `Dps_StandingFrog_TwoThrowsKill` | Frog 36 HP on the line / two throws / dead |

## Manual verification (Editor / device)

1. **[Editor]** Beastmaster, stage 1: both axes leave together; nothing thrown while the stick is held.
2. **[Editor]** Take Swift Hands: throws come visibly faster, axes back in time.

## Out of scope

- Cover stopping axes (BT-02). The animals' targets (BT-03). Axe Master (unbuilt).

## As built

_Filled at merge._
