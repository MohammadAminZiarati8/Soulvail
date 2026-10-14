# UI-08e — The tree, Pause and Run end in portrait

**Size:** M · **Depends on:** UI-08a, UI-07c, UI-07d · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** — · **Ledger rows:** none

## Goal

The three overlay screens a Jungle run shows — the tree, Pause and Run end — fit a portrait phone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/TreeViewPresenter.cs` | Game | the card below the block, both full-width, in portrait |
| `Game/Presentation/PausePresenter.cs` | Game | its column sized from the safe width |
| `Game/Presentation/RunEndPresenter.cs` | Game | its column sized from the safe width |
| `Tests/Game/Presentation/TreeLayoutTests.cs` | Tests.Game | the portrait rows added to UI-07d's fixture |
| `Tests/Game/Presentation/OverlayLayoutTests.cs` | Tests.Game | Pause and Run end |
| *small edits* | | `TreeView.prefab`, `Pause.prefab`, `RunEnd.prefab`: `CanvasScaler` 1080 × 1920 |

## Behaviour

1. **Each screen's canvas is referenced 1080 × 1920**, match 0.5.
2. **The tree in portrait:** the popup takes the safe width less 16 dp a side; the class block above, the detail card below it, Learn under the card. UI-07d's rules hold — the block centred, the branch names 8 dp clear of the card, nothing overlapping.
3. **Pause and Run end** lay out as one centred column no wider than the safe width less 16 dp a side; every button 48 dp tall or more and inside the safe area.
4. **All three fit 1080 × 2400 @ 420 dpi and 720 × 1600 @ 320 dpi**; text clears 12 sp.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_Portrait_StacksBlockCardLearn` | rule 2, both sizes |
| `Tree_Portrait_KeepsUi07dRules` | rule 2, both sizes |
| `Pause_Portrait_FitsTheSafeWidth` | rule 3, both sizes |
| `RunEnd_Portrait_FitsTheSafeWidth` | rule 3, both sizes |
| `Overlays_Canvas_IsReferencedPortrait` | rule 1 |
| `Overlays_Portrait_TextClears12Sp` | rule 4 |

## Manual verification

1. **[Editor]** UI-07b's captures of each screen at both sizes.

## Out of scope

The Pause and Run-end art (UI-09), and the screens switched off in the focus build (Sanctum, Skills, Splash, LevelUp).

## As built

**Built.** The three prefabs' `CanvasScaler`s read 1080 × 1920, match 0.5. `TreeViewPresenter` gained `IsPortrait`, which is true when the safe area is taller than it is wide. In portrait the frame's margin is 16 dp and the full card keeps the bottom. Both block and card take the content width. `FitBlockToContent` runs after `Arrange` and shrinks the block's rect to the lowest active child it drew, then centres it between the header and the card. Without it, a 914 dp phone leaves about 330 dp of empty stone between the plate and the card. `PausePresenter.LayOutColumn` stacks the shown buttons in one column centred on the safe area. The column is min(safe − 32, 320) dp wide, buttons are 56 dp tall with 12 dp gaps, and labels are 22 dp. It runs on every open and when View Tree comes or goes. The panel is the safe area's sibling, so the safe rect is carried across through world corners. `RunEndPresenter.LayOut` runs in `Show`. It lays out one centred column: a 40 dp title, three 36 dp rows (word left 60 %, number right) and a 56 dp Return. The column is min(safe − 32, 360) dp wide. Pause and RunEnd each gained `UsePixelsPerDp`, UI-07d's seam. `TreeLayoutTests` adds two portrait rows, and UI-07d's rows now share helpers. `OverlayLayoutTests` has four rows. Both phones are 1080 × 2400 @ 420 and 720 × 1600 @ 320.

**Deviations.**
1. **`TreeFrameTests.cs` is a sixth file**, approved by the owner. UI-04b's `Frame_FillsSafeAreaLessMargin` held 12 dp on a portrait screen, which rule 2 replaces with 16. It now opens at its landscape 914 × 411 dp, so the 12 dp margin is still guarded. The portrait 16 dp is held by `Tree_Portrait_StacksBlockCardLearn`.
2. **"Learn under the card" is Learn at the foot of the card, under its description.** Learn belongs to `TreeDetailCard`, which is outside the table. UI-07d's overlap row keeps it inside the card, and the full card already draws it there.
3. **The block is centred in its band, not hung right under the header.** Rule 2 says only "above". Centring keeps the tree and the card together on a tall phone.
4. **The overlay rows lay the screens out without injecting them.** Neither column reads anything `Construct` hands in. The shipped English words are written onto the labels, because `Start` does not run in EditMode.
5. **No `Capture Look`.** No Device Simulator was opened, because it edits `Packages/manifest.json`. Manual step 1 is the owner's.

**Judgement calls for the owner.** Whether a 22 dp Pause column reads well over the arena. Whether the Oathbound's two-per-tier tiles (about 28 dp at 360 dp wide) are too small. The Ranger's tiles are 63 dp.
