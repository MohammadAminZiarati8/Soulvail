# JG-12a — A boss can own a stage and the arena's middle

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9, GD §9.2

## Goal

A mode's boss roster can name one stage (`AtStage`) as well as a cycle (`EveryNStages`), and a boss
authored `SpawnsCentral` stands on the spawn point nearest the arena's middle.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Content/ModeSpecTests.cs` | Tests.Core | Rules 1–4 |
| `Tests/Core/Director/SpawnDirectorTests.cs` | Tests.Core | Rule 5 |
| *small edits* | | `Core/Content/ModeSpec.cs` (`BossRosterEntry.AtStage`, `TryGetBossFor`, `CopyBossRoster`); `Core/Content/BossSpec.cs` (`SpawnsCentral`); `Core/Director/SpawnDirector.cs` (`TickBoss`'s point); `Game/Authoring/ModeDefinition.cs` (`BossRosterRow._atStage`; `_everyNStages` `Min(0)`); `Game/Authoring/BossDefinition.cs` (`_spawnsCentral`) |

## Public API

```csharp
public readonly struct BossRosterEntry {
    public BossRosterEntry(ContentId bossId, int everyNStages, int atStage = 0);
    public int AtStage { get; }        // 0 = a cycle row
}
public sealed class BossSpec {
    // `bool spawnsCentral = false` appended to the constructor
    public bool SpawnsCentral { get; }
}
```

## Behaviour

1. **A row is a cycle or a stage, never both.** Exactly one of `everyNStages` ≥ 1 and `atStage` ≥ 1
   holds; anything else throws where the entry is built (and in `CopyBossRoster`, for `default`).
2. **A stage row wins.** `TryGetBossFor(n)` answers a row with `AtStage == n` first, whatever the row
   order; otherwise the first cycle row matching, as today.
3. **The roster refuses** two rows on one stage, a stage the mode does not have, and a scripted stage
   on a stage row's stage (the existing `TryGetBossFor` check). Two cycle rows on one interval stay
   refused; a stage row is not an interval.
4. **Existing rosters are unchanged.** A row with no `_atStage` reads 0; Descent and the Jungle answer
   every stage 1–40 as before.
5. **A central boss stands in the middle.** A `SpawnsCentral` boss spawns on the spawn point nearest
   the mean of all spawn points, once the player is at least the spawn-safety distance from it
   (`TryPickPoint`'s rule); any other boss picks its point as today.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `BossRow_CycleOrStage_NeverBoth` | (5, 0), (0, 10) / built / fine; (0, 0), (5, 10), (0, −1) throw (rule 1) |
| `BossRoster_StageRowWins_WhateverTheOrder` | croaker every 5 listed first, mother at 10 / `TryGetBossFor(5, 10, 15, 20)` / croaker, mother, croaker, croaker (rule 2) |
| `BossRoster_RefusesTwoOnAStage_OrAStageItLacks` | two rows at 10; a row at 99 on a 30-stage mode / built / throw (rule 3) |
| `BossRoster_ScriptOnAStageRow_IsRefused` | stage row 10, stage 10 scripted / built / throws (rule 3) |
| `BossRoster_CycleOnly_AnswersAsBefore` | every 5 alone / stages 1–40 / bosses on the fifths only (rule 4) |
| `Director_CentralBoss_SpawnsNearestTheMiddle` | eight points, one near their mean, player 14 m off / boss stage / spawned on that point (rule 5) |
| `Director_CentralBoss_WaitsForThePlayerToStepOff` | player 2 m from that point / tick / nothing; player steps 7 m away / tick / spawned (rule 5) |

## Manual verification (Editor / device)

None: no asset names a stage row or a central boss until JG-12h.

## Out of scope

- Jungle.asset's roster and Mother Pitcher's boss asset (JG-12h): a roster naming an unbuilt boss is
  refused by `RunSession.Start`.
- An arena-centre fact on the snapshot. The spawn points' mean is core's only view of the room.

## As built

Implemented rules 1–5 and every Tests-table row; no asset changes or manual checks.

Deviations / existing-code resolutions:
- `Core/Ai/EnemySystem.cs`: added internal `Boss(ContentId)` lookup because `SpawnDirector`
  has no catalog access before spawning. No new constructor wiring.
- `Tests/Game/Authoring/ModeDefinitionTests.cs`: `Descent_EveryYamlKeyBindsToAField`
  permits only the newly serialized `_atStage: 0` default and restores the original text
  in `finally`. Its exact-text comparison otherwise fails rule 4 and rewrites Descent.asset.
- Rule 3 retains the existing refusal of duplicate boss ids, alongside stage/interval guards.
- Existing Core `System.Numerics.Vector3` API retained (AR §18.4: XZ geometry).
  Central selection keeps AR §18.3's single Spawn draw, breaking equal-distance ties.

Verification: full EditMode suite through TestRunnerApi; final counts in PROGRESS.
Whitespace check clean; no compiler errors or new analyzer warnings.
