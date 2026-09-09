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

Built to the Files table. 414 EditMode + 3 PlayMode, zero errors, zero new warnings. The dash was
measured in the Editor rather than left for the playtest — see *Measured* below.

**Five places the code says something the spec did not.**

1. **`State.Combat` is `internal`, so rule 5's `State.Combat.Charge.CooldownFraction` does not
   compile from `Soulvail.Game`.** `RunState` gained one narrow read instead —
   `MovementSkillCooldownFraction` — which is exactly the escape hatch that property's own remarks
   promised ("anything that genuinely needs a live number gets a narrow read here rather than the
   handle"). The handle stays internal, so nothing in the body can still advance, hurt or heal the
   player. This is the only Core change in the task.
2. **`ChargeMotion` is stepped by `RunTicker`, not by an `Update` of its own**, so it gained a
   `Step(dt)` beyond the spec's two members (and a `Cancel()` for teardown). Forced by the first
   trap: the sweep calls `ReportChargeHits`, which is what makes core write the knockbacks, so
   "when the sweep runs" and "when the shoves are applied" have to be adjacent lines in the file
   that owns the frame. With two `Update`s the order would have been Unity's to pick.
3. **The `PlayerMove` guard is in `RunTicker`, not in `PlayerView`.** The spec puts "ignores
   `PlayerMove` while charging" on the view, which would need the view to hold a reference to the
   other view. `RunTicker` already holds both and already owns the order, and this keeps
   `PlayerView` dumb.
4. **`RunTicker` takes `IPlayerCommands` as a second handle on the session.** `IRunSession` has no
   `MovementSkill` — commands are a separate port (M1-09) — so the press could not be forwarded
   without it. Same shape `TapToFocusAdapter` already uses, and it keeps the frame loop unable to
   end a run through the door the player presses.
5. **`RunScope` gained the two serialized fields these components are registered through**
   (`_chargeMotion`, `_skillButton`), which the spec's small-edits row does not list. Nothing in a
   scene is injected unless the scope registers it. The mask reaches the sweep as
   `RegisterComponent(_chargeMotion).WithParameter("enemyLayer", _enemyLayer)` — the same field that
   arms the cone query, passed rather than registered as a bare `LayerMask`, so M1-19's wall layer
   cannot later be resolved into the wrong one by type.

**The button stays tappable while it is cooling**, which is CC §6.2's "no tap response" deliberately
not followed. CC §5's 0.15 s input buffer only exists if the early press reaches core, and a button
that swallowed it would make the buffer unreachable through the control it was written for. Core is
what refuses a stale press. The 40 % dimming is the whole of the feedback, and manual steps 2 and 3
are the two halves of that being right.

**Two moments, still.** `ChargeMotion.IsActive` is the *movement* ending (0.22 s); `ChargeEnded` is
the i-frames lapsing (0.27 s). Nothing in this task reads the later one — M1-17's HUD is the first
that will — but every place the earlier one is read says which it means.

**`EnemyView.Velocity` is now written by a shove.** It had been documented as "zero until M1-18".
Leaving it zero would have the snapshot report a body standing still while its position moved 5 m,
which is two senses about one enemy contradicting each other. A knockback is core's decision
arriving as an intent, so the slide is still "what core asked for" and the M1-07 rule is intact.

**Beyond the Files table:** `IntentBufferTests` gained five rows covering `HasCharge`, `Charge`,
`Knockbacks` and their clearing — the coverage M1-15's footer explicitly left to this task — and its
allocation row now writes all four intent kinds.

**Measured, in play mode, through the real command port** (`Temp/soulvail-dash-trace.txt` at the
time): press → **10.00 m travelled exactly**, then `IsActive` false; ~45 m/s along the way
(7.73 m in 0.172 s); cooldown fraction 1.000 at the start decaying to 0.784 over 0.54 s, which is a
2.5 s cooldown to three figures; the capsule passed *through* the enemy on its line rather than
being stopped by it; and that enemy moved exactly **5.00 m** in a direction identical to the dash's.
Every number in CC §5 that this task is responsible for, checked against the running game.
