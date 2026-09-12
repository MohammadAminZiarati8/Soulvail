# M2-14a — The snapshot at the boundary: what a run writes, when, and what it deliberately forgets

**Size:** M · **Depends on:** M2-13a, M2-10 (`StageCleared` and the phase machine), M2-01 · **Branch:** `m2-14a-snapshot-at-the-boundary`
**Design refs:** GD §7.3 (run state persists at every stage boundary), §12.5; AR §4.1, §10.3, §18.1, §18.2; ADR-0007, ADR-0011 · **Ledger rows:** 1 — the capture half, at the one moment a resume can reproduce

## Goal

A run is on disk from its first frame and re-written at every stage boundary — each time captured before the stage it describes draws a single random number — and death deletes it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Save/RunRecorder.cs` | Core | Builds the `RunSnapshot` from live state and announces it |
| `Core/Events/SaveEvents.cs` | Core | `RunSnapshotTaken` — grouped, on `RunEvents.cs`' precedent |
| `Game/Adapters/SaveWriter.cs` | Game | The only thing that awaits `ISaveStore`; writes on the event, clears on death |
| `Tests/Core/Save/RunRecorderTests.cs` | Tests.Core | Every rule 1–6 |
| `Tests/Game/Adapters/SaveWriterTests.cs` | Tests.Game | Rules 7–10 |
| *small edits* | | `RunState` + `PlayerShield`, a narrow read beside `PlayerHp` (rule 4); `RunSession` takes a `RunRecorder`, `Start` takes the opening snapshot after `RunStarted` and before the flow composes, `Tick` watches the flow's phase edge (rule 1); `RunInstaller` registers `RunRecorder` and `SaveWriter` in `RunScope`; **AR §18.1**'s stage row gains "…and the boundary snapshot is taken on entering `Clear`"; **`FixedClockTests`' `RunSession_TakesNoClock`** widened to every type in `Soulvail.Core.Run` (rule 12) |
| *ripple* | | **`RunSession`'s constructor gains a parameter**, so every `Tests.Core` fixture that builds one gains a `RunRecorder` over a `FixedRandom` and a `FixedClock` — a mechanical, compiler-guided sweep, and the reason the recorder is *injected* rather than built inside `Start` is rule 12: a session that constructed its own recorder would have to hold the clock to do it |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Events;

/// A run has been written down. Carries the whole DTO by value — nothing allocates, and the
/// listener that persists it is on the Unity side (rule 7).
public readonly struct RunSnapshotTaken
{
    public readonly RunSnapshot Snapshot;
}
```

```csharp
namespace Soulvail.Core.Save;

/// Turns the live run into a `RunSnapshot` at the moments GD §7.3 names. Holds the clock, which
/// is why it is here and not in `Core/Run` (rule 11).
public sealed class RunRecorder
{
    public RunRecorder(IRandom random, IClock clock, IDomainEvents events);

    /// Writes the run down as it stands, for a resume that will begin at `resumeStage`, and
    /// publishes `RunSnapshotTaken`. Allocates nothing.
    public void Take(RunState state, int resumeStage);
}
```

```csharp
namespace Soulvail.Core.Run;

// RunState (added) — a narrow read beside PlayerHp, never the handle (AR §18.2).
/// The Aegis in absolute points, not the fraction `PlayerShieldFraction` reports. A fraction
/// cannot be restored without the maximum that produced it, and M3's tree moves that maximum.
public float PlayerShield => Combat.Health.Shield;
```

```csharp
namespace Soulvail.Game.Adapters;

/// The one place `ISaveStore` is awaited. Hears core, writes the file, and never lets a failed
/// write reach the frame.
public sealed class SaveWriter : IDisposable
{
    public SaveWriter(ISaveStore store, DomainEventHub hub);

    /// Whether a write is in flight. For tests and the debug overlay; nothing gates on it.
    public bool IsWriting { get; }

    public void Dispose();
}
```

## Behaviour

**When**

