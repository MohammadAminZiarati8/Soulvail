# MK-08a — Binding Shot in core

**Size:** M · **Depends on:** MK-06a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; AR §18.2 (core holds no walls; Unity reports facts), §18.1 (projectiles)

## Goal

An arrow that, on hitting, chains its target to an enemy, stone, tree or wall behind it — both
stunned long — or stuns it briefly when nothing is behind.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/BindingShot.cs` | Core | **New.** Effect + handler: picks the target, looses the arrow |
| `Core/Combat/BindResolver.cs` | Core | **New.** Enemy anchor search, probe bookkeeping, the stuns |
| `Core/Events/BindingShotBound.cs` | Core | **New.** `(int TargetId, BindAnchor Anchor, int AnchorEnemyId, Vector3 AnchorPoint, float Seconds)` |
| `Tests/Core/Combat/BindResolverTests.cs` | Tests.Core | **New** |
| *small edits* | | `IIntentSink` (`BindProbe(in BindProbeIntent)`), `IRunSession` (`ReportBindAnchor`), `ProjectileSystem` (a projectile may carry a bind tag and report its hit), `PlayerStat` (append `BindStun`), `CombatantStats`, `RunSession` (register, order), fakes |

## Public API

```csharp
public enum BindAnchor { None, Enemy, Obstacle }
public readonly struct BindProbeIntent { int TargetId; Vector3 From; Vector3 Direction; float Reach; }
// IRunSession: void ReportBindAnchor(int targetId, bool hit, Vector3 point);
public sealed class BindingShot : IEffect {
  BindingShot(float reach, float coneHalfAngleDeg, float boundSeconds, float aloneSeconds,
              float damageOfWeapon); }
```

## Behaviour

1. **Target:** the current target if in `WeaponRange`, else the nearest enemy in range; none →
   refused, cooldown kept. The arrow is a projectile (the weapon's speed) dealing
   `damageOfWeapon × WeaponDamage`.
2. **On hit,** direction = player → target at the hit (XZ). **Enemy anchor first:** the nearest other
   living enemy within `reach` of the target inside a ±`coneHalfAngleDeg` cone along that direction.
3. **No enemy anchor:** core emits `BindProbe` (from the target, along the direction, `reach`) that
   tick; Unity answers through `ReportBindAnchor` in the facts phase; the next tick resolves —
   `hit` → `Obstacle` at `point`, else `None`. No answer within 2 ticks → `None`.
4. **Stuns:** Enemy/Obstacle → target (and the anchor enemy) stunned `boundSeconds + BindStun`;
   None → target stunned `aloneSeconds`. Bosses half (MK-06a rule 1). A dead target by resolve
   time → nothing.
5. `BindingShotBound` publishes once per resolve, carrying what was found.
6. **Auto-cast:** `EnemiesInAcquireRange` ≥ 1; cooldown 12 s.
7. Ranks (MK-08b's assets): 1 — Active, reach 4 m, cone 20°, bound 2.0 s, alone 0.75 s, 2 × weapon;
   2 — `BindStun` +0.4; 3 — `BindStun` +0.6; 4 — cooldown −3 s.
8. Order: the bind resolves in the projectiles' step after hits; a pending probe never outlives a
   stage boundary. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Target_CurrentElseNearest` / `NoTarget_Refused_CooldownKept` (1) |
| `EnemyBehind_InCone_Binds` / `EnemyBeside_OutOfCone_Ignored` (2) |
| `NoEnemy_EmitsProbe` (3) |
| `ProbeHit_BindsToObstacle` / `ProbeMiss_Alone` / `NoAnswer_Alone` (3) |
| `Bound_BothStunned` / `Alone_ShortStun` / `Boss_Half` (4) |
| `DeadTarget_Nothing` (4) |
| `Bound_PublishesOnce` (5) |
| `Rank_StunAddsUp` (7) |
| `Boundary_DropsPendingProbe` / `Tick_DoesNotAllocate` (8) |

## Out of scope

- The Unity probe, chain visual, assets (MK-08b).

## As built

_Filled at merge._
