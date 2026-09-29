# UI-01a — The HUD's art and its font, imported

**Size:** S · **Depends on:** — · **Branch:** `ui-01a-the-huds-art-and-font`
**Design refs:** GD §16.4; CLAUDE.md's asset-naming table · **Ledger rows:** none

The owner's go of 2026-09-29: *"Rebuild only the top of the run HUD in the new Clash of Clans–style
art."* The pieces are Gemini's, cut to transparent PNGs by `UI_v2/cut_sheets.py` beside the repository,
at about twice the size the HUD draws them. This task brings in the ones UI-01 uses and nothing else,
and writes the track's plan: the [ROADMAP section](../ROADMAP.md#ui--the-hud-in-the-new-art-a-side-track-the-top-band-first)
and the six specs, UI-01a to UI-01f.

## Goal

Nineteen sprites and the Lilita One font are in the project with the import settings a phone wants,
and a test says so — so UI-01b to UI-01f dress prefabs from assets that are already right.

## Why this shape

- **Only the pieces UI-01 draws.** `UI_v2/png/` holds 102 pieces; the owner's list names 19. The rest
  come in with the task that draws them, so no sprite sits in the build unreferenced.
- **The names Gemini's cutter wrote, not `T_` names.** The folder says the kind, which is CLAUDE.md's
  rule, and keeping the cutter's names means a regenerated sheet is copied over, not renamed. The
  table gains the row UI sprites never had (rule 6).
- **Sliced where a piece stretches, simple where it does not.** A bar, a strip, a pill and a chip are
  drawn at widths the art never had, so each carries 9-slice borders on its rounded ends. The borders
  are the ones `UI_v2/mockup_hud.py` composed the approved mockup with. A star, a button, a cell and an
  icon are drawn at their own aspect, and carry none.
- **A static SDF atlas, not a dynamic one.** A dynamic font rasterises glyphs on the phone at run time
  and rewrites its atlas asset in the Editor. Lilita One is for numbers and titles, so ASCII and the
  Latin-1 letters the pseudo-locale adds fit in one 1024² atlas.
- **The outline is the font material's, in `Palette.Ink`.** Every Lilita word on the HUD is white with
  a dark outline and a drop shadow. Set on each text, the outline would clone a material per text and
  break batching. So the font's own material carries it, and the colour comes from `Palette`, which
  makes the art's ink the twentieth member.

## Files

