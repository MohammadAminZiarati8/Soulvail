# UI-01b — The health bar, the XP strip and the level star

**Size:** M · **Depends on:** UI-01a · **Branch:** `ui-01b-the-health-bar-xp-and-star`
**Design refs:** GD §16.1, §16.2, §16.4; M1-17, M3-10b, M3-13b, M4-04 · **Ledger rows:** 1 (the device list gains the band at 400 dpi)

The owner's brief of 2026-09-29, items 1–3: the XP strip full width along the top of the safe area;
the blue level star with the number inside it; the health bar with its frame, dark track, cyan fill, a
ghost that lags, a gloss, the blocked flash, a low-health state that *"must never turn red-orange"*,
and *"72 / 120"* inside the bar. Starting sizes: bar 240 × 22 dp, star 44 dp.

## Goal

The player's own row is drawn in the new art, and the top band's layout is dp arithmetic that holds
at 20:9 landscape and 9:20 portrait — so the chips, the skill row and the boss bar that follow have
their places already.

## Why this shape

- **One file of numbers the band agrees on.** Six components place pieces of the top band, on two
  canvases, and today they agree by default values copied between them. `HudLayout` holds the shared
  dp numbers and the two width rules, and each component's Inspector fields default to them, as
  `BossBarView.TopDp` already defaults to `XpBarView.HeightDp`.
- **A sliced pill that changes width, not a filled image.** `fillAmount` on a horizontal fill cuts
  the pill's round end square, and a `Filled` image does not slice, so its round ends stretch. So the
  fill, the ghost and the XP fill are sliced sprites whose right anchor is the fraction. A sliced
  image narrower than its borders shrinks them, so a sliver of health is a sliver, not a disc.
- **The bar's width is what the row leaves, up to 240.** At 9:20 (411 dp) a 240 dp bar, the star and
  the right-hand chips cannot share one row; the mockup's bar is 213 dp for that reason. So the bar
  takes the room left of a reserved right cluster, between 120 and 240 dp.
- **The Aegis ring goes round the star.** Its old place, right of the readout, is inside the bar now,
  and in portrait no room is left in the row. Concentric and behind, it rings the star at no width.
  It draws what it drew; the class that shows it, the Oathbound, is hidden from class select.
- **The low state pulses in the player's own family.** GD §16.3's desaturation vignette is a
  full-screen effect and outside this band. The bar's part is to beat between cyan and a deep cyan
  below a quarter of its health. That is a heartbeat in the player's colour, and never the danger
  colour.
