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

_2026-10-26._ Built as specified, with three deviations.

**The hop is 0.7 s with a 0.75 s sit, not 0.8 s and 0.85 s.** The spec took the `Hop` clip's length
(24 frames). `FrogBodyTests` measures the Frog's `HopTime` from when the clip's feet land (0.5 s of a
0.6 s clip), and sampled the same way, `Croaker.fbx`'s `Hop` lands at 0.70 s, with its last 0.1 s a settle. A
0.75 s sit keeps the leap at 1.8 × 1.45 / 0.7 ≈ 3.7 m/s, the stride the spec aimed for. The leap is
about 2.6 m. A new `CroakerBodyTests.Croaker_HopTimeIsTheClipsLeap` pins `HopTime` to the clip's landing,
the Frog's row for the boss. That row is an extra test, not a rule change.

**`HopGait.Grounded` was not built.** The Croaker remembers whether its last approach tick moved
(`_leaping`, cleared on `EnterApproach` and `Reset`), which answers "mid-leap?" without reading the
phase back. The phase cannot answer it cleanly: a wrapped timer reads as "in the air" on the tick a leap
has not yet left. `HopGait` is `Speed` alone.

**`TickApproach` zeroes the timer while it sits in range**, so the next move it makes leaves at the top
of a leap. That was implied by rule 2's "every approach starts at the top of a leap" and is written down here.

The Croaker body with no hop (`HopTime` 0, the fixture's plain `enemy.croaker`) walks and decides every
tick, exactly as before, so every JG-04 row passes unedited. The new rows drive a second fixture body,
`enemy.croaker.hopper`, with the asset's gait. `ChaserBehaviourTests` pass unedited (rule 1).
Croaker, Chaser, CroakerBody, FrogBody and BossDispatch fixtures: 50/50.
