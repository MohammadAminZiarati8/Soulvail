# SK-05a — Whirlwind in core

**Size:** M · **Depends on:** SK-01a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Whirlwind); Invariants §18.1 (where a pending ring ticks)

## Goal

An active that looses rings of arrows outward all round the Ranger, two or three rings 0.3 s
apart, without stopping it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Whirlwind.cs` | Core | **New.** Effect + handler; the handler holds pending rings (fixed 3) and is ticked |
| `Core/Events/WhirlwindCast.cs` | Core | **New.** `(Vector3 Origin, int Arrows, int Rings, float Gap)` |
| `Core/Effects/PlayerStat.cs` | Core | Append `WhirlwindArrows` (base 12), `WhirlwindRings` (base 2) |
| `Tests/Core/Effects/WhirlwindTests.cs` | Tests.Core | **New** |
| *small edits* | | `RunSession` (one `Register`; the handler's tick in the combat phase), `CombatantStats`, stat coverage tests |

## Public API

```csharp
public sealed class Whirlwind : IEffect {
  public const float RingGap = 0.3f;
  public Whirlwind(float damageOfWeapon); }
public sealed class WhirlwindHandler : IEffectHandler<Whirlwind> {
  public void Tick(float now, Vector3 playerPosition); }
```

## Behaviour

1. A cast publishes `WhirlwindCast` and fires ring 1 at once; ring *k* fires `(k−1) × 0.3 s`
   later **from the player's position at that moment**.
2. A ring is `WhirlwindArrows` arrows at even angles. Ring 1 starts at the facing direction; each
   later ring is turned by half a step.
3. Each arrow flies outward to `WeaponRange` like an ordinary arrow, hits the first enemy or cover
   in its way, and deals `damageOfWeapon × WeaponDamage` with the multipliers ordinary arrows get.
   Its hits count as player arrow hits (Hit and Run).
4. Whirlwind never stops movement, the roll or the basic shot.
5. A cast with no enemy near still fires (manual tap); no refusal.
6. **Auto-cast:** `EnemiesWithin6m` at least 3. Cooldown 10 s.
7. **Ranks** (for SK-05b): 1 — Active, 0.6 × weapon, 12 arrows, 2 rings, 10 s;
   2 — `WhirlwindArrows` +4; 3 — `WhirlwindRings` +1 and cooldown −2 s.
8. A stage boundary or the player's death drops pending rings. If the projectile pool is full,
   the arrows that do not fit are skipped. Nothing allocates per tick.

## Tests

| Test | Given / When / Then |
|---|---|
| `Cast_FiresFirstRingNow_SecondAfterGap` (1) |
| `LaterRing_FromCurrentPosition` | player moved 1 m / ring 2 origin moved (1) |
| `Ring_EvenAngles_SecondTurnedHalfStep` (2) |
| `Arrow_DamageAndHitAndRun` (3) |
| `Cast_DoesNotStopMovementOrShot` (4) |
| `Cast_NoEnemies_StillFires` (5) |
| `Rank2_SixteenArrows` / `Rank3_ThreeRings_EightSeconds` (7) |
| `Boundary_DropsPending` / `PoolFull_Skips` / `Tick_DoesNotAllocate` (8) |

## Out of scope

- Assets, spin animation, swirl VFX (SK-05b).

## As built

_Filled at merge._
