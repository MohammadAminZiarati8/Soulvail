# JG-24d — The Thornweaver in Jungle stage 24

**Size:** S · **Depends on:** JG-23, JG-24c · **Branch:** `dev` (direct)
**Design refs:** GD §8.2, GD §12.1

## Goal

`enemy.thornweaver` exists with its numbers and body, the Jungle rosters it from stage 24, and
stage 24 meets it alone before two weavers pen the player in front of a wall of Mossguards.

## Why this shape

A priority target like the Shaman, but it hurts: HP 70, threat 15, priority 7 (below the Shaman's
8: healing is the bigger threat). Move 2.0 m/s, contact 10 (the wall's bite; scaled by depth),
reach 1.0, windup 0.8 s, recover 1.0 s. XP 34. The weaver block: JG-24a's authored values.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Thornweaver.asset` | — | **New.** `enemy.thornweaver`, the numbers above, body `Thornweaver.prefab` |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 2–6 |
| *small edits* | | `EnemyDefinition` (weaver block, ten fields; built when `wallLength` > 0); `Data/Modes/Jungle.asset` (roster at 24, scripted stage 24); `BootScope.prefab`; `English.asset`, `Pseudo.asset` (`enemy.thornweaver.name` = "Thornweaver") |
| *ripple* | | `EnemyDefinitionTests` (the block's row) |

## Behaviour

Threat: Bullfrog 6, Thorn pitcher 10, Chain bomber 10, Shadow panther 12, Mossguard 14,
Thornweaver 15, Shaman 16, Stalker 18.

1. **The weaver block builds a `ThornweaverSpec` when `wallLength` > 0**, and none otherwise.
2. **`enemy.thornweaver` is a Thornweaver with the numbers above** and wears its prefab.
3. **The Jungle rosters it from stage 24.** Descent does not.
4. **Stage 24, "the weaver"** (269): w1 = 1 Thornweaver + 8 Bullfrogs; w2 = 2 Stalkers +
   4 Chain bombers + 2 Thorn pitchers + 2 Bullfrogs; w3 = 2 Thornweavers + 2 Mossguards +
   2 Shadow panthers + 1 Shaman.
5. **Threat climbs** 21 → 24: 194 < 218 < 242 < 269. Stage 25 stays Gravemaw's boss stage.
6. **It boots**: `BootScope` lists it, both tables name it, a Jungle run resolves it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_WeaverBlock_BuildsOnlyWithAWall` | 5; 0 / spec with the ten; none (1) |
| `Jungle_Thornweaver_HasItsNumbers` | Thornweaver, 70, 15, 7, 2.0, 34, block, body (2) |
| `Jungle_RostersThornweaverFrom24` | `RosterFor(23)`, `(24)` / absent, present (3) |
| `Jungle_StageTwentyFour_IsTheWeaver` | three waves as rule 4, in order (4) |
| `Jungle_StagesTwentyOneToTwentyFour_Climb` | 194 < 218 < 242 < 269 (5) |
| `Jungle_Thornweaver_Boots` | `BootScope`, English, Pseudo (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at 24: the lone weaver walls off the retreat while Bullfrogs leap in.
2. **[Editor]** Wave 3: two weavers pen the Ranger toward the Mossguards; killing a weaver drops its
   walls. **The owner rules whether 21–24 climb fairly into Gravemaw.**

## Out of scope

- Gravemaw (JG-25), stages 26–29, Elites.

## As built

_Filled at merge._
