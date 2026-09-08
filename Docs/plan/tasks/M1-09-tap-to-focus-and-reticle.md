# M1-09 — `IPlayerCommands.FocusTarget/ClearFocus`, 3 m resolver, `TapToFocusAdapter`, `ReticleView`

**Size:** M · **Depends on:** M1-07, M1-08 · **Branch:** `m1-09-tap-to-focus-and-reticle`
**Design refs:** CC §3.4–3.6; AR §4.1 (commands), §6; GD §5.4

## Goal

The player can override auto-aim with one thumb-tap, and can always see what the gun is aimed at — including the "blocked, go around" state. The first inbound *command* port exists.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IPlayerCommands.cs` | Core | Inbound command port |
| `Core/Combat/FocusResolver.cs` | Core | Pure: world point → enemy id within radius |
| `Game/Adapters/TapToFocusAdapter.cs` | Game | Tap → ground point → command |
| `Game/Views/ReticleView.cs` | Game | Ring under the current target, three states |
| `Tests/Core/Combat/FocusResolverTests.cs` | Tests.Core | Resolver + command rules (via `RunSession`) |
| **Assets** | | `Prefabs/UI/Reticle.prefab` — flat ring quad, cyan; states via colour/scale |
| *small edits* | | `RunSession : IPlayerCommands` (forwards to `PlayerCombat`); `RunInstaller` registers `RunSession` as `IPlayerCommands` too; `InputAdapter` + `FocusPressedThisFrame`, `PointerPosition`; `RunScope` registers adapter + reticle; `PlayerCombat` + `FocusAt(point)` / `ClearFocus()`; `DebugOverlay` + `target {id} {focus/blocked}` line |

## Public API

```csharp
namespace Soulvail.Core.Ports;

public interface IPlayerCommands
{
    void FocusTarget(Vector3 worldPoint);   // resolves to an enemy within FocusRadius, else clears
    void ClearFocus();
}
```

```csharp
namespace Soulvail.Core.Combat;

public static class FocusResolver
{
    public const float RadiusMetres = 3f;
    /// Nearest living enemy within radius (XZ distance) or -1.
    public static int Resolve(Vector3 point, ReadOnlySpan<EnemyAgent> enemies, float radius);
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class TapToFocusAdapter
{
    public TapToFocusAdapter(InputAdapter input, IPlayerCommands commands, Camera camera);
    public void Poll();      // called by RunTicker's command phase, before the snapshot is built
}
```

```csharp
namespace Soulvail.Game.Views;

public sealed class ReticleView : MonoBehaviour
{
    [Inject] public void Construct(DomainEventHub hub, EnemyViews views);
}
```

## Behaviour

1. `FocusResolver.Resolve` ignores dead agents; ties → lowest id; empty → −1.
2. `PlayerCombat.FocusAt(point)`: `id = Resolve(point, enemies, RadiusMetres)`; `id >= 0` → `Targeter.Focus(id)`, else `Targeter.ClearFocus()`. Tapping empty ground clears (CC §3.4).
3. `TapToFocusAdapter.Tick`: when `input.FocusPressedThisFrame` and the pointer is **not** over a UI raycast target (`EventSystem.current.IsPointerOverGameObject(pointerId)` — this excludes the stick region and any button) → `camera.ScreenPointToRay(pointer)` intersected with the plane `y = 0` → `commands.FocusTarget(point)`. Taps over UI are ignored entirely.
   **Ordering:** commands must land before the session ticks. `TapToFocusAdapter` is therefore not a free-standing `ITickable`; `RunTicker` owns a small `CommandPhase` it runs first (`MovementSkill` press from M1-16 joins it), so the frame order is one list in one file, not an accident of registration order.
4. `ReticleView` subscribes to `TargetChanged`. Each `LateUpdate` it positions itself at `views[id].Position` (ground level) when `id >= 0`; hidden otherwise. States (GD §16.4): **auto** — cyan ring, 60 % alpha; **focused** — cyan, 100 %, pulsing scale 1.0↔1.15 at 2 Hz, plus a small chevron child; **blocked** — hollow ring (thin) with a "×" glyph child, still cyan (red is reserved for danger).
5. `ReticleView` allocates nothing per frame; the pulse is a `Mathf.Sin` on scale.
6. `RunSession.FocusTarget/ClearFocus` throw `InvalidOperationException` when not running (commands during menu are a bug, not a no-op).

## Tests

| Test | Given / When / Then |
|---|---|
| `Resolve_NearestWithinRadius` | enemies at (1,0,0), (2.5,0,0), (5,0,0); point origin / Resolve(r 3) / id of the one at 1 |
| `Resolve_NoneWithinRadius_MinusOne` | nearest at 3.1 / — / −1 |
| `Resolve_IgnoresDead` | dead at 0.5, alive at 2 / — / the alive one |
| `Resolve_Ties_LowestId` | two at equal distance / — / lower id |
| `FocusTarget_OnEnemy_SetsFocus` | running session, enemy at (4,0,0) / FocusTarget((4.5,0,0)), Tick / `Targeter.HasFocus`, `TargetChanged{IsFocused}` |
| `FocusTarget_OnEmptyGround_Clears` | focused / FocusTarget((30,0,30)), Tick / no focus |
| `ClearFocus_Clears` | focused / ClearFocus, Tick / no focus |
| `Commands_WhenNotRunning_Throw` | not started / FocusTarget / `InvalidOperationException` |

## Manual verification (Editor)

1. Play: ring appears under the auto-selected dummy; walk around — it switches sensibly and doesn't flicker.
2. Click (Editor) / tap (device, after M1-21) a far dummy → ring becomes bright and pulses on it; overlay shows `focus`.
3. Click empty ground → back to auto.
4. Click inside the stick region → nothing happens to focus.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Blocked-state enemies don't exist yet (Warden is M7-01); the blocked visual is verified by temporarily setting `IsVulnerable = false` on a dummy in a test scene, then reverted.
- Precision control mode (manual aim) — M8-02.

## As built

_Filled at merge._
