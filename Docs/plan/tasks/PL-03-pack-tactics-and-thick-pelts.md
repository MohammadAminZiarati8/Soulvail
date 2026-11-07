# PL-03 — Pack Tactics and Thick Pelts

**Size:** M · **Depends on:** PL-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader Lv 14); CH §3.6.1

## Goal

The Packleader's two Lv-14 passives: animals hit harder together, and take less damage.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | The shared-target bonus and slow; damage taken scaled |
| `Core/Effects/PlayerStat.cs` | Core | Append `PackTactics` (0), `PackTacticsSlow` (0), `PetDamageTaken` (1) |
| `Core/Effects/PetStats.cs` | Core | Answers the three |
| `Data/Skills/Packleader/PackTactics*.asset`, `ThickPelts*.asset` | — | **New.** Three ranks each |
| `Data/Effects/Packleader/PackTactics*.asset`, `ThickPelts*.asset` | — | **New** |
| `Tests/Core/Ai/PackTacticsTests.cs` | Tests.Core | **New** |
| *small edits* | | `Packleader.asset` rows; `English`/`Pseudo`; `SkillIconBook`; `PackleaderPathTests` gains rows; stat coverage tests |

## Behaviour

1. **Pack Tactics:** a bite on an enemy that another pet has as its quarry and is within its reach
   of deals `× (1 + PackTactics)`.
2. With `PackTacticsSlow` > 0, an enemy two pets are biting is slowed by that fraction, refreshed
   for 1 s on each bite (bosses immune).
3. **Thick Pelts:** every pet's incoming damage is multiplied by `PetDamageTaken`, clamped 0.1–1.
4. **Ranks.** Pack Tactics: `PackTactics` +0.10 / +0.05 / +0.05, and rank 3 `PackTacticsSlow` +0.3.
   Thick Pelts: `PetDamageTaken` −0.10 / −0.10 / −0.10, and rank 3 `PetRescueSeconds` −1.
5. The Packleader now holds 4 + 4 + 3 + 3 of its 20 ranks.

## Tests

| Test | Given / When / Then |
|---|---|
| `TwoOnOne_BonusApplies` / `Alone_NoBonus` (1) |
| `TwoBiting_Slowed_Refreshed` / `Boss_NotSlowed` (2) |
| `PetDamageTaken_Scales_Clamped` (3) |
| `PackTactics_Ranks` / `ThickPelts_Ranks_RescueOneSecond` (4) |
| `Path_FourteenRanksAuthored` (5) |

## Out of scope

- Hawk, Stampede.

## As built

_Filled at merge._
