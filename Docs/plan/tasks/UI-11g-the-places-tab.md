# UI-11g — The Places tab

**Size:** M · **Depends on:** UI-11b, UI-11e · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §3, GD §4.5 · **Ledger rows:** none

## Goal

The Places tab is a scrolling column of every listed place: open ones show their progress and can be picked, and locked ones say which place opens them.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/PlaceList.cs` | Game | the column: one card per listed place, then the "more to come" card |
| `Tests/Game/Presentation/PlaceListTests.cs` | Tests.Game | the rule rows |
| *small edits* | | `Menu.unity`: the list under the Places tab; `English.asset`: `ui.places.title`, `ui.places.finished`, `ui.places.locked`, `ui.places.soon` |

## Behaviour

1. **One card per `PlaceUnlocks.Listed` place, in that order**, full width and 140 dp tall.
2. **An open card** shows the name and either "Best: stage {0} / {1}" (best of final) or nothing yet. A finished place adds `ui.places.finished` ("Finished") in `Palette.Essence`. **The picked place** wears the `Palette.Player` rim.
3. **A locked card** is tinted `Palette.NodeLocked` and reads `ui.places.locked` ("Finish {0} to open"), naming its `UnlockedBy` place. Tapping it does nothing and plays no pressed state.
4. **Tapping an open card** calls `ProfileStore.Pick` with the current class and this place, and shows home.
5. **The column ends with one `ui.places.soon` card** ("More places coming"). It is greyed, cannot be tapped, and names no place.
6. **It fits 9:20 at both sizes.**

## Tests

| Test | Given / When / Then |
|---|---|
| `List_OneCardPerListedPlace_ThenSoon` | rules 1, 5 |
| `OpenCard_ShowsBestOfFinal` · `OpenCard_Finished_ShowsGoldLabel` · `OpenCard_Picked_HasRim` | rule 2 |
| `LockedCard_NamesOpener_NotTappable` | rule 3, with a test-only second mode opened by the Jungle |
| `OpenCard_Tap_PicksAndGoesHome` | rule 4 |
| `List_FitsPortrait` | rule 6 |

## Manual verification

1. **[Editor]** `Capture Look`: the Jungle card shows the best stage and the "More places coming" card sits under it.

## Out of scope

- A picture per place: UI-11h.
- The next place's content, which is what turns the chain into more than one card.

## As built

Filled in the PROGRESS entry of 2026-10-15; deviations listed there. `PlaceList` builds its scroll area and cards in code on the first show (backdrop `Palette.Ink`, one full-width 140 dp card per listed place, the viewport ending above the tab bar). A locked or "soon" card is a non-interactable `Button` whose disabled colour is white, so it keeps its dressed tint and plays no pressed state. The list raises `Tapped` only for an open place; `MenuPresenter` saves the pick with the current class and returns home.
