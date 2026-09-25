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

**Rules 1–5 as written.** `SaveWriter` takes the boot scope's `SavedRun`, calls `Set(snapshot)` on
`RunSnapshotTaken` and `Clear()` on `PlayerDied`, each on the publishing thread and **above** its
`Enqueue`. `BootFlow` is unchanged and no registration moved: `SavedRun` sits in `BootInstaller`
beside `PendingRun`, and every run scope — `Run.unity` by `VContainerSettings`' root, every test
container by `BuildBoot()` — already had it as a parent.

**The ripple was 18 sites by grep and 18 by compile** — the grep was the list this time, and
[Traps §2](../../Traps.md) still says to count. Thirteen in `SaveWriterTests`, one each in
`ResumeFlowTests`, `PausePresenterTests`, `SkillsPresenterTests`, `SkillBarPresenterTests` and
`FrameOrderTests`. `RunInstaller`'s `Register<SaveWriter>` needed nothing.

**Four deviations.**

*1. Three comments outside the table were corrected, because each had become false.* `SavedRun`'s
remarks called it a fact *"established once at launch and never written again by the app"*;
`BootInstaller` said it *"is written exactly once per app launch, by BootFlow"*; `MenuPresenter`'s
summary said Continue records *"the run that was on disk at launch"*. That last sentence is the bug,
written down as a design. Comments only; no code outside the table moved.

*2. `SaveWriter` is the first `Game.Adapters` type to import `Game.Composition`.* `Composition`
already imports `Adapters` (`RunInstaller` registers the writer), so this is a namespace cycle
inside one assembly, not an assembly one. The Public API asks for `SavedRun` by name. An interface
over it would be a port with one implementation and one caller, bought to satisfy a namespace
diagram nothing enforces.

*3. The two stores in `SaveWriterTests` gained a `Watched` spy.* Rules 1 and 2 say **before**,
and an end-state assertion cannot see order. With the mirror moved *below* each `Enqueue`, three of
the five new rows stayed green; only `HangingStore.MirroredWhenStarted` and
`RecordingStore.PresentWhenCleared`, which read `SavedRun` at the moment the store is called, went
red. That is the red check below.

*4. The two `Continue_` rows go through a real run scope, and tap the button.* `PlayARun` builds
`boot.CreateScope(RunInstaller.Install)` with `ISaveStore` shadowed by a stub: boot's is a
`LocalJsonSaveStore` over `persistentDataPath`, the owner's own `run.json`, and the row asserts the
shadow before it publishes. Resolving the writer from the scope and `SavedRun` from boot is
**rule 5 as a test** — a `SavedRun` registered per run would leave the Menu reading boot's copy and
both rows red — so rule 5 needed no row of its own. The tap goes through a recording loader, the
route the fixture's remarks said had been open since M3-09a; the remarks now say it was taken.

**Red checks, both on the Editor.** With the two mirror lines removed, the two fixtures ran **36 / 5**,
and the five were exactly the new rows. `Continue_AfterAQuitResumesTheRunJustPlayed` failed with
*"Continue resumed the run boot read, not the one just played"*, which is M6-11's lost stage-30 run
in one assertion. With the lines moved below `Enqueue`, **39 / 2**, as item 3 says.

**Verified:** **3 145 EditMode / 0 / 0** (+5 on 3 140). **PlayMode 26 / 0 / 0 on two of three
passes**; the first was 25 / 1 on `Ticker_ReportsFactsAfterBodiesMoved`'s wedge assertion, the
flake [M7 row 8](../ROADMAP.md#carry-forward-into-m7) hands to M6-11e. This task only adds a
constructor argument in that fixture. Console clean after the compile; after the suite, 16 errors
and 30 warnings, every one traced to a passing negative-path row and none to this task's. Zero new
analyzer warnings,
`dotnet format whitespace --verify-no-changes` *Formatted 0 of 10* over the ten touched C# files.
`TimeManager.asset` re-serialised and was reverted.

**Rule 3's cost, stated where a reader will look.** A failed write leaves `SavedRun` newer than the
file: Continue in the same session resumes the run just played, and a relaunch the older one. The
`Debug.LogError` the writer already raises is unchanged and still true, because the run is still
playable and still will not survive being killed. **One ordering hazard remains, and rule 4 names
it:** `BootFlow`'s load is a continuation, so a store that answered *after* a run had started would
seed over a newer mirror. `LocalJsonSaveStore` answers inside `Start`, and ADR-0007's syncing store
is where that stops being true.
