# JG-16a — Stage sixteen is a breather

**Size:** S · **Depends on:** JG-15g · **Branch:** `dev` (direct)
**Design refs:** GD §8.2, GD §12.1

## Goal

Jungle stage 16 is a hand-written mix of known enemies, a step down from stage 14's 128 after the
Warlord, so the player spends their new levels before the shaman arrives at 17.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | the rows below |
| *small edits* | | `Data/Modes/Jungle.asset` (one `_scriptedStages` entry) |

## Behaviour

Threat: Frog 4, Pitcher 7, Puffball 8, Panther 9, Mossguard 14. No code change.

1. **Stage 16, "regroup"** (121): wave 1 = 6 Frogs + 2 Pitchers; wave 2 = 2 Mossguards +
   2 Panthers; wave 3 = 1 Mossguard + 2 Puffballs + 1 Pitcher.
2. **It is a breather**: stage 16's threat, summed from the enemy assets, is below stage 14's 128.
3. **Stage 15 stays the Warlord's and stage 17 the budget's**: neither is scripted. Stages 1–14,
   roster, scaling and bosses are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageSixteen_Regroups` | stage 16 / three waves as rule 1, in order (rule 1) |
| `Jungle_StageSixteen_IsABreather` | asset and enemy assets / count × cost / 121 < 128 (rule 2) |
| `Jungle_StagesFifteenAndSeventeen_AreNotScripted` | replaces `…FifteenAndSixteen…` / `TryGetScriptedStage(15)`, `(17)` false (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Test start at 16: Frogs and Pitchers; two Mossguards with Panthers; a last
   Mossguard with spores and a Pitcher; the door after wave 3. The owner judges it as a breather.

## Out of scope

- Stages 17–19 (JG-16e, JG-16f). The budget curve past 19.

## As built

_Filled at merge._
