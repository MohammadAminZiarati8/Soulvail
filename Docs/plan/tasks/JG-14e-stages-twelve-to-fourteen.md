# JG-14e — Stages twelve to fourteen are scripted

**Size:** S · **Depends on:** JG-14d · **Branch:** `dev` (direct)
**Design refs:** GD §8.2, GD §12.1

## Goal

Jungle stages 12, 13 and 14 are hand-written mixes with the Mossguard, as JG-11 wrote 7–9, so the
climb from stage 11's 44 threat is 76 → 110 → 128 rather than straight to the budget's ~250.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | the rows below |
| *small edits* | | `Data/Modes/Jungle.asset` (three `_scriptedStages` entries) |

## Behaviour

Threat: Frog 4, Pitcher 7, Puffball 8, Panther 9, Mossguard 14 (owner, 2026-10-31). No code change.

1. **Stage 12, "shield and spit"** (76): wave 1 = 1 Mossguard + 2 Pitchers + 3 Frogs; wave 2 =
   1 Mossguard + 2 Panthers + 1 Frog.
2. **Stage 13, "pincer"** (110): wave 1 = 2 Mossguards + 2 Puffballs; wave 2 = 2 Pitchers +
   2 Panthers + 3 Frogs; wave 3 = 1 Mossguard + 1 Puffball.
3. **Stage 14, "the exam"** (128): wave 1 = 4 Frogs + 2 Puffballs; wave 2 = 2 Mossguards +
   2 Pitchers; wave 3 = 2 Mossguards + 2 Panthers + 2 Frogs.
4. **Threat climbs** stage 11 → 14: 44 < 76 < 110 < 128, summed from the enemy assets.
5. **Stage 15 stays Old Croaker's and stage 16 the budget's**: neither is scripted. Stages 1–11,
   roster, scaling and bosses are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageTwelve_IsShieldAndSpit` | stage 12 / two waves as rule 1, in order (rule 1) |
| `Jungle_StageThirteen_IsAPincer` | stage 13 / three waves as rule 2, in order (rule 2) |
| `Jungle_StageFourteen_UsesAllFive` | stage 14 / three waves as rule 3, in order (rule 3) |
| `Jungle_StagesElevenToFourteen_ClimbInThreat` | asset and five enemy assets / count × cost per stage / 44 < 76 < 110 < 128 (rule 4) |
| `Jungle_StagesFifteenAndSixteen_AreNotScripted` | `TryGetScriptedStage(15)`, `(16)` / both false (rule 5) |

Existing rows (stages 1–11, roster, bosses) stay green and cover the rest of rule 5.

## Manual verification (Editor / device)

1. **[Editor]** Test start at 12: a Mossguard screens two Pitchers; then one with two Panthers.
2. **[Editor]** Test start at 13: two Mossguards with Puffballs; the open crowd; a last Mossguard and spores.
3. **[Editor]** Test start at 14: three waves, the door after wave 3.
4. **[Editor]** The owner rules whether 11–14 climb fairly into stage 15's Old Croaker.

## Out of scope

- Stage 15 (Old Croaker, decided later), the budget curve past 14, Elites, other places.

## As built

Built 2026-10-31. Only the two Files-table assets plus this footer, PROGRESS and ROADMAP changed.
Three `_scriptedStages` entries follow the specified wave order and counts: stage 12 shield/spit,
13 pincer, 14 exam. Five new JungleTests rows map one-to-one to rules 1–5; threat is summed from
all five shipped enemy assets, pinning 44 < 76 < 110 < 128. Stages 15/16 remain unscripted;
existing rows cover stages 1–11, roster, scaling and bosses. No runtime code change.

Verified: full EditMode 4 779 total / 4 778 passed / 0 failed / 1 known animator-clock inconclusive;
JungleTests 45/45 and JG-14e 5/5. Compiler errors 0, new analyzer warnings 0; dotnet format clean
(1 file checked). Focused run adds 0 errors/warnings; full-suite failure-path logs are expected.

Manual Editor/device checks remain: Test start 12 shield/spit, 13 pincer, 14 three waves and door
after wave 3; owner judges the 11–14 climb into Old Croaker at 15. No tuning beyond the spec.
