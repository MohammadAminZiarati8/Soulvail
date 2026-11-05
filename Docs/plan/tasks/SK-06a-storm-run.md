# SK-06a — Storm Run in core

**Size:** M · **Depends on:** SK-04a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Storm Run); RS-08b (the Volley's one-arrow-per-enemy shape)

## Goal

The Skirmisher's strongest skill: for a few seconds every roll ends in a volley and the roll comes
back twice as fast.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/StormRun.cs` | Core | **New.** Effect + handler: starts the timed state |
| `Core/Combat/PlayerCombat.cs` | Core | On a roll's end during Storm Run: the free volley |
| `Core/Events/StormRunChanged.cs` | Core | **New.** `(bool Active, float Until)` |
| `Core/Effects/PlayerStat.cs` | Core | Append `StormRunSeconds` (base 6), `StormRunDamage` |
| `Tests/Core/Effects/StormRunTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession` (one `Register`), `CombatantStats`, stat coverage tests |

## Public API

```csharp
public sealed class StormRun : IEffect {
  public const int MaxArrows = 8;
  public StormRun(float damageOfWeapon); }
```

## Behaviour

1. A cast starts Storm Run for `StormRunSeconds`, publishes `StormRunChanged(true, until)` and
   posts `status.storm-run` (buff) with its timer.
2. **Faster roll:** while it lasts, `MovementSkillCooldown` is ×0.5 through one modifier held in
   `TimedEffects` until the end. Charges refilling (SK-04a) refill at the halved rate; a refill
   already running keeps its remaining time.
3. **Storm volley:** each roll that **ends** while Storm Run lasts fires one arrow at each enemy
   in `WeaponRange` with a clear line, nearest first, at most 8. Each arrow deals
   `damageOfWeapon × WeaponDamage × (1 + StormRunDamage)` with the ordinary multipliers, and
   publishes as a volley arrow (`isVolley` true). No enemy in range → no arrows.
4. The storm volley does not read, spend or reset the Ranger's own Volley, and works whether or
   not Volley is taken.
5. Storm volley hits count as player arrow hits (Hit and Run).
6. At the end, the modifier is released, `StormRunChanged(false, …)` publishes and the status
   clears. A stage boundary or death ends it the same way.
7. **Auto-cast:** `EnemiesInAcquireRange` at least 5. Cooldown 30 s.
8. **Ranks** (for SK-06b): 1 — Active, 0.8 × weapon, 6 s, 30 s; 2 — `StormRunSeconds` +2;
   3 — `StormRunDamage` +0.30.
9. Nothing allocates per tick or per volley.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cast_StartsAndPublishes_PostsStatus` (1) |
| `Active_RollCooldownHalved` / `RunningRefill_KeepsItsTime` (2) |
| `RollEnd_FiresOneArrowPerEnemy_CapEight` (3) |
| `RollEnd_NoEnemy_NoArrows` / `RollStart_FiresNothing` (3) |
| `StormVolley_LeavesOwnVolleyAlone` / `WorksWithoutVolleyTaken` (4) |
| `StormVolley_HitsStartBurst` (5) |
| `End_ReleasesAndClears` / `Boundary_Ends` (6) |
| `Rank2_EightSeconds` / `Rank3_ThirtyPercent` (8) |
| `Tick_DoesNotAllocate` (9) |

## Out of scope

- Assets, the wind aura (SK-06b).

## As built

_Filled at merge._
