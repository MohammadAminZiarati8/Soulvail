# BT-03 — Animals hunt the whole arena, your last mark first; the hawk climbs back

**Size:** M · **Depends on:** BT-01 · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.2 (Reach, Target order), CH §3.7 (Hawk)

## Goal

Every animal fights any enemy in the arena, switches at once to the enemy your last throw aimed at
while it is marked, and the hawk climbs back to its circle instead of snapping.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | `ChooseQuarry`/`FindQuarry`/`FindPounceTarget` lose the leash; new order; retarget on a new throw target |
| `Core/Ai/HawkSystem.cs` | Core | Same priority for dives; a `ClimbSeconds` lerp after each strike |
| `Core/Combat/PlayerCombat.cs` | Core | `LastThrowTargetId`, set when `TryThrowPair` throws |
| `Core/Content/PetSpec.cs` | Core | `Leash` and `ReturnRange` removed |
| `Tests/Core/Ai/PetArenaHuntTests.cs` | Tests.Core | **New** |
| `Tests/Core/Ai/HawkClimbTests.cs` | Tests.Core | **New** |
| *ripple* | | `CharacterDefinition` drops `_petLeash`/`_petReturnRange` (and the asset fields) · `PetSpec` fixtures · leash tests deleted or rewritten |

## Public API

```csharp
public sealed class PlayerCombat { public int LastThrowTargetId { get; } } // 0 before any throw
public sealed class HawkSystem { public const float ClimbSeconds = 0.4f; }
```

## Behaviour

1. **No leash:** quarry, pounce and dive candidates are every living enemy, any distance from you;
   the "returning" state goes. Heel below `HeelAt` is unchanged.
2. **Order:** `LastThrowTargetId` if alive and marked → nearest marked enemy to the animal →
   enemy nearest you.
3. **At once:** when `LastThrowTargetId` changes to a marked living enemy, every hunting animal takes
   it that tick, ignoring `RetargetInterval`. Animals at heel do not.
4. **Hawk priority:** dives use rule 2's order among marked enemies.
5. **Climb:** after a strike the hawk moves from the strike point to its current circle point over
   `ClimbSeconds` (height eased up); the circle keeps turning meanwhile. A dive cannot start mid-climb.

## Tests

| Test | Given / When / Then |
|---|---|
| `Quarry_EnemyTwentyMetresAway_Hunted` (1) | |
| `Pounce_TargetFarFromPlayer_Allowed` (1) | |
| `Quarry_ThrowTargetMarked_BeatsNearerMarked` / `Quarry_ThrowTargetUnmarked_NearestMarked` (2) | |
| `ThrowTargetChanges_HuntersSwitchSameTick` / `ThrowTargetChanges_HeelingPetStays` (3) | |
| `Hawk_DivesAtThrowTarget` (4) | |
| `Hawk_AfterStrike_NotOnCircle_ReachesItIn0_4s` / `Hawk_MidClimb_NoDive` (5) | |

## Manual verification (Editor / device)

1. **[Editor]** Hound runs to an enemy across the arena; hit a different enemy: the pack switches to it.
2. **[Editor]** Hawk: after a dive it rises back to its circle.

## Out of scope

- Slots, spacing and easing (BT-04). Pathing around cover.

## As built

_Filled at merge._
