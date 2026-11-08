# BT-05 — Wild Swap lands him exactly where the hound was

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.3

## Goal

The swap's leap can no longer be stopped short: core already sends the exact distance, but
`ChargeMotion` sweeps the capsule through `CharacterController.Move` and an enemy (or the hound's
body) stops it. After this he lands on the hound's spot every time.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ChargeMotion.cs` | Game | A swap (`PassesCover`) excludes Cover, Enemy and the pets' layer, and its last step lands on `start + direction × distance` |
| `Tests/Game/Views/ChargeMotionSwapTests.cs` | Tests.Game | **New**, beside `ChargeMotionThrowTests` |

## Public API

```csharp
// No new members. ChargeIntent.PassesCover keeps meaning "this is a swap"; its doc says so.
```

## Behaviour

1. **Nothing stops a swap:** during a swap the controller's `excludeLayers` also holds the Enemy
   layer and whichever layer the pet bodies use; both restored when it ends, as cover is now.
2. **Exact landing:** the swap records its start and destination; the final step moves the capsule
   onto the destination (within 0.05 m). A dash or a throw keeps today's "a wall stops it" rule.
3. **Bounds:** the destination is the hound's position, inside the arena by construction; no clamp added.

## Tests

| Test | Given / When / Then |
|---|---|
| `Swap_EnemyOnTheLine_LandsOnDestination` (1, 2) | |
| `Swap_PetBodyAtDestination_LandsOnIt` (1) | |
| `Swap_Ends_LayersRestored` (1) | |
| `Dash_EnemyOnTheLine_StillStopped` (2) | |

## Manual verification (Editor / device)

1. **[Editor]** Swap with the hound across a crowd: he lands on its spot, the hound on his.

## Out of scope

- The hound's leap (`PetView.Leap` is already a lerp to the exact point).

## As built

_Filled at merge._