- **The boss band and the skill row move now, not in their own tasks.** An 8 dp strip and a star at
  13 dp would sit under today's band at 9–19 dp and under today's cells at (16, 48). So this task
  moves both out of the way, and UI-01e and UI-01f redraw them where they already are.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HudLayout.cs` | Game | **New.** The band's dp numbers, the two width rules, the slice fit |
| `Game/Presentation/HpBarView.cs` | Game | **Rewrite of its drawing.** Rules 4–8, 10 |
| `Game/Presentation/HudPresenter.cs` | Game | **Edit.** `Place` lays out the star, the ring, the bar and the numbers; rules 2–4, 9 |
| `Tests/Game/Presentation/HpBarViewTests.cs` | Tests.Game | **New.** Rules 4–10 |
| `Tests/Game/Presentation/HudLayoutTests.cs` | Tests.Game | **New.** Rules 2–4, 11–12, 14 |
| *small edits* | Game | `XpBarView` (rule 1: 8 dp, a sliced fill in a well); `BossBarView.Place` (rule 11, placement only); `AutoCastRow._marginDp`'s default (rule 12); `Palette` gains three members (rule 13) |
| *small edits* | Tests.Game | `PaletteTests` (three rows, the count 20 → 23); `XpBarViewTests` (`Xp_StripIsFourDpAndFullWidth` → `Xp_StripIsEightDpAndFullWidth`; the fill rows read `Fraction`; `Level_IsPlacedInTheHudRow` → `Level_SitsInTheStar`; `Prefab_IsDressed`); `BossBarViewTests.Hud_StripAndBarDoNotOverlap` → `Hud_TheBandHangsUnderTheTopBand`; `GrantedShieldTests` (read `Fraction`, not `fillAmount`); `AutoCastRowTests.Row_IsPlacedInDp` |
| *assets* | | `Hud.prefab`: `HpBar` gains `Frame`, `Well` (holding `Ghost`, `Fill`, `Gloss`, `ShieldSegment`); `LevelStar` before `Level`; `XpStrip` gains `Well`; `HpText` and `Level` in `LilitaOne_SDF` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
public static class HudLayout                 // Soulvail.Game.Presentation, file-scoped, pure
{
    public const float MarginDp = 12f;        // the safe area's left and right edges to the first piece
    public const float StripHeightDp = 8f;    // the XP strip's track
    public const float RowTopDp = 13f;        // the star's, the pause button's and the stage chip's top
    public const float StarSizeDp = 44f;
    public const float GapDp = 4f;
    public const float BarHeightDp = 22f;
    public const float BarMaxWidthDp = 240f;
    public const float BarMinWidthDp = 120f;
    public const float RightReserveDp = 150f; // the pause button, the stage chip, their gaps and margin
    public const float BandBottomDp = 96f;    // nothing in the top band reaches lower
    public const float BossTopDp = BandBottomDp + GapDp;
    public const float BossMaxWidthDp = 400f;

    public static float BarWidth(float safeWidthDp, float leftDp, float maxDp, float rightReserveDp);
    public static float BossWidth(float safeWidthDp, float maxDp);
    public static void FitSlices(Image image); // pixelsPerUnitMultiplier = sprite height ÷ rect height
}

// HpBarView gains:
public const float LowFraction = 0.25f;
[SerializeField] private RectTransform _well;   // where the fills live, inset in the frame
[SerializeField] private Image _frame;          // optional; sliced
[SerializeField] private Image _gloss;          // optional; sliced
[SerializeField] private float _lowPulseHz = 2f;
public float Fraction { get; }                  // the fill as drawn
public float GhostFraction { get; }             // the ghost as drawn
public bool IsLow { get; }
public void Fit();                              // insets the well and fits the slices to the bar's height
private void Tick(float dt);                    // Update's body, driven by a fixture

// HudPresenter: _levelStar (Image), _starSizeDp 44, _barSizeDp (240, 22), _marginDp (12, 13),
// _gapDp 4, _ringSizeDp 52, _rightReserveDp 150. _textWidthDp is gone. HpFormat is "{0:0} / {1:0}".
// XpBarView: HeightDp = HudLayout.StripHeightDp; _well (RectTransform); public float Fraction is the drawn value.
// BossBarView: TopDp = HudLayout.BossTopDp; [SerializeField] float _maxWidthDp = HudLayout.BossMaxWidthDp.
// Palette gains PlayerGhost #A5F3FC, PlayerLow #0E7490, HudText #FFFFFF.
```

## Behaviour

1. **The XP strip is an 8 dp track across the safe area's top edge**, `XpStrip_Track` sliced. Its fill
   is `Fill_BarThin` in `Palette.Player`, in the track's well, as wide as the fraction of the well and
   hidden at 0. GD §16.1's 4 dp is the fill; the track frames it. `XpChanged` and the opening read are
   M3-10b's, unchanged.
2. **The level star is 44 dp**, `MarginDp` from the left and `RowTopDp` down, and the level is centred
   in it. Lilita, `Palette.HudText`, sized at 0.45 of the star. `LeveledUp` and the opening read are
   unchanged.
3. **The Aegis ring is concentric with the star, 52 dp, and behind it.** It is an earlier sibling, and
   it draws `PlayerShieldFraction` exactly as today.
4. **The health bar is 22 dp tall, on the star's centre line, `GapDp` right of it.** Its width is
   `BarWidth`: the room left of `RightReserveDp`, clamped to 120–240. Its frame is `HealthBar_Frame`,
   sliced, and the frame's dark well is the track. The well is inset 0.5 of the bar's height at each
   end and 0.23 top and bottom.
5. **The fill and the ghost are `Fill_Bar` pills in the well, each as wide as its fraction.** The fill
   is `Palette.Player`. The ghost is `Palette.PlayerGhost`, behind it, not the prefab's 50 % gold. Each
   is hidden at 0. The ghost keeps M1-17's clock: a hit holds it 0.4 s and restarts the hold, then it
   drains at 1.5 a second down to the fill; a heal snaps it; the first value snaps it.