1. **Two write points, and both are "a stage is about to be played".** The first is the **opening of a run**, in `RunSession.Start` after the state is built and before the stage flow composes anything. The second is the **transition into `Clear`** — the frame the last body of a stage drops, which is M2-10 rule 3's moment and GD §7.3's *"run state persists to disk at every stage boundary"*. For the boundary, `RunSession.Tick` records `_stage.Phase` before ticking the flow and compares it after; the edge into `Clear` is the trigger. **Rejected: a parameter on `StageFlow`'s constructor**, which would widen a shape M2-10 fixed for a caller that only wants to know *when*; and **rejected: writing on the next stage's `StageArrived`**, which carries the same information one beat later — but the beat in between is the gate wait, and GD §7.3 calls that the natural *"put the phone down"* point. A phone put down at the door must already be saved.
2. **The opening write is what stops a stale run being resumed, and it is cheaper than the alternatives.** Without it, a player who abandons a stage-12 run, starts a fresh one and loses the phone in stage 1 resumes into stage 12 — the file on disk describes a run nobody is playing until the new one reaches its own first boundary, 40–75 s later. The fix could have been the Menu deleting the file when `Descend` is tapped, which puts an `ISaveStore` and a fire-and-forget delete into a presenter (M2-14b would own it); writing the new run down instead makes the file describe the run in progress *at all times*, needs no new dependency anywhere, and has the side benefit GD §7.3 actually asked for — a run is durable from its first frame rather than from its first door.
3. **A snapshot always describes the stage the player is about to play, never the one behind them.** At the opening, `resumeStage` is `config.StageIndex`; at a boundary it is `flow.Stage + 1`. A resume that replayed a stage the player had already cleared would be the same rewind the owner's ruling accepted *once* per kill, charged twice.
4. **What it records:** the mode, the class, the seed, the resume stage, `IRandom.Capture()`, `RunState.PlayerHp`, `RunState.PlayerShield`, `RunState.Time`, and `IClock.UtcNow`. HP and shield are absolute, not fractions — `RunState` exposes `PlayerShieldFraction` today and a fraction cannot be restored without the maximum that produced it, and that maximum will move the moment M3's tree exists. The new read is narrow (AR §18.2): `Combat` stays `internal`.
5. **Each capture happens before anything draws for the stage it describes, and that is the whole of ledger row 1's payoff.** M2-10 rule 10 recomposes the wave plan when the boundary is *crossed* — after `Clear`, `Gate` and `Transition` — so a capture on entering `Clear` is upstream of every draw the resumed run will make, and the opening capture is upstream of the opening stage's composition for the same reason (M2-05 rule 13 composes after `RunStarted`). A run resumed from either therefore composes byte-identical waves, spawns at the same positions, and (M3) offers the same three nodes. **Nothing may draw randomness from a `RunStarted` or `StageCleared` handler**: a draw there would land between the publish and the capture, so the resumed run would start one draw further on than a continuous one. M2-10's `Tick_DrawsNoRandomOutsideComposition` is the assertion from the other side; the rows below are this one.
6. **A finite mode that has just completed writes nothing.** `IsModeComplete` (M2-10 rule 14) means the run is over and the session is about to end it; a snapshot of a finished run is a resume into a stage the mode does not have. Inert in V1 because Descent is endless, and written anyway for M2-10 rule 14's reason.
7. `RunRecorder.Take` allocates nothing: `RunSnapshot` and `RandomState` are `readonly struct`s, `Capture` allocates nothing (M2-13a), and the event carries the DTO by value. It is not on a `Tick` path — it runs twice a minute at most — but the rule is cheap to keep and the alternative is a boundary frame that also swaps an arena doing a heap allocation.

**Who writes**

8. **`SaveWriter` is the only thing in the app that awaits `ISaveStore`, and a failed write is logged and swallowed.** A phone with a full disk, a revoked permission or a storage volume that vanished mid-write must not end the player's run — the run is still perfectly playable, it just will not survive a kill. **Rejected: surfacing it to the player**, which needs a dialog and a `LocKey`, and M6-10 owns the first of those.
9. **Death deletes the run: `SaveWriter` subscribes to `PlayerDied` and calls `ClearRun`** — AR §10.3's *"deleted on death"*. **`PlayerDied` and not `RunEnded`**, deliberately: `RunEnded` is also published when `RunScope` is disposed, which is every ordinary exit from the Run scene, and the first "quit to menu" button (M8-02) would silently start deleting saved runs the day it lands.
10. **Writes are serialised.** A local file write is sub-millisecond and the two write points are a stage apart, so today they cannot overlap; they are chained onto the previous task regardless, because ADR-0007's `SyncingSaveStore` is a network write over the same port and the day two of them overlap is the day one stage's snapshot lands after the next one's. Chaining also makes `Dispose` honest: it drops the subscriptions and **does not await** — an abandoned write either completed or did not, and M2-13b rule 4's atomic move means the file is never half of either.
11. **`SaveWriter` observes every fault.** A `Task` that faults with nobody awaiting it surfaces later as an unobserved-exception log with no stack that points anywhere useful — the worst possible shape for a bug that only reproduces on a device. Every chained continuation reads the antecedent's exception and logs it once, naming the operation.

