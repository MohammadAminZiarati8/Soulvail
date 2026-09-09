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

- [x] All tests green — 317 EditMode, 0 failed
- [x] Zero errors, zero new analyzer warnings
- [x] Manual steps verified — 1 and 4 in Play mode by probe, 2 by the owner playtesting (clicking different dummies switches the target); 3 not separately called out
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Blocked-state enemies don't exist yet (Warden is M7-01); the blocked visual is verified by temporarily setting `IsVulnerable = false` on a dummy in a test scene, then reverted.
- Precision control mode (manual aim) — M8-02.

## As built

**Six deviations, each small and each said out loud.**

1. **`PlayerCombat.FocusAt` takes the enemy span**, not just a point: `FocusAt(Vector3 worldPoint, ReadOnlySpan<EnemyAgent> enemies)`. `PlayerCombat` owns no registry — `Tick` is handed the world each time — and a retained span would dangle the moment anything spawned. `RunSession` passes `State.Enemies.Registry.Alive`, the same span it hands `Tick`.
2. **`TapToFocusAdapter.Poll`, not `Tick`.** The Public API block says `Poll`; behaviour rule 3 says `Tick`. `Poll` won, because the spec's own point is that this is *not* an `ITickable` and `Tick` would suggest it is.
3. **The UI hit test is `EventSystem.RaycastAll`, not `IsPointerOverGameObject(pointerId)`.** That overload needs a pointer id that is `kMouseLeftId` for a mouse and the *touch id* for a finger, so it has to be guessed per device — and the wrong guess fails open: the tap goes through and dragging the stick re-focuses whatever is under it. Raycasting the position the adapter already holds has no id to get wrong. Verified live: a probe at x = 0.2 hits `Region` (tap ignored), at x = 0.75 and 0.95 hits nothing (tap reaches the arena).
4. **Two assets beyond the table.** `Materials/M_Reticle.mat` — a transparent URP/Unlit the prefab's four line renderers share; the table said "flat ring quad, cyan" without naming the material a quad would need either way. And the `Reticle` instance dressed into `Run.unity`, without which `RunScope` has nothing to register.
5. **`RunScope` gained a `Camera` field.** `TapToFocusAdapter` needs one and `Camera.main` is a tagged scene lookup — `FindObjectOfType` wearing a hat. Guarded like the player view rather than optional: without it the scope cannot compose at all.
6. **The reticle is four `LineRenderer`s, not a ring quad.** A quad needs a ring *texture*, which is a fifth asset and a hand-drawn one; line renderers give a true ring whose width is a number, which is exactly what the blocked state needs ("hollow, thin"). Geometry is generated in `Awake` from the serialized radius, so the prefab carries no baked circle to disagree with it.

**Two additions beyond the Tests table**, both named in the files: three extra resolver rows (empty span, meaningless radius, XZ-only distance — the radius one earns its place, since −1 squared is 1 and without a guard a nonsense radius becomes a plausible one-metre one), and `Run_SessionAndCommands_SameInstance` in `InstallerTests`, which belongs there rather than here because it catches a registration rather than a rule.

**The command rows assert through `TargetChanged`, not through `Targeter`.** `RunState.Combat` and `.Enemies` are `internal` and `Soulvail.Tests.Core` has no `InternalsVisibleTo` (M0-10), so the event *is* the public surface — which is the right thing to be testing anyway, since it is what the reticle and the overlay both read.

**Verified in Play mode**, on the Run scene with eight dummies: the ring appears under the auto-selected dummy at ground level (`reticle pos = (8.00, 0.02, 0.00)`, ring on, chevron off, × off, scale 1.000), the overlay reads `target 1`, and a tap switches it to the focused look — chevron on, scale pulsing at 1.148 — with the overlay reading `target 1 focus`. Manual steps 2 and 3 (tapping a far dummy, tapping empty ground, both by hand) are the owner's; synthetic input could not be made deterministic with a live physical mouse present, since a real device event overwrites the queued state in the same update.

**The owner playtested it and found the one rough edge**: tapping an enemy far from the character does nothing. That is M1-04 working as specified rather than a fault here — `FocusResolver` has no distance limit, but `Targeter.Select` lets the override take the target only inside the class's `acquireRange` (12 m for the Oathbound), and drops the focus after 2 s outside it, per CC §3.4. The rule is right; the *silence* is not, since `TargetChanged` goes out with `IsFocused: false` and nothing on screen moves, which CC §3.5 exists to prevent. **Left as is by the owner's decision** and parked in the ROADMAP for a later pass — M1-18's chasers may shrink the problem to nothing by closing the distance themselves.

**One incidental file:** creating the first URP material made Unity write `m_ProjectSettingFolderPath: URPDefaultResources` into `ProjectSettings/URPProjectSettings.asset` by itself. The owner reverted it, so it is not in this PR; expect it again from whoever next creates one.
