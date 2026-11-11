# SS-09b — The reflection: hero hits on a shining shell send glass bolts back

**Size:** M · **Depends on:** SS-09a · **Branch:** `ss-09b-glassback-reflect`
**Design refs:** GD §8.9, §16.4

## Goal

Hits from the hero's own weapon on a shining glassback each queue one glass bolt back at the hero, at most one per `BoltInterval`. The behaviour fires it as an `AtPlayer` shot that lands in a 0.8 m ring. Every other source is blocked with no bolt.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/EnemySystem.cs` | Core | `ApplyHeroDamage`: `ApplyDamage` plus the reflect check |
| `Core/Ai/ChaserBehaviour.cs` | Core | fires a queued bolt on its next tick (`SpitterBehaviour`'s door into `ctx.Projectiles`) |
| `Tests/Core/Ai/GlassbackReflectTests.cs` | Tests.Core | |
| *small edits* | | `EnemyAgent`: one pending-bolt slot; `EnemyEvents.cs`: `EnemyReflected`; the hero-weapon call sites switch to `ApplyHeroDamage`: `ProjectileSystem` (Ranger/Shade/Echo/Crossfire shots), `AxeSystem`, `PlayerCombat` (cone, charge) |

## Public API

```csharp
// EnemySystem
public DamageResult ApplyHeroDamage(int enemyId, float amount, float now, PlayerCombat player);
// public readonly struct EnemyReflected { int Id; Vector3 From; Vector3 Landing; float Seconds; }
```

## Behaviour

1. `ApplyHeroDamage` is `ApplyDamage` on every enemy that is not shining: same result, same events, bit for bit.
2. On a shining glassback (SS-09a's `IsShining`) the hit is `Blocked` as SS-09a built it. If `now` is at least `BoltInterval` past this shine's last bolt, it records one pending bolt aimed at the hero's position at that moment. A pending bolt not yet fired is not doubled.
3. Only the call sites in *Files* use `ApplyHeroDamage`. Pets, the hawk, poison, blasts, Wights and summons keep their own doors: blocked, no bolt.
4. On its next `Tick` the glassback fires the bolt as an `AtPlayer` `Projectile` from its body to the recorded landing. Speed `BoltSpeed`, with the flight never under `BoltMinFlight`. Radius `BoltRadius`, damage `BoltDamage` (depth-scaled as its bite is). Spec id is the glassback's. One `EnemyReflected` per bolt fired.
5. The bolt lands like any lob: the ring, i-frames and roll as built. A bolt in flight when the beetle dies still lands.
6. A shine yields at most `ceil(ShineSeconds / BoltInterval)` bolts (5 at the design's numbers), however fast the hero fires. Volley, ricochet and pierce hits count as hits.
7. A full pool (`NoProjectile`) drops the bolt silently, as `SpitterBehaviour` does.
8. `Reset()`, death and the end of the shine clear a pending bolt that has not been fired.
9. No allocation in `ApplyHeroDamage` or the fire path.

## Tests

| Test | Given / When / Then |
|---|---|
| `NotShining_HeroDamageIsApplyDamage` | scorpion, and glassback in Chase / both doors / identical results and events |
| `Shining_HeroHit_BlocksAndQueues` | shining / `ApplyHeroDamage` / `Blocked`; next tick one `AtPlayer` shot, one `EnemyReflected` |
| `Bolt_AimsAtHeroWhenHit` | hero moves after the hit / fire / landing = position at the hit |
| `Bolt_FlightNeverUnderMin` | hero 1 m away / fire / arrival − now ≥ 0.3 s |
| `Bolt_Numbers` | fire / read shot / speed 14, radius 0.8, damage 6 × depth |
| `Interval_CapsBolts` | 20 hits across one shine / count / ≤ 5 bolts, ≥ 0.4 s apart |
| `PetBite_Shining_NoBolt` | pet bites shining / tick / blocked, no shot |
| `Poison_Hawk_Blast_NoBolt` | each source / tick / no shot |
| `Arrow_ShiningShell_ReflectsThroughProjectileSystem` | Ranger arrow lands on it / tick / one bolt |
| `Axe_And_Swing_Reflect` | axe, cone, charge on it / tick / one bolt each, interval respected |
| `Bolt_LandsOnStillHero_MissesMovedHero` | still, then moved 1.5 m / land / damaged, then not |
| `PoolFull_DropsBolt` | projectile pool full / fire / no throw, no event |
| `Reset_ClearsPending` | queued, `Reset` / tick / no shot |
| `Reflect_NoAlloc` | shine, 20 hits, fire / `AllocationAssert.None` |

Every existing combat, projectile and axe test stays green (rule 1).

## Out of scope

- The bolt's mesh and the shell's glow (SS-09d). Data (SS-09e).
- Reflection for any other enemy. A bolt that homes.

## As built

_Commit body._
