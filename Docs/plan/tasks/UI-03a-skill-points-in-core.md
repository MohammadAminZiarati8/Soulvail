# UI-03a — Skill points and the free pick, in core

**Size:** M · **Depends on:** — · **Branch:** `ui-03a-skill-points-in-core`
**Design refs:** CH §5.1 (superseded, owner 2026-09-29), CH §5.2, AR §18.2 · **Ledger rows:** none

## Goal

Behind a mode flag, a level banks a point and opens nothing; a command spends a point on any open node, with no draw.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Progression/FreePickTests.cs` | Tests.Core | one row per rule |
| *small edits* | | `ModeSpec` (`hasFreePick = false`, beside `hasSanctum`); `ModeDefinition` (`_hasFreePick`, default off); `LevelUpFlow`; `RunState`; `IProgressionCommands` + `RunSession`; `RunTicker.LevelUpPhase` (calls `SettleFullTree`) |
| *ripple* | | the `IProgressionCommands` fakes in `ResumeFlowTests`, `SanctumPresenterTests`, `FrameOrderTests` |

## Public API

```csharp
// ModeSpec: bool HasFreePick { get; }    RunState: bool IsFreePick { get; }  int SkillPoints { get; }
// IProgressionCommands:
int SkillPoints { get; }   // PendingLevelUps while free with a tree; else 0. 0 outside a run.
void TakeNode(ContentId id);
void SettleFullTree();     // no-op unless free and nothing is available
```

## Behaviour

1. Flag off changes nothing; both shipped modes keep it off until UI-03d.
2. While free, `IsLevelUpPending` and `IsSplashPending` are false: no offer opens and nothing is drawn from `Offers`. A level only raises `PendingLevelUps`, which `run.json` v4 already holds.
3. `SkillPoints` is `PendingLevelUps` while free with a tree, else 0.
4. `TakeNode(id)`: `SkillTree.Take(id)`, then `SpendLevelUp`, then `SkillRunner.Add` for an Active (`Choose`'s order).
5. `TakeNode` throws `InvalidOperationException` outside a run, when not free, or at 0 points. A node the tree refuses throws from `Take` before the point is spent.
6. `SettleFullTree` spends every point as Overflow (CH §5.2, publishes `OverflowGranted`) when no node is available; otherwise nothing. `RunTicker.LevelUpPhase` calls it each frame, where it calls `OpenLevelUp` today.
7. While free, `CanBuy(Reroll)` is false and `BanishableInto` writes 0: both act on offers, which a free run never has.
8. Resume keeps banked points and the Overflow sum `Level − 1 − Taken − Pending`.

## Tests

| Test | Given / When / Then |
|---|---|
| `NotFree_IsUnchanged` | default mode / level / `IsLevelUpPending` true |
| `Free_LevelUp_OpensAndDrawsNothing` | two levels / frame / no offer, `Offers` position unchanged |
| `SkillPoints_FollowPending_ZeroWithoutTree` | rule 3 |
| `TakeNode_Open_TakesAndSpendsOne` | 2 points / tier 1 / taken, 1 left |
| `TakeNode_Locked_ThrowsKeepsPoint` | tier 2 untaken / take / throws, points kept |
| `TakeNode_NoPointOrNotFree_Throws` | rule 5 |
| `TakeNode_Active_ReachesRunner` | rule 4 |
| `Settle_NothingAvailable_SpendsAsOverflow` | full tree, 2 points / 2 overflow, 0 points |
| `Settle_NodeAvailable_SpendsNothing` | rule 6 |
| `Free_RerollAndBanish_Refused` | rule 7 |
| `Resume_KeepsBankedPoints` | rule 8 |
| `TakeNode_AllocatesNothing` | `AllocationAssert.None` |

## Out of scope

- Every view (UI-03b, c); the Jungle's flag (UI-03d).
- Removing `Open`, Pacts or the Sanctum: they stay, behind the flag.
- Taking a whole new class; while free the half-tree splash is simply off.

## As built

_Filled at merge, 6 000 bytes or fewer._