| Path (under `Assets/_Project/` unless rooted) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Hud/*.png` | — | 16 pieces: `HealthBar_Frame`, `Fill_Bar`, `Gloss_Streak`, `XpStrip_Track`, `Fill_BarThin`, `LevelStar`, `SkillCell_Frame`, `ChargePip_Empty`, `ChargePip_Full`, `Sweep_Disc`, `Glow_Soft`, `Pill_Dark`, `Chip_Counter`, `Button_Pause`, `BossBar_Frame`, `BossBar_PhaseSeam` |
| `Art/UI/Icons/*.png` | — | 3 icons: `Icon_EnemiesLeft`, `Icon_Volley`, `Icon_Placeholder` |
| `Art/UI/Fonts/LilitaOne-Regular.ttf`, `OFL-LilitaOne.txt` | — | the font and its SIL OFL licence, verbatim |
| `Art/UI/Fonts/LilitaOne_SDF.asset` | — | the TMP font asset: static atlas, its material with the outline |
| `Tests/Game/Art/HudArtTests.cs` | Tests.Game | **New.** Rules 1–5 |
| *small edits* | | `Palette.Ink` and its `PaletteTests` row; `PaletteTests.Palette_ThePactFrameIsNotATenthColour`'s count, 19 → 20; CLAUDE.md's asset-naming table (rule 6); the [ROADMAP section](../ROADMAP.md#ui--the-hud-in-the-new-art-a-side-track-the-top-band-first) and the specs UI-01a to UI-01f |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Palette gains:
/// <summary>#0F1419 — the outline on every HUD word and number, and the art's own ink.</summary>
public static readonly Color Ink;
```

## Behaviour

1. **Nineteen pieces and no more.** `Art/UI/Hud/` holds the 16 pieces and `Art/UI/Icons/` the 3 icons
   the Files table names, copied byte for byte from `UI_v2/png/`.
2. **Every piece imports as a phone wants a UI sprite.** Texture type Sprite, single, full-rect mesh,
   100 pixels per unit, clamped, bilinear, alpha is transparency, **no mipmaps**, and an Android
   override to **ASTC 4×4**. ASTC 4×4 rather than 6×6, because an outline two texels wide is the
   whole of the look and 6×6 smears it.
3. **The pieces that stretch carry borders; the rest carry none.** Left and right, in source pixels,
   top and bottom 0: `HealthBar_Frame` 130, `Fill_Bar` 80, `Gloss_Streak` 120, `XpStrip_Track` 60,
   `Fill_BarThin` 40, `BossBar_Frame` 90, `Pill_Dark` 105, `Chip_Counter` 70. The mockup's own numbers,
   and 90 for the boss frame, whose rounded end the mockup never drew.
4. **The pieces code tints are white.** `Fill_Bar`, `Fill_BarThin`, `Gloss_Streak`, `Sweep_Disc` and
   `Glow_Soft` are drawn in a `Palette` colour, so each is near-white. A tinted piece multiplies, and a
   cyan fill drawn over a grey one is a darker cyan than GD §16.4's.
5. **Lilita One draws every HUD character, from a static atlas, outlined in ink.** `LilitaOne_SDF` is
   static, holds ASCII 0x20–0x7E, `·` and the Latin-1 letters 0xC0–0xFF in one atlas, and falls back
   to TMP's `LiberationSans SDF` for anything else. Its material is TMP's mobile SDF shader with
   `OUTLINE_ON` and `UNDERLAY_ON`, both in `Palette.Ink`. The licence sits beside the font.
6. **UI sprites and fonts have a naming row.** CLAUDE.md's table gains two rows:
   - a UI sprite is `Name_Part` under `Art/UI/<Screen>/`, or `Icon_Name` under `Art/UI/Icons/`;
   - a font keeps its foundry's file name, and its TMP asset is `<Family>_SDF` beside it.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `HudArt_IsTheNineteenPieces` | the two folders / listed / exactly the 19 names — rule 1 |
| `HudArt_IsByteForByteTheCut` | each piece / hashed beside `UI_v2/png/` / equal; ignored when the folder is absent — rule 1 |
| `HudArt_ImportsAsAUiSprite` | each importer / read / Sprite, single, full rect, clamp, bilinear, 100 ppu — rule 2 |
| `HudArt_HasNoMipmaps` | each importer / read / mipmaps off — rule 2 |
| `HudArt_IsAstcOnAndroid` | each importer / the Android override / on, `ASTC_4x4` — rule 2 |
| `HudArt_TheStretchingPiecesAreSliced` | the eight / `spriteBorder` / the table's left and right, 0 top and bottom — rule 3 |
| `HudArt_TheRestAreNotSliced` | the other eleven / `spriteBorder` / zero — rule 3 |
| `HudArt_TheTintedPiecesAreWhite` | the five / every opaque pixel's saturation / mean under 0.08 — rule 4 |
| `Font_IsStaticAndCoversTheHud` | `LilitaOne_SDF` / population mode, `HasCharacters` / static; every character of rule 5 — rule 5 |
| `Font_FallsBackToLiberation` | its fallback table / read / `LiberationSans SDF` — rule 5 |
| `Font_IsOutlinedInInk` | its material / keywords and colours / both on, both `Palette.Ink`, both widths above 0 — rule 5 |
| `Font_ShipsWithItsLicence` | the folder / listed / `OFL-LilitaOne.txt` beside the `.ttf` — rule 5 |
| `Ink_IsTheArtsOutline` (PaletteTests) | `Palette.Ink` / read / `#0F1419`, not danger — Public API |

Rule 6 is a line in CLAUDE.md and has no row.

## Manual verification (Editor / device)

1. **[Editor]** Select `LilitaOne_SDF` and type `STAGE 3 · Wave 2/3 · 72 / 120` in a TMP text using it.
   *Expected: white, outlined in near-black, with a small shadow below.*
2. **[device]** Whether ASTC 4×4 keeps the outlines crisp at 400 dpi. Deferred with every device row.

## Out of scope

- **Drawing any of it.** UI-01b to UI-01f dress the prefabs.
- **A sprite atlas.** The pieces are separate textures; batching them is a [parking-lot](../ROADMAP.md#parking-lot) line.
- **Nunito,** the body font beside Lilita One in `UI_v2/fonts/`: no UI-01 piece draws a sentence.
- **The other 83 pieces.**

## As built

**As specified, with two deviations.**

**Deviation 1: `.gitattributes` gains a line.** The SIL OFL text opens with a copyright line that ends in
a space, and the pre-commit whitespace check flags it. A licence is reproduced verbatim, which is why
`Assets/ThirdParty/**` is already `-whitespace`, so `Assets/_Project/Art/UI/Fonts/OFL-*.txt` joins it.
The file is not in the Files table.

**Deviation 2: the font's numbers are the build's.** Rule 5 names the atlas and the set, not the numbers:
sampled at **56 pt with 7 px of padding** in **one 1024² atlas**, all 160 characters fit with none
missing. The material draws the face dilated **0.15**, an outline of **0.25** and a shadow offset
**−0.6** and dilated **0.25**, all in `Palette.Ink`. `ShaderUtilities.UpdateShaderRatios` ran on it, so
TMP's padding accounts for the outline. The asset is 2.1 MB of YAML, beside `LiberationSans SDF`'s 2.2,
and under the hook's 5 MB.

**How the font asset was made, since nothing in the project did it before.** `TMP_FontAsset.CreateFontAsset`
builds it dynamic, `TryAddCharacters` fills the atlas, and `atlasPopulationMode` then goes static. The
asset is created, and the atlas texture and the material are added to it as sub-objects with
`AssetDatabase.AddObjectToAsset`. Without the last step the file saves with references to objects that
exist only in memory. The material's shader is swapped to `TextMeshPro/Mobile/Distance Field`, which
declares `OUTLINE_ON` and `UNDERLAY_ON` as shader features (checked in the shader, [Traps §9](../../Traps.md)).
`Font_IsOutlinedInInk` asks `IsKeywordEnabled`, not `shaderKeywords`.

**The import was one command over the two folders**: texture type, single, full-rect mesh, 100 ppu,
clamp, bilinear, alpha is transparency, no mips, `Compressed`, and an Android override to `ASTC_4x4` at
2048. The borders are rule 3's table, written as `(left, 0, right, 0)`. `HudArt_TheTintedPiecesAreWhite`
reads each PNG from disk, because the imported texture is compressed and not readable. The five tinted
pieces measure a mean saturation of **0.000**, against 0.30 for the health bar's frame and 0.61 for the
star.

**Rows: 105 in `HudArtTests` and one in `PaletteTests`.** The five per-piece rows run for all nineteen.
`HudArt_IsByteForByteTheCut` compares against `../Soulvail_GeminiSamples/UI_v2/png/`, and is ignored
where that folder is absent, so a clone without the samples does not go red. `Palette_ThePactFrameIsNotATenthColour`'s
count went 19 → 20, and its message names the new member.
