# MK-07a — Rain of Arrows in core

**Size:** M · **Depends on:** MK-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; AR §18.1 (zones), CC §6 (auto-cast triggers)

## Goal

An active that drops a 2 s rain of arrows on the densest group in range, damaging and, from rank 3,
slowing everything inside.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/RainOfArrows.cs` | Core | **New.** Effect + handler (`SpawnBurnZone`'s shape) |
| `Core/Combat/DensestPoint.cs` | Core | **New.** Picks the centre |
| `Core/Combat/EnemyPoint.cs` | Core | **New.** `readonly struct EnemyPoint(int Id, Vector3 Position, float Radius, bool IsBoss)` — filled from the registry; MK-10a reuses it |
| `Core/Events/RainOfArrowsCast.cs` | Core | **New.** `(Vector3 Centre, float Radius, float Duration)` |
| `Core/Combat/ZoneSystem.cs` | Core | A zone may slow what it burns |
| `Tests/Core/Effects/RainOfArrowsTests.cs` | Tests.Core | **New** |
| *small edits* | | `PlayerStat` (append `RainDamage`, `RainRadius`, `RainSlow`), `CombatantStats`, `RunSession` (one `Register` line), `Docs/Invariants.md` (zones may spawn at a point — amend the "zones spawn at `PlayerPosition`" line) |

## Public API

```csharp
public sealed class RainOfArrows : IEffect {
  RainOfArrows(float radius, float duration, float pulseInterval, float damageOfWeapon); }
public static class DensestPoint {   // pure function, no state
  bool TryPick(ReadOnlySpan<EnemyPoint> enemies, Vector3 player, float range, float radius,
               out Vector3 centre); }
```

## Behaviour

1. **Centre:** among living enemies within `WeaponRange` (XZ), the one with the most other enemies
   within `radius` of it; ties → nearest the player, then lowest id. No enemy in range → the cast
   is refused and the cooldown is not spent.
2. The handler spawns a burning zone **at the centre** (`ZoneSystem.Spawn(…, ZoneSide.BurnsEnemies,
   at: centre)`): radius = base `radius` + `RainRadius`; `duration` 2 s; a pulse every 0.25 s
   dealing `damageOfWeapon × WeaponDamage × (1 + RainDamage)` to each enemy inside.
3. **Slow:** when `RainSlow` > 0, each pulse slows every enemy inside by `RainSlow` for 0.35 s
   (`EnemySystem.Slow`), so it ends shortly after leaving the circle.
4. `RainOfArrowsCast` publishes once per cast with the final centre, radius and duration.
5. **Auto-cast trigger:** `EnemiesInAcquireRange` at least 3. Cooldown 10 s.
6. Ranks (for MK-07b's assets): 1 — Active, radius 1.5, 0.6 × weapon per pulse, 10 s; 2 —
   `RainDamage` +0.25; 3 — `RainRadius` +0.5, `RainSlow` +0.30; 4 — cooldown −2 s
   (`ModifySkillCooldown`).
7. The zone stays where it fell; it does not follow the player or the group.
8. Nothing allocates per tick; `DensestPoint` is O(n²) over at most the registry's living enemies.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pick_DensestGroupWins` | 3 clustered + 1 nearer alone / the cluster (1) |
| `Pick_TieBreaks` | (1) |
| `Pick_NoneInRange_Refused_CooldownKept` (1) |
| `Zone_AtCentre_DamagesInside` | 8 pulses × expected damage; outside untouched (2) |
| `Rank2_Damage` / `Rank3_WiderAndSlows` (2, 3, 6) |
| `Slow_EndsAfterLeaving` (3) |
| `Cast_PublishesOnce` (4) |
| `Zone_DoesNotFollow` (7) |
| `Tick_DoesNotAllocate` (8) |

## Out of scope

- Assets, VFX, animation (MK-07b).

## As built

_Filled at merge._
