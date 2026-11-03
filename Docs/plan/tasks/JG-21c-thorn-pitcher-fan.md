# JG-21c — The Thorn pitcher throws a fan of three

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02); AR §18.2, §18.4

## Goal

A Spitter whose `ProjectileSpec` has a count above one throws that many ringed lobs at once, landing
in a line across the throw, so standing still or stepping sideways a little is never safe.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Ai/ThornPitcherFanTests.cs` | Tests.Core | **New.** Rules 1–6 |
| *small edits* | | `ProjectileSpec` (`count`, `spread`, defaulted); `SpitterBehaviour.EnterRelease` (fires the fan) |

## Public API

```csharp
public ProjectileSpec(float standoffRange, float speed, float radius, int count = 1,
    float spread = 0f);
public int ProjectileSpec.Count { get; }    // 1..5
public float ProjectileSpec.Spread { get; } // metres between landings
```

Authored values (JG-21f): count 3, spread 3 m, radius 1.4 m. The GD's "fan or ringed lobs" is
resolved as one move: a fan *of* ringed lobs.

## Behaviour

1. **Landings**: lob *k* of *n* lands at the player's position plus
   `(k − (n − 1) / 2) × spread` along the perpendicular to the throw (XZ), so the middle lands on the
   player for odd *n*.
2. **One release**: all *n* are fired in the same tick, each a normal `Projectile` with the
   Spitter's speed, radius and `ContactDamage`; each publishes its own `ProjectileFired`, so each
   draws its own ring.
3. **Each lob hits on its own**; a player inside two overlapping rings is hit by each (the spread
   keeps rings apart at the authored values).
4. **A full projectile table** fires what fits and drops the rest, from the outside in; nothing
   throws.
5. **`count` 1** is the Spitter as before (Pitcher and Spitter tests unchanged).
6. **No allocation** in `Tick` at 8 pitchers.

`ProjectileSpec` refuses: `count` outside 1–5, a negative or non-finite `spread`, `count` > 1 with
`spread` 0.

## Tests

| Test | Given / When / Then |
|---|---|
| `Fan_LandsAcrossTheThrow` | 3, 3 m, player due north / landings at x −3, 0, +3 (1) |
| `Fan_FiresAllInOneTick` | release / 3 `ProjectileFired` that tick (2) |
| `Fan_EachLobHitsAlone` | player under the middle / one hit (3) |
| `Fan_FullTableDropsOutsideFirst` | 2 slots free / middle and one side fired (4) |
| `CountOne_IsTheOldSpitter` | Pitcher spec / one lob at the player (5) |
| `Tick_DoesNotAllocate` | 8 agents (6) |

## Out of scope

- Data (JG-21f). Lead aim; every lob aims where the player stands.

## As built

_Filled at merge._
