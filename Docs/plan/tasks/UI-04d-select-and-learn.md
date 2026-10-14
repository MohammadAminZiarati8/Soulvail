# UI-04d — Select, then Learn

**Size:** M · **Depends on:** UI-04c · **Branch:** `ui-04d-select-and-learn`
**Design refs:** CH §5.1 · **Ledger rows:** none

## Goal

A tap selects a node and the card under the block describes it; the green Learn button spends the point. A tap alone no longer takes anything.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeDetailCard.cs` | Game | `Card_Stone` with a ribbon title, the tile and icon, "Level n of m", the description and the Learn button |
| `Tests/Game/Presentation/TreeDetailCardTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `TreeViewPresenter` (`OnCellTapped` selects; `Learn` takes; selection cleared on `Close`); `TreeNodeButton` (shows `TreeTile_SelectedRing`); `TreeView.prefab`; strings (`ui.tree.learn` "Learn", `ui.tree.level` "Level {0:0} of {1:0}", `ui.tree.pick` "Tap a skill to see it", `ui.tree.learned` "Learned") |
| *ripple* | | `TreeScreenTests.Tap_AvailableWithPoint_Takes` and `Tap_LockedTakenOrNoPoint_SendsNothing` become select rows; the take rows move to Learn |

## Public API

```csharp
// TreeViewPresenter: ContentId Selected { get; }
// TreeDetailCard (MonoBehaviour): void Show(SkillSpec spec, Sprite icon, NodeState state, int tier, int tiers, bool canLearn, ILocalizer localizer);
//                                 void ShowHint(ILocalizer localizer);  void Bind(Action learn);
```

## Behaviour

1. **A tap on any cell selects it**, in any state and any run; it sends no command. The selected cell wears `TreeTile_SelectedRing`; one cell at most is selected.
2. **Opening the screen selects nothing**; the card shows `ui.tree.pick`. `Close` clears the selection.
3. **The card** shows the selected node's name and tier numeral on `Popup_TitleRibbon`, its tile and icon as UI-04c draws them, "Level *tier* of *tiers in its branch*", and its description. No stat diff.
4. **Learn** is `Button_Green` (pressed: `Button_Green_Pressed`) with "Learn", `Icon_SkillPoint` and "1", interactable only when the selected node is Available and a point is banked. Otherwise it is `Button_Grey`, not interactable; on a Taken node it reads `ui.tree.learned`.
5. **Learn calls `TakeNode(Selected)`** once and redraws; the selection stays on the node, now Taken. Points are read after `TakeNode` returns.
6. **A drawing run** selects and shows the card, and has no Learn button.
7. The card fits under the block at 9:20 with the frame and block of UI-04b and UI-04c; text clears 12 sp; Learn's hit area is at least 48 dp tall.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tap_Selects_SendsNothing` | Available cell, 1 point / tap / `Selected` = it, ring shown, 1 point, not taken |
| `Tap_Another_MovesRing` | rule 1 |
| `Open_SelectsNothing_ShowsHint` | rule 2 |
| `Close_ClearsSelection` | rule 2 |
| `Card_DescribesSelected` | Fleet Foot II / title, "Level 2 of 3", description |
| `Learn_EnabledOnlyAvailableWithPoint` | available at 1 and 0, locked, taken / green; grey, grey, grey "Learned" |
| `Learn_TakesOnce_KeepsSelection` | 1 point / Learn / taken, 0 points, selection kept, Learn grey |
| `DrawingRun_NoLearn` | rule 6 |
| `Card_FitsPortrait` | 9:20 / card and Learn inside the frame, no overlap with the block, ≥ 12 sp, Learn ≥ 48 dp |

## Manual verification

1. **[Editor]** Jungle, Ranger, one point: tap Fleet Foot I; ring and card appear, nothing taken. Learn; the tile turns Taken, the pill reads 0, Learn goes grey. **[device]** a mistaken tap no longer costs a point.

## Out of scope

- The stat diff line ("Speed +8% → +16%"), the capstone, a second class block (owner's scope, 2026-10-13). Confirm dialogs, respec, animation.

## As built

As specified, with three differences.

1. **`TreeDetailCard.Show` takes a trailing `bool free = true`** beside the spec's signature. Rule 6 needs the card to know the run draws; `canLearn` alone cannot say "no button at all".
2. **Every shown tile is interactable.** `TreeNodeButton` reports a tap in any state and any run (rule 1), so `IsTakeable` now drives only the plus badge. The `NotFree_IsReadOnly` and `Tree_DrawingRun_SendsNothing` rows drop their "not interactable" assertion and check the card has no Learn instead.
3. **Rows moved, not just added:** `Tap_AvailableWithPoint_Takes` became `Learn_AvailableWithPoint_Takes` and `Tap_LockedTakenOrNoPoint_SendsNothing` became `Learn_LockedTakenOrNoPoint_SendsNothing`; both, and the take steps in `TreeBlockTests`, `TreeFrameTests` and the header row, go through a `Take` helper (select, then Learn). The nine spec rows are in `TreeDetailCardTests.cs`.

Unspecified numbers: the card is 176 dp tall (`TreeDetailCard.HeightDp`, inside UI-04c's 190 dp band), its ribbon 220×40 dp, Learn 168×48 dp. The title is "Name" plus the tier numeral. Pseudo-locale regenerated for the four new keys.

Not done: a look at the card in the Editor or on a phone. `Card_FitsPortrait` pins 9:20 only.
