# JG-30c — The Apex's phases: the pack at 66 %, the hunt at 33 %

**Size:** S · **Depends on:** JG-30b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 3, 4; `BossBehaviour` phase summons and beat; Shadow panther pairing (JG-21b)

## Goal

At 66 % two Shadow panthers join her. At 33 % she is alone again and never lets up: chained
lanes, a double pounce, shorter windows, more speed.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ApexBehaviour.cs` | Core | Phase listener rules below |
| `Core/Ai/ApexStats.cs` | Core | Gains the stats below |
| `Tests/Core/Ai/ApexPhaseTests.cs` | Tests.Core | **New** |

## Public API

```csharp
// ApexBehaviour gains: public int Phase { get; }  public bool IsHunting { get; }   // phase 2
// ApexStats gains: PackDashCooldownSeconds 3,
//   HuntSpeedBonus 0.3, HuntChainLanes 3, HuntLaneGapSeconds 0.25, HuntWindowSeconds 0.9,
//   HuntSecondCrouchSeconds 0.6, HuntMoveGapSeconds 0.6
```

## Behaviour

1. **Phase 1 (66 %), the pack.** The panthers are `BossBehaviour`'s phase summons, authored in 30f;
   nothing here spawns them. The Apex's dash cooldown drops to `PackDashCooldownSeconds`.
2. **Phase 2 (33 %), the hunt.** `BossBehaviour`'s beat clears the panthers, as every phase entry
   does. From then on, `RunSpeed` and `DashSpeed` gain `HuntSpeedBonus`, `MoveGapSeconds` becomes
   `HuntMoveGapSeconds`, and `PounceWindowSeconds` becomes `HuntWindowSeconds`.
3. **Chained lanes.** In the hunt a dash is `HuntChainLanes` lanes: each next lane is probed from
   where the last dash ended, aimed at the player then, and armed `HuntLaneGapSeconds` after it
   fires. Each lane keeps its 0.7 s telegraph. At most 3 lanes live, within `LaneSystem.Capacity`.
4. **Double pounce.** In the hunt a real pounce lands, then crouches again at once for
   `HuntSecondCrouchSeconds` with a new ring on the player. The window follows the second landing
   only. A feint is never doubled.
5. **Telegraphs stay ≥ 0.6 s** in every phase. The modifiers are `Stat` modifiers, removed on
   `Reset`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Phase1_DashCooldownThree` / `Phase1_SpawnsNothingItself` (1) |
| `Phase2_SpeedsAndGapAndWindow_Tuned` / `Reset_RemovesHuntModifiers` (2, 5) |
| `Hunt_DashChainsThreeLanes_EachFromLastEnd_EachPointSeven` / `Hunt_NeverMoreThanThreeLanes` (3) |
| `Hunt_PounceLands_CrouchesAgainPointSix_WindowAfterSecond` / `Hunt_FeintNotDoubled` (4) |
| `Telegraphs_AtLeastPointSix_InEveryPhase` (5) |

## Out of scope

- The phase data (30f); the roar and look (30e).
