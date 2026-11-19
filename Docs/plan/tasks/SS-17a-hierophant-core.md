# SS-17a — Hierophant attacks and phase rotation

**Size:** M · **Depends on:** Sunscar regular waves · **Branch:** `ss-17a-hierophant-core`

Implement the committed staff and seal attacks in [GD §9.5](../../GameDesign.md#95-the-sunscar--stage-15-the-sunscar-hierophant).

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Core/Ai/HierophantBehaviour.cs` | Approach, casts, recovery and phase rotation |
| `Core/Ai/HierophantStats.cs` | Attack and layout stats |
| `Core/Events/HierophantEvents.cs` | Immutable presentation payloads |
| `Tests/Core/Ai/HierophantBehaviourTests.cs` | Geometry, timing, cancellation and allocation |

## Public API

```csharp
public sealed class HierophantBehaviour : IEnemyBehaviour, IBossPhaseListener
{
    public HierophantBehaviour(EnemyAgent agent, HierophantStats stats);
    public void Tick(in EnemyTickContext ctx);
    public void OnPhaseEntered(int phase);
    public void Reset();
}
// HierophantStats: get-only Stat properties, with defaults.
// MoveSpeed 2.25, TurnDegreesPerSecond 360
// SweepRange 3.25, SweepHalfAngleDegrees 50, SweepTellSeconds 1,
// SweepDamage 16, SweepRecoverySeconds 1.25
// SealHalfWidth 2, SealHalfLength 9, SealTellSeconds 1.3,
// SealDamage 20, SealRecoverySeconds 2, SealPairGapSeconds 0.3
public enum HierophantMove { StaffSweep, SunSeal }
// Public readonly structs with matching fields and constructors.
HierophantSealsPlaced(int bossId, Vector2 west, Vector2 centre, Vector2 east);
HierophantArmed(int bossId, HierophantMove move, int sealSlot, Vector3 origin,
    Vector2 facing, float reach, float halfWidth, float halfAngleDegrees, float seconds);
HierophantTellProgress(int bossId, float fraction);
HierophantImpact(int bossId, HierophantMove move, int sealSlot, float recoverySeconds);
```

## Behaviour

1. First tick publishes world XZ seal centres (-4,0), (0,0), (4,0) in slots 0–2. Derive ±4 from twice the half-width and lock the layout for the fight. Select nearest centre by hero X, ties west. Arm an axis-aligned ground rectangle with reach = half-length.
2. Alternate seal and sweep, starting with seal. Path and turn toward the hero for the sweep until in range. Sweep arms a filled sector (slot -1, width 0) using the locked heading and origin. Stationary during tell, gap and recovery. One move intent per tick.
3. At the deadline check the real hero's current XZ with inclusive edges. Resolve one defendable hit inside and one impact event even on a miss. Publish clamped tell progress after arming. Ground damage ignores cover.
4. Phase index 2 uses two seal casts with a full gap and fresh tells. First impact reports zero recovery. Second arm selects from fresh hero X with the first slot excluded. Coarse steps cannot shorten subsequent tells, gaps or recovery.
5. Phase entry cancels the pending cast, gap or recovery and starts the rotation over. The boss wrapper owns beats. Reset reinstates phase 0 and republishes the layout on the next tick. Warm ticks allocate zero.

Tests > Behaviour > API > Files. Built code wins. Validate null and non-finite inputs.

## Tests

| Test | Given / when / then |
|---|---|
| `Seal_ChoosesNearestAndLocks` | All slots, ties and outside X / move during tell / layout and rectangle stay fixed |
| `Sweep_PathsThenCommits` | Outside range, obstructed direct path / approach then arm / one intent and locked sector |
| `Impact_UsesHeroAndArmedGeometry` | Inside, edge, outside, dodge, decoy / tick before and at deadline / one defendable hit where owed |
| `FinalPair_ReaimsWithFullTiming` | Coarse ticks / first seal, gap, second seal, recovery / different slot, fresh warning, one impact per cast |
| `PhaseOrReset_CancelsPendingWork` | Tell, gap or recovery / phase or reset / fresh rotation with no stale impact |
| `Tick_AllocatesNothing` | Warm cycles / `AllocationAssert.None` / zero allocation |

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
