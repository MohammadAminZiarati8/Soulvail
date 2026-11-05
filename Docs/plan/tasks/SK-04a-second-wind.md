# SK-04a — Second Wind in core

**Size:** M · **Depends on:** SK-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Second Wind); FX-05 (blocked hits, rejected debuffs)

## Goal

The roll gets shorter cooldowns, an after-roll guard that turns away the next hit, and a second
charge.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ChargeSkill.cs` | Core | Charges; the after-roll guard window |
| `Core/Combat/PlayerCombat.cs` | Core | The guard blocks one damage instance; posts the status |
| `Core/Effects/PlayerStat.cs` | Core | Append `MovementSkillCharges` (base 0), `RollGuardSeconds` (base 0) |
| `Tests/Core/Combat/SecondWindTests.cs` | Tests.Core | **New** |
| *small edits* | | `CombatEvents` (`ChargeEnded` gains nothing; new `ChargeChargesChanged(int Ready, int Max)`), `CombatantStats`, `RunSession` (wiring), stat coverage tests |

## Public API

```csharp
public sealed class ChargeSkill {
  public int MaxCharges { get; }        // 1 + MovementSkillCharges, read live
  public int ReadyCharges { get; }
  public bool IsGuarding { get; }       // the after-roll window is open
  internal bool ConsumeGuard();         // true once per window
}
```

## Behaviour

1. **Cooldown:** ranks reduce `MovementSkillCooldown` (the stat the roll already reads).
2. **Charges:** a roll spends one charge; charges refill one at a time, each over the cooldown.
   The roll is ready while `ReadyCharges` ≥ 1 and no roll is active. `CooldownFraction` reports
   the refilling charge. A class with no rank has 1 charge and behaves exactly as today.
3. `ChargeChargesChanged` publishes when `ReadyCharges` or `MaxCharges` changes.
4. **Guard:** with `RollGuardSeconds` > 0, a window of that length opens when a roll ends. The
   first damage instance that would land on the player inside it is blocked (`DamageResult.Blocked`,
   the `PlayerDamaged` blocked path FX-05 uses), its debuffs are refused, and the window closes.
   A new roll restarts the window.
5. The guard posts `status.second-wind` (buff) on the `StatusBoard` for the window's seconds left
   and clears it on use or expiry.
6. **Ranks** (for SK-04b): 1 — `MovementSkillCooldown` Flat −0.5; 2 — −0.5 more and
   `RollGuardSeconds` +1; 3 — `MovementSkillCharges` +1.
7. A stage boundary refills every charge and closes the window.
8. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rank1_CooldownHalfSecondShorter` / `Rank2_OneSecondShorter` (1, 6) |
| `NoRank_OneCharge_AsToday` (2) |
| `TwoCharges_RollTwiceBackToBack` / `Charges_RefillOneAtATime` (2) |
| `CooldownFraction_ReportsRefillingCharge` (2) |
| `ChargesChanged_Publishes` (3) |
| `Guard_BlocksFirstHitAfterRoll` / `Guard_SecondHitLands` (4) |
| `Guard_ExpiresAfterOneSecond` / `Guard_RefusesDebuffs` (4) |
| `Guard_PostsAndClearsStatus` (5) |
| `StageBoundary_RefillsAndCloses` (7) |
| `Tick_DoesNotAllocate` (8) |

## Out of scope

- Assets, the roll button's charge pips, the dodge pop (SK-04b).

## As built

_Filled at merge._
