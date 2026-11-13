# SS-14a — The vulture in core: out of reach aloft, a locked dive line, a grounded window

**Size:** M · **Depends on:** — · **Branch:** `ss-14a-vulture-core`
**Design refs:** GD §8.14, §8.10 (the antlion's guard), JG-04e (`LaneSystem`), JG-25h (open ground)

## Goal

A new `VultureBehaviour` runs Climb → Circle → Lock → Dive → Grounded → Climb. Aloft it is out of reach as a burrowed antlion is. Its dive is a `LaneSystem` lane from its shadow through the hero, and it sits targetable for 2 s where it lands.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/DiveSpec.cs` | Core | `BurrowSpec`'s shape and validation |
| `Core/Ai/VultureBehaviour.cs` | Core | the cycle |
| `Core/Events/VultureEvents.cs` | Core | `VulturePhase`, `VulturePhaseChanged`, `VultureDiveLocked` |
| `Tests/Core/Ai/VultureBehaviourTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec` (`dive = null` last, property, guard); `EnemyBehaviourKind.Vulture` appended; `EnemyAgent`'s factory case; `EnemySystem.IsBurrowedAntlion` renamed `IsOutOfReach` and true for a `VultureBehaviour` that is not vulnerable too (every caller unchanged; antlion tests unchanged) |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class DiveSpec   // finite, > 0; MinInterval ≤ MaxInterval; LineLength > CircleRadius
{
    public DiveSpec(float circleRadius, float circleSpeed, float approachSpeed, float minIntervalSeconds,
        float maxIntervalSeconds, float lockSeconds, float lineLength, float lineHalfWidth,
        float diveSeconds, float diveDamage, float groundedSeconds, float climbSeconds);
        // 7, 4, 6, 4, 6, 1, 10, 0.6, 0.4, 14, 2, 0.6 — a get-only property each
}
// EnemySpec.Dive — required for Vulture, refused on any other kind.
namespace Soulvail.Core.Events;
public enum VulturePhase { Climb, Circle, Lock, Dive, Grounded }
public readonly struct VulturePhaseChanged { int Id; VulturePhase Phase; float Seconds; }
public readonly struct VultureDiveLocked { int Id; int LaneId; Vector3 Origin; Vector2 Direction; Vector3 Landing; float Seconds; }
// VultureBehaviour.State, .DiveCount
```

## Behaviour

1. **Spawn.** It starts in `Climb` for `ClimbSeconds`, then `Circle`.
2. **Out of reach** in `Climb`, `Circle`, `Lock` and `Dive`: `IsVulnerable` false, `IsHidden` true, `Health.SetExternalInvulnerable(true)`, written every tick (the antlion's `SetUnderground` shape). `IsOutOfReach` refuses CC, poison, marks and executes. Auto-aim, skills and focus skip it. Leaving `Dive` restores all of it.
3. **Circle.** It steers to the hero's 7 m circle at `ApproachSpeed`, then runs round it at `CircleSpeed`, clockwise for an even id and anticlockwise for an odd one, ignoring cover and enemies.
4. **Interval.** The nth dive (from 0) waits `Min + (Max − Min) × ((n + id) mod 3) / 2` seconds of `Circle`: 4, 5 and 6 s in turn.
5. **Lock.** It stops and opens a lane: `ctx.Lanes.Open(its position, toward the hero, LineLength, LineHalfWidth, LockSeconds, DiveDamage, ctx.Now)`. If that returns 0 (lanes full) it keeps circling and tries the next tick. The landing is the lane's end, moved by `GravemawBehaviour.FindOpenSpot` when it lies in cover. One `VultureDiveLocked`. Nothing turns or cancels the lane.
6. **The hit** is the lane's, as built: the hero only, inside the strip as it fires, roll and guard as built. Pets and minions are untouched.
7. **Dive.** When the lane fires it moves to the landing over `DiveSeconds`.
8. **Grounded** for `GroundedSeconds`: vulnerable, turns toward the hero at `ShamanBehaviour.TurnRateDegrees`, no attack. A stun or root holds the clock (the antlion's `SurfaceHoldSeconds`); a slow does not. Then `Climb`.
9. One `VulturePhaseChanged` on each entry, with that phase's seconds (Circle's is its interval).
10. Death while grounded is normal; it cannot die aloft (rule 2). `Reset()` returns to rule 1 with the dive count 0.
11. `Tick` allocates nothing in any state.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_Guards` | Vulture without the spec; spec on a Chaser; bad numbers / construct / throws |
| `Spawn_ClimbsThenCircles` | new / tick 0.6 s / `Climb` → `Circle`; out of reach from tick 0 |
| `Aloft_IsUntouchable` | in `Circle` / damage, stun, slow, poison, target query / blocked, none applied, not a candidate |
| `Circle_HoldsSevenMetres` | hero still / 3 s / on the 7 m circle, moving round at 4 m/s |
| `Circle_DirectionByIdParity` | ids 2 and 3 / 1 s / opposite senses |
| `Interval_FourFiveSix` | id 0 / three cycles / locks after 4, 5, 6 s of `Circle` |
| `Lock_OpensLaneThroughHero` | lock / read the lane / from the vulture, through the hero, 10 m × 1.2 m, 1.0 s, 14 |
| `Lock_LanesFullRetries` | lanes at capacity / tick / still `Circle`; locks once one frees |
| `Lock_LandingAvoidsCover` | lane ends in a rock / lock / landing on open ground |
| `Dive_HitsInsideLine` | hero still / fire / 14 once |
| `Dive_MissesASidestep` | hero steps 0.7 m sideways in the lock / fire / no damage |
| `Dive_IgnoresPets` | hound on the line / fire / untouched |
| `Grounded_ExposedForTwo` | landed / 2 s / vulnerable, a candidate, no attack; then `Climb`, out of reach |
| `Grounded_StunHoldsClock` | 1 s stun at 1 s / tick / grounded ends at 3 s |
| `Events_OnePerPhase` | full cycle / record / Climb, Circle, Lock, Dive, Grounded, Climb, with seconds |
| `Reset_Aloft_Cleans` | in `Circle` / `Reset` / `Climb`, dive count 0 |
| `Tick_Cycle_NoAlloc` | two cycles / ticks / `AllocationAssert.None` |
| `Antlion*`, `Lane*`, `Gravemaw*` | unchanged / run / green |

No count or coverage test reads `EnemyBehaviourKind`'s length (grep, 2026-11-13); one that breaks moves by one and *As built* names it.

## Out of scope

- Model, body, shadow, line drawing (SS-14b–c). Data, roster (SS-14d). Attacks on pets. Elites.

## As built

_Commit body._
