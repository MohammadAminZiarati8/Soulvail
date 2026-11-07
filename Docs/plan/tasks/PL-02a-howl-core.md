# PL-02a — Howl in core

**Size:** M · **Depends on:** PL-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Howl); CH §3.5 (auto-cast); GD §16.5 (attack speed up is a buff)

## Goal

The pack's heal: every standing animal heals and bites faster for a few seconds.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Howl.cs` | Core | **New.** `IEffect`, handler, cast guard |
| `Core/Ai/PetSystem.cs` | Core | `Howl(heal, attackSpeed, seconds, now)`; the bite interval reads the buff |
| `Core/Effects/PlayerStat.cs` | Core | Append `HowlHeal` (0.2), `HowlSeconds` (4) |
| `Core/Combat/CombatBlackboard.cs` | Core | `LowestPetHpFraction` (1 with no standing pet) |
| `Core/Content/TriggerSpec.cs` | Core | Append `TriggerField.LowestPetHpFraction` |
| `Core/Events/PetEvents.cs` | Core | `PackHowled(Position)` |
| `Tests/Core/Effects/HowlTests.cs` | Tests.Core | **New** |
| *small edits* | | `EffectRegistry`; `RunSession` fills the field; `TriggerText`; stat coverage tests |

## Public API

```csharp
public sealed class Howl : IEffect { public Howl(float attackSpeedBonus); }   // 0.25
public sealed class PetSystem { public int Howl(float healFraction, float attackSpeedBonus, float seconds, float now); }
public struct CombatBlackboard { public float LowestPetHpFraction; }
```

## Behaviour

1. Every standing pet heals `HowlHeal × its Max` at once; fallen pets are not raised.
2. For `HowlSeconds` each standing pet's bite interval is divided by `1 + attackSpeedBonus`; a second
   howl refreshes the time, never stacks the bonus (strongest wins, GD §16.5).
3. `PackHowled` at the lead's position.
4. With no standing pet `CanCast` is false and the cooldown is kept.
5. `LowestPetHpFraction` is the lowest HP fraction among standing pets, 1 with none; the authored
   trigger is `LowestPetHpFraction Below 0.5`; cooldown 14 s.
6. **Ranks** (for PL-02b): 1 — active; 2 — `HowlHeal` +0.1; 3 — `HowlSeconds` +2; 4 — cooldown −3 s.
7. No allocation on a cast or a tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Howl_HealsStanding_NotFallen` (1) |
| `Howl_BitesFaster_ForSeconds` / `SecondHowl_Refreshes_NoStack` (2) |
| `Howl_PublishesAtLead` (3) |
| `NoStandingPet_Refused_CooldownKept` (4) |
| `Blackboard_LowestFraction` / `Trigger_BelowHalf` (5) |
| `Ranks_Numbers` (6) |
| `Howl_DoesNotAllocate` (7) |

## Out of scope

- Assets and the howl drawn (PL-02b).

## As built

_Filled at merge._
