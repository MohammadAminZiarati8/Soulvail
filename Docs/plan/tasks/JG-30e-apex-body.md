# JG-30e — The Apex drawn: eyes, feint, dash streak, window, roar

**Size:** M · **Depends on:** JG-30c, JG-30d · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 7; readable at first look; colour law; `GravemawView`, `BossViews` (lanes), `TelegraphRings` (leap ring)

## Goal

Every Apex move reads at a glance on a phone. Above all, a real pounce (lit eyes, ring) never looks
like a feint (dark eyes, tail flick).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ApexView.cs` | Game | **New.** Drives animator, eyes, streak from events (block namespace) |
| `Animation/Controllers/AC_Apex.controller` | — | **New.** The ten clips, triggers below |
| `Prefabs/Vfx/FX_Apex_Streak.prefab` | — | **New.** Dark trail while dashing or leaping |
| `Prefabs/Enemies/Apex.prefab` | — | Gains `ApexView`, controller, eye glow on `Head`, streak |
| `Tests/Game/Views/ApexViewTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Clips from events:** `LaneArmed` with her id plays `Crouch` low, then `Dash` on `LaneFired`.
   `EnemyTelegraph` move 2 plays `Rake`. `ApexCrouched` plays `Crouch`; the leap plays `Pounce`;
   `ApexLanded` plays `Land`. `BossPhaseChanged` plays `Roar`; hits and death as `EnemyView`'s.
   Otherwise `Run` above 0.5 m/s, else `Idle`.
2. **The tell:** `ApexCrouched(isFeint: false)` lights both eyes in `Palette.Danger` with a bloom
   readable at phone zoom, until landing. The ring is `TelegraphRings`' leap ring, unchanged. A feint
   leaves the eyes dark and flicks the tail. Nothing else tells the two apart, so the eyes must.
3. **Lanes** are `BossViews`' strips, unchanged, cut at the stone the probe hit.
4. **Streak:** `FX_Apex_Streak` emits only while dashing or airborne. It is dark violet, never cyan
   or Danger.
5. **Window:** during `ApexLanded`'s window she pants, head low, and her body brightens 15 %, so
   "hit her now" reads. It ends with the window.
6. **Colour law:** nothing she shows is in the cyan band; Danger only on the eyes and telegraphs.
7. Pooled: every state clears on despawn; no allocation per frame.

## Tests

| Test | Given / When / Then |
|---|---|
| `Lane_PlaysCrouchThenDash` / `Rake_Telegraph_PlaysRake` / `Crouch_Pounce_Land_Play` / `Phase_PlaysRoar` (1) |
| `RealCrouch_EyesLitDanger_UntilLanding` / `Feint_EyesDark_TailFlicks` (2) |
| `Streak_OnlyWhileDashingOrAirborne` / `Streak_NotCyanNotDanger` (4) |
| `Landed_BrightensForWindow_ThenRestores` (5) |
| `Despawn_ClearsEyesStreakAndWindow` (7) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 to stage 30: lit-eye pounces and dark-eye feints tell apart at phone zoom
   without looking for them.
2. **[Editor]** A lane aimed through a stone ends at the stone; she stops there.
3. **[device]** The eyes read on a 6-inch screen in daylight.

## Out of scope

- Audio (none yet); data, scale and stage (30f).
