# JG-11 — Stages seven to nine are scripted

**Size:** S · **Depends on:** — · **Branch:** `jg-11-stages-seven-to-nine`
**Design refs:** GD §8.2, GD §12.1

## Goal

Jungle stages 7, 8 and 9 are hand-written mixes of the four known enemies, so the climb from stage
6's 18 threat is 37 → 56 → 77 rather than straight to the budget's 144.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | the rows below; `Jungle_StageSix_IsTwoPanthers` drops its stage-7 line |
| *small edits* | | `Data/Modes/Jungle.asset` (three `_scriptedStages` entries) |

## Behaviour

Threat: Frog 4, Pitcher 7, Puffball 8, Panther 9. No new enemy, no code change.

1. **Stage 7, "lunge while chased"** (8 enemies, 37): wave 1 = 4 Frogs; wave 2 = 1 Panther + 3 Frogs.
2. **Stage 8, "crossfire"** (10 enemies, 56): wave 1 = 2 Pitchers + 3 Frogs; wave 2 = 2 Panthers + 3 Frogs.
3. **Stage 9, "the exam"** (12 enemies, 77): wave 1 = 3 Frogs + 2 Puffballs; wave 2 = 2 Pitchers +
   1 Panther; wave 3 = 2 Panthers + 2 Frogs.
4. **Threat climbs** stage 6 → 9: each stage's summed threat exceeds the one before.
5. **Stage 10 stays Old Croaker's and stage 11 the budget's**: neither is scripted. Stages 1–6,
   roster, scaling and bosses are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageSeven_IsOnePantherAmongFrogs` | the asset / stage 7 / `(1, frog, 4)`, `(2, panther, 1)`, `(2, frog, 3)` (rule 1) |
| `Jungle_StageEight_IsPitchersThenPanthers` | the asset / stage 8 / `(1, pitcher, 2)`, `(1, frog, 3)`, `(2, panther, 2)`, `(2, frog, 3)` (rule 2) |
| `Jungle_StageNine_UsesAllFour` | the asset / stage 9 / three waves as rule 3, in that order (rule 3) |
| `Jungle_StagesSixToNine_ClimbInThreat` | the asset and the four enemy assets / sum count × threat cost per stage / 18 < 37 < 56 < 77 (rule 4) |
| `Jungle_StagesTenAndEleven_AreNotScripted` | the asset / `TryGetScriptedStage(10)`, `(11)` / both false (rule 5) |

Existing rows (`Jungle_ScriptsItsFirstFourStages`, `Jungle_StageSix_IsTwoPanthers`,
`Jungle_IsDescentsNumbers`, `Jungle_RostersItsOwnEnemies`) stay green and cover the rest of rule 5.

## Manual verification (Editor / device)

1. **[Editor]** Test start at 7: four Frogs, then a Panther lunges while three Frogs chase.
2. **[Editor]** Test start at 8: Pitchers lob while Frogs crowd; then two Panthers.
3. **[Editor]** Test start at 9: Frogs with Puffballs, then Pitchers + Panther, then two Panthers
   and Frogs; the door opens after wave 3.
4. **[Editor]** Owner rules whether 7–9 read as a step up from 6 and a fair lead-in to Old Croaker.

## Out of scope

- Stage 11 and on (budget 250): the owner decides after playing this.
- Budget curve, wave timing, Elites, a new Jungle enemy.
- Descent's stages.

## As built

as specified. Added only the stage 7–9 scripted entries and five behaviour tests; removed the old
stage-7 budget assertion. Threat costs come from enemy assets: totals 18, 37, 56, 77.

Verified: 4 581 EditMode pass, 0 fail, 1 existing inconclusive (animator clock); zero compiler errors
and new analyzer warnings; dotnet format clean across 549 files. Manual Editor checks above remain
owed to the owner. No runtime code changes or deviations.
