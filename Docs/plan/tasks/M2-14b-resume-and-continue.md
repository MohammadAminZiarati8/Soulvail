# M2-14b — Resume: a `Continue` that means it, and `PendingRun.Clear`'s first caller

**Size:** M · **Depends on:** M2-14a, M2-13b, M2-02 (`RunConfig`'s shape and its seed guard) · **Branch:** `m2-14b-resume-and-continue`
**Design refs:** GD §7.3 (a stage is a commute unit), §4.5; AR §4.1, §7, §10.3, §18.2; ADR-0002, ADR-0007, ADR-0011 · **Ledger rows:** 1 — the restore half, and the row leaves here; **10** — `PendingRun.Clear()` finally has a caller, and that row leaves here too

## Goal

An app killed by a phone call comes back to a `Continue` button, and tapping it puts the player at the top of the stage they had reached — same arena, same waves, same rolls, the health they actually had.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Composition/SavedRun.cs` | Game | What the disk said at launch. `PendingRun`'s sibling, and a different question |
| `Tests/Game/Composition/SavedRunTests.cs` | Tests.Game | Presence, the throw-before-set bargain, clearing |
| `Tests/Core/Run/RunSessionResumeTests.cs` | Tests.Core | `RunConfig.Restore` and what `Start` does with it (rules 1–5) |
| `Tests/Game/Composition/ResumeFlowTests.cs` | Tests.Game | Boot → Menu → scope → session, end to end (rules 6–11) |
| *small edits* | | `RunConfig` + `RunSnapshot? Restore`, its sixth field (rule 1); `RunSession.Start` applies it (rules 2–4); `PendingRun.Set` + a mode id and an optional snapshot, and **`Clear()` called from `RunTicker.Start`** (rule 9); `MenuPresenter` + a `Continue` button that appears only when there is something to continue (rule 7); `BootFlow` loads the run beside M2-13b's profile (rule 6); `RunInstaller.CreateRandom` restores the captured state onto the seeded generator (rule 3); `RunTicker.Start` builds the six-field config; `BootInstaller` registers `SavedRun` |
| *assets* | | `Menu.unity` gains the `Continue` button. Listed, not counted |
| *ripple* | | every `new RunConfig(...)` call site gains a sixth argument — M2-02 swept 19 of them and named the sweep; this is the same sweep, one argument wider |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Run;

// RunConfig (added — the sixth field M2-02's Out of scope named in advance)
/// The run this one is continuing, or null for a fresh one. Its `Seed` and `StageIndex` must
/// agree with this config's, and `Start` is where that is checked (rule 4).
public RunSnapshot? Restore { get; }
```

```csharp
namespace Soulvail.Game.Composition;

/// The run that was on disk when the app launched. A `BootScope` singleton beside `PendingRun`,
/// and deliberately a *different* object: this one is what the disk said, that one is what the
/// player chose (rule 8).
public sealed class SavedRun
{
    /// Whether there is a run to continue. False on a fresh install, after a death, and after a
    /// load that failed (M2-13b rule 5).
    public bool IsPresent { get; }

    /// <exception cref="InvalidOperationException">Nothing is present.</exception>
    public RunSnapshot Value { get; }

    public void Set(in RunSnapshot snapshot);
    public void Clear();
}

// PendingRun (changed)
public void Set(ContentId modeId, ContentId characterId, int seed);

/// The resume form: mode, class and seed all come from the snapshot, so they cannot disagree.
public void Resume(in RunSnapshot snapshot);

public ContentId ModeId { get; }
public RunSnapshot? Snapshot { get; }   // null for a fresh run
```

## Behaviour

**Core**

1. **`RunConfig` gains its sixth field and no others** — M2-02's *Out of scope* named it, named its type and said why in advance: *"reshape once means these five fields are right, not that a sixth is forbidden."* That promise is kept here rather than renegotiated.
2. **A restore is applied after content validation and before `RunStarted`.** `Start`'s order (M2-02 rule 7) gains one step: resolve the mode → resolve the character → check the seed → check the stage → check the restore (rule 4) → resolve every archetype → build the state → **apply the restore** → publish `RunStarted` → `IsRunning = true` → `SpawnAll`. Before the publish, because a HUD handling `RunStarted` draws the bar it is told about (M1-17 reads `RunState.PlayerHp` there), and a full bar that jumps to 62 % on the next frame is the resumed run's first impression.
3. **What the restore puts back: HP, shield, run time, and the generator's position.** The last is not core's to apply — `IRandom` is constructed by the container from the seed, so `RunInstaller` calls `Restore` on it the moment it builds `SeededRandom`, and by the time `RunSession.Start` runs the streams are already where the snapshot left them. Core would otherwise need a way to reach back through the port and rewind it, which is precisely the door M2-13a rule 7 declined to open.
4. **The restore is checked against the config, and the check is the one M2-02 already wrote.** `Start` throws when `Restore.Seed != config.Seed` or `Restore.StageIndex != config.StageIndex` — the same shape and the same moment as M2-02 rule 5's `config.Seed != _random.Seed`, because all three are one question: *does everything about to build this run agree about which run it is?* Nothing is announced and nothing stands when it fails.
5. **Everything else a resumed run needs is rebuilt, not read back.** The arena is `ArenaFor(stage, seed)` (M2-11a rule 3); the wave plan is composed from the restored stream position (M2-14a rule 5); the enemies are none, because a boundary has none. This is the list that makes the DTO ten fields instead of a hundred, and each entry earns its place by being *derivable* rather than by being unimportant.

**The app**

6. **`BootFlow` loads the run and the profile before it leaves Boot**, and Boot exists for exactly this. A Menu that appeared first and then grew a `Continue` button a frame later is the shape that gets tapped through by accident; and the load is a local file read of a few hundred bytes, on the one screen in the game with nothing to be smooth about.
7. **`Continue` is shown only when `SavedRun.IsPresent`, and it is the top button.** Tapping it calls `PendingRun.Resume(savedRun.Value)` and loads the Run scene — the same path `Descend` takes, differing in one argument. `Descend` is unchanged and starts a fresh run; **it does not delete the saved one, because M2-14a rule 1's opening write overwrites it on the new run's first frame.** A confirmation dialog before abandoning a deep run is real UX and it is M8-02's, with the string M6-10 owns.
8. **`SavedRun` is not `PendingRun` with an extra field.** They answer different questions at different times — one is a fact about the disk, established once at launch and never written again by the app; the other is a choice, made in the Menu, consumed in the Run scene and cleared. Folding them together would make "is there a saved run" and "did the player pick one" the same boolean, and the first `Continue`-then-back-out would find them disagreeing. Both are `BootScope` singletons for the reason `PendingRun`'s own comment gives: *owned*, injected, declared — not static (ADR-0002).
9. **`PendingRun.Clear()`'s first caller is `RunTicker.Start`, immediately after `_session.Start` returns — ledger row 10.** The promise has been unkept since M0-12: the method's own doc says *"Called once the run has started, so a second trip through the Run scene cannot silently reuse the previous run's seed."* `RunTicker.Start` is the last reader — `RunInstaller.CreateRandom` resolves `PendingRun` when `IRandom` is first built, which is during `RunSession`'s construction and therefore before this — so clearing here cannot pull a value out from under anything. Clearing in the installer would.
10. **A resumed run is *not* deleted from disk when it is resumed.** The file survives until the next boundary overwrites it or death clears it, so a player interrupted twice in one stage resumes twice. Deleting on read would mean a second phone call loses the run outright, which is the exact failure GD §7.3 calls the most rage-inducing bug we could ship. The cost is the rewind the owner ruled acceptable at M2-00e, taken more than once.
11. **Direct Play is untouched.** Pressing Play in the Run scene resolves a `SavedRun` that nothing set and a `PendingRun` that nothing set, so the run is fresh, unseeded and warned about exactly as M0-12 left it. The resume path lives entirely in `BootScope` and the Menu, which is what keeps the fastest loop in development working (M0-12 rule 6, `BootFlow`'s own comment).

## Tests

| Test | Given / When / Then |
|---|---|
| `Config_RecordsTheRestore` | a config with a snapshot / — / `Restore` reads back; a config without / `Restore` is null (rule 1) |
| `Start_RestoresHpAndShield` | a snapshot at HP 62, shield 9 / `Start` / `State.PlayerHp` 62, `State.PlayerShield` 9 — **not** the class's maximum (rules 2, 3) |
| `Start_RestoresRunTime` | snapshot `RunTime` 412.5 / `Start` / `State.Time` 412.5 |
| `Start_RestoreIsAppliedBeforeRunStarted` | a subscriber reading `State.PlayerHp` from its `RunStarted` handler / `Start` / it read 62, not the maximum (rule 2) |
| `Start_FreshRunStartsAtFullHealth` | `Restore` null / `Start` / HP is the class's maximum, and nothing about the fresh path changed |
| `Start_RestoreSeedDisagrees_Throws` | config seed 7, snapshot seed 8 / `Start` / throws; not running; no events; previous `State` readable (rule 4) |
| `Start_RestoreStageDisagrees_Throws` | config stage 4, snapshot stage 5 / `Start` / same (rule 4) |
| `Start_RestoreOfAnUnauthoredMode_ThrowsFirst` | a snapshot naming `mode.ghost` / `Start` / the content diagnostic M2-02 rule 7 writes, not a restore error — validation runs first (rule 2) |
| `Resume_LandsInTheSameArena` | run A at stage 7; a run resumed from A's snapshot / — / the same `ArenaFor` result (rule 5, M2-11a rule 3) |
| `Resume_ComposesTheSameStage` | as above / compose stage 7 in both / identical wave plans (rule 5, M2-14a rule 5) |
| `Resume_SpawnsAtTheSamePositions` | as above / `Tick` through wave 1 in both / the same telegraph positions in the same order (ledger row 1, end to end) |
| `Resume_StartsWithNoEnemies` | a resumed run / `Start` / nothing registered before the director's first wave (rule 5) |
| `Saved_ReadBeforeSet_Throws` | a fresh `SavedRun` / `Value` / `InvalidOperationException` naming `IsPresent` — `PendingRun`'s bargain, for its reason (rule 8) |
| `Saved_SetThenPresent` | `Set(x)` / — / `IsPresent` true, `Value` equals `x` |
| `Saved_ClearForgets` | set, then `Clear` / — / `IsPresent` false, `Value` throws |
| `Boot_LoadsBeforeLeavingBoot` | a store holding a run / run `BootFlow` / `SavedRun.IsPresent` was true before the Menu scene was requested (rule 6) |
| `Boot_NoSavedRun_IsAbsent` | an empty store / boot / `IsPresent` false, no throw (rule 6) |
| `Boot_UnreadableSave_IsAbsent` | a store returning null for a corrupt file (M2-13b rule 5) / boot / `IsPresent` false, the app reaches the Menu (rule 6) |
| `Menu_ContinueHiddenWithNoSave` | `IsPresent` false / enable the menu / the `Continue` button is inactive; `Descend` is not (rule 7) |
| `Menu_ContinueShownWithASave` | `IsPresent` true / enable / `Continue` active |
| `Menu_ContinueSetsThePendingRun` | a snapshot at stage 9 / tap `Continue` / `PendingRun.Snapshot` is it, `ModeId` and `CharacterId` are the snapshot's, Run scene requested (rule 7) |
| `Menu_DescendStartsFresh` | `IsPresent` true / tap `Descend` / `PendingRun.Snapshot` null, and **`SavedRun` is untouched** — the file is overwritten by the new run's opening write, not by the menu (rule 7) |
| `Ticker_BuildsTheResumedConfig` | `PendingRun` resumed at stage 9 / `RunTicker.Start` / the config carries the mode, class, seed, stage 9 and the snapshot (rules 3, 7) |
| `Ticker_ClearsThePendingRun` | any run started / `RunTicker.Start` / `PendingRun.IsSet` false afterwards (**ledger row 10**) |
| `Ticker_ClearsAfterTheSessionStarted` | a `PendingRun` spy / `Start` / `Clear` was called after `_session.Start` returned, never before (rule 9) |
| `Installer_RestoresTheGeneratorState` | a pending resume whose snapshot captured 500 draws in / resolve `IRandom` / its next draw equals the original's 501st (rule 3) |
| `Installer_FreshRunDoesNotRestore` | a pending fresh run / resolve `IRandom` / seeded and at draw 0 |
| `DirectPlay_ResumesNothing` | neither singleton set / build `RunScope`, start / a fresh unseeded run and the M0-12 warning, unchanged (rule 11) |
| `Resume_TwiceFromOneSnapshot` | resume, take damage, quit without reaching a boundary, resume again / — / the second resume gets the same snapshot (rule 10) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play from Boot: the Menu has one button, `Descend`. Start a run, clear two stages, then stop Play from the door of stage 3. Press Play again — the Menu now has `Continue` above `Descend`.
2. **[Editor]** Tap `Continue`. You arrive at the top of stage 3, in the same room, with the HP you had — not a full bar. The overlay reads `depth 3`.
3. **[Editor]** Do it again from the same save without clearing a stage. You get the same room and the same first wave, in the same order — that is ledger row 1, and before this task the second resume would have re-drawn everything from zero.
4. **[Editor]** Tap `Descend` instead, with a save present. A fresh run starts at stage 1, and `run.json` immediately describes *it* (M2-14a rule 2). Stop and press Play: `Continue` now offers stage 1, not stage 3.
5. **[Editor]** Die in a resumed run, then relaunch: `Continue` is gone.
6. **[device]** **Kill from recents and relaunch** — the row that has sat on the deferred list since M0 with nothing to check. Clear a stage, swipe the app away, reopen it, tap `Continue`. Also: an incoming call mid-stage, which is the scenario GD §7.3 is written about. Deferred until a phone exists.

## Out of scope

- **A confirmation before `Descend` abandons a deep run** — M8-02, and the string is M6-10's. Rule 7 names it rather than half-building it.
- **Showing anything *about* the saved run on the button** — "Stage 9, 12 minutes ago" needs `LocKey`s and a format, and `WrittenAt` is in the DTO waiting for them (M2-13a rule 11). M8-02.
- **"How long were you away"** and its backwards-clock guard (M2-01 rule 4). Nothing uses the elapsed difference yet; the day something does, that guard is its owner's.
- **Resuming mid-stage.** The owner ruled at M2-00e that a resume restarts the stage whole; M2-14a's *Out of scope* carries the pause-write that would change it.
- **Level, XP and the tree coming back** — M3, which adds fields and a migration.
- **More than one save slot.** One run, one file (AR §10.3). Slots are a feature nothing has asked for and a format change if it is ever asked for.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
