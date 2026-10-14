# UI-07d — The tree's labels and centring

**Size:** S · **Depends on:** UI-07a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** — · **Ledger rows:** none

## Goal

On a landscape phone the tree screen's branch names are whole and the class block sits in the middle of the frame.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeViewPresenter.cs` | Game | the block centred in the frame's content rect; the card kept below the labels |
| `Tests/Game/Presentation/TreeLayoutTests.cs` | Tests.Game | lays the screen out at the look pass's size and at 16:9 |
| *small edits* | | `TreeBlockView.cs`, if the branch labels' rects are placed there |

## Behaviour

1. **The class block is centred horizontally** in the popup's content rect: its left and right margins differ by 1 dp or less. The look pass found it in the left 55 %, the right side empty.
2. **The branch names (Draw, Volley, Stride) are not clipped**: each label's rect ends 8 dp or more above the detail card's top edge, and each label's preferred width fits its rect.
3. **Nothing in the popup overlaps**: ribbon, points pill, Close, block, labels, card, Learn.
4. **All three hold at 2658 × 960 @ 480 dpi** (320 dp tall) **and at 1920 × 1080 @ 400 dpi.** The rows call the layout with the px per dp that device would have, not the Editor's `Screen.dpi` ([Traps §9](../../Traps.md#9-rendering-canvas-and-tmp)).

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_Block_IsCentred` | rule 1, both sizes |
| `Tree_BranchNames_ClearTheCard` | rule 2, both sizes |
| `Tree_NothingOverlaps` | rule 3, both sizes |

## Manual verification

1. **[Editor]** UI-07b's capture of the tree at both sizes: three whole names, the block centred.

## Out of scope

Portrait (UI-08e), the font (UI-07c), new art.

## As built

_Filled at merge._
