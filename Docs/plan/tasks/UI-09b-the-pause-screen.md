# UI-09b — The Pause screen in the new art

**Size:** M · **Depends on:** UI-09a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

Pause is a stone popup with a "Paused" ribbon and its buttons in the kit's art, each with a glyph, laid out in UI-08e's portrait column.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/PausePresenter.cs` | Game | `LayOutColumn` grows the panel, the ribbon and the glyphs; no behaviour change |
| `Tests/Game/Presentation/OverlayLayoutTests.cs` | Tests.Game | the Pause rows below |
| *small edits* | | `Pause.prefab`: a `Frame` `Image` (`Popup_Panel`, sliced) and a `Ribbon` `Image` with its `Title` label under `Panel`, before the buttons; a `Glyph` `Image` in each button; the buttons' sprites and Sprite Swap. `English.asset` and `Pseudo.asset`: `ui.pause.title` "Paused" |

## Public API

```csharp
// PausePresenter gains serialized references only:
[SerializeField] private Image _frame;      // Popup_Panel
[SerializeField] private Image _ribbon;     // Popup_TitleRibbon
[SerializeField] private TMP_Text _titleLabel;
private static readonly LocKey TitleKey = new LocKey("ui.pause.title");
```

## Behaviour

1. **The frame** is `Popup_Panel`, sliced. Its width is the column's plus 24 dp a side; it runs from 24 dp below the shown buttons to 40 dp above them. It sits centred on the safe area, behind the buttons, over the scrim.
2. **The ribbon** is `Popup_TitleRibbon`, sliced: the column's width plus 48 dp, 64 dp tall, its centre on the frame's top edge. "Paused" (`ui.pause.title`) sits on it at 28 dp, `Palette.HudText`, centred.
3. **The buttons**: Resume is `Button_Green` with `Button_Green_Pressed`; Skills and View Tree are `Button_Blue` with `Button_Blue_Pressed`; Quit is `Button_Grey`, whose press is a ColorTint of 0.8, since it has no pressed sprite. Every label is `Palette.HudText`.
4. **Each button carries its glyph**: `Icon_Resume`, `Icon_Skills`, `Icon_Tree` or `Icon_Quit`, 28 dp square, 16 dp in from the button's left edge and centred on its height. The label centres in the space right of the glyph.
5. **UI-08e's column is unchanged**: the same width, button heights and gaps, centred on the safe area, and only shown buttons are stacked. The frame fits the buttons that are shown, so a class with no tree draws no empty slot.
6. **Nothing else changes**: the pause icon, the hold, Quit's latch, View Tree's point count and its colour all work as before.
7. **It fits 1080 × 2400 @ 420 dpi and 720 × 1600 @ 320 dpi**: the ribbon and the frame stay inside the safe area, nothing overlaps, and text clears 12 sp.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pause_Frame_WrapsTheShownButtons` | rules 1, 5: with and without View Tree |
| `Pause_Ribbon_StraddlesTheFrameTop` | rule 2 |
| `Pause_Buttons_WearTheirSprites` | rule 3 |
| `Pause_Glyphs_SitLeftOfTheLabel` | rule 4 |
| `Pause_Portrait_FitsTheSafeWidth` | rule 7: UI-08e's row, now with the frame and ribbon inside the safe area |
| `Pause_Title_IsWritten` | rule 2: the key resolves in English and Pseudo |

`PausePresenterTests` stays green untouched (rule 6).

## Manual verification

1. **[Editor]** Pause a Jungle run: the stone popup, "Paused" on the dark ribbon, a green Resume, a blue View Tree, a grey Quit, each with its glyph; with a point banked, View Tree reads "View Tree (1)".
2. **[Editor]** `Capture Look` at both phone sizes.

## Out of scope

The scrim's colour, a settings button (M8-02), and the Skills screen itself, which is switched off in the focus build.

## As built

_Filled at merge._
