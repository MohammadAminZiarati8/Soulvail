# MK-03 — The tree screen shows a path

**Size:** M · **Depends on:** MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (*The tree screen*); UI style: Clash-like, colour law (cyan = player)

## Goal

The tree draws **one icon per skill with rank pips**, the Ranger row at the bottom and the path above
it in level rows; before the choice, the path area shows three cards and lets the player choose.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeBlockView.cs` | Game | Rewritten: rows of skill icons, not a node per rank |
| `Game/Presentation/TreeNodeButton.cs` | Game | One skill: icon, pips, lock + level, cyan edge for actives |
| `Game/Presentation/PathCardsView.cs` | Game | **New.** Three cards, preview, Choose |
| `Game/Presentation/TreeViewPresenter.cs` | Game | Wires rows, cards, detail card |
| `Prefabs/UI/TreeView.prefab` | prefab | Layout |
| `Tests/Game/Presentation/TreeViewPresenterTests.cs` | Tests.Game | Rows below |
| *small edits* | | `TreeDetailCard` (rank N/M, next rank's text, Learn takes the next rank), `English`/`Pseudo` (`ui.tree.path.*`, `ui.tree.coming-soon`, `ui.tree.unlocks-at`) |

## Behaviour

1. **A skill is one icon.** Its pips = its rank count; filled pips = ranks taken. The Ranger's three
   skills form the bottom row (Quick Draw, Volley, Fleet Foot).
2. **Learn** on the detail card takes the skill's next rank through `TakeNode`; it is enabled only
   when a point is banked and that rank is available. A maxed skill shows "MAX".
3. **Path rows**: one row per distinct unlock level, lowest at the bottom, labelled "Lv N"; a row
   above the current level shows a padlock and "Lv N" on each icon and its Learn is disabled.
4. **Actives** get a cyan edge; passives a neutral one.
5. **Before the choice** the path area holds three cards from `CharacterDefinition.PathSlots`: name,
   one-line description, its skill icons in a strip. An empty slot draws "Coming soon", not tappable.
   Below `PathOpensAtLevel` every card shows "Lv 11" and Choose is disabled.
6. Tapping a card previews its rows greyed above the Ranger row; **Choose** (enabled while
   `IsPathChoiceOpen`) sends `ChoosePath`; on `PathChosen` the cards go and the chosen path's rows
   stay.
7. The whole tree fits one portrait screen at 360 × 780 without scrolling (Ranger row + four path
   rows + header); at 411 × 914 likewise.
8. Pausing, the points pill and Close behave as before.

## Tests

| Test | Given / When / Then |
|---|---|
| `Skill_PipsMatchRanks` | Steady Aim 2 of 5 / five pips, two filled (1) |
| `Learn_TakesTheNextRank` | (2) |
| `Learn_DisabledWithoutPoint` / `Learn_DisabledWhenMax` | (2) |
| `Row_AboveLevel_LockedWithLevel` | L12, Eagle Eye at 14 / padlock, "Lv 14", Learn off (3) |
| `Active_HasCyanEdge` | Volley / cyan; Quick Draw / neutral (4) |
| `Cards_ThreeSlots_TwoComingSoon` | (5) |
| `Cards_BelowLevel_ChooseDisabled` | L10 (5) |
| `Choose_SendsChoosePath` / `Chosen_CardsGo_RowsStay` | (6) |
| `Fits_360x780` | layout / every icon inside the safe area, none overlap (7) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 start at stage 12: tree shows the Ranger row and three cards (two "Coming soon");
   choose Marksman; Steady Aim row (Lv 11) learnable, Eagle Eye (Lv 14) locked until level 14.
2. **[Editor]** `Soulvail/Capture Look` at 411 × 914 and 360 × 780: nothing clipped.

## Out of scope

- The battle HUD (MK-04). Final icon art.

## As built

_Filled at merge._
