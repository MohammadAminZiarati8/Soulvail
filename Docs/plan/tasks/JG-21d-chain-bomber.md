# JG-21d — The Chain bomber sets off its neighbours

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02); AR §18.2

## Goal

An explosion with a chain radius lights every other bomber near it; each goes off a beat later, so a
crowd of bombers is a fuse the player can light from range — or be caught in.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Core/Ai/ChainBomberTests.cs` | Tests.Core | **New.** Rules 1–7 |
| *small edits* | | `ExplosionSpec` (`chainRadius`, defaulted 0); `EnemySystem` (`Explode` lights, `Tick` burns fuses, `ChainFuseSeconds`); `EnemyAgent` (`ChainFuseAt`); `Events/EnemyEvents.cs` (`EnemyChainLit`) |

## Public API

```csharp
public ExplosionSpec(float radius, float chainRadius = 0f);
public float ExplosionSpec.ChainRadius { get; } // 0 = does not chain
public const float EnemySystem.ChainFuseSeconds = 0.3f;
public readonly struct EnemyChainLit { int Id; int ByEnemyId; Vector3 From; Vector3 To; float Seconds; }
```

Authored values (JG-21f): explosion 2.5 m, chain 4 m.

## Behaviour

1. **Lighting**: when an agent whose explosion has `ChainRadius > 0` explodes, every other living
   agent **with any explosion block** within `ChainRadius` (XZ) and not already lit is lit:
   `EnemyChainLit` once each.
2. **Burning**: a lit agent dies `ChainFuseSeconds` later through `Execute` — the one door — so it
   pays XP and counts as a kill like any death, then explodes (and may light others).
3. **A chain ends**: an agent is lit at most once; a ring of bombers terminates.
4. **Its own fuse first**: a lit Bloater whose own contact fuse ends earlier dies by that; it is not
   killed twice.
5. **Not lit**: bosses, boss summons whose spec has no explosion, anything outside the radius.
   Lighting never damages by itself.
6. **`ChainRadius` 0** is the explosion as before (Bloater and Puffball tests unchanged).
7. **No allocation** in `Tick` with 16 lit agents.

`ExplosionSpec` refuses a negative or non-finite `chainRadius`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Chain_LightsBombersInRadius` | bombers at 3 m and 5 m / only 3 m lit, one event (1) |
| `Chain_IgnoresNonBombers` | Frog at 2 m / not lit (1, 5) |
| `Chain_BurnsAfterFuseAndPays` | 0.29 s alive, 0.31 s dead, XP banked, exploded (2) |
| `Chain_Propagates` | A–B–C 3 m apart, kill A / B then C, 0.3 s apart (2) |
| `Chain_LightsEachOnce` | four in a ring / four deaths, no repeat (3) |
| `Chain_OwnFuseWins` | lit mid-fuse, own fuse ends first / one death (4) |
| `ZeroChain_IsTheOldExplosion` | Puffball / nothing lit (6) |
| `Tick_DoesNotAllocate` | 16 lit (7) |

## Out of scope

- The flash and the link line (JG-21e); data (JG-21f); Elites' Volatile affix.

## As built

_Filled at merge._
