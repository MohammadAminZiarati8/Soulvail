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

**Rules 1–3 as written.** `Run.unity`'s `_dummyPositions` is `[]` and `_dummySpec` is still the Husk;
`RunScope`, `SpawnPlan` and `SpawnAll` did not move. `RunSceneTests` opens the scene additively once
for the fixture and closes it again — unless it was already open, in which case it is read as it
stands and left alone, so a test pass never closes the owner's scene or drops an unsaved edit.

**Three deviations.**

*1. Each row asserts the answer as well as the field.* The spec's *Then* column read the serialized
field alone, and a field is a premise — [M7 row 8](../ROADMAP.md#carry-forward-into-m7)'s whole
lesson. So `RunScene_DressesNoEnemy` also invokes `BuildSpawnPlan` and asserts it **is**
`SpawnPlan.Empty`, and `RunScene_StillPrewarmsTheEnemyPool` invokes `PrewarmCount` and asserts
`DeviceEnemyCap + 1`, the 29 bodies rule 2 is about. Both are private and reached by name; neither has
an overload, so M6-11b's `AmbiguousMatchException` tax does not apply today, and a rename fails with
*"RunScope has no …, so this row tests nothing."*

*2. Two statements in `RunScope` were corrected outside the table, text only.* `_dummySpec`'s tooltip
said *"Leave empty for an arena that starts bare"* — the exact edit rule 2's row now goes red on, so
the Inspector was instructing the regression. `PrewarmCount`'s remarks said a scene without an
archetype is one where *"core will never ask for a body"*, false since M2-05 made the director the
spawner. Both now say the archetype is what the prewarm keys on, and why `Run.unity` keeps it. No
member moved.

*3. Rule 3 has no row, and it was witnessed in Play instead.* It is rule 1 composed with
`StageFlowTests.Arrival_SpawnsNothing`, which already holds the director's half. A sampler counting
active `EnemyView`s every frame, Boot → Menu → Oathbound: **0 from the Run scene's first frame to
t + 2.83 s** — two seconds of arrival and wave 1's telegraph — then 1, 2 and 3 by t + 3.55 s. The
same sampler before this change would have opened on 8. The probe was a `Unity_RunCommand` and left
nothing in the tree; `run.json` and `profile.json` were backed up before it and restored after.

**Found on the way: the PlayMode suite writes the owner's real `run.json`.**
`BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap` reaches a run through boot's
`LocalJsonSaveStore` over `persistentDataPath` — the file was rewritten in the minute the suite ran.
EditMode's resume rows shadow the store for exactly this reason (`ResumeFlowTests.PlayARun`); the
smoke row does not. So every handover's PlayMode pass has replaced whatever run an Editor playtest
left. Not this task's → [parking lot](../ROADMAP.md#parking-lot).

**Rules ↔ rows.** 1: `RunScene_DressesNoEnemy`. 2: `RunScene_StillPrewarmsTheEnemyPool`. 3: the
witness above, and `Arrival_SpawnsNothing`.

**Red checks, on the Editor.** Against the unchanged scene, the fixture ran **1 / 1**:
`RunScene_DressesNoEnemy` failed with *"Expected: 0 But was: 8"* while the prewarm row passed, since
cap + 1 already outranked eight. With `_dummySpec` cleared in the file and the positions empty, **1 / 1**
the other way, the prewarm row failing with *"_dummySpec was cleared with the dummies."* The scene was
restored byte for byte and re-imported.

**Verified:** **3 154 EditMode / 0 / 0** (+2 on M6-11b's 3 152), then **PlayMode 26 / 0 / 0** on the
first pass. Console clean after each compile; after the EditMode pass, 16 errors and 30 warnings,
M6-11b's count, each from a passing negative-path row — opening `Run.unity` added none. Zero new
analyzer warnings; `dotnet format` green over both C# files; `TimeManager.asset` re-serialised and
was reverted.

**Stage 1 is ten Husks now, not eighteen.** Early levels come slower, and every early-XP figure in
[M7 row 2](../ROADMAP.md#carry-forward-into-m7)'s table was taken with eight extra kills in stage 1.
Retuning is M8-05's, as *Out of scope* says.
