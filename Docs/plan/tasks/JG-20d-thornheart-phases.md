# JG-20d — Thornheart's phases: the strangling ring at 66 %, a faster grove at 33 %

**Size:** M · **Depends on:** JG-20c · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rule 3 (66 / 33, beat clears adds and resets pressure); GD §7.2 (no dead ends: a circle-strafe route always exists); `BossBehaviour`'s beat

## Goal

Phase 2 closes a ring of thorn in from the arena's edge and phase 3 speeds the grove up, without
ever sealing the circle-strafe lane.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/ThornheartBehaviour.cs` | Core | Ring, spoke clamp, phase-3 tuning |
| `Core/Events/ThornheartEvents.cs` | Core | Gains `StrangleRing` |
| `Tests/Core/Ai/ThornheartPhaseTests.cs` | Tests.Core | **New** |

## Public API

```csharp
// consts: RingStartRadius 17f, RingRadius 11f, RingCloseSeconds 6f, RingPulseSeconds 0.5f,
//   RingDamageScale 0.25f, StrafeLane 3f; PhaseTwoSpokes 4;
//   PhaseThreeRootTelegraphSeconds 0.7f, PhaseThreeAttackGapSeconds 1.1f; PhaseThreePodsPerCast 3
// ThornheartBehaviour: int Phase { get; }  float RingRadiusNow { get; }  (0 = no ring)
// ThornheartEvents gains StrangleRing(BossId, FromRadius, ToRadius, Seconds)
```

## Behaviour

1. **Phases** are `BossSpec` 1 / 0.66 / 0.33 (rule 3). The beat is `BossBehaviour`'s:
   invulnerable, adds cleared, walls withered (20b rule 8).
2. **Ring (phase ≥ 1):** on entry it opens at `RingStartRadius` round the boss and closes linearly to
   `RingRadius` over `RingCloseSeconds`, publishing `StrangleRing`. Each later phase entry reopens
   and recloses it.
3. **Bite:** while the player stands farther than `RingRadiusNow` from the boss (XZ), every
   `RingPulseSeconds` they take `ContactDamage × RingDamageScale` through `ApplyDamage` — a pulse,
   so the answer is to step in.
4. **Strafe lane:** with a ring up, spokes end at `min(RootOuter, RingRadiusNow − StrafeLane)`, so a
   lane `StrafeLane` wide inside the ring stays clear of root walls. The slam is not clamped (it
   stands 3 s and crosses the lane; the lane round its far end stays open).
5. **Phase 2** casts `PhaseTwoSpokes` spokes.
6. **Phase 3** also uses `PhaseThreeRootTelegraphSeconds` (≥ 0.6), `PhaseThreeAttackGapSeconds`
   and `PhaseThreePodsPerCast`.
7. **Reset** drops the ring; phase 0 behaves as 20b–c exactly. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `Phases_AtSixtySixAndThirtyThree_BeatClearsAndWithers` — real `BossSpec` (1) |
| `Ring_ClosesSeventeenToElevenInSix_PublishesEvent` / `Ring_ReopensOnNextPhase` (2) |
| `Ring_OutsidePulses_InsideSafe` / `Ring_RollIFrames_NoDamage` (3) |
| `Spokes_ClampedInsideRing_LeaveStrafeLane` / `Slam_NotClamped` (4) |
| `PhaseTwo_FourSpokes` (5) |
| `PhaseThree_FasterTelegraph_ShorterGap_ThreePods` / `Telegraphs_AtLeastPointSix` (6) |
| `Reset_DropsRing` / `PhaseZero_AsBefore` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- The ring's look (20g); balance beyond 20h's band.

## As built

_Filled at merge._
