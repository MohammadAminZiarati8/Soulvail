# UI-03d — The switch: pause door, the Jungle on, the docs

**Size:** S · **Depends on:** UI-03c · **Branch:** `ui-03d-the-switch-and-the-docs`
**Design refs:** CH §5.1, GD §13.1 · **Ledger rows:** none

## Goal

The Jungle plays the new levelling flow end to end, the pause menu opens the same tree screen, and the docs say so.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/FreePickModeTests.cs` | Tests.Game | the shipped flags and the pause door |
| *small edits* | | `Data/Modes/Jungle.asset` (`_hasFreePick: 1`); `PausePresenter` (View Tree hands over to the tree screen, with the badge count); `Docs/Characters.md` §5.1; `Docs/GameDesign.md` §13.1 |

## Public API

None new. `PausePresenter` calls `TreeViewPresenter.Open` / is called back on `Close`.

## Behaviour

1. **The Jungle has the flag on; Descent keeps it off.** Descent's classes have 12-node trees not yet reshaped into 3 × 3, and class select offers the Ranger alone, so Descent stays on the random-three flow until those classes are reshaped.
2. View Tree from the pause menu, in a free run: the menu releases `Menu`, the screen takes `Tree` in the same call, so `RunPause` never holds two and the run never ticks between them.
3. Closing a screen opened from the menu reopens the menu (it takes `Menu` back). Closing one opened from the star returns to play.
4. In a free run View Tree shows the point count in `Palette.Available`, hidden at 0. Not free, View Tree opens today's read-only view, unchanged.
5. CH §5.1 and GD §13.1 describe banked points: a level banks a point, play goes on, the tree screen pauses and spends it, and Overflow comes only once nothing is open. The random-three text stays under a "Descent, until its classes are reshaped" note, because that path still ships.
6. The docs list Pacts, Reroll, Banish and the half-tree splash as off while free, not removed.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_HasFreePickOn` | load asset / to spec / `HasFreePick` |
| `Descent_HasFreePickOff` | rule 1 |
| `ViewTree_Free_HandsOverThePause` | menu open / tap / `Holder == Tree`, menu hidden, no tick in between |
| `Close_FromMenu_ReturnsToMenu` | rule 3 |
| `Close_FromStar_ReturnsToPlay` | rule 3 |
| `ViewTree_ShowsPoints_HidesAtZero` | 2, then 0 points / rule 4 |
| `ViewTree_NotFree_Unchanged` | Descent / tap / read-only view |

Rules 5 and 6 are document edits and have no row.

## Manual verification

1. **[Editor]** Jungle, Ranger: level 1 → 4 without a stop, open the tree from the star and from Pause, take three nodes, close each way, quit, Continue: points and nodes match, Console clean.
2. **[device]** Deferred with the rest (PROGRESS).

## Out of scope

- Taking a whole new class; reshaping the other three classes; Descent's flag.
- Ruling on the M7-04a–k specs this design supersedes: the owner's.

## As built

**Built to the Files table plus the edits below; 7 rows in `FreePickModeTests`, one per row of the table. Deviations:**

1. **Branched off `ui-03c-the-tree-screen`**, since `origin/dev` held none of UI-03a–c when this began.
2. **`TreeViewPresenter.OpenHoldingPause(Action)` is new public API** (the spec said none). `Open()` is now `OpenHoldingPause(null)`. The pause panel needs a door that takes `Tree` *and* runs a return callback; `Open(Action)` holds no pause and stays for a drawing run and the level-up screen.
3. **The hand-over lives in `PausePresenter.OpenTree`**: in a free run it resumes `Menu`, calls `OpenHoldingPause(OnFreeTreeClosed)`, and takes `Menu` back at once if the screen refused to open. `OnFreeTreeClosed` re-pauses `Menu` only while the panel is still open and not retired, so `PausePresenter.Close` (death, Resume) closing the tree does not re-raise a pause. A drawing run takes the old `Open(OnTreeClosed)` path unchanged.
4. **Rule 4's count is on the View Tree button's label, not in the tree screen's header** (which already shows "Points: n"). New key `ui.pause.tree.points` ("View Tree ({0:0})"), tinted `Palette.Available`, the authored colour back at 0; rewritten only when the number changes. The Pseudo row was hand-typed one dot long and `Pseudo_IsTheGeneratorsOutput` caught it.
5. **"No tick in between" is asserted as holder and `timeScale`**, since EditMode has no ticker: both calls are inside one tap handler, so no frame separates them.
6. **Docs:** CH §5.1 is now "Levelling: banked points, and the tree screen" with the random three under a "Descent, until its classes are reshaped" sub-heading; GD §13.1 gains the free-pick paragraph and keeps the old text under the Descent label. Pacts, Reroll, Banish and the half-tree splash are listed as off while free.

