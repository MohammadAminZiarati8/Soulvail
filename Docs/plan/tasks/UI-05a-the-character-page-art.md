# UI-05a — The character page's art, imported

**Size:** S · **Depends on:** UI-04d · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

The UI-05 section and the specs UI-05a–d and UI-06a–b exist, and the 14 sprites the character page wears are imported for mobile.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Character/*.png` (11) | — | `CharacterPage_Background`, `_Pedestal`, `_ArrowLeft`, `_ArrowRight`, `_DotOff`, `_DotOn`, `_SkillFrame`, `_SkillFrameSelected`, `_LockBadge`, `_CostPill`, `_RoleRanged` |
| `Art/UI/Popup/*.png` (2) | — | `Button_Violet`, `Button_Violet_Pressed`: the Unlock button |
| `Art/UI/Icons/Icon_SoulShard.png` | — | the shard, beside the price |
| `Tests/Game/Art/CharacterPageArtTests.cs` | Tests.Game | one row per rule |
| *small edits* | | `Docs/plan/ROADMAP.md` (the UI-05 and UI-06 section), the six specs |

The 14 PNGs are one asset kind copied by name, counted as one file (UI-04a's precedent).

## Behaviour

1. Each piece is the file `UI_v3/cut_sheets.py` wrote from `Soulvail_GeminiSamples/UI_v3_sheets/`, byte for byte. The cutter keys the magenta out and runs `fix_colours.py`'s edge clean-up, because the sheets are JPEG and a magenta fringe is the known failure.
2. Each imports as a single full-rect sprite at 100 PPU, clamped, bilinear, alpha is transparency, no mipmaps, ASTC 4×4 on Android. **The background is the exception:** ASTC 6×6, and it is the only piece not cut from a magenta sheet.
3. The stretching pieces carry borders (left, bottom, right, top) of the shape `Button_Green` used, 0.37 × the piece's height at each end: `Button_Violet` and `Button_Violet_Pressed` `x/0/x/0`; `CharacterPage_CostPill` half its height at each end. Every other piece is drawn whole.
4. `Art/UI/Character/` holds these 11 pieces and nothing else. Of the four role badges the sheet drew, only the ranged one is imported; the melee and caster ones are cut when a class needs them.

## Tests

| Test | Given / When / Then |
|---|---|
| `CharacterArt_IsTheFourteenPieces` | rule 4 |
| `CharacterArt_IsByteForByteTheCut` | rule 1, per piece; ignored when the samples folder is absent |
| `CharacterArt_ImportsAsAUiSprite` | rule 2 |
| `CharacterArt_HasNoMipmaps` | rule 2 |
| `CharacterArt_IsAstcOnAndroid` | rule 2, with the background at 6×6 |
| `CharacterArt_HasItsBorders` | rule 3 |
| `CharacterArt_HasNoMagentaFringe` | the edge pixels of every piece are not magenta-leaning |

## Manual verification

None. Nothing wears the art yet.

## Out of scope

- Wearing any of it: UI-05b to UI-05d.
- The blue button and the title plate: UI-06a, which waits on a second Gemini sheet.
- The Gemini knight emblem and its five skill icons: they were drawn from an unfilled prompt and match no class. The Ranger's emblem and four icons already exist.

## As built

As specified, with five differences.

1. **The violet buttons have their own key.** `key_magenta` read the violet fill (nearer the key colour than any other piece) as a blend and returned it translucent blue. `cut_sheets.py` carries `key_violet`: a flood from the corners over pixels near the key, and a 3 px blend around it.
2. **The buttons are scaled to 1000 px wide** (1000×234 and 1000×235) from the sheet's 2404. Their borders are 87 at each end, 0.37 × the height. The cost pill is 740×273 with 136 at each end.
3. **Each piece is cut by connected blob, not by row and column**, because the frames and the lock overlap rows. The cutter keeps a piece's own glow and shadow (16 px) and drops a neighbour's.
4. **The background is the sheet, as a PNG** (1408×3040), so its Android and default `maxTextureSize` is 4096, not 2048. ASTC 6×6.
5. **Two older tests count folders and had to change:** `TreeArt_IsTheTwentyTwoPieces` ignores `Button_Violet*` in `Popup/`, and `LowerControlsArt_TotalHudCountIsThirty` is `…ThirtyOne` (the shard is in `Icons/`).

The edge clean-up is a despill (pull the magenta excess out of every pixel within 3 px of clear), not `fix_colours.py`'s pink-to-grey recolour, which is for icons and would grey the shard. The three role badges not imported (melee, a second melee, caster) are cut by the same script's table and dropped there. The metas were written from `Button_Green`'s and imported by Unity.

Not done: a look at any piece in a scene; nothing wears them.
