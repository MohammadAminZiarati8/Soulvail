# JG-22d — The Chameleon stalker in Jungle stage 22

**Size:** S · **Depends on:** JG-21f, JG-22c · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (Revenant), GD §8.2, GD §12.1

## Goal

`enemy.stalker` exists with its numbers and body, the Jungle rosters it from stage 22, and stage 22
meets it alone in a wave before mixing it in.

## Why this shape

GD §8.1's Revenant numbers: HP 70, threat 18, priority 6. Move 2.6 m/s (it must catch a player who
stops), contact 18 (the strike), reach 1.2, windup 0.45 s, recover 1.0 s. XP 36, the ladder's next
step. The stalker block: JG-22a's authored values.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Stalker.asset` | — | **New.** `enemy.stalker`, the numbers above, body `Stalker.prefab` |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 2–6 |
| *small edits* | | `EnemyDefinition` (stalker block, nine fields; built when `behindDistance` > 0); `Data/Modes/Jungle.asset` (roster at 22, scripted stage 22); `BootScope.prefab`; `English.asset`, `Pseudo.asset` (`enemy.stalker.name` = "Chameleon Stalker") |
| *ripple* | | `EnemyDefinitionTests` (the block's row) |

## Behaviour

Threat: Bullfrog 6, Thorn pitcher 10, Chain bomber 10, Shadow panther 12, Mossguard 14, Shaman 16,
Stalker 18.

1. **The stalker block builds a `StalkerSpec` when `behindDistance` > 0**, and none otherwise.
2. **`enemy.stalker` is a Stalker with the numbers above** and wears `Stalker.prefab`.
3. **The Jungle rosters it from stage 22.** Descent does not.
4. **Stage 22, "the stalker"** (218): w1 = 1 Stalker + 6 Bullfrogs; w2 = 2 Shadow panthers +
   2 Thorn pitchers + 4 Chain bombers; w3 = 2 Stalkers + 2 Mossguards + 1 Shaman.
5. **Threat climbs**: 194 (21) < 218 (22), summed from the assets.
6. **It boots**: `BootScope` lists it, both tables name it, a Jungle run resolves it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_StalkerBlock_BuildsOnlyWithADistance` | 3; 0 / spec with the nine; none (1) |
| `Jungle_Stalker_IsAStalkerWithItsNumbers` | Stalker, 70, 18, 6, 2.6, 36, block, body (2) |
| `Jungle_RostersStalkerFrom22` | `RosterFor(21)`, `(22)` / absent, present (3) |
| `Jungle_StageTwentyTwo_IsTheStalker` | three waves as rule 4, in order (4) |
| `Jungle_StagesTwentyOneToTwentyTwo_Climb` | 194 < 218 (5) |
| `Jungle_Stalker_Boots` | `BootScope`, English, Pseudo (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at 22: wave 1's lone stalker is the only new thing; stopping to shoot Bullfrogs
   gets punished from behind.
2. **[Editor]** The owner rules whether 2 Stalkers behind a Mossguard wall in wave 3 is fair.

## Out of scope

- Stages 23–24; Elites.

## As built

_Filled at merge._
