# JG-08b — Old Croaker holds its aim through the lash

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** JG-04e (the lash), GD §9.1 (telegraphs)

## Goal

Once the tongue lash winds up, Old Croaker faces down its lane until the tongue lands, wherever the
player runs.

## Why this shape

- **The lane is already fixed.** `EnterLash` arms it along `DirectionToPlayer` at the wind-up. Only the
  body turns: `TickLash` calls `Stand()`, and `Stand()` faces the player every tick. The body then points
  one way while the lane bites another, which is the bug the owner saw.
- **The aim is held in a field**, `_lashAim`, set in `EnterLash` and emitted as the facing by `TickLash`.
  `Stand()` keeps its meaning for Idle, the croak and recovery, where facing the player is right.
- The hop-slam already faces its hop velocity. The Panther (`LungerBehaviour`) already holds its lane. Both
  are left alone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/CroakerBehaviour.cs` | Core | `_lashAim`; `TickLash` emits it, zero velocity |
| `Tests/Core/Ai/CroakerBehaviourTests.cs` | Tests.Core | rules 1–3 |

## Public API

```csharp
// No new public members.
```

## Behaviour

1. Through the whole lash, every `EnemyMoveIntent` for Old Croaker has zero velocity and `FacingXZ` equal to
   the lane's direction, even when the player circles the boss.
2. The lane opened at `EnterLash` and the held facing are the same direction.
3. After the lash, back in Approach, the facing follows the player again.

## Tests

| Test | Rule |
|---|---|
| `Croaker_Lash_HoldsFacingWhilePlayerCircles` (player moved 90° mid-telegraph) | 1 |
| `Croaker_Lash_FacingMatchesTheLane` | 2 |
| `Croaker_AfterLash_FacesThePlayerAgain` | 3 |

## Manual verification (Editor / device)

1. **[Editor]** Stage 5: as the tongue winds up, run sideways. The frog keeps looking down the lane, and the
   tongue shoots where it looks.

## Out of scope

- The Warden's facing. Turning speed or smoothing in `EnemyView.Face`.

## As built

_Not built._
