# MK-02a — Steady Aim and Eagle Eye in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Marksman table); CH §3.4 (the Ranger stops to shoot)

## Goal

The Marksman's two passives exist as player numbers and rules: standing stacks with a stop crit and
a full-stack guard, and longer range with a far-target bonus. No assets yet.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/SteadyAim.cs` | Core | **New.** Stacks, stop crit, guard |
| `Core/Combat/EagleEye.cs` | Core | **New.** Far-target bonus |
| `Core/Events/SteadyAimChanged.cs` | Core | **New.** `readonly struct SteadyAimChanged(int Stacks, int MaxStacks)` |
| `Tests/Core/Combat/SteadyAimTests.cs` | Tests.Core | **New** |
| `Tests/Core/Combat/EagleEyeTests.cs` | Tests.Core | **New** |
| *small edits* | | `PlayerStat` (**append** five members — assets store the ordinal), `CombatantStats` (map them), `PlayerCombat` (outgoing and incoming multipliers), `RunSession` (construct, tick, stage-boundary reset), `ArrowLoosed`/the shot event if one exists (`IsCrit`) |

## Public API

```csharp
// PlayerStat, appended: SteadyAimPerStack, SteadyAimStopCrit, SteadyAimGuard, EagleEyeFarBonus,
//                       SteadyAimMaxStacks   (all base 0, except MaxStacks base 5)
public sealed class SteadyAim {
  Stat PerStack, StopCrit, Guard, MaxStacks; int Stacks { get; }
  float OnLoose();          // the multiplier for the arrow being loosed, then adds a stack
  void OnMoved();           // clears stacks (and publishes) if any
  float IncomingMultiplier { get; } }
public sealed class EagleEye { Stat FarBonus; public const float FarDistance = 6f;
  float MultiplierAt(float distanceXZ); }
```

## Behaviour

1. Steady Aim is **inert** while `PerStack` is 0: multiplier 1, no stacks, no events.
2. Each ordinary or Volley arrow loosed while the player is standing still calls `OnLoose`:
   multiplier = `1 + PerStack × Stacks` (stacks **before** this arrow), then `Stacks` rises by 1 up to
   `MaxStacks`.
3. **Stop crit:** when `StopCrit` > 0 and `Stacks` was 0, the multiplier is also × `StopCrit` and the
   shot is marked a crit.
4. **Any movement** (motor speed above its standing threshold) or a roll calls `OnMoved`: stacks 0.
5. **Guard:** at `Stacks == MaxStacks`, damage taken × `(1 − Guard)`; otherwise × 1.
6. `SteadyAimChanged` publishes on every change of `Stacks`, never twice for one value.
7. Eagle Eye: an arrow whose target is farther than `FarDistance` (XZ, at the loose) deals ×
   `(1 + FarBonus)`. Range comes from `WeaponRange` modifiers on the assets, not from this class.
8. Multipliers stack multiplicatively with each other and with existing damage stats.
9. A stage boundary clears stacks. Nothing allocates per tick.

Rank values the assets in MK-02b will carry (here for reference; tests use them):

| Rank | Steady Aim | Eagle Eye |
|---|---|---|
| 1 | `PerStack` +0.05 | `WeaponRange` +1 m, `FarBonus` +0.10 |
| 2 | `PerStack` +0.02 | +1 m, +0.10 |
| 3 | `StopCrit` +2 | +1 m, +0.10 |
| 4 | `PerStack` +0.02 | — |
| 5 | `Guard` +0.20 | — |

## Tests

| Test | Given / When / Then |
|---|---|
| `Inert_WithoutPerStack` | (1) |
| `Stacks_ApplyBeforeAdding` | 0.05, five shots / 1.00, 1.05, 1.10, 1.15, 1.20, then 1.25 held (2) |
| `Stacks_CapAtMax` | (2) |
| `StopCrit_FirstArrowOnly` | StopCrit 2 / first ×2 and crit, second not (3) |
| `Move_ClearsStacks` / `Roll_ClearsStacks` | (4) |
| `Guard_OnlyAtFullStacks` | 0.2 / 4 stacks ×1, 5 stacks ×0.8 (5) |
| `Changed_PublishesOncePerValue` | (6) |
| `EagleEye_FarOnly` | 0.2 / 5.9 m ×1, 6.1 m ×1.2 (7) |
| `Multipliers_Compose` | stack 1.1 × far 1.2 / 1.32 (8) |
| `Boundary_ClearsStacks`, `Tick_DoesNotAllocate` | (9) |

## Out of scope

- Assets, icons, loc (MK-02b). The status icon (MK-05). Any visual.

## As built

_Filled at merge._
