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

**Built to the Files table plus the edits below; 16 rows in `TreeScreenTests` (the table's 12, and `Star_Free_OpensAtZeroPoints`, `Cells_NoPoint_NoOutline`, `Header_ShowsPointsLeft`, `Back_Closes` for the rules it gave no row). Deviations:**

1. **Branched off `ui-03b-the-point-badge`**, since `origin/dev` held neither UI-03a nor UI-03b when this began.
2. **`TreeNodeButton` is `TreeNodeView` moved, GUID and all** (`git mv` of the `.cs` and its `.meta`), so the cell template kept its four references. `NodeState` moved with it, from `Soulvail.Game.Controls` to `Soulvail.Game.Presentation`. **It keeps `Banished`**, the spec's three plus one: a drawing run still has the Sanctum's Banish. `SkillId` is now `Node`; `Show` gained a `takeable` flag; `Bind(Action<ContentId>)` and `SizeText` are new.
3. **Two ways in.** `Open()` is the star's and holds `PauseReason.Tree`. `Open(Action)` stays for the pause panel's and the level-up's View Tree and holds none, as before. `Close` resumes `Tree` only when it holds it. The tree screen and the pause panel share one `RunPause` in `TreeViewPresenterTests` now, as they do in the scope.
4. **The star's gate is in `HudPresenter`**: a tap opens the screen only when `RunState.IsFreePick` and `TreeViewPresenter.HasTree`. The tree screen reaches the HUD the way it reaches the pause panel: a `_treeScreen` override in `Run.unity`, since the two are separate root prefabs. The star is a `Button` (transition None) on the existing `LevelStar`, which was already a raycast target; the level text over it is not one.
5. **Android Back is an `InputAction` on `<Keyboard>/escape`**, created on first show, enabled only while the screen is up and disposed in `OnDestroy`. That keeps `Tree_BuildsOnceOnOpen`'s "no per-frame method" true, and `InputAdapter` is untouched. `TreeScreenTests` is an `InputTestFixture` for that one row.
6. **Rule 9 needed the chrome in dp, not only the cells.** The prefab authored the header, the column tops and the labels in a 1920×1080 landscape canvas's units, which at 9:20 ran Close and Points past the safe area (the first run of `Layout_FitsPortrait` failed on exactly that). `PlaceChrome` places them at runtime in dp, before `Layout`. The header is 40 dp tall with a 12 dp margin, Close is 112 dp and Points 160 dp, and the columns hang from the header's bottom edge. Text: header 18 dp, branch names 14, node name 13, description 12. All are guesses until a device, and all clear the 12 sp floor, which the row asserts.
7. **Locked's padlock is `Icon_Placeholder`** in the cell's top-right corner, and the "Available" outline is a UGUI `Outline` (4 units, `Palette.Available`, on only while takeable). The owner's art replaces both.
8. **The key is `ui.tree.points` ("Points: {0:0}")**, beside the existing `ui.tree.close`, not the spec's `tree.points`/`tree.close`. Pseudo is regenerated from English by the menu item. The re-serialisation noise the Editor wrote into `English.asset` was reverted by hand.
9. **Rows edited that the table did not name:** `RunPauseTests.Reason_HasAllFourMembers` → `Reason_HasAllFiveMembers`; `SanctumPresenterTests.Picker_IsNotATreeCell` drops its "the cell has no `Button`" assert; `PaletteTests.Readers` and `TableLocalizerTests`' two cell lists swap the type name. In `TreeViewPresenterTests`, `Tree_SendsNothing` became `Tree_DrawingRun_SendsNothing` (rule 7 as a state: no cell interactable, every tap sends nothing), and `Prefab_IsDressed` checks the three new cell fields.
10. **Tier 1 is at the bottom**: cells anchor to the column's bottom edge and stack up, so every column's tier *t* shares a row whatever the column's height.
