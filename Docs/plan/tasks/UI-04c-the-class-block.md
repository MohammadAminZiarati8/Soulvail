# UI-04c — The class block: tiles, badges, links and the plate

**Size:** M · **Depends on:** UI-04b · **Branch:** `ui-04c-the-class-block`
**Design refs:** CH §5, GD §16.4 · **Ledger rows:** none

## Goal

The class's tree draws as the mockup's block: a stone panel, a tile per node with its icon, badge and tier numeral, links up each branch, "LV n" down the side, and the class plate under it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeBlockView.cs` | Game | the panel, the row labels, the links and the class plate; the presenter hands it the tree and the states |
| `Game/Presentation/TreeNodeButton.cs` | Game | **rewritten**: tile, icon, badge, numeral; the name and description text leave the cell |
| `Tests/Game/Presentation/TreeBlockTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `TreeViewPresenter` (`Build`, `Layout`, `Draw` go through the block; branch names under the columns); `TreeView.prefab`; `CharacterLook` gains `Sprite Emblem`, authored for the Ranger only; the nine Ranger skill assets get their branch's icon (`Icon_QuickDraw`, `Icon_Volley`, `Icon_FleetFoot`); strings (`ui.tree.row` "LV {0:0}") |
| *ripple* | | `TreeScreenTests` and `TreeViewPresenterTests` rows that read a cell's name, description, `Outline` or padlock now read its tile and badge; `PaletteTests` if a colour is added or dropped |

## Public API

```csharp
// TreeBlockView (MonoBehaviour, block namespace — Traps §5)
public void Show(SkillTreeSpec tree, Func<ContentId, NodeState> stateOf, ILocalizer localizer, Sprite emblem, LocKey className);
// TreeNodeButton.Show(SkillSpec spec, NodeState state, Sprite icon, int tier, bool takeable)
// CharacterLook: Sprite Emblem { get; }
```

## Behaviour

1. **One block**, `TreeBlock_Panel` sliced, holds the class's grid. The grid keeps UI-03c's shape: a column per branch, tier 1 at the bottom, a 12-node class in the same block.
2. **Tile by state:** Taken → `TreeTile_Taken`, Available → `TreeTile_Available`, Locked and Banished → `TreeTile_Locked`. The tiles are drawn untinted; the art carries the colour.
3. **Icon:** the node's `SkillIconBook` sprite, else `Icon_Placeholder`. Locked dims it (greyed, `Palette.NodeLocked`).
4. **Badge**, top-right of the tile: Taken → `TreeBadge_Check`, Available → `TreeBadge_Plus` **only while a point is banked**, otherwise none; Locked → `TreeBadge_Lock`. It replaces UI-03c's `Outline` and padlock.
5. **Numeral**, bottom-left: the node's tier in its branch, I, II, III, IV.
6. **Links:** between a node and the one above it in its branch, `TreeLink_Lit` when the lower node is Taken, else `TreeLink_Dark`, drawn behind the tiles.
7. **Row labels:** "LV n" left of each row, one per tier of the deepest branch.
8. **Branch name** under its column's tier-1 tile; **the class plate** (`ClassPlate`, sliced) below that, with the class name and, when the look has one, its emblem at the plate's left end. No emblem → the name alone, centred.
9. Tapping still behaves as UI-03c rule 4 until UI-04d; the redraw after a take updates tile, badge, icon and link.
10. Sizes are dp; the Ranger's block, header and a 190 dp card space below it fit 9:20 without scrolling. Text clears 12 sp.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tile_MatchesState` | one taken, one available, one locked / three tile sprites |
| `Tile_Banished_IsLocked` | drawing run, banished node / `TreeTile_Locked` |
| `Icon_FromBook_ElsePlaceholder` | Ranger, then a class without icons / branch icon, then `Icon_Placeholder` |
| `Icon_Locked_IsDimmed` | rule 3 |
| `Badge_ByStateAndPoints` | available at 1 and 0 points / Plus, then none; taken Check; locked Lock |
| `Numeral_IsTierInBranch` | Ranger / I, II, III up each column |
| `Link_LitWhenLowerTaken` | tier 1 taken / link 1→2 lit, 2→3 dark |
| `Rows_OnePerTier` | Ranger 3, Oathbound its deepest branch |
| `Plate_NameAndEmblem` | Ranger / emblem shown; a class with none / hidden, name centred |
| `Take_RedrawsTileBadgeLink` | rule 9 |
| `Block_FitsPortrait` | 9:20 / every tile, label and the plate inside the frame, ≥ 12 sp |
| `RangerSkills_HaveBranchIcons` | the nine assets / each has its branch's icon |

## Manual verification

1. **[Editor]** Jungle, Ranger: the block matches the mockup's lower block, minus the capstone. Take tier 1; its link lights and tier 2's badge turns to Plus. **[device]** tile size under a thumb.

## Out of scope

- The capstone star and its links, a second class block, other classes' emblems and icons (owner's scope, 2026-10-13). Selection and Learn (UI-04d).

## As built

_Filled at merge._
