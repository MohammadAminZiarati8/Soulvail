# BT-06a — Frenzy in core; Howl heals only

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader: Howl, Frenzy), GD §16.5

## Goal

A Frenzy effect that gives every standing animal +attack speed, +move speed and +damage for a
time, and a Howl that only heals.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Frenzy.cs` | Core | **New.** `Frenzy : IEffect` and `FrenzyHandler` (cast guard: a standing animal) |
| `Core/Ai/PetSystem.cs` | Core | `_howlBuffs` becomes `_frenzyBuffs` (one bonus, one deadline per pet); bite rate, move speed and bite damage read it; `Howl` heals only |
| `Core/Effects/Howl.cs` | Core | `AttackSpeedBonus` removed; `Apply` heals |
| `Core/Effects/PlayerStat.cs` | Core | Append `FrenzySeconds` (base 8), `FrenzyBonus` (base 0.3); `HowlSeconds` kept, unread, doc "retired" |
| `Core/Events/PetEvents.cs` | Core | `PetsFrenzied(float Seconds, float Bonus)`; `PetsHowled` loses its speed field |
| `Tests/Core/Effects/FrenzyTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession` registers `FrenzyHandler` · `HowlDefinition` drops its bonus field · Howl tests updated · stat coverage |

## Public API

```csharp
public sealed class Frenzy : IEffect { }
public sealed class FrenzyHandler : IEffectHandler<Frenzy>, IEffectCastGuard<Frenzy> {
    public FrenzyHandler(PetSystem pets, PlayerStats stats, SimulatedClock clock);
}
public sealed class PetSystem {
    public int Frenzy(float bonus, float seconds, float now); // animals buffed
    public int Howl(float healFraction, float now);           // animals healed
    public bool IsFrenzied(int petId, float now);
}
```

## Behaviour

1. **Frenzy:** every standing (not fallen) animal gets `FrenzyBonus` on bite rate, move speed and
   bite damage until `now + FrenzySeconds`; a recast refreshes and takes the larger bonus. `PetsFrenzied`.
2. A pet spawned or risen mid-Frenzy is not buffed. A fall ends its buff.
3. **Howl:** heals every standing animal `HowlHeal` × max HP; no buff, no speed.
4. Frenzy cannot cast with no standing animal (the guard).
5. PlayerStat members stay append-only: nothing removed or reordered.

## Tests

| Test | Given / When / Then |
|---|---|
| `Frenzy_BitesThirtyPercentFaster` / `Frenzy_MovesThirtyPercentFaster` / `Frenzy_BitesThirtyPercentHarder` (1) | |
| `Frenzy_Expires_AfterSeconds` / `Frenzy_Recast_RefreshesAndKeepsLarger` (1) | |
| `Frenzy_FallenSkipped_FallEndsBuff` / `Frenzy_SpawnedMidway_NotBuffed` (1, 2) | |
| `Howl_HealsOnly_NoRateChange` (3) | |
| `Frenzy_NoStandingPet_CannotCast` (4) | |
| `PlayerStat_Ordinals_Unchanged` (5) | |

## Out of scope

- The Lv-20 node assets, the look, Stampede's removal (BT-06b, BT-06c).

## As built

_Filled at merge._
