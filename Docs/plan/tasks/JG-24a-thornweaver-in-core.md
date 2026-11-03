# JG-24a — The Thornweaver in core

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.2 (owner, 2026-11-02); AR §18.2; JG-20a's `ThornSystem`

## Goal

`EnemyBehaviourKind.Thornweaver`: it keeps its distance and plants short thorn walls on
Thornheart's `ThornSystem` just beyond the player, cutting off the retreat away from it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ThornweaverSpec.cs` | Core | **New.** The numbers below, validated |
| `Core/Ai/ThornweaverBehaviour.cs` | Core | **New.** `ThornweaverState` and the behaviour |
| `Tests/Core/Ai/ThornweaverBehaviourTests.cs` | Tests.Core | **New.** Rules 1–8 |
| *small edits* | | `EnemySpec` (kind **appended** after `Stalker`, `Thornweaver` block); `EnemyAgent`, `EnemySystem` (dispatch; death withers its walls); `EnemyTickContext` (`Thorns`); `ThornSystem` (`Wither(int id)`, `Open`'s `ownerId`); `ThornEvents.ThornArmed` (`OwnerId`) |
| *ripple* | | `RunSession` and `EnemySystem`'s root-gated context pass `Thorns`; `ThornSystemTests` gains rule 7's row |

## Public API

```csharp
public sealed class ThornweaverSpec(float holdDistance, float castRange, float castInterval,
    float castSeconds, float wallLength, float wallHalfWidth, float armSeconds,
    float standSeconds, float beyondDistance, int maxWalls);
public enum EnemyBehaviourKind { …, Stalker, Thornweaver } // appended
public enum ThornweaverState { Approach, Hold, Cast }
public ThornSystem EnemyTickContext.Thorns { get; }
public bool ThornSystem.Wither(int id); // false = no such wall
public int ThornSystem.Open(…, float now, int ownerId = -1); // appended; Thornheart passes its id
public readonly int ThornArmed.OwnerId; // −1 = none
public int ThornweaverBehaviour.WallCount { get; }
```

Authored values (JG-24d): hold 10 m, cast range 14 m, every 5 s (first at 2 s), cast 0.8 s, wall
5 m × 0.4 m, arm 0.9 s, stand 5 s, beyond 2.5 m, at most 2 walls.

## Behaviour

1. **Approach / Hold**: walks the path in until `holdDistance`, then holds; backs off walking out if
   the player comes within `holdDistance − 3`. Faces the player.
2. **Cast**: every `castInterval`, player within `castRange`, fewer than `maxWalls` of its own walls
   arming or standing → stands `castSeconds` with `EnemyTelegraph`.
3. **The wall**: at the cast's end, `Thorns.Open` a wall of `wallLength` centred `beyondDistance`
   past the player on the line from the weaver, **perpendicular** to that line; bite =
   `ContactDamage`, arm and stand as authored. The player's position is read at the cast's end.
4. **Refused** (`Open` returns 0): the cast is spent, nothing else happens; no wall counted.
5. **Own walls** are tracked by id; a wall that withers or is cleared stops counting.
6. **Death withers its walls** through `ThornSystem.Wither`, and only its own.
7. **`ThornSystem.Wither(id)`** removes one wall by id with the same event as `WitherAll`'s;
   `ThornArmed.OwnerId` carries `Open`'s `ownerId` (the weaver's id; Thornheart's own).
8. **No allocation** in `Tick` at 4 weavers.

Walls block only the player (JG-20e); a weaver's walls never stop arrows or enemies.

## Tests

| Test | Given / When / Then |
|---|---|
| `Holds_AtTen` | settle / 10 ± 0.5 m (1) |
| `Cast_OnIntervalInRange` | 12 m; 16 m / cast at 2 s; none (2) |
| `Wall_BeyondPlayerAcrossTheLine` | weaver south / wall 2.5 m north, running east–west, 5 m (3) |
| `Wall_RefusedSpendsTheCast` | table full / no count, next cast 5 s later (4) |
| `Walls_CappedAtTwo` | two standing / no cast until one withers (2, 5) |
| `Death_WithersOnlyItsWalls` | two weavers' walls, kill one / its walls gone, others stand (6) |
| `Wither_RemovesOneById` | `ThornSystemTests` row; `ThornArmed.OwnerId` = weaver id (7) |
| `Tick_DoesNotAllocate` | 4 agents (8) |

## Out of scope

- Body and clips (JG-24c); asset (JG-24d). Walls are drawn by JG-20e's views unchanged.

## As built

_Filled at merge._
