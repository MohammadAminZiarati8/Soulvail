# BM-07b — Cleaving Return and Quick Swap in core

**Size:** S · **Depends on:** BM-01a, BM-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4 (Axe and Wild tier 3)

## Goal

The two tree effects that need code: a slowing return pass, and a roar where the hound lands
after a swap. Heavy Edge, Third Axe, Sharp Claws, Thick Hide and Lick Wounds are `ModifyStat`
data on stats that already exist (BM-07d).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/AxeSystem.cs` | Core | A return-pass hit slows by `AxeReturnSlow` for 1.5 s |
| `Core/Ai/PetSystem.cs` | Core | On a swap landing, a roar slows enemies within 3 m by `SwapRoarSlow` for 2 s |
| `Core/Effects/PlayerStat.cs` | Core | Append `AxeReturnSlow` (base 0), `SwapRoarSlow` (base 0) |
| `Core/Events/PetEvents.cs` | Core | `PetRoared(PetId, Position, Radius)` |
| `Tests/Core/Combat/CleavingReturnTests.cs` | Tests.Core | **New** |
| `Tests/Core/Ai/SwapRoarTests.cs` | Tests.Core | **New** |
| *small edits* | | stat coverage tests |

## Public API

```csharp
public sealed class AxeSystem { public const float ReturnSlowSeconds = 1.5f; }
public sealed class PetSystem { public const float RoarRadius = 3f; public const float RoarSlowSeconds = 2f; }
// PetRoared(int PetId, Vector3 Position, float Radius)
```

## Behaviour

1. **Cleaving Return:** with `AxeReturnSlow` > 0, every back-pass hit slows the enemy by that
   fraction (clamped 0–0.9) for `ReturnSlowSeconds`, through `EnemySystem.Slow` (bosses immune).
   Out-pass hits never slow.
2. **Quick Swap:** with `SwapRoarSlow` > 0, when a swap's pet leap ends, every living enemy within
   `RoarRadius` of the landing point is slowed by that fraction for `RoarSlowSeconds`; `PetRoared`.
   A fallen pet's swap has no landing, so no roar.
3. 0 on either stat does nothing and publishes nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `ReturnHit_Slows_OutHitDoesNot` / `ReturnSlow_Boss_Immune` (1) |
| `SwapLanding_RoarSlowsWithinThree` / `FallenSwap_NoRoar` (2) |
| `ZeroStats_Silent` (3) |

## Out of scope

- The node assets and the roar drawn (BM-07d).

## As built

_Filled at merge._
