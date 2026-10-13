# UI-04b — The tree screen's frame

**Size:** S · **Depends on:** UI-04a · **Branch:** `ui-04b-the-tree-frame`
**Design refs:** CH §5.1, GD §16.4 · **Ledger rows:** none

## Goal

The tree screen sits in a stone popup with a "Skill Tree" ribbon, a points pill and a round Close button, as in `UI_v2/mockups/SkillTree.png`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Presentation/TreeFrameTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `TreeViewPresenter.PlaceChrome` (the frame, ribbon, pill and Close in dp); `TreeView.prefab` (the four pieces); strings (`ui.tree.title`; `ui.tree.points` becomes the bare count); Pseudo regenerated |
| *ripple* | | `TreeScreenTests.Header_ShowsPointsLeft` reads the pill's count |

## Public API

None new.

## Behaviour

1. `Popup_Panel`, sliced, fills the safe area less a margin, over the dimmed run. Nothing of the tree draws outside it.
2. `Popup_TitleRibbon`, sliced, is centred on the frame's top edge and carries `ui.tree.title` ("Skill Tree") in Lilita One.
3. The points pill (`Pill_Dark`) sits right of the ribbon: `Icon_SkillPoint` and the count, `Palette.Available` while above 0, plain at 0. The count is read after `TakeNode` returns, as today.
4. Close is `Button_Close`, top-right inside the frame, and its hit area is at least 48 dp. Android Back still closes.
5. The pill shows in a drawing run too, with that run's 0.
6. Every size is dp, text clears the 12 sp floor, and frame, ribbon, pill and Close all sit inside the safe area at 9:20 and at 20:9.

## Tests

| Test | Given / When / Then |
|---|---|
| `Frame_FillsSafeAreaLessMargin` | 9:20 / open / panel rect = safe area inset by the margin |
| `Ribbon_ReadsTitleKey` | open / ribbon text = `ui.tree.title` resolved |
| `Pill_ShowsPointsAndTint` | 2 points, then 0 / colour Available, then plain |
| `Pill_UpdatesAfterTake` | 1 point, take / reads 0 |
| `Close_HitAreaAtLeast48Dp` | rule 4 |
| `Pill_DrawingRun_ShowsZero` | rule 5 |
| `Frame_FitsBothAspects` | 9:20 and 20:9 / the four pieces inside the safe area, no overlap, text ≥ 12 sp |

## Manual verification

1. **[Editor]** Jungle, Ranger: tap the star; the frame, ribbon, pill and Close match the mockup's header. **[device]** Close under a thumb.

## Out of scope

- The class block and cells (UI-04c), the selection and Learn (UI-04d). Any open/close animation.

## As built

_Filled at merge._
