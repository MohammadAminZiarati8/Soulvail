# UI-09a — The Pause and Run-end art, imported

**Size:** S · **Depends on:** UI-08e · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

The pieces Pause and Run end need that the popup kit does not already have — the two title ribbons, the two emblems, three stat icons and four button glyphs — are imported for mobile.

## The sheets

The owner's three Gemini sheets, 2026-10-17, in `Soulvail_GeminiSamples/UI_v5/`: `1.jpg` (ribbons and emblems, 1024²), `2.jpg` (stat icons, 1376×768), `3.jpg` (glyphs, 1376×768). All on flat magenta. A new `UI_v5/cut_sheets.py` beside `UI_v5/png/` cuts them in `UI_v4/cut_sheets.py`'s style (`key_magenta`, then `despill`; the biggest blobs of a sheet are its pieces, so a speck is not one).

**Already in the kit, so not cut:** the panel (`Popup_Panel`), the dark title ribbon for Pause (`Popup_TitleRibbon`), the buttons (`Button_Green`, `Button_Blue`, `Button_Grey` and their pressed states), the row pill (`Pill_Dark`), the shard icon (`Icon_SoulShard`) and the glow (`Glow_Soft`).

**`2.jpg`'s fourth piece, the gold glow burst, is not cut.** Its halo fades into the magenta, so any key leaves a pink haze. UI-09c tints `Glow_Soft` with `Palette.Essence` instead.

| Piece | From | Folder | Worn by |
|---|---|---|---|
| `RunEnd_RibbonDefeat` · `RunEnd_RibbonVictory` | `1.jpg` top row | `Art/UI/RunEnd/` | the Run-end title (UI-09c) |
| `RunEnd_EmblemDefeat` · `RunEnd_EmblemVictory` | `1.jpg` bottom row | `Art/UI/RunEnd/` | above the title (UI-09c) |
| `Icon_Stage` · `Icon_BossKill` · `Icon_PlaceOpened` | `2.jpg`, pieces 1–3 | `Art/UI/Icons/` | the Run-end rows (UI-09c) |
| `Icon_Resume` · `Icon_Skills` · `Icon_Tree` · `Icon_Quit` | `3.jpg` | `Art/UI/Icons/` | the Pause buttons (UI-09b) |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/RunEnd/*.png` (4) | — | the ribbons and emblems, with hand-built `.meta`s from `Popup_TitleRibbon.png.meta` |
| `Art/UI/Icons/*.png` (7) | — | the stat icons and glyphs, `.meta`s from `Icon_SoulShard.png.meta` |
| `Tests/Game/Art/RunEndArtTests.cs` | Tests.Game | one row per rule |
| *small edits* | | none |

## Behaviour

1. **Each piece is the file `UI_v5/cut_sheets.py` wrote, byte for byte**, cut at the sheet's own resolution, not scaled.
2. **Each imports as a single full-rect UI sprite**: 100 PPU, clamped, bilinear, alpha is transparency, no mipmaps, ASTC 4×4 on Android.
3. **A pair shares one canvas**: the two ribbons have the same canvas size, and so do the two emblems, with each body centred in transparent padding. Swapping the defeat piece for the victory one moves nothing.
4. **The ribbons carry `Popup_TitleRibbon`'s 9-slice form, `x/0/x/0`**, with x set at the cut by eye so it clears the folded tails. Both ribbons get the same x. The emblems and the icons have no border.
5. **No piece has a partial-alpha pixel away from its edge**, and none keeps a magenta-hued pixel (hue 285°–315°, saturation > 0.5) at alpha > 0.
6. **`RunEnd/` holds these four pieces and nothing else.**

## Tests

| Test | Given / When / Then |
|---|---|
| `RunEndArt_IsTheFourPieces` | rule 6 |
| `RunEndArt_IconsAreInIcons` | the seven icons exist under `Icons/` |
| `RunEndArt_IsByteForByteTheCut` | rule 1; ignored when the samples folder is absent |
| `RunEndArt_ImportsAsAUiSprite` | rule 2, all eleven |
| `RunEndArt_PairsShareACanvas` | rule 3 |
| `RunEndArt_RibbonsHaveTheirBorder` | rule 4 |
| `RunEndArt_NoMagentaLeft` | rule 5 |

## Manual verification

1. **[Editor]** The cutter's contact sheet (`UI_v5/contact.png`) shows each piece on dark and on light, with no pink fringe.

## Out of scope

Wearing any of it: UI-09b and UI-09c. A redrawn glow burst: `Glow_Soft` covers it.

## As built

_Filled at merge._
