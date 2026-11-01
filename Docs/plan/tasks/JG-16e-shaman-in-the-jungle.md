# JG-16e — The Moss Shaman in the Jungle

**Size:** M · **Depends on:** JG-16a, JG-16b, JG-16d · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (Choir), GD §8.2

## Goal

`enemy.shaman`, a Shaman, rostered in the Jungle from stage 17, and stage 17 scripted to meet it.

## Why this shape

- **GD §8.1's Choir numbers**: HP 60, threat 16, priority 8 (the targeter's highest). Move 2.0 m/s,
  contact damage 0 (if `EnemySpec` refuses 0, the minimum, never applied — JG-16b rule 6). XP 32,
  the next step up the ladder (Mossguard 30). The shaman block: JG-16b's authored values.
- **Met beside Mossguards**, because a Shaman alone has nothing to do.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Shaman.asset` | — | **New.** `enemy.shaman`, the numbers above, body `Shaman.prefab` |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 2–5 |
| *small edits* | | `Game/Authoring/EnemyDefinition.cs` (shaman block, eleven fields; builds a `ShamanSpec` when the aura > 0); `Data/Modes/Jungle.asset` (roster at 17, scripted stage 17); `Prefabs/Composition/BootScope.prefab`; `Data/Localisation/English.asset`, `Pseudo.asset` (`enemy.shaman.name` = "Moss Shaman") |
| *ripple* | | `Tests/Game/Authoring/EnemyDefinitionTests.cs` (the block's row) |

## Behaviour

1. **The shaman block builds a `ShamanSpec` when its aura is above zero**, and none otherwise.
2. **`enemy.shaman` is a Shaman with the numbers above** and wears `Shaman.prefab`.
3. **The Jungle rosters it from stage 17.** Descent does not.
4. **Stage 17, "the healer"** (144): wave 1 = 1 Shaman + 2 Mossguards; wave 2 = 1 Shaman +
   6 Frogs + 2 Pitchers; wave 3 = 2 Mossguards + 2 Panthers. Stage 16 is unchanged.
5. **It boots**: `BootScope` lists it, both tables name it, a Jungle run resolves it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Definition_ShamanBlock_BuildsASpecOnlyWithAnAura` | 8; 0 / spec with the eleven; none (1) |
| `Jungle_Shaman_IsAShamanWithItsNumbers` | Shaman, 60, 16, 8, 2.0, 32, block, body (2) |
| `Jungle_RostersTheShamanFromStageSeventeen` | at 17; not in Descent (3) |
| `Jungle_StageSeventeen_MeetsTheHealer` | three waves as rule 4, in order; 144 (4) |
| `Jungle_StagesFifteenAndEighteen_AreNotScripted` | replaces `…FifteenAndSeventeen…` (4) |
| `Jungle_Shaman_Boots` | listed, named in both tables (5) |

## Manual verification (Editor / device)

1. **[Editor]** Test start at 17. Two Mossguards walk in; the Shaman hangs back at the edge of range.
   The reticle jumps to it. Its ring heals the guards and they run.
2. **[Editor]** Step in to reach it: it breaks off and runs. Kill it and the guards stop healing.
3. **[Editor]** Wave 2: a Shaman among Frogs and Pitchers. The owner judges the feel and the name.

## Out of scope

- Stages 18–19 (JG-16f). Retuning: data, on the owner's word after playing.

## As built

_Filled at merge._
