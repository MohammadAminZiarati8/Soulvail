# UI-11e — The home screen and its tab bar

**Size:** M · **Depends on:** UI-11a, UI-11b · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.1 · **Ledger rows:** none

## Goal

The menu becomes a home screen. It shows the picked place and the picked class, Play starts a run with both in one tap, and a bottom tab bar reaches Heroes and Places.

**This supersedes UI-05/UI-06's "no tabs, no roster grid" brief**, by the owner's request of 2026-10-15 for a page of every character and a page of every place. One primary button per screen still holds.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/MenuPresenter.cs` | Game | the home layout, Play/Continue, and the picks resolved |
| `Game/Presentation/TabBar.cs` | Game | three tabs, one selected; raises `Selected(Tab)` |
| `Game/Adapters/ProfileStore.cs` | Game | `Pick(characterId, modeId)` — saves `WithSelection` |
| `Tests/Game/Presentation/HomeScreenTests.cs` | Tests.Game | the rule rows, on the real `Menu.unity` |
| *small edits* | | `Menu.unity`: place card, class card, tab bar; `ClassSelectPresenter.FirstModeId` removed (its caller moves to the picks); `English.asset`: `ui.menu.play`, `ui.tab.heroes`, `ui.tab.play`, `ui.tab.places`, `ui.home.best` |

## Public API

```csharp
public enum Tab { Heroes, Play, Places }
// TabBar: MonoBehaviour, block namespace
public event Action<Tab> Selected;
public void Show(Tab tab);
// ProfileStore
public void Pick(ContentId characterId, ContentId modeId);
```

## Behaviour

1. **The picks resolve, never fail.** The place is the profile's `SelectedModeId` if it is listed and open, otherwise the first listed open place. The class is `SelectedCharacterId` if it is on the roster and owned, otherwise the roster's first owned class. A fresh profile therefore plays the Jungle with the Ranger.
2. **The place card** (top half) shows the place's name and `ui.home.best` ("Best: stage {0}"), or nothing under it when the best is 0. Tapping it opens the Places tab.
3. **The class card** shows the class's `CharacterPreview` and name. Tapping it opens the Heroes tab.
4. **Play** (green, `ui.menu.play`) starts a run with the resolved place and class. **With a save, Continue is green and on top, and Play is blue** (UI-06b rule 3's shape, with Descend renamed).
5. **The tab bar** sits on the safe area's bottom edge, 64 dp tall, with three equal tabs. Play is selected on the home screen. Until UI-11h's art lands, a tab is `Button_Blue` and the selected tab is `Button_Green`.
6. **It fits 9:20 at 411 × 914 and 360 × 780 dp**: no overlap, text at least 12 sp, every tap target at least 48 dp.

## Tests

| Test | Given / When / Then |
|---|---|
| `Picks_Fresh_JungleAndRanger` · `Picks_LockedPlace_FallsBack` · `Picks_UnownedClass_FallsBack` | rule 1 |
| `PlaceCard_ShowsBest` · `PlaceCard_NoBest_NoLine` · `PlaceCard_Tap_OpensPlaces` | rule 2 |
| `ClassCard_Tap_OpensHeroes` | rule 3 |
| `Play_StartsWithPicks` · `WithSave_ContinueGreenOnTop` | rule 4 |
| `TabBar_OnSafeBottom_PlaySelected` | rule 5 |
| `Home_FitsPortrait` (two sizes, with and without a save) | rule 6 |

## Manual verification

1. **[Editor]** `Capture Look` at both phone sizes: the place card, the Ranger, Play and the tab bar, with nothing clipped by the notch or the home bar.

## Out of scope

- What the Heroes and Places tabs hold: UI-11f and UI-11g. Until they land, their tabs open the existing class page and do nothing, respectively.
- A Shop or Settings tab (M8-02).

## As built

Filled in the PROGRESS entry of 2026-10-15; deviations listed there. The home layout is dp up from the safe area's bottom: tab bar 0–64, buttons, class card 232–424, place card 440–548, title plate above. The class card shares the one `CharacterPreview` with the class page and stands its body again when the page closes.
