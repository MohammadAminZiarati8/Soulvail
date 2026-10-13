# UI-03c — The tree screen: pause, look, take

**Size:** M · **Depends on:** UI-03a, UI-03b · **Branch:** `ui-03c-the-tree-screen`
**Design refs:** CH §5 (§5.1's read-only view superseded), AR §18.2 · **Ledger rows:** none

## Goal

In a free run, tapping the level star pauses and opens the tree as a grid; a banked point takes any open node.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeNodeButton.cs` | Game | **replaces** `TreeNodeView` on the cell template: the view plus a `Button` |
| `Tests/Game/Presentation/TreeScreenTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `TreeViewPresenter` (takes `IProgressionCommands` and `RunPause`; open, close, take); `PauseReason` (`Tree`); `HudPresenter` (star is a button); `TreeView.prefab`; strings (`tree.points`, `tree.close`) |
| *ripple* | | `TreeViewPresenterTests`: the rows pinning "no `Button`, no command port" are rewritten as rule 7; delete `TreeNodeView.cs` |

## Public API

```csharp
// PauseReason.Tree
// TreeViewPresenter: bool IsOpen { get; }  void Open();  void Close();
// TreeNodeButton: ContentId Node { get; }  NodeState State { get; }
public enum NodeState { Taken, Available, Locked }
```

## Behaviour

1. In a free run the star calls `Open()`; in any other it does nothing. **The screen holds its own pause**, `PausePresenter`'s pattern: `Open` calls `RunPause.Pause(Tree)` only when nothing holds the pause, otherwise it does not open; `Close` resumes `Tree`. It opens at 0 points too, to look.
2. Grid: a column per branch, a row per tier, **tier 1 at the bottom**; a tier's nodes sit side by side. The Ranger is the plain 3×3; a 12-node class draws in the same grid. Branch name heads each column.
3. A cell's state comes from core: `IsTaken` → Taken (lit), `IsAvailable` → Available, else Locked (dim, padlock). Available cells are outlined in `Palette.Available` while `SkillPoints > 0`.
4. Tapping an Available cell with a point calls `TakeNode(id)` and redraws. Any other tap sends nothing.
5. The header shows the points left and a Close button; Android Back closes.
6. Spending the last point leaves the screen open; `SettleFullTree` runs on the ticker as before.
7. Not free: the screen is today's read-only view — no cell is interactable and no command is sent.
8. A class with no tree shows `NoTreeKey`'s line and the star is inert.
9. Text is `LocKey`, sizes are dp, and the grid fits 9:20 without scrolling.

## Tests

| Test | Given / When / Then |
|---|---|
| `Star_Free_OpensAndPauses` | tap / `IsOpen`, `Holder == Tree` |
| `Star_NotFree_DoesNothing` | rule 1 |
| `Open_PauseHeld_DoesNotOpenOrThrow` | Menu holds / `Open` / closed, no throw |
| `Close_ReleasesTree` | open, close / not paused |
| `Grid_TierOneAtBottom` | Ranger / 3×3, tier 1 lowest |
| `Grid_TwelveNodeTree_Draws` | Oathbound / every node one cell |
| `Cells_MatchCore` | one taken / Taken, Available, Locked |
| `Tap_AvailableWithPoint_Takes` | 1 point / tap / taken, 0 points |
| `Tap_LockedTakenOrNoPoint_SendsNothing` | rule 4 |
| `NotFree_IsReadOnly` | rule 7 |
| `NoTree_LineAndInertStar` | rule 8 |
| `Layout_FitsPortrait` | 9:20 / every cell inside the safe area |

## Manual verification

1. **[Editor]** Jungle flag on locally, Ranger: level, tap the star, take tier 1, tier 2 opens. **[device]** cell size under a thumb.

## Out of scope

- The pause menu's door, the committed flag (UI-03d). Class cards, respec, animation.

## As built

_Filled at merge, 6 000 bytes or fewer._
