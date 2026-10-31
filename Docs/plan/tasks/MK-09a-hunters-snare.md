# MK-09a — Hunter's Snare in core

**Size:** M · **Depends on:** MK-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; CC §6 (triggers); AR §18.1 (stage boundary)

## Goal

When an enemy closes in, the Ranger drops a trap at their feet; the first enemy to step on it is
rooted and hurt.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/TrapSystem.cs` | Core | **New.** Traps: place, arm, spring, expire |
| `Core/Effects/PlaceSnare.cs` | Core | **New.** Effect + handler |
| `Core/Events/TrapEvents.cs` | Core | **New.** `TrapPlaced(int Id, Vector3 At)`, `TrapSprung(int Id, int EnemyId)`, `TrapExpired(int Id)` (three structs, one file — the precedent is the event files that already do) |
| `Tests/Core/Combat/TrapSystemTests.cs` | Tests.Core | **New** |
| *small edits* | | `TriggerSpec`'s `TriggerField` (append `NearestEnemyDistance`, wired to the blackboard), `PlayerStat` (append `SnareRoot`, `SnareDamage`), `CombatantStats`, `RunSession` (construct, tick after zones, boundary clear, register) |

## Public API

```csharp
public sealed class TrapSystem { public const int Capacity = 2;
  int Place(Vector3 at, float rootSeconds, float damage, float lifetime, float now);
  void Tick(float now); void Clear(); int Count { get; } }
public sealed class PlaceSnare : IEffect {
  PlaceSnare(float radius, float armDelay, float lifetime, float rootSeconds, float damageOfWeapon); }
```

## Behaviour

1. The trap is placed at the player's position at cast; it **arms** after 0.3 s.
2. An armed trap springs on the **first** living enemy whose position comes within its radius
   (0.8 m + the enemy's radius, XZ): root `rootSeconds + SnareRoot` (bosses half), damage
   `damageOfWeapon × WeaponDamage × (1 + SnareDamage)`; the trap is consumed.
3. Unsprung, it expires after `lifetime` 10 s.
4. At most `Capacity` traps stand; a third replaces the oldest (it expires, with its event).
5. **Auto-cast:** `NearestEnemyDistance` below 3 m; cooldown 10 s.
6. Ranks (MK-09b's assets): 1 — Active, root 2.0 s, 3 × weapon; 2 — `SnareRoot` +0.5,
   `SnareDamage` +0.33; 3 — `SnareRoot` +0.5, cooldown −2 s.
7. A stage boundary clears every trap silently (views tear down on `StageArrived`). Nothing
   allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Place_AtPlayer_ArmsAfterDelay` (1) |
| `Spring_FirstEnemyOnly_RootsAndHurts` / `Spring_Boss_Half` (2) |
| `Expire_AfterLifetime` (3) |
| `Third_ReplacesOldest` (4) |
| `Trigger_NearestEnemyBelow3m` (5) |
| `Ranks_AddUp` (6) |
| `Boundary_Clears` / `Tick_DoesNotAllocate` (7) |

## Out of scope

- The trap's look, animations, assets (MK-09b).

## As built

_Filled at merge._
