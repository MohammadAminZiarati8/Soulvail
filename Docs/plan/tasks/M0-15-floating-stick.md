# M0-15 — `StickShaper` (pure) + `FloatingStick` control + HUD prefab

**Size:** M · **Depends on:** M0-14 · **Branch:** `m0-15-floating-stick`
**Design refs:** CC §2.1–2.3, §7; AR §8; GD §5.2

## Goal

The one-thumb control the whole game rests on: a floating joystick with the deadzone / analog band / **dynamic recentering** rules from CoreCombat, with the math isolated in a pure class that is unit-tested to the dp.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Controls/StickShaper.cs` | Game | Pure static: shape a drag vector, recenter an origin, dp conversion |
| `Game/Controls/FloatingStick.cs` | Game | `OnScreenControl` + pointer handlers + visuals |
| `Prefabs/UI/Hud.prefab` | — | Canvas, safe-area container, VJR region, stick base + knob |
| `Tests/Game/Controls/StickShaperTests.cs` | Tests.Game | Every number in the spec |

## Public API

```csharp
namespace Soulvail.Game.Controls;

public static class StickShaper
{
    public const float DeadzoneDp  = 8f;
    public const float FullSpeedDp = 40f;
    public const float MaxRadiusDp = 60f;

    /// dragDp = touch − origin, in dp. Returns direction × t, t ∈ [0, 1].
    public static Vector2 Shape(Vector2 dragDp);

    /// If |touch − origin| > MaxRadiusDp, returns the origin moved so the distance is exactly MaxRadiusDp
    /// along the same direction; otherwise returns origin unchanged.
    public static Vector2 Recenter(Vector2 originDp, Vector2 touchDp);

    /// dpi / 160. dpi <= 0 (Editor, unknown) → 1.
    public static float PixelsPerDp(float dpi);
}

