# M1-16 — `PlayerView` charge motion + sweep, `SkillButton`, movement-skill input

**Size:** M · **Depends on:** M1-15 · **Branch:** `m1-16-charge-view-and-skill-button`
**Design refs:** CC §5, §6.2 (button layout), §7; GD §5.2

## Goal

The Charge is playable: a 72 dp button under the right thumb, a 10 m dash in 0.22 s that passes through enemies and shoves them, with a radial cooldown you can read at a glance.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ChargeMotion.cs` | Game | Executes `ChargeIntent`; sweeps for enemies; reports |
| `Game/Controls/SkillButton.cs` | Game | `OnScreenButton` + radial cooldown fill |
| *small edits* | | `InputAdapter` + `MovementSkillPressedThisFrame`; `RunTicker` forwards the press to `IPlayerCommands.MovementSkill()` **before** building the snapshot, and forwards `Charge`/`EnemyKnockback` intents to views after the tick; `PlayerView` ignores `PlayerMove` while `ChargeMotion.IsActive`; `EnemyView` + `Knockback(direction, distance)` (lerp over 0.15 s); `Hud.prefab` + button (bottom-right arc, 72 dp); `Player.prefab` + `ChargeMotion`; `DebugOverlay` + `charge {fraction}` |

## Public API

```csharp
namespace Soulvail.Game.Views;

public sealed class ChargeMotion : MonoBehaviour
{
    public bool IsActive { get; }
    public void Begin(in ChargeIntent intent, IRunSession session);
}

// EnemyView (added)
public void Knockback(Vector2 directionXZ, float distance);
```

```csharp
namespace Soulvail.Game.Controls;

public sealed class SkillButton : MonoBehaviour     // hosts an OnScreenButton bound to <Gamepad>/buttonSouth
{
    [SerializeField] private Image _radialFill;      // Filled / Radial360
    [Inject] public void Construct(IRunSession session);   // reads State.Combat.Charge.CooldownFraction each frame
}
```

## Behaviour

1. **Order in `RunTicker.Tick`:** read input → if `MovementSkillPressedThisFrame` → `commands.MovementSkill()` → build snapshot → clear intents → `session.Tick` → apply intents (`PlayerMove` unless charging; `Charge` → `chargeMotion.Begin`; `EnemyKnockback` → `views[id].Knockback`) → cone queries/reports (M1-12). Commands go in before the tick so the buffer timestamp is this frame's.
2. `ChargeMotion.Begin`: for `Duration` seconds, each frame `controller.Move(dir × (Distance / Duration) × dt)`; after each step, `Physics.OverlapCapsuleNonAlloc` over the segment just traversed (radius = controller radius, layer `Enemy`, triggers included) → `EnemyView.FromCollider` → `session.ReportChargeHits(idsThisFrame)`. Core dedupes across frames (M1-15 rule 5). Facing is set to the charge direction for the duration.
3. Walls stop the motion naturally (`CharacterController` collision); the timer still runs out — no "stuck charging".
4. `EnemyView.Knockback`: lerps the transform `distance` along `direction` over 0.15 s using `controller.Move` (or a direct transform move for the static dummies until M1-18 gives them a controller); a second knockback restarts the lerp from the current position.
5. `SkillButton`: `_radialFill.fillAmount = 1 − CooldownFraction` (empty while cooling, full when ready); 40 % opacity while cooling, 100 % when ready. The button's `OnScreenButton` emits `<Gamepad>/buttonSouth`, which `Soulvail.inputactions` maps to `MovementSkill` — so the button needs no code path of its own into gameplay.
6. Button geometry (CC §6.2): 72 dp, anchored bottom-right inside the safe area, at the natural thumb-rest position (≈ 96 dp from the right edge and 96 dp from the bottom on a 1080p-class phone; tune on device).
7. No per-frame allocation in `ChargeMotion` (preallocated `Collider[]`, `int[]`).

## Tests

None beyond M1-15's core tests — this is body and UI. Verified on device.

## Manual verification (Editor, then device)

1. Tap/press Space: the capsule dashes ~10 m in about a fifth of a second; the button empties and refills over 2.5 s.
2. Tap while the button is nearly full (< 0.15 s left) → charge fires the instant it's ready (buffer).
3. Tap while cooling with > 0.15 s left → nothing, and no "queued" charge later.
4. Charge through two dummies: both flash, both slide ~5 m along the charge line; a dummy at 15 HP dies.
5. Charge into a wall: stops at the wall, cooldown starts normally, no jitter.
6. Charge while focused: focus glow vanishes; stick input during the dash does nothing until it ends.
7. Take a hit (M1-18) during the dash: no damage; 0.05 s after the dash ends, hits land again.

## Acceptance

- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps 1–6 verified (7 after M1-18)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Skill slot buttons S1–S4 — M3-10. Manual/auto toggles — M3-07.
- Charge VFX (trail, impact) — M7.

## As built

_Filled at merge._
