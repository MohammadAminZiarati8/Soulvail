# JG-05a — A blocked body reports its real speed

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** AR §3, §4.2 · CC §4 (basic attack, lead)

## Goal

A Ranger's arrow hits a Pitcher that is backed against a wall. Today it lands beside the Pitcher every time.

## Why it misses

The player's shot is aimed by `ProjectileLead.Solve(playerPosition, agent.Position, agent.Velocity, shotSpeed)`
(`PlayerCombat`), and `agent.Velocity` is `EnemySense.Velocity`, which `EnemyView` reports. But
`EnemyView.Apply` stores the velocity it was **told** to move at (`_velocity = horizontal`), not the one
the `CharacterController` actually moved at. A Pitcher kiting away from the player (2.8 m/s, standoff
14 m) that is pinned against a wall still reports 2.8 m/s of retreat. The lead puts the shot's impact
point ~2 m behind it, inside the wall, and the shot's 0.5 m radius misses (`ProjectileSystem.LandOnEnemies`
tests the impact point, not the path). Any enemy blocked while moving (by a wall, a pillar or a crowd)
has the same fault. The Pitcher shows it most because it backs straight into walls.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyView.cs` | Game | `Velocity` is the body's horizontal displacement this frame over `dt`, measured after `CharacterController.Move`, in both `Apply` and the knockback step |
| `Tests/Game/Views/EnemyViewsTests.cs` | Tests.Game | Rules 1–3 |

Only these files change.

## Public API

```csharp
// Unchanged. EnemyView.Velocity keeps its signature; its meaning becomes "how fast the body moved".
public Vector3 Velocity => _velocity;
```

## Behaviour

1. **A free body reports the velocity it was told to move at**, as today, to within float error.
2. **A blocked body reports what it actually moved.** A body walked into a wall reports a horizontal
   speed near 0 along the wall's normal. It reports only the part of its motion that slid along the wall.
3. **Vertical motion is never reported.** Gravity and the step offset stay out of `Velocity`, and a zero or
   negative `dt` reports zero rather than dividing by it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Apply_FreeBody_ReportsTheIntent` | A body on open ground / `Apply` 2 m/s along +X / `Velocity` ≈ (2, 0, 0) |
| `Apply_IntoAWall_ReportsNearZero` | A body touching a box collider on +X / `Apply` 2 m/s along +X (after `Physics.SyncTransforms`) / `Velocity.magnitude` < 0.1 |
| `Apply_ZeroDt_ReportsZero` | Any body / `Apply` with `dt` 0 / `Velocity` is zero, and `y` is 0 in every row above |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 2: chase a Pitcher until it backs into the arena wall. Every Ranger arrow
   lands on it and plays its hit flash.
2. **[Editor]** A Frog that walks into a pillar does not show its hop flying through the pillar. (The
   animator reads the same speed.)

## Out of scope

- Teaching the Pitcher to slide along a wall or step sideways away from it. That is a behaviour change,
  and the owner hasn't asked for it.
- `MinionView`'s velocity: minions are the player's, and no shot leads them.
- Sweeping the shot along its path in core. Core holds no walls (`ProjectileSystem` remarks).

## As built

_Filled at merge._
