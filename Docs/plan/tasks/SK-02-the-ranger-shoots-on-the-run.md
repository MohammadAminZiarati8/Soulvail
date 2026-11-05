# SK-02 — The Ranger shoots on the run

**Size:** M · **Depends on:** SK-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Running Shot); RS-07 (the Ranger's bow and release)

## Goal

Running Shot reads as running and shooting: the bow stays up, the upper body aims and releases at
the target, and the legs keep running in the direction of travel.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Views/RangerAnimatorView.cs` | Game | Locomotion by move-vs-facing angle; release on the upper-body layer while moving |
| `Animation/Controllers/AC_Ranger.controller` | controller | Upper-body layer (masked); a 2D locomotion blend |
| `Animation/Masks/AM_RangerUpperBody.mask` | mask | **New.** Spine and up |
| `Tests/Game/Views/RangerRunShotTests.cs` | Tests.Game | **New** |
| *small edits* | | none expected |

## Behaviour

1. **Bow up:** while `FireWhileMoving` > 0.5 moving is not a hold, so the bow stays raised on the
   run. If core still publishes `HoldFireChanged` for moving in that case, fix it at its source and
   say so in *As built*. Nothing changes for the Ranger without Running Shot.
2. **Facing:** the body faces core's `FaceDirection` (the aim while firing, SK-01a rule 2).
3. **Legs:** a locomotion blend picks run forward, strafe left/right or run backwards from the
   angle between the move direction and the facing, using the rig's own movement clips
   (`Rig_Medium` set). A missing clip falls back to the nearest one that exists; name any fallback
   in *As built*.
4. **Release on the move:** `PlayerAttacked` while moving plays the draw and release on the
   upper-body layer only; the legs never stop. Standing, the release plays as today.
5. The upper-body layer's weight drops to 0 within 0.15 s of the last release, so a plain run
   looks as it does today.
6. Roll, Killshot's kneel, flinch and death override the layer as they override today.

## Tests

| Test | Given / When / Then |
|---|---|
| `RunningShot_BowStaysUpWhileMoving` | Running Shot, moving / no lower-bow state (1) |
| `NoRunningShot_BowLowersAsToday` (1) |
| `Legs_PickClipByAngle` | 0°, 90°, −90°, 180° / forward, strafe R, strafe L, back (3) |
| `Release_WhileMoving_UpperLayerOnly` | base layer stays in locomotion (4) |
| `Release_Standing_AsToday` (4) |
| `UpperLayer_FadesAfterLastRelease` (5) |
| `Roll_OverridesUpperLayer` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Skirmisher, Running Shot taken, a target at the side: the Ranger strafes, aims
   sideways and releases without stopping; running away from it, it back-pedals and shoots. With no
   target it runs normally, bow raised.

## Out of scope

- New animation clips; the look (SK-07).

## As built

_Filled at merge._