6. **The gloss is `Gloss_Streak` over the fill's top half and follows its width.** It is inset a
   quarter of the well's height at each end, and hidden while the fill is narrower than the well is
   tall.
7. **A blocked hit flashes the fill `Palette.PlayerBlocked` for `_blockedSeconds` and moves nothing** —
   M1-17's rule, unchanged.
8. **Below a quarter, the fill pulses in the player's family.** While the fill is above 0 and below
   `LowFraction`, its colour moves between `Palette.Player` and `Palette.PlayerLow` at `_lowPulseHz`.
   Every colour it passes through is between the two, channel by channel, and none is danger. A
   blocked flash wins over the pulse. At `LowFraction` and above, and at 0, the fill is
   `Palette.Player`.
9. **The numbers are inside the bar**, centred on it, as `72 / 120`. Lilita, `Palette.HudText`, sized
   at 0.62 of the bar's height. TMP's float overload, as before: no allocation per hit.
10. **The granted-shield segment keeps M3-13b's rule**: ahead of the fill, pressed back against the
    well's end when there is no room. It is drawn in the well, in `Fill_Bar`, in
    `Palette.PlayerBlocked`.
11. **The boss band hangs under the top band.** Its top is `BossTopDp`. It is centred, and its width is
    `BossWidth`: at most 400 dp, and inside the margins. Its height, fill, seams and beat are M4-04's;
    the art is UI-01f's.
12. **The auto-cast row starts under the bar**, at (60, 52) dp: the bar's left edge, 6 dp under it.
    Its cells are otherwise unchanged until UI-01e.
13. **Three colours join `Palette`, and no view serializes one.** `PlayerGhost` `#A5F3FC` is the ghost:
    pale cyan, because gold means rewards. `PlayerLow` `#0E7490` is the low pulse's deep end.
    `HudText` `#FFFFFF` is every number on the band. Each has a `PaletteTests` row, and none is danger.
14. **The band holds at 20:9 and at 9:20.** Given a 914 × 411 dp or a 411 × 914 dp safe area, the
    strip, the star, the ring, the bar, the numbers, the level and the pause icon overlap nothing but
    their declared host: the level in the star, the ring round it, the numbers in the bar. Every piece
    lies inside the safe area, and none reaches below `BandBottomDp`. The boss band is below it and
    inside the safe area.
