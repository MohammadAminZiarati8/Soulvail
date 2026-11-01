# JG-16f — Stages eighteen and nineteen are scripted

**Size:** S · **Depends on:** JG-16e · **Branch:** `dev` (direct)
**Design refs:** GD §8.2, GD §12.1

## Goal

Jungle stages 18 and 19 are hand-written mixes with the Shaman, so 16–19 climb 121 → 144 → 178 →
208 into stage 20 rather than jumping to the budget.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | the rows below |
| *small edits* | | `Data/Modes/Jungle.asset` (two `_scriptedStages` entries) |

## Behaviour

Threat: Frog 4, Pitcher 7, Puffball 8, Panther 9, Mossguard 14, Shaman 16. No code change.

1. **Stage 18, "the healer's wall"** (178): wave 1 = 2 Mossguards + 1 Shaman + 2 Pitchers;
   wave 2 = 4 Frogs + 3 Puffballs + 2 Panthers; wave 3 = 2 Mossguards + 1 Shaman + 2 Panthers.
2. **Stage 19, "the gate"** (208): wave 1 = 6 Frogs + 2 Panthers + 2 Pitchers; wave 2 =
   2 Shamans + 3 Mossguards; wave 3 = 2 Mossguards + 1 Shaman + 2 Panthers + 2 Puffballs.
3. **Threat climbs** 16 → 19: 121 < 144 < 178 < 208, summed from the enemy assets.
4. **Stage 20 is untouched**: not scripted, its boss slot as before. Stages 1–17, roster, scaling
   and bosses are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_StageEighteen_IsTheHealersWall` | stage 18 / three waves as rule 1, in order (1) |
| `Jungle_StageNineteen_IsTheGate` | stage 19 / three waves as rule 2, in order (2) |
| `Jungle_StagesSixteenToNineteen_ClimbInThreat` | asset and six enemy assets / 121 < 144 < 178 < 208 (3) |
| `Jungle_StagesFifteenAndTwenty_AreNotScripted` | replaces `…FifteenAndEighteen…` (4) |

## Manual verification (Editor / device)

1. **[Editor]** Test start at 18: a Shaman behind two Mossguards with Pitchers; the open crowd; a
   second Shaman wall with Panthers.
2. **[Editor]** Test start at 19: the crowd; two Shamans behind three Mossguards; the last mix; the door.
3. **[Editor]** The owner rules whether 16–19 climb fairly into stage 20.

## Out of scope

- Stage 20 and its boss (undecided), the budget curve past 19, Elites, other places.

## As built

_Filled at merge._
