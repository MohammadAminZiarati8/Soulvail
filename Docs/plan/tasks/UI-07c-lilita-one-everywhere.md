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

_Filled at merge._