15. **A nonsense dp field leaves the authored layout alone**, piece by piece — `XpBarView.Place`'s rule,
    for every new field.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fill_IsTheFractionOfTheWell` | `Set(0.6)` / drawn / the fill's anchor max 0.6, `Fraction` 0.6 — rule 5 |
| `Fill_HidesAtZero` | `Set(0)` / drawn / fill and ghost disabled — rule 5 |
| `Ghost_HoldsForFourTenthsThenDrains` | 1 then 0.5 / ticks of 0.3, 0.1, 0.1, 1 / ghost 1, 0.85, 0.7, 0.5 (±0.01) — rule 5 |
| `Ghost_AHitRestartsTheHold` | a second hit at 0.3 s / 0.3 s more / ghost still 1 — rule 5 |
| `Ghost_AHealSnapsIt` | 0.5, drained, then 0.8 / drawn / ghost 0.8 at once — rule 5 |
| `Ghost_TheFirstValueSnapsIt` | a fresh bar / `Set(0.4)` / ghost 0.4 — rule 5 |
| `Ghost_IsPaleCyanNotGold` | a hit / drawn / the ghost's colour is `Palette.PlayerGhost` — rules 5, 13 |
| `Gloss_FollowsTheFill` | 0.7, then 0.02 / drawn / anchor max 0.7 and shown, then hidden — rule 6 |
| `Blocked_FlashesAndMovesNothing` | 0.8 / `FlashBlocked`, then 0.07 s / `PlayerBlocked`, `Fraction` 0.8, then `Player` — rule 7 |
| `Low_PulsesInThePlayersFamily` | 0.2 / 25 ticks of 0.02 s / each colour between `Player` and `PlayerLow`, at least two distinct, none danger — rule 8 |
| `Low_TheBlockedFlashWins` | 0.2 / `FlashBlocked` / `PlayerBlocked` — rule 8 |
| `Low_NotAtAQuarterNorAtZero` | 0.25, then 0 / ticks / `IsLow` false, `Player` — rule 8 |
| `Numbers_AreInsideTheBar` | a 72 / 120 run / `Start` / the text's rect is the bar's; it reads `72 / 120` — rule 9 |
| `Segment_LivesInTheWell` | a grant / drawn / the segment's parent is the well; its colour `PlayerBlocked` — rule 10 |
| `Prefab_IsDressed` | `Hud.prefab` / read / the art's sprites, fill and ghost sliced, `HpText` and `Level` in `LilitaOne_SDF`, `_lowPulseHz` 2 — rules 4–9 |
| `BarWidth_IsTheRoomLeftUpTo240` | 914, 411, 300 dp / `BarWidth(…, 60, 240, 150)` / 240, 201, 120 — rule 4 |
| `BossWidth_IsAtMost400InsideTheMargins` | 914, 411 / `BossWidth(…, 400)` / 400, 387 — rule 11 |
| `FitSlices_ScalesTheBordersWithTheHeight` | a 259 px sprite on a 22 px rect / fit / multiplier 259 / 22 — rule 4 |
| `Band_Landscape_NothingOverlaps` | the two prefabs at 914 × 411 dp / placed / rule 14's pairs, containment, the floor — rule 14 |
| `Band_Portrait_NothingOverlaps` | the two prefabs at 411 × 914 dp / placed / the same — rule 14 |
| `Band_TheStarRingAndBarShareACentreLine` | placed / read / three centres within 0.5 dp; the ring an earlier sibling — rules 2–4 |
| `Band_TheBossBandHangsBelowIt` | both sizes / placed / top at `BossTopDp`, centred, inside the margins — rule 11 |
| `Band_TheRowStartsUnderTheBar` | placed / the first cell / its left is the bar's, its top 6 dp under it — rule 12 |
| `Band_ANonFiniteFieldLeavesThePieceAlone` | `_starSizeDp` NaN, `_rightReserveDp` NaN / placed / those pieces keep their authored rects — rule 15 |
| `Ghost_IsPaleCyan`, `PlayerLow_IsADeeperCyan`, `HudText_IsWhite` (PaletteTests) | each / read / its hex; hue within 15° of `Player`'s for the first two; not danger — rule 13 |
| `Xp_StripIsEightDpAndFullWidth` (changed) | placed / read / 8 dp, stretched; the old row's 4 is the fill's height — rule 1 |
| `Hud_TheBandHangsUnderTheTopBand` (changed) | placed / read / the band's top ≥ `BandBottomDp` — rule 11 |

**Changed expectations, and why.** `Xp_StripIsFourDpAndFullWidth` becomes `Xp_StripIsEightDpAndFullWidth`
(rule 1). `Hud_StripAndBarDoNotOverlap` asserted a band at 5–15 dp above an HP row at 16; the band now
hangs under the whole top band (rule 11). The fill rows in `XpBarViewTests` and `GrantedShieldTests`
read `fillAmount`, which a sliced image does not draw; they read the view's `Fraction` (rules 1, 5).
`Level_IsPlacedInTheHudRow` asserted the level right of the ring; it sits in the star (rule 2).
`Row_IsPlacedInDp` compared the row with `_marginDp.y + _barSizeDp.y`, which is no longer the bar's
bottom (rule 12). The Palette count goes 20 → 23 (rule 13).

## Manual verification (Editor / device)

1. **[Editor]** Play a Ranger run and capture the Game view at 2400 × 1080 and at 1080 × 2400 through
   the Device Simulator, or at the dp-equivalent size when the simulator is unavailable. *Expected:
   the strip, star and bar as in `Hud_LevelUp.png`'s top band; the bar narrower in portrait.*
2. **[Editor]** Take hits down past a quarter. *Expected: a pale cyan ghost that lags and drains; the
   bar beating cyan to deep cyan below a quarter, never red-orange.*
3. **[device]** The band at 400 dpi: the numbers' legibility, the pulse's reading. Deferred with
   every device row ([ledger row 1](../ROADMAP.md#carry-forward-into-m7)).

## Out of scope

- **The chips, the pause button's art, the skill row's art and the boss bar's art:** UI-01d to UI-01f.
- **GD §16.3's desaturation vignette and heartbeat audio:** full-screen and audio, not the band.
- **The "+1" skill-point bubble on the star:** the new levelling flow.
- **Hiding the Aegis ring for a class with no Aegis:** it draws an empty ring today, and that is kept.

## As built

**As specified in behaviour, with five deviations in where things live.**

**Deviation 1: `Numbers_AreInsideTheBar` is in `GrantedShieldTests`, not `HpBarViewTests`.** The readout
is a run's numbers, and `HpBarViewTests` drives a bare bar off the prefab with no run, which is the
point of it. `GrantedShieldTests` already builds a run and a started presenter. The row asserts
`140 / 140` on its Oathbound, where the table said `72 / 120`.

**Deviation 2: one more changed expectation than the spec listed.** `Player_TextDoesNotMentionIt`
pinned the readout as `140/140`; rule 9 spaces it, so it reads `140 / 140`. `Row_IgnoresANonFiniteDpField`
restored the row's margin by typing the old `(16, 48)`; it now reads the shipped margin back first.

**Deviation 3: `HudLayout` has two more constants than its Public API**: `BarLeftDp` (60) and
`SkillRowTopDp` (52), the row's corner. `AutoCastRow._marginDp` defaults to them, because the
formatter refuses a field initialiser split across lines and one named number reads better anyway.

**Deviation 4: `HudLayoutTests` checks eight of the row's twelve cells.** At 24 dp, cells 10 to 12 of
a full row run under the pause corner at 9:20. The row packs from the front and no shipped class owns
more than three Actives, so the band's rows hold the first eight, and the tenth cell is a
[parking-lot](../ROADMAP.md#parking-lot) line. UI-01e re-reads the number at its 34 dp.

**Deviation 5: an extra row, `Band_TheBarTakesWhatTheRowLeaves`**, which pins rule 4's widths at both
sizes: 240 dp landscape, 201 dp at 9:20.

**Rule 15, resolved: a non-finite measurement answers the bar's full ceiling.** `BarWidth` reads a
ceiling that is not a usable width as `BarMaxWidthDp`. A NaN safe width, left edge or reserve answers
that ceiling outright. `HudPresenter.Place` places each piece only when every number it depends on is
usable, so a NaN star leaves the star, the level, the ring and the bar where the prefab put them.

**The prefab kept its objects.** `HpBar/Backing` became `Frame` and `XpStrip/Backing` became `Track`,
each re-dressed. `Well` is new under each, and `Gloss` and `LevelStar` are new. The fill, the ghost
and the segment moved into the bar's well, and the XP fill into the strip's. Every image a view tints
is authored white. `HpText` and `Level` are in `LilitaOne_SDF`, centred, and autosize is off, because
`Place` sizes both from dp: 0.62 of the bar and 0.45 of the star. The texts' authored point sizes are
untouched, so `Hud_NothingElseMoved` holds.

**The ghost's clock had never been driven by a row.** `Update` read `Time.deltaTime`, which does not
advance in EditMode. The body is now a private `Tick(float)`, `OverflowToast`'s arrangement, and six
rows drive it. The low beat is `Color.Lerp` between two palette members at `_lowPulseHz` (2). It
redraws the fill every frame while below a quarter, which is the one state where that is the point.

**The captures, and why they were taken two ways.** Landscape is the Device Simulator: a
2658 × 960 phone at 480 dpi with a 92 px notch, so a dp there is a phone's dp. The simulator refuses
portrait, because Player Settings allow landscape only, and changing that is a `ProjectSettings/`
change the owner has not asked for. So portrait is the Game view at 308 × 686, which at the Editor's
120 dpi is exactly a 9:20 phone's 411 × 914 dp, captured at 3× (softer, because overlay UI is
upscaled). Both are in `Soulvail_GeminiSamples/UI_v2/captures/`. The star, the bar with `90 / 90` and
the strip match `Hud_LevelUp.png`'s top band; the bar is 240 dp in landscape and 201 at 9:20. The
empty Aegis ring shows as a faint dark disc behind the star, as it drew an empty ring before; a
[parking-lot](../ROADMAP.md#parking-lot) line asks whether a class with no Aegis should draw one.
