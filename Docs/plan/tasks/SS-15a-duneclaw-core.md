# SS-15a — The Duneclaw's sweep, slam and recovery

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** [GD §9.3](../../GameDesign.md#93-the-sunscar--stage-5-the-duneclaw), Invariants §§18.2–18.5, `BossBehaviour`

## Goal

The Duneclaw fights through the boss wrapper with two locked attacks and a final-phase combo.

## Files

| Path under `Assets/_Project/` | Assembly | Purpose |
|---|---|---|
| `Core/Ai/DuneclawBehaviour.cs` | Core | Approach, attack and phase state |
| `Core/Ai/DuneclawStats.cs` | Core | Tunable motion and attack stats |
| `Core/Events/DuneclawEvents.cs` | Core | Move and presentation facts |
| `Tests/Core/Ai/DuneclawBehaviourTests.cs` | Tests.Core | Geometry, timing, phase and reset |
| `Core/Run/RunSession.cs` | Core | Register inner behaviour for `boss.duneclaw` |

## Public API

```csharp
// DuneclawBehaviour(EnemyAgent agent, DuneclawStats stats)
// IEnemyBehaviour, IBossPhaseListener
// DuneclawMove: Sweep, Slam
// DuneclawArmed: BossId, Move, Origin (Vector3), Facing (Vector2, XZ),
//   Target (Vector3), Radius (float), ArcDegrees (float), Seconds (float)
// DuneclawImpact: BossId, Move, Target (Vector3), RecoverySeconds (float)
```

## Behaviour

1. Approach using `PathDirectionToPlayer` at 1.8 m/s. Open with sweep when in reach.
   In phases 0–1 alternate sweep and slam, approaching again as needed. Sweep reach 3.2 m,
   full arc 100°, wind-up 1.0 s, recovery 0.8 s. Slam acquisition range 4.5 m, radius 1.5 m,
   wind-up 1.0 s, pin 2.0 s. Initial damage stats are 12 and 16 respectively.
2. Lock sweep origin and heading or slam centre at arm time, using the player's current XZ
   position for aim. Keep the boss stationary through wind-up and recovery. At expiry test
   the player's current XZ once against the announced area and use `PlayerCombat.ApplyDamage`
   on a hit. Publish impact and recover on hits and misses. Normal roll and i-frame rules apply.
3. Phase 1 uses the same cadence. Phase 2 follows each sweep impact with a fresh slam tell
   after a 0.35 s gap when in slam range. If out of range, approach before arming the slam.
   The slam takes its full wind-up and recovery. Core moves and clocks use `Stat` and snapshot dt.
4. Phase entry cancels the current attack and combo, and the next active move starts with
   a sweep. `Reset` clears attack, clocks and phase. The wrapper owns the beat, invulnerability
   and summons. No tick allocations after warm-up.

## Tests

| Test | Given / When / Then |
|---|---|
| `Approach_AlternatesWithinReach` | Walk and attack / both opening phases / correct order, ranges and timings (1, 3) |
| `Sweep_UsesLockedArc_AtExpiryOnly` | Walk across edge / wind-up then impact / correct angle, range, damage and single hit (2) |
| `Slam_UsesLockedCentre_AtExpiryOnly` | Walk out or stay / impact / dodge or one hit with the same pin (2) |
| `Attack_IFramesAndRollUseNormalDamagePath` | Inside tell during protection / impact / normal protection (2) |
| `PhaseTwo_SweepThenFreshSlam_AfterGapAndInRange` | Live phase 2 / near or far / fresh full tell and pin (3) |
| `PhaseEntryOrReset_DropsPendingHitAndRestartsAtSweep` | Armed slash or queued combo / transition or reset / no stale hit (4) |
| `DuneclawTick_NoAllocations` | Warmed behaviour / repeated ticks / `AllocationAssert.None` (4) |

## Out of scope

Scorpling data, slab effects and rendering land in subsequent tasks.

## As built

_Written at merge in the commit body._
