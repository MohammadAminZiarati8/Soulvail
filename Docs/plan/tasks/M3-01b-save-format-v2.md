# M3-01b — Save format v2: what a levelled run writes down, and the first real migration

**Size:** S (no new files; two test fixtures substantially extended) · **Depends on:** M3-01a · **Branch:** `m3-01b-save-format-v2`
**Design refs:** AR §10.3, §11.6, §18.1, §18.2, §18.3; ADR-0007; M2-13a, M2-13b, M2-14a, M2-14b · **Ledger rows:** 2 — the whole of it; M3-03 fills the fourth field and bumps nothing

## Goal

`RunSnapshot` becomes v2 with the four fields a levelled run is made of, the v1 → v2 step ships in the same PR as the fields, the chain test loops over two versions for the first time, and a run resumed after a kill comes back at the level it had.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| *small edits* | | `Core/Save/SaveDtos.cs` — `CurrentVersion` 2, four fields (rule 1), four guards (rule 4); `Core/Save/SaveMigrations.cs` — the `if (version < 2)` step (rule 3); `Game/Adapters/LocalJsonSaveStore.cs` — `RunMirror` gains `level`, `xp`, `pendingLevelUps`, `takenNodeIds`; `Core/Save/RunRecorder.cs` — `Take` passes the three tracker reads and an empty list (rule 6); `Core/Progression/LevelTracker.cs` — `internal Restore` (rule 7); `Core/Run/RunState.cs` — `Xp` read (rule 8); `Core/Run/RunSession.cs` — the restore line beside `Health.Restore` (rule 7) |
| *tests* | Tests.Core / Tests.Game | `SaveDtoTests` (guards, copy, default), `SaveMigrationTests` (the step, the chain now two long), `LocalJsonSaveStoreTests` (the v1 literal proves the migration, a v2 literal is what this build writes), `RunRecorderTests` (capture), `RunSessionResumeTests` (restore) |
| *ripple* | | **13 `new RunSnapshot(...)` sites across 10 files** — compiler-guided |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Save;

public readonly struct RunSnapshot
{
    public const int CurrentVersion = 2;

    public RunSnapshot(
        int version,
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        RandomState random,
        float playerHp,
        float playerShield,
        float runTime,
        DateTimeOffset writtenAt,
        int level,                                   // v2: >= 1
        float xp,                                    // v2: >= 0, finite — into the level, not cumulative
        int pendingLevelUps,                         // v2: >= 0
        IReadOnlyList<ContentId> takenNodeIds);      // v2: copied; take order; empty until M3-03 writes it

    public int Level { get; }
    public float Xp { get; }
    public int PendingLevelUps { get; }
    public IReadOnlyList<ContentId> TakenNodeIds { get; }   // never null — empty for default(RunSnapshot)
}

// SaveMigrations.MigrateRun (changed): `if (version < 2)` rebuilds the DTO at v2 with
// Level 1, Xp 0, PendingLevelUps 0 and no nodes, keeping every v1 field as decoded.
```

```csharp
// LevelTracker (added)
internal void Restore(int level, float xp, int pendingLevelUps);   // silent; settles thresholds (rule 7)

