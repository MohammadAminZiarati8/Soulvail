# SK-08 — Skirmisher retune

**Size:** S · **Depends on:** SK-07 · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Running Shot row, Triggers line; owner, 2026-11-06)

## Goal

Running the Skirmisher pays: Running Shot reaches 125% of the standing rate at rank 4, and
Whirlwind and Storm Run cast as soon as one enemy is in reach. Data only.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Data/Skills/Skirmisher/RunningShotMovingFireRate.asset` | asset | `_value` −0.6 → −0.5 |
| `Data/Skills/Skirmisher/RunningShot{2,3,4}MovingFireRate.asset` | assets | `_value` 0.15 → 0.25 |
| `Data/Skills/Skirmisher/Whirlwind.asset` | asset | trigger threshold 3 → 1 |
| `Data/Skills/Skirmisher/StormRun.asset` | asset | trigger threshold 5 → 1 |
| *small edits* | | `Localisation/English`, `Localisation/Pseudo` (Running Shot rows), `Tests/Game/Authoring/SkirmisherPathTests.cs`, `Tests/Core/Combat/RunningShotTests.cs` |

## Behaviour

1. **Running Shot:** moving fire rate 50 / 75 / 100 / 125% of standing at ranks 1–4 (rank 1 flat
   −0.5, ranks 2–4 flat +0.25 each on `MovingFireRate`). Standing still stays 100%.
2. **Above standing:** at rank 4, a draw while moving takes `0.25 / 1.25` of the base draw, i.e.
   the Ranger fires faster on the run than standing. Core needs no change; if it caps the rate at 1,
   stop and raise it.
3. **Whirlwind:** trigger `EnemiesWithin6m ≥ 1`; cooldown and rings unchanged.
4. **Storm Run:** trigger `EnemiesInAcquireRange ≥ 1`; cooldown 30 s and the rest unchanged.
5. **Loc:** rank 1 *"Fire while moving, at 50% of your standing rate."*; ranks *"75%"*, *"100%"*,
   *"125% — faster on the run than standing."*; Pseudo regenerated from those rows.

## Tests

| Test | Given / When / Then |
|---|---|
| `SkirmisherPathTests` rank table (1) | Running Shot ranks read −0.50, +0.25, +0.25, +0.25 |
| `SkirmisherPathTests` Whirlwind trigger (3) | threshold 1 |
| `SkirmisherPathTests` Storm Run trigger (4) | threshold 1 |
| `RunningShotTests.Rate_SamplesAtTheStartOfEachDraw` (1, 2) | `LearnRunningShot` uses −0.5 / +0.25; cases: rank 1 moving 0.50, rank 4 moving 1.25, rank 4 standing 1.0 |
| `SkirmisherPathTests.Loc_Keys` (5) | still passes; English rows read as rule 5 |

## Manual verification (Editor / device)

1. **[Editor]** Skirmisher, Running Shot rank 4: strafing fires visibly faster than standing.
2. **[Editor]** Whirlwind and Storm Run learned, one enemy in reach: both cast.

## Out of scope

- Ricochet's trigger (stays ≥ 3), cooldowns, damage, other paths.
- Any core or view code.

## As built

_Written at merge as the commit body, ≤ 1 500 bytes._
