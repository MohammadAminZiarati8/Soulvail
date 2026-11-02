# JG-20h — Stage 20 is Thornheart's: data, pod, roster, the open grove

**Size:** M · **Depends on:** JG-20e, JG-20g · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 5, 6, §9.2; JG-15g's shape; M4-01b (a boss stage has no director waves)

## Goal

The Jungle's stage 20 spawns Thornheart in the centre of an open arena; stages 5, 25, 30 keep the
Croaker, 10 the Mother, 15 the Warlord.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Thornheart.asset` | — | **New.** `enemy.thornheart` |
| `Data/Enemies/ThornheartBoss.asset` | — | **New.** `boss.thornheart` |
| `Data/Enemies/ThornPod.asset` | — | **New.** `enemy.thorn_pod` |
| `Tests/Game/Authoring/ThornheartStageTests.cs` | Tests.Game | **New** |
| *small edits* | | `Jungle.asset` boss roster row; `BootScope.prefab` catalog lists and looks (`Thornheart.prefab`, `ThornPod.prefab`); English/Pseudo name rows; `JungleTests` |

## Behaviour

1. **Boss:** `Boss` kind; `MoveSpeed` 0; `ContactDamage` 20; `XpValue` 600; `ThreatCost` 40;
   `BodyScale` 3; body `Thornheart.prefab`; no cover (the thorns are the arena's hazard, rule 6).
   Name "Thornheart".
2. **Boss spec:** phases 1 / 0.66 / 0.33, `BeatSeconds` 1.5, `SpawnsCentral` on, no phase summons
   (pods are its adds).
3. **Pod:** `Static` kind; `TargetPriority` 7 (above every Jungle enemy but the Shaman); `MaxHp`
   so three base Ranger arrows at stage 20 kill it; `XpValue` 0; body `ThornPod.prefab`. Name
   "Thorn Pod".
4. **Roster:** row `boss.thornheart`, `_atStage` 20, ahead of the Croaker cycle.
5. **The script:** a boss stage, out of `_scriptedStages`: no director waves, the boss stands up on
   the first tick. **Door:** the standard `IsStageComplete` — boss down, nothing breathing.
6. **Fight length:** `Jungle_Thornheart_FightIsInTheBand` — Mother's formula at stage 20 with
   33 DPS, always targetable, ×2 for 3 s per ~12 s cycle (×1.25 uptime), plus two beats;
   `MaxHp` set to land ~95 s inside 75–120. The number goes in *As built*.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_Thornheart_HasItsNumbers` (1) / `Jungle_ThornheartBoss_HasThreePhases` (2) |
| `Jungle_ThornPod_IsStaticPrioritySeven_DiesToThreeArrows` (3) |
| `Jungle_FifthStages_HaveTheirBosses` — 20 is Thornheart (4) |
| `Jungle_StageTwenty_IsNotScripted` / `StageTwenty_DoorOpens_WhenBossAndAddsAreDown` — session (5) |
| `Jungle_Thornheart_FightIsInTheBand` / `Jungle_StageTwenty_StartsWithThornheart` — DT-01 start (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 stage 20: spokes rise where warned and stop you, never a Rootling; stand on a
   strip and get bitten, roll through one and don't.
2. **[Editor]** The slam lights the heart and damage numbers double; an unshot pod grows three
   Rootlings, a shot one nothing.
3. **[Editor]** At 66 % the ring closes; the lane inside it stays clear of spokes. At 33 % it all
   speeds up. Fight lands in 75–120 s.

## Out of scope

- Balance beyond rule 6; a second arena; Gravemaw (stage 25, parked).

## As built

_Filled at merge._
