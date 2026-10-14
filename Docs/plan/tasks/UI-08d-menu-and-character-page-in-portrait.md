# UI-08d — The menu and the character page in portrait

**Size:** M · **Depends on:** UI-08a, UI-07c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.1 · **Ledger rows:** none

## Goal

The menu and the character page fit a portrait phone, fill the notch strip with their background, and lay out again whenever the screen changes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/MenuPresenter.cs` | Game | re-lays out on a size or dpi change (finding 5) |
| `Game/Presentation/CharacterPage.cs` | Game | stacks the page for portrait; the preview never gets a negative height (finding 1) |
| `Tests/Game/Presentation/MenuLayoutTests.cs` | Tests.Game | rows added |
| `Tests/Game/Presentation/CharacterPageTests.cs` | Tests.Game | rows added |
| *small edits* | | `Menu.unity`: `Background` moved out of `Safe` to the full canvas (finding 4); `CanvasScaler` 1080 × 1920; `ClassSelect.prefab`'s scaler likewise |

## Behaviour

1. **The menu's background covers the full canvas**, notch strip included, as the class page's already does; the title and buttons stay inside `Safe`.
2. **`MenuPresenter` and `CharacterPage` lay out again** when `Screen.width`, `Screen.height`, `Screen.dpi` or the safe area changes, checked once a frame against the last values and doing nothing when none changed. Today the menu lays out once, on enable.
3. **The character page stacks top to bottom in portrait**: name band, role label, the preview, the skill strip, its caption, the one button. **No two of them overlap**, and the caption clears the role label.
4. **The preview's rect has a height of at least 120 dp at every size the tests run**; when the stack does not fit, the preview shrinks first and nothing else does. A negative height — the look pass's blank Ranger — cannot occur.
5. **Both screens fit 1080 × 2400 @ 420 dpi and 720 × 1600 @ 320 dpi**, and the page still lays out without overlap at 2658 × 960 @ 480 dpi (finding 1's size), preview included.

## Tests

| Test | Given / When / Then |
|---|---|
| `Menu_Background_CoversTheFullCanvas` | rule 1 |
| `Menu_ScreenChanges_LaysOutAgain` | rule 2 |
| `Menu_ScreenUnchanged_DoesNotLayOut` | rule 2 |
| `Page_Portrait_StacksWithoutOverlap` | rule 3, both portrait sizes |
| `Page_Preview_NeverUnder120Dp` | rule 4, all three sizes |
| `Page_LookPassSize_Fits` | rule 5 |
| `Page_ScreenChanges_LaysOutAgain` | rule 2 |

## Manual verification

1. **[Editor]** UI-07b's captures at the three sizes: the Ranger on the pedestal in each, the notch strip painted on the menu.
2. **[Editor]** Rotating the Simulator's device while on the menu re-lays it out with no relaunch.

## Out of scope

New art, a second class, and the tree, Pause and RunEnd (UI-08e).

## As built

_Filled at merge._
