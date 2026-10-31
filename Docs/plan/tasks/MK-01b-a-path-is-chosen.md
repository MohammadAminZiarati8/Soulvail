# MK-01b — A path is chosen from level 11

**Size:** M · **Depends on:** MK-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; AR §18.1 (`RunSession.Start` restore order)

## Goal

From the class's `PathOpensAtLevel`, the player may choose one of its paths, free and for the run;
the choice survives a resume, and banked points are never turned into Overflow while a path has room.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/PathFlow.cs` | Core | **New.** The choice: open, candidates, choose, derive, restore |
| `Core/Events/PathChosen.cs` | Core | **New.** `readonly struct PathChosen(ContentId PathId)` |
| `Core/Run/RunSession.cs` | Core | `ChoosePath`; restore; Overflow rule |
| `Tests/Core/Progression/PathFlowTests.cs` | Tests.Core | **New** |
| *small edits* | | `IProgressionCommands` (`ChoosePath`), `RunState` (reads below), `SplashFlow` (never opens for a class with paths), `LevelUpFlow` (full-tree test), `Docs/Invariants.md` §18.1 (restore order line) |
| *ripple* | | fakes implementing `IProgressionCommands` |

## Public API

```csharp
public sealed class PathFlow {
  PathFlow(SkillTree tree, ContentCatalog catalog, CharacterSpec character, IDomainEvents events);
  bool IsOpen { get; }                       // see rule 1
  IReadOnlyList<ContentId> Candidates { get; } // the class's PathIds the catalog has, in order
  ContentId ChosenId { get; }                // default until chosen
  void Choose(ContentId pathId);
  bool TryDerive(IReadOnlyList<ContentId> takenNodeIds, out ContentId pathId);
  void Restore(ContentId pathId); }          // silent: no event
// IProgressionCommands: void ChoosePath(ContentId pathId);
// RunState: ContentId PathId; bool IsPathChoiceOpen; IReadOnlyList<ContentId> PathCandidates;
//           int UnlockLevelOf(ContentId nodeId)
```

## Behaviour

1. `IsOpen` ⇔ the class has candidates, no path is chosen, and `LevelTracker.Level` ≥
   `PathOpensAtLevel`. It does **not** wait for the class's tree to be full (CH §3.5).
2. `Choose` installs the path (`TreeRules.InstallPath`, then `SkillTree.OnPathInstalled`), publishes
   `PathChosen` once, and spends no point. Refused (throws in `PathFlow`; ignored with no event
   through `ChoosePath`) when not open or the id is not a candidate.
3. Irreversible: after a choice `IsOpen` is false for the run.
4. **Not saved; derived** (`SplashFlow`'s bargain). `TryDerive` finds the path holding any taken id;
   `RunSession.Start` restores it **before** `SkillTree.Restore`. A run saved after choosing and
   before its first path pick resumes unchosen and may choose again. `RunSnapshot` stays v4.
5. A class with paths never opens `SplashFlow`.
6. **Overflow waits for room.** A level is paid as Overflow only when no node remains takeable at
   **any** level: before a path is chosen a class with candidates is never full; after, the run is full
   only when every class and path node is taken. A point with only level-gated nodes left stays banked.
7. `RunState.UnlockLevelOf` forwards `TreeRules.UnlockLevelOf`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Open_AtTheLevel_NotBefore` | opens at 11 / L10 closed, L11 open (1) |
| `Open_TreeNotFull_StillOpens` | 5 of 9 taken, L11 / open (1) |
| `Choose_InstallsAndPublishesOnce` | (2) |
| `Choose_SpendsNoPoint` | 2 banked / still 2 (2) |
| `Choose_NotACandidate_Ignored` / `Choose_Closed_Ignored` | via `ChoosePath` / nothing changes (2) |
| `Choose_Twice_Refused` | (3) |
| `Resume_DerivesThePath` | snapshot with a path node / installed before takes replay, no event (4) |
| `Resume_ChosenButUnpicked_AsksAgain` | (4) |
| `Splash_NeverOpensWithPaths` | (5) |
| `Overflow_NotBeforeAPath` | Ranger tree full at L10, L11–12 / points banked, Overflow 0 (6) |
| `Overflow_GatedNodesLeft_Banks` | only a Lv-20 skill untaken at L18 / banked (6) |
| `Overflow_AllTaken_Pays` | (6) |

## Out of scope

- The cards and screen (MK-03). Authoring (MK-02b).

## As built

_Filled at merge._
