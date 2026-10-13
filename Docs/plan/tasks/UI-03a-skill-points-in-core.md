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

**Built to the Files table; 12 rows in `FreePickTests`, one PlayMode row beside them. Deviations:**

1. **`LevelUpFlow` owns the flag, `RunState` reads it.** `ModeSpec.HasFreePick` (`hasFreePick = false`, last) → `ModeDefinition._hasFreePick` → `LevelUpFlow(…, isFree)` via `RunSession.Start`. `RunState.IsFreePick` is `LevelUp is not null && LevelUp.IsFree`, so it takes **no new constructor argument** and is false for a class with no tree, which has no flow for a pick to be free of. `SkillPoints` follows (`IsFreePick ? PendingLevelUps : 0`).
2. **`IsLevelUpPending` and `IsSplashPending` are false while free; `LevelUpFlow.Open` also returns at once**, so a stray `OpenLevelUp` cannot spend a draw. `OpenSplash` is already guarded by `IsSplashPending`.
3. **`TakeNode` → `LevelUpFlow.Take(id)`**: `SkillTree.Take`, `SpendLevelUp`, `SkillRunner.Add` for an Active. Throws from `Take` before the point is spent, so a refused node keeps it. A stranger id throws `KeyNotFoundException` (the tree's), the rest `InvalidOperationException`. **Never a Pact:** a free run offers none.
4. **`SettleFullTree` → `LevelUpFlow.Settle()`**: asks `SkillTree.Available` into a buffer sized to the tree once at construction (empty for a drawing run), and if nothing is open spends every point through `GrantOne` — `OverflowGranted` per point. **A no-op outside a run**, where `OpenLevelUp` throws: the ticker asks it every frame and outlives the run. `RunTicker.LevelUpPhase` calls it as a third branch (`else if SkillPoints > 0`) after the splash and the level-up, not inside `IsLevelUpPending`'s branch, since that is false while free.
5. **Rule 7 is wider than the spec's sentence.** `CanBuy` is false for **Banish as well as Reroll** while free, `Buy(Reroll)` and `Banish(id)` throw (`FreePickRefusal`), and `BanishableInto` writes 0 — a screen reading `CanBuy` and `BanishableInto` offers neither, and the commands refuse what a stale screen sends. `RunState.ActsOnOffers` names the pair.
6. **`TakeNode_AllocatesNothing` measures two things**, because a node can only be taken once and `AllocationAssert.None` repeats its body: the per-frame path (`SkillPoints`, both pending reads, `SettleFullTree` with a node open) at the full 10 000 iterations, and `TakeNode` itself at `iterations: 1` — the warm-up takes one node and the measured call another. Through `SilentEvents`, since `RecordingEvents` boxes.
7. **One PlayMode row the table did not list**, `Frame_BankedPointIsSettledNotOpened`, so rule 6's "each frame" has a row in the file that owns the frame order; the three fakes in the ripple row gained the three members.
8. **`Descent.asset` and `Jungle.asset` gained `_hasFreePick: 0`.** `Descent_EveryYamlKeyBindsToAField` reserialises the asset and fails on a new serialized field the file lacks (Traps §7); the failed run wrote the key itself, and Jungle was given the same line by hand. Both modes are off, as the Goal asks.

**Not done, by the table:** every view (UI-03b, c), the Jungle's flag (UI-03d). `TreeViewPresenterTests` still asserts the tree screen holds no `IProgressionCommands` (CH §5.1); UI-03c reverses that row.

**Run:** the 12 rows green; full EditMode and the PlayMode row in the PROGRESS entry.
