# M6-11c — Stage 1 without the eight M1 dummies

**Size:** S · **Depends on:** M6-11 · **Branch:** `m6-11c-stage-one-without-the-m1-dummies`
**Design refs:** GD §7.1, §12.1 · **Ledger rows:** [M7 row 6](../ROADMAP.md#carry-forward-into-m7)

## Goal

A run's first stage holds what the director composed and nothing else, and GD §7.1's two seconds of
arrival are empty on stage 1 as on every other.

## Why this is a bug and how it was found

Found by [M6-11](M6-11-acceptance-and-tag.md)'s probe, as eight kills in stage 1 it could not match to
a spawn. **`Run.unity`'s `RunScope` still carries M1's chaser dummies**: `_dummySpec` is the Husk and
`_dummyPositions` is a ring of eight at 8–11 m. `RunScope.BuildSpawnPlan` turns them into a
`SpawnPlan`, and `RunSession.Start` calls `enemies.SpawnAll(config.SpawnPlan)` above `_flow.Begin` —
so **every run since M2 has begun with eight Husks standing around the player at t = 0**, before
`StageArrived`, outside the threat budget, and depth-scaled at whatever stage the run starts on.

Stage 1 composes **ten** Husks; the player meets **eighteen**. Every played log in the project carries
it: M5-08's *"18 kills in stage 1"* is these eight plus the composed ten. The mechanism is M2-05's and
is sound — an arena may be dressed — but the content is a grey-box leftover the design never asked for.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Scenes/Run.unity` | — | `_dummyPositions` emptied. **`_dummySpec` stays**: `RunScope.PrewarmCount` returns 0 without it, and the enemy pool would stop prewarming |
| `Tests/Game/Composition/RunSceneTests.cs` | Tests.Game | **New**: the shipped scene dresses no enemy, and still prewarms |

Only these files change. `RunScope`, `SpawnPlan` and `SpawnAll` do not: dressing an arena is a
mechanism fixtures use, and `FrameOrderTests` and `PlayerProjectileTests` dress their own.

## Behaviour

1. **The shipped `Run.unity` dresses no enemy**: `BuildSpawnPlan` answers `SpawnPlan.Empty`.
2. **The pool still prewarms** `DeviceEnemyCap + 1` bodies, because `_dummySpec` is still assigned.
3. **Stage 1 spawns nothing before its first wave.**

## Tests

| Test | Given / When / Then |
|---|---|
| `RunScene_DressesNoEnemy` | `Run.unity` opened additively / read `RunScope`'s serialized `_dummyPositions` / empty |
| `RunScene_StillPrewarmsTheEnemyPool` | the same scope / read `_dummySpec` / assigned |

## Manual verification (Editor)

1. **[Editor]** Descend. The overlay reads `enemies 0/28` through arrival, and wave 1 is the first
   thing in the arena.

## Out of scope

- **Retuning stage 1** now that it is ten Husks rather than eighteen. Early levels come slower; that
  is M8-05's to weigh, and every earlier log's early XP was inflated by these eight.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
