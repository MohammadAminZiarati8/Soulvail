# JG-14e — Stages twelve to fourteen are scripted

**Size:** S · **Depends on:** JG-14d · **Branch:** `dev` (direct)
**Design refs:** GD §8.2, GD §12.1

## Goal

Jungle stages 12, 13 and 14 are hand-written mixes with the Shellback, as JG-11 wrote 7–9, so the
climb from stage 11's 44 threat is 76 → 110 → 128 rather than straight to the budget's ~250.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | the rows below |
| *small edits* | | `Data/Modes/Jungle.asset` (three `_scriptedStages` entries) |

## Behaviour

Threat: Frog 4, Pitcher 7, Puffball 8, Panther 9, Shellback 14 (owner, 2026-10-31). No code change.

1. **Stage 12, "shell and spit"** (76): wave 1 = 1 Shellback + 2 Pitchers + 3 Frogs; wave 2 =
   1 Shellback + 2 Panthers + 1 Frog.
2. **Stage 13, "pincer"** (110): wave 1 = 2 Shellbacks + 2 Puffballs; wave 2 = 2 Pitchers +
   2 Panthers + 3 Frogs; wave 3 = 1 Shellback + 1 Puffball.
3. **Stage 14, "the exam"** (128): wave 1 = 4 Frogs + 2 Puffballs; wave 2 = 2 Shellbacks +
   2 Pitchers; wave 3 = 2 Shellbacks + 2 Panthers + 2 Frogs.
4. **Threat climbs** stage 11 → 14: 44 < 76 < 110 < 128, summed from the enemy assets.
5. **Stage 15 stays Old Croaker's and stage 16 the budget's**: neither is scripted. Stages 1–11,
   roster, scaling and bosses are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageTwelve_IsShellAndSpit` | stage 12 / two waves as rule 1, in order (rule 1) |
| `Jungle_StageThirteen_IsAPincer` | stage 13 / three waves as rule 2, in order (rule 2) |
| `Jungle_StageFourteen_UsesAllFive` | stage 14 / three waves as rule 3, in order (rule 3) |
| `Jungle_StagesElevenToFourteen_ClimbInThreat` | asset and five enemy assets / count × cost per stage / 44 < 76 < 110 < 128 (rule 4) |
| `Jungle_StagesFifteenAndSixteen_AreNotScripted` | `TryGetScriptedStage(15)`, `(16)` / both false (rule 5) |

Existing rows (stages 1–11, roster, bosses) stay green and cover the rest of rule 5.

## Manual verification (Editor / device)

1. **[Editor]** Test start at 12: a Shellback screens two Pitchers; then one with two Panthers.
2. **[Editor]** Test start at 13: two Shellbacks with Puffballs; the open crowd; a last shell and spores.
3. **[Editor]** Test start at 14: three waves, the door after wave 3.
4. **[Editor]** The owner rules whether 11–14 climb fairly into stage 15's Old Croaker.

## Out of scope

- Stage 15 (Old Croaker, decided later), the budget curve past 14, Elites, other places.

## As built

_Filled at merge._
