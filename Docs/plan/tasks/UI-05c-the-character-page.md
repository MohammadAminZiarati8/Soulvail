# UI-05c — The character page

**Size:** M · **Depends on:** UI-05a, UI-05b · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** CH §3, §6 · GD §4.5, §16.1 · **Ledger rows:** none

## Goal

Class select is one class at a time: its 3D body, its name and role, a swipe to the next, and one button that is Choose, Unlock or dead.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/CharacterPage.cs` | Game | the page's view: name, role badge, arrows, dots, the preview image, the button and its price |
| `Game/Presentation/ClassSelectPresenter.cs` | Game | rewritten to drive one page by index; the start, buy and guard paths are kept as they are |
| `Prefabs/UI/ClassSelect.prefab` | — | rebuilt portrait: back button, shard balance, page, button |
| `Tests/Game/Presentation/CharacterPageTests.cs` | Tests.Game | one row per rule |
| `Tests/Game/Presentation/ClassSelectPresenterTests.cs` | Tests.Game | the card rows become page rows |
| *small edits* | | strings below; `ClassCard` stays, `SplashPresenter` still uses it |

## Public API

```csharp
// CharacterPage (MonoBehaviour):
//   void Bind(Action previous, Action next, Action primary);
//   void Show(ClassPageModel model, ILocalizer localizer);
//   int Index/Count are the presenter's; the page is told, never asks.
// ClassPageModel (readonly struct): CharacterSpec Spec, int Index, int Count,
//   PageButton Button (Choose | Unlock | Dead), int Price, string DeedLine, RoleKind Role.
```

## Behaviour

1. **One class on screen.** The presenter holds an index into the roster (`ClassSelectRoster`), starting on the first owned class; `Open` binds nothing else.
2. **Previous and next** step the index and redraw. They do not wrap; the arrow at an end is hidden. **With one class there are no arrows and no dots.** The dots count the roster and light the index.
3. **A horizontal swipe across the name band** steps like the arrows once it passes 48 dp. **A drag on the preview only turns the model** (`CharacterPreview.Rotate`) and never steps, so one gesture is never both.
4. **The preview shows the class's body** from `CharacterLookBook` through `CharacterPreview.Show`, and `Hide`s when the page closes.
5. **The name** is `CharacterSpec.NameKey`; **the role badge** follows `WeaponKind` (ranged, melee, caster) and draws nothing for a role whose sprite is not imported yet.
6. **The button.** Owned: green `Button_Green`, "Choose", starts the run once through the existing path (the double-tap guard is kept). Locked and the price payable: violet `Button_Violet`, "Unlock", the shard pill showing the price; the tap buys, redraws, and starts nothing (*buying is not picking*, M6-09b rule 8). Locked and not payable: `Button_Grey`, not interactable, the price or the deed line; **a screen may not offer what the model refuses.**
7. **A locked class shows `CharacterPage_LockBadge` beside its name.** The body is shown as it is.
8. **Back** is `Button_Close`, top-left, and closes without writing anything. **The balance** is `Icon_SoulShard` and a number, top-right, drawn on open and after a buy and on nothing else.
9. **It fits 9:20.** The page uses the safe area; text clears 12 sp; the button's hit area is at least 48 dp tall; nothing overlaps.
10. **More classes than the roster capacity** is not a case: the roster is a list, and the warning M5-07 wrote for cards goes with the cards.

New strings (English and pseudo-locale): `ui.classselect.choose` "Choose", `ui.classselect.unlock` "Unlock", `ui.classselect.role.ranged` "Ranged", `.melee` "Melee", `.caster` "Caster". `ui.classselect.title` is retired.

## Tests

| Test | Given / When / Then |
|---|---|
| `Open_ShowsTheFirstOwnedClass` | rule 1 |
| `Next_Previous_StepAndClampAtTheEnds` | rule 2, three fake classes |
| `OneClass_HasNoArrowsOrDots` | rule 2 |
| `Swipe_OnTheNameBandSteps_DragOnThePreviewRotates` | rule 3 |
| `Preview_ShowsTheClassBody_HidesOnClose` | rule 4 |
| `Header_NameAndRole` | rule 5 |
| `Choose_StartsTheRunOnce` | rule 6, double tap |
| `Unlock_BuysRedrawsStartsNothing` | rule 6 |
| `Locked_Unpayable_IsDead` | rule 6 |
| `Locked_ShowsTheBadge` | rule 7 |
| `Back_ClosesWritingNothing` | rule 8 |
| `Balance_OnlyOnOpenAndAfterBuy` | rule 8 |
| `Page_FitsPortrait` | rule 9: 9:20, ≥ 12 sp, button ≥ 48 dp |

## Manual verification

1. **[Editor]** Menu → Descend: the Ranger stands on the pedestal, idling; Choose starts a run. Open the Device Simulator at 9:20 and check nothing is clipped. **[device]** the drag to turn the model feels like a turn, not a stutter.

## Out of scope

- The skill strip: UI-05d. A second class's content: the roster handles it; nothing authors it here.
- The menu's own look: UI-06b.

## As built

As specified, with the differences below. Batch-mode EditMode: **4 036 run, 4 034 passed, 1 failed, 1 inconclusive**; the failure is not this task's (item 9). PlayMode `BootSmokeTests` (3) passed. Not seen in the Editor.

1. **Files beyond the table.** `MenuScope.cs` (item 5), `English.asset` and `Pseudo.asset` (item 3), and the rows the new screen broke: `PaletteTests` (`CharacterPage` is now an `Essence` reader), `TableLocalizerTests` (two retired keys), `RangerTests` (`Cards_FourFitTheMenu` deleted: it asserted four cards on a prefab that has none), `EmberwrightTests` (`Class_TheThirdCardIsBound…` became `Class_TheThirdClassIsReachedWithNoPrefabEdit`), and PlayMode `BootSmokeTests` (taps `CharacterPage.Primary`).
2. **`CharacterPage.Bind` takes a fourth, optional argument**, `Action<float> rotate`, so the drag reaches `CharacterPreview.Rotate` without the page knowing the preview. `ClassPageModel`, `PageButton` and `RoleKind` live in `CharacterPage.cs`.
3. **Strings.** Added `choose`, `unlock`, `role.ranged/.melee/.caster`. **Retired three, not one:** `title`, `back` and `balance`. The balance is now `Icon_SoulShard` and a bare number, and Back is the `Button_Close` icon with no label; `Sweep_EveryAuthoredAssetKeyIsAlreadyCovered` fails on a row nothing names. `Pseudo.asset` was regenerated by `LocalisationSweepTests.Regenerate`.
4. **The `ClassCard` rows stay**, on a card the fixture builds by hand (`NewCard`): the Splash screen still uses `ClassCard`, and its rows were reachable only through the old prefab.
5. **`CharacterPreview` is required in `MenuScope`** (it throws when `_preview` is unassigned, like the two presenters). `ClassSelectPresenter.Construct` gained two trailing optional parameters, `preview` and `looks`; the fixtures that build the screen by hand may omit them and get a page with no body.
6. **The page lays itself out in code**, in dp (`CharacterPage.Layout`, `ClassSelectPresenter.PlaceChrome`), `TreeDetailCard`'s way, so the prefab carries hierarchy and sprites and no authored pixel sizes. `CharacterPage_Pedestal` is not placed: the stage already has a 3D pedestal and a second one would stack. `Page_FitsPortrait` pins it at 966 × 2146 canvas units (9:20 at match 0.5): every piece inside the screen, no two overlapping, text at least 12 sp, button and arrows at least 48 dp.
7. **Dots are cloned from a template on demand and kept** (a roster is a list). `Select_InstantiatesNothingButDots` counts transforms over 100 reopens. The role is `Cone → Melee`, `Projectile → Ranged`; `Caster` has strings and no weapon kind yet, so it is never drawn.
8. **The profile is read on open and after a purchase only.** A step redraws from that read (`_drawn`), so `Balance_OnlyOnOpenAndAfterBuy` also holds across arrows. `OnCardChosen` and `OnUnlockTapped` keep their names and guards; `OnPrimary` picks between them.
9. **A failing row that is not this task's:** `CharacterPreviewTests.Resize_MakesTheTextureCappedAndFreesTheOld` expects `RenderTexture.depth` 24 and reads 32 in batch mode here. It fails the same on a clean `dev` (checked by stashing this work), so it is the machine's graphics device and not a regression. Not changed.
10. **`ClassSelect.prefab` was rebuilt in place** by a throwaway batch-mode editor script, deleted afterwards, so the root, its `CanvasGroup` and the presenter keep their file IDs and the Menu scene's instance still points at them.

Not done: the page has not been seen in the Editor or on a device. The stage camera's framing (UI-05b's 28°, 3.4 m, 1 m up) and the arrow, dot and button positions are unseen; if the body is cropped, adjust `CharacterStage.prefab`.
