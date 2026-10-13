# UI-01f — The boss bar in the new art

**Size:** S · **Depends on:** UI-01b (the band's place) · **Branch:** `ui-01f-the-boss-bar`
**Design refs:** GD §16.2, §16.4; M4-04 · **Ledger rows:** none

The owner's brief of 2026-09-29, item 8: *"Boss bar: the new frame and phase seams, bone fill. It
appears only in boss fights, as today."* Starting size: up to 400 dp wide, centred. UI-01b has already
put it there; this task draws it.

## Goal

A boss fight's band is `BossBar_Frame` with a bone fill in its well and `BossBar_PhaseSeam` marks at the
authored thresholds, and every rule M4-04 gave it still holds.

## Why this shape

- **The frame's well is the track.** The band's track was `Palette.Boss` at 22 %. The frame draws a
  dark well, so the fill runs in it and the faint track goes; `_trackAlpha` goes with it.
- **The fill is a sliced pill, as the player's is** (UI-01b). The seams are the art's, untinted: a dark
  pill across a bone fill is the contrast `Palette.Neutral` gave a flat line, drawn in.
- **One quantity with marks, unchanged.** The fill is the boss's whole health across the whole band and
  the seams sit at `EntersBelow`, M4-04 rules 3 and 4. The beat still dims the band.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/BossBarView.cs` | Game | **Edit.** Rules 1–4 |
| `Tests/Game/Presentation/BossBarViewTests.cs` | Tests.Game | **Edit.** Rules 1–4 |
| *small edits* | Tests.Game | `HudLayoutTests`: the band's new height inside the safe area |
| *assets* | | `Hud.prefab`'s `BossBar`: `Frame` (`BossBar_Frame`, sliced), `Well` holding `Fill` (`Fill_Bar`, sliced) and `Mark` (`BossBar_PhaseSeam`) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// BossBarView:
public const float HeightDp = 18f;                 // was 10
[SerializeField] private RectTransform _well;      // new: where the fill and the seams live
// _track is the frame now, drawn untinted; _trackAlpha is gone.
// Fraction, SegmentCount, IsShown, IsInBeat, MarkCount and MarkFraction are unchanged.
```

## Behaviour

1. **The band is `BossBar_Frame`, 18 dp tall**, at UI-01b's place and width. The well is inset a third
   of the band's height at each end and a quarter top and bottom.
2. **The fill is `Fill_Bar` in `Palette.Boss`, in the well, as wide as the boss's whole fraction** —
   M4-04 rule 3. A non-finite fraction keeps the last good fill, and the fill is hidden at 0.
3. **The seams are `BossBar_PhaseSeam`, untinted, at the authored thresholds across the well**, a
   little taller than it — M4-04 rule 4. They are made on demand and reused, as before.
4. **Everything else is M4-04's**: shown on `BossPhaseChanged`, gone on the boss's `EnemyDied`, dimmed to
   `_beatAlpha` through a beat, no string, not danger, never in the raycast path.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Bar_FillIsTotalHealthNotPerSegment` (changed) | a hit to 0.55 / drawn / the fill's anchor max 0.55 — rule 2 |
| `Bar_FillHidesAtZero` | 0 / drawn / fill disabled — rule 2 |
| `Bar_FillIsBone` | drawn / read / `Palette.Boss` — rule 2 |
| `Bar_MarksSitAtTheAuthoredThresholds` (kept) | — rule 3 |
| `Bar_SeamsAreTheArtUntinted` | a three-phase boss / drawn / two seams, `BossBar_PhaseSeam`, white — rule 3 |
| `Bar_TheFrameHoldsTheWell` | placed / read / the well inside the frame by rule 1's insets — rule 1 |
| `Prefab_IsDressed` (changed) | `Hud.prefab` / read / frame, fill and seam are the art; fill sliced — rules 1–3 |
| `Bar_CarriesNoSerializedColour`, `Bar_IsNotTheDangerColour`, `Bar_SaysSoDuringABeat`, `Bar_DrawsNoRawString` (kept) | — rule 4 |

**Changed expectations, and why.** `Bar_FillIsTotalHealthNotPerSegment` read `fillAmount`, which a sliced
image does not draw; it reads the fill's anchor (rule 2). `Prefab_IsDressed` asked for exactly one
`Filled` image in the band; the fill is sliced now (rule 2). The track's 22 % row, if any, goes with
`_trackAlpha` (rule 1).

## Manual verification (Editor / device)

1. **[Editor]** A run to stage 5's Warden. *Expected: the new band under the top band, bone draining
   through two seams, dimmed in each beat, gone at the kill.*
2. **[device]** The seams at 400 dpi. Deferred with every device row.

## Out of scope

- **The boss's name and portrait.** GD §16.2 asks for a bar.
- **Elite bars:** M7-02e's.

## As built

**As specified in behaviour, with five deviations.**

**Deviation 1: the frame is the band's own image, not a `Frame` child.** The Files row named a child.
The band's root `Image` has been the track since M4-04, and `_track` points at it. uGUI draws a parent
before its children, so a frame on the root already sits under the well it holds. A child would add an
object to do the same job, and would have to stay first among its siblings.

**Deviation 2: `HudLayoutTests` needed no edit.** `Band_Landscape_NothingOverlaps` and
`Band_Portrait_NothingOverlaps` lay out the shipped prefab. So they found the 18 dp band (100 to 118 dp)
clear of the Essence counter at 124 without being told. `Hud_TheBandHangsUnderTheTopBand` reads
`HeightDp`.

**Deviation 3: four more rows changed expectation than the spec listed.**
- `Bar_MarksSitAtTheAuthoredThresholds` pinned each seam to exactly the band's height, 0 to 1. Rule 3's
  seam is a little taller than the well, −0.35 to 1.35 of it. So the row asserts that the seam covers at
  least the whole well: min ≤ 0, max ≥ 1.
- `Bar_IgnoresANonFiniteFraction` reads the fill's anchor instead of `fillAmount`, for rule 2's reason.
- `Bar_CarriesNoSerializedColour` now allows only `_beatAlpha`, since `_trackAlpha` is gone (rule 1).
- `Bar_IsNotTheDangerColour` asserted a faint `Palette.Boss` track and `Palette.Neutral` seams. It now
  asserts the frame and the seams white, the art untinted, and the fill still `Palette.Boss`.

The spec's own list holds as written. `Prefab_IsDressed` checks the sprites, the slicing and the well.

**Deviation 4: a seam is 6 dp wide.** `_markWidthDp` was 2, a flat line's width. `BossBar_PhaseSeam` is
a rounded pill, and at 2 dp it drew as a line. Six keeps its shape at the seam's 15 dp height. How far
a seam overhangs is a constant, `SeamOverhang`: 0.35 of the well's height.

**Deviation 5: `Palette.cs` changed, outside the table, in one remark.** `Palette.Boss`'s remark said
the seams were `Palette.Neutral`. It now says they are the art. No colour changed, and `Palette.Neutral`
keeps its other five users.

**The captures** are in `Soulvail_GeminiSamples/UI_v2/captures/`: landscape on the Device Simulator at
480 dpi, and 9:20 at its dp-equivalent. The mockup draws no boss, so the band was checked against
`BossBar_Frame` and the seam. Stage 1 has no boss, so the band was bound in Play to three phases, with
seams at 0.66 and 0.33, and drawn at 80 %. Manual step 1, a run to the Warden, is the owner's.