// RunState (added)
public float Xp => Progression.Xp;                                  // absolute, for the recorder (rule 8)
```

## Behaviour

1. **One bump for the tree, four fields, ruled at M3-00a (ledger row 2):** `Level`, `Xp`, `PendingLevelUps`, `TakenNodeIds`. The fourth has no writer until M3-03 and is written empty until then. This trades against M2-13a rule 4 — *a field nothing reads is a field every later migration carries* — and the trade is stated: the reader is two tasks away in the same milestone, and the alternative is a second step in the chain, for ever, for a format nobody has shipped. **M3-03 fills the field and does not bump the version**; `Fixture_V2Run_IsWhatThisBuildWrites` is the row that objects if it does. Auto/Manual toggles (M3-07) are *not* reserved here: whether they survive a kill is M3-00b's call, and if they do that is a v3 by the same rule this task follows.
2. **The fields and the migration ship together.** `CurrentVersion` → 2, the `if (version < 2)` step, the v1 fixture proving the step through the real adapter, and the v2 fixture — one PR. `Chain_IsUnbrokenFromOldestToCurrent` loops over two versions for the first time; its text does not change, which is the point of having written it at v1. `OldestSupportedRunVersion` stays 1.
3. **A v1 run was unlevelled by construction**, so its v2 form is level 1, no XP, no picks owed, no nodes. The adapter's `RunMirror` carries `level = 1` as a field initialiser so a v1 document decodes into a DTO the constructor accepts; the migration step is the authority and writes all four regardless of what the mirror held — a v1 document that somehow carried a level is still a v1 document, and gets v1's meaning.
4. **The constructor guards what a reader can be handed** (M2-13a rule 10's reason): `level < 1`, a negative or non-finite `xp`, `pendingLevelUps < 0`, a null list, and a `default(ContentId)` entry all throw. Entries are not resolved against content — a node id this build no longer ships is content validation's answer at `RunSession.Start` (M3-03 rule 5), with the diagnostic that names it.
5. **The list is copied in and wrapped**, `ContentCatalog.Index`'s reasoning, **and that allocates at a boundary**, against M2-14a rule 7's *allocates nothing*. Named rather than hidden: `SaveWriter` **enqueues** the write (`SaveWriter.cs:119`), so a snapshot is held across frames, and a borrowed buffer would be rewritten under a save that had not happened yet. Twenty-seven ids twice a minute, and `default(RunSnapshot)` answers an empty list rather than null so no reader has to ask.
6. **Capture** — `RunRecorder.Take` reads `state.Level`, `state.Xp`, `state.PendingLevelUps`, and passes an empty list (M3-03 swaps in the tree's view). The boundary capture sits downstream of the tick's XP drain (M3-01a rule 6), so the level a stage's last kill earned is in the file that describes the next stage.
7. **Restore** — `RunSession.Start` calls `Progression.Restore(level, xp, pending)` in the block where `Health.Restore` sits, **before `RunStarted`**, for M2-14b rule 2's reason: a presenter reading `State.Level` from inside `RunStarted` must already see it. `Restore` is **silent** — nothing may publish before `RunStarted` — and it **settles**: if `xp >= XpToNext` under this build's curve, thresholds are crossed with no `LeveledUp`, so a curve retuned between builds cannot strand XP above the bar or turn a legal v2 file into a frozen one. `TakenNodeIds` is read and **ignored** by `Start` until M3-03, and a row says so, so that M3-03 flips a row rather than introduces a behaviour.
8. **`Xp` on `RunState` is absolute** for the reason `PlayerShield` is (M2-14a rule 4): a fraction cannot be restored without the maximum that produced it, and `XpToNext` moves with the level.
9. **Both on-disk literals are typed by hand** — `LocalJsonSaveStoreTests`' standing rule — and the key order is the mirror's field order: the four new keys follow `writtenAt`. The v1 literal stays exactly as it is and becomes the migration's input; the v2 literal is what this build writes.

## Tests

| Test | Given / When / Then |
|---|---|
| `Snapshot_RecordsTheFourNewFields` | level 7, xp 33.5, pending 1, two ids / ctor / each reads back; `TakenNodeIds` equal to the input and not the same instance |
| `Snapshot_LevelBelowOne_Throws` | level 0 / ctor / throws (rule 4) |
| `Snapshot_NegativeOrNonFiniteXp_Throws` | −1, NaN, ∞ / ctor / throws each (AR §18.3's spelling) |
| `Snapshot_NegativePending_Throws` | −1 / ctor / throws |
| `Snapshot_NullNodes_Throws` · `Snapshot_DefaultNodeId_Throws` | null; `[valid, default]` / ctor / throws |
| `Snapshot_NodesAreCopied` | a `List<ContentId>` the caller appends to afterwards / — / `TakenNodeIds.Count` unchanged (rule 5) |
| `Snapshot_DefaultHasEmptyNodesAndVersionZero` | `default(RunSnapshot)` / — / `TakenNodeIds` empty, not null; `Version` 0 still |
| `Gate_AcceptsOneAndTwoRefusesThree` | — / `CanReadRun(1)`, `(2)`, `(3)` / true, true, false |
| `Chain_IsUnbrokenFromOldestToCurrent` | *the existing row, unchanged* / — / loops 1..2 and passes (rule 2) |
| `Migrate_V1_GetsUnlevelledDefaults` | a v1-versioned DTO carrying level 5, xp 99, pending 2, one id / `MigrateRun(1, …)` / `Version` 2, `Level` 1, `Xp` 0, `Pending` 0, no nodes; every v1 field as given (rule 3) |
| `Migrate_V2_IsIdentity` | the current `Migrate_AtCurrent_IsIdentity`, extended to the four fields |
| `Fixture_V1Run_DecodesToTheExpectedSnapshot` | *the existing v1 literal* / `LoadRun` / every v1 field as before **and** `Level` 1, `Xp` 0, `Pending` 0, empty nodes — the step through the real adapter (rule 3) |
| `Fixture_V2Run_DecodesToTheExpectedSnapshot` | a hand-typed v2 literal (level 7, xp 33.5, pending 1, two ids) / `LoadRun` / all four read back |
| `Fixture_V2Run_IsWhatThisBuildWrites` | `SaveRun` of that snapshot / read the file / text equals the v2 literal byte for byte — renamed from the v1 row, which is now the migration's input (rules 1, 9) |
| `Recorder_CapturesLevelXpPending` | a session at level 3 with 20 XP and three picks owed / a boundary `Take` / `Level` 3, `Xp` 20, `Pending` 3 |
| `Recorder_NodesAreEmptyUntilM3-03` | any capture / — / `TakenNodeIds.Count == 0` — **M3-03 replaces this row** |
| `Recorder_BoundaryCarriesTheClearingKillsLevel` | a stage's last kill crosses a threshold / the snapshot taken that tick / `Level` is the new one (rule 6) |
| `Start_RestoresLevelXpPending` | a snapshot at level 4, xp 30, pending 1 / `StartResumed` / `State.Level` 4, `Xp` 30, `PendingLevelUps` 1 |
| `Start_RestoreIsAppliedBeforeRunStarted` | *the existing row*, extended / — / `Level` seen from the `RunStarted` handler is 4 (rule 7) |
| `Start_RestoreSettlesAnOverfullBar` | level 2, xp 10 000 / `Start` / `Level` > 2, `Pending` > 0, **no** `LeveledUp` or `XpChanged` published (rule 7) |
| `Start_IgnoresTakenNodesUntilM3-03` | a snapshot naming two ids / `Start` / no throw, nothing applied — **M3-03 flips this row to `Start_RestoresTakenNodes`** |
| `Start_FreshRunIsLevelOne` | `FreshConfig` / `Start` / `Level` 1, `Xp` 0, `Pending` 0 |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend, level once, clear the stage. Stop Play. `run.json` under `persistentDataPath` carries `"version":2`, `"level":2`, an `"xp"`, `"pendingLevelUps":1` and `"takenNodeIds":[]`.
2. **[Editor]** Play again, `Continue`. `DebugOverlay` shows level 2 from the first frame.
3. **[Editor]** Keep a `run.json` written by the M2 build. Drop it in, `Continue`. The run resumes at level 1 with no error, and the next boundary rewrites the file at v2.
4. **[device]** The kill-from-recents row (ledger row 4) now has a second thing to lose. Deferred with the rest.

## Out of scope

- **Restoring nodes** — M3-03 rule 5, which also decides the order against `Health.Restore`.
- **`PlayerProfile`** — untouched, still v1. The two formats version independently (M2-13b).
- **Auto/Manual toggles on disk** — M3-07 (rule 1).
- **A migration that renames or retypes a field.** `MigrateRun`'s signature takes and returns the current DTO; this bump only adds, which is the case that signature was built for. The first rename is a wider change and this task does not pretend to have solved it.

## As built

_Filled at merge._
