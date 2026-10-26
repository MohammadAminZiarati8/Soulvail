# JG-05c — Old Croaker hops in

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2

## Goal

Old Croaker moves like a Frog: it never walks. It closes on the player in hops with a sit between them,
playing its `Hop` clip once per leap. The slam and the lash are unchanged.

## Why this shape

- **The Frog's gait, shared rather than copied.** `ChaserBehaviour.TickChase` holds the hop arithmetic
  (a leap of `HopTime`, a rest of `HopRestTime`, and the leap fast enough that the average is `MoveSpeed`).
  It moves into a small pure helper, `HopGait`, and both behaviours call it. `Croaker.asset` already
  carries `_hopTime` and `_hopRestTime` (both 0), so no new field is needed.
- **The numbers come from the clip.** `Croaker.fbx`'s `Hop` is 24 frames, which is 0.8 s. Its true stride at the
  prefab's scale is ~3.7 m/s (JG-04d's *As built*, in git). A 0.8 s leap plus a 0.85 s rest at `MoveSpeed` 1.8
  gives a leap speed of 1.8 × 1.65 / 0.8 ≈ 3.7 m/s. The feet match the ground, and the average approach speed stays 1.8.
- **It decides only on the ground.** A leap in progress is finished before the boss stops in slam
  range or starts a lash. A frog does not stop in mid-air.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/HopGait.cs` | Core | **New.** `HopGait.Speed`: the hop arithmetic moved out of `ChaserBehaviour` |
| `Core/Ai/CroakerBehaviour.cs` | Core | `EnterApproach` zeroes the timer, and `TickApproach` hops via `HopGait` and checks for an attack only while grounded |
| `Tests/Core/Ai/CroakerBehaviourTests.cs` | Tests.Core | Rules 2–4 |
| *small edits* | | `Core/Ai/ChaserBehaviour.cs` (`TickChase` calls `HopGait.Speed`, same results); `Data/Enemies/Croaker.asset` (`_hopTime` 0.8, `_hopRestTime` 0.85); `Prefabs/Enemies/Croaker.prefab` (`EnemyAnimatorView._hops` on); `Tests/Game/Views/CroakerBodyTests.cs` (the `_hops` row flips to true) |

## Public API

```csharp
namespace Soulvail.Core.Ai;

/// Pure arithmetic: no state of its own, so the no-statics rule (AR §18) is not touched.
public static class HopGait
{
    /// The ground speed this tick. `timer` is the state's StateTimer: read as the phase in the
    /// cycle, then advanced by dt and kept inside one cycle. hopTime <= 0 is a walker: returns speed.
    public static float Speed(float speed, float hopTime, float restTime, ref float timer, float dt);

    /// True while the phase `timer` sits in the rest (or hopTime <= 0).
    public static bool Grounded(float hopTime, float restTime, float timer);
}
```

## Behaviour

1. **`HopGait.Speed` is `TickChase`'s arithmetic, unchanged.** A hopper moves at
   `speed × (hop + rest) / hop` during the leap and 0 at rest, and the timer wraps once per cycle. A walker
   (`hopTime` ≤ 0) gets `speed`. `ChaserBehaviourTests` pass unedited.
2. **The Croaker hops while approaching.** In `Approach` with the player beyond `SlamRange`, it emits a
   non-zero velocity only during each 0.8 s leap and stands still for the 0.85 s rest. Every approach starts
   at the top of a leap.
3. **It decides only on the ground.** Inside `SlamRange`, or when a lash becomes legal, a leap in progress
   still finishes. The boss then stands and slams or lashes on the first grounded tick, as today.
4. **Slam, lash, rest and cooldowns are unchanged.** `SlamRange`, `LashMinRange`/`LashRange`,
   `AttackGapSeconds` and both cooldowns keep their values and meaning.

## Tests

| Test | Rule |
|---|---|
| `HopGait_MatchesTheChasersOldArithmetic` / `HopGait_WalkerGetsItsSpeed` (in `CroakerBehaviourTests`, or a `HopGaitTests` if it reads better: then say so) | 1 |
| `Approach_MovesOnlyInTheAir` / `Approach_StartsInTheAir` | 2 |
| `Approach_FinishesTheLeap_ThenSlams` | 3 |
| `Approach_InRangeOnTheGround_SlamsAsBefore` | 4 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 5: Old Croaker never walks. It hops, sits, hops again, each hop a full `Hop`
   clip with its feet on the ground, then lands, stops and slams.
2. **[Editor]** A lash can only start after a landing, never in mid-air.

## Out of scope

- Hop-slamming *as* its movement (each approach hop dealing damage). A hop to close in is harmless,
  and only the slam threatens.
- Changing `SlamRange` or cooldowns to suit the new approach. The owner judges that in the playtest.

## As built

_Filled at merge._
