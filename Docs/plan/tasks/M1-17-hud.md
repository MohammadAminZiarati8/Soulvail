# M1-17 — `HudPresenter`, HP bar with ghost trail, shield ring, cooldown, death → Menu

**Size:** M · **Depends on:** M1-08, M1-16 · **Branch:** `m1-17-hud`
**Design refs:** GD §16.1, §16.2 (player row); CC §6.2

## Goal

The player can read their own state — HP with a ghost trail, the Aegis ring, the Charge cooldown — from the top-left where thumbs never go, and dying returns them to the menu instead of leaving a corpse in a running scene.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HudPresenter.cs` | Game | Subscribes to combat events; drives the views; death flow |
| `Game/Presentation/HpBarView.cs` | Game | Fill + delayed ghost fill |
| `Game/Presentation/ShieldRingView.cs` | Game | Radial fill |
| *small edits* | | `Hud.prefab` + top-left group (HP bar 320 × 24 dp with ghost, `140/140` TMP text, shield ring 40 dp beside it, death overlay panel); `RunScope` registers the presenter (serialized); `RunSession` ends the run on `PlayerDied`; `RunTicker` stops ticking when `!IsRunning` |

## Public API

```csharp
namespace Soulvail.Game.Presentation;

public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HpBarView _hp;
    [SerializeField] private ShieldRingView _shield;
    [SerializeField] private TMP_Text _hpText;
    [SerializeField] private GameObject _deathOverlay;   // "You died" + "Tap to return" (raw strings; M6-10)
    [Inject] public void Construct(DomainEventHub hub, IRunSession session, SceneLoader loader);
}

public sealed class HpBarView : MonoBehaviour
{
    public void Set(float fraction);        // instant fill; ghost follows after 0.4 s at 1.5 fraction/s
}

public sealed class ShieldRingView : MonoBehaviour
{
    public void Set(float fraction);
}
```

## Behaviour

1. `HudPresenter` subscribes in `OnEnable` / unsubscribes in `OnDisable`: `PlayerDamaged` → `_hp.Set(HpFraction)`, `_shield.Set(ShieldFraction)`, text `"{current}/{max}"` (integers); a blocked hit briefly tints the bar cyan (60 ms) instead of moving it. `PlayerShieldChanged` → `_shield.Set`. `RunStarted` → initialise from `session.State.Combat.Health`.
2. `HpBarView`: the main fill snaps to the new fraction; the ghost fill (a second image behind, GD gold at 50 %) waits 0.4 s after the last decrease, then shrinks toward the main fill at 1.5 fraction/s. On an *increase* (heal), the ghost snaps to the new value immediately.
3. On `PlayerDied`: `RunSession` publishes `RunEnded` and sets `IsRunning = false` (M0-10 `End` path). `RunTicker.Tick` returns early while `!IsRunning`. The presenter shows `_deathOverlay` and, on the next tap anywhere, `loader.LoadAsync(SceneLoader.Menu)`.
4. Layout (GD §16.1): everything in the safe area's top-left; nothing in the bottom corners.
5. No per-frame allocation except the HP text `SetText` on change (TMP's `SetText(string, float, float)` overload avoids string allocation — use it).

## Tests

None — presentation. The core side (`RunSession` ends on `PlayerDied`) gets one test added to `PlayerCombatTests`: `PlayerDied_EndsRun` — after death, `IsRunning == false`, `RunEnded` published once.

## Manual verification (Editor)

1. HP bar full, ring full, text `140/140` at run start.
2. Use a temporary debug key (Editor only, removed before merge) or wait for M1-18: take damage → ring drains first; then HP drops, ghost lags 0.4 s then catches up.
3. Stand still 4 s → ring refills over 2 s.
4. Die → overlay appears, capsule stops, tap → Menu.

## Acceptance

- [ ] `PlayerDied_EndsRun` green; zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked
- [ ] Parking lot lists the death-overlay strings for M6-10

## Out of scope

- XP bar, level, skill cooldown row, Veilrot meter, Essence — their milestones (GD §16.1 table).
- Run-end payout screen — M4-06.

## As built

_Filled at merge._
