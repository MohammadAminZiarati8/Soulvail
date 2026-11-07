# BM-05a — Heel, regen and rescue in core

**Size:** M · **Depends on:** BM-04 · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.1 (Retreat to heel; Falls, waits for a rescue)

## Goal

A hurt hound runs to heel and heals; a fallen one gets up when the player stands by it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/PetSystem.cs` | Core | The heel run, regen, return, rescue |
| `Core/Ai/PetAgent.cs` | Core | `RescueProgress`; the four live stats below |
| `Core/Content/PetSpec.cs` | Core | `HeelAt` 0.35, `ReturnAt` 0.8, `HeelRegen` 0.06, `RescueSeconds` 2, `RescueRadius` 2.5, `RiseFraction` 0.5 |
| `Core/Effects/PlayerStat.cs` | Core | Append `PetHeelRegen`, `PetReturnAt`, `PetRescueSeconds` (answered by `PetStats`, refused by `PlayerStats`) |
| `Core/Effects/PetStats.cs` | Core | Answers the three |
| `Core/Events/PetEvents.cs` | Core | `PetHeeled`, `PetReturned`, `RescueProgressed(PetId, Fraction)`, `PetRose(PetId)` |
| `Tests/Core/Ai/PetSurvivalTests.cs` | Tests.Core | **New** |
| *small edits* | | `PlayerStatCoverageTests`, `StatBlockTests` |

## Public API

```csharp
public sealed class PetSystem {
  public const float AtHeelRadius = 2f;
  public void AddRescueHeadStart(int petId, float fraction); }   // BM-06a
// PetHeeled(PetId) · PetReturned(PetId) · RescueProgressed(PetId, float Fraction) · PetRose(PetId)
```

## Behaviour

1. **Heel run:** HP below `HeelAt × Max` → `Heel`: target dropped, no bites, runs to the player's
   side at full speed, `PetHeeled`. Its hunters lose it through BM-04 rule 1(c) once 2 s pass.
2. **Regen:** in `Heel` and within `AtHeelRadius` of the player it heals `PetHeelRegen × Max` per
   second. This includes heeling with no enemy in leash, so between waves it is always healing.
3. **Return:** a pet sent to heel by rule 1 goes back to `Hunt` at `PetReturnAt × Max`,
   `PetReturned`; one heeling for want of a target hunts as soon as one is in leash.
4. **Rescue:** while `Fallen` and the player is within `RescueRadius`, progress grows by
   `dt / PetRescueSeconds`; stepping away keeps it; `RescueProgressed` whenever it changes.
5. At 1 the pet rises at `RiseFraction × Max`, goes to `Heel`, `PetRose`; progress resets.
6. `AddRescueHeadStart` adds to a fallen pet's progress (clamped to 1), ignored otherwise.
7. Unrescued, it stays down until `StageArrived` (BM-04 rule 5). No self-revive.
8. No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `BelowHeelAt_Heels_StopsBiting` (1) |
| `AtHeel_Regens_SixPercentASecond` / `OutOfReach_NoRegen` / `BetweenWaves_Regens` (2) |
| `ReachesReturnAt_HuntsAgain` (3) |
| `PlayerNear_RescueFills_TwoSeconds` / `StepAway_ProgressKept` (4) |
| `Rescued_RisesHalfHp_AtHeel` (5) |
| `HeadStart_Half_OneSecondLeft` / `HeadStart_NotFallen_Ignored` (6) |
| `Unrescued_StaysDown_UntilStageArrived` (7) |
| `ModifyStat_PetReturnAt_Lands` | Lick Wounds' numbers land on `PetStats` |
| `Tick_DoesNotAllocate` (8) |

## Out of scope

- The fall, rise and ring drawn (BM-05b); Lick Wounds as a node (BM-07d).

## As built

_Filled at merge._
