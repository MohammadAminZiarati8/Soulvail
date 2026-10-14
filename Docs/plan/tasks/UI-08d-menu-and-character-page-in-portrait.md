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

**Built.** `Menu.unity`'s `Background` is now the canvas's first child, not `Safe`'s, so it stretches over the whole canvas and draws behind everything. The Menu's `CanvasScaler` reads 1080 × 1920, match 0.5. `MenuPresenter` and `CharacterPage` each gained `Refit(width, height, dpi, safeArea)`, called from `LateUpdate`. It lays out when any of the four values, or the px per dp they give on the canvas, differs from the last call, and returns without touching a rect when none does. `LateUpdate` is used because `CanvasScaler` and `SafeAreaFitter` take the frame's screen in `Update`. The menu's `OnEnable` now goes through `Refit`. The page's `Layout` reads its own rect. When the page holds the stack with a 120 dp preview (`PreviewMinDp`, 478.5 dp in all), it stacks: band, role, preview, strip, caption, dots, button row, deed. The preview takes what is left and never less than 120 dp. When the page is shorter than that and wider than tall, it lays out in three columns under the band: the preview between its arrows, the strip, then dots over the button row over the deed. At the look pass's 886 × 320 dp the preview is 170 × 140 dp. `MenuLayoutTests` has three new rows and `CharacterPageTests` has four new tests (eight rows). The phones are worked out from px and dpi: 411 × 914, 360 × 800 and 886 × 320 dp.

**Deviations.**
1. **`ClassSelect.prefab` has no `CanvasScaler`.** It is nested under the Menu's canvas, so the Menu's scaler is the one it draws by, and the table's "likewise" changed nothing.
2. **The page has a landscape layout, which rule 3 does not describe.** Rule 5 asks the page to fit without overlap at 2658 × 960 @ 480 dpi. The page is 248 dp tall there, and the stack needs 478.5. No stack fits, so the short, wide page uses columns. At 1920 × 1080 @ 400 dpi, which no row runs, the columns leave the preview 52 dp wide. It is narrow, but nothing overlaps. The game is portrait-only.
3. **`Refit` also compares the px per dp**, which is the four values plus the canvas's scale. `CanvasScaler` can take its first reading a frame after the menu's `OnEnable`, and the four values alone would never notice.
4. **`MenuLayoutTests`' `Layout` helper now sizes the canvas as well as `Safe`**. `Menu_FitsPortrait`'s background check now measures the canvas, which the helper makes the same size as the area. One assertion message changed.
5. **No `Capture Look`.** No Device Simulator was opened, because it edits `Packages/manifest.json` and the Game view would have to close. Manual steps 1 and 2 are the owner's.

**Stale beside this.** `ClassSelectPresenter` still places its chrome and sizes the preview's texture only on open and on a step. That is a [parking-lot](../ROADMAP.md#parking-lot) line. The ROADMAP's UI-08c box read `☐☑` and now reads `☑`.

**Judgement calls for the owner.** How the columns look on a landscape screen, if one ever shows the page. Whether the Ranger reads well in a 224 × 370 dp preview on a 360 dp phone.
