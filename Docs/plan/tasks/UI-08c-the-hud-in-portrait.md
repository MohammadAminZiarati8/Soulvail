# UI-08c — The HUD in portrait

**Size:** M · **Depends on:** UI-08a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** CC §2.1, GD §16.4 · **Ledger rows:** none

## Goal

The run's HUD lays out on a portrait phone — the top band across the top, the stick and the buttons in the thumbs' reach at the bottom — and the development readout no longer covers the XP strip.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HudPresenter.cs` | Game | the lower controls placed from the safe area's bottom edge in portrait |
| `Game/Presentation/DebugOverlay.cs` | Game | docks below the top band (finding 6) |
| `Tests/Game/Presentation/HudLayoutTests.cs` | Tests.Game | the portrait rows added |
| *small edits* | | `Hud.prefab` and `DebugOverlay.prefab`: `CanvasScaler` reference 1080 × 1920 |

## Behaviour

1. **Every run canvas's `CanvasScaler` is referenced 1080 × 1920**, match 0.5, so a font authored in reference px reads at its intended size on a portrait phone.
2. **The top band is unchanged in dp** — UI-01b already lays it out from the safe area's top edge and `HudLayoutTests` already checks 9:20.
3. **The fixed stick sits in the bottom-left quarter, the Roll and skill buttons in the bottom-right**, each at least 24 dp from the safe area's edges; the stick's touch zone is the left half of the bottom 40 % of the safe area (UI-02d's fixed stick, the zone re-derived for portrait).
4. **The `DebugOverlay`'s text sits below the top band**, never over the XP strip; in a release build the overlay is absent as today.
5. **Nothing overlaps at 1080 × 2400 @ 420 dpi or 720 × 1600 @ 320 dpi**: top band, threat arrows' inset, stick, Roll, skill buttons, boss bar.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hud_Canvas_IsReferencedPortrait` | rule 1 |
| `Hud_Controls_SitInTheThumbZones` | rule 3, both sizes |
| `Hud_DebugOverlay_ClearsTheTopBand` | rule 4 |
| `Hud_Portrait_NothingOverlaps` | rules 2, 5, both sizes |

## Manual verification

1. **[Editor]** UI-07b's capture of a run at both sizes: the band whole, the controls at the bottom, the debug text under the band.
2. **[device]** Both thumbs reach their controls without covering the Ranger.

## Out of scope

New art, the boss bar's own placement beyond rule 5, the camera (UI-08b), and the overlay screens (UI-08e).

## As built

**Built.** `HudPresenter.Place` ends in `PlaceLowerControls`. In portrait (the safe area taller than it is wide) it re-anchors `_stickZone`, FloatingStick's `Region`, to `(0, 0)–(0.5, 0.4)`. It also always pins `_arrowLane`, the `Arrows` rect `ThreatArrows` rides, to the safe area with its top `ArrowLaneTopDp` (126 dp) down. `UnderBossDp` (122 = boss top 100 + 18 + a 4 dp gap) is where `DebugOverlay.Place` hangs its line. That line now stretches across the safe area less 12 dp margins, and wraps. The stick, the Roll and the skill buttons still place themselves. The zone's bottom-left corner is the safe area's in both orientations, so the stick's fixed centre does not move, and `FloatingStick.LateUpdate` re-places its sprites every resting frame. Both canvases' `CanvasScaler` read 1080 × 1920, match 0.5. At match 0.5 the scale is `√(w·h ÷ 1920·1080)` either way, so rule 1 changes no pixel. Four rows in `HudLayoutTests`. The two phones are worked out from px and dpi as 411 × 914 and 360 × 800 dp.

**Deviations.**
1. **The skill cluster moved, on `Hud.prefab`'s `_offsetsDp`.** CC §6.2's quarter arc (radius 140 around the Roll) puts S1 236 dp in from the right edge. At 360 dp that is across the middle and over the stick's ring. No arc of four 72 dp-spaced buttons fits the right half there. The four sit in a 2 × 2 grid above the Roll: `(−24, 78) (48, 78) (−24, 150) (48, 150)` dp from its corner, nearest first. The grid spans 42–174 dp in from the right and 144–276 dp up, every pair at least 72 dp apart and 78 from the Roll, which `Button_SpacingMeetsTheMinimum` still checks. The first tally put a staggered layout's S1 box over the Roll's corner, which is why it is a grid. CC §6.2's drawing, `SkillBarPresenter`'s tooltip and its "236 dp" remark now describe the old arc. They are outside the table and were left alone.
2. **Two more fields wired on `Hud.prefab`** (`_stickZone`, `_arrowLane`) beyond the table's scaler edit. **The prefab's `Region` keeps UI-02d's left 45 %, full height.** `FloatingStickTests` reads the asset, so the portrait zone is applied at runtime.
3. **Rule 5's arrows are a lane, not an inset constant.** `ThreatArrows` is outside the table. It holds an arrow's centre 28 canvas units inside its rect, and a 48-unit arrow at 1.15× turned 45° reaches 39 units from there. That is 11 units, about 5 dp, past the rect's top. So the lane's top sits one more gap under the boss band, and the row measures that reach on each phone's canvas scale.
4. **The rule-2/5 band check uses what a shipped run draws**: volley plus three Active cells, and without the Essence and Veilrot readouts every shipped mode switches off. At 360 dp a sixth Active's cell reaches 3 dp under the pause icon, and the Veilrot caption overlaps the level-up toast. Both are parking-lot lines. The existing `Band_*` rows keep six cells at 411 dp.
5. **No `Capture Look`.** No Device Simulator was opened. It edits `Packages/manifest.json`, and the Game view would have to close. Manual step 1 is the owner's.

**Stale beside this.** Traps §9 still quotes `Hud.prefab`'s scaler as 1920 × 1080 for its legibility arithmetic. The arithmetic is unchanged at match 0.5.

**Judgement calls for the owner.** The grid in place of the arc, and whether the right thumb reaches 276 dp up, are device questions ([M7 row 1](../ROADMAP.md#carry-forward-into-m7)). Arrows now never draw over the top band; at the top edge they ride 126 dp down.
