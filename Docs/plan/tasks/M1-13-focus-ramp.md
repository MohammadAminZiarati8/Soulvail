# M1-13 — `FocusSpec`, `FocusTracker` → fire-rate modifier; ground glow

**Size:** M · **Depends on:** M1-10 · **Branch:** `m1-13-focus-ramp`
**Design refs:** CC §4.3, §5.5, §7; ADR-0008 (first real modifier)

## Goal

Standing still ramps the swing rate — the *dodge, plant, burn* rhythm — implemented as the first live `Stat` modifier, with a glow so the player can see the commitment paying off.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/FocusSpec.cs` | Core | Delay 0.4, ramp 1.0, max ×1.3 |
| `Core/Combat/FocusTracker.cs` | Core | Stationary timer → level → modifier |
| `Game/Views/FocusGlowView.cs` | Game | Ground glow intensity from `FocusChanged` |
| `Tests/Core/Combat/FocusTrackerTests.cs` | Tests.Core | Timeline + modifier |
| *small edits* | | `CharacterSpec`/`CharacterDefinition`/`Oathbound.asset` + `Focus`; `PlayerCombat` owns the tracker and passes `Weapon.FireRate`; `CombatBlackboard` + `FocusLevel`; `CombatEvents` + `FocusChanged`; `Player.prefab` + glow child |

## Public API

```csharp
namespace Soulvail.Core.Content;

public sealed class FocusSpec
{
    public float Delay         { get; }   // 0.4 s stationary before ramp starts
    public float RampTime      { get; }   // 1.0 s from 0 to full
    public float MaxMultiplier { get; }   // 1.3
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class FocusTracker
{
    public FocusTracker(FocusSpec spec, Stat fireRate, IDomainEvents events);
    public float Level { get; }            // 0..1
    public float StationaryTime { get; }
    public void Tick(float dt, bool isMoving);
    public void Reset();
}

public readonly struct FocusChanged { public readonly float Level; }   // in CombatEvents
```

## Behaviour

1. `isMoving = |snapshot.MoveInput| > 0` (supplied by `PlayerCombat`). A Charge in flight (M1-15) also counts as moving.
2. Moving → `StationaryTime = 0`, `Level = 0` immediately. Stationary → `StationaryTime += dt`; `Level = clamp((StationaryTime − Delay) / RampTime, 0, 1)`.
3. The modifier is `PercentAdd` with value `Level × (MaxMultiplier − 1)` and source `this`. When `Level` changes, `fireRate.RemoveAll(this)` then `Add` (if `Level > 0`). At full level the Oathbound swings at 3.9/s.
4. `FocusChanged` is published when `Level` changes by ≥ 0.01 or crosses 0/1 exactly.
5. `CombatBlackboard.FocusLevel = Level` each tick.
6. `FocusGlowView` subscribes to `FocusChanged`: a flat cyan disc under the player, alpha `0.35 × Level`, radius `1 + 0.5 × Level` m. Hidden at level 0.
7. `Tick` allocates nothing when `Level` is unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Level_ZeroDuringDelay` | stationary / Tick to 0.39 / Level 0 |
| `Level_HalfAtMidRamp` | stationary / Tick to 0.9 / Level 0.5 ± 0.01 |
| `Level_FullAfterRamp` | stationary / Tick to 1.4 / Level 1; stays 1 at 5 s |
| `Moving_CancelsInstantly` | Level 1 / Tick(0.016, moving) / Level 0, StationaryTime 0 |
| `Modifier_RaisesFireRate` | Level 1 / — / `fireRate.Value == 3.9 ± 1e-4`; Level 0.5 / 3.45 |
| `Modifier_RemovedWhenMoving` | Level 1 then moving / — / `fireRate.ModifierCount == 0` |
| `FocusChanged_PublishedOnChange` | ramp 0→1 in 0.05 steps / — / ~20 events; none while stable |
| `Blackboard_ReflectsLevel` | through `PlayerCombat` / — / `FocusLevel` matches |
| `Tick_StableLevel_AllocatesNothing` | Level 1, warm-up / 10 000 stationary ticks / allocated-bytes delta == 0 |

## Manual verification (Editor)

1. Stand still near a dummy: after ~0.4 s the glow appears and grows for a second; swings audibly/visibly speed up.
2. Nudge the stick — glow vanishes instantly; swings drop back to 3/s.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Focus affecting damage (a tree node later, M3-12).
- Audio cue — M7-07.

## As built

_Filled at merge._
