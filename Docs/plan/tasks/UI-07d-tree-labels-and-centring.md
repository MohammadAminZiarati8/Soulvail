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
| `Game/Presentation/TreeDetailCard.cs` | Game | the compact horizontal card (owner's ruling, 2026-10-14) |
| `Game/Presentation/TreeBlockView.cs` | Game | tighter padding and no plate when compact |

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

**The ruling.** The spec's stack cannot fit 320 dp: the 176 dp card left the block 28 dp (14 after `CardSpaceDp`) and the tiles overflowed upward, which is the clipped names. The owner ruled for a compact horizontal card (2026-10-14), which added `TreeDetailCard.cs` and made `TreeBlockView.cs` a real edit, not a "small" one.

**Built.** `TreeViewPresenter` goes compact when the safe area is under 480 dp tall (320 dp and 432 dp both are; portrait is not). Compact: card 88 dp (`TreeDetailCard.Layout(px, compact)`: 56 dp tile left; name ribbon, level and 12 sp description middle; 150 dp Learn right), card bottom 16 dp inside the frame margin, block 8 dp above it and 2 dp under the ribbon, `TreeBlockView.Compact` (4 dp pad, 18 dp branch labels, no class plate, 4 dp cell gap). At 320 dp the tiles come to about 30 dp. `UsePixelsPerDp(px)` is the seam rule 4 needs; `IsCompact` reads the mode. `TreeLayoutTests` (partial of `TreeScreenTests`, 3 rows) run both devices at 3.0 and 2.5 px per dp.

**Judgement calls.** The class plate is hidden in compact (no room); the branch-name row test writes the shipped names (Draw, Volley, Stride) because the fixture tree's keys are placeholders; rects are compared with half a unit of slack. "Centred" is measured on the block rect and its panel against the frame.

**Owed to the owner:** no Device Simulator was opened, so no `Capture Look` PNG. Capture the tree at both sizes and look at the compact card, the 30 dp tiles and the card against the frame's rim.
