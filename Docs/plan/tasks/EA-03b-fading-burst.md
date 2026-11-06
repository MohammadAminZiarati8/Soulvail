# EA-03b — Fading Burst in core

**Size:** S · **Depends on:** EA-01a, MK-06a (slow) · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Fading Burst); GD §16.5 (slow, bosses half)

## Goal

A shade that fades bursts, hurting every enemy within 3 m; at rank 3 the burst also slows.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ShadeSystem.cs` | Core | Burst on expiry; `Tick` gains the `PlayerCombat` that `ApplyDamage` needs |
| `Core/Events/ShadeEvents.cs` | Core | Add `ShadeBurst` |
| `Core/Effects/PlayerStat.cs` | Core | Append `FadeBurst` (base 0), `FadeBurstSlow` (base 0) |
| `Tests/Core/Combat/FadingBurstTests.cs` | Tests.Core | **New** |
| *small edits* | | `CombatantStats`, `RunSession` (the widened `Tick` call), `ShadeSystemTests` (the call), stat coverage tests |

## Public API

```csharp
public const float BurstRadius = 3f, BurstSlowSeconds = 2f;   // on ShadeSystem
public void Tick(float now, EnemySystem enemies, PlayerCombat player);
// ShadeBurst(Position, Radius, bool Slows)
```

## Behaviour

1. With `FadeBurst` > 0, a shade of **any** kind that leaves with `Expired: true` bursts at its
   position on that tick: every living enemy within `BurstRadius` (XZ, edge of its body radius)
   takes `FadeBurst × WeaponDamage` through `EnemySystem.ApplyDamage`, so guards, phase shields,
   kills and XP behave as for any other player hit.
2. A shade replaced early or cleared (`Expired: false`) never bursts.
3. With `FadeBurstSlow` > 0, each enemy hit is also slowed by `FadeBurstSlow` for
   `BurstSlowSeconds` through `EnemySystem.Slow` (bosses at half, as MK-06a rules).
4. Each burst publishes `ShadeBurst` after its `ShadeFaded`, with `Slows` = `FadeBurstSlow` > 0;
   a burst with no enemy in reach still publishes.
5. **Ranks** (for EA-03c): 1 — `FadeBurst` Flat +1.5; 2 — +0.5; 3 — +0.5 and `FadeBurstSlow` +0.4.
6. No allocation per burst.

## Tests

| Test | Given / When / Then |
|---|---|
| `Expired_Bursts_EnemiesInThreeMetres` / `Enemy_AtFourMetres_Untouched` (1) |
| `Burst_Kills_PayXp` / `Burst_GuardedEnemy_AsAnyPlayerHit` (1) |
| `Replaced_NoBurst` / `Cleared_NoBurst` (2) |
| `Rank3_Slows_FortyPercent_TwoSeconds` / `Rank3_Boss_HalfSlow` (3) |
| `Burst_PublishedAfterFaded_EvenWithNoEnemy` (4) |
| `Rank2_TwoTimesWeapon` (5) |
| `Burst_DoesNotAllocate` (6) |

## Out of scope

- The burst's look and the assets (EA-03c).

## As built

_Filled at merge._
