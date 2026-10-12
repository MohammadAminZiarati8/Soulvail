# UI-02a — The lower controls' art, imported

**Size:** S · **Depends on:** UI-01f · **Branch:** `ui-02a-lower-control-assets`
**Design refs:** CC §2.1–2.3, §5, §6.2; GD §16.4; CLAUDE.md's asset-naming table · **Ledger rows:** none

The second phase of the UI track: the lower controls of the run HUD in the new Clash of Clans–style art.
The pieces are Gemini's, cut to transparent PNGs by `UI_v2/cut_sheets.py` beside the repository.
This task brings in the 8 sprites required for the lower controls (stick, roll button, manual skill buttons)
with phone import settings, and writes the plan for UI-02: the ROADMAP section and the three specs, UI-02a to UI-02c.

## Goal

Eight lower control sprites are in the project with the import settings a phone wants, and tests say so —
so UI-02b and UI-02c dress the stick and buttons from assets that are already right.

## Why this shape

- **Only the pieces UI-02 draws.** `UI_v2/png/` holds 102 pieces. This task imports exactly the 8 lower control
  sprites: 2 for the stick (`Stick_Base`, `Stick_Knob`), 4 for Roll (`RollButton_Ready`, `RollButton_Cooling`,
  `RollButton_Pressed`, `Icon_Roll`), and 2 for manual skills (`SkillButton_Ready`, `SkillButton_Cooling`).
- **The names Gemini's cutter wrote.** Following CLAUDE.md's asset-naming rule, placed under `Art/UI/Hud/` for
  HUD elements and `Art/UI/Icons/` for `Icon_Roll`.
- **Not sliced.** All eight pieces are drawn at their authored aspect ratio; none stretch, so each carries
  `spriteBorder == Vector4.zero`.
- **Phone UI sprite settings.** Sprite 2D/UI, single, full-rect mesh, 100 pixels per unit, clamped, bilinear,
  alpha is transparency, no mipmaps, and an Android override to ASTC 4×4.
- **Dimensions pinned.** Each piece's width and height is verified against the authored cut dimensions.

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Hud/Stick_Base.png` | — | The virtual stick's 543×482 base ring |
| `Art/UI/Hud/Stick_Knob.png` | — | The virtual stick's 365×410 knob disc |
| `Art/UI/Hud/RollButton_Ready.png` | — | The Roll button's ready state frame (530×523) |
| `Art/UI/Hud/RollButton_Cooling.png` | — | The Roll button's cooling/dimmed state frame (531×523) |
| `Art/UI/Hud/RollButton_Pressed.png` | — | The Roll button's pressed state frame (530×523) |
| `Art/UI/Hud/SkillButton_Ready.png` | — | Manual skill button ready state frame (348×347) |
| `Art/UI/Hud/SkillButton_Cooling.png` | — | Manual skill button cooling state frame (348×347) |
| `Art/UI/Icons/Icon_Roll.png` | — | The Roll button's boot icon (621×607) |
| `Tests/Game/Art/LowerControlsArtTests.cs` | Tests.Game | **New.** Rules 1–4 |
| *small edits* | | `Tests/Game/Art/HudArtTests.cs` (subset check for UI-01 pieces), `Docs/plan/ROADMAP.md`, `Docs/plan/PROGRESS.md` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

None — asset import and verification.

## Behaviour

1. **Eight lower control pieces and no more.** `Art/UI/Hud/` holds the 7 pieces and `Art/UI/Icons/` the 1 icon
   the Files table names, copied byte for byte from `UI_v2/png/`, bringing the HUD total across both folders to 27.
2. **Every piece imports as a phone wants a UI sprite.** Texture type Sprite, single, full-rect mesh,
   100 pixels per unit, clamped, bilinear, alpha is transparency, **no mipmaps**, and an Android
   override to **ASTC 4×4**.
3. **The pieces are not sliced.** All eight pieces carry `spriteBorder == Vector4.zero`.
4. **Authored dimensions are preserved.** Each piece matches its cut dimensions: `Stick_Base` 543×482,
   `Stick_Knob` 365×410, `RollButton_Ready` 530×523, `RollButton_Cooling` 531×523, `RollButton_Pressed` 530×523,
   `Icon_Roll` 621×607, `SkillButton_Ready` 348×347, `SkillButton_Cooling` 348×347.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `LowerControlsArt_IsTheEightPieces` | the two folders / listed / exactly the 8 lower control names — rule 1 |
| `LowerControlsArt_TotalHudCountIsTwentySeven` | `Art/UI/Hud` and `Icons` / textures found / exactly 27 — rule 1 |
| `LowerControlsArt_IsByteForByteTheCut` | each piece / hashed beside `UI_v2/png/` / equal; ignored when the folder is absent — rule 1 |
| `LowerControlsArt_ImportsAsAUiSprite` | each importer / read / Sprite, single, full rect, clamp, bilinear, 100 ppu — rule 2 |
| `LowerControlsArt_HasNoMipmaps` | each importer / read / mipmaps off — rule 2 |
| `LowerControlsArt_IsAstcOnAndroid` | each importer / the Android override / on, `ASTC_4x4` — rule 2 |
| `LowerControlsArt_AreNotSliced` | all eight / `spriteBorder` / zero — rule 3 |
| `LowerControlsArt_HasAuthoredDimensions` | each piece / source width and height / exact authored pixel dimensions — rule 4 |

## Manual verification (Editor / device)

1. **[Editor]** Inspect each of the 8 sprites in Project view. Confirm Sprite 2D/UI, 100 PPU, Clamp, ASTC 4x4 Android override.
2. **[device]** Verify clarity and outline sharpness on phone display at 400 dpi. Deferred with other device rows.

## Out of scope

- Dressing `FloatingStick`, `SkillButton`, or `ManualSkillButton` (UI-02b, UI-02c).
- Sprite atlas batching.
- The remaining unused pieces in `UI_v2/png/`.

## As built

**As specified, with one deviation.**

**Deviation 1: `HudArtTests.cs` relaxed its piece-count assertion.** `HudArt_IsTheNineteenPieces` in `HudArtTests.cs`
originally asserted that `AssetDatabase.FindAssets("t:Texture2D", ...)` returned exactly the 19 top-band pieces.
With the 8 lower control pieces added, the test now asserts that the 19 top-band pieces remain a subset of the
found assets (`Assert.That(PiecePaths(), Is.SubsetOf(found))`), while `LowerControlsArtTests.LowerControlsArt_TotalHudCountIsTwentySeven`
pins the exact total count of 27 across both folders.

**The import was executed via Unity script:** Sprite 2D/UI, single, full-rect mesh, 100 PPU, clamp, bilinear,
alpha is transparency, no mipmaps, and Android override to ASTC 4×4 at 2048 max size. All 8 sprites have
`spriteBorder == Vector4.zero`.

**Rows: 50 in `LowerControlsArtTests` and 105 in `HudArtTests` all green.**
