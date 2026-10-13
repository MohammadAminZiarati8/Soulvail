# UI-04a — The tree screen's art, imported

**Size:** S · **Depends on:** UI-03c · **Branch:** `ui-04a-the-tree-art`
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

The UI-04 section and the specs UI-04a–d exist, and the 22 sprites the tree screen wears are imported for mobile.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Popup/*.png` (7) | — | `Popup_Panel`, `Popup_TitleRibbon`, `Button_Close`, `Button_Green`, `Button_Green_Pressed`, `Button_Grey`, `Card_Stone`: any popup's pieces |
| `Art/UI/Tree/*.png` (12) | — | `TreeBlock_Panel`, `TreeTile_Taken`/`_Available`/`_Locked`/`_SelectedRing`, `TreeBadge_Check`/`_Plus`/`_Lock`, `TreeLink_Lit`/`_Dark`, `ClassPlate`, `Emblem_Ranger` |
| `Art/UI/Icons/*.png` (3) | — | `Icon_SkillPoint`, `Icon_QuickDraw`, `Icon_FleetFoot` |
| `Tests/Game/Art/TreeArtTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `Docs/plan/ROADMAP.md` (the UI-04 section), the four UI-04 specs |
| *ripple* | | `LowerControlsArtTests`: the Hud + Icons count goes from 27 to 30 |

The 22 PNGs are one asset kind copied by name, counted as one file, UI-01a's and UI-02a's precedent.

## Behaviour

1. Each piece is the file `UI_v2/cut_sheets.py` wrote, byte for byte.
2. Each imports as a single full-rect sprite at 100 PPU, clamped, bilinear, alpha is transparency, no mipmaps, ASTC 4×4 on Android.
3. The pieces that stretch carry the borders `UI_v2/mockup_tree.py` composed the approved mockup with (left, bottom, right, top): `Popup_Panel` and `TreeBlock_Panel` 110 all round, `Card_Stone` 80, `Popup_TitleRibbon` 200/0/200/0, `Button_Green`, `Button_Green_Pressed` and `Button_Grey` 130/0/130/0, `ClassPlate` 100/90/100/90, both links 0/60/0/60. Every other piece is drawn whole.
4. `Popup/` and `Tree/` hold these 19 pieces and nothing else; the three icons are in `Icons/`.

## Tests

| Test | Given / When / Then |
|---|---|
| `TreeArt_IsTheTwentyTwoPieces` | rule 4 |
| `TreeArt_IsByteForByteTheCut` | rule 1, per piece; ignored when the samples folder is absent |
| `TreeArt_ImportsAsAUiSprite` | rule 2 |
| `TreeArt_HasNoMipmaps` | rule 2 |
| `TreeArt_IsAstcOnAndroid` | rule 2 |
| `TreeArt_HasItsBorders` | rule 3 |

## Out of scope

- Wearing any of it: UI-04b to UI-04d.
- The capstone (`Capstone_*`, `Icon_RainOfArrows`), the other classes' emblems and icons, and the second class block: owner's scope for UI-04, 2026-10-13.

## As built

As specified. `Button_Grey` is the one piece the mockup did not draw; it is in for UI-04d's disabled Learn button and takes `Button_Green`'s borders, since its rounded ends are the same height.
