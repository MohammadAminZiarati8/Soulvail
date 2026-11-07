# PL-01a — Pack Hounds in core

**Size:** M · **Depends on:** BM-08 · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader: Pack Hounds); CH §3.6.1; BM-03a, BM-05a

## Goal

Up to two pack hounds run beside the Hound, at a share of its HP and bite, under every survival rule.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | Spawns and despawns pack hounds to match `PackHounds`; shares; per-pet rules |
| `Core/Ai/PetAgent.cs` | Core | `IsLead` |
| `Core/Effects/PlayerStat.cs` | Core | Append `PackHounds` (base 0), `PackShare` (base 0.5) |
| `Core/Effects/PetStats.cs` | Core | Answers both (pack-wide numbers live on the lead's block) |
| `Core/Events/PetEvents.cs` | Core | `PetSpawned` gains `IsLead`; `PetDespawned(PetId)` |
| `Tests/Core/Ai/PackHoundTests.cs` | Tests.Core | **New** |
| *small edits* | | stat coverage tests; `WorldSnapshot.Pets` sized to `PetSystem.Capacity` (3) |

## Public API

```csharp
public sealed class PetAgent { public bool IsLead { get; } }
public readonly struct PetSpawned { int Id; Vector3 Position; bool IsLead; }
public readonly struct PetDespawned { int Id; }
```

## Behaviour

1. `PackHounds` (read as a whole number, clamped 0–2) pack hounds stand beside the lead. A raise
   spawns the new ones beside the player at once; a cut despawns the newest first.
2. A pack hound's `MaxHp` and bite are `PackShare ×` the lead's current values, recomputed whenever
   either changes; a raise heals the gain, a cut clamps current HP.
3. Every pack hound follows BM-03a's target order and leash, BM-04's hunt rule (its own two hunter
   slots), BM-05a's heel, regen, fall and rescue, and Feral Bite and Pounce do not apply to it
   (lead only).
4. At `StageArrived` every pet, pack hounds included, is back at full beside the player.
5. Wild Swap trades with the lead alone; `DropHunters` touches the lead alone.
6. A rescue fills for each fallen pet within reach at once.
7. Pack hounds' kills pay XP as the player's.
8. No allocation per tick or on a spawn.

## Tests

| Test | Given / When / Then |
|---|---|
| `PackHoundsOne_SpawnsBesideLead` / `Cut_DespawnsNewest` / `ClampedToTwo` (1) |
| `Share_HalfOfLead_RecomputedOnLeadGrowth` / `ShareRaise_HealsGain` (2) |
| `PackHound_HuntedFalls_Rescued` / `PackHound_NoPounce` (3) |
| `StageArrived_AllFull` (4) |
| `Swap_TradesWithLeadOnly` (5) |
| `Rescue_FillsTwoFallenAtOnce` (6) |
| `PackHoundKill_PaysXp` (7) |
| `Tick_DoesNotAllocate` (8) |

## Out of scope

- The path asset, its card and the smaller bodies (PL-01b).

## As built

_Filled at merge._
