# M3-07b — Save format v3: the loadout on disk, and the first chain with two steps in it

**Size:** S (no new files; three test fixtures extended) · **Depends on:** M3-07a, M3-01b · **Branch:** `m3-07b-save-format-v3`
**Design refs:** GD §7.3; CC §6.1, §6.2, §6.3; CH §4.3; AR §10.3, §11.6, §18.1, §18.2, §18.3; ADR-0007; M2-13a, M2-13b, M2-14a, M3-01b · **Ledger rows:** 2 — its second bump, by the rule M3-01b wrote: *"if M3-07 wants them to survive a kill, that is a v3 by the same rule this task follows"*

## Goal

`RunSnapshot` becomes v3 with the one field a loadout is, the v2 → v3 step ships in the same PR as the field, `MigrateRun` runs two steps in sequence for the first time, and a player who set four skills to Manual and lost the app to an incoming call gets their thumbs back.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| *small edits* | | `Core/Save/SaveDtos.cs` — `CurrentVersion` 3, `ManualSkillIds` and its guards (rules 2, 3); `Core/Save/SaveMigrations.cs` — the `if (version < 3)` step, below the `< 2` one (rule 4); `Game/Adapters/LocalJsonSaveStore.cs` — `RunMirror` gains `manualSkillIds`; `Core/Save/RunRecorder.cs` — `Take` passes `state.ManualSkillIds`; `Core/Combat/SkillRunner.cs` — `internal Restore` (rule 6); `Core/Run/RunState.cs` — the `ManualSkillIds` read; `Core/Run/RunSession.cs` — the restore line, below the runner's actives and above `Health.Restore` (rule 7); **AR §18.1**'s restore-order row gains its fifth line |
| *tests* | Tests.Core / Tests.Game | `SaveDtoTests` (guards, copy, default, the fixed length), `SaveMigrationTests` (the step, the chain now three long, **v1 through both steps**), `LocalJsonSaveStoreTests` (a v3 literal is what this build writes; the v2 literal becomes the new step's input; the v1 literal still decodes), `RunRecorderTests` (capture), `RunSessionResumeTests` (restore, and the dropped id) |
| *ripple* | | every `new RunSnapshot(...)` site gains one argument — **13 across 10 files at M3-01b, plus whatever that task added**; compiler-guided, M2-02's nineteen `RunConfig` sites again |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Save;

public readonly struct RunSnapshot
{
    public const int CurrentVersion = 3;

    // ... the v2 arguments, then:
    //     IReadOnlyList<ContentId> manualSkillIds   // v3: exactly SkillRunner.MaxManualSlots long;
    //                                               // default(ContentId) means an empty slot (rule 3)

    public IReadOnlyList<ContentId> ManualSkillIds { get; }   // never null; four defaults for default(RunSnapshot)
}

// SaveMigrations.MigrateRun (changed): a second step, `if (version < 3)`, below the first —
// it rebuilds the DTO at v3 with four empty slots and keeps every v2 field as decoded.
```

```csharp
// SkillRunner (added)
internal void Restore(IReadOnlyList<ContentId> slots);   // silent; drops what the run does not own (rule 6)

// RunState (added)
public IReadOnlyList<ContentId> ManualSkillIds { get; }  // the runner's Slots view; four empties with no tree
```

## Behaviour

1. **One field, and it carries the slots rather than a set of ids.** CC §6.2's four positions are the state: which skills are Manual is recoverable from the list, and *where each one sits under the thumb* is not recoverable from anything else. A set of ids would come back compacted into S1…Sn, which is precisely the silent re-bind M3-07a rule 3 refuses to do while the app is running and has no business doing across a restart either.
2. **The list is exactly `SkillRunner.MaxManualSlots` long**, always, on disk and in the DTO. A shorter or longer one throws. A fixed length is what makes a *hole* expressible, and a hole is a legal and ordinary state (M3-07a rule 3) — a player with skills in S1 and S3 has two buttons, not two adjacent ones.
3. **`default(ContentId)` means an empty slot here, and that is the opposite of what it means in `TakenNodeIds`.** M3-01b rule 4 refuses a default entry in the nodes list because there it can only be a forgotten field; here it is the only way to spell *"this slot is empty"*, and refusing it would make an empty S2 unsaveable. Said out loud in both places, because two lists of `ContentId` in one struct with opposite rules is exactly the sort of thing a later reader gets wrong. Everything else is guarded as M3-01b guards its list: null throws, and the entries are **copied in and wrapped** for that task's rule 5 reason — `SaveWriter` enqueues, so a snapshot outlives the frame it was taken in and a borrowed buffer would be rewritten under it.
4. **The field and the migration ship together, and the chain now has two steps.** `CurrentVersion` → 3 and `if (version < 3)` in the same PR, below the `< 2` step and never beside it: `MigrateRun` walks the steps **in order**, so a v1 document runs through both and arrives at v3, and this is the first time that shape has been exercised rather than asserted. `Chain_IsUnbrokenFromOldestToCurrent` loops over three versions and its text still does not change, which remains the point of having written it at v1. `OldestSupportedRunVersion` stays 1.
5. **A v2 run had no loadout by construction**, so its v3 form is four empty slots — every skill on Auto, which is also CC §6.1's default, so a migrated run is indistinguishable from a fresh one on this axis and needs no special case anywhere above the DTO. `RunMirror` carries an empty four-slot array as a field initialiser so a v1 or v2 document decodes into a DTO the constructor accepts; the step is the authority and overwrites it regardless, M3-01b rule 3's rule for the same reason — a v2 document that somehow carried slots is still a v2 document.
6. **Restore drops what the run does not own, and does not throw.** An id naming a skill the runner has no entry for — a node this build no longer ships, a file hand-edited, a tree that changed between builds — leaves that slot **empty**, and the rest restore. This is deliberately *unlike* `SkillTree.Restore`, which throws for an unknown node (M3-03 rule 5): a taken node **is** the run's power, so silently dropping one hands the player a weaker character than they saved, while a slot is where a button sits and a missing button costs one visit to CC §6.3's screen. An id that appears twice is a corrupt list rather than a stale one and throws, because the second copy cannot be a slot the run has forgotten about. `Restore` is **silent** — nothing may publish before `RunStarted` — and it ignores the ceiling by construction, since four slots cannot hold five skills.
7. **The restore line sits below the runner's actives and above `Health.Restore`**, and the whole order is now five lines long — an **AR §18.1** row that no single task owns and every task adds to: `LevelTracker.Restore` (M3-01b rule 7) → `SkillTree.Restore` (M3-03 rule 5) → the runner is told about the restored Actives (M3-06 rule 5) → **this** → `Health.Restore` last, after everything that can move `MaxHp`. Below the runner because a slot naming a skill the runner has not been told about yet is indistinguishable from rule 6's dropped id, and a resumed run would come back with empty buttons and no error. Above `Health.Restore` for no reason of its own — it moves no stat — and written there anyway, because the block is read as an order and a line placed outside it invites the next one to be placed anywhere.
8. **Capture is the recorder reading `RunState.ManualSkillIds`**, which is the runner's `Slots` view (M3-07a), taken at the boundary like every other field. A run with no tree has no actives and therefore four empty slots, which is what a snapshot of an M3-era run without content correctly says.
9. **Both on-disk literals are typed by hand** — `LocalJsonSaveStoreTests`' standing rule — and the new key follows `takenNodeIds` in the mirror's field order. The **v2 literal stays exactly as it is and becomes the new step's input**; the v3 literal is what this build writes. The v1 literal is untouched and now proves two steps in one decode, which is the row this task exists to earn.

## Tests

| Test | Given / When / Then |
|---|---|
| `Snapshot_RecordsTheSlots` | `[A, default, C, default]` / ctor / reads back equal and **not** the same instance (rule 3) |
| `Snapshot_WrongSlotCount_Throws` | three entries; five entries / ctor / throws each naming 4 (rule 2) |
| `Snapshot_NullSlots_Throws` | null / ctor / throws |
| `Snapshot_DefaultEntryIsLegal` | `[default, default, default, default]` / ctor / no throw — the contrast with `TakenNodeIds` (rule 3) |
| `Snapshot_DuplicateSlotEntry_Throws` | `[A, A, default, default]` / ctor / throws (rule 6's other half, refused at the door) |
| `Snapshot_SlotsAreCopied` | a `List<ContentId>` the caller rewrites afterwards / — / unchanged (rule 3) |
| `Snapshot_DefaultHasFourEmptySlots` | `default(RunSnapshot)` / — / `ManualSkillIds.Count` 4, all default, not null |
| `Gate_AcceptsOneToThreeRefusesFour` | — / `CanReadRun(1)`, `(2)`, `(3)`, `(4)` / true, true, true, false |
| `Chain_IsUnbrokenFromOldestToCurrent` | *the existing row, unchanged* / — / loops 1..3 and passes (rule 4) |
| `Migrate_V2_GetsEmptySlots` | a v2 DTO carrying four ids / `MigrateRun(2, …)` / `Version` 3, four empty slots; every v2 field as given (rule 5) |
| `Migrate_V1_RunsBothStepsInOrder` | a v1 DTO carrying a level, XP, picks, nodes **and** slots / `MigrateRun(1, …)` / `Version` 3, `Level` 1, `Xp` 0, `Pending` 0, no nodes, four empty slots — **the first two-step migration** (rule 4) |
| `Migrate_V3_IsIdentity` | the current identity row, extended to the slots |
| `Fixture_V1Run_DecodesToTheExpectedSnapshot` | *the existing v1 literal* / `LoadRun` / every v1 field as before, plus v2's defaults and four empty slots (rules 4, 9) |
| `Fixture_V2Run_DecodesToTheExpectedSnapshot` | *M3-01b's v2 literal, unchanged* / `LoadRun` / its four v2 fields, plus four empty slots — the new step through the real adapter (rule 5) |
| `Fixture_V3Run_DecodesToTheExpectedSnapshot` | a hand-typed v3 literal with S1 and S3 filled / `LoadRun` / `[A, default, C, default]` (rule 9) |
| `Fixture_V3Run_IsWhatThisBuildWrites` | `SaveRun` of that snapshot / read the file / text equals the v3 literal byte for byte — renamed from the v2 row, which is now a migration input (rules 4, 9) |
| `Recorder_CapturesTheSlots` | a run with two manual skills in S1 and S3 / a boundary `Take` / `[A, default, C, default]` (rule 8) |
| `Recorder_NoTreeCapturesFourEmpties` | a class with no tree / a boundary `Take` / four defaults, no throw (rule 8) |
| `Start_RestoresTheSlots` | a snapshot naming two owned skills in S1 and S3 / `StartResumed` / `ManualSlotAt(0)` A, `(1)` default, `(2)` C; `ManualSlotCount` 2; **no** `SkillAutoCastChanged` published (rule 6) |
| `Start_RestoreDropsAnUnownedSlot` | a snapshot naming a skill whose node is not in `TakenNodeIds` / `StartResumed` / that slot empty, the others restored, no throw, run starts (rule 6) |
| `Start_RestoredManualSkillDoesNotAutoCast` | a restored manual skill whose trigger is met / the first `Tick` / no `SkillCast` — the point of the field (rules 6, 7) |
| `Start_RestoresSlotsAfterTheRunnerKnowsTheActives` | a snapshot whose node list and slot list name the same Active / `StartResumed` / the slot is filled — it is empty if the two lines are swapped (rule 7) |
| `Roundtrip_PreservesTheHole` | a run with skills in S1 and S3 / `SaveRun`, `LoadRun`, `StartResumed` / S1 and S3, **not** S1 and S2 — the whole reason the field is four slots and not a set (rule 1) |
| `Start_FreshRunHasFourEmptySlots` | `FreshConfig` / `Start` / four defaults, `ManualSlotCount` 0 |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with a tree authored by hand (M3-12 has not shipped): take an Active, set it to Manual, clear the stage. Stop Play. `run.json` carries `"version":3` and a `"manualSkillIds"` array of four with the id in the first slot.
2. **[Editor]** Play again, `Continue`. The skill is still Manual — `DebugOverlay` shows `slots 1/4` from the first frame.
3. **[Editor]** Keep a `run.json` written by the M3-01b build. Drop it in, `Continue`. The run resumes with everything on Auto, no error, and the next boundary rewrites the file at v3.
4. **[device]** Ledger row 4's kill-from-recents row now has a third thing to lose, and this task is the reason it is worth doing: `Continue` after a real process death should come back with the same four buttons. Deferred with the rest.

## Out of scope

- **A migration that renames or retypes a field.** `MigrateRun`'s signature takes and returns the current DTO; this bump only adds, which is the case that signature was built for — M3-01b's line, still true and still not solved.
- **`PlayerProfile`** — untouched, still v1. The two formats version independently (M2-13b).
- **Cooldowns on disk.** A resumed run comes back with every skill ready, like a resumed run comes back with the Charge ready today. A cooldown is seconds of a fight the player is not in the middle of any more, and saving it would need `RunState.Time` to mean something across a process death, which it deliberately does not.
- **Which skills are *owned*** — that is `TakenNodeIds` (M3-01b, M3-03), and this list only says where four of them sit.
- **Reordering** — M3-07a's Out of scope. If M3-09 adds it, the field already carries the answer and no bump is needed.

## As built

_Filled at merge._
