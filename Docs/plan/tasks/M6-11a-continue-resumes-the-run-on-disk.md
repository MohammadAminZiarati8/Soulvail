# M6-11a — Continue resumes the run on disk, not the one the Menu read at boot

**Size:** S · **Depends on:** M6-11 · **Branch:** `m6-11a-continue-resumes-the-run-on-disk`
**Design refs:** GD §7.3; AR §18.1 · **Ledger rows:** [M7 row 4](../ROADMAP.md#carry-forward-into-m7)

## Goal

The Menu's Continue always resumes the run most recently written, and is hidden once that run has
died — within one app session as well as across a relaunch.

## Why this is a bug and how it was found

Found by [M6-11](M6-11-acceptance-and-tag.md)'s instrument B. The owner played an Emberwright from
stage 10 to 30, quit to the Menu, pressed Continue, and was handed **a stage-10, level-1 run with
another seed**: the five-second run abandoned half an hour earlier, whose opening write was on disk
when the Play session began. That restored run's own opening write then **overwrote the real
stage-30 save**. Nothing was logged, because nothing failed.

`BootFlow.LoadRun` reads `run.json` once and calls `SavedRun.Set`. `MenuPresenter` shows Continue
on `SavedRun.IsPresent` and resumes `SavedRun.Value`. **Nothing else in the project writes
`SavedRun`, and nothing clears it.** So within one app session:

- a save present at boot is offered for ever — after a newer run has overwritten it, and after that
  run has died and `SaveWriter` has deleted the file;
- a run started when no save existed at boot is never offered, although it is on disk.

A relaunch re-reads the file, which is why every Continue anyone had witnessed before (M2-13b,
M5-08, the resume rows) was correct: each one restarted Play mode first.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/SaveWriter.cs` | Game | Mirrors every snapshot it is handed, and every clear, into `SavedRun` before it queues the disk operation |
| `Tests/Game/Adapters/SaveWriterTests.cs` | Tests.Game | Rules 1–3 |
| `Tests/Game/Composition/ResumeFlowTests.cs` | Tests.Game | Rule 4, end to end through the Menu's two reads |
| *ripple* | | every `new SaveWriter(` — **18 sites by grep, which is a scope and not a list** ([Traps §2](../../Traps.md)): compile, then count |

## Public API

```csharp
public sealed class SaveWriter : IDisposable
{
    /// <param name="saved">The boot scope's SavedRun — what the Menu offers and resumes.</param>
    public SaveWriter(ISaveStore store, DomainEventHub hub, SavedRun saved);
}
```

## Behaviour

1. **On `RunSnapshotTaken`, `SavedRun.Set(snapshot)` runs before the write is queued.** Memory leads
   disk, never trails it.
2. **On `PlayerDied`, `SavedRun.Clear()` runs before `ClearRun` is queued.** A dead run is never
   offered.
3. **A write that fails leaves memory newer than disk, and that is the stated cost.** Continue in the
   same session resumes the run just played; a relaunch resumes the older file. The alternative —
   setting `SavedRun` only after the write lands — puts a thread-pool continuation on a main-thread
   object and makes Continue wrong in the ordinary case to be right in the failing one.
4. **`BootFlow` is unchanged.** It seeds `SavedRun` once at boot, which is the one moment memory can
   lag disk, and it is before any run exists in this session to lag it.
5. **`SavedRun` stays a boot-scope singleton** and the run scope's `SaveWriter` resolves it from its
   parent; no registration moves.

## Tests

| Test | Given / When / Then |
|---|---|
| `Writer_MirrorsEachSnapshotIntoTheSavedRun` | an empty `SavedRun` / two `RunSnapshotTaken`, stages 3 and 4 / `Value.StageIndex` is 4 |
| `Writer_MirrorsBeforeTheWriteLands` | a store whose `SaveRun` never completes / one snapshot / `IsPresent` true, the write still pending |
| `Writer_ClearsTheSavedRunOnDeath` | a present `SavedRun` / `PlayerDied` / `IsPresent` false, `ClearRun` queued |
| `Continue_AfterAQuitResumesTheRunJustPlayed` | boot seeded run A (seed 1, stage 10) / a run with seed 2 publishes its stage-31 snapshot, the Menu opens / Continue is shown and the pending resume is seed 2, stage 31 |
| `Continue_IsHiddenAfterADeath` | boot seeded run A / the run dies / the Menu hides Continue |

## Manual verification (Editor)

1. **[Editor]** With a saved run on disk, press Play, **Descend** a new run, clear two stages, quit
   to the Menu, press Continue → the run just played, at the stage it was saved at.
2. **[Editor]** Die, return to the Menu → no Continue.

## Out of scope

- **Re-reading the file when the Menu opens.** An asynchronous read on every Menu visit to learn what
  this process wrote a second ago.
- **A finite mode that completes** — nothing clears the file for it today and no mode is finite; a
  parking-lot question if one ever is.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
