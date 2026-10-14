# UI-07c — Lilita One everywhere

**Size:** S · **Depends on:** UI-07a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §16.4 · **Ledger rows:** none

## Goal

Every player-facing TMP text draws in `LilitaOne_SDF`; none falls back to TMP's default, LiberationSans SDF.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Art/FontTests.cs` | Tests.Game | opens each UI prefab and `Menu.unity` and reads every text's font |
| *small edits* | | each text's font asset in `Menu.unity`, `TreeView.prefab`, `Pause.prefab`, `RunEnd.prefab`, and any other prefab under `Prefabs/UI/` the test finds |

## Behaviour

1. **Every `TMP_Text`** in `Menu.unity` and in each prefab under `Prefabs/UI/` names `LilitaOne_SDF` as its font asset — set on the text, not inherited. The look pass found the Menu's title and both buttons, the whole TreeView, Pause and RunEnd on LiberationSans; only `Hud.prefab` and `ClassSelect.prefab` reference Lilita today.
2. **`DebugOverlay.prefab` is the one exception**: a development readout that never ships.
3. **TMP Settings' default font is not changed.** Left as LiberationSans, a text added later without a font fails rule 1's row instead of passing it by inheritance.
4. **A text moved off LiberationSans takes `LilitaOne_SDF`'s default material**; one already on a Lilita material preset keeps it.
5. **Sizes and wrapping are re-checked, not re-tuned.** Lilita One is wider than LiberationSans; a text that now overflows its rect is listed in *As built* and fixed here if the overflow shows in today's layout, by its screen's UI-08 task if not.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fonts_EveryUiPrefabText_IsLilitaOne` | each prefab under `Prefabs/UI/` but DebugOverlay / every `TMP_Text` / font is `LilitaOne_SDF` (rules 1, 2) |
| `Fonts_MenuSceneText_IsLilitaOne` | rule 1, `Menu.unity` |
| `Fonts_DefaultFontAsset_IsUnchanged` | rule 3 |

## Manual verification

1. **[Editor]** UI-07b's capture of the Menu, the tree, Pause and RunEnd: every word in Lilita One, none spilling its plate.

## Out of scope

A second font, font sizes, and TMP Settings.

## As built

**Built as specced:** `FontTests` (3 rows) and the font edits. A one-off Editor script set `font` and, for texts not already on a Lilita material, `fontSharedMaterial = LilitaOne_SDF.material` on every `TMP_Text` that was not on Lilita: 3 in `Menu.unity` and, in prefabs, FirstActiveHint 1, Hud 9, LevelUp 12, Pause 4, RunEnd 8, Sanctum 29, Skills 24, Splash 23, TreeView 16. The spec named four; the others are the "any other prefab the test finds" row. The Hud edits contradict the spec's claim that Hud was already Lilita: nine of its texts were not. `DebugOverlay` and TMP Settings are untouched; `Boot.unity` is outside the test and the table.

**Overflow (rule 5):** in Play on `Menu.unity` no text overflows (Title 93 x 31 in a 178 x 72 rect, Label 53 x 17 in 165 x 41). The other screens were not measured: no Device Simulator was open, so no `Capture Look` PNG was taken. **Owed to the owner:** capture the tree, Pause and RunEnd; an overflow there is UI-08e's.
