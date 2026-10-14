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

_Filled at merge._
