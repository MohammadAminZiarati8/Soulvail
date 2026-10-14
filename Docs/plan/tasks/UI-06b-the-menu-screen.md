# UI-06b — The menu screen

**Size:** S · **Depends on:** UI-06a, UI-05c · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.1 · **Ledger rows:** none

## Goal

The menu is a title and at most two buttons in the new art, in portrait, and each button says what it does.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/MenuPresenter.cs` | Game | places the pieces in dp; the Descend and Continue logic is untouched |
| `Tests/Game/Presentation/MenuLayoutTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `Menu.unity`'s canvas: background, title plate, the two buttons dressed |

## Behaviour

1. **The background** is `CharacterPage_Background`, stretched to the safe area's full rect.
2. **The title** "Soulvail" (`ui.app.title`) sits on `Menu_TitlePlate`, centred in the upper third.
3. **With a save present, Continue is the primary button** (`Button_Green`, "Continue") and **Descend is the secondary** (`Button_Blue`, "Descend"), below it; **with none, Descend is the green primary alone.** Both are 240 × 56 dp or larger and nothing under 48 dp tall.
4. **Pressed states** use `Button_Green_Pressed` and `Button_Blue_Pressed`.
5. **It fits 9:20**; text clears 12 sp; nothing overlaps.
6. **No new buttons.** Settings and Quit are not on this screen: M8-02 owns options, and a phone has no Quit.

## Tests

| Test | Given / When / Then |
|---|---|
| `Menu_WithSave_ContinueIsGreenAndAbove` | rule 3 |
| `Menu_NoSave_DescendIsGreenAlone` | rule 3 |
| `Menu_TitleOnThePlate` | rule 2 |
| `Menu_Pressed_UsesPressedSprites` | rule 4 |
| `Menu_FitsPortrait` | rules 1, 5 |

## Manual verification

1. **[Editor]** with and without `run.json`: the right button is green and on top; Descend opens the character page.

## Out of scope

The Ranger standing behind the title, music and a settings page: none is asked for, and the simple menu is the point.

## As built

_Filled at merge._
