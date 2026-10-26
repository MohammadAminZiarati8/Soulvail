# JG-04e — The tongue lash

**Size:** M · **Depends on:** JG-04b, JG-04d · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 7

## Goal

Old Croaker's second attack: a lane telegraph from its mouth toward the player, then the tongue strikes
down it. Core gets a reusable `LaneSystem`, the line counterpart of `FissureSystem`, which later bosses
(the Strangler's vine walls) can use.

## Why this shape

- **A lane, not a cone.** On a portrait screen, a lane that runs along the player's line from the boss
  reads whole, and the safe answer is one sidestep. A wide sweep would leave the screen's sides.
- **The same lifecycle as a fissure:** armed, fired once, closed. Events are `LaneArmed`, `LaneFired`
  and `LaneClosed`, drawn by `BossViews` the way it draws fissures.

## Files

| Path (under `Assets/_Project/`) | Purpose |
|---|---|
| `Core/Combat/Lane.cs` | **New.** `LaneSystem`: `Open(origin, direction, length, halfWidth, armSeconds, damage)`, `Tick`, `Clear`; capacity 4 |
| `Tests/Core/Combat/LaneSystemTests.cs` | **New.** Rules 1–3 |
| *small edits* | `Core/Events/BossEvents.cs` (three events); `Core/Ai/CroakerBehaviour.cs` (`Lash` state); `Core/Run/RunSession.cs` (one `LaneSystem`, ticked and cleared beside fissures); `Game/Views/BossViews.cs` (draws a lane as a red-orange strip); `Tests/Core/Ai/CroakerBehaviourTests.cs` (rule 4) |

## Behaviour

1. **A lane damages once, on firing**, if the player is inside the rectangle (origin, direction, length,
   half-width). It then closes after 0.2 s.
2. **Arming publishes `LaneArmed`** with the geometry and arm time (≥ 0.6 s), and firing publishes `LaneFired`.
3. `Clear` closes every lane (the phase beat and stage end), and a full system drops a new lane
   without error, as `FissureSystem` does.
4. **The Croaker lashes** when the player is between 3 m and 9 m away and the slam is on cooldown.
   It publishes `EnemyTelegraph(id, 0.8 s, Move 2)` and opens a lane 9 m long, 0.8 m half-width,
   aimed at the player when the wind-up starts. Cooldown is 4 s.

## Tests

| Test | Rule |
|---|---|
| `Fire_HitsInsideTheLane_MissesBeside` | 1 |
| `Open_PublishesArmed_FirePublishesFired` | 2 |
| `Clear_ClosesAll` / `Full_DropsTheNewLane` | 3 |
| `Croaker_LashesInRange_WhenSlamIsCooling` | 4 |

## Manual verification

1. **[Editor]** Jungle stage 5, mid-range: a red-orange strip runs from the frog toward you, and one
   sidestep avoids the tongue.

## Out of scope

The mud hazard (next, if the owner wants it); the Strangler.

## As built

_2026-10-26._ `LaneSystem` (`Core/Combat/Lane.cs`) is `FissureSystem` line for line, but for the shape (a
rectangle from an origin along a unit XZ direction) and the open window, a constant
`OpenSeconds = 0.2`. `Open(origin, direction, length, halfWidth, armSeconds, damage, now)` also takes
`now`, as the fissure's does. The direction is read on XZ and normalised, and one with no XZ length is
refused. Behind the mouth and past the tip are outside the lane. `LaneArmed` carries the origin, the
direction (a `Vector2`, X then Z), length, half-width and arm.

- **The lash fills the slam's cooldown and never takes the slam's turn.** In `Approach`: if the slam is
  cooling, the lash is off its 4 s cooldown and the player is 3–9 m away, it lashes. Otherwise it is the
  old slam-or-close logic. No draw is made, so the `Misc` stream is still guarded and not held.
- **Lash** is rooted. On entry it publishes `EnemyTelegraph(id, 0.8 s, Move 2)` and opens the lane from
  the body's position, along `DirectionToPlayer`, armed for exactly the telegraph. After the telegraph it
  goes back to `Approach`, with no recover. A phase beat mid-lash abandons it, and the lane fires anyway,
  as a hop's fissure does. New public constants: `LashTelegraphSeconds`, `LashMinRange`, `LashRange`,
  `LashCooldown`, `LashLength`, `LashHalfWidth`, plus `NextLashAt`.
- **`Clear` is silent, as the fissure's is.** `RunSession.End` calls it after `RunEnded`. The strip
  `BossViews` draws runs on its own clock (the arm, then `OpenSeconds`), so a silently cleared lane is
  not left on the floor. `LaneClosed` usually retires it first.
- **The strip is built in code. There is no prefab.** It is the fissure prefab's own Quad, with a runtime
  copy of `M_TelegraphRing` whose `_BaseMap` is white, because the ring texture is a disc and would
  stretch into an ellipse. `Palette.Danger` is premultiplied as `FissureView.Paint` does it. The alpha goes
  from 0.3 to 0.6 over the arm, flashes to 0.95, then fades over the open window. `BossViews` builds
  `LaneSystem.Capacity` strips at construction and destroys them, and the material, on `Dispose`. It needs
  no `RunScope` change.

**Deviations (said here):** `LaneSystemTests` has one more row than the table,
`Fire_DamagesOnce_ThenClosesAfterItsWindow` (rule 1's once and 0.2 s). `CroakerBehaviourTests.Approach_StopsAtSlamRange`
now moves the player inside 3 m before its "sits on cooldown" tick, because at 8 m a cooling slam is now a
lash. `Tick_AllocatesNothing` ticks a `LaneSystem` too, and its cycle runs both attacks. `CroakerBodyTests`
reads `CroakerBehaviour.LashTelegraphSeconds`. No `BossViews` lane test: the spec lists none. The strip
is unverified on screen (manual step 1).

**Verified.** Full EditMode suite through `TestRunnerApi`: **4 440: 4 438 passed / 1 failed / 1
inconclusive**, the two known rows. All six new rows pass. No compiler error or warning.
`dotnet format whitespace` is clean on all eight .cs files.
