# UI-11f — The Heroes tab

**Size:** M · **Depends on:** UI-11e · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** UI-05c · **Ledger rows:** none

## Goal

The Heroes tab is a grid of every class on the roster, locked ones included. A card opens the existing character page as its detail, and Choose there makes the class the pick instead of starting a run.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HeroGrid.cs` | Game | the grid: two columns, scrolling, one card per roster class |
| `Game/Presentation/ClassSelectPresenter.cs` | Game | opens on a given class; Choose picks and returns home |
| `Tests/Game/Presentation/HeroGridTests.cs` | Tests.Game | the rule rows |
| *small edits* | | `Menu.unity`: the grid under the tab bar's Heroes tab; `English.asset`: `ui.heroes.title`, `ui.heroes.picked` |

## Behaviour

1. **One card per `ClassSelectRoster` class, in roster order.** The roster is the focus switch, so today that is the Ranger alone, and the grid is built for twelve.
2. **An owned card** shows the class emblem and name. **The picked class** wears a `Palette.Player` rim and `ui.heroes.picked` ("Picked").
3. **A locked card** is the emblem drawn at 35 % in `Palette.NodeLocked`, with a lock and the existing price line (`DeedLine`, or the Shard price).
4. **Tapping any card opens the character page on that class.** Its arrows still step through the roster.
5. **On the page, Choose calls `ProfileStore.Pick`** with the class and the current place, closes the page, and shows home with the new class. It no longer starts a run: Play does that. Unlock keeps today's flow, and an unlocked class can then be chosen.
6. **It fits 9:20 at both sizes**, and the cards are at least 120 dp wide.

## Tests

| Test | Given / When / Then |
|---|---|
| `Grid_OneCardPerRosterClass_InOrder` | rule 1 |
| `Card_Picked_HasRimAndLabel` | rule 2 |
| `Card_Locked_GreyWithPrice` | rule 3 |
| `Card_Tap_OpensPageOnThatClass` | rule 4 |
| `Page_Choose_PicksAndReturnsHome_NoRun` | rule 5 |
| `Grid_FitsPortrait` | rule 6 |

## Manual verification

1. **[Editor]** With the roster widened to all four classes for the check, then put back: three locked cards show their prices, the Ranger is picked, and choosing another owned class changes the home card.

## Out of scope

- A second class's content, and putting the other classes back on the roster (owner's focus switch).

## As built

Filled in the PROGRESS entry of 2026-10-15; deviations listed there. `HeroGrid` builds its scroll area and cards in code on the first show (backdrop `Palette.Ink`, two columns, cards at least 120 dp wide, the viewport ending above the tab bar). The Heroes tab and the class card show it and light the Heroes tab; a card tap opens the class page on that class (`ClassSelectPresenter.Open(ContentId)`); Choose calls `ProfileStore.Pick` with `MenuPresenter.PickedMode`, closes the page and raises `Picked`, which the menu answers by hiding the grid, lighting Play and redrawing. `ClassSelectPresenter` no longer touches `PendingRun` or the scene loader: Play is the one method that starts a fresh run.
