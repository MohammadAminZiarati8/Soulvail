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

_Filled at merge._