**The clock**

12. **`RunRecorder` holds the `IClock`, and `Soulvail.Core.Run` still does not.** AR §18.2's rule — no clock in the session, simulated time is the sum of each tick's `Dt` — is about the *simulation* reading wall-clock, and a save stamp is not simulation. `RunSession` takes a `RunRecorder`, not an `IClock`, so M2-01's reflection assertion still holds; it is **widened here from `RunSession`'s constructors to every type in the `Soulvail.Core.Run` namespace**, because an invariant that names one class is one refactor away from being decorative, and this is the task that gives it something to be tempted by.

## Tests

| Test | Given / When / Then |
|---|---|
| `Take_RecordsTheRun` | stage 3 cleared, HP 84, shield 12, time 190.5 s, mode and class set / `Take(state, 4)` / one `RunSnapshotTaken` whose fields all match, `StageIndex` 4 (rules 3, 4) |
| `Take_StampsTheWallClock` | a `FixedClock` at a known instant / `Take` / `WrittenAt` is that instant, `RunTime` is the simulated seconds — two different numbers, neither derived from the other (rule 4, M2-01 rule 1) |
| `Take_CapturesEveryStream` | draws taken on all five streams / `Take` / `Snapshot.Random` equals `random.Capture()` at that moment (rule 4) |
| `Take_VersionIsCurrent` | / `Take` / `Version == RunSnapshot.CurrentVersion` |
| `Take_AllocatesNothing` | warm-up / 10 000 × `Take` / allocated-bytes delta == 0 (rule 7) |
| `Session_TakesAtTheOpening` | a run started at stage 1 / `Start` / exactly one `RunSnapshotTaken`, `StageIndex` 1, published after `RunStarted` (rules 1, 3) |
| `Session_OpeningStageIsTheConfigsStage` | a run started at stage 7 / `Start` / `StageIndex` 7, not 1 (rule 3) |
| `Session_OpeningPrecedesComposition` | a `FixedRandom` scripted to a known sequence / `Start` / the captured state is the generator's position **before** the opening stage was composed (rule 5) |
| `Session_TakesOnEnteringClear` | a stage whose last body dies this tick / `Tick` / one further `RunSnapshotTaken`, published in the same tick as `StageCleared` (rule 1) |
| `Session_TakesOncePerStage` | in `Clear` / `Tick` ×200 through `Clear`, `Gate` and `Transition` / no further snapshot (rule 1) |
| `Session_TakesAgainAtTheNextBoundary` | two stages cleared / — / three snapshots in all, `StageIndex` 1, 2, 3 (rules 1, 3) |
| `Session_DoesNotTakeOnALaterArrival` | a boundary crossed into stage 2's `Arrival` / — / no snapshot at the arrival; the one for stage 2 was written at the previous `Clear` (rule 1) |
| `Snapshot_PrecedesEveryDrawForTheNextStage` | as above / clear a stage, then cross the boundary / the state in the snapshot equals the generator's position **before** the recompose drew anything (rule 5) |
| `Resume_RecomposesIdenticalWaves` | run A cleared to stage 4; a second run seeded and `Restore`d from A's snapshot / compose stage 4 in both / identical wave plans, entry for entry (rule 5 — the row ledger row 1 exists for) |
| `Started_HandlerDrawing_IsRefused` | a `RunStarted` subscriber that draws once, a `FixedRandom` that throws on a draw before composition / `Start` / throws, naming the draw (rule 5) |
| `Cleared_HandlerDrawing_IsRefused` | a `StageCleared` subscriber that draws once, the same guard during `Clear` / clear a stage / throws, naming the draw (rule 5) |
| `Session_ModeCompleteWritesNothing` | mode final 3, stage 3 cleared / `Tick` / `IsModeComplete` true and no further `RunSnapshotTaken` (rule 6) |
| `Writer_SavesWhatCoreAnnounced` | a fake store / publish `RunSnapshotTaken(x)` / one `SaveRun`, argument equals `x` (rule 8) |
| `Writer_OpeningWriteReplacesAStaleRun` | a store holding a stage-12 snapshot / start a fresh run at stage 1 / `LoadRun` is the new one before the first boundary (rule 2 — the whole reason the opening write exists) |
| `Writer_ClearsOnPlayerDied` | a saved run / publish `PlayerDied` / one `ClearRun`, no `SaveRun` (rule 9) |
| `Writer_IgnoresRunEnded` | a saved run / publish `RunEnded` / **no** `ClearRun` — rule 9's whole point, pinned |
| `Writer_FailedSaveDoesNotThrow` | `FailNextWrite()` / publish a snapshot / nothing throws, one logged error, the writer still works on the next snapshot (rules 8, 11) |
| `Writer_FailedClearDoesNotThrow` | a store that faults `ClearRun` / publish `PlayerDied` / no throw, one logged error |
| `Writer_ObservesEveryFault` | a faulting store, then a forced GC and finalisation / — / no unobserved-task exception reaches the handler (rule 11) |
| `Writer_SerialisesTwoSnapshots` | a store whose first save never completes, two snapshots published / — / the second `SaveRun` has not been called until the first completes, then it is, in order (rule 10) |
| `Writer_DisposeDropsSubscriptions` | disposed / publish a snapshot and a `PlayerDied` / no store call, no throw (rule 10) |
| `Writer_DisposeDoesNotAwait` | a save in flight / `Dispose` / returns immediately, no deadlock (rule 10) |
| `Run_NoTypeTakesAClock` | reflection over every type in `Soulvail.Core.Run` / — / no constructor parameter of type `IClock` (rule 12, widening M2-01) |
| `Recorder_MayTakeAClock` | reflection over `RunRecorder` / — / it does — the exception rule 12 states, asserted so the widening above cannot be read as a ban on the recorder too |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play. Before doing anything else, look in `Application.persistentDataPath`: `run.json` already exists and says `"stageIndex": 1`. That is rule 1's opening write, and it is what makes rule 2 true.
2. **[Editor]** Clear stage 1 and stop at the door. The file now says 2 — it appeared *at the door* rather than after it, which is the beat GD §7.3 cares about. Clear stage 2: it says 3, and there is still exactly one file.
3. **[Editor]** Die. `run.json` is gone (rule 9). Start another run and quit to the Editor without clearing anything: the file describes *that* run at stage 1, not the one before it (rule 2).
4. **[Editor]** Make the folder read-only and clear a stage. The Console logs one error naming the write, the run keeps playing, and the door still opens — rule 8, which is the one failure mode that must not be allowed to matter.
5. **[device]** Whether the write is invisible at a boundary that is also swapping an arena behind a 0.3 s fade, on a real filesystem. Deferred with the rest of the device list.

