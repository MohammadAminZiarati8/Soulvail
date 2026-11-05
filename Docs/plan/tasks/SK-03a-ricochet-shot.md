# SK-03a — Ricochet Shot in core

**Size:** M · **Depends on:** SK-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Ricochet Shot); FX-03 (cover blocks aim); CC §6 (auto-cast triggers)

## Goal

An active that looses one arrow which bounces from enemy to enemy, hitting up to 3–5 of them.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/RicochetShot.cs` | Core | **New.** Effect + handler + cast guard (`BindingShot`'s shape) |
| `Core/Combat/ProjectileSystem.cs` | Core | A projectile may carry a chain: hits left, reach, ids already hit (fixed 5) |
| `Core/Effects/PlayerStat.cs` | Core | Append `RicochetHits` (base 3), `RicochetDamage` |
| `Tests/Core/Effects/RicochetShotTests.cs` | Tests.Core | **New** |
| *small edits* | | `ProjectileEvents` (`ProjectileFired` gains `IsRicochet`, `ChainIndex`), `CombatantStats`, `RunSession` (one `Register`), stat coverage tests |

## Public API

```csharp
public sealed class RicochetShot : IEffect {
  public const float BounceReach = 5f;
  public RicochetShot(float damageOfWeapon); }
public sealed class RicochetShotHandler : IEffectHandler<RicochetShot>, IEffectCastGuard<RicochetShot> {
  public RicochetShotHandler(ProjectileSystem projectiles, EnemySystem enemies, PlayerStats stats /* + what BindingShotHandler takes */); }
```

## Behaviour

1. **First target:** the player's current target if it is in `WeaponRange` with a clear line
   (FX-03's test); else the nearest living enemy in range with a clear line; ties → lowest id.
   None → `CanCast` false and the cooldown is not spent.
2. The first arrow flies from the player like an ordinary arrow. Each hit deals
   `damageOfWeapon × WeaponDamage × (1 + RicochetDamage)`, with the multipliers ordinary arrows get
   (level growth, Momentum).
3. **Bounce:** when a chain arrow damages an enemy and hits remain, a new arrow leaves the impact
   point for the nearest living enemy within 5 m (XZ) that this chain has not hit, with a clear
   line; ties → lowest id. None → the chain ends. Total hits = `RicochetHits`.
4. An arrow that misses, hits cover or is blocked (a frontal guard, a phase shield) ends the chain.
5. Every chain hit counts as a player arrow hit (Hit and Run's burst, kill credit, XP).
6. Each chain arrow publishes `ProjectileFired` with `IsRicochet` true and its `ChainIndex`
   (0 = first). Other shots publish `IsRicochet` false.
7. **Auto-cast:** `EnemiesInAcquireRange` at least 3. Cooldown 8 s.
8. **Ranks** (for SK-03b): 1 — Active, 1.0 × weapon, 3 hits, 8 s; 2 — `RicochetHits` +1;
   3 — `RicochetDamage` +0.25; 4 — `RicochetHits` +1 and cooldown −2 s (`ModifySkillCooldown`).
9. A stage boundary clears chains in flight. Nothing allocates per tick or per bounce.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cast_PicksTarget_ThenNearestClear` / `Cast_NoneInRange_Refused_CooldownKept` (1) |
| `Hit_DamageMatchesFormula` (2) |
| `Bounce_ToNearestUnhit_WithinFive` | 4 enemies, one at 6 m / that one never hit (3) |
| `Bounce_NeverRepeatsAnEnemy` | 2 enemies, 3 hits / 2 hits, chain ends (3) |
| `Bounce_CoverBlocks` (3) |
| `Chain_EndsOnMissOrGuard` (4) |
| `ChainHit_StartsHitAndRunBurst` (5) |
| `Fired_FlagsAndIndex` (6) |
| `Rank2_FourHits` / `Rank4_FiveHits_SixSeconds` (8) |
| `StageBoundary_ClearsChains` / `Bounce_DoesNotAllocate` (9) |

## Out of scope

- Assets, animation, the bounce spark (SK-03b).

## As built

_Filled at merge._
