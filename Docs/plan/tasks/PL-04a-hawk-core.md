# PL-04a — Hawk in core

**Size:** M · **Depends on:** PL-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Hawk); BM-01b (mark), BM-07a (light)

## Goal

A jungle hawk circles above the player for a few seconds and dives at marked enemies. It flies,
so nothing can hit it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/HawkSystem.cs` | Core | **New.** One hawk: circle, pick, dive, strike, leave |
| `Core/Effects/CallHawk.cs` | Core | **New.** `IEffect`, handler, cast guard |
| `Core/Events/HawkEvents.cs` | Core | **New.** `HawkArrived`, `HawkDove(EnemyId, From, To)`, `HawkStruck(EnemyId, Amount)`, `HawkLeft` |
| `Core/Effects/PlayerStat.cs` | Core | Append `HawkSeconds` (8), `HawkDiveInterval` (1.5) |
| `Core/Combat/CombatBlackboard.cs` | Core | `MarkedEnemies` (marked within the weapon's range) |
| `Core/Content/TriggerSpec.cs` | Core | Append `TriggerField.MarkedEnemies` |
| `Tests/Core/Ai/HawkSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `EffectRegistry`; `RunSession` (construct, tick after `PetSystem`, clear, the field); `TriggerText`; stat coverage tests |

## Public API

```csharp
public sealed class CallHawk : IEffect { public CallHawk(float damageOfWeapon, float stunSeconds); }  // 2, 0.4
public sealed class HawkSystem {
  public const float CircleRadius = 3f, Height = 4f, DiveSeconds = 0.4f;
  public HawkSystem(IDomainEvents events, PlayerStats stats);
  public bool IsAloft { get; } public Vector3 Position { get; }
  public void Call(float now, float damageOfWeapon, float stunSeconds);
  public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat);
  public void Clear(); }
```

## Behaviour

1. `Call` puts the hawk aloft for `HawkSeconds`, circling the player at `CircleRadius`, `Height`;
   a second call while aloft refreshes the time. `HawkArrived`.
2. Every `HawkDiveInterval` it dives at the marked enemy nearest the player within `WeaponRange`;
   none → it waits and tries each tick.
3. A dive takes `DiveSeconds`; on arrival the target takes `damageOfWeapon × WeaponDamage` through
   the damage door and, if light, a stun of `stunSeconds`; `HawkStruck`; it climbs back to circling.
   A target dead on arrival: no damage.
4. No enemy can target, hit or hunt the hawk; it is not in any registry or the snapshot.
5. When the time runs out it finishes a dive in progress, then `HawkLeft`. `Clear` at stage
   change, death and run end: gone, `HawkLeft`.
6. Trigger `MarkedEnemies ≥ 1`; cooldown 16 s. **Ranks** (PL-04c): 1 — active; 2 — `HawkSeconds`
   +2; 3 — `HawkDiveInterval` −0.5 and cooldown −4 s.
7. Kills pay XP as the player's. No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Call_Aloft_ForSeconds` / `CallAgain_Refreshes` (1) |
| `Dives_NearestMarked_AtInterval` / `NoMarked_Waits` (2) |
| `Strike_DoubleWeapon_StunsLight` / `TargetDied_NoDamage` (3) |
| `Hawk_NotInSnapshot_NotHunted` (4) |
| `TimeUp_FinishesDive_Leaves` / `Clear_Leaves` (5) |
| `Trigger_MarkedEnemies` / `Ranks_Numbers` (6) |
| `HawkKill_PaysXp` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- Model (PL-04b); view and assets (PL-04c).

## As built

_Filled at merge._
