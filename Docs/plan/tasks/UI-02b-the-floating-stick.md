# UI-02b — The floating stick in the new art

**Size:** S · **Depends on:** UI-02a · **Branch:** `ui-02b-the-floating-stick`
**Design refs:** CC §2.1–2.3; GD §16.4; CLAUDE.md · **Ledger rows:** none

## Goal

The floating virtual stick on `Hud.prefab` wears `Stick_Base` and `Stick_Knob` in the new Clash of Clans–style
art, preserving the 120 dp base and 52 dp knob dimensions, dynamic touch-down placement, and drag recentring.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/UI/Hud.prefab` | — | `FloatingStick`'s `_base` and `_knob` wear `Stick_Base` and `Stick_Knob` |
| `Game/Controls/FloatingStick.cs` | Game | Any aspect adjustments or tinting resets for the new sprites |
| `Tests/Game/Controls/FloatingStickTests.cs` | Tests.Game | **New.** Verifies stick dressing, hierarchy, and runtime dimensions |
| *small edits* | | `Docs/plan/ROADMAP.md`, `Docs/plan/PROGRESS.md` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

No public API changes.

## Behaviour

1. **`_base` wears `Stick_Base.png`.** The base image uses the imported sprite, with native aspect preserved,
   sized to `BaseDiameterDp` (120 dp) via `StickShaper.PixelsPerDp`.
2. **`_knob` wears `Stick_Knob.png`.** The knob image uses the imported sprite, with native aspect preserved,
   sized to `KnobDiameterDp` (52 dp) via `StickShaper.PixelsPerDp`.
3. **Floating behavior preserved.** Touch-down plants origin at the touch point within the left 45% region;
   drag recentres when exceeding `MaxRadiusDp` (60 dp); touch-up hides both elements.
4. **Input routing unchanged.** Deflection continues to write `<Gamepad>/leftStick` on the virtual gamepad.

## Tests

| Test | Given / When / Then |
|---|---|
| `FloatingStick_BaseWearsStickBase` | `Hud.prefab` / `_base` Image inspected / sprite is `Stick_Base` — rule 1 |
| `FloatingStick_KnobWearsStickKnob` | `Hud.prefab` / `_knob` Image inspected / sprite is `Stick_Knob` — rule 2 |
| `FloatingStick_SizingMatchesDpSpecification` | stick awakened with test DPI / base and knob dimensions / 120 dp and 52 dp — rules 1, 2 |
| `FloatingStick_HiddenUntilTouched` | stick awakened / activeSelf or canvas group / invisible until pointer down — rule 3 |

## Manual verification (Editor / device)

1. **[Editor]** Enter PlayMode in `Run.unity`. Touch/drag on the left side of the screen. Confirm the stick
   base and knob appear under the cursor with the new Clash-style art and follow the pointer smoothly.
2. **[device]** Confirm one-thumb steering feel and recentring visual tracking at 400 dpi.

## Out of scope

- Right-hand controls (Roll and manual skill buttons belong to UI-02c).
- Portrait safe-area stick relocation.

## As built

_Filled at merge._
