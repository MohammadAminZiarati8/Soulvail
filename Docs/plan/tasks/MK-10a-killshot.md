# MK-10a — Killshot in core

**Size:** M · **Depends on:** MK-07a (`EnemyPoint`) · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; AR §18.1 (`PlayerMotor.Tick`, death drain)

## Goal

The Marksman's strongest skill: the Ranger kneels and draws for 0.7 s, then one arrow crosses the
whole arena down the most crowded line, weakening per enemy and killing the badly wounded outright.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Killshot.cs` | Core | **New.** Effect + handler; starts the wind-up |
| `Core/Combat/KillshotLine.cs` | Core | **New.** Picks the direction; walks the line |
| `Core/Events/KillshotEvents.cs` | Core | **New.** `KillshotWindUp(Vector3 Direction, float Seconds)`, `KillshotFired(Vector3 Origin, Vector3 Direction, float Length, int Hits)`, `KillshotExecuted(int EnemyId)` |
| `Tests/Core/Combat/KillshotTests.cs` | Tests.Core | **New** |
| *small edits* | | `PlayerMotor` (`Hold(float seconds)`: no movement, no roll while held), `PlayerCombat` (no ordinary shots while held), `PlayerStat` (append `KillshotExecute`, `KillshotDamage`), `CombatantStats`, `RunSession` (register; tick the wind-up after skills) |

## Public API

```csharp
public sealed class Killshot : IEffect {
  Killshot(float windUp, float halfWidth, float length, float damageOfWeapon,
           float falloffPerHit, float minFactor, float executeBelow); }
public static class KillshotLine {
  bool TryPick(ReadOnlySpan<EnemyPoint> enemies, Vector3 player, float halfWidth, float length,
               out Vector3 direction); }
```

## Behaviour

1. **Direction** (at cast): for each living enemy within `length`, the line from the player through
   it; the one containing the most enemies within `halfWidth` wins; ties → the nearer first enemy,
   then lowest id. No enemy → refused, cooldown kept. The direction is **locked** for the shot.
2. **Wind-up:** `KillshotWindUp` publishes; the player is held (`PlayerMotor.Hold`) and fires no
   ordinary arrows for `windUp` 0.7 s; taking damage does **not** cancel it.
3. **Fire:** after the wind-up, every living enemy within `halfWidth` of the line and `length` along
   it is hit in order of distance. Hit *k* (0-based) deals
   `damageOfWeapon × (1 + KillshotDamage) × WeaponDamage × max(minFactor, 1 − falloffPerHit × k)`.
   The line ignores stones and walls (it crosses the arena).
4. **Execute:** before its hit, a **non-boss** at or below `executeBelow + KillshotExecute` of its
   max HP dies outright (`KillshotExecuted`, then the normal death path — XP, drops). Bosses only
   take the damage.
5. `KillshotFired` publishes once with the number of enemies hit.
6. **Auto-cast:** `EnemiesInAcquireRange` ≥ 3; cooldown 30 s.
7. Ranks (MK-10b's assets): 1 — Active, wind-up 0.7, half-width 0.5 m, length 30 m, 8 × weapon,
   falloff 0.15, min 0.4, execute 0.15; 2 — `KillshotExecute` +0.05, `KillshotDamage` +0.25;
   3 — `KillshotExecute` +0.05, cooldown −8 s.
8. Dying during the wind-up cancels it silently. A stage boundary cancels it. No allocation per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Pick_MostCrowdedLine` / `Pick_TieBreaks` / `NoEnemy_Refused` (1) |
| `Direction_LockedAtCast` | the group moves / shot goes down the old line (1) |
| `WindUp_HoldsPlayer_NoShots` / `WindUp_DamageDoesNotCancel` (2) |
| `Fire_FalloffPerHit_FloorsAtMin` (3) |
| `Fire_IgnoresWalls` (3) |
| `Execute_NonBossBelowThreshold` / `Execute_NeverBoss` (4) |
| `Fired_PublishesOnce` (5) |
| `Rank_ExecuteRises` (7) |
| `Death_CancelsWindUp` / `Boundary_Cancels` / `Tick_DoesNotAllocate` (8) |

## Out of scope

- The kneel, streak, jolt, assets (MK-10b).

## As built

_Filled at merge._
