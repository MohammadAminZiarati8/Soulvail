# BT-07a — The boar in core: a ranged animal that spits

**Size:** M · **Depends on:** BT-04, BT-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Packleader Lv 14: Boar), CH §3.6.1

## Goal

A `BoarCount` stat adds a ranged pet that holds 5 m from its quarry and spits globs that cover
stops; Pack Tactics' shared-bite bonus stops being read.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | Spawns the boar in slot 3 off `BoarCount`; ranged movement and firing; Pack Tactics reads removed; `Capacity` 3 → 4 |
| `Core/Ai/PetSpit.cs` | Core | **New.** Fixed pool of in-flight globs: fly, hit the quarry, die at cover |
| `Core/Ai/PetAgent.cs` | Core | `PetKind Kind` (`Hound`, `PackHound`, `Boar`) |
| `Core/Content/PetSpec.cs` | Core | Ranged fields: `SpitRange` 7, `HoldDistance` 5, `SpitInterval` 1.5, `SpitSpeed` 12; `IsRanged` |
| `Core/Effects/PetStats.cs` | Core | Append `BoarCount` (0), `SpitDamage` (1, a multiplier), `SpitSlow` (0); `PackTactics`/`PackTacticsSlow` kept, unread, "retired" |
| `Core/Events/PetEvents.cs` | Core | `PetSpat(PetId, GlobId, From, TargetId)`, `GlobLanded(GlobId, Position, Hit)`; `PetSpawned` gains `Kind` |
| `Tests/Core/Ai/BoarTests.cs` | Tests.Core | **New** |
| `Tests/Core/Ai/PetSpitTests.cs` | Tests.Core | **New** |
| *ripple* | | `CharacterSpec.Boar` (a second `PetSpec`, null for every other class) · `PetSystem` constructor · `RunSession` passes `TraceCover` · pack-tactics tests deleted |

## Public API

```csharp
public enum PetKind { Hound, PackHound, Boar }
public sealed class PetSpit {
    public const int Capacity = 8;
    public ReadOnlySpan<PetGlob> InFlight { get; }
}
public sealed class PetSystem { public PetSystem(PetSpec hound, PetSpec boar, IDomainEvents e, IIntentSink i); }
```

## Behaviour

1. **Joins:** `BoarCount` ≥ 1 spawns one boar from `CharacterSpec.Boar` beside him; 0 removes it.
   Stats scale +3% HP / +2% damage a level like the Hound; Thick Hide, Thick Pelts and Frenzy reach it.
2. **Quarry** is BT-03's order. It moves until within `HoldDistance` and with a clear line
   (`TraceCover` null between it and the quarry), then stands; it backs off when nearer than 3 m.
3. **Spit:** standing in range with a clear line, a glob every `SpitInterval` at `SpitSpeed`, homing
   on the quarry; damage = the boar's bite damage × `SpitDamage`. Cover on the glob's step kills it (`Hit` false).
4. **Slow:** with `SpitSlow` > 0 a hit slows by it for 1.5 s; bosses immune.
5. **Hunt rule:** a glob hit sets `LastBitBy`/`LastBitAt`, so a melee enemy may turn on it (two-hunter cap).
6. **Survival:** heel, fall and rescue as every animal (§3.6.1). Wild Swap still trades with the Hound alone.
7. **Pack Tactics retired:** no bite is multiplied or slows for a shared quarry.

## Tests

| Test | Given / When / Then |
|---|---|
| `BoarCount_One_SpawnsBoarInSlotThree` / `BoarCount_Zero_Removes` (1) | |
| `Boar_HoldsAtFive_BacksOffInsideThree` / `Boar_BlockedLine_MovesNotSpits` (2) | |
| `Spit_EveryInterval_HitsQuarry` / `Spit_CoverOnPath_Dies` (3) | |
| `Spit_SlowStat_Slows_BossImmune` (4) | |
| `SpitHit_MeleeEnemy_MayHuntBoar` (5) | |
| `Boar_Falls_RescuedLikeHound` / `Swap_TradesWithHoundNotBoar` (6) | |
| `SharedQuarry_NoBonus` (7) | |
| `Spit_Tick_NoAllocation` | `AllocationAssert.None` |

## Out of scope

- The node, the model in the run, the glob drawn (BT-07b).

## As built

_Filled at merge._
