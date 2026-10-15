# UI-11d — The profile hears a finish, and Run end says so

**Size:** M · **Depends on:** UI-11a, UI-11c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §4.4 · **Ledger rows:** none

## Goal

Every run's deepest stage and every finished place are written to `profile.json`. A finished run's end screen says "Jungle cleared" and names the place it opened, rather than reading like a death.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/PlaceWriter.cs` | Game | `ShardWriter`'s shape: hears `ShardsAwarded` and `ModeFinished`, writes through `ProfileStore` |
| `Game/Presentation/RunEndPresenter.cs` | Game | the finished title and the "opened" line |
| `Tests/Game/Adapters/PlaceWriterTests.cs` | Tests.Game | rules 1–3 |
| `Tests/Game/Presentation/RunEndFinishTests.cs` | Tests.Game | rules 4–5 |
| *small edits* | | `RunScope` registers `PlaceWriter`; `English.asset` gains `ui.runend.finished`, `ui.runend.opened` |

## Behaviour

1. **`ShardsAwarded` raises the best stage** for its `ModeId` to `DeepestStage`, through `WithBestStage`.
2. **`ModeFinished` adds the place** to `FinishedModeIds`, and raises its best stage to `FinalStage`.
3. **One save per run end.** Both events arrive in one tick, so the writer folds them into a single `ProfileStore.Save`, not one save per event.
4. **A finished run's title is `ui.runend.finished`** ("{0} cleared!", the place's name), drawn in `Palette.Essence`, because gold means rewards. A death keeps today's title.
5. **When the finish opened a place** (a listed mode whose `UnlockedBy` is this one), a second line reads `ui.runend.opened` ("New place: {0}"). With the Jungle alone, no line is drawn.

## Tests

| Test | Given / When / Then |
|---|---|
| `ShardsAwarded_RaisesBest` · `ShardsAwarded_Lower_KeepsBest` | rule 1 |
| `ModeFinished_AddsFinished_AndBest` | rule 2 |
| `FinishTick_SavesOnce` | rule 3 |
| `RunEnd_Finished_ShowsClearedTitle` · `RunEnd_Death_KeepsTitle` | rule 4 |
| `RunEnd_FinishOpensPlace_ShowsLine` · `RunEnd_NothingOpened_NoLine` | rule 5 |

## Manual verification

1. **[Editor]** Set the Jungle's final stage to 2, play it through: the end screen reads "Jungle cleared!", `profile.json` lists `mode.jungle` as finished with best stage 2, and Continue is gone. Set it back to 30.

## Out of scope

- A celebration (confetti, sound): UI-09's art and the audio ruling.

## As built

_Filled at merge._