public sealed class FloatingStick : OnScreenControl,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [InputControl(layout = "Vector2")] [SerializeField] private string _controlPath = "<Gamepad>/leftStick";
    [SerializeField] private RectTransform _region;   // the VJR; this component sits on it
    [SerializeField] private RectTransform _base;
    [SerializeField] private RectTransform _knob;
    protected override string controlPathInternal { get; set; }
}
```

## Behaviour

**StickShaper**
1. `Shape`: `d = |dragDp|`. `d <= DeadzoneDp` → zero. `d >= FullSpeedDp` → unit direction. Between → direction × `(d − Deadzone) / (FullSpeed − Deadzone)`. Direction preserved exactly.
2. `Recenter`: as documented above. Distance exactly `MaxRadiusDp` after recentering (± float epsilon).
3. `PixelsPerDp(160) == 1`, `(320) == 2`, `(0) == 1`, `(-1) == 1`.

**FloatingStick**
4. Pointer down inside `_region`: if no pointer is active, capture this `pointerId`; origin = pointer position (screen px); show `_base` at origin and `_knob` on it; send **zero**. Other pointers are ignored while one is active (multi-touch: buttons elsewhere still work because they're separate raycast targets).
5. Drag with the captured pointer: `pxPerDp = PixelsPerDp(Screen.dpi)`; `originPx = Recenter(originPx / pxPerDp, touchPx / pxPerDp) × pxPerDp`; `value = Shape((touchPx − originPx) / pxPerDp)`; `SendValueToControl(value)`. `_base` follows the (possibly recentered) origin; `_knob` = origin + clamp(delta, `FullSpeedDp × pxPerDp`).
6. Pointer up of the captured pointer: `SendValueToControl(Vector2.zero)`, hide visuals, release capture. Ups from other pointers are ignored.
7. `OnDisable`: send zero, release capture, hide visuals — no stuck input when the HUD is disabled mid-drag.
8. The stick never reads the Input System; it only feeds `<Gamepad>/leftStick`.
9. No per-frame allocation.

**Hud prefab**
10. Canvas: Screen Space – Overlay, Canvas Scaler *Scale With Screen Size*, reference 1920×1080, match 0.5. A `SafeArea` child RectTransform is fitted to `Screen.safeArea` on enable and on resolution change.
11. `Region` RectTransform inside SafeArea: anchors `(0,0)`–`(0.45,1)`, transparent `Image` with `raycastTarget = true` (it must catch pointer events). `Base` 120 dp ring at 60 % alpha, `Knob` 52 dp disc — both hidden until touch.

## Tests

| Test | Given / When / Then |
|---|---|
| `Shape_BelowDeadzone_IsZero` | (5, 0), (0, 7.9) / Shape / zero |
| `Shape_AtDeadzone_IsZero` | (8, 0) / — / zero |
| `Shape_MidBand_IsLinear` | (24, 0) / — / (0.5, 0) ± 1e-4 |
| `Shape_AtFullSpeed_IsUnit` | (40, 0) / — / (1, 0) |
| `Shape_BeyondFullSpeed_IsUnit_DirectionKept` | (0, 100), (30, 40) / — / (0, 1); (0.6, 0.8) ± 1e-4 |
| `Shape_PreservesDirectionInBand` | (12, 16) (d = 20) / — / direction (0.6, 0.8), magnitude 0.375 |
| `Recenter_WithinRadius_Unchanged` | origin (100,100), touch (150,100) / — / (100,100) |
| `Recenter_AtRadius_Unchanged` | touch 60 dp away / — / unchanged |
| `Recenter_BeyondRadius_MovesOrigin` | origin (0,0), touch (100,0) / — / (40,0); distance to touch == 60 |
| `Recenter_BeyondRadius_Diagonal` | origin (0,0), touch (60,80) (d=100) / — / (24,32) |
| `PixelsPerDp_Values` | 160, 320, 0, −1 / — / 1, 2, 1, 1 |

## Manual verification (Editor first, then device after M0-16)

**In the Editor:** Window → General → **Device Simulator** with a 1080p phone profile (it converts the mouse to a single touch and applies the profile's DPI and safe area). Steps 1, 2, 3 and 5 are verifiable there; step 4 (multi-touch) is **device-only** and stays deferred until a phone exists.

1. Touch anywhere in the left 45 % — base and knob appear under the thumb.
2. Drag right 200 px then back left: movement reverses **immediately** (recentering).
3. Tiny drags (< 8 dp) do nothing; ~24 dp moves at half speed; ≥ 40 dp full speed.
4. Hold the stick with one thumb and tap the right half with another finger — the stick keeps working.
5. Lift the thumb — capsule stops within ~0.1 s, visuals hide.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Prefab has every `.meta`; no scene is modified in this task
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Placing the HUD in the Run scene and the `EventSystem` — M0-16.
- Stick size/opacity/handedness settings (GD §18) — M8-02.
- Right-side buttons — M1-13.

## As built

**Files — five, not four.** `Game/Controls/SafeAreaFitter.cs` was added with the owner's approval, raised before it was written: rule 10 asks the `SafeArea` child to track `Screen.safeArea`, Unity ships no component that does, and folding it into `FloatingStick` would make every later HUD element under that node depend on the stick existing. Still size M.

| Path (under `Assets/_Project/`) | Assembly | Status |
|---|---|---|
| `Game/Controls/StickShaper.cs` | Game | As specced |
| `Game/Controls/FloatingStick.cs` | Game | As specced, plus runtime dp sizing (below) |
| `Game/Controls/SafeAreaFitter.cs` | Game | **Added** — rule 10's fitting behaviour |
| `Prefabs/UI/Hud.prefab` | — | As specced, base is a disc not a ring (below) |
| `Tests/Game/Controls/StickShaperTests.cs` | Tests.Game | 11 specced rows + 1 allocation test |

**Public API:** exactly as written above, no additions. `FloatingStick` gained only private members.

**Three other deviations**, all recorded in full in the PROGRESS entry:

1. **Rule 11's dp sizes are applied in `OnPointerDown`, not authored in the prefab.** A *Scale With Screen Size* canvas measures reference pixels, so a `sizeDelta` of 120 is 48 dp on a 400 dpi phone — and the ring's diameter is exactly `2 × MaxRadiusDp`, so at the wrong scale it misreports where recentring begins. Sized as `dp × pxPerDp ÷ canvas.scaleFactor`.
2. **`Base` is a 60 %-alpha disc rather than a ring.** No built-in ring sprite exists and M0 adds no art; both sprites are Unity's built-in `UI/Skin/Knob.psd`, distinguished by alpha. No asset file added.
3. **A twelfth test, `ShapeAndRecenter_DoNotAllocate`.** Behaviour rule 9 is the only rule the Tests table leaves unmeasured. It covers the `StickShaper` calls the drag path makes per frame; its remark records that the component's own frame — `RectTransformUtility`, event dispatch — is not covered.

**Verified:** 148 EditMode tests green (136 before), zero errors, zero new analyzer warnings, `MonoScript.GetClass()` resolves `FloatingStick` and `SafeAreaFitter`, `Hud.prefab` loads with `_region`/`_base`/`_knob` all wired and no `m_Script: {fileID: 0}`, every `.meta` present, no scene touched.

**Manual verification: not yet run.** Steps 1, 2, 3 and 5 need the Device Simulator and are the owner's to walk; step 4 (multi-touch) stays deferred until a phone exists. Note that the stick cannot be exercised until M0-16 places `Hud.prefab` in a scene and adds an `EventSystem` — so in practice all five steps happen during M0-16, which is what the spec's heading already anticipated.

**Follow-ups:** `SafeAreaFitter` has no tests (its anchor math is reachable only through `Screen`; M0-16 decides whether to extract a pure static or leave it to the simulator's notch profiles), and behaviour rules 4–9 are hand-verified — M0-16 is the first task with the scene, canvas and `EventSystem` a PlayMode test would need.
