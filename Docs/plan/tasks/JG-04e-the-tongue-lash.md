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

_Filled at merge._
