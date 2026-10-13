# UI-02d — A fixed stick

**Size:** S · **Depends on:** UI-02b · **Branch:** `ui-02d-a-fixed-stick`
**Design refs:** CC §2.1–2.3, owner ruling 2026-10-13 · **Ledger rows:** none

## Goal

The movement stick sits at one fixed place on the screen, always visible, and a touch anywhere in the left 45 % reads as a deflection from that centre. The floating behaviour stays in the code behind a serialized flag.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Controls/FloatingStick.cs` | Game | `_floating` and `_fixedOffsetDp` fields; fixed mode reads from the fixed centre, never recentres, never hides |
| `Prefabs/UI/Hud.prefab` | — | Serializes `_floating: 0` and `_fixedOffsetDp: (110, 110)` on the stick |
| `Tests/Game/Controls/FloatingStickTests.cs` | Tests.Game | One row per rule; the rows that pinned floating move behind the flag |
| *small edits* | | `Docs/CoreCombat.md` §2.1 (origin row, max-radius row), `Docs/plan/ROADMAP.md`, `Docs/plan/PROGRESS.md` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

No public API changes. New serialized fields on `FloatingStick`: `bool _floating` (default `false`), `Vector2 _fixedOffsetDp` (default `110, 110`).

## Behaviour

1. **Fixed centre.** With `_floating` off, the centre is the region's bottom-left corner (the safe area's) plus `_fixedOffsetDp`, measured in dp, so it holds at 20:9 and 9:20 and at any density. It is recomputed while idle, so a layout change moves it.
2. **Always visible.** Base and knob show at the centre at rest, before any touch, and after release. The knob returns to the centre on release.
3. **Grab anywhere in the region.** A touch anywhere in the left 45 % captures the stick; deflection is measured from the fixed centre, not the touch point. Touching down away from the centre is already a deflection.
4. **No recentring.** Deadzone, full-speed threshold and radius stay 8 / 40 / 60 dp. Past 60 dp the origin does not move and the knob clamps at the rim (60 dp).
5. **Floating behind the flag.** With `_floating` on, behaviour is as UI-02b built it: hidden until touched, origin planted under the thumb, recentred past 60 dp, knob clamped at 40 dp, hidden on release.
6. **Unchanged.** Single capture (second pointer ignored), left-45 % region, `<Gamepad>/leftStick` routing, the dp sizes and the dressing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fixed_CentreIsRegionBottomLeftPlusOffsetInDp` | Fixed stick / spawned / `_originPx` is the region's bottom-left screen corner + 110 dp both axes — rule 1 |
| `Fixed_IsVisibleAtRest_AndAfterRelease` | Fixed stick / before touch, after touch and release / base and knob active, knob at centre — rule 2 |
| `Fixed_TouchAwayFromCentre_ReadsFromCentre` | Fixed stick / down 30 dp right of the centre / origin unchanged, value is `Shape((30, 0))` — rule 3 |
| `Fixed_DragPastRim_DoesNotRecentre_AndKnobClamps` | Fixed stick / drag 100 dp right / origin unchanged, value is full, knob 60 dp from centre — rule 4 |
| `Floating_HiddenUntilTouched` | `_floating` on / spawn, down, up / hidden, shown, hidden — rule 5 |
| `Floating_DragRecentres_WhenExceedingMaxRadius` | `_floating` on / drag 100 dp / origin sits 60 dp behind the touch — rule 5 |
| `SecondPointer_IsIgnored` | Either mode / second down and up / capture and origin unchanged — rule 6 |
| existing dressing, sizing, region and control-path rows | unchanged — rule 6 |

## Manual verification (Editor / device)

1. **[Editor]** Play: the stick shows at the bottom-left before any touch; a drag from anywhere in the left 45 % moves the Ranger from the stick's centre.
2. **[device]** Tune `_fixedOffsetDp` (default 110 × 110) for the thumb. Deferred with the rest of the device checks.

## Out of scope

- Virtual-gamepad routing, deadzone/cap/radius, the art.
- Deleting the floating code. A settings toggle for it.

## As built

As specified, with these resolutions:

- **Test names.** The three floating rows were renamed `Floating_*` and spawn with `_floating` on; `SecondPointer_IsIgnored` kept its `FloatingStick_` name and runs in fixed mode. Four new `Fixed_*` rows. Suite 3 763 / 3 762 / 0 / 1 inconclusive.
- **`_reported`.** EditMode has no virtual gamepad, so `SendValueToControl` writes nowhere. `FloatingStick.Report` wraps it and remembers the value; `Fixed_TouchAwayFromCentre_ReadsFromCentre` and `Fixed_DragPastRim_DoesNotRecentre_AndKnobClamps` read it by reflection.
- **Idle placement.** The region's layout is not final in `Awake` and changes on rotation, so a resting fixed stick re-places itself in `LateUpdate` (only while not held).
- **Rim.** The fixed knob clamps at 60 dp (`MaxRadiusDp`); the floating knob keeps its 40 dp clamp.
- **Docs.** CC §2.1 (origin, max radius, visuals) and §2.3's heading rewritten; no ADR, as nothing in Architecture.md changes.