## Out of scope

- **Reading any of it back** — M2-14b. Nothing in this PR loads a snapshot, and the file it writes is verified by looking at it.
- **The `Continue` button, `RunConfig.Restore`, `PendingRun.Clear`** — M2-14b, which owns ledger row 10.
- **A mid-stage write on app pause.** The owner ruled at M2-00e that a resume restarts the stage whole; the free-heal that implies is the ordinary checkpoint one, and closing it needs an Android lifecycle callback that nothing outside the Editor has ever run. Named in PROGRESS's device list rather than built blind.
- **Live enemies, waves, telegraphs and projectiles.** A boundary has none by construction — M2-10 rule 10 clears both systems — which is exactly why this is the write point and mid-stage is not.
- **The arena id.** Derived from the seed and the depth (M2-11a rule 3), so saving it would be a second source of truth for something arithmetic already answers.
- **Level, XP, the tree** — M3. The first of them to exist adds a field and a migration, which is what M2-13b's chain test is for.

## As built

Built as specified in shape: a recorder in core, an event, one listener on the Unity side, and two write points. **Nine deviations, three that change a decision** (1, 2 and 3). Every behaviour rule and every test row shipped as written except where noted.

**The three that change a decision**

1. **Rule 1's "after the state is built" and rule 5's "upstream of every draw" cannot both hold for the opening write, and rule 5 won.** The spec's parenthetical — *"M2-05 rule 13 composes after `RunStarted`"* — is not true of the merged code: M2-02 moved the opening composition **into `Start`'s validation block, before `RunStarted`**, so that an ineligible mode throws with nothing announced (ledger row 3), and AR §18.1 pins it there. `StageFlow.Begin` then *adopts* that composition rather than making one (M2-10's own deviation). So the position the opening snapshot must carry is the position at the **top of `Start`**, while the snapshot cannot be *announced* until after `RunStarted` — which is after the composition. One moment reads the dice, another reports them, and they are thirty lines apart.

   **`RunRecorder.Take` therefore has two overloads**, not the one the Public API sketch shows: `Take(state, resumeStage)` captures at the call (the boundary, where the two moments coincide) and `Take(state, resumeStage, in RandomState)` takes a position the caller pinned earlier (the opening). `RunSession.Start` holds a local `RandomState opening = _random.Capture();` above the composition. Resolved towards **Behaviour** per the spec's own precedence, and towards rule 5 rather than rule 1 because rule 5 is the payoff ledger row 1 was carried forward for: capturing after the composition compiles, round-trips, and deals a run killed in its first stage a *different* first stage on resume — invisible to every test that does not compare two compositions.

2. **`Started_HandlerDrawing_IsRefused` could not be written, because under that ordering the hazard does not exist.** The rule's reason is that a draw *"would land between the publish and the capture"*; the capture now precedes the publish, so a `RunStarted` handler that draws lands after it, and a resumed run replays the identical sequence of operations from the identical position. The row shipped as **`Started_HandlerDrawing_DoesNotMoveTheSnapshot`** — a drawing `RunStarted` subscriber, asserting the opening snapshot's captured state is unchanged. A test that asserted a throw would have been asserting a guard that should not exist. **`Cleared_HandlerDrawing_IsRefused` shipped exactly as specified** and is real: the boundary capture is taken immediately *after* `StageCleared` is published, so that window genuinely spans a handler. It is armed for the clearing tick and proves both halves — an ordinary clearing tick draws nothing, and a subscriber that draws once throws naming the stream.

3. **`RunTicker` gained a constructor parameter it does not keep, and it is not in the Files table.** Registering `SaveWriter` in `RunInstaller` makes it *resolvable*, not *constructed* — VContainer builds a scoped type lazily, and nothing depends on one. The opening snapshot is published from inside `RunSession.Start`, which `RunTicker` calls from *its* `Start`, so a writer that had not been constructed by then would miss the write that rule 2 exists for. AR §18.1's rule is explicit — *"a subscriber that must hear the opening of a run subscribes from a constructor on the dependency chain, never from a `Start` of its own"* — and `EnemyViews`, `ProjectileViews` and `TelegraphRings` are already on that constructor for the same reason. It is taken and discarded with `_ = saveWriter ?? throw`, not stored: a field assigned and never read is a warning, and the comment says why the parameter is there.

**The six that do not**

4. **The `RunSession_TakesNoClock` widening is in `Tests/Core/AssemblyPurityTests.cs`, not in `FixedClockTests`** as the Files table says. That file's own remarks had already named M2-14a as the task that would widen it — *"the array is the seam"* — and the row is now `Run_NoTypeTakesAClock`, a sweep over every type declared in `Soulvail.Core.Run` in the loaded assembly rather than a literal list, so a type added tomorrow is covered without anyone remembering. `Recorder_MayTakeAClock` sits beside it and also asserts the recorder's namespace, because *"outside `Soulvail.Core.Run`"* is what lets the sweep be a namespace instead of a list with an exception in it.

5. **`Tests/PlayMode/FrameOrderTests.cs` is a twelfth ripple file.** The table's ripple row names `Tests.Core` fixtures; the compiler found one more, because that fixture constructs a `RunTicker` directly. It needed an `ISaveStore`, and `Soulvail.Tests.PlayMode` does not reference `Soulvail.Tests.Core` — so the store is a nested eight-line `InertSaveStore` rather than a borrowed `InMemorySaveStore`. Opening an assembly boundary to save eight lines is the wrong trade.

6. **`Take_RecordsTheRun` is two rows.** The spec's single row wants HP, shield and time all distinct and all non-default, and a run cannot be in that state without being played into it: a fresh run has HP equal to its maximum, and one that has been hit has no Aegis left. So `Take_RecordsTheRun` asserts every field of a full-health run — and, crucially, that `PlayerShield` is **12 rather than the fraction 1.0**, which is the trap rule 4 names — while `Take_RecordsAWoundedRun` puts an Executioner next to the player and asserts both numbers are reads of what is *left*. Neither row could make the other's claim.

7. **`Session_OpeningPrecedesComposition` starts at stage 2, not 1**, and the reason is a real property of the composer rather than a fixture convenience: at a stage that *introduces* an archetype, `WaveComposer` buys it **before any draw** (M2-04 rule 5), so a one-Husk stage 1 composes without touching the Spawn stream at all — and the row's second half, *"and the composition did draw"*, would have been false against perfectly correct code. At stage 2 there is no introduction, so the buy goes through the draw.

8. **Three rows beyond the table, all of them the template's implied guards:** `Ctor_NullDependency_Throws` on the recorder, `Take_NullState_Throws` (both overloads), and `Take_StageBelowOne_Throws` — which deliberately leans on `RunSnapshot`'s own guard rather than adding a second copy of "stages are numbered from 1". `RunSessionTests.Ctor_NullDependency_Throws` gained the recorder row for the same family; `SaveWriterTests` gained `Writer_ThrowingStoreDoesNotThrow`, because a store can fail *at the call* as well as at the await and a writer that only handled faulted tasks would let that one into the frame.

9. **Three existing rows asserted the exact event sequence at `Start` and had to absorb the new one.** `RunSessionTests.Start_OrderUnchanged` and `EnemySystemTests.RunSession_Start_PublishesRunStartedBeforeSpawns` now expect `RunStarted` → `RunSnapshotTaken` → `EnemySpawned`; `Start_EventPublishedBeforeIsRunningFlips` captured the *last* publish through a callback that overwrote on every event, so it now keeps the first — a row about ordering that reported whatever happened last had stopped being about ordering.

**Two things worth knowing that are not deviations.** `SaveWriter` chains with `ExecuteSynchronously` on `TaskScheduler.Default` rather than `await`: with the local adapter every task is already completed, so the whole chain runs inline instead of being posted to Unity's synchronisation context — which an EditMode test never pumps. That is what makes the writer's rows synchronous instead of coroutines. And the chain **never faults**: each link catches its own operation and completes normally, so `Finish` reading `task.Exception` is the one place a fault is observed and the link behind it has nothing left to observe — which is what makes rule 11 checkable rather than a habit.

**Verified:** **1035 EditMode passed, 0 failed, 0 skipped, 15.6 s** — run twice, 19.5 s and 15.6 s, identical counts. M2-13b's 1001 plus **34 new rows**, every one green: 20 in `RunRecorderTests`, 11 in `SaveWriterTests`, `Recorder_MayTakeAClock`, the recorder's null row in `RunSessionTests`, and `Run_NoTypeTakesAClock` replacing its narrower predecessor. **Zero compile errors, zero new analyzer warnings**, `git diff ProjectSettings/` **empty**, and **no asset, prefab or scene was touched at all**. The two `ArenaView.OnValidate` Console warnings are the pre-existing import-order pair. **PlayMode is 9/11 and 8/11 across two runs, and both failures are pre-existing**: `FrameOrderTests`' known intermittent `ConeReport` rows (PROGRESS → Known issues, `m_AutoSyncTransforms` still 0) failed two rows then one, which is the intermittency that entry describes; the second run additionally failed two `BootSmokeTests` rows on an unhandled `[Log]` from Unity's AI Assistant package (`NoSubscription`), which is the Editor's environment rather than the game. **This session did not re-baseline PlayMode against `dev`** — the evidence for "pre-existing" is the intermittency across two runs on one tree, the verbatim match to the recorded assertion, and a diff that touches nothing in the physics or ordering path.
