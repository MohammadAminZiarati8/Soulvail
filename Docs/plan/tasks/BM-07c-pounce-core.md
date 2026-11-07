# BM-07c — Pounce in core

**Size:** M · **Depends on:** BM-07a · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4 (Pounce); CH §3.5 (auto-cast, 0.5 s gap); MK-07a (an active's shape)

## Goal

The Beastmaster's tree active: the hound leaps at a marked enemy, bites hard and knocks a light
one down, cast from the arc or by its own trigger.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Pounce.cs` | Core | **New.** `IEffect`, its handler and cast guard |
| `Core/Ai/PetSystem.cs` | Core | `TryPounce`: pick, leap, land, bite, knock down |
| `Core/Combat/CombatBlackboard.cs` | Core | `MarkedInPounceReach` |
| `Core/Content/TriggerSpec.cs` | Core | Append `TriggerField.MarkedInPounceReach` |
| `Core/Events/PetEvents.cs` | Core | `PetPounced(PetId, EnemyId, From, To)`, `PetLanded(PetId, EnemyId)` |
| `Tests/Core/Effects/PounceTests.cs` | Tests.Core | **New** |
| *small edits* | | `EffectRegistry` registration; `RunSession` fills the blackboard field; `TriggerText` names the field |

## Public API

```csharp
public sealed class Pounce : IEffect { public Pounce(float minReach, float maxReach, float biteMultiplier,
  float knockdownSeconds, float leapSeconds); }    // 3, 6, 2, 0.8, 0.35
public sealed class PetSystem { public bool TryPounce(in Pounce pounce, float now, EnemySystem enemies, PlayerCombat combat); }
public struct CombatBlackboard { public int MarkedInPounceReach; }
```

## Behaviour

1. **Pick:** the marked living enemy nearest the lead pet whose XZ distance is within
   `[minReach, maxReach]` and within the pet's leash of the player. None → `CanCast` false,
   cooldown kept.
2. A pet that is `Fallen`, in a heel run, or leaping cannot pounce (`CanCast` false).
3. **Leap:** `PetLeap` intent to the target's position over `leapSeconds`; `PetPounced`. It bites
   nothing while airborne and is not huntable during the leap.
4. **Land:** the bite deals `biteMultiplier ×` its bite through the damage door, then a light target
   is stunned `knockdownSeconds`; `PetLanded`. A target dead on landing: no damage, the pet lands.
5. After landing it hunts that target as normal.
6. `MarkedInPounceReach` counts rule 1's candidates each tick; the authored trigger is
   `MarkedInPounceReach ≥ 1`; cooldown 8 s. The 0.5 s gap between auto-casts applies (SkillRunner).
7. No allocation in pick, leap or land.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pick_NearestMarkedInBand` / `NoneInBand_Refused_CooldownKept` / `UnmarkedOnly_Refused` (1) |
| `Fallen_Or_Heeling_Refused` (2) |
| `Leap_PublishesPounced_NotHuntable` (3) |
| `Land_DoubleBite_StunsLight` / `Land_Bullfrog_NoStun` / `TargetDied_NoDamage` (4) |
| `AfterLanding_HuntsTarget` (5) |
| `Blackboard_CountsCandidates` / `Trigger_FiresAtOne` (6) |
| `Pounce_DoesNotAllocate` (7) |

## Out of scope

- The node asset, its icon and the leap drawn (BM-07d).

## As built

_Filled at merge._
