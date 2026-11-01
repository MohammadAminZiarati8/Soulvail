# JG-15c — The Warlord's war cry and shield lock

**Size:** M · **Depends on:** JG-15b · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 3, 4; boss summons pay no XP (`EnemySystem.SpawnSummon`); AR §18.4

## Goal

The Warlord roars two Mossguards in from the arena's edges for no XP; guards standing beside it lock
shields and widen its guard; a phase beat sends them away.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WarlordBehaviour.cs` | Core | War cry move, guard roll-call, shield lock |
| `Core/Ai/IBossAddOwner.cs` | Core | **New.** An inner behaviour that clears its own adds at a phase beat |
| `Tests/Core/Ai/WarlordWarCryTests.cs` | Tests.Core | **New** |
| *small edits* | | `BossBehaviour.EnterPhase` calls `(Inner as IBossAddOwner)?.ClearAdds(ctx)` after its own `ClearAdds` |

## Public API

```csharp
public interface IBossAddOwner { void ClearAdds(in EnemyTickContext ctx); }
// WarlordBehaviour : IBossAddOwner
public const float WarCryTelegraphSeconds = 1f, WarCryCooldownSeconds = 14f,
  WarCryFirstAtSeconds = 8f, LockRadius = 3.5f, LockedHalfArcDeg = 90f, EdgeSpacing = 6f;
public const int MaxGuards = 2;
public int GuardCount { get; }   // living guards it summoned
public bool IsLocked { get; }
```

## Behaviour

1. **Choose:** war cry outranks every other move when it is off cooldown (first after
   `WarCryFirstAtSeconds` of fight time), `GuardCount < 2` and the registry has room. Stand, publish
   `EnemyTelegraph(id, 1.0, 3)`.
2. **Summon** at 1.0 s, `2 − GuardCount` Mossguards via `SpawnSummon` (0 XP): from `ctx.OpenGround`,
   the points farthest from the Warlord that are ≥ `OpenGroundPicker.PlayerClearance` from the
   player and ≥ `EdgeSpacing` from each other; ties to the lower index. Too few points → the rest on
   the `BossBehaviour.AddRingRadius` ring on the side away from the player. Recover; cooldown from
   the release.
3. **Roll-call:** ids are tracked; a dead or despawned guard leaves the count the tick it goes.
   Only guards it summoned count — not stage Mossguards.
4. **Shield lock:** while any living guard of its own is within `LockRadius` (XZ),
   `GuardHalfArcDeg` = 90 (`IsLocked`); else 60. A daze (15b) still forces 0. `WarlordGuardArc`
   publishes on each change.
5. **Phase beat:** `ClearAdds` despawns every tracked guard through `DespawnAtEndOfTick`, empties the
   roll-call and resets the war cry cooldown to `WarCryFirstAtSeconds` from the beat's end.
   `BossBehaviour` calls it after clearing its own adds; behaviours without the interface are unchanged.
6. **Reset** empties the roll-call without despawning. No per-tick allocation.

## Tests

| Test | Given / When / Then |
|---|---|
| `WarCry_BeforeFirstAt_NotArmed` / `WarCry_Ready_OutranksSweep` / `WarCry_TwoGuards_NotArmed` / `WarCry_FullRegistry_NotArmed` (1) |
| `WarCry_TelegraphsOneSecond_ThenSummonsTwo` / `WarCry_OneAlive_SummonsOne` (2) |
| `Summon_PaysNoXp` (2) |
| `Summon_PicksFarthestSpacedOpenGround` / `Summon_KeepsPlayerClearance` / `Summon_NoGround_RingAwayFromPlayer` (2) |
| `RollCall_DeadGuardLeaves` / `RollCall_StageMossguardIgnored` (3) |
| `Lock_GuardWithinRadius_Ninety` / `Lock_GuardAway_Sixty` / `Lock_Dazed_Zero` / `Lock_PublishesOnChange` (4) |
| `PhaseBeat_DespawnsGuards_ResetsCooldown` / `PhaseBeat_OtherBosses_Unchanged` (5) |
| `Reset_EmptiesRollCall` / `Tick_DoesNotAllocate` (6) |

## Out of scope

- Moving guards into formation (they keep `BulwarkBehaviour`); the lock's look (15f).
- Mother's pod Frogs surviving beats (Known issue 6) — `IBossAddOwner` is the fix's door, not here.

## As built

_Filled at merge._
