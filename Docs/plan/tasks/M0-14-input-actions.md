# M0-14 — `Soulvail.inputactions`, generated class, `InputAdapter`

**Size:** S · **Depends on:** M0-01 · **Branch:** `m0-14-input-actions`
**Design refs:** AR §8; CC §5.2; GD §5.2

## Goal

One input actions asset owns every action the game will ever read, and a single adapter is the only place Game code reads it — so control modes and on-screen controls are swappable without touching anything downstream.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Input/Soulvail.inputactions` | — | The actions asset (Generate C# Class → `SoulvailActions`, namespace `Soulvail.Game.Input`) |
| `Game/Input/SoulvailActions.cs` | Game | Generated; committed |
| `Game/Adapters/InputAdapter.cs` | Game | Reads `Move`; owns enable/disable |
| `Tests/Game/Adapters/InputAdapterTests.cs` | Tests.Game | Via `InputTestFixture` |
| `Tests/Game/Soulvail.Tests.Game.asmdef` | Tests.Game | *modified:* add refs `Unity.InputSystem`, `Unity.InputSystem.TestFramework` |

**Removed:** `Assets/InputSystem_Actions.inputactions` (+meta). Project Settings → Input System → *Project-wide Actions* set to `Soulvail.inputactions`.

## Actions asset

Map **Player** (the only map for now):

| Action | Type | Bindings |
|---|---|---|
| `Move` | Value / Vector2 | `<Gamepad>/leftStick` · 2D composite `<Keyboard>` WASD |
| `MovementSkill` | Button | `<Gamepad>/buttonSouth` · `<Keyboard>/space` |
| `Skill1`…`Skill4` | Button | `<Gamepad>/buttonWest`, `/buttonNorth`, `/leftShoulder`, `/rightShoulder` · `<Keyboard>/1`…`/4` |
| `Focus` | Button | `<Pointer>/press` |
| `Pause` | Button | `<Keyboard>/escape` · `<Gamepad>/start` |

Control schemes: **Touch** (`Gamepad` — the on-screen stick emits as a virtual gamepad, `Touchscreen`, `Pointer`), **KeyboardMouse**. Only `Move` is consumed in M0; the rest exist so later tasks add readers, not bindings.

## Public API

```csharp
namespace Soulvail.Game.Adapters;

public sealed class InputAdapter : IDisposable
{
    public InputAdapter();                     // creates SoulvailActions; disabled until Enable()
    public Vector2 Move { get; }               // Player.Move value, magnitude clamped to 1
    public bool IsEnabled { get; }
    public void Enable();
    public void Disable();
    public void Dispose();                     // Disable + dispose the actions
}
```

## Behaviour

1. `Move` returns `Vector2.ClampMagnitude(Player.Move.ReadValue<Vector2>(), 1f)`; returns zero while disabled.
2. `Enable`/`Disable` are idempotent and only toggle the `Player` map.
3. `Dispose` disables and disposes the generated actions; `Move` after dispose returns zero, nothing throws.
4. Nothing else in `Game` calls `InputSystem`, `Touchscreen`, or the generated class directly. `FloatingStick` (M0-15) *feeds* the system through `OnScreenControl`; it does not read it.

## Tests

`InputAdapterTests : InputTestFixture` (the Input System's EditMode fixture; it installs a clean input state per test).

| Test | Given / When / Then |
|---|---|
| `Move_ReflectsGamepadLeftStick` | `InputSystem.AddDevice<Gamepad>()`, adapter enabled / `Set(gamepad.leftStick, (0.3, 0.6))`, `InputSystem.Update()` / `Move ≈ (0.3, 0.6)` |
| `Move_IsClampedToUnit` | stick at (1, 1) (magnitude 1.41) / — / `|Move| == 1 ± 1e-4` |
| `Move_ZeroWhileDisabled` | stick at (0.5, 0) / Disable / `Move == zero`; Enable / value returns |
| `EnableDisable_Idempotent` | — / Enable ×2, Disable ×2 / no throw; `IsEnabled` reflects last call |
| `Dispose_ThenMove_ReturnsZero` | disposed / `Move` / zero, no exception |

## Manual verification (Editor)

1. Open `Soulvail.inputactions`: one map, eight actions, two schemes, as tabled.
2. Project Settings → Input System Package → Project-wide Actions shows `Soulvail.inputactions`.

## Acceptance

- [x] All tests green
- [x] Zero errors, zero new analyzer warnings
- [x] Old template asset and its `.meta` removed
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Reading any action other than `Move` — M1-08 (`Focus`), M1-13 (`MovementSkill`), M3-10 (`Skill1–4`).
- Rebinding UI, control-mode switching (GD §5.3) — M8-02.

## As built

Five files as tabled, plus `ProjectSettings/EditorBuildSettings.asset` (the project-wide actions
config object, which is where that setting actually lives) and the removal of
`Assets/InputSystem_Actions.inputactions` + `.meta`. 136 EditMode tests, up from 131. Zero errors,
zero new warnings — the three the Console shows are the deliberate ones other fixtures assert on.

**Deviations:**

1. **`InputAdapter.Dispose` does not delegate to the generated `SoulvailActions.Dispose()`.** That
   method calls `Object.Destroy` unconditionally. In the Editor outside play mode `Object.Destroy`
   destroys nothing and logs an **error** (it does not throw), so delegating would leak the asset
   *and* fail `Dispose_ThenMove_ReturnsZero` on an unexpected error log. The adapter destroys
   `_actions.asset` itself, using `DestroyImmediate` when `!Application.isPlaying` under
   `#if UNITY_EDITOR`. The Public API block above is unchanged.
2. **The test fixture sets `defaultDeadzoneMin = 0` / `defaultDeadzoneMax = 1` in setup.** Behaviour
   rule 1's `Move ≈ (0.3, 0.6)` is not reachable otherwise: `StickDeadzone` at its defaults rescales
   that stick position to ≈(0.305, 0.610), and `InputTestFixture` does not neutralise deadzones.
   The fixture does snapshot and restore `InputSystem.settings`, so this cannot escape the test.
3. **`Move_IsClampedToUnit` carries a third assertion recording what it does not prove.**
   `StickDeadzone` returns exactly 1 for any magnitude above `max`, so a gamepad stick can never
   hand the adapter a value outside the unit disc and this row would pass with `ClampMagnitude`
   deleted. The row's Given ("magnitude 1.41") describes a value the action never emits. Kept as a
   contract test, with the pre-clamp asserted so the gap is on the record rather than latent; the
   clamp itself stays because both sources being unit-bounded is a fact about today's bindings.
4. **`Move_ZeroWhileDisabled` and `Dispose_ThenMove_ReturnsZero` call `InputSystem.Update()` after
   each `Enable`.** A Value action runs its initial state check on the update *after* being enabled,
   so an action enabled while the stick is held reads zero until then. Not an adapter defect, but a
   one-frame lag M0-16 will inherit.
5. **Additive, worth naming:** the composite binding is named `WASD` (matching what the Inspector
   would write), the `Touch` scheme's three devices are OR'd rather than AND'd, and `Mouse` is
   optional in `KeyboardMouse` so a keyboard alone satisfies it.

**Method:** the asset was built through `InputActionSetupExtensions` and serialised with
`ToJson()`, and the wrapper was produced by the importer via a hand-written `.meta` with
`generateWrapperCode: 1` — neither file's format is hand-authored.
