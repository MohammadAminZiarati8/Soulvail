# UI-11c — Finishing a place, in core

**Size:** S · **Depends on:** UI-11b · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §4.4, M2-10 rule 14 · **Ledger rows:** none

## Goal

A run that clears its place's final stage is paid like a death and says "this place is finished", so the profile and the run-end screen can hear it.

Today `RunSession` calls `End()` on `StageFlow.IsModeComplete` and nothing else. No shipped mode was finite, so nothing noticed that a finished run paid no Shards.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Events/RunEvents.cs` | Core | `ModeFinished` |
| `Core/Run/RunSession.cs` | Core | the completion branch publishes |
| `Tests/Core/Run/RunSessionFinishTests.cs` | Tests.Core | the rule rows |

## Public API

```csharp
public readonly struct ModeFinished
{
    public ModeFinished(ContentId modeId, int finalStage);
    public readonly ContentId ModeId;
    public readonly int FinalStage;
}
```

## Behaviour

1. **On `IsModeComplete`, the order is `ShardsAwarded` → `ModeFinished` → `RunEnded`**, in one tick. `ShardsAwarded` uses the death branch's arguments, so the payout is `ShardPayout.For` at the final stage.
2. **A death publishes no `ModeFinished`**, including a death on the final stage before its last body drops.
3. **No snapshot is taken** after the final clear. This is the existing rule, now pinned by a test: Continue never offers a finished run.
4. **An endless mode never publishes `ModeFinished`.**
5. **Allocation:** the completion branch allocates only what the death branch already does (the new-archetype array).

## Tests

| Test | Given / When / Then |
|---|---|
| `FinalClear_PublishesShardsThenFinishedThenEnded` | a 2-stage mode, stage 2 cleared / tick / the three events in order, `FinalStage` 2 |
| `FinalClear_PaysAsDeathWould` | rule 1, the total equals `ShardPayout.For(2, …)` |
| `DeathOnFinalStage_NoFinished` | rule 2 |
| `FinalClear_TakesNoSnapshot` | rule 3 |
| `Endless_NeverFinished` | rule 4, 40 stages cleared |

## Out of scope

- Writing the profile (UI-11d) and what the screen says (UI-11d).
- A finish bonus on top of the death payout: tuning, M8-05.

## As built

Built to the Files table, no deviations.

The death branch's payout moved into a private `RunSession.PublishPayout()`, which the completion branch also calls, so the two cannot disagree about the arithmetic (rule 1) and the completion branch allocates exactly what the death branch does (rule 5). The new `ModeFinished` carries `State.ModeId` and `State.StageIndex`, which the flow leaves at the final stage. `ShardsAwarded`'s doc comment now names the finish as its second publisher.

`RunSessionFinishTests` plays a real session through a flat-scaled two-stage mode (so the endless row's stage 40 is the same fight as stage 1). `FinalClear_TakesNoSnapshot` counts `RunSnapshotTaken` after the stage-2 arrival and again after the end.

Suite: 4 135 EditMode, 4 133 passed, 1 failed (the known D3D12 depth row), 1 inconclusive. Nothing writes `FinishedModeIds` yet; that is UI-11d.
