# JG-23 — Stage twenty-three mixes what the player knows

**Size:** S · **Depends on:** JG-22d · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 ("by stage 21 the player knows the full vocabulary"), GD §12.1

## Goal

Jungle stage 23 is a hand-written mix with nothing new: the cousins, the stalker and a Shaman wall,
so 21–24 climb 194 → 218 → 242 → 269 into Gravemaw.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | The rows below |
| *small edits* | | `Data/Modes/Jungle.asset` (one `_scriptedStages` entry) |

## Behaviour

Threat: Bullfrog 6, Thorn pitcher 10, Chain bomber 10, Shadow panther 12, Mossguard 14, Shaman 16,
Stalker 18. No code change.

1. **Stage 23, "the tangle"** (242): w1 = 4 Chain bombers + 2 Shadow panthers + 4 Bullfrogs;
   w2 = 2 Mossguards + 1 Shaman + 3 Thorn pitchers; w3 = 2 Stalkers + 2 Shadow panthers +
   2 Chain bombers.
2. **Nothing new**: every archetype it names is rostered before stage 23.
3. **Threat climbs**: 194 < 218 < 242, summed from the assets.
4. **Untouched**: stages 1–22, roster, scaling and bosses; stage 25 stays a boss stage.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageTwentyThree_IsTheTangle` | stage 23 / three waves as rule 1, in order (1) |
| `Jungle_StageTwentyThree_IntroducesNothing` | each spec's introduction / < 23 (2) |
| `Jungle_StagesTwentyOneToTwentyThree_Climb` | 194 < 218 < 242 (3) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at 23: bombers chain into panthers; a healer's wall with a fan behind it;
   stalkers among panthers. The door.
2. **[Editor]** The owner rules whether 23 is a fair step from 22.

## Out of scope

- Stage 24 (JG-24d), Elites, the budget curve past 24.

## As built

_Filled at merge._
