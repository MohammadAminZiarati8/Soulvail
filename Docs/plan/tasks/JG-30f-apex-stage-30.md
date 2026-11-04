# JG-30f — Stage 30 is the Apex's: boss data, stones, roster row; the Jungle is done

**Size:** M · **Depends on:** JG-30e, JG-26d · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 3–6; GD §9.2; `GravemawStageTests` (the shape to copy)

## Goal

Stage 30 fights the Apex instead of the Croaker, in an arena with stones that stop her lanes, and
the Jungle's thirty stages are complete.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Apex.asset` | — | **New.** `enemy.apex`: body, stats, look |
| `Data/Enemies/ApexBoss.asset` | — | **New.** `boss.apex`: phases, summons, beat |
| `Prefabs/Arenas/Jungle/Cover_ApexStones.prefab` | — | **New.** Four stones from the Jungle kit |
| `Data/Modes/Jungle.asset` | — | Stage 30's boss and cover → the Apex's |
| `Tests/Game/Authoring/ApexStageTests.cs` | Tests.Game | **New** |
| *small edits* | | `BootScope.prefab` registers both assets; `English`/`Pseudo` gain `enemy.apex.name` |

## Behaviour

1. **Body:** `enemy.apex`, `EnemyBehaviourKind.Boss`, `Apex.prefab`, `BodyScale` 1.6, XP 700
   (Gravemaw pays 600), contact damage 20 (Gravemaw's), HP 720 to start (Gravemaw's 632).
2. **Fight length:** HP is set so the session probe (a Ranger with stage-30 expected DPS, dodging
   nothing) kills her in 75–120 s. Record the number in *As built*.
3. **Phases:** `boss.apex` enters at 1, 0.66, 0.33. Phase 1 summons 2 `enemy.shadow_panther`. Phases
   0 and 2 summon nothing. Beat at the other bosses' length.
4. **Stones:** `Cover_ApexStones`, four stones, not one inside 6 m of the player's start or the
   boss's spawn, each blocking a `LaneProbeIntent` (the `Cover` layer).
5. **Stage 30:** the Croaker leaves stage 30; the Apex is its boss. No other stage changes; the
   Croaker stays at its earlier stages.
6. **Final:** `_finalStage` stays 30. Beating the Apex ends the Jungle run as a win, the same path
   the Croaker's death took.
7. **Readable:** the stones read as stones at phone zoom; each gap between them is at least 3 m.

## Tests

| Test | Given / When / Then |
|---|---|
| `Apex_AssetValid_BossKind_Prefab_Scale` (1) |
| `Apex_ProbeFight_SeventyFiveToOneTwenty` — session (2) |
| `ApexBoss_PhasesAt66And33_TwoShadowPanthersAt66Only` (3) |
| `ApexStones_FourClearOfStarts_BlockLaneProbe` / `ApexStones_GapsAtLeastThree` (4, 7) |
| `Stage30_BossIsApex_CroakerElsewhereUnchanged` (5) |
| `Stage30_ApexDeath_EndsRunAsWin` — session (6) |
| `Apex_NameLocalised` (1) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 to stage 30, full fight: panthers at 66 %, alone and chaining at 33 %,
   panthers gone, win screen. Time it.
2. **[device]** Play stage 30 on a phone; note anything unreadable.

## Out of scope

- Balance past this fight (PROGRESS "does not work" 1); Elites; audio.
