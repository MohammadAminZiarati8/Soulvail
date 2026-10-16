# UI-09c — The Run-end screen in the new art

**Size:** M · **Depends on:** UI-09a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

Run end is a stone popup that shows the outcome at a glance: an emblem and a grey ribbon for a death, a laurel and a gold ribbon for a cleared place. Below them, three rows with icons, and a green Home button.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/RunEndPresenter.cs` | Game | `LayOut` places the new pieces; `OnModeFinished` swaps the ribbon and emblem |
| `Tests/Game/Presentation/OverlayLayoutTests.cs` | Tests.Game | the Run-end rows below |
| *small edits* | | `RunEnd.prefab`: `Frame` (`Popup_Panel`, sliced), `Emblem`, `Ribbon` (the title moves under it), a `Pill_Dark` backing and an icon `Image` per row, a `Glow` (`Glow_Soft`) behind the shard figure; Return wears `Button_Green` with Sprite Swap. `English.asset`: `ui.death.hint` "Tap to return" → "Home" (Pseudo follows) |
| *ripple* | | `RunEndFinishTests`: the finished title is `Palette.HudText`, no longer `Palette.Essence` (rule 3); `RunEndPresenterTests` where it holds Depth and Bosses `Neutral` (rule 4), and it and `TableLocalizerTests` where they quote "Tap to return" |

## Public API

```csharp
// RunEndPresenter gains serialized references only:
[SerializeField] private Image _frame, _emblem, _ribbon, _shardGlow;
[SerializeField] private Image _depthIcon, _bossIcon, _shardIcon, _openedIcon;
[SerializeField] private Sprite _ribbonDefeat, _ribbonVictory, _emblemDefeat, _emblemVictory;
```

## Behaviour

1. **The frame** is `Popup_Panel`, sliced, centred on the safe area, the column's width plus 24 dp a side. It holds the ribbon's lower half, the rows and Return, with 24 dp padding at the bottom.
2. **The emblem** is 96 dp square and centred above the ribbon, overlapping it by 16 dp. The ribbon is the column's width plus 48 dp, 72 dp tall, with its centre on the frame's top edge. The title sits on the ribbon at UI-08e's 40 dp.
3. **A death draws `RunEnd_EmblemDefeat` and `RunEnd_RibbonDefeat`. A `ModeFinished` swaps in `RunEnd_EmblemVictory` and `RunEnd_RibbonVictory`** on the same call that rewrites the title. **The title is `Palette.HudText` either way**: the gold lives in the ribbon now, and gold text on a gold ribbon cannot be read. This supersedes UI-11d's gold title.
4. **Each of the three rows sits on `Pill_Dark`**, sliced, the column's width and 44 dp tall. It has a 28 dp icon 12 dp in from its left edge (`Icon_Stage`, `Icon_BossKill` or `Icon_SoulShard`), then the word in `Palette.HudCaption`, then the figure right-aligned 16 dp from the right edge. **Depth's and Bosses' figures change from `Palette.Neutral` to `Palette.HudText`**, because the grey cannot be read on a dark pill. The payout stays `Palette.Essence`.
5. **The payout glows**: `Glow_Soft` is tinted `Palette.Essence` at alpha 0.6, centred on the shard figure, twice the figure's height. It is drawn behind the figure and above the pill.
6. **The "New place" line** keeps UI-11d's rule: it is shown only when a finish opens a place. It gets `Icon_PlaceOpened`, 28 dp, to the left of its text, and the two are centred together on the panel. The text is `Palette.Neutral` on the light stone.
7. **Return is `Button_Green`** with `Button_Green_Pressed`, labelled "Home" in `Palette.HudText`. The latch, the scene load and its failure path are unchanged.
8. **It fits 1080 × 2400 @ 420 dpi and 720 × 1600 @ 320 dpi**, with and without the "New place" line: the emblem's top is inside the safe area, nothing overlaps, and text clears 12 sp.

## Tests

| Test | Given / When / Then |
|---|---|
| `RunEnd_Frame_HoldsTheColumn` | rule 1 |
| `RunEnd_EmblemAndRibbon_StackOnTheFrameTop` | rule 2 |
| `RunEnd_Death_DrawsTheDefeatPair` | rule 3 |
| `RunEnd_Finish_SwapsInTheVictoryPair` | rule 3: sprites swapped, title `HudText` |
| `RunEnd_Rows_SitOnPillsWithIcons` | rule 4 |
| `RunEnd_ShardGlow_SitsBehindTheFigure` | rule 5 |
| `RunEnd_Opened_HasItsIcon` | rule 6 |
| `RunEnd_Return_IsGreenHome` | rule 7 |
| `RunEnd_Portrait_FitsTheSafeWidth` | rule 8: UI-08e's row, both sizes, with and without the line |

## Manual verification

1. **[Editor]** Die in the Jungle: the cracked emblem, "You died" on the grey ribbon, three rows on dark pills with their icons, the shards glowing gold, a green Home.
2. **[Editor]** Finish the Jungle (the debug stage jump to 30): the laurel, "Jungle cleared!" on the gold ribbon, "New place: …" with its archway once a second place is listed.
3. **[Editor]** `Capture Look` at both phone sizes.

## Out of scope

Counting the numbers up, any tween or particle (timeScale is 0), a "Play again" button, and sound.

## As built

_Filled at merge._
