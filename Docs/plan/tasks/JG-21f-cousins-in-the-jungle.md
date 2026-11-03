# JG-21f — The cousins replace the easy four from stage 21

**Size:** M · **Depends on:** JG-21a–e · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02), GD §12.1

## Goal

Four cousin assets on their parents' bodies; the Frog, Pitcher, Puffball and Panther leave the
Jungle's waves at stage 21; stage 21 is scripted to meet one cousin per wave.

## Why this shape

Each steps up from its parent in cost, HP and XP; tints differ so no cousin reads as its parent.

| Asset | Kind, body | HP | Move | Prio | Cost | XP | Contact | Block |
|---|---|---|---|---|---|---|---|---|
| `enemy.bullfrog` | Chaser, `Frog.prefab` ×1.3, tint (0.55, 0.60, 0.40) | 52 | 1.4 | 2 | 6 | 22 | 10 | hop 0.5/0.8; JG-21a leap |
| `enemy.thorn_pitcher` | Spitter, `Pitcher.prefab` ×1.15, (0.70, 0.80, 0.50) | 38 | 2.8 | 4 | 10 | 25 | 12 | standoff 14, speed 7, radius 1.4, count 3, spread 3, rings |
| `enemy.chain_bomber` | Bloater, `Puffball.prefab` ×1.15, (0.85, 0.90, 0.55) | 30 | 2.4 | 2 | 10 | 28 | 15 | reach 2, explosion 2.5, chain 4 |
| `enemy.shadow_panther` | Lunger, `Panther.prefab` ×1.1, (0.35, 0.35, 0.40) | 60 | 2.6 | 5 | 12 | 31 | 16 | Panther's lunge; JG-21b pack |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Bullfrog.asset`, `ThornPitcher.asset`, `ChainBomber.asset`, `ShadowPanther.asset` | — | **New.** The table |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | Rows for rules 3–7 |
| `Tests/Core/Content/ModeSpecTests.cs` | Tests.Core | Rows for rules 1–2 |
| *small edits* | | `ModeSpec` (`RosterEntry.RetiredAtStage`, `RosterFor`, the shared-stage rule); `ModeDefinition` (`_retiredAtStage`); `EnemyDefinition` (leap, pack, count/spread, chain fields; a block is built only when its first field > 0); `Data/Modes/Jungle.asset`; `BootScope.prefab`; `English.asset`, `Pseudo.asset` |
| *ripple* | | `EnemyDefinitionTests` (one row per new block) |

## Behaviour

1. **Retirement**: `RosterEntry(specId, introducedAtStage, retiredAtStage = 0)`; 0 = never, else it
   must exceed the introduction. `RosterFor(stage)` lists an entry only while
   `introduced ≤ stage < retired`. Scripted stages may still name a retired archetype.
2. **Shared introductions**: two roster entries may share a stage **only if that stage is
   scripted**; otherwise the GD §8.2 refusal stands.
3. **The four assets** have the table's numbers and bodies; each new block builds its spec.
4. **Jungle roster**: the four cousins introduced at 21; Frog, Pitcher, Puffball, Panther retired at
   21. Mossguard, Shaman unchanged. Descent unchanged.
5. **Stage 21, "the cousins"** (194): w1 = 6 Bullfrogs; w2 = 3 Thorn pitchers + 3 Bullfrogs;
   w3 = 4 Chain bombers + 2 Bullfrogs; w4 = 2 Shadow panthers + 1 Mossguard + 2 Thorn pitchers.
   One new archetype per wave, in that order.
6. **Below 19's 208** on purpose: the breather after a boss, as 16 was after 15.
7. **It boots**: `BootScope` lists all four, both tables name them, a Jungle run resolves them.

## Tests

| Test | Given / When / Then |
|---|---|
| `Roster_RetiredEntryLeavesFromItsStage` | retired 21 / listed at 20, not at 21 (1) |
| `Roster_RefusesRetireAtOrBeforeIntro` | 21/21 / throws (1) |
| `Roster_SharedStageNeedsAScript` | two at 21, scripted; unscripted / ok; throws (2) |
| `Jungle_Cousins_HaveTheirNumbers` | four assets / table (3) |
| `Jungle_RosterAt21_IsCousinsAndKeepers` | `RosterFor(26)` / four cousins, Mossguard, Shaman (4) |
| `Jungle_StageTwentyOne_IsTheCousins` | four waves as rule 5, 194 (5, 6) |
| `Jungle_Cousins_Boot` | `BootScope`, English, Pseudo (7) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at 21: each wave brings one cousin; each reads as its parent's harder kin.
2. **[Editor]** The owner rules the tints and whether 21 is a fair step after Thornheart.

## Out of scope

- Elites (still unbuilt), boss adds, stages 22–24.

## As built

_Filled at merge._
