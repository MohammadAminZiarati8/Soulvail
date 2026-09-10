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

- [x] `PlayerDied_EndsRun` green; zero errors, zero new analyzer warnings
- [ ] Manual steps verified — **owner's, steps 2–4 outstanding.** Step 1 was measured in play mode (see *As built*)
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked
- [x] Parking lot lists the death-overlay strings for M6-10

## Out of scope

- XP bar, level, skill cooldown row, Veilrot meter, Essence — their milestones (GD §16.1 table).
- Run-end payout screen — M4-06.

## As built

Six deviations, all from the Public API sketch rather than from the Behaviour rules.

1. **`RunState` gained four narrow reads** — `PlayerHp`, `PlayerMaxHp`, `PlayerHpFraction`, `PlayerShieldFraction`. Rule 1's `session.State.Combat.Health` does not compile: `RunState.Combat` is `internal`, and exposing the `Health` handle would hand every view a public `ApplyDamage`, `Heal` and `Reset`. This is the same move M1-16 made for the cooldown, and the reads exist for the one thing no event carries — a run's *opening* numbers, which `RunStarted` cannot report because nothing has happened yet. Damage and shield still come off the events.
2. **All three new types use block namespaces**, not the file-scoped `namespace Soulvail.Game.Presentation;` the API block shows. They are `MonoBehaviour`s, so a file-scoped namespace would leave `Hud.prefab` deserialising them as null with nothing reported anywhere (M0-11).
3. **Subscriptions live in `Construct`, not `OnEnable`**, and are dropped in `OnDestroy`. `RunScope` injects from its own `Awake` and Unity gives no order between two of those, so an `OnEnable` subscription reaches for a hub that may not have arrived. `ReticleView`, `FocusGlowView` and `EnemyHitFeedback` all made the same move for the same reason.
4. **`Construct` takes a fourth dependency, `InputAdapter`**, for the death overlay's tap. The alternative — a full-screen `Button` under the overlay — needs a serialized field the spec's own API does not have, would be swallowed by whatever UI sits above it, and would not be "tap *anywhere*". `InputAdapter`'s own remarks already name the HUD as a downstream reader, so this keeps "one reader of the Input System" intact. A tap on the frame the overlay appears is ignored, since core publishes the death from inside the Update phase.
5. **`HpBarView` gained `FlashBlocked()`.** Rule 1's blocked-hit tint is not a health value — nothing moved — so it cannot be spelled as a `Set` call. The fill is cyan (GD §16.4: cyan is the player), so the flash brightens it rather than recolouring it; red-orange is reserved for danger and may never appear here.
6. **`HudPresenter` lays the row out in dp at `Start`.** The spec's 320 × 24 dp and 40 dp are physical sizes, and a Scale-With-Screen-Size canvas measures in reference pixels — so authoring them in the prefab would give a different physical size on every phone, and the HUD's two halves would scale by different rules against `SkillButton`, which already places itself in real dp. One `Place()` on the presenter rather than one per view, because the three rects have to agree about where each other are.

**Rule 5's allocation budget needed one correction to the format string.** TMP treats an unformatted `{0}` as *nine* decimal places, so `"{current}/{max}"` renders 139.5 HP as `139.5`. `"{0:0}/{1:0}"` is the integer form, rounds half-up, and still writes straight into TMP's backing array without allocating.

**Verified.** Full EditMode suite 428 passed / 0 failed, including `PlayerDied_EndsRun`; zero compile errors; no new analyzer warnings. Manual step 1 measured in play mode: bar 1.00, ghost 1.00, ring 1.00, text `140/140`, death overlay hidden, held steady over 6 s with no errors. Steps 2–4 are the owner's.

**What the play-mode probe could not reach.** With eight Husks at 8–11 m the Censer kills every one before any lands a strike — even with contact damage temporarily raised to 500, the player finished 8 s untouched. Forcing a hit means moving the dummies to melee range, and the attempt to do that mid-probe lost its state to a domain reload and left `ProjectSettings/QualitySettings.asset` re-serialised with a quality level dropped. Both were reverted; see the PROGRESS entry's watch-list note.
