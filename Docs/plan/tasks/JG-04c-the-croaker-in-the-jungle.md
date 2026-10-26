# JG-04c — Old Croaker in the Jungle

**Size:** M · **Depends on:** JG-04b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1, GD §9.2; M4-01b rule 1

## Goal

A second boss can exist. `RunSession` picks a boss's behaviour by its id, and the Jungle's boss stages
are Old Croaker.

## Why this shape

- **A map, not a switch.** `RunSession` builds a `Dictionary<ContentId, Func<EnemyAgent, IEnemyBehaviour>>`
  once per run: `boss.warden → WardenBehaviour` and `boss.croaker → CroakerBehaviour`. The factory
  `EnemySystem` takes looks the id up. An id with no entry is refused at `RunSession.Start`, alongside
  the existing check that the catalog holds the boss. This is the dispatch `RunSession`'s own comment
  says the second boss would need.
- **The Jungle's boss roster is Croaker every 5th, for now.** `TryGetBossFor` is modulo-based, so it
  can't say "stage 5 only". Until the second Jungle boss exists, all six Jungle boss stages are Croaker,
  which already beats the off-theme Warden. Stage-specific entries come with that second boss.
- **Health.** Old Croaker uses `enemy.warden`'s HP and contact damage. GD §9.1 rule 5's 75–120 s is the
  owner's playtest to judge; tuning is a later task.

## Files

| Path (under `Assets/_Project/`) | Purpose |
|---|---|
| `Data/Enemies/Croaker.asset` | **New.** `enemy.croaker`: Warden's numbers, the Croaker prefab (a capsule until JG-04d) |
| `Data/Enemies/CroakerBoss.asset` | **New.** `boss.croaker`: phases 1 / 0.66 / 0.33, summoning 3 then 4 `enemy.frog` |
| `Tests/Core/Run/BossDispatchTests.cs` | **New.** Rules 1–3 |
| *small edits* | `Core/Run/RunSession.cs` (the map); `Data/Modes/Jungle.asset` (`_bossRoster` → `boss.croaker`, 5); wherever `WardenBoss.asset` is registered in the catalog (the two new assets) |
| *ripple* | `Tests/Game/Content/ContentValidationTests` rows that list bosses |

## Behaviour

1. A boss spawned as `boss.croaker` fights with `CroakerBehaviour`, and `boss.warden` still with `WardenBehaviour`.
2. `RunSession.Start` refuses a mode whose roster names a boss with no behaviour, naming the id.
3. A Jungle run's stage 5 spawns Old Croaker. Descent's stage 5 is still the Warden.
4. Phase 2 calls in 3 Frogs and phase 3 calls in 4.

## Tests

| Test | Rule |
|---|---|
| `Croaker_FightsWithCroakerBehaviour` / `Warden_StillTheWarden` | 1 |
| `Start_RefusesABossWithNoBehaviour` | 2 |
| `Jungle_Stage5_IsTheCroaker` / `Descent_Stage5_IsTheWarden` | 3 |
| `CroakerBoss_PhasesSummonFrogs` (Tests.Game, asset row) | 4 |

## Manual verification

1. **[Editor]** Jump to Jungle stage 5. A big capsule hop-slams: a red-orange circle where you stood,
   then a ring outward from it. At 66 % the arena clears and 3 Frogs come.

## Out of scope

The body (JG-04d); the tongue lash (JG-04e); HP tuning; the mud hazard.

## As built

_Filled at merge._
