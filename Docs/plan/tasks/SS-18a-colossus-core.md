# SS-18a — Sundial Colossus combat

**Size:** M · **Depends on:** Sunscar Stage 15 · **Branch:** `ss-18a-colossus-core`

**Design ref:** [GD §9.6](../../GameDesign.md#96-the-sunscar--stage-20-the-sundial-colossus).

## Goal

Implement locked sweep sectors, a close-range stomp and the final-phase return pass in Core.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Core/Ai/ColossusBehaviour.cs` | Approach, attacks and phase cancellation |
| `Core/Ai/ColossusStats.cs` | Attack and movement stats |
| `Core/Events/ColossusEvents.cs` | Locked footprints, progress and impacts |
| `Tests/Core/Ai/ColossusBehaviourTests.cs` | Timing, geometry, defence and reset |

## Public API

```csharp
public sealed class ColossusBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public ColossusBehaviour(EnemyAgent agent, ColossusStats stats);
    public void Tick(in EnemyTickContext ctx);
    public void OnPhaseEntered(int phase);
    public void Reset();
}
// ColossusStats get-only Stat defaults:
// MoveSpeed 2.2, SweepRadius 9.5, SectorDegrees 20,
// SweepTellSeconds 1.35, SectorIntervalSeconds 0.35, FastSectorIntervalSeconds 0.22,
// SweepDamage 20, SweepRecoverySeconds 1.8, ReturnGapSeconds 0.30,
// ReturnTellSeconds 1.35, ReturnRecoverySeconds 2.4,
// StompTriggerRadius 2.8, StompRadius 3.3, StompTellSeconds 1.0,
// StompDamage 16, StompRecoverySeconds 1.25
public enum ColossusMove { Sweep, Stomp, ReverseSweep }
// Public readonly structs with matching constructors and fields.
ColossusArmed(int bossId, ColossusMove move, Vector3 origin, Vector2 facing,
    float radius, float sectorDegrees, float seconds);
ColossusTellProgress(int bossId, float fraction);
ColossusImpact(int bossId, ColossusMove move, int sector, float recoverySeconds);
```

## Behaviour

1. Open with Sweep. Path to sweep range. At arm time lock origin and XZ heading to the quarry, keeping the last valid heading on coincidence (initially +Z). Stand through recovery. Emit one move intent per tick.
2. Slots 0–2 are adjacent sectors centred at -SectorDegrees, 0 and +SectorDegrees, left to right. Strike in order. Outer edges are inclusive. Shared edges belong to the higher slot, the origin to slot 1. Ground damage ignores cover. Attempt one defendable hit per pass. Decoy and pet quarry never damage the hero. Emit every impact, with zero recovery before the last slot.
3. After sweep recovery, Stomp once within its trigger radius. Use a filled boss-centred disc with an inclusive edge, zero sector degrees and impact slot -1. Check current quarry at impact for both attacks. Then resume Sweep.
4. Phases 1 and 2 shorten slot intervals. In phase 2 the forward pass ends with zero recovery. After the gap, ReverseSweep uses the same geometry, a full new tell and order 2, 1, 0. The last slot starts the longer recovery. Coarse ticks preserve full tells, intervals, gaps and recovery. Publish clamped tell progress.
5. Phase entry cancels pending work and opens with Sweep after the beat. Reset restores initial phase and heading. Warm ticks allocate zero.

Tests > Behaviour > API > Files. Built code wins. Validate null and non-finite inputs.

## Tests

| Test | Given / when / then |
|---|---|
| `Sweep_PathsAndLocksHeading` | Obstructed route / approach and arm / one path intent per tick and locked footprint |
| `Sweep_ResolvesSlotsAndDefence` | Edges, centre, cover, decoy, pet, roll, moving hero / strike / correct hit and impact counts |
| `Stomp_TriggersOnlyAfterSweep` | Near and far hero / end recovery / correct rotation and disc hit |
| `FinalSweep_ReversesWithFullTiming` | All phases and coarse ticks / cast / slot order, faster spacing, full tells and final recovery |
| `PhaseAndReset_ClearPendingWork` | Tell, pass, gap or recovery / phase entry or reset / no stale impact and correct restart |
| `Tick_AllocatesNothing` | Warm attack cycles / `AllocationAssert.None` / zero bytes |

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
