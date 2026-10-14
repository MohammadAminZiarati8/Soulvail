# UI-06a — The menu's art, imported

**Size:** S · **Depends on:** UI-05a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

The pieces the main menu needs that the character page does not — a blue secondary button and a title plate — are imported for mobile.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Menu/*.png` (3) | — | `Menu_TitlePlate`, `Button_Blue`, `Button_Blue_Pressed` |
| `Tests/Game/Art/MenuArtTests.cs` | Tests.Game | one row per rule |
| *small edits* | | none |

## Behaviour

1. **Blocked on the owner's Gemini sheet** (the owner was given the prompt on 2026-10-14: two buttons and a plain plaque). Each piece is the file `UI_v3/cut_sheets.py` wrote from it, byte for byte, edges cleaned.
2. Each imports as a single full-rect sprite at 100 PPU, clamped, bilinear, alpha is transparency, no mipmaps, ASTC 4×4 on Android.
3. `Button_Blue` and `_Pressed` carry `Button_Green`'s ratio of border to height at each end (`x/0/x/0`); `Menu_TitlePlate` carries its rim as a border on all four sides. The buttons and the plate carry no text.
4. `Menu/` holds these three pieces and nothing else. The menu's background is `CharacterPage_Background`, reused.

## Tests

| Test | Given / When / Then |
|---|---|
| `MenuArt_IsTheThreePieces` | rule 4 |
| `MenuArt_IsByteForByteTheCut` | rule 1; ignored when the samples folder is absent |
| `MenuArt_ImportsAsAUiSprite` | rule 2 |
| `MenuArt_HasItsBorders` | rule 3 |

## Out of scope

Wearing any of it: UI-06b. A settings button: M8-02 owns options.

## As built

1. **The cutter grew a section, not a new script.** `UI_v3/cut_sheets.py` cuts `One more Gemini prompt (UI-06a).jpg` into the three pieces, each scaled to 1000 px wide: `Button_Blue` 1000×269, `Button_Blue_Pressed` 1000×274, `Menu_TitlePlate` 1000×331.
2. **The plate takes `key_violet`, the buttons `key_magenta`.** `key_magenta` floods through the plate's cream rim and left a band inside it half see-through (233 alpha across 7 493 px); `key_violet` leaves none. No piece has a partial-alpha pixel away from its edge.
3. **Borders:** the buttons `x/0/x/0` at 0.37 × height (100 and 101); the plate 56 on all four sides, which clears its rivets and chamfered corners.
4. **Metas are hand-built** from `Button_Green.png.meta` (new GUIDs, sprite names, borders); no Editor was open. A batch-mode import left them unchanged.
5. **Tests:** `MenuArtTests` 10/10 in batch mode (the byte-for-byte rows ran, none ignored). The full suite was not re-run.
